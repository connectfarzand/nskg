using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Nskg.Controllers
{
    [Authorize(Roles = "Admin")]
    public class BackupController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _env;

        public BackupController(IConfiguration configuration, IWebHostEnvironment env)
        {
            _configuration = configuration;
            _env = env;
        }

        public IActionResult Index()
        {
            var backupsDir = Path.Combine(_env.ContentRootPath, "Backups");
            if (!Directory.Exists(backupsDir))
            {
                Directory.CreateDirectory(backupsDir);
            }

            var dirInfo = new DirectoryInfo(backupsDir);
            var files = dirInfo.GetFiles("*.bak")
                               .OrderByDescending(f => f.CreationTime)
                               .ToList();

            ViewBag.DatabaseName = GetDatabaseName();
            ViewBag.TotalBackups = files.Count;
            long totalBytes = files.Sum(f => f.Length);
            ViewBag.TotalSpaceUsed = FormatFileSize(totalBytes);

            return View(files);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateBackup()
        {
            try
            {
                string dbName = GetDatabaseName();
                string connStr = _configuration.GetConnectionString("DefaultConnection")!;

                var backupsDir = Path.Combine(_env.ContentRootPath, "Backups");
                if (!Directory.Exists(backupsDir))
                {
                    Directory.CreateDirectory(backupsDir);
                }

                EnsureDirectoryPermissions(backupsDir);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = $"{dbName}_Backup_{timestamp}.bak";
                string fullPath = Path.Combine(backupsDir, fileName);

                using (var conn = new SqlConnection(connStr))
                {
                    await conn.OpenAsync();
                    bool backupSuccess = false;
                    bool isWindows = OperatingSystem.IsWindows();

                    if (isWindows)
                    {
                        // On Windows: Fast compressed backup directly to application Backups directory
                        string sql = $"BACKUP DATABASE [{dbName}] TO DISK = @path WITH INIT, COPY_ONLY, COMPRESSION, CHECKSUM;";
                        using (var cmd = new SqlCommand(sql, conn))
                        {
                            cmd.CommandTimeout = 300;
                            cmd.Parameters.AddWithValue("@path", fullPath);
                            await cmd.ExecuteNonQueryAsync();
                        }
                        backupSuccess = System.IO.File.Exists(fullPath);
                    }
                    else
                    {
                        // On Linux (where SQL Server runs in Docker container 'sqlserver'):
                        // 1. Perform high-speed COMPRESSED backup in container default data path
                        string containerBackupPath = $"/var/opt/mssql/data/{fileName}";
                        string sql = $"BACKUP DATABASE [{dbName}] TO DISK = @path WITH INIT, COPY_ONLY, COMPRESSION, CHECKSUM;";
                        using (var cmd = new SqlCommand(sql, conn))
                        {
                            cmd.CommandTimeout = 300;
                            cmd.Parameters.AddWithValue("@path", containerBackupPath);
                            await cmd.ExecuteNonQueryAsync();
                        }

                        // 2. Transfer file from Docker container 'sqlserver' to host Backups folder
                        try
                        {
                            var copyProcess = Process.Start(new ProcessStartInfo
                            {
                                FileName = "docker",
                                Arguments = $"cp sqlserver:{containerBackupPath} \"{fullPath}\"",
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                UseShellExecute = false
                            });

                            if (copyProcess != null)
                            {
                                await copyProcess.WaitForExitAsync();
                                if (copyProcess.ExitCode == 0 && System.IO.File.Exists(fullPath))
                                {
                                    backupSuccess = true;
                                    try
                                    {
                                        System.IO.File.SetUnixFileMode(fullPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
                                    }
                                    catch { }

                                    // Remove temp backup inside container to prevent container disk bloat
                                    var rmProcess = Process.Start(new ProcessStartInfo
                                    {
                                        FileName = "docker",
                                        Arguments = $"exec sqlserver rm -f {containerBackupPath}",
                                        UseShellExecute = false
                                    });
                                    rmProcess?.WaitForExit();
                                }
                            }
                        }
                        catch
                        {
                            // Fallback if docker command cannot be called directly
                            if (System.IO.File.Exists(containerBackupPath))
                            {
                                System.IO.File.Copy(containerBackupPath, fullPath, true);
                                try { System.IO.File.Delete(containerBackupPath); } catch { }
                                backupSuccess = true;
                            }
                        }
                    }

                    if (backupSuccess)
                    {
                        // Auto-cleanup: keep only latest 10 backups to preserve server disk space
                        CleanOldBackups(backupsDir, 10);

                        long sizeBytes = new FileInfo(fullPath).Length;
                        string formatted = FormatFileSize(sizeBytes);
                        TempData["SuccessMessage"] = $"Optimized backup created successfully: {fileName} ({formatted})";
                    }
                    else
                    {
                        TempData["ErrorMessage"] = "Backup process completed in SQL Server, but the file could not be transferred to web storage.";
                    }
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Failed to create backup: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        private string GetDatabaseName()
        {
            try
            {
                string connStr = _configuration.GetConnectionString("DefaultConnection")!;
                var builder = new SqlConnectionStringBuilder(connStr);
                return string.IsNullOrEmpty(builder.InitialCatalog) ? "nskg_live" : builder.InitialCatalog;
            }
            catch
            {
                return "nskg_live";
            }
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes >= 1024 * 1024 * 1024)
                return $"{(bytes / (1024.0 * 1024.0 * 1024.0)):F2} GB";
            if (bytes >= 1024 * 1024)
                return $"{(bytes / (1024.0 * 1024.0)):F2} MB";
            if (bytes >= 1024)
                return $"{(bytes / 1024.0):F2} KB";
            return $"{bytes} B";
        }

        private void CleanOldBackups(string backupsDir, int keepCount)
        {
            try
            {
                var dirInfo = new DirectoryInfo(backupsDir);
                var oldFiles = dirInfo.GetFiles("*.bak")
                                      .OrderByDescending(f => f.CreationTime)
                                      .Skip(keepCount)
                                      .ToList();

                foreach (var old in oldFiles)
                {
                    try { old.Delete(); } catch { }
                }
            }
            catch { }
        }

        private void EnsureDirectoryPermissions(string path)
        {
            try
            {
                if (!OperatingSystem.IsWindows()) return;
                var dirInfo = new DirectoryInfo(path);
                var dirSecurity = dirInfo.GetAccessControl();
                dirSecurity.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                    FileSystemRights.Modify,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));
                dirInfo.SetAccessControl(dirSecurity);
            }
            catch
            {
                // Ignore if process lacks privilege to set ACL
            }
        }

        [HttpGet]
        public IActionResult Download(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return BadRequest("Invalid file name.");
            }

            var safeFileName = Path.GetFileName(fileName);
            var backupsDir = Path.Combine(_env.ContentRootPath, "Backups");
            var fullPath = Path.Combine(backupsDir, safeFileName);

            if (!System.IO.File.Exists(fullPath))
            {
                return NotFound("Backup file not found.");
            }

            return PhysicalFile(fullPath, "application/octet-stream", safeFileName);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Delete(string fileName)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    var safeFileName = Path.GetFileName(fileName);
                    var backupsDir = Path.Combine(_env.ContentRootPath, "Backups");
                    var fullPath = Path.Combine(backupsDir, safeFileName);

                    if (System.IO.File.Exists(fullPath))
                    {
                        System.IO.File.Delete(fullPath);
                        TempData["SuccessMessage"] = $"Backup file {safeFileName} deleted.";
                    }
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error deleting file: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }
    }
}
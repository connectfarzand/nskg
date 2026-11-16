using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Reporting.NETCore;
using System.Data;
using System.IO;
using System.Reflection.Emit;
using System.Text;
using Nskg.Data; // <-- added for ApplicationDbContext

namespace Nskg.Controllers  
{
    [Authorize(Roles = "Admin")]
    public class GenerlLedgerReportController : Controller
    {
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _env;
        private readonly ApplicationDbContext _context; // <-- added

        public GenerlLedgerReportController(IConfiguration config, IWebHostEnvironment env, ApplicationDbContext context)
        {
            _config = config;
            _env = env;
            _context = context; // <-- assign

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        }
        private int GetCompanyId(int? companyId = null)
        {
            if (companyId.HasValue && companyId.Value > 0) return companyId.Value;
            var compId = User.FindFirst("CompanyId")?.Value;
            if (int.TryParse(compId, out int id) && id > 0) return id;
            return 1006;
        }

        private int GetFinancialYearId()
        {
            var fyId = User.FindFirst("FinancialYearId")?.Value;
            if (int.TryParse(fyId, out int id) && id > 0) return id;
            return 4;
        }

        [HttpGet]
        public IActionResult GetAccountsByCompany(int companyId)
        {
            var accounts = _context.GLChart3
                .Where(x => x.CompanyId == companyId || x.CompanyId == 0 || x.CompanyId == null)
                .Select(x => new
                {
                    Code = x.ACC ?? (x.AC1 + x.AC3),
                    Name = x.Name
                })
                .OrderBy(x => x.Code)
                .ToList();

            return Json(accounts);
        }

        [HttpGet]
        public IActionResult Index(string? accode, string? fromDate, string? toDate, int? companyId)
        {
            int selectedCompanyId = GetCompanyId(companyId);

            var companies = _context.Companies
                .Where(c => !c.IsDeleted)
                .OrderBy(c => c.Cocode)
                .Select(c => new
                {
                    Id = c.Id,
                    Code = c.Cocode,
                    Name = (!string.IsNullOrEmpty(c.Cocode) ? c.Cocode + " - " : "") + c.Name
                })
                .ToList();

            var accounts = _context.GLChart3
                .Where(x => x.CompanyId == selectedCompanyId || x.CompanyId == 0 || x.CompanyId == null)
                .Select(x => new
                {
                    Code = x.ACC ?? (x.AC1 + x.AC3),
                    Name = x.Name
                })
                .OrderBy(x => x.Code)
                .ToList();

            ViewBag.CompanyList = companies;
            ViewBag.SelectedCompanyId = selectedCompanyId;
            ViewBag.AccountList = accounts;
            ViewBag.Accode = accode;
            ViewBag.FromDate = fromDate;
            ViewBag.ToDate = toDate;

            return View();
        }

        private DataTable GetGeneralLedger(string accode, DateTime fromDate, DateTime toDate, int companyId, int financialYearId)
        {
            DataTable dt = new DataTable();
            string connString = _config.GetConnectionString("DefaultConnection");

            using (SqlConnection con = new SqlConnection(connString))
            {
                using (SqlCommand cmd = new SqlCommand("sp_GeneralLedger", con))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    // Parameters
                    cmd.Parameters.AddWithValue("@Accode", accode);
                    cmd.Parameters.AddWithValue("@FromDate", fromDate);
                    cmd.Parameters.AddWithValue("@ToDate", toDate);
                    cmd.Parameters.AddWithValue("@CompanyId", companyId);
                    cmd.Parameters.AddWithValue("@FinancialYearId", financialYearId);

                    con.Open();

                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        dt.Load(reader); // Load data into DataTable
                    }
                }
            }

            return dt;
        }

        [HttpGet]
        private string BuildGeneralLedgerHtml(DataTable dt, string accode, DateTime fromDate, DateTime toDate, bool isPrintMode = false, bool isForPdf = false)
        {
            string compName = (dt.Columns.Contains("CompanyName") && dt.Rows.Count > 0 ? dt.Rows[0]["CompanyName"]?.ToString() : null) ?? "Company";
            string accName = (dt.Columns.Contains("AccName") && dt.Rows.Count > 0 ? dt.Rows[0]["AccName"]?.ToString() : null) ?? "";

            var sb = new StringBuilder();
            sb.Append(@"<!DOCTYPE html><html><head><meta charset='utf-8'>
            <title>General Ledger - " + accode + @"</title>
            <style>
                body{font-family:Arial,sans-serif;font-size:12px;margin:12px;color:#333;}
                .header-box{text-align:center;margin-bottom:12px;}
                .header-box h2{margin:0 0 4px;color:#0d6efd;font-size:18px;}
                .header-box h3{margin:0 0 4px;font-size:14px;color:#495057;}
                .header-box p{margin:0;font-size:11px;color:#6c757d;}
                table{width:100%;border-collapse:collapse;margin-top:8px;}
                th{background:#0d6efd;color:#fff;padding:7px 6px;text-align:left;font-size:11px;border:1px solid #0b5ed7;}
                td{padding:5px 6px;border:1px solid #dee2e6;font-size:11px;}
                tr:nth-child(even){background:#f8f9fa;}
                tr:hover{background:#e9ecef;}
                .num{text-align:right;}
                .center{text-align:center;}
                .bold{font-weight:bold;}
                tfoot tr{background:#d0e2ff;font-weight:bold;}
                @media print {
                    .no-print { display: none !important; }
                    body { margin: 5mm; }
                }
            </style>");

            if (!isForPdf)
            {
                sb.Append("<style>" + Nskg.Helpers.ReportPaginationHelper.GetPaginationStyles() + "</style>");
            }

            if (isPrintMode)
            {
                sb.Append("<script>window.addEventListener('load', function() { setTimeout(function() { window.print(); }, 300); });</script>");
            }

            sb.Append("</head><body>");

            if (!isForPdf && !isPrintMode)
            {
                sb.Append(Nskg.Helpers.ReportPaginationHelper.GetPaginationToolbarHtml("General Ledger"));
            }

            sb.Append($"<div class='header-box'>");
            sb.Append($"<h2>{compName}</h2>");
            sb.Append($"<h3>GENERAL LEDGER REPORT</h3>");
            sb.Append($"<div style='display:flex; justify-content:space-between; align-items:center; margin:8px 0 4px 0; padding-bottom:4px; border-bottom:1.5px solid #000;'>");
            sb.Append($"<div style='font-size:15px; font-weight:bold;'><span style='letter-spacing:1px;'>{accode}</span> &nbsp;&nbsp;&nbsp;&nbsp; <span style='color:#0d6efd;'>{accName}</span></div>");
            sb.Append($"<div style='font-size:11px; color:#495057;'>Period: <b>{fromDate:dd-MMM-yyyy}</b> to <b>{toDate:dd-MMM-yyyy}</b></div>");
            sb.Append($"</div>");
            sb.Append("</div>");

            sb.Append("<table><thead><tr>");
            sb.Append("<th style='width:35px;' class='center'>#</th>");
            foreach (DataColumn col in dt.Columns)
            {
                if (col.ColumnName.Equals("CompanyName", StringComparison.OrdinalIgnoreCase) ||
                    col.ColumnName.Equals("AccName", StringComparison.OrdinalIgnoreCase) ||
                    col.ColumnName.Equals("Accode", StringComparison.OrdinalIgnoreCase) ||
                    col.ColumnName.Equals("SortOrder", StringComparison.OrdinalIgnoreCase) ||
                    col.ColumnName.Equals("RowNum", StringComparison.OrdinalIgnoreCase))
                    continue;

                bool isNum = col.DataType == typeof(decimal) || col.DataType == typeof(double) || col.DataType == typeof(int);
                sb.Append($"<th {(isNum ? "class='num'" : "")}>{col.ColumnName}</th>");
            }
            sb.Append("</tr></thead><tbody>");

            int sr = 1;
            foreach (DataRow row in dt.Rows)
            {
                sb.Append("<tr>");
                sb.Append($"<td class='center'>{sr++}</td>");
                foreach (DataColumn col in dt.Columns)
                {
                    if (col.ColumnName.Equals("CompanyName", StringComparison.OrdinalIgnoreCase) ||
                        col.ColumnName.Equals("AccName", StringComparison.OrdinalIgnoreCase) ||
                        col.ColumnName.Equals("Accode", StringComparison.OrdinalIgnoreCase) ||
                        col.ColumnName.Equals("SortOrder", StringComparison.OrdinalIgnoreCase) ||
                        col.ColumnName.Equals("RowNum", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var val = row[col];
                    if (val == DBNull.Value || val == null)
                    {
                        sb.Append("<td></td>");
                    }
                    else if (col.DataType == typeof(DateTime))
                    {
                        sb.Append($"<td>{Convert.ToDateTime(val):dd-MMM-yyyy}</td>");
                    }
                    else if (col.DataType == typeof(decimal) || col.DataType == typeof(double))
                    {
                        decimal dVal = Convert.ToDecimal(val);
                        sb.Append($"<td class='num'>{(dVal != 0 ? dVal.ToString("#,##0.00") : "")}</td>");
                    }
                    else
                    {
                        sb.Append($"<td>{val}</td>");
                    }
                }
                sb.Append("</tr>");
            }

            sb.Append("</tbody></table>");

            if (!isForPdf && !isPrintMode)
            {
                sb.Append(Nskg.Helpers.ReportPaginationHelper.GetPaginationScript());
            }

            sb.Append("</body></html>");

            return sb.ToString();
        }

        [HttpGet]
        public IActionResult GeneratePDF(string accode, DateTime fromDate, DateTime toDate, int? companyId, string? format = null)
        {
            if (string.IsNullOrWhiteSpace(accode))
            {
                return Content("<div style='font-family:Arial; padding:30px; text-align:center; color:#721c24; background-color:#f8d7da; border:1px solid #f5c6cb; border-radius:6px; margin:20px;'><strong>Please select an account before generating report.</strong></div>", "text/html");
            }

            int selectedCompanyId = GetCompanyId(companyId);
            int financialYearId = GetFinancialYearId();
            try
            {
                DataTable dt = GetGeneralLedger(accode, fromDate, toDate, selectedCompanyId, financialYearId);

                if (dt == null || dt.Rows.Count == 0)
                {
                    return Content("<div style='font-family:Arial; padding:30px; text-align:center; color:#721c24; background-color:#f8d7da; border:1px solid #f5c6cb; border-radius:6px; margin:20px;'><strong>No data found for selected date range.</strong></div>", "text/html");
                }

                string html = BuildGeneralLedgerHtml(dt, accode, fromDate, toDate, isPrintMode: format == "print", isForPdf: format != "print");

                if (format == "print")
                {
                    return Content(html, "text/html");
                }

                byte[] pdfBytes = Nskg.Helpers.PdfReportHelper.GeneratePdfFromHtml(html, PdfSharpCore.PageSize.A4, PdfSharpCore.PageOrientation.Landscape);

                if (pdfBytes == null || pdfBytes.Length < 100)
                {
                    return Content(BuildGeneralLedgerHtml(dt, accode, fromDate, toDate, isPrintMode: true, isForPdf: false), "text/html");
                }

                return File(pdfBytes, "application/pdf", $"GeneralLedger_{accode}_{fromDate:yyyyMMdd}_{toDate:yyyyMMdd}.pdf", enableRangeProcessing: true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GeneralLedgerReport] GeneratePDF exception: {ex.Message}. Falling back to print HTML.");
                try
                {
                    DataTable dt = GetGeneralLedger(accode, fromDate, toDate, selectedCompanyId, financialYearId);
                    return Content(BuildGeneralLedgerHtml(dt, accode, fromDate, toDate, isPrintMode: true, isForPdf: false), "text/html");
                }
                catch
                {
                    return Content($"ERROR: {ex.Message}\n\nSTACK: {ex.StackTrace}");
                }
            }
        }


        [HttpGet]
        public IActionResult OnScreenReport(string accode, DateTime fromDate, DateTime toDate, int? companyId)
        {
            if (string.IsNullOrWhiteSpace(accode))
            {
                return Content("<div style='font-family:Arial; padding:30px; text-align:center; color:#721c24; background-color:#f8d7da; border:1px solid #f5c6cb; border-radius:6px; margin:20px;'><strong>Please select an account before viewing report.</strong></div>", "text/html");
            }

            int selectedCompanyId = GetCompanyId(companyId);
            int financialYearId = GetFinancialYearId();
            var dt = GetGeneralLedger(accode, fromDate, toDate, selectedCompanyId, financialYearId);

            if (dt == null || dt.Rows.Count == 0)
            {
                return Content("<div style='font-family:Arial; padding:30px; text-align:center; color:#721c24; background-color:#f8d7da; border:1px solid #f5c6cb; border-radius:6px; margin:20px;'><strong>No data found for selected date range.</strong></div>", "text/html");
            }

            string html = BuildGeneralLedgerHtml(dt, accode, fromDate, toDate, isPrintMode: false, isForPdf: false);
            return Content(html, "text/html");
        }

        [HttpGet]
        public IActionResult ExportExcel(string accode, DateTime fromDate, DateTime toDate, int? companyId)
        {
            if (string.IsNullOrWhiteSpace(accode))
            {
                return Content("Please select an account before exporting.");
            }

            int selectedCompanyId = GetCompanyId(companyId);
            int financialYearId = GetFinancialYearId();
            var dt = GetGeneralLedger(accode, fromDate, toDate, selectedCompanyId, financialYearId);

            if (dt == null || dt.Rows.Count == 0)
            {
                return Content("No data found for selected date range.");
            }

            var csv = ConvertDataTableToCsv(dt);
            var fileName = $"GeneralLedgerReport_{fromDate:yyyyMMdd}_{toDate:yyyyMMdd}.csv";

            return File(Encoding.UTF8.GetBytes(csv), "text/csv", fileName);
        }

        private string ConvertDataTableToCsv(DataTable dt)
        {
            if (dt == null || dt.Columns.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();

            // header
            for (int i = 0; i < dt.Columns.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(EscapeCsv(dt.Columns[i].ColumnName));
            }
            sb.AppendLine();

            // rows
            foreach (DataRow row in dt.Rows)
            {
                for (int i = 0; i < dt.Columns.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    var val = row[i] == DBNull.Value ? string.Empty : row[i].ToString();
                    sb.Append(EscapeCsv(val));
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private string EscapeCsv(string s)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;

            // If contains quote, comma, or newline, wrap in quotes and escape quotes by doubling them
            if (s.Contains("\"") || s.Contains(",") || s.Contains("\n") || s.Contains("\r"))
            {
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            }
            return s;
        }
    }
}
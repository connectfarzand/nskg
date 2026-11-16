using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Reporting.NETCore;
using System.Data;
using System.IO;
using System.Text;

namespace Nskg.Controllers  // Apna actual namespace dalo
{
    [Authorize(Roles = "Admin")]
    public class AuditLogReportController : Controller
    {
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _env;

        public AuditLogReportController(IConfiguration config, IWebHostEnvironment env)
        {
            _config = config;
            _env = env;

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        }

        // GET: Display form
        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        private DataTable GetAuditLogs(DateTime fromDate, DateTime toDate, int? companyId, int? financialYearId)
        {
            DataTable dt = new DataTable();

            string connString = _config.GetConnectionString("DefaultConnection");

            using (SqlConnection con = new SqlConnection(connString))
            {
                using (SqlCommand cmd = new SqlCommand("sp_GetAuditLogs", con))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    cmd.Parameters.AddWithValue("@FromDate", fromDate);
                    cmd.Parameters.AddWithValue("@ToDate", toDate);
                    cmd.Parameters.AddWithValue("@CompanyId", companyId ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@FinancialYearId", financialYearId ?? (object)DBNull.Value);

                    con.Open();

                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        dt.Load(reader); // 🔥 THIS IS THE FIX
                    }
                }
            }

            return dt;
        }


        [HttpGet]
        private string BuildAuditLogHtml(DataTable dt, DateTime fromDate, DateTime toDate, bool isPrintMode = false, bool isForPdf = false)
        {
            var sb = new StringBuilder();
            sb.Append(@"<!DOCTYPE html><html><head><meta charset='utf-8'>
            <title>Audit Log Report</title>
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
                sb.Append(Nskg.Helpers.ReportPaginationHelper.GetPaginationToolbarHtml("Audit Log"));
            }

            sb.Append($"<div class='header-box'>");
            sb.Append($"<h2>AUDIT LOG REPORT</h2>");
            sb.Append($"<p>Period: <b>{fromDate:dd-MMM-yyyy}</b> to <b>{toDate:dd-MMM-yyyy}</b></p>");
            sb.Append("</div>");

            sb.Append("<table><thead><tr>");
            sb.Append("<th style='width:35px;' class='center'>#</th>");
            foreach (DataColumn col in dt.Columns)
            {
                sb.Append($"<th>{col.ColumnName}</th>");
            }
            sb.Append("</tr></thead><tbody>");

            int sr = 1;
            foreach (DataRow row in dt.Rows)
            {
                sb.Append("<tr>");
                sb.Append($"<td class='center'>{sr++}</td>");
                foreach (DataColumn col in dt.Columns)
                {
                    var val = row[col];
                    if (val == DBNull.Value || val == null)
                    {
                        sb.Append("<td></td>");
                    }
                    else if (col.DataType == typeof(DateTime))
                    {
                        sb.Append($"<td>{Convert.ToDateTime(val):dd-MMM-yyyy HH:mm}</td>");
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
        public IActionResult GeneratePDF(DateTime fromDate, DateTime toDate, int? companyId, int? financialYearId, string? format = null)
        {
            try
            {
                DataTable dt = GetAuditLogs(fromDate, toDate, companyId, financialYearId);

                if (dt == null || dt.Rows.Count == 0)
                {
                    return Content("<div style='font-family:Arial; padding:30px; text-align:center; color:#721c24; background-color:#f8d7da; border:1px solid #f5c6cb; border-radius:6px; margin:20px;'><strong>No data found for selected date range.</strong></div>", "text/html");
                }

                string html = BuildAuditLogHtml(dt, fromDate, toDate, isPrintMode: format == "print", isForPdf: format != "print");

                if (format == "print")
                {
                    return Content(html, "text/html");
                }

                byte[] pdfBytes = Nskg.Helpers.PdfReportHelper.GeneratePdfFromHtml(html, PdfSharpCore.PageSize.A4, PdfSharpCore.PageOrientation.Landscape);

                if (pdfBytes == null || pdfBytes.Length < 100)
                {
                    return Content(BuildAuditLogHtml(dt, fromDate, toDate, isPrintMode: true, isForPdf: false), "text/html");
                }

                return File(pdfBytes, "application/pdf", $"AuditLog_{fromDate:yyyyMMdd}_{toDate:yyyyMMdd}.pdf", enableRangeProcessing: true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AuditLogReport] GeneratePDF exception: {ex.Message}. Falling back to print HTML.");
                try
                {
                    DataTable dt = GetAuditLogs(fromDate, toDate, companyId, financialYearId);
                    return Content(BuildAuditLogHtml(dt, fromDate, toDate, isPrintMode: true, isForPdf: false), "text/html");
                }
                catch
                {
                    return Content($"ERROR: {ex.Message}\n\nSTACK: {ex.StackTrace}");
                }
            }
        }


        [HttpGet]
        public IActionResult OnScreenReport(DateTime fromDate, DateTime toDate, int? companyId, int? financialYearId)
        {
            var dt = GetAuditLogs(fromDate, toDate, companyId, financialYearId);

            if (dt == null || dt.Rows.Count == 0)
            {
                return Content("<div style='font-family:Arial; padding:30px; text-align:center; color:#721c24; background-color:#f8d7da; border:1px solid #f5c6cb; border-radius:6px; margin:20px;'><strong>No data found for selected date range.</strong></div>", "text/html");
            }

            string html = BuildAuditLogHtml(dt, fromDate, toDate, isPrintMode: false, isForPdf: false);
            return Content(html, "text/html");
        }

        [HttpGet]
        public IActionResult ExportExcel(DateTime fromDate, DateTime toDate, int? companyId, int? financialYearId)
        {
            var dt = GetAuditLogs(fromDate, toDate, companyId, financialYearId);

            if (dt == null || dt.Rows.Count == 0)
            {
                return Content("No data found for selected date range.");
            }

            var csv = ConvertDataTableToCsv(dt);
            var fileName = $"AuditLogReport_{fromDate:yyyyMMdd}_{toDate:yyyyMMdd}.csv";

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

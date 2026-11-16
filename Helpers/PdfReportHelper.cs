using System;
using System.IO;
using PdfSharpCore;
using PdfSharpCore.Fonts;
using PdfSharpCore.Pdf;
using VetCV.HtmlRendererCore.PdfSharpCore;

namespace Nskg.Helpers
{
    public class LinuxFontResolver : IFontResolver
    {
        public string DefaultFontName => "Arial";

        public byte[] GetFont(string faceName)
        {
            string[] searchDirs = new[]
            {
                "/usr/share/fonts/truetype/msttcorefonts",
                "/usr/share/fonts/truetype/liberation",
                "/usr/share/fonts/truetype/dejavu",
                "/usr/share/fonts/truetype"
            };

            foreach (var dir in searchDirs)
            {
                if (!Directory.Exists(dir)) continue;
                string filePath = Path.Combine(dir, faceName);
                if (File.Exists(filePath)) return File.ReadAllBytes(filePath);
            }

            string[] fallbackFiles = new[]
            {
                "/usr/share/fonts/truetype/msttcorefonts/Arial.ttf",
                "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
                "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"
            };

            foreach (var fb in fallbackFiles)
            {
                if (File.Exists(fb)) return File.ReadAllBytes(fb);
            }

            return null!;
        }

        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            if (familyName != null && familyName.IndexOf("Times", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (isBold && isItalic) return new FontResolverInfo("Times_New_Roman_Bold_Italic.ttf");
                if (isBold) return new FontResolverInfo("Times_New_Roman_Bold.ttf");
                if (isItalic) return new FontResolverInfo("Times_New_Roman_Italic.ttf");
                return new FontResolverInfo("Times_New_Roman.ttf");
            }

            if (familyName != null && familyName.IndexOf("Courier", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (isBold && isItalic) return new FontResolverInfo("Courier_New_Bold_Italic.ttf");
                if (isBold) return new FontResolverInfo("Courier_New_Bold.ttf");
                if (isItalic) return new FontResolverInfo("Courier_New_Italic.ttf");
                return new FontResolverInfo("Courier_New.ttf");
            }

            if (isBold && isItalic) return new FontResolverInfo("Arial_Bold_Italic.ttf");
            if (isBold) return new FontResolverInfo("Arial_Bold.ttf");
            if (isItalic) return new FontResolverInfo("Arial_Italic.ttf");
            return new FontResolverInfo("Arial.ttf");
        }
    }

    public static class PdfReportHelper
    {
        private static readonly object _lock = new object();
        private static bool _initialized = false;

        public static void EnsureInitialized()
        {
            if (_initialized) return;
            lock (_lock)
            {
                if (_initialized) return;
                try
                {
                    GlobalFontSettings.FontResolver = new LinuxFontResolver();
                }
                catch
                {
                    // Ignore if already set in runtime
                }
                _initialized = true;
            }
        }

        public static byte[] GeneratePdfFromHtml(string html, PageSize pageSize = PageSize.A4, PageOrientation orientation = PageOrientation.Portrait)
        {
            EnsureInitialized();

            var config = new PdfGenerateConfig
            {
                PageSize = pageSize,
                PageOrientation = orientation,
                MarginTop = 15,
                MarginBottom = 15,
                MarginLeft = 15,
                MarginRight = 15
            };

            using (var pdf = PdfGenerator.GeneratePdf(html, config))
            using (var ms = new MemoryStream())
            {
                pdf.Save(ms, false);
                return ms.ToArray();
            }
        }

        public static string WrapPrintableHtml(string html, bool autoPrint = false)
        {
            if (string.IsNullOrWhiteSpace(html)) return html;

            if (autoPrint && !html.Contains("window.print()"))
            {
                string printScript = @"<script>
                    window.addEventListener('load', function() {
                        setTimeout(function() { window.print(); }, 300);
                    });
                </script>";

                if (html.Contains("</head>"))
                {
                    html = html.Replace("</head>", printScript + "</head>");
                }
                else
                {
                    html = printScript + html;
                }
            }

            return html;
        }
    }
}

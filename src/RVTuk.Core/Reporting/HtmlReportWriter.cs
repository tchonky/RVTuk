using System;
using System.Globalization;
using System.Text;
using RVTuk.Core.Models.Comparison;

namespace RVTuk.Core.Reporting
{
    /// <summary>Renders a ComparisonResult as a self-contained HTML report (inline CSS, no
    /// external dependencies) suitable for opening in a browser and printing to PDF.</summary>
    public static class HtmlReportWriter
    {
        public static string Write(ComparisonResult result)
        {
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">");
            sb.Append("<title>RVTuk Project Comparator</title>");
            sb.Append("<style>");
            sb.Append("body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#1e1e1e;}");
            sb.Append("h1{font-size:20px;} h2{font-size:16px;margin-top:28px;}");
            sb.Append("table{border-collapse:collapse;width:100%;margin:8px 0;}");
            sb.Append("th,td{border:1px solid #ccc;padding:4px 8px;text-align:left;font-size:13px;}");
            sb.Append("th{background:#f0f0f0;}");
            sb.Append(".added{background:#e7f6e7;} .removed{background:#fbe7e7;} .changed{background:#fff4e0;}");
            sb.Append(".muted{color:#888;} .field{font-size:12px;}");
            sb.Append("</style></head><body>");

            sb.Append("<h1>RVTuk &mdash; Project Comparator report</h1>");
            sb.Append("<p><strong>A:</strong> ").Append(Enc(result.SideA.SourceName))
              .Append(" &nbsp;&nbsp; <strong>B:</strong> ").Append(Enc(result.SideB.SourceName)).Append("</p>");
            sb.Append("<p class=\"muted\">Generated ")
              .Append(Enc(DateTime.UtcNow.ToString("u", CultureInfo.InvariantCulture)))
              .Append(". Recommendations rank on completeness, not modification date.</p>");

            foreach (var cat in result.Categories)
            {
                sb.Append("<h2>").Append(Enc(cat.DisplayName)).Append("</h2>");
                sb.Append("<p>")
                  .Append(cat.Summary.Changed).Append(" changed, ")
                  .Append(cat.Summary.Added).Append(" only in A, ")
                  .Append(cat.Summary.Removed).Append(" only in B, ")
                  .Append(cat.Summary.Unchanged).Append(" identical.</p>");

                sb.Append("<table><tr><th>Status</th><th>Item</th><th>Score A</th><th>Score B</th><th>Recommendation</th></tr>");
                foreach (var item in cat.Items)
                {
                    sb.Append("<tr class=\"").Append(CssClass(item.Kind)).Append("\">");
                    sb.Append("<td>").Append(StatusLabel(item.Kind)).Append("</td>");
                    sb.Append("<td>").Append(Enc(item.DisplayName));
                    if (item.Kind == DiffKind.Changed && item.Fields.Count > 0)
                    {
                        sb.Append("<div class=\"field muted\">");
                        bool first = true;
                        foreach (var f in item.Fields)
                        {
                            if (f.IsEqual) continue;
                            if (!first) sb.Append("; ");
                            sb.Append(Enc(f.Label)).Append(": ")
                              .Append(Enc(f.ValueA ?? "—")).Append(" &rarr; ").Append(Enc(f.ValueB ?? "—"));
                            first = false;
                        }
                        sb.Append("</div>");
                    }
                    sb.Append("</td>");
                    sb.Append("<td>").Append(Pct(item.CompletenessA)).Append("</td>");
                    sb.Append("<td>").Append(Pct(item.CompletenessB)).Append("</td>");
                    sb.Append("<td>").Append(Enc(Recommendation(item))).Append("</td>");
                    sb.Append("</tr>");
                }
                sb.Append("</table>");
            }

            sb.Append("</body></html>");
            return sb.ToString();
        }

        private static string Recommendation(ItemDiff item)
        {
            switch (item.Kind)
            {
                case DiffKind.Added: return "Only in A — consider adopting";
                case DiffKind.Removed: return "Only in B — consider adopting";
                case DiffKind.Unchanged: return "Identical";
                default:
                    if (item.CompletenessB > item.CompletenessA) return "B is more complete";
                    if (item.CompletenessA > item.CompletenessB) return "A is more complete";
                    return "Differs — review";
            }
        }

        private static string CssClass(DiffKind k) => k switch
        {
            DiffKind.Added => "added",
            DiffKind.Removed => "removed",
            DiffKind.Changed => "changed",
            _ => ""
        };

        private static string StatusLabel(DiffKind k) => k switch
        {
            DiffKind.Added => "Only in A",
            DiffKind.Removed => "Only in B",
            DiffKind.Changed => "Changed",
            _ => "Identical"
        };

        private static string Pct(double v) => ((int)Math.Round(v * 100)).ToString(CultureInfo.InvariantCulture) + "%";

        private static string Enc(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s!.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }
    }
}

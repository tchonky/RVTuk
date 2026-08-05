using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RVTuk.Core.DwgExporter
{
    /// <summary>
    /// Renders the TRANSMITTAL.txt that ships inside the archive. Its job is as much to say
    /// what the bundle does <em>not</em> contain — fonts the receiver has to already have —
    /// as what it does. Pure.
    /// </summary>
    public static class TransmittalReport
    {
        public static string Render(TransmittalInfo info, TransmittalContents contents)
        {
            var sb = new StringBuilder();
            sb.AppendLine("RVTuk — DWG transmittal");
            sb.AppendLine("=======================");
            sb.AppendLine();
            sb.AppendLine("Created:        " + info.CreatedUtc.ToString("yyyy-MM-dd HH:mm") + " UTC");
            if (!string.IsNullOrWhiteSpace(info.SheetSetName))
                sb.AppendLine("Range:          " + info.SheetSetName);
            if (info.ModelTitles.Count > 0)
                sb.AppendLine("Models:         " + string.Join(", ", info.ModelTitles));
            if (!string.IsNullOrWhiteSpace(info.DwgSetupName))
                sb.AppendLine("DWG setup:      " + info.DwgSetupName);
            if (!string.IsNullOrWhiteSpace(info.SheetNamingSetupName))
                sb.AppendLine("Naming, sheets: " + info.SheetNamingSetupName);
            if (!string.IsNullOrWhiteSpace(info.ViewNamingSetupName))
                sb.AppendLine("Naming, views:  " + info.ViewNamingSetupName);

            Section(sb, "Drawings", contents.Entries, TransmittalKind.Drawing);
            Section(sb, "Images", contents.Entries, TransmittalKind.Image);
            Section(sb, "Fonts", contents.Entries, TransmittalKind.Font);
            Section(sb, "Other files", contents.Entries, TransmittalKind.Other);

            if (contents.FontsNotIncluded.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Fonts NOT included — you need these installed");
                sb.AppendLine("---------------------------------------------");
                sb.AppendLine("These are TrueType fonts, or could not be found on the exporting");
                sb.AppendLine("machine. They are left out because redistributing a TrueType font");
                sb.AppendLine("generally breaches its licence. Text will be substituted without them.");
                foreach (var font in contents.FontsNotIncluded) sb.AppendLine("  " + font);
            }

            if (contents.Warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Warnings");
                sb.AppendLine("--------");
                foreach (var warning in contents.Warnings) sb.AppendLine("  " + warning);
            }

            return sb.ToString();
        }

        private static void Section(
            StringBuilder sb, string heading, IReadOnlyList<TransmittalEntry> entries, TransmittalKind kind)
        {
            var matching = entries.Where(e => e.Kind == kind).ToList();
            if (matching.Count == 0) return;

            sb.AppendLine();
            sb.AppendLine(heading + " (" + matching.Count + ")");
            sb.AppendLine(new string('-', heading.Length + 4));
            foreach (var entry in matching.OrderBy(e => e.EntryName, StringComparer.OrdinalIgnoreCase))
                sb.AppendLine("  " + entry.EntryName);
        }
    }
}

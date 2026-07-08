using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RVTuk.UI.Helpers
{
    // Small, dependency-free, best-effort markdown -> FlowDocument renderer for the Help panel.
    // Supports: #/## headings, plain paragraphs, **bold**, *italic*/_italic_, `inline code`,
    // [text](url) links, "- "/"* " bullet lists, "---" horizontal rules, and GFM pipe tables.
    // Anything else is rendered as plain text rather than throwing. Colors are hardcoded to
    // match Themes/DarkTheme.xaml (Brush.Text/Control/Accent/Border/Success, and the DataGrid's
    // #1F1F20 AlternatingRowBackground for table row banding) since this is a plain C# class,
    // not XAML, and has no loaded ResourceDictionary to pull {StaticResource} values from.
    public static class MarkdownConverter
    {
        private static readonly SolidColorBrush TextBrush = MakeBrush("#E3E3E3");
        private static readonly SolidColorBrush ControlBrush = MakeBrush("#3C3C3C");
        private static readonly SolidColorBrush AccentBrush = MakeBrush("#4A90D9");
        private static readonly SolidColorBrush BorderBrush = MakeBrush("#4A4A4A");
        private static readonly SolidColorBrush AltRowBrush = MakeBrush("#1F1F20");

        private static readonly Regex TableSeparatorRegex = new Regex(
            @"^\|?\s*:?-{1,}:?\s*(\|\s*:?-{1,}:?\s*)*\|?$", RegexOptions.Compiled);

        // Raised when the user clicks a generated [text](url) hyperlink. Opening a browser (or
        // whatever) from this library-level helper isn't this class's call to make, so we just
        // surface the URL and let the caller decide what to do with it.
        public static event Action<string>? LinkClicked;

        private static readonly Regex InlineRegex = new Regex(
            @"!\[(?<imgalt>[^\]]*)\]\((?<imgurl>[^)]+)\)" +
            @"|\[(?<linktext>[^\]]+)\]\((?<linkurl>[^)]+)\)" +
            @"|\*\*(?<bold>[^*]+)\*\*" +
            @"|\*(?<italic1>[^*]+)\*" +
            @"|_(?<italic2>[^_]+)_" +
            @"|`(?<code>[^`]+)`",
            RegexOptions.Compiled);

        private const string DataUriPrefix = "data:image";
        private const string Base64Marker = "base64,";

        public static FlowDocument Build(string? markdown)
        {
            try
            {
                return BuildInternal(markdown);
            }
            catch
            {
                var errorDoc = new FlowDocument { PagePadding = new Thickness(0) };
                errorDoc.Blocks.Add(new Paragraph(new Run("Unable to render document."))
                {
                    Foreground = TextBrush
                });
                return errorDoc;
            }
        }

        private static FlowDocument BuildInternal(string? markdown)
        {
            var doc = new FlowDocument { PagePadding = new Thickness(0) };
            if (string.IsNullOrEmpty(markdown))
                return doc;

            var lines = markdown!.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

            bool isFirstBlock = true;
            var paragraphBuffer = new System.Collections.Generic.List<string>();
            var listBuffer = new System.Collections.Generic.List<string>();

            void FlushParagraph()
            {
                if (paragraphBuffer.Count == 0) return;
                var text = string.Join(" ", paragraphBuffer).Trim();
                paragraphBuffer.Clear();
                if (text.Length == 0) return;

                var p = new Paragraph
                {
                    FontSize = 13,
                    Foreground = TextBrush,
                    LineHeight = 20,
                    Margin = new Thickness(0, isFirstBlock ? 0 : 4, 0, 8)
                };
                foreach (var inline in ParseInline(text))
                    p.Inlines.Add(inline);
                doc.Blocks.Add(p);
                isFirstBlock = false;
            }

            void FlushList()
            {
                if (listBuffer.Count == 0) return;
                var list = new System.Windows.Documents.List
                {
                    MarkerStyle = TextMarkerStyle.Disc,
                    Margin = new Thickness(0, isFirstBlock ? 0 : 4, 0, 8)
                };
                foreach (var item in listBuffer)
                {
                    var p = new Paragraph { FontSize = 13, Foreground = TextBrush };
                    foreach (var inline in ParseInline(item))
                        p.Inlines.Add(inline);
                    list.ListItems.Add(new ListItem(p));
                }
                listBuffer.Clear();
                doc.Blocks.Add(list);
                isFirstBlock = false;
            }

            for (int i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();

                // GFM pipe table: a "| a | b |"-style row immediately followed by a
                // "|---|---|"-style separator row of the same column count. Checked before the
                // "---" rule below so a real separator row is never swallowed as a plain <hr>.
                if (trimmed.Contains("|") && i + 1 < lines.Length)
                {
                    var headerCells = SplitTableRow(trimmed);
                    var nextTrimmed = lines[i + 1].Trim();
                    if (headerCells.Length >= 2 && TableSeparatorRegex.IsMatch(nextTrimmed) &&
                        SplitTableRow(nextTrimmed).Length == headerCells.Length)
                    {
                        FlushParagraph();
                        FlushList();

                        i += 2; // skip header + separator rows already consumed above
                        var bodyRows = new System.Collections.Generic.List<string[]>();
                        while (i < lines.Length && lines[i].Trim().Contains("|"))
                        {
                            bodyRows.Add(SplitTableRow(lines[i].Trim()));
                            i++;
                        }
                        i--; // compensate for the loop's own i++ on the next iteration

                        AddTable(doc, headerCells, bodyRows, ref isFirstBlock);
                        continue;
                    }
                }

                if (trimmed == "---")
                {
                    FlushParagraph();
                    FlushList();

                    var rule = new Border
                    {
                        Height = 1,
                        Background = BorderBrush,
                        Margin = new Thickness(0, isFirstBlock ? 0 : 10, 0, 10)
                    };
                    doc.Blocks.Add(new BlockUIContainer(rule));
                    isFirstBlock = false;
                    continue;
                }

                if (trimmed.StartsWith("## ", StringComparison.Ordinal))
                {
                    FlushParagraph();
                    FlushList();
                    AddHeading(doc, trimmed.Substring(3).Trim(), fontSize: 15,
                        marginTop: isFirstBlock ? 0 : 12, marginBottom: 4, ref isFirstBlock);
                    continue;
                }

                if (trimmed.StartsWith("# ", StringComparison.Ordinal))
                {
                    FlushParagraph();
                    FlushList();
                    AddHeading(doc, trimmed.Substring(2).Trim(), fontSize: 19,
                        marginTop: isFirstBlock ? 0 : 16, marginBottom: 6, ref isFirstBlock);
                    continue;
                }

                if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
                {
                    FlushParagraph();
                    listBuffer.Add(trimmed.Substring(2).Trim());
                    continue;
                }

                if (trimmed.Length == 0)
                {
                    FlushParagraph();
                    FlushList();
                    continue;
                }

                FlushList();
                paragraphBuffer.Add(trimmed);
            }

            FlushParagraph();
            FlushList();

            return doc;
        }

        private static void AddHeading(FlowDocument doc, string text, double fontSize, double marginTop, double marginBottom, ref bool isFirstBlock)
        {
            var p = new Paragraph
            {
                FontSize = fontSize,
                Foreground = TextBrush,
                Margin = new Thickness(0, marginTop, 0, marginBottom)
            };
            var bold = new Bold();
            foreach (var inline in ParseInline(text))
                bold.Inlines.Add(inline);
            p.Inlines.Add(bold);
            doc.Blocks.Add(p);
            isFirstBlock = false;
        }

        // "| a | b |" -> ["a", "b"]. Tolerates missing leading/trailing pipes.
        private static string[] SplitTableRow(string line)
        {
            var t = line;
            if (t.StartsWith("|", StringComparison.Ordinal)) t = t.Substring(1);
            if (t.EndsWith("|", StringComparison.Ordinal)) t = t.Substring(0, t.Length - 1);
            return t.Split('|').Select(c => c.Trim()).ToArray();
        }

        private static void AddTable(FlowDocument doc, string[] headerCells,
            System.Collections.Generic.List<string[]> bodyRows, ref bool isFirstBlock)
        {
            var table = new Table
            {
                CellSpacing = 0,
                Margin = new Thickness(0, isFirstBlock ? 0 : 8, 0, 8)
            };
            int colCount = headerCells.Length;
            for (int c = 0; c < colCount; c++) table.Columns.Add(new TableColumn());

            TableCell MakeCell(string text, bool header, Brush? rowBg)
            {
                var p = new Paragraph
                {
                    FontSize = 12,
                    Foreground = TextBrush,
                    FontWeight = header ? FontWeights.Bold : FontWeights.Normal,
                    Margin = new Thickness(0)
                };
                foreach (var inline in ParseInline(text))
                    p.Inlines.Add(inline);
                return new TableCell(p)
                {
                    Background = header ? ControlBrush : rowBg,
                    BorderBrush = BorderBrush,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(6, 3, 6, 3)
                };
            }

            var headerGroup = new TableRowGroup();
            var headerRow = new TableRow();
            foreach (var cell in headerCells)
                headerRow.Cells.Add(MakeCell(cell, header: true, rowBg: null));
            headerGroup.Rows.Add(headerRow);
            table.RowGroups.Add(headerGroup);

            var bodyGroup = new TableRowGroup();
            for (int r = 0; r < bodyRows.Count; r++)
            {
                var row = new TableRow();
                // Alternating row banding, matching the app's own DataGrid.AlternatingRowBackground.
                var rowBg = (r % 2 == 1) ? AltRowBrush : null;
                for (int c = 0; c < colCount; c++)
                {
                    var text = c < bodyRows[r].Length ? bodyRows[r][c] : string.Empty;
                    row.Cells.Add(MakeCell(text, header: false, rowBg: rowBg));
                }
                bodyGroup.Rows.Add(row);
            }
            table.RowGroups.Add(bodyGroup);

            doc.Blocks.Add(table);
            isFirstBlock = false;
        }

        private static System.Collections.Generic.IEnumerable<Inline> ParseInline(string text)
        {
            var result = new System.Collections.Generic.List<Inline>();
            int last = 0;

            foreach (Match m in InlineRegex.Matches(text))
            {
                if (m.Index > last)
                    result.Add(new Run(text.Substring(last, m.Index - last)));

                if (m.Groups["imgurl"].Success)
                {
                    result.Add(BuildImageInline(m.Groups["imgurl"].Value, m.Groups["imgalt"].Value));
                }
                else if (m.Groups["linktext"].Success)
                {
                    var linkText = m.Groups["linktext"].Value;
                    var url = m.Groups["linkurl"].Value;
                    var hyperlink = new Hyperlink(new Run(linkText))
                    {
                        Foreground = AccentBrush,
                        TextDecorations = null
                    };
                    if (Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var uri))
                        hyperlink.NavigateUri = uri;
                    hyperlink.Click += (s, e) => LinkClicked?.Invoke(url);
                    result.Add(hyperlink);
                }
                else if (m.Groups["bold"].Success)
                {
                    var bold = new Bold();
                    foreach (var inner in ParseInline(m.Groups["bold"].Value))
                        bold.Inlines.Add(inner);
                    result.Add(bold);
                }
                else if (m.Groups["italic1"].Success)
                {
                    var italic = new Italic();
                    foreach (var inner in ParseInline(m.Groups["italic1"].Value))
                        italic.Inlines.Add(inner);
                    result.Add(italic);
                }
                else if (m.Groups["italic2"].Success)
                {
                    var italic = new Italic();
                    foreach (var inner in ParseInline(m.Groups["italic2"].Value))
                        italic.Inlines.Add(inner);
                    result.Add(italic);
                }
                else if (m.Groups["code"].Success)
                {
                    result.Add(new Run(m.Groups["code"].Value)
                    {
                        FontFamily = new FontFamily("Consolas"),
                        Background = ControlBrush,
                        Foreground = TextBrush
                    });
                }

                last = m.Index + m.Length;
            }

            if (last < text.Length)
                result.Add(new Run(text.Substring(last)));

            if (result.Count == 0)
                result.Add(new Run(text));

            return result;
        }

        // Turn "![alt](url)" into an inline. A data:image;base64 URI is decoded into a bare Image
        // wrapped in an InlineUIContainer (the caller re-wraps/tags it for the editor as needed —
        // this class stays decoupled from those concerns; the source base64 is parked on Image.Tag
        // so the caller can persist it without re-encoding). Any other URL (e.g. http) can't be
        // fetched synchronously here, so it degrades to a plain-text placeholder rather than
        // throwing or blocking.
        private static Inline BuildImageInline(string url, string alt)
        {
            if (url.StartsWith(DataUriPrefix, StringComparison.OrdinalIgnoreCase))
            {
                int idx = url.IndexOf(Base64Marker, StringComparison.Ordinal);
                if (idx >= 0)
                {
                    try
                    {
                        var b64 = url.Substring(idx + Base64Marker.Length);
                        var bytes = Convert.FromBase64String(b64);
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.StreamSource = new MemoryStream(bytes);
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                        bmp.Freeze();

                        var img = new Image
                        {
                            Source = bmp,
                            Stretch = Stretch.Uniform,
                            Width = Math.Min(400, bmp.PixelWidth),
                            Tag = b64 // carry the persisted bytes for the caller; not editor-specific
                        };
                        return new InlineUIContainer(img);
                    }
                    catch { /* fall through to placeholder */ }
                }
            }

            return new Run(string.IsNullOrEmpty(alt) ? "[image]" : alt);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // FlowDocument -> Markdown (inverse of Build). Used by the Instructions editor's Raw view.
        // ─────────────────────────────────────────────────────────────────────────

        // Walk a FlowDocument and emit the closest matching Markdown. Lossy by design for a few
        // cases (see the report / inline notes): underline is dropped, and headings are detected
        // heuristically from font size because this editor has no real heading block.
        //
        // imageDataProvider supplies the persisted base64 for an inline Image (the editor passes
        // RichTextBoxHelper.GetImageData). Kept as an injected delegate so this helper stays
        // decoupled from the editor-specific ImageData attached property.
        public static string ToMarkdown(FlowDocument? doc, Func<Image, string?>? imageDataProvider = null)
        {
            if (doc == null) return string.Empty;

            var parts = new List<string>();
            foreach (var block in doc.Blocks)
            {
                var s = BlockToMarkdown(block, imageDataProvider);
                if (s != null) parts.Add(s);
            }
            return string.Join("\n\n", parts);
        }

        private static string? BlockToMarkdown(Block block, Func<Image, string?>? imageProvider)
        {
            switch (block)
            {
                case Paragraph p:
                {
                    var level = HeadingLevel(p);
                    var prefix = level == 1 ? "# " : level == 2 ? "## " : string.Empty;
                    var text = InlinesToMarkdown(p.Inlines, imageProvider, suppressBold: level > 0);
                    if (level == 0 && string.IsNullOrWhiteSpace(text)) return null;
                    return prefix + text;
                }
                case List list:
                {
                    var lines = new List<string>();
                    foreach (var li in list.ListItems)
                        foreach (var b in li.Blocks)
                            if (b is Paragraph lp)
                                lines.Add("- " + InlinesToMarkdown(lp.Inlines, imageProvider, false));
                    return lines.Count == 0 ? null : string.Join("\n", lines);
                }
                case Table table:
                    return TableToMarkdown(table, imageProvider);
                case BlockUIContainer:
                    // Build emits horizontal rules as a BlockUIContainer(Border).
                    return "---";
                case Section section:
                {
                    var parts = new List<string>();
                    foreach (var b in section.Blocks)
                    {
                        var s = BlockToMarkdown(b, imageProvider);
                        if (s != null) parts.Add(s);
                    }
                    return parts.Count == 0 ? null : string.Join("\n\n", parts);
                }
                default:
                    return null;
            }
        }

        // Heuristic: H1_Click/H2_Click just bump FontSize on the selected runs (22 / 16), and
        // Build's own headings use 19 / 15. Treat a paragraph whose every run is >= 18 as H1 and
        // >= 14 (but < 18) as H2. Partial-paragraph large text won't round-trip as a heading.
        private static int HeadingLevel(Paragraph p)
        {
            var runs = new List<Run>();
            GatherRuns(p.Inlines, runs);
            var sized = runs.Where(r => !string.IsNullOrEmpty(r.Text)).ToList();
            if (sized.Count == 0) return 0;

            double min = sized.Min(r => r.FontSize);
            if (min >= 18) return 1;
            if (min >= 14) return 2;
            return 0;
        }

        private static void GatherRuns(InlineCollection inlines, List<Run> runs)
        {
            foreach (var inline in inlines)
            {
                switch (inline)
                {
                    case Run r: runs.Add(r); break;
                    case Span s: GatherRuns(s.Inlines, runs); break; // Bold/Italic/Underline/Hyperlink
                }
            }
        }

        private static string InlinesToMarkdown(InlineCollection inlines,
            Func<Image, string?>? imageProvider, bool suppressBold)
        {
            var sb = new StringBuilder();
            foreach (var inline in inlines)
                sb.Append(InlineToMarkdown(inline, imageProvider, suppressBold));
            return sb.ToString();
        }

        private static string InlineToMarkdown(Inline inline,
            Func<Image, string?>? imageProvider, bool suppressBold)
        {
            switch (inline)
            {
                case Run r:
                {
                    var text = r.Text ?? string.Empty;
                    if (text.Length == 0) return string.Empty;

                    if (IsInlineCode(r)) return "`" + text + "`";

                    bool bold = !suppressBold && r.FontWeight.ToOpenTypeWeight() >= 600;
                    bool italic = r.FontStyle == FontStyles.Italic;
                    if (bold && italic) return "***" + text + "***";
                    if (bold) return "**" + text + "**";
                    if (italic) return "*" + text + "*";
                    return text;
                }
                case Bold b:
                {
                    var inner = InlinesToMarkdown(b.Inlines, imageProvider, suppressBold);
                    return suppressBold ? inner : "**" + inner + "**";
                }
                case Italic it:
                    return "*" + InlinesToMarkdown(it.Inlines, imageProvider, suppressBold) + "*";
                case Underline u:
                    // No Markdown equivalent for underline — drop the decoration, keep the text.
                    return InlinesToMarkdown(u.Inlines, imageProvider, suppressBold);
                case Hyperlink h:
                {
                    var inner = InlinesToMarkdown(h.Inlines, imageProvider, suppressBold);
                    var url = h.NavigateUri?.ToString() ?? string.Empty;
                    return "[" + inner + "](" + url + ")";
                }
                case InlineUIContainer iuc:
                {
                    var img = iuc.Child as Image ?? (iuc.Child as Border)?.Child as Image;
                    if (img != null)
                    {
                        var b64 = imageProvider?.Invoke(img);
                        if (!string.IsNullOrEmpty(b64))
                            return "![](data:image/png;base64," + b64 + ")";
                    }
                    return string.Empty;
                }
                case LineBreak:
                    return "\n";
                case Span s:
                    return InlinesToMarkdown(s.Inlines, imageProvider, suppressBold);
                default:
                    return string.Empty;
            }
        }

        private static bool IsInlineCode(Run r)
            => r.Background != null && r.FontFamily != null && r.FontFamily.Source == "Consolas";

        // Inverse of AddTable/SplitTableRow: emit a GFM pipe table with a header row + separator.
        private static string? TableToMarkdown(Table table, Func<Image, string?>? imageProvider)
        {
            var rows = table.RowGroups.SelectMany(g => g.Rows).ToList();
            if (rows.Count == 0) return null;

            string CellText(TableCell cell)
            {
                var p = cell.Blocks.OfType<Paragraph>().FirstOrDefault();
                var text = p == null ? string.Empty : InlinesToMarkdown(p.Inlines, imageProvider, false);
                return text.Replace("|", "\\|").Replace("\n", " ").Trim();
            }

            string RowLine(TableRow row)
                => "| " + string.Join(" | ", row.Cells.Select(CellText)) + " |";

            var lines = new List<string> { RowLine(rows[0]) };
            int cols = rows[0].Cells.Count;
            lines.Add("| " + string.Join(" | ", Enumerable.Repeat("---", cols)) + " |");
            for (int i = 1; i < rows.Count; i++)
                lines.Add(RowLine(rows[i]));

            return string.Join("\n", lines);
        }

        private static SolidColorBrush MakeBrush(string hex)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}

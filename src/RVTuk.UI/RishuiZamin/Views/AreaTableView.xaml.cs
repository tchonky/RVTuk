using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RVTuk.Core.RishuiZamin;
using RVTuk.UI.RishuiZamin.ViewModels;

namespace RVTuk.UI.RishuiZamin.Views
{
    /// <summary>
    /// The Area Calc window's Table tab: plot area and limits, the limits strip, and a table of
    /// floors × used usage codes laid out like the official "חישוב שטחים בניין" table: Hebrew,
    /// right to left, three merged header rows (group / codes or totals / column), and a double
    /// line between each <see cref="AreaTableBand"/>. DataContext is the window's
    /// <see cref="RishuiZaminViewModel"/>; the table depends on the codes in use, so it is rebuilt
    /// here whenever the view model raises <see cref="RishuiZaminViewModel.TableChanged"/>.
    /// The floor columns (<see cref="FixedPart"/>) stay put while the usage columns
    /// (<see cref="CodesPart"/>) scroll sideways; both share row heights through SharedSizeGroup.
    /// </summary>
    public partial class AreaTableView : UserControl
    {
        private const int HeaderRows = 3;
        private const double DoubleLineWidth = 4;

        private RishuiZaminViewModel? _vm;

        public AreaTableView()
        {
            InitializeComponent();
            DataContextChanged += (_, _) => Attach(DataContext as RishuiZaminViewModel);
        }

        private void Attach(RishuiZaminViewModel? vm)
        {
            if (_vm != null) _vm.TableChanged -= Rebuild;
            _vm = vm;
            if (_vm != null) _vm.TableChanged += Rebuild;
            Rebuild();
        }

        private void LimitBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            _vm?.CommitPlotLimits();
            e.Handled = true;
        }

        private void LimitBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => _vm?.CommitPlotLimits();

        private void Rebuild()
        {
            Clear(FixedPart);
            Clear(CodesPart);
            if (_vm == null) return;

            var table = _vm.Table;
            var rowCount = HeaderRows + table.Rows.Count;
            AddRows(FixedPart, rowCount);
            AddRows(CodesPart, rowCount);

            BuildFixedPart(table);
            BuildCodesPart(table, _vm.PlotAreaM2);
        }

        private static void Clear(Grid g)
        {
            g.Children.Clear();
            g.RowDefinitions.Clear();
            g.ColumnDefinitions.Clear();
        }

        // Same SharedSizeGroup per row in both parts, so a row is as tall on both sides.
        private static void AddRows(Grid g, int count)
        {
            for (var r = 0; r < count; r++)
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto, SharedSizeGroup = "R" + r });
        }

        private void BuildFixedPart(AreaTable table)
        {
            var headers = new[] { "מפלס", "תת-קרקעי", "קומה" };
            for (var c = 0; c < headers.Length; c++)
            {
                FixedPart.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var cell = HeaderCell(Text(headers[c], bold: true), c, 0, rowSpan: HeaderRows);
                FixedPart.Children.Add(cell);
            }

            for (var i = 0; i < table.Rows.Count; i++)
            {
                var row = table.Rows[i];
                var r = HeaderRows + i;
                var summary = row.Kind != AreaTableRowKind.Floor;
                Add(FixedPart, DataCell(row.Elevation is { } e ? e.ToString("F2", CultureInfo.InvariantCulture) : "", true, summary), 0, r);
                Add(FixedPart, DataCell(row.Kind == AreaTableRowKind.Floor && row.IsUnderground ? "כן" : "", false, summary), 1, r);
                Add(FixedPart, DataCell(row.Label, false, summary), 2, r);
            }
        }

        private void BuildCodesPart(AreaTable table, double? plotAreaM2)
        {
            var bands = table.Bands;
            var gridColumn = new int[table.Columns.Count];
            var beforeLine = new HashSet<int>();   // table columns whose trailing edge is a double line
            var g = 0;
            for (var b = 0; b < bands.Count; b++)
            {
                var band = bands[b];
                if (b > 0)
                {
                    // A double line between bands: through the group header too when the group changes.
                    var newGroup = bands[b - 1].Group != band.Group;
                    CodesPart.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(DoubleLineWidth) });
                    var line = new Border
                    {
                        BorderBrush = Brush("Brush.Text"),
                        BorderThickness = new Thickness(1, 0, 1, 0),
                        Background = Brush("Brush.Bg"),
                    };
                    var top = newGroup ? 0 : 1;
                    Add(CodesPart, line, g, top, rowSpan: CodesPart.RowDefinitions.Count - top);
                    beforeLine.Add(band.Start - 1);
                    g++;
                }
                for (var i = band.Start; i < band.Start + band.Count; i++)
                {
                    CodesPart.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 48 });
                    gridColumn[i] = g++;
                }
            }

            // Row 0: one cell per group, across its bands (and the double lines inside it).
            foreach (var group in bands.GroupBy(b => b.Group))
            {
                var first = group.First();
                var last = group.Last();
                var from = gridColumn[first.Start];
                var to = gridColumn[last.Start + last.Count - 1];
                var name = Text(GroupName(first.Group), bold: true);
                name.Foreground = GroupBrush(first.Group);
                CodesPart.Children.Add(HeaderCell(name, from, 0, columnSpan: to - from + 1, trailing: !beforeLine.Contains(last.Start + last.Count - 1)));
            }

            // Row 1: "שימושים" over a group's codes, "סה\"כ" over its totals.
            foreach (var band in bands)
            {
                var from = gridColumn[band.Start];
                var lastColumn = band.Start + band.Count - 1;
                var text = Text(band.IsCodes ? "שימושים" : "סה\"כ", bold: false);
                CodesPart.Children.Add(HeaderCell(text, from, 1, columnSpan: gridColumn[lastColumn] - from + 1, trailing: !beforeLine.Contains(lastColumn)));
            }

            // Row 2: the column's own header.
            for (var i = 0; i < table.Columns.Count; i++)
                CodesPart.Children.Add(HeaderCell(ColumnHeader(table.Columns[i]), gridColumn[i], 2, trailing: !beforeLine.Contains(i)));

            for (var r = 0; r < table.Rows.Count; r++)
            {
                var row = table.Rows[r];
                var cells = table.Format(row, plotAreaM2);
                var summary = row.Kind != AreaTableRowKind.Floor;
                for (var i = 0; i < cells.Count; i++)
                    Add(CodesPart, DataCell(cells[i], true, summary, trailing: !beforeLine.Contains(i)), gridColumn[i], HeaderRows + r);
            }
        }

        private static void Add(Grid g, UIElement e, int column, int row, int rowSpan = 1)
        {
            Grid.SetColumn(e, column);
            Grid.SetRow(e, row);
            if (rowSpan > 1) Grid.SetRowSpan(e, rowSpan);
            g.Children.Add(e);
        }

        // Each cell draws its bottom edge and its trailing edge (the left, as the table flows right
        // to left); a cell next to a double line leaves its trailing edge to the line.
        private Border HeaderCell(UIElement content, int column, int row, int rowSpan = 1, int columnSpan = 1, bool trailing = true)
        {
            var cell = new Border
            {
                Child = content,
                Padding = new Thickness(5, 3, 5, 3),
                Background = Brush("Brush.GroupHeader"),
                BorderBrush = Brush("Brush.Border"),
                BorderThickness = new Thickness(0, 0, trailing ? 1 : 0, 1),
            };
            Grid.SetColumn(cell, column);
            Grid.SetRow(cell, row);
            if (rowSpan > 1) Grid.SetRowSpan(cell, rowSpan);
            if (columnSpan > 1) Grid.SetColumnSpan(cell, columnSpan);
            return cell;
        }

        // Every cell is centred. Number cells flow left to right, or "-4.90" would read "4.90-";
        // text cells keep the table's direction.
        private Border DataCell(string text, bool number, bool summary, bool trailing = true)
        {
            var block = new TextBlock
            {
                Text = text,
                TextAlignment = TextAlignment.Center,
                FontWeight = summary ? FontWeights.SemiBold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center,
            };
            if (number) block.FlowDirection = FlowDirection.LeftToRight;
            return new Border
            {
                Child = block,
                Padding = new Thickness(5, 2, 5, 2),
                Background = summary ? Brush("Brush.Panel") : null,
                BorderBrush = Brush("Brush.Border"),
                BorderThickness = new Thickness(0, 0, trailing ? 1 : 0, 1),
            };
        }

        private static TextBlock Text(string text, bool bold) => new TextBlock
        {
            Text = text,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };

        // Code and Hebrew name in separate TextBlocks, so bidi never reorders them. Long names wrap.
        private FrameworkElement ColumnHeader(AreaTableColumn c)
        {
            var panel = new StackPanel { ToolTip = Tip(c), VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center };
            if (c.Kind == AreaTableColumnKind.Code)
            {
                panel.Children.Add(new TextBlock { Text = c.Code!.Value.ToString(CultureInfo.InvariantCulture), FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center });
                panel.Children.Add(new TextBlock
                {
                    Text = UsageCatalog.ByCode(c.Code.Value)?.HebrewName ?? "",
                    FontSize = 10,
                    MaxWidth = 100,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    Foreground = Brush("Brush.TextMuted"),
                });
            }
            else
            {
                panel.Children.Add(new TextBlock { Text = c.Kind == AreaTableColumnKind.Sum ? "מ\"ר" : "% מהמגרש", TextAlignment = TextAlignment.Center });
            }
            return panel;
        }

        private static string? Tip(AreaTableColumn c) => c.Kind switch
        {
            AreaTableColumnKind.Code => c.Code + " - " + UsageCatalog.ByCode(c.Code!.Value)?.HebrewName,
            AreaTableColumnKind.Percent => "% משטח המגרש",
            _ => c.Group == UsageGroup.NoCode ? "שטחים ללא קוד שימוש" : null,
        };

        private static string GroupName(UsageGroup g) => g switch
        {
            UsageGroup.Primary => "שטחים עיקריים",
            UsageGroup.Service => "שטחי שירות",
            UsageGroup.Other => "אחר",
            UsageGroup.Process => "הורדה והריסה",
            _ => "ללא קוד",
        };

        private Brush Brush(string key) => (Brush)FindResource(key);

        private Brush GroupBrush(UsageGroup g) => Brush(g switch
        {
            UsageGroup.Primary => "Brush.UsagePrimary",
            UsageGroup.Service => "Brush.UsageService",
            UsageGroup.Other => "Brush.UsageOther",
            UsageGroup.Process => "Brush.UsageProcess",
            _ => "Brush.Warning",
        });
    }
}

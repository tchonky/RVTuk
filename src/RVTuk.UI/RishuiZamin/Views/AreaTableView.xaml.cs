using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using RVTuk.Core.RishuiZamin;
using RVTuk.UI.RishuiZamin.ViewModels;

namespace RVTuk.UI.RishuiZamin.Views
{
    /// <summary>
    /// The Area Calc window's Table tab: plot area and limits, the limits strip, and a grid of
    /// floors × used usage codes. DataContext is the window's <see cref="RishuiZaminViewModel"/>;
    /// the grid's columns depend on the codes in use, so they are rebuilt here whenever the view
    /// model raises <see cref="RishuiZaminViewModel.TableChanged"/>.
    /// </summary>
    public partial class AreaTableView : UserControl
    {
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

        /// <summary>One grid row: the floor columns plus the formatted cells.</summary>
        public sealed class Row
        {
            public Row(string elevation, string below, string label, bool isSummary, IReadOnlyList<string> cells)
            {
                Elevation = elevation;
                Below = below;
                Label = label;
                IsSummary = isSummary;
                Cells = cells;
            }

            public string Elevation { get; }
            public string Below { get; }
            public string Label { get; }
            public bool IsSummary { get; }
            public IReadOnlyList<string> Cells { get; }
        }

        private void Rebuild()
        {
            Grid.Columns.Clear();
            if (_vm == null)
            {
                Grid.ItemsSource = null;
                return;
            }

            var table = _vm.Table;
            Grid.Columns.Add(TextColumn(PlainHeader("מפלס"), nameof(Row.Elevation), number: true));
            Grid.Columns.Add(TextColumn(PlainHeader("תת-קרקעי"), nameof(Row.Below), number: false));
            Grid.Columns.Add(TextColumn(PlainHeader("קומה"), nameof(Row.Label), number: false));

            UsageGroup? previous = null;
            for (var i = 0; i < table.Columns.Count; i++)
            {
                var c = table.Columns[i];
                var first = c.Group != previous;
                previous = c.Group;
                var column = TextColumn(Header(c, first), "Cells[" + i + "]", number: true);
                Grid.Columns.Add(column);
            }

            Grid.ItemsSource = table.Rows.Select(r => new Row(
                r.Elevation is { } e ? e.ToString("F2", CultureInfo.InvariantCulture) : "",
                r.Kind == AreaTableRowKind.Floor && r.IsUnderground ? "כן" : "",
                r.Label,
                r.Kind != AreaTableRowKind.Floor,
                table.Format(r, _vm.PlotAreaM2))).ToList();
        }

        // The grid flows right to left like the official table. Number cells flow left to right,
        // or "-4.90" would read "4.90-"; text cells keep the grid's direction and start at the right.
        private static DataGridTextColumn TextColumn(object header, string path, bool number)
        {
            var style = new Style(typeof(TextBlock));
            if (number)
                style.Setters.Add(new Setter(FrameworkElement.FlowDirectionProperty, FlowDirection.LeftToRight));
            style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, number ? TextAlignment.Right : TextAlignment.Left));
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(4, 1, 4, 1)));
            var column = new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding(path),
                ElementStyle = style,
            };
            return column;
        }

        private static FrameworkElement PlainHeader(string text) =>
            new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Bottom };

        // Three separate TextBlocks — group, code, Hebrew name — so bidi never reorders them.
        // Long names wrap rather than being cut.
        private FrameworkElement Header(AreaTableColumn c, bool firstOfGroup)
        {
            var panel = new StackPanel { ToolTip = Tip(c), VerticalAlignment = VerticalAlignment.Bottom };
            panel.Children.Add(new TextBlock
            {
                Text = firstOfGroup ? GroupName(c.Group) : " ",
                FontSize = 10,
                MaxWidth = 110,
                TextWrapping = TextWrapping.Wrap,
                FontWeight = FontWeights.SemiBold,
                Foreground = GroupBrush(c.Group),
            });
            panel.Children.Add(new TextBlock
            {
                Text = c.Kind switch
                {
                    AreaTableColumnKind.Code => c.Code!.Value.ToString(CultureInfo.InvariantCulture),
                    AreaTableColumnKind.Sum => "סה\"כ",
                    _ => "%",
                },
                FontWeight = FontWeights.SemiBold,
            });
            panel.Children.Add(new TextBlock
            {
                Text = c.Kind == AreaTableColumnKind.Code ? UsageCatalog.ByCode(c.Code!.Value)?.HebrewName ?? "" : " ",
                FontSize = 10,
                MaxWidth = 110,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)FindResource("Brush.TextMuted"),
            });
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

        private Brush GroupBrush(UsageGroup g) => (Brush)FindResource(g switch
        {
            UsageGroup.Primary => "Brush.UsagePrimary",
            UsageGroup.Service => "Brush.UsageService",
            UsageGroup.Other => "Brush.UsageOther",
            UsageGroup.Process => "Brush.UsageProcess",
            _ => "Brush.Warning",
        });
    }
}

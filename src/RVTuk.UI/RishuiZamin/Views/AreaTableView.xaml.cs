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
            Grid.Columns.Add(TextColumn("Elev.", nameof(Row.Elevation), right: true));
            Grid.Columns.Add(TextColumn("Below", nameof(Row.Below), right: false));
            Grid.Columns.Add(TextColumn("Floor", nameof(Row.Label), right: false));

            UsageGroup? previous = null;
            for (var i = 0; i < table.Columns.Count; i++)
            {
                var c = table.Columns[i];
                var first = c.Group != previous;
                previous = c.Group;
                var column = TextColumn(Header(c, first), "Cells[" + i + "]", right: true);
                Grid.Columns.Add(column);
            }

            Grid.ItemsSource = table.Rows.Select(r => new Row(
                r.Elevation is { } e ? e.ToString("F2", CultureInfo.InvariantCulture) : "",
                r.Kind == AreaTableRowKind.Floor && r.IsUnderground ? "yes" : "",
                r.Label,
                r.Kind != AreaTableRowKind.Floor,
                table.Format(r, _vm.PlotAreaM2))).ToList();
        }

        private static DataGridTextColumn TextColumn(object header, string path, bool right)
        {
            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, right ? TextAlignment.Right : TextAlignment.Left));
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(4, 1, 4, 1)));
            var column = new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding(path),
                ElementStyle = style,
            };
            return column;
        }

        // Three separate TextBlocks — group, code, Hebrew name — so bidi never reorders them.
        private FrameworkElement Header(AreaTableColumn c, bool firstOfGroup)
        {
            var panel = new StackPanel { ToolTip = Tip(c) };
            panel.Children.Add(new TextBlock
            {
                Text = firstOfGroup ? GroupName(c.Group) : " ",
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                Foreground = GroupBrush(c.Group),
            });
            panel.Children.Add(new TextBlock
            {
                Text = c.Kind switch
                {
                    AreaTableColumnKind.Code => c.Code!.Value.ToString(CultureInfo.InvariantCulture),
                    AreaTableColumnKind.Sum => "Σ",
                    _ => "%",
                },
                FontWeight = FontWeights.SemiBold,
            });
            panel.Children.Add(new TextBlock
            {
                Text = c.Kind == AreaTableColumnKind.Code ? UsageCatalog.ByCode(c.Code!.Value)?.HebrewName ?? "" : " ",
                FontSize = 10,
                MaxWidth = 90,
                FlowDirection = FlowDirection.RightToLeft,   // so a long Hebrew name loses its end, not its start
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = (Brush)FindResource("Brush.TextMuted"),
            });
            return panel;
        }

        private static string? Tip(AreaTableColumn c) => c.Kind switch
        {
            AreaTableColumnKind.Code => c.Code + " - " + UsageCatalog.ByCode(c.Code!.Value)?.HebrewName,
            AreaTableColumnKind.Percent => "% of the plot area",
            _ => c.Group == UsageGroup.NoCode ? "Areas without a usage code" : null,
        };

        private static string GroupName(UsageGroup g) => g switch
        {
            UsageGroup.Primary => "PRIMARY",
            UsageGroup.Service => "SERVICE",
            UsageGroup.Other => "OTHER",
            UsageGroup.Process => "REMOVED",
            _ => "NO CODE",
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

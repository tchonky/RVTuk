using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RVTuk.Core.Models.Comparison;

namespace RVTuk.UI.Views
{
    /// <summary>Modal picker over saved Comparator project snapshots (not the Standard — that is
    /// selected directly from the Source Bar). Lets the user load one into Source A/B, or delete
    /// a stale one. No Revit access — the caller supplies the already-loaded list plus a delete
    /// delegate, same injection pattern as the rest of the Comparator.</summary>
    public partial class SnapshotPickerWindow : Window
    {
        private readonly Action<long> _deleteSnapshot;

        public ObservableCollection<SnapshotPickerRow> Items { get; }

        /// <summary>The picked snapshot's metadata, or null if the dialog was cancelled.</summary>
        public SnapshotMeta? SelectedMeta { get; private set; }

        public SnapshotPickerWindow(System.Collections.Generic.IReadOnlyList<SnapshotMeta> snapshots, Action<long> deleteSnapshot)
        {
            _deleteSnapshot = deleteSnapshot;
            Items = new ObservableCollection<SnapshotPickerRow>();
            foreach (var m in snapshots) Items.Add(new SnapshotPickerRow(m));

            InitializeComponent();
            DataContext = this;
        }

        private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            LoadButton.IsEnabled = List.SelectedItem is SnapshotPickerRow;
        }

        private void Load_Click(object sender, RoutedEventArgs e) => TryLoadSelected();

        private void List_MouseDoubleClick(object sender, MouseButtonEventArgs e) => TryLoadSelected();

        private void TryLoadSelected()
        {
            if (List.SelectedItem is SnapshotPickerRow row)
            {
                SelectedMeta = row.Meta;
                DialogResult = true;
            }
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: SnapshotPickerRow row })
            {
                _deleteSnapshot(row.Meta.Id);
                Items.Remove(row);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }

    /// <summary>Display-ready wrapper: pre-formats the ISO-8601 CapturedUtc for the grid so the
    /// XAML needs no converter.</summary>
    public class SnapshotPickerRow
    {
        public SnapshotMeta Meta { get; }
        public string Name => Meta.SourceName;
        public int RevitYear => Meta.RevitYear;
        public string CapturedDisplay { get; }

        public SnapshotPickerRow(SnapshotMeta meta)
        {
            Meta = meta;
            CapturedDisplay = DateTime.TryParse(meta.CapturedUtc, null, DateTimeStyles.RoundtripKind, out var dt)
                ? dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                : meta.CapturedUtc;
        }
    }
}

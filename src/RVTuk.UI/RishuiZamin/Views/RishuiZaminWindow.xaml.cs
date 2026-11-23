using System;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using RVTuk.Core.RishuiZamin;
using RVTuk.UI.RishuiZamin.ViewModels;

namespace RVTuk.UI.RishuiZamin.Views
{
    public partial class RishuiZaminWindow : Window
    {
        private readonly RishuiZaminViewModel _vm;

        public RishuiZaminWindow(RishuiZaminViewModel viewModel)
        {
            InitializeComponent();
            _vm = viewModel;
            DataContext = _vm;
            _vm.ExportCompleted += OnExportCompleted;
            _vm.AssignFailed += OnAssignFailed;
            _vm.ConfirmRunSetup = ConfirmRunSetup;
            Closed += (_, _) =>
            {
                _vm.ExportCompleted -= OnExportCompleted;
                _vm.AssignFailed -= OnAssignFailed;
                _vm.ConfirmRunSetup = null;
            };
            Loaded += (_, _) => _vm.RefreshCommand.Execute(null);
            _vm.AllRows.CollectionChanged += KeepScrollAcrossRefresh;
        }

        // A refresh (after every assignment) clears and refills the rows; put the list back where
        // the user was instead of jumping to the top.
        private void KeepScrollAcrossRefresh(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Reset) return;
            var scroller = FindChild<ScrollViewer>(AreaList);
            if (scroller == null) return;
            var offset = scroller.VerticalOffset;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => scroller.ScrollToVerticalOffset(offset)));
        }

        private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T found) return found;
                if (FindChild<T>(child) is { } deeper) return deeper;
            }

            return null;
        }

        // The rows' IsSelected is bound to the list items; tell the view model once per change
        // so it updates the detail strip and selects the same areas in Revit.
        private void AreaList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            _vm.OnSelectionChanged();

        private void NewUsageButton_Click(object sender, RoutedEventArgs e) =>
            OpenPicker(UsageKeySlot.New, NewUsageButton);

        private void OldUsageButton_Click(object sender, RoutedEventArgs e) =>
            OpenPicker(UsageKeySlot.Old, OldUsageButton);

        private void OpenPicker(UsageKeySlot slot, FrameworkElement target)
        {
            var shared = slot == UsageKeySlot.New ? _vm.Selection.New : _vm.Selection.Old;
            var picker = new UsagePickerViewModel(slot,
                _vm.SelectedRows.Select(r => r.Name).ToList(),
                shared.Kind == UsageValueKind.Code ? shared.Code : null,
                isMixed: shared.Kind == UsageValueKind.Mixed);
            var view = new UsagePickerPopup { DataContext = picker };
            picker.Picked += code =>
            {
                PickerPopup.IsOpen = false;
                _vm.Assign(slot, code);
            };
            view.CloseRequested += () => PickerPopup.IsOpen = false;

            PickerPopup.Child = view;
            PickerPopup.PlacementTarget = target;
            PickerPopup.IsOpen = true;   // WPF flips a Bottom popup above the target when there is no room below
            view.FocusSearch();
        }

        private bool ConfirmRunSetup(string message) =>
            MessageBox.Show(this, message + "\n\nYes = run Setup Usage Keys, then set the code.",
                "RVTuk – Area Calc", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

        private void OnAssignFailed(string message) =>
            MessageBox.Show(this, message, "RVTuk – Area Calc", MessageBoxButton.OK, MessageBoxImage.Warning);

        private void OnExportCompleted(bool ok, string message)
        {
            MessageBox.Show(this, message,
                ok ? "RVTuk – Export successful" : "RVTuk – Export blocked",
                MessageBoxButton.OK,
                ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
    }
}

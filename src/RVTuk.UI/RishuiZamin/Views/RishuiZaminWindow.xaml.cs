using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
                shared.Kind == UsageValueKind.Code ? shared.Code : null);
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

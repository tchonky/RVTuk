using System.Windows;
using RVTuk.UI.ViewModels;

namespace RVTuk.UI.Views
{
    public partial class AreaSubmissionWindow : Window
    {
        private readonly AreaSubmissionViewModel _vm;

        public AreaSubmissionWindow(AreaSubmissionViewModel viewModel)
        {
            InitializeComponent();
            _vm = viewModel;
            DataContext = _vm;
            _vm.ExportCompleted += OnExportCompleted;
            Closed += (_, _) => _vm.ExportCompleted -= OnExportCompleted;
        }

        // TreeView.SelectedItem isn't directly bindable; forward area-row selection to the VM
        // (which selects the element in the Revit model).
        private void AreaTree_SelectedItemChanged(object sender,
            RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is AreaRowViewModel row)
                _vm.SelectedRow = row;
        }

        private void OnExportCompleted(bool ok, string message)
        {
            MessageBox.Show(this, message,
                ok ? "RVTuk – Export successful" : "RVTuk – Export blocked",
                MessageBoxButton.OK,
                ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
    }
}

using System.Windows;
using System.Windows.Threading;
using RVTuk.UI.DwgExporter.ViewModels;

namespace RVTuk.UI.DwgExporter.Views
{
    public partial class DwgExportWindow : Window
    {
        private readonly DwgExportViewModel _vm;

        public DwgExportWindow(DwgExportViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;

            vm.ShowError = msg =>
                MessageBox.Show(this, msg, "DWG Export", MessageBoxButton.OK, MessageBoxImage.Warning);
            vm.ConfirmOverwrite = msg =>
                MessageBox.Show(this, msg, "DWG Export", MessageBoxButton.YesNo, MessageBoxImage.Question)
                    == MessageBoxResult.Yes;
            // The export loop runs synchronously on this thread (it must — it needs the
            // command's Revit API context); an empty Background-priority dispatcher hop
            // lets pending layout/render work run so the progress bar actually moves.
            vm.PumpUi = () =>
                Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Choose the DWG output folder",
                SelectedPath = _vm.OutputFolder,
            };
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                _vm.OutputFolder = dialog.SelectedPath;
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}

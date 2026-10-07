using System.Windows;
using RVTuk.UI.FamilyBrowser.ViewModels;

namespace RVTuk.UI.FamilyBrowser.Views
{
    public partial class IndexProgressWindow : Window
    {
        public IndexProgressViewModel ViewModel { get; }

        public IndexProgressWindow()
        {
            InitializeComponent();
            ViewModel = new IndexProgressViewModel();
            DataContext = ViewModel;
            // Closing the window (its X, or the browser that owns it closing) cancels the scan
            // rather than leaving it running with no UI.
            Closed += (_, __) => ViewModel.CancelCommand.Execute(null);
        }
    }
}

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
        }
    }
}

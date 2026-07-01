using System.Windows;
using RVTuk.UI.ViewModels;

namespace RVTuk.UI.Views
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

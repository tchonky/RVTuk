using System.Windows;
using ReviTchucky.UI.ViewModels;

namespace ReviTchucky.UI.Views
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

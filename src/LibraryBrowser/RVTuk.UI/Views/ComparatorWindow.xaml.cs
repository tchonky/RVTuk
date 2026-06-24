using System.Windows;
using RVTuk.UI.ViewModels;

namespace RVTuk.UI.Views
{
    public partial class ComparatorWindow : Window
    {
        public ComparatorWindow(ComparatorViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}

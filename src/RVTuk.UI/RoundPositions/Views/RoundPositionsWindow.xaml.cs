using System.Windows;
using RVTuk.UI.RoundPositions.ViewModels;

namespace RVTuk.UI.RoundPositions.Views
{
    public partial class RoundPositionsWindow : Window
    {
        public RoundPositionsWindow(RoundPositionsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private void OnRun(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}

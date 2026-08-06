using System.Windows;
using RVTuk.UI.RoundPositions.ViewModels;

namespace RVTuk.UI.RoundPositions.Views
{
    public partial class RoundPositionsResultWindow : Window
    {
        public RoundPositionsResultWindow(RoundPositionsResultViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}

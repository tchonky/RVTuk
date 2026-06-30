using System.Windows;
using RVTuk.UI.ViewModels;

namespace RVTuk.UI.Views
{
    public partial class ConfigWindow : Window
    {
        public ConfigWindow(ConfigViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}

using System.Windows;
using RVTuk.UI.ViewModels;

namespace RVTuk.UI.Views
{
    public partial class SettingsWindow : Window
    {
        public SettingsViewModel ViewModel { get; }

        public SettingsWindow()
        {
            InitializeComponent();
            ViewModel = new SettingsViewModel();
            ViewModel.RequestClose += Close;
            DataContext = ViewModel;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    }
}

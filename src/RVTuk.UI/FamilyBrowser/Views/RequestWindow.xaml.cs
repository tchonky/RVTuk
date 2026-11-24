using System.Windows;
using System.Windows.Controls;
using RVTuk.UI.FamilyBrowser.ViewModels;

namespace RVTuk.UI.FamilyBrowser.Views
{
    /// <summary>Asks for a fix to a family, or for a new family. Send stays off until there is
    /// text; Enter makes a new line (requests are often several lines), so Send is a click.</summary>
    public partial class RequestWindow : Window
    {
        public RequestDialogResult? Result { get; private set; }

        public RequestWindow(RequestDialogInput input)
        {
            InitializeComponent();
            if (input.IsFix)
            {
                Title = "RVTuk — Request a fix — " + input.FamilyName;
                PromptLabel.Content = "WHAT NEEDS FIXING OR CHANGING?";
                Placeholder.Text = "e.g. the swing is wrong in plan, a size is missing, a parameter does not work…";
                AttachBox.IsEnabled = input.CanAttach;
                AttachHint.Text = input.CanAttach
                    ? "Send it if you already changed this family in your project, so the maintainer can see your change."
                    : "Available when this family is loaded in the open project (press ⟳ first).";
            }
            else
            {
                Title = "RVTuk — Request a new family";
                PromptLabel.Content = "WHAT FAMILY DO YOU NEED?";
                Placeholder.Text = "Category, sizes, where you will use it, a manufacturer link…";
                AttachBox.Visibility = Visibility.Collapsed;
            }
            Loaded += (_, __) => RequestText.Focus();
        }

        private void RequestText_TextChanged(object sender, TextChangedEventArgs e)
        {
            var text = RequestText.Text;
            Placeholder.Visibility = text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            SendButton.IsEnabled = text.Trim().Length > 0;
            RequestText.FlowDirection = RequestItemViewModel.StartsRightToLeft(text)
                ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        }

        private void Send_Click(object sender, RoutedEventArgs e)
        {
            Result = new RequestDialogResult
            {
                Text = RequestText.Text.Trim(),
                Attach = AttachBox.IsEnabled && AttachBox.IsChecked == true,
            };
            DialogResult = true;
        }
    }
}

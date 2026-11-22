using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using RVTuk.UI.RishuiZamin.ViewModels;

namespace RVTuk.UI.RishuiZamin.Views
{
    /// <summary>The usage picker shown in a popup under a New/Old usage button. DataContext is a
    /// <see cref="UsagePickerViewModel"/>. ↑/↓ move, Enter picks, Esc closes.</summary>
    public partial class UsagePickerPopup : UserControl
    {
        public UsagePickerPopup()
        {
            InitializeComponent();
        }

        /// <summary>Esc was pressed.</summary>
        public event Action? CloseRequested;

        private UsagePickerViewModel? Picker => DataContext as UsagePickerViewModel;

        /// <summary>Puts the caret in the search box once the popup is showing.</summary>
        public void FocusSearch() =>
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                Search.Focus();
                Keyboard.Focus(Search);
            }));

        private void Search_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Picker is not { } picker) return;
            switch (e.Key)
            {
                case Key.Down:
                    picker.Move(1);
                    break;
                case Key.Up:
                    picker.Move(-1);
                    break;
                case Key.Enter:
                    picker.PickHighlighted();
                    break;
                case Key.Escape:
                    CloseRequested?.Invoke();
                    break;
                default:
                    return;
            }

            e.Handled = true;
            if (picker.Highlighted is { } item) List.ScrollIntoView(item);
        }

        private void List_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var element = e.OriginalSource as DependencyObject;
            while (element != null && element is not ListBoxItem)
                element = VisualTreeHelper.GetParent(element);
            if (element is ListBoxItem { DataContext: PickerItem item } && Picker is { } picker)
            {
                picker.Pick(item);
                e.Handled = true;
            }
        }
    }
}

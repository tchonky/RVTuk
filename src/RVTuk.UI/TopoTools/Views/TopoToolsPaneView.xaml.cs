using System.Windows.Controls;
using System.Windows.Input;

namespace RVTuk.UI.TopoTools.Views
{
    public partial class TopoToolsPaneView : UserControl
    {
        public TopoToolsPaneView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Enter commits the box under the caret.
        ///
        /// The boxes update their binding on lost focus, which they have to: committing per
        /// keystroke would open a Revit transaction for every character typed. That left Enter —
        /// the key everyone reaches for after typing a number — doing nothing at all, so it is
        /// wired to push the value through by hand.
        /// </summary>
        private void OnEditorKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            if (sender is not TextBox box) return;

            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            e.Handled = true;
        }
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

        /// <summary>
        /// Keeps the wheel alive over the height-capped inner lists. <c>ScrollViewer</c> marks the
        /// wheel handled whenever it has scroll info — even with nothing to scroll — so without this
        /// the wheel would die over every element list, including the common 1–4 element group that
        /// never scrolls at all, and over a long group once it hits its end. Those rows cover most
        /// of the window, and the rows are plain TextBlocks with no keyboard route as a fallback.
        /// </summary>
        private void InnerList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var inner = (ScrollViewer)sender;

            // A negative delta scrolls down. The inner list keeps the wheel only while it can still
            // move in the direction asked for.
            var atLimit = e.Delta < 0
                ? inner.VerticalOffset >= inner.ScrollableHeight
                : inner.VerticalOffset <= 0;
            if (inner.ScrollableHeight > 0 && !atLimit) return;

            if (VisualTreeHelper.GetParent(inner) is not UIElement parent) return;

            // Re-raise rather than call the outer viewer directly: the outer ScrollViewer is found
            // by the normal bubbling route, so this stays correct if the layout around it changes.
            e.Handled = true;
            parent.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = MouseWheelEvent,
                Source = inner,
            });
        }
    }
}

using System.Windows.Controls;
using RVTuk.UI.AutoDimensions.ViewModels;

namespace RVTuk.UI.AutoDimensions.Views
{
    public partial class AutoDimensionsPaneView : UserControl
    {
        public AutoDimensionsPaneView()
        {
            InitializeComponent();

            // The pane instance outlives any one document, so its content is re-read every time
            // it becomes visible — Revit restoring it at startup, the user tabbing back to it, or
            // the ribbon command showing it. Refresh returns immediately and no-ops while busy.
            IsVisibleChanged += (_, _) =>
            {
                if (IsVisible && DataContext is AutoDimensionsPaneViewModel viewModel)
                    viewModel.Refresh();
            };
        }
    }
}

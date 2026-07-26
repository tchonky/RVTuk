using System;
using Autodesk.Revit.UI;
using RVTuk.Revit.NeoProperties;
using RVTuk.UI.AutoDimensions.Views;

namespace RVTuk.Revit.AutoDimensions
{
    /// <summary>
    /// Registers the scope pane tabbed alongside Neo Properties — both are utility panes, and
    /// tabbing avoids adding a second permanent dock slot.
    /// </summary>
    public class AutoDimensionsPaneProvider : IDockablePaneProvider
    {
        public static readonly DockablePaneId PaneId =
            new DockablePaneId(new Guid("e5d47b31-2c8a-4f16-b0d9-73a5e91c46f2"));

        private readonly AutoDimensionsPaneView _view;

        public AutoDimensionsPaneProvider(AutoDimensionsPaneView view)
        {
            _view = view;
        }

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            data.FrameworkElement = _view;
            data.InitialState = new DockablePaneState
            {
                DockPosition = DockPosition.Tabbed,
                TabBehind = NeoPropertiesPaneProvider.PaneId,
            };
        }
    }
}

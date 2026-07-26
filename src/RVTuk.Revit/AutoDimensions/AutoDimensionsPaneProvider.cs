using System;
using Autodesk.Revit.UI;
using RVTuk.UI.AutoDimensions.Views;

namespace RVTuk.Revit.AutoDimensions
{
    /// <summary>
    /// Registers the Auto Dimensions pane docked to the right.
    ///
    /// Deliberately NOT tabbed behind another custom pane: Revit only creates a registered
    /// custom pane the first time it is shown, so tabbing behind one that has never been shown
    /// leaves the pane with nowhere to go and Show() silently does nothing. TabBehind is only
    /// dependable against DockablePanes.BuiltInDockablePanes, which always exist.
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
                DockPosition = DockPosition.Right,
            };
        }
    }
}

using System;
using Autodesk.Revit.UI;
using RVTuk.UI.RoundPositions.Views;

namespace RVTuk.Revit.RoundPositions
{
    /// <summary>
    /// Registers the Round Positions pane docked to the right.
    ///
    /// Deliberately NOT tabbed behind another custom pane: Revit only creates a registered custom
    /// pane the first time it is shown, so tabbing behind one that has never been shown leaves the
    /// pane with nowhere to go and Show() silently does nothing.
    /// </summary>
    public class RoundPositionsPaneProvider : IDockablePaneProvider
    {
        public static readonly DockablePaneId PaneId =
            new DockablePaneId(new Guid("b7a1c94e-3f52-4d08-9c6b-1e84a5f2d370"));

        private readonly RoundPositionsPaneView _view;

        public RoundPositionsPaneProvider(RoundPositionsPaneView view)
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

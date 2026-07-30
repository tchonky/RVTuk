using System;
using Autodesk.Revit.UI;
using RVTuk.UI.TopoTools.Views;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// Registers the Topo Tools pane docked to the right.
    ///
    /// Deliberately NOT tabbed behind another custom pane: Revit only creates a registered custom
    /// pane the first time it is shown, so tabbing behind one that has never been shown leaves the
    /// pane with nowhere to go and Show() silently does nothing.
    /// </summary>
    public class TopoToolsPaneProvider : IDockablePaneProvider
    {
        public static readonly DockablePaneId PaneId =
            new DockablePaneId(new Guid("a17c46e9-5b83-4d20-8f6a-9c4e2b70d135"));

        private readonly TopoToolsPaneView _view;

        public TopoToolsPaneProvider(TopoToolsPaneView view)
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

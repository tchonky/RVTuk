using System;
using Autodesk.Revit.UI;
using RVTuk.UI.NeoProperties.Views;

namespace RVTuk.Revit.NeoProperties
{
    public class NeoPropertiesPaneProvider : IDockablePaneProvider
    {
        public static readonly DockablePaneId PaneId =
            new DockablePaneId(new Guid("88a9c4fa-9c6a-4b02-b169-701be8358090"));

        private readonly NeoPropertiesView _view;

        public NeoPropertiesPaneProvider(NeoPropertiesView view)
        {
            _view = view;
        }

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            data.FrameworkElement = _view;
            data.InitialState = new DockablePaneState
            {
                DockPosition = DockPosition.Tabbed,
                TabBehind = DockablePanes.BuiltInDockablePanes.PropertiesPalette
            };
        }
    }
}

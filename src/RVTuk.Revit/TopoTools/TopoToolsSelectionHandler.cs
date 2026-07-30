using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.UI.Events;
using RVTuk.UI.TopoTools.ViewModels;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// Mirrors Revit's selection into the pane, so picking a topo line in the view highlights its
    /// row. With the height stored invisibly rather than in Properties, this is what makes a row
    /// labelled "Line 418732" identifiable at all.
    ///
    /// Follows NeoPropertiesSelectionHandler: SelectionChanged arrives on Revit's main thread, which
    /// is also the pane's dispatcher thread, so the view model is updated directly.
    /// </summary>
    public static class TopoToolsSelectionHandler
    {
        public static TopoToolsPaneViewModel? ViewModel { get; set; }

        public static void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            var vm = ViewModel;
            if (vm == null) return;

            IReadOnlyCollection<long> ids;
            try
            {
                ids = e.GetSelectedElements().Select(id => id.Value).ToList();
            }
            catch
            {
                ids = new List<long>();
            }

            vm.SetSelectedLineIds(ids);
        }
    }
}

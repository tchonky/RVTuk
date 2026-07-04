using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Events;
using RVTuk.Core.NeoProperties;
using RVTuk.UI.ViewModels;

namespace RVTuk.Revit.NeoProperties
{
    public static class NeoPropertiesSelectionHandler
    {
        public static NeoPropertiesViewModel? ViewModel { get; set; }

        public static void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            var vm = ViewModel;
            if (vm == null) return;

            var ids = e.GetSelectedElements();
            if (ids.Count == 0)
            {
                vm.ShowNoSelection();
                return;
            }
            if (ids.Count > 1)
            {
                vm.ShowMultipleSelection();
                return;
            }

            var document = e.GetDocument();
            var element = document.GetElement(ids.First());
            if (element == null)
            {
                vm.ShowNoSelection();
                return;
            }

            var entries = ExtractParameterEntries(element);
            vm.ShowParameters(ParameterOrderer.Order(entries));
        }

        private static IReadOnlyList<ParameterEntry> ExtractParameterEntries(Element element)
        {
            var entries = new List<ParameterEntry>();
            foreach (Parameter parameter in element.GetOrderedParameters())
            {
                try
                {
                    var groupName = LabelUtils.GetLabelForGroup(parameter.Definition.GetGroupTypeId());
                    var name = parameter.Definition.Name;
                    var value = parameter.AsValueString() ?? parameter.AsString() ?? "";
                    entries.Add(new ParameterEntry(groupName, name, value));
                }
                catch
                {
                    // A malformed/inaccessible parameter shouldn't blank the whole pane.
                }
            }
            return entries;
        }
    }
}

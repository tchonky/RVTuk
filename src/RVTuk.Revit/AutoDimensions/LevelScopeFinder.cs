using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RVTuk.Core.AutoDimensions;

namespace RVTuk.Revit.AutoDimensions
{
    /// <summary>
    /// Discovers, per level, which of its plan views can act as the reference view (owns at least
    /// one _DP-Dim Outer or _DP-Dim Inner detail line) and which views can receive the fanned-out
    /// dimensions. A view holding only ref lines is not a reference view: ref lines with no
    /// string to join produce nothing. Views with no level — 3D, sections, drafting views,
    /// schedules, sheets — are never candidates for either role.
    /// </summary>
    public static class LevelScopeFinder
    {
        public static IReadOnlyList<LevelScope> Find(Document doc)
        {
            var planViews = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .Where(v => !v.IsTemplate && v.GenLevel != null)
                .ToList();

            // Document-wide, filtered by OwnerViewId — see DimensionRunner.CollectReferenceLines
            // for why a view-scoped collector would miss template-hidden lines.
            var viewsOwningReferenceLines = new HashSet<ElementId>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(CurveElement))
                    .Cast<CurveElement>()
                    .OfType<DetailLine>()
                    .Where(l => DimensionLineStyle.TryGetRing(l, out _))
                    .Select(l => l.OwnerViewId));

            var scopes = new List<(double Elevation, LevelScope Scope)>();
            foreach (var group in planViews.GroupBy(v => v.GenLevel.Id))
            {
                if (doc.GetElement(group.Key) is not Level level) continue;

                // Ascending ElementId: arbitrary but deterministic, per the spec — a level is only
                // expected to hold one reference view, and the first found wins if it holds more.
                // The reference view is chosen from THIS order; the tree is sorted for display
                // afterwards, so renaming a view can never change which one is the reference.
                var views = group.OrderBy(v => v.Id.Value).ToList();
                var referenceView = views.FirstOrDefault(v => viewsOwningReferenceLines.Contains(v.Id));

                scopes.Add((level.Elevation, new LevelScope(
                    level.Id.Value,
                    level.Name,
                    referenceView?.Id.Value,
                    ScopeViewOrder.Sort(views.Select(v => new ScopeViewInfo(v.Id.Value, v.Name))))));
            }

            return scopes
                .OrderBy(s => s.Elevation)
                .Select(s => s.Scope)
                .ToList();
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RVTuk.Core.WallFinishes;
using RVTuk.Revit.Shared;

namespace RVTuk.Revit.WallFinishes.Commands
{
    /// <summary>
    /// Wall Finishes: in the active floor plan, a detail line 10 cm off every exposed wall face,
    /// on a line style named after the face's finish, then the legend view rebuilt. Re-running
    /// replaces this view's lines. One undo step; runs in the command's API context.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class WallFinishesCommand : IExternalCommand
    {
        private const string Title = "Wall Finishes";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument.Document;

            if (!(doc.ActiveView is ViewPlan view) || view.GenLevel == null)
            {
                TaskDialog.Show(Title, "Open a floor plan first: Wall Finishes draws in a plan view.");
                return Result.Cancelled;
            }

            var outline = FinishOutline.Read(doc, view);
            if (outline.Loops.Count == 0)
            {
                TaskDialog.Show(Title, "No walls are cut in this view.");
                return Result.Cancelled;
            }

            // Detail lines lie in the view's plane, at its level.
            var flatten = Transform.CreateTranslation(new XYZ(0, 0, view.GenLevel.ProjectElevation));
            int failedLoops = 0;
            var lines = new List<FinishEdge>();
            foreach (var loop in outline.Loops)
            {
                var offset = FinishOffset.Apply(loop, out bool failed);
                if (failed) failedLoops++;
                lines.AddRange(offset.Where(e => !e.Skip && e.MaterialId != ElementId.InvalidElementId));
            }

            using (var tx = new Transaction(doc, Title))
            {
                tx.Start();

                var old = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(CurveElement))
                    .Cast<CurveElement>()
                    .Where(c => c.OwnerViewId == view.Id && c.LineStyle is GraphicsStyle s && FinishStyleName.IsFinishStyle(s.Name))
                    .Select(c => c.Id)
                    .ToList();
                if (old.Count > 0) doc.Delete(old);

                var styles = new Dictionary<ElementId, GraphicsStyle?>();
                foreach (var line in lines)
                {
                    if (!styles.TryGetValue(line.MaterialId, out var style))
                    {
                        style = styles[line.MaterialId] = StyleFor(doc, line.MaterialId);
                    }
                    if (style == null) continue;

                    var curve = line.Edge.CreateTransformed(
                        flatten.Multiply(Transform.CreateTranslation(new XYZ(0, 0, -line.Edge.GetEndPoint(0).Z))));
                    doc.Create.NewDetailCurve(view, curve).LineStyle = style;
                }

                FinishLegend.Rebuild(doc);
                tx.Commit();
            }

            var report = new List<string>();
            if (outline.UsedFallback)
                report.Add("The doors and windows could not be set aside (another user may have them borrowed), so the lines break at each opening.");
            if (outline.BooleanFailures > 0)
                report.Add($"{outline.BooleanFailures} wall(s) could not be merged with their neighbours and were drawn on their own.");
            if (outline.EdgesWithoutMaterial > 0)
                report.Add($"{outline.EdgesWithoutMaterial} face(s) have no material and got no line.");
            if (failedLoops > 0)
                report.Add($"{failedLoops} outline(s) could not be offset 10 cm (a gap narrower than 20 cm) and got no line.");
            if (report.Count > 0) TaskDialog.Show(Title, string.Join("\n\n", report));

            return Result.Succeeded;
        }

        private static GraphicsStyle? StyleFor(Document doc, ElementId materialId)
        {
            if (!(doc.GetElement(materialId) is Material material)) return null;
            var name = FinishStyleName.For(material.Name);
            LineStyleCreator.EnsureExists(doc, name, material.Color.IsValid ? material.Color : null);
            return doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines)
                .SubCategories.get_Item(name)
                ?.GetGraphicsStyle(GraphicsStyleType.Projection);
        }
    }
}

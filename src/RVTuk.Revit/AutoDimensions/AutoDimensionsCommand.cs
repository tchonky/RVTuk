using System;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RVTuk.Core.AutoDimensions;

namespace RVTuk.Revit.AutoDimensions
{
    [Transaction(TransactionMode.Manual)]
    public class AutoDimensionsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                return Run(commandData, ref message);
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("RVTuk – Auto Dimensions (error)", ex.ToString());
                return Result.Failed;
            }
        }

        private static Result Run(ExternalCommandData commandData, ref string message)
        {
            var uiApp = commandData.Application;
            var uiDoc = uiApp.ActiveUIDocument;
            if (uiDoc == null)
            {
                message = "No active document.";
                return Result.Failed;
            }

            var doc = uiDoc.Document;
            var view = uiDoc.ActiveView;

            var created = 0;
            var skipped = 0;

            using (var tx = new Transaction(doc, "Auto Dimensions"))
            {
                tx.Start();

                try
                {
                    RunInTransaction(doc, view, ref created, ref skipped);
                }
                catch
                {
                    tx.RollBack();
                    throw;
                }

                tx.Commit();
            }

            ShowSummary(created, skipped);
            return Result.Succeeded;
        }

        private static void RunInTransaction(Document doc, View view, ref int created, ref int skipped)
        {
            DimensionLineStyle.EnsureExists(doc);

            var referenceLines = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(DetailLine))
                .Cast<DetailLine>()
                .Where(DimensionLineStyle.IsDimensionsLine)
                .ToList();

            var straightWalls = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(Wall))
                .Cast<Wall>()
                .Where(w => (w.Location as LocationCurve)?.Curve is Line)
                .ToList();

            foreach (var line in referenceLines)
            {
                var tracked = AutoDimensionTracker.TryGetTrackedDimension(line, view.Id);
                if (tracked != null)
                {
                    doc.Delete(tracked.Id);
                }

                if (line.GeometryCurve is not Line geometryLine)
                {
                    skipped++;
                    continue;
                }

                var lineStart = ToXyPoint(geometryLine.GetEndPoint(0));
                var lineEnd = ToXyPoint(geometryLine.GetEndPoint(1));

                var candidates = straightWalls
                    .Select(w => (Line)((LocationCurve)w.Location).Curve)
                    .Select(c => new WallCandidate(ToXyPoint(c.GetEndPoint(0)), ToXyPoint(c.GetEndPoint(1))))
                    .ToList();

                var crossingIndices = WallCrossingFinder.FindCrossingIndices(lineStart, lineEnd, candidates);
                if (crossingIndices.Count == 0)
                {
                    skipped++;
                    continue;
                }

                var referenceArray = new ReferenceArray();
                var anyFaceFound = false;
                foreach (var index in crossingIndices)
                {
                    var wall = straightWalls[index];
                    var exteriorFaces = HostObjectUtils.GetSideFaces(wall, ShellLayerType.Exterior);
                    var interiorFaces = HostObjectUtils.GetSideFaces(wall, ShellLayerType.Interior);
                    if (exteriorFaces.Count == 0 || interiorFaces.Count == 0) continue;

                    referenceArray.Append(exteriorFaces[0]);
                    referenceArray.Append(interiorFaces[0]);
                    anyFaceFound = true;
                }

                if (!anyFaceFound)
                {
                    skipped++;
                    continue;
                }

                var dimension = doc.Create.NewDimension(view, geometryLine, referenceArray);
                AutoDimensionTracker.SetTrackedDimension(line, dimension.Id);
                created++;
            }
        }

        private static void ShowSummary(int created, int skipped)
        {
            var summary = new StringBuilder();
            if (created == 0 && skipped == 0)
            {
                summary.Append("No Dimensions_Line lines found — the line style now exists in " +
                    "this project; draw reference lines and run again.");
            }
            else
            {
                summary.Append($"{created} dimension(s) created, {skipped} line(s) skipped " +
                    "(no walls found).");
            }
            TaskDialog.Show("RVTuk – Auto Dimensions", summary.ToString());
        }

        private static XyPoint ToXyPoint(XYZ point) => new(point.X, point.Y);
    }
}

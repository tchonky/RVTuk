using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RVTuk.Core.AreaSubmission;

namespace RVTuk.Revit.AreaSubmission
{
    /// <summary>
    /// One extracted Revit Area: its element id (for later selection/highlighting in Revit)
    /// paired with the Core-level <see cref="AreaRecord"/> built from it.
    /// </summary>
    public class ExtractedArea
    {
        public long ElementId { get; set; }
        public AreaRecord Record { get; set; } = new();
    }

    /// <summary>
    /// Reads Revit <see cref="Area"/> elements from the Area Plan view(s) placed on the currently
    /// active sheet, converting each into an <see cref="AreaRecord"/> (feet -> centimetres for
    /// boundary geometry; feet^2 -> metres^2 for area) plus its Revit <see cref="ElementId"/>.
    ///
    /// NOTE: this touches Revit API surface directly and can only be runtime-verified inside Revit.
    /// </summary>
    public class AreaExtractor
    {
        // Revit internal units are feet; the DXF writer (Task 5) works in centimetres.
        private const double FeetToCm = 30.48;
        private const double FeetToM = 0.3048;
        private const double SqFeetToSqM = 0.09290304;

        public IReadOnlyList<ExtractedArea> FromOpenSheet(UIDocument uidoc)
        {
            var result = new List<ExtractedArea>();

            if (uidoc?.ActiveView is not ViewSheet sheet)
            {
                return result;
            }

            var doc = uidoc.Document;

            foreach (var plan in GetAreaPlans(doc, sheet))
            {
                var areas = new FilteredElementCollector(doc, plan.Id)
                    .OfCategory(BuiltInCategory.OST_Areas)
                    .WhereElementIsNotElementType()
                    .Cast<Area>();

                foreach (var area in areas)
                {
                    // Skip unplaced/unbounded areas: Area.Area is 0 (or the area has no
                    // Location) when it hasn't been placed in the model.
                    if (area.Area <= 0 || area.Location == null)
                    {
                        continue;
                    }

                    var record = BuildRecord(doc, area);
                    result.Add(new ExtractedArea
                    {
                        ElementId = Raw(area.Id),
                        Record = record,
                    });
                }
            }

            return result;
        }

        private static IEnumerable<ViewPlan> GetAreaPlans(Document doc, ViewSheet sheet)
        {
            foreach (var vpId in sheet.GetAllViewports())
            {
                if (doc.GetElement(vpId) is not Viewport viewport)
                {
                    continue;
                }

                if (doc.GetElement(viewport.ViewId) is ViewPlan plan && plan.ViewType == ViewType.AreaPlan)
                {
                    yield return plan;
                }
            }
        }

        private static AreaRecord BuildRecord(Document doc, Area area)
        {
            var level = doc.GetElement(area.LevelId) as Level;
            var levelName = level?.Name ?? "";

            var record = new AreaRecord
            {
                Level = levelName,
                Floor = levelName,
                ElevationMeters = (level?.Elevation ?? 0.0) * FeetToM,
                Number = area.Number,
                Name = area.Name,
                UsageCode = ReadUsageCode(area),
                IsUnderground = level != null && level.Elevation < 0,
                // Single-sheet v1: the open sheet is always page 1 (Open item for multi-sheet).
                PageNo = 1,
                AreaValue = area.Area * SqFeetToSqM,
                BoundaryLoops = GetBoundaryLoops(area),
            };

            record.Errors = AreaValidator.CheckArea(record);
            return record;
        }

        private static int? ReadUsageCode(Area area)
        {
            var p = area.LookupParameter("RZ_UsageType");
            if (p == null || p.StorageType != StorageType.Integer || !p.HasValue)
            {
                return null;
            }

            return p.AsInteger();
        }

        private static List<List<Point2D>> GetBoundaryLoops(Area area)
        {
            var loops = new List<List<Point2D>>();
            var options = new SpatialElementBoundaryOptions();
            var boundaries = area.GetBoundarySegments(options);

            if (boundaries == null)
            {
                return loops;
            }

            foreach (var loop in boundaries)
            {
                var points = new List<Point2D>();
                foreach (var segment in loop)
                {
                    var curve = segment.GetCurve();
                    if (curve == null)
                    {
                        continue;
                    }

                    var pt = curve.GetEndPoint(0);
                    points.Add(new Point2D
                    {
                        X = pt.X * FeetToCm,
                        Y = pt.Y * FeetToCm,
                    });
                }

                if (points.Count > 0)
                {
                    loops.Add(points);
                }
            }

            return loops;
        }

        /// <summary>ElementId raw value. ElementId.Value (long) exists in Revit 2024+.</summary>
        private static long Raw(ElementId id)
        {
            return id.Value;
        }
    }
}

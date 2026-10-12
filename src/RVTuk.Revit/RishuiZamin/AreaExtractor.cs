using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RVTuk.Core.RishuiZamin;

namespace RVTuk.Revit.RishuiZamin
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

    /// <summary>Counts gathered while walking the open sheet's area plans, used to explain a
    /// zero-area result (no area plans on the sheet vs. areas present but all unplaced), plus
    /// the sheet's physical (paper) size read from its title block.</summary>
    public struct ExtractDiagnostics
    {
        public int AreaPlanCount { get; set; }
        public int RawAreaCount { get; set; }

        /// <summary>The title block's physical size in paper centimetres (0 when no title block
        /// is placed). Multiplied by the submission scale this gives the real-world RZ_FRAME
        /// size in drawing units.</summary>
        public double SheetPaperWidthCm { get; set; }
        public double SheetPaperHeightCm { get; set; }
    }

    /// <summary>
    /// Reads Revit <see cref="Area"/> elements from the Area Plan view(s) placed on the currently
    /// active sheet, converting each into an <see cref="AreaRecord"/> (feet -> centimetres for
    /// boundary geometry; feet^2 -> metres^2 for area) plus its Revit <see cref="ElementId"/>.
    ///
    /// Boundary points are mapped through Revit's own model-to-sheet transforms, so a polygon
    /// lands at the same position on the DXF sheet as Revit draws it on the Revit sheet — not
    /// at the area's raw (and possibly huge, shared-coordinate) model position. The sheet's own
    /// title block corner is the (0,0) reference.
    ///
    /// NOTE: this touches Revit API surface directly and can only be runtime-verified inside Revit.
    /// </summary>
    public class AreaExtractor
    {
        // Revit internal units are feet; the DXF writer (Task 5) works in centimetres.
        private const double FeetToCm = 30.48;
        private const double FeetToM = 0.3048;
        private const double SqFeetToSqM = 0.09290304;

        // A level modelled a hair below zero (e.g. -0.0001 ft) is still the ground floor, not
        // an underground one; match the "-0.00 formats as 0.00" threshold instead.
        private const double UndergroundThresholdM = -0.005;

        public IReadOnlyList<ExtractedArea> FromOpenSheet(UIDocument uidoc)
            => FromOpenSheet(uidoc, out _);

        /// <param name="diagnostics">Counts gathered along the way, so callers can tell "no area
        /// plans on the sheet" apart from "area plans present but every Area is unplaced."</param>
        public IReadOnlyList<ExtractedArea> FromOpenSheet(UIDocument uidoc, out ExtractDiagnostics diagnostics)
        {
            var result = new List<ExtractedArea>();
            diagnostics = new ExtractDiagnostics();

            if (uidoc?.ActiveView is not ViewSheet sheet)
            {
                return result;
            }

            var doc = uidoc.Document;
            var sheetOrigin = GetSheetFrame(doc, sheet, out var sheetPaperSize);
            diagnostics.SheetPaperWidthCm = sheetPaperSize.U * FeetToCm;
            diagnostics.SheetPaperHeightCm = sheetPaperSize.V * FeetToCm;

            foreach (var (viewport, plan) in GetAreaPlanViewports(doc, sheet))
            {
                diagnostics.AreaPlanCount++;

                var mapper = new ViewportMapper(plan, viewport, sheetOrigin);

                var areas = new FilteredElementCollector(doc, plan.Id)
                    .OfCategory(BuiltInCategory.OST_Areas)
                    .WhereElementIsNotElementType()
                    .Cast<Area>();

                foreach (var area in areas)
                {
                    diagnostics.RawAreaCount++;

                    // Skip unplaced/unbounded areas: Area.Area is 0 (or the area has no
                    // Location) when it hasn't been placed in the model.
                    if (area.Area <= 0 || area.Location == null)
                    {
                        continue;
                    }

                    var record = BuildRecord(doc, area, mapper);
                    result.Add(new ExtractedArea
                    {
                        ElementId = Raw(area.Id),
                        Record = record,
                    });
                }
            }

            return result;
        }

        private static IEnumerable<(Viewport Viewport, ViewPlan Plan)> GetAreaPlanViewports(Document doc, ViewSheet sheet)
        {
            foreach (var vpId in sheet.GetAllViewports())
            {
                if (doc.GetElement(vpId) is not Viewport viewport)
                {
                    continue;
                }

                if (doc.GetElement(viewport.ViewId) is ViewPlan plan && plan.ViewType == ViewType.AreaPlan)
                {
                    yield return (viewport, plan);
                }
            }
        }

        /// <summary>
        /// The sheet's own (0,0) reference for the export: the title block instance's bounding
        /// box corner (the physical lower-left of the paper), so exported positions read as
        /// "where on this sheet" rather than in Revit's raw, possibly huge, shared coordinates.
        /// Also reports the title block's paper size (feet), which becomes the RZ_FRAME — the
        /// real sheet outline — once multiplied by the submission scale. Falls back to the
        /// sheet's native origin / zero size if no title block is placed.
        /// </summary>
        private static UV GetSheetFrame(Document doc, ViewSheet sheet, out UV paperSize)
        {
            var titleBlock = new FilteredElementCollector(doc, sheet.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsNotElementType()
                .FirstOrDefault();

            var bbox = titleBlock?.get_BoundingBox(sheet);
            if (bbox == null)
            {
                paperSize = UV.Zero;
                return UV.Zero;
            }

            paperSize = new UV(bbox.Max.X - bbox.Min.X, bbox.Max.Y - bbox.Min.Y);
            return new UV(bbox.Min.X, bbox.Min.Y);
        }

        /// <summary>
        /// Maps a 3D model point into this viewport's position on the sheet, in feet, relative to
        /// the sheet's title-block origin and scaled back up by the view's plot scale so the
        /// exported geometry keeps its true real-world size (not shrunk to paper size).
        /// </summary>
        /// <remarks>
        /// Model → view projection (paper distances) → sheet, through the API's own transforms
        /// (Revit 2023+), which place a point exactly where Revit draws it whatever the viewport's
        /// rotation, annotation crop, or annotations outside the crop. The earlier mapping pinned
        /// the crop box's corner to <see cref="Viewport.GetBoxOutline"/>'s corner — but that box
        /// also encloses annotations, so every area shifted down/left by the gap (27 x 554 cm, i.e.
        /// 3 x 55 mm on paper, on job 338000-1). A split crop region would need a per-point region
        /// pick; it uses the first region.
        /// </remarks>
        private readonly struct ViewportMapper
        {
            private readonly Transform _modelToProjection;
            private readonly Transform _projectionToSheet;
            private readonly int _scale;
            private readonly UV _sheetOrigin;

            public ViewportMapper(ViewPlan plan, Viewport viewport, UV sheetOrigin)
            {
                _modelToProjection = plan.GetModelToProjectionTransforms()[0].GetModelToProjectionTransform();
                _projectionToSheet = viewport.GetProjectionToSheetTransform();
                _scale = Math.Max(plan.Scale, 1);
                _sheetOrigin = sheetOrigin;
            }

            /// <summary>The view's plot scale (e.g. 100 for 1:100), exposed so callers can
            /// stamp it onto each <see cref="AreaRecord"/> built through this mapper.</summary>
            public int Scale => _scale;

            public Point2D Map(XYZ modelPoint)
            {
                var onSheet = _projectionToSheet.OfPoint(_modelToProjection.OfPoint(modelPoint));

                var realX = (onSheet.X - _sheetOrigin.U) * _scale;
                var realY = (onSheet.Y - _sheetOrigin.V) * _scale;

                return new Point2D { X = realX * FeetToCm, Y = realY * FeetToCm };
            }
        }

        private static AreaRecord BuildRecord(Document doc, Area area, ViewportMapper mapper)
        {
            var level = doc.GetElement(area.LevelId) as Level;
            var levelName = level?.Name ?? "";
            var elevationM = (level?.Elevation ?? 0) * FeetToM;

            var record = new AreaRecord
            {
                LevelElevation = elevationM,
                Floor = levelName,
                Number = area.Number,
                Name = area.Name,
                // The RZ_* shared parameters (bound + key-schedule-driven by
                // UsageKeyScheduleBuilder) are authoritative; the office template's older
                // "Usage Type…" text parameters remain as fallback for projects set up
                // before the RZ_* convention.
                UsageCode = ReadCode(area, UsageKeyScheduleBuilder.UsageTypeParam, null)
                    ?? ReadCode(area, "Usage Type", "Usage Type Name"),
                UsageCodePrev = ReadCode(area, UsageKeyScheduleBuilder.UsageTypeOldParam, null)
                    ?? ReadCode(area, "Usage Type Prev", "Usage Type Prev. Name"),
                PermitArea = ReadText(area, UsageKeyScheduleBuilder.PermitAreaParam),
                Asset = ReadText(area, UsageKeyScheduleBuilder.AssetParam),
                IsUnderground = level != null && elevationM < UndergroundThresholdM,
                // Single-sheet v1: the open sheet is always page 1 (Open item for multi-sheet).
                PageNo = 1,
                Scale = mapper.Scale,
                AreaValue = area.Area * SqFeetToSqM,
                BoundaryLoops = GetBoundaryLoops(area, mapper),
            };

            record.Errors = AreaValidator.CheckArea(record);
            return record;
        }

        /// <summary>
        /// Reads a usage code off the area: first the numeric/name code parameter (e.g.
        /// RZ_USAGE_TYPE or the legacy "Usage Type"), then — when that is absent or unreadable
        /// and a companion Hebrew-name parameter is given (e.g. "Usage Type Name") — that label
        /// resolved through <see cref="UsageCatalog"/>. Returns null when neither yields a code
        /// (a legitimately empty USAGE_TYPE_OLD for new work).
        /// </summary>
        private static int? ReadCode(Area area, string codeParamName, string? nameParamName)
        {
            var fromCode = ParseCodeParameter(area.LookupParameter(codeParamName));
            if (fromCode != null)
            {
                return fromCode;
            }

            if (nameParamName == null)
            {
                return null;
            }

            // The companion *Name* parameter is a label, not a code field — office templates
            // put all sorts of derived text there (observed: numbers tied to the area number,
            // which must NOT leak out as a usage code). Accept only values that resolve to a
            // real catalog entry; anything else means "no code" and shows up in validation.
            var nameParam = area.LookupParameter(nameParamName);
            return nameParam != null && nameParam.HasValue
                ? UsageCodeParser.Resolve(nameParam.AsString(), requireCatalog: true)
                : null;
        }

        /// <summary>A plain text parameter's trimmed value, or null when absent/empty.</summary>
        private static string? ReadText(Area area, string paramName)
        {
            var p = area.LookupParameter(paramName);
            if (p == null || !p.HasValue || p.StorageType != StorageType.String)
            {
                return null;
            }

            var text = p.AsString()?.Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        }

        /// <summary>The office template stores every robot field as a *text* parameter, so the
        /// String branch is the expected path (parsing via <see cref="UsageCodeParser"/>); the
        /// other storage types are kept for templates that model the code numerically or via a
        /// key schedule.</summary>
        private static int? ParseCodeParameter(Parameter? p)
        {
            if (p == null || !p.HasValue)
            {
                return null;
            }

            switch (p.StorageType)
            {
                case StorageType.Integer:
                    // Revit integer/number parameters default to 0 when never filled in — there
                    // is no usage code 0, so treat it as unset rather than an invalid code.
                    var i = p.AsInteger();
                    return i == 0 ? (int?)null : i;
                case StorageType.Double:
                    var d = (int)Math.Round(p.AsDouble());
                    return d == 0 ? (int?)null : d;
                case StorageType.String:
                    return UsageCodeParser.Resolve(p.AsString(), requireCatalog: false);
                default:
                    // StorageType.ElementId or unrecognized: AsValueString() renders whatever the
                    // parameter's display formatter produces (covers e.g. a key-schedule lookup).
                    return UsageCodeParser.Resolve(p.AsValueString(), requireCatalog: false);
            }
        }

        private static List<List<Point2D>> GetBoundaryLoops(Area area, ViewportMapper mapper)
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

                    points.Add(mapper.Map(curve.GetEndPoint(0)));
                }

                if (points.Count > 0)
                {
                    loops.Add(points);
                }
            }

            return loops;
        }

        /// <summary>ElementId raw value. ElementId.Value (long) exists in Revit 2024+;
        /// Revit 2023 (KKarea) uses the int-typed IntegerValue.</summary>
        private static long Raw(ElementId id)
        {
#if REVIT2023
            return id.IntegerValue;
#else
            return id.Value;
#endif
        }
    }
}

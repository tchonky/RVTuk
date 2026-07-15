using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace RVTuk.Core.RishuiZamin
{
    /// <summary>
    /// Generates an ASCII DXF matching the Rishui Zamin (רישוי זמין) "רכיב אוטומטי לחישוב
    /// שטחים" schema: <c>RZ_FRAME</c>/<c>RZ_FLOOR</c>/<c>RZ_AREA</c> layers with closed
    /// <c>LWPOLYLINE</c> polygons, each carrying one marker whose anchor point lies strictly
    /// inside its polygon (the robot matches markers to polygons by point-in-polygon). Two
    /// marker encodings are supported, selected by <see cref="AreaSubmissionConfig.MarkerForm"/>
    /// (rules §5): Form A — the official block form (an <c>RZ_*_SYM</c> INSERT with one ATTRIB
    /// per tag, closed by SEQEND, as in the Garmoshka sample) — and Form B — the tekenplus
    /// plain-TEXT form whose content is <c>KEY=VALUE&amp;&amp;&amp;KEY=VALUE…</c> pairs (see
    /// <c>tests/output/Export_example.dxf</c>).
    ///
    /// The HEADER/TABLES/BLOCKS preamble and the OBJECTS postamble are captured verbatim from
    /// a real sample (<c>tests/Examples Autoarea/Garmoshka.dxf</c>, see
    /// <c>docs/autoarea/rishui-zamin-notes.md</c> §5b-bis) and embedded as resources; only the
    /// ENTITIES section is generated per call. The postamble matters: AutoCAD rejects an AC1032
    /// file whose OBJECTS section (root NamedObject dictionary, LAYOUT objects the
    /// BLOCK_RECORDs point at) is missing with "File lacks the NamedObject dictionary —
    /// Invalid or incomplete DXF input".
    /// </summary>
    public static class DxfWriter
    {
        private const string Nl = "\r\n";

        /// <summary>Joins KEY=VALUE pairs inside a marker TEXT, exactly as tekenplus does.</summary>
        private const string PairSeparator = "&&&";

        // Marker TEXT metrics measured from the tekenplus reference (Export_example.dxf):
        // height 2.0, left/baseline justification, frame label inset 10 drawing units from the
        // frame's top-right corner, floor label inset 25 from its box's top-right corner. The
        // glyphs may overflow the box — only the anchor point matters to the robot.
        private const double LabelTextHeight = 2.0;
        private const double FrameLabelInset = 10.0;
        private const double FloorLabelInset = 25.0;

        // ── Form A (block/ATTRIB marker) constants, all measured from Garmoshka.dxf ──
        // The RZ_*_SYM block definitions (with their ATTDEFs) ship in the captured preamble's
        // BLOCKS section, so INSERTs can reference them by name. ATTRIB absolute position =
        // INSERT point + the ATTDEF's block-local offset; height/justification/invisible flag
        // mirror each ATTDEF (frame/floor tags: height 2.667, right-justified; area tags:
        // height 1.0, middle-justified, AREA + ASSET invisible).
        private const double FrameAttribHeight = 2.66666666666667;
        private const double AreaAttribHeight = 1.0;
        private const int JustifyRight = 2;
        private const int JustifyMiddle = 4;

        private static readonly (double X, double Y) PageNoOffset = (-18.915864696529, -0.103198527319176);

        private static readonly (double X, double Y) FloorTagOffset = (-30.5957224776758, -0.118543481047163);
        private static readonly (double X, double Y) BuildingNoOffset = (-30.5957224776758, -5.57129379138933);
        private static readonly (double X, double Y) LevelElevationOffset = (-30.5957224776758, -11.0240441017315);
        private static readonly (double X, double Y) IsUndergroundOffset = (-30.5957224776758, -15.9745853111437);

        private static readonly (double X, double Y) UsageTypeOffset = (0.0, 0.0);
        private static readonly (double X, double Y) UsageTypeOldOffset = (0.0, -2.72910819608965);
        private static readonly (double X, double Y) AreaTagOffset = (0.0, -4.72910819608965);
        private static readonly (double X, double Y) AssetOffset = (0.0, -6.72910819608965);

        // RZ_FRAME_SYM/RZ_FLOOR_SYM must be inserted strictly inside their own polygon —
        // Garmoshka.dxf itself insets its frame symbol ~5 units from the box's max corner
        // (26995,8995 inside a 27000,9000 frame) rather than placing it on the corner.
        private const double SymbolInset = 5.0;

        private const double FrameMargin = 200.0; // cm; content-box fallback when sheet size unknown
        private const double FloorMargin = 50.0;  // cm; keeps RZ_FLOOR strictly outside its areas
        private const double PageGap = 200.0;     // cm between frames when a multi-page set is laid out

        // The captured preamble's *Model_Space BLOCK_RECORD handle (TABLES section, BLOCK_RECORD
        // table) — every entity's owner (group 330) in ENTITIES must point at it. $HANDSEED in
        // the same preamble is "B8"; AC1015+ (this file declares AC1032) requires every entity to
        // carry a unique handle (group 5) and owner pointer, so starting a counter there and
        // rewriting $HANDSEED afterwards keeps handles unique and internally consistent.
        private const string ModelSpaceHandle = "1C";
        private const int InitialHandle = 0xB8;

        /// <summary>Hands out sequential unique hex handles for the ENTITIES section, starting
        /// where the preamble's $HANDSEED left off.</summary>
        private sealed class HandleAllocator
        {
            private int _next = InitialHandle;

            public string Next() => (_next++).ToString("X", CultureInfo.InvariantCulture);

            public string HandSeed => _next.ToString("X", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Builds the full ASCII DXF text for the given areas and submission config: the
        /// captured preamble, then one <c>RZ_FRAME</c> per distinct <see cref="AreaRecord.PageNo"/>,
        /// one <c>RZ_FLOOR</c> per distinct <see cref="AreaRecord.Floor"/> within that page, then
        /// each area's <c>RZ_AREA</c> polygon + marker <c>TEXT</c>, then the OBJECTS postamble and
        /// <c>EOF</c>.
        ///
        /// When <see cref="AreaSubmissionConfig.SheetWidthCm"/>/<see cref="AreaSubmissionConfig.SheetHeightCm"/>
        /// are set, each page's RZ_FRAME is that exact rectangle anchored at (0,0) — the real
        /// physical sheet — and the sheet-relative geometry is emitted untranslated. Otherwise the
        /// frame falls back to the page's content bounding box expanded by <see cref="FrameMargin"/>,
        /// translated so the frame's bottom-left corner lands at (0,0) as in the real samples.
        /// Pages are laid out right-to-left (PAGE_NO=1 rightmost, per the robot's reading order).
        /// </summary>
        public static string Build(IReadOnlyList<AreaRecord> areas, AreaSubmissionConfig config)
        {
            if (areas == null) throw new ArgumentNullException(nameof(areas));
            if (config == null) throw new ArgumentNullException(nameof(config));

            var handles = new HandleAllocator();
            var entities = new StringBuilder();

            entities.Append('0').Append(Nl).Append("SECTION").Append(Nl)
              .Append('2').Append(Nl).Append("ENTITIES").Append(Nl);

            var fixedSheet = config.SheetWidthCm > 0 && config.SheetHeightCm > 0;

            var pageGroups = areas
                .Where(a => a.BoundaryLoops.Any(l => l.Count >= 3))
                .GroupBy(a => a.PageNo)
                .OrderBy(g => g.Key)
                .ToList();

            for (var pageIndex = 0; pageIndex < pageGroups.Count; pageIndex++)
            {
                var pageGroup = pageGroups[pageIndex];

                BBox pageBox;
                double offsetX, offsetY;
                if (fixedSheet)
                {
                    // Geometry is already sheet-relative (title-block corner = (0,0)); only
                    // shift whole pages so PAGE_NO=1 ends up rightmost (robot reads right-to-left).
                    var pageShift = (pageGroups.Count - 1 - pageIndex) * (config.SheetWidthCm + PageGap);
                    pageBox = new BBox(pageShift, 0, pageShift + config.SheetWidthCm, config.SheetHeightCm);
                    offsetX = pageShift;
                    offsetY = 0;
                }
                else
                {
                    // Real Rishui Zamin samples always have RZ_FRAME's bottom-left corner at
                    // exactly (0,0) — translate the whole page uniformly (every floor/area keeps
                    // its true position *relative to the others*) rather than rearranging anything.
                    var rawBox = BoundingBox(pageGroup.SelectMany(AllPoints));
                    offsetX = FrameMargin - rawBox.MinX;
                    offsetY = FrameMargin - rawBox.MinY;
                    pageBox = rawBox.Expand(FrameMargin).Translate(offsetX, offsetY);
                }

                AppendFrame(entities, handles, config, pageGroup.Key, pageBox);

                foreach (var floorGroup in pageGroup.GroupBy(a => a.Floor))
                {
                    var floorBox = BoundingBox(floorGroup.SelectMany(AllPoints)).Expand(FloorMargin).Translate(offsetX, offsetY);
                    var first = floorGroup.First();
                    AppendFloor(entities, handles, config, floorGroup.Key, first.LevelElevation, config.BuildingNo, first.IsUnderground, floorBox);

                    foreach (var area in floorGroup)
                    {
                        AppendArea(entities, handles, area, config, offsetX, offsetY);
                    }
                }
            }

            entities.Append('0').Append(Nl).Append("ENDSEC").Append(Nl);

            // OBJECTS postamble after ENTITIES: without the root NamedObject dictionary
            // AutoCAD discards the whole file. All postamble handles predate the sample's
            // $HANDSEED (B8) and our entity handles start there, so nothing collides.
            entities.Append(LoadTemplate("Postamble.dxf", ref _cachedPostamble));
            entities.Append('0').Append(Nl).Append("EOF").Append(Nl);

            var preamble = LoadTemplate("Preamble.dxf", ref _cachedPreamble).Replace(
                "$HANDSEED" + Nl + "5" + Nl + "B8" + Nl,
                "$HANDSEED" + Nl + "5" + Nl + handles.HandSeed + Nl);

            return preamble + entities;
        }

        private static IEnumerable<Point2D> AllPoints(AreaRecord a) => a.BoundaryLoops.SelectMany(loop => loop);

        private static void AppendFrame(StringBuilder sb, HandleAllocator handles, AreaSubmissionConfig config, int pageNo, BBox box)
        {
            AppendPolyline(sb, handles, "RZ_FRAME", RectCorners(box));

            var pageNoText = pageNo.ToString(CultureInfo.InvariantCulture);
            if (config.MarkerForm == MarkerForm.FormA)
            {
                var insertion = (X: box.MaxX - SymbolInset, Y: box.MaxY - SymbolInset);
                var insertHandle = AppendInsert(sb, handles, "RZ_FRAME", "RZ_FRAME_SYM", insertion);
                AppendAttrib(sb, handles, insertHandle, insertion, PageNoOffset, FrameAttribHeight,
                    "PAGE_NO", pageNoText, invisible: false, JustifyRight);
                AppendSeqend(sb, handles, "RZ_FRAME");
            }
            else
            {
                AppendText(sb, handles, "RZ_FRAME",
                    box.MaxX - FrameLabelInset, box.MaxY - FrameLabelInset, "PAGE_NO=" + pageNoText);
            }
        }

        private static void AppendFloor(StringBuilder sb, HandleAllocator handles, AreaSubmissionConfig config, string floor, double levelElevation, int buildingNo, bool isUnderground, BBox box)
        {
            AppendPolyline(sb, handles, "RZ_FLOOR", RectCorners(box));

            var buildingNoText = buildingNo.ToString(CultureInfo.InvariantCulture);
            var elevationText = FormatElevation(levelElevation);
            var undergroundText = isUnderground ? "1" : "0";

            if (config.MarkerForm == MarkerForm.FormA)
            {
                var insertion = (X: box.MaxX - SymbolInset, Y: box.MaxY - SymbolInset);
                var insertHandle = AppendInsert(sb, handles, "RZ_FLOOR", "RZ_FLOOR_SYM", insertion);
                AppendAttrib(sb, handles, insertHandle, insertion, FloorTagOffset, FrameAttribHeight,
                    "FLOOR", floor ?? "", invisible: false, JustifyRight);
                AppendAttrib(sb, handles, insertHandle, insertion, BuildingNoOffset, FrameAttribHeight,
                    "BUILDING_NO", buildingNoText, invisible: false, JustifyRight);
                AppendAttrib(sb, handles, insertHandle, insertion, LevelElevationOffset, FrameAttribHeight,
                    "LEVEL_ELEVATION", elevationText, invisible: false, JustifyRight);
                AppendAttrib(sb, handles, insertHandle, insertion, IsUndergroundOffset, FrameAttribHeight,
                    "IS_UNDERGROUND", undergroundText, invisible: false, JustifyRight);
                AppendSeqend(sb, handles, "RZ_FLOOR");
            }
            else
            {
                var value =
                    "BUILDING_NO=" + buildingNoText + PairSeparator +
                    "FLOOR=" + (floor ?? "") + PairSeparator +
                    "LEVEL_ELEVATION=" + elevationText + PairSeparator +
                    "IS_UNDERGROUND=" + undergroundText;

                AppendText(sb, handles, "RZ_FLOOR",
                    box.MaxX - FloorLabelInset, box.MaxY - FloorLabelInset, value);
            }
        }

        private static void AppendArea(StringBuilder sb, HandleAllocator handles, AreaRecord area, AreaSubmissionConfig config, double offsetX, double offsetY)
        {
            var loop = area.BoundaryLoops.First(l => l.Count >= 3);
            var shifted = loop.Select(p => (X: p.X + offsetX, Y: p.Y + offsetY)).ToList();
            AppendPolyline(sb, handles, "RZ_AREA", shifted);

            var anchor = InteriorPoint(shifted);

            var usageType = area.UsageCode?.ToString(CultureInfo.InvariantCulture) ?? "";
            // USAGE_TYPE_OLD is the usage as existing in the current permit — empty for new
            // work (tekenplus leaves it empty too); a change-of-use/demolition submission fills
            // it from the area's RZ_USAGE_TYPE_OLD parameter. Never mirror USAGE_TYPE into it.
            var usageTypeOld = area.UsageCodePrev?.ToString(CultureInfo.InvariantCulture) ?? "";
            // AREA ("area as existed in permit") is a manual historical field the robot does not
            // recompute — empty unless the RZ_AREA parameter was filled by hand; the robot
            // derives the actual area from the polygon geometry.
            var permitArea = area.PermitArea ?? "";
            var asset = string.IsNullOrEmpty(area.Asset) ? config.Asset ?? "" : area.Asset;

            if (config.MarkerForm == MarkerForm.FormA)
            {
                var insertHandle = AppendInsert(sb, handles, "RZ_AREA", "RZ_AREA_SYM", anchor);
                AppendAttrib(sb, handles, insertHandle, anchor, UsageTypeOffset, AreaAttribHeight,
                    "USAGE_TYPE", usageType, invisible: false, JustifyMiddle);
                AppendAttrib(sb, handles, insertHandle, anchor, UsageTypeOldOffset, AreaAttribHeight,
                    "USAGE_TYPE_OLD", usageTypeOld, invisible: false, JustifyMiddle);
                AppendAttrib(sb, handles, insertHandle, anchor, AreaTagOffset, AreaAttribHeight,
                    "AREA", permitArea, invisible: true, JustifyMiddle);
                AppendAttrib(sb, handles, insertHandle, anchor, AssetOffset, AreaAttribHeight,
                    "ASSET", asset, invisible: true, JustifyMiddle);
                AppendSeqend(sb, handles, "RZ_AREA");
            }
            else
            {
                var value =
                    "USAGE_TYPE=" + usageType + PairSeparator +
                    "USAGE_TYPE_OLD=" + usageTypeOld + PairSeparator +
                    "AREA=" + permitArea + PairSeparator +
                    "ASSET=" + asset;

                AppendText(sb, handles, "RZ_AREA", anchor.X, anchor.Y, value);
            }
        }

        /// <summary>LEVEL_ELEVATION formatting per the tekenplus reference: metres with exactly
        /// two decimals and no explicit plus sign ("-4.90", "0.00", "4.00").</summary>
        private static string FormatElevation(double metres)
        {
            return metres.ToString("0.00", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// A point strictly inside the polygon for the marker TEXT anchor — the robot assigns
        /// markers to polygons by point-in-polygon, so the anchor must not fall outside (the
        /// vertex average / centroid of a concave polygon can). Uses the area-weighted centroid
        /// when it is inside; otherwise casts a horizontal scanline through the polygon's
        /// vertical middle and takes the midpoint of the widest inside span.
        /// </summary>
        private static (double X, double Y) InteriorPoint(List<(double X, double Y)> loop)
        {
            var centroid = Centroid(loop);
            if (Contains(loop, centroid.X, centroid.Y))
            {
                return centroid;
            }

            var minY = loop.Min(p => p.Y);
            var maxY = loop.Max(p => p.Y);
            // Nudge off the exact middle so the scanline cannot run along a horizontal edge.
            var y = (minY + maxY) / 2 + (maxY - minY) * 1e-6;

            var crossings = new List<double>();
            for (var i = 0; i < loop.Count; i++)
            {
                var p1 = loop[i];
                var p2 = loop[(i + 1) % loop.Count];
                if ((p1.Y > y) != (p2.Y > y))
                {
                    crossings.Add(p1.X + (y - p1.Y) * (p2.X - p1.X) / (p2.Y - p1.Y));
                }
            }

            crossings.Sort();
            var bestWidth = -1.0;
            var bestX = centroid.X;
            for (var i = 0; i + 1 < crossings.Count; i += 2)
            {
                var width = crossings[i + 1] - crossings[i];
                if (width > bestWidth)
                {
                    bestWidth = width;
                    bestX = (crossings[i] + crossings[i + 1]) / 2;
                }
            }

            return bestWidth > 0 ? (bestX, y) : centroid;
        }

        /// <summary>Area-weighted (true) polygon centroid; falls back to the vertex average for
        /// degenerate (zero-area) loops.</summary>
        private static (double X, double Y) Centroid(List<(double X, double Y)> loop)
        {
            double a = 0, cx = 0, cy = 0;
            for (var i = 0; i < loop.Count; i++)
            {
                var p1 = loop[i];
                var p2 = loop[(i + 1) % loop.Count];
                var cross = p1.X * p2.Y - p2.X * p1.Y;
                a += cross;
                cx += (p1.X + p2.X) * cross;
                cy += (p1.Y + p2.Y) * cross;
            }

            a *= 0.5;
            if (Math.Abs(a) < 1e-9)
            {
                return (loop.Average(p => p.X), loop.Average(p => p.Y));
            }

            return (cx / (6 * a), cy / (6 * a));
        }

        private static bool Contains(List<(double X, double Y)> loop, double x, double y)
        {
            var inside = false;
            for (var i = 0; i < loop.Count; i++)
            {
                var p1 = loop[i];
                var p2 = loop[(i + 1) % loop.Count];
                if ((p1.Y > y) != (p2.Y > y) &&
                    x < p1.X + (y - p1.Y) * (p2.X - p1.X) / (p2.Y - p1.Y))
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        private static IEnumerable<(double X, double Y)> RectCorners(BBox box)
        {
            yield return (box.MinX, box.MinY);
            yield return (box.MinX, box.MaxY);
            yield return (box.MaxX, box.MaxY);
            yield return (box.MaxX, box.MinY);
        }

        private static void AppendPolyline(StringBuilder sb, HandleAllocator handles, string layer, IEnumerable<(double X, double Y)> points)
        {
            var list = points.ToList();

            sb.Append('0').Append(Nl).Append("LWPOLYLINE").Append(Nl);
            sb.Append('5').Append(Nl).Append(handles.Next()).Append(Nl);
            sb.Append("330").Append(Nl).Append(ModelSpaceHandle).Append(Nl);
            sb.Append("100").Append(Nl).Append("AcDbEntity").Append(Nl);
            sb.Append("67").Append(Nl).Append('0').Append(Nl);
            sb.Append('8').Append(Nl).Append(layer).Append(Nl);
            sb.Append("62").Append(Nl).Append("256").Append(Nl);
            sb.Append('6').Append(Nl).Append("ByLayer").Append(Nl);
            sb.Append("370").Append(Nl).Append("-1").Append(Nl);
            sb.Append("48").Append(Nl).Append("1.0").Append(Nl);
            sb.Append("60").Append(Nl).Append('0').Append(Nl);
            sb.Append("100").Append(Nl).Append("AcDbPolyline").Append(Nl);
            sb.Append("90").Append(Nl).Append(list.Count.ToString(CultureInfo.InvariantCulture)).Append(Nl);
            sb.Append("70").Append(Nl).Append('1').Append(Nl);
            sb.Append("38").Append(Nl).Append("0.0").Append(Nl);
            sb.Append("39").Append(Nl).Append("0.0").Append(Nl);

            foreach (var (x, y) in list)
            {
                sb.Append("10").Append(Nl).Append(F(x)).Append(Nl);
                sb.Append("20").Append(Nl).Append(F(y)).Append(Nl);
                sb.Append("40").Append(Nl).Append("0.0").Append(Nl);
                sb.Append("41").Append(Nl).Append("0.0").Append(Nl);
                sb.Append("42").Append(Nl).Append("0.0").Append(Nl);
            }

            sb.Append("210").Append(Nl).Append("0.0").Append(Nl);
            sb.Append("220").Append(Nl).Append("0.0").Append(Nl);
            sb.Append("230").Append(Nl).Append("1.0").Append(Nl);
        }

        /// <summary>
        /// One Form A block reference: the marker <c>INSERT</c> with attributes-follow flag
        /// (66 = 1) on the polygon's layer. Group layout mirrors Garmoshka.dxf's INSERTs
        /// exactly, plus the entity handle/owner AC1032 requires. Returns the INSERT's handle —
        /// the following ATTRIBs point at it as their owner.
        /// </summary>
        private static string AppendInsert(StringBuilder sb, HandleAllocator handles, string layer, string blockName, (double X, double Y) insertion)
        {
            var handle = handles.Next();
            sb.Append('0').Append(Nl).Append("INSERT").Append(Nl);
            sb.Append('5').Append(Nl).Append(handle).Append(Nl);
            sb.Append("330").Append(Nl).Append(ModelSpaceHandle).Append(Nl);
            sb.Append("100").Append(Nl).Append("AcDbEntity").Append(Nl);
            sb.Append("67").Append(Nl).Append('0').Append(Nl);
            sb.Append('8').Append(Nl).Append(layer).Append(Nl);
            sb.Append("62").Append(Nl).Append("256").Append(Nl);
            sb.Append('6').Append(Nl).Append("ByLayer").Append(Nl);
            sb.Append("370").Append(Nl).Append("-1").Append(Nl);
            sb.Append("48").Append(Nl).Append("1.0").Append(Nl);
            sb.Append("60").Append(Nl).Append('0').Append(Nl);
            sb.Append("100").Append(Nl).Append("AcDbBlockReference").Append(Nl);
            sb.Append('2').Append(Nl).Append(blockName).Append(Nl);
            sb.Append("10").Append(Nl).Append(F(insertion.X)).Append(Nl);
            sb.Append("20").Append(Nl).Append(F(insertion.Y)).Append(Nl);
            sb.Append("30").Append(Nl).Append("0.0").Append(Nl);
            sb.Append("41").Append(Nl).Append("1.0").Append(Nl);
            sb.Append("42").Append(Nl).Append("1.0").Append(Nl);
            sb.Append("43").Append(Nl).Append("1.0").Append(Nl);
            sb.Append("50").Append(Nl).Append("0.0").Append(Nl);
            sb.Append("210").Append(Nl).Append("0.0").Append(Nl);
            sb.Append("220").Append(Nl).Append("0.0").Append(Nl);
            sb.Append("230").Append(Nl).Append("1.0").Append(Nl);
            sb.Append("66").Append(Nl).Append('1').Append(Nl);
            return handle;
        }

        /// <summary>
        /// One <c>ATTRIB</c> of a Form A marker, owned by its INSERT (group 330), on layer 0
        /// like every ATTRIB in the sample. Position = INSERT point + the ATTDEF's block-local
        /// offset; height, justification (group 72) and the invisible flag (group 70) mirror
        /// the block's ATTDEF for that tag. Justification is non-left, so the alignment point
        /// (11/21) is authoritative and 10/20 mirror it, exactly as the sample serialises it.
        /// </summary>
        private static void AppendAttrib(
            StringBuilder sb,
            HandleAllocator handles,
            string insertHandle,
            (double X, double Y) insertion,
            (double X, double Y) offset,
            double textHeight,
            string tag,
            string value,
            bool invisible,
            int justify)
        {
            var x = insertion.X + offset.X;
            var y = insertion.Y + offset.Y;

            sb.Append('0').Append(Nl).Append("ATTRIB").Append(Nl);
            sb.Append('5').Append(Nl).Append(handles.Next()).Append(Nl);
            sb.Append("330").Append(Nl).Append(insertHandle).Append(Nl);
            sb.Append("100").Append(Nl).Append("AcDbEntity").Append(Nl);
            sb.Append('8').Append(Nl).Append('0').Append(Nl);
            sb.Append("62").Append(Nl).Append("256").Append(Nl);
            sb.Append('6').Append(Nl).Append("ByLayer").Append(Nl);
            sb.Append("370").Append(Nl).Append("-1").Append(Nl);
            sb.Append("48").Append(Nl).Append("1.0").Append(Nl);
            sb.Append("60").Append(Nl).Append('0').Append(Nl);
            sb.Append("100").Append(Nl).Append("AcDbText").Append(Nl);
            sb.Append("10").Append(Nl).Append(F(x)).Append(Nl);
            sb.Append("20").Append(Nl).Append(F(y)).Append(Nl);
            sb.Append("30").Append(Nl).Append("0.0").Append(Nl);
            sb.Append("40").Append(Nl).Append(F(textHeight)).Append(Nl);
            sb.Append("41").Append(Nl).Append("1.0").Append(Nl);
            sb.Append('7').Append(Nl).Append("RZ_Area").Append(Nl);
            sb.Append('1').Append(Nl).Append(value ?? "").Append(Nl);
            sb.Append("11").Append(Nl).Append(F(x)).Append(Nl);
            sb.Append("21").Append(Nl).Append(F(y)).Append(Nl);
            sb.Append("31").Append(Nl).Append("0.0").Append(Nl);
            sb.Append("50").Append(Nl).Append("0.0").Append(Nl);
            sb.Append("51").Append(Nl).Append("0.0").Append(Nl);
            sb.Append("210").Append(Nl).Append("0.0").Append(Nl);
            sb.Append("220").Append(Nl).Append("0.0").Append(Nl);
            sb.Append("230").Append(Nl).Append("1.0").Append(Nl);
            sb.Append("71").Append(Nl).Append('0').Append(Nl);
            sb.Append("72").Append(Nl).Append(justify.ToString(CultureInfo.InvariantCulture)).Append(Nl);
            sb.Append("100").Append(Nl).Append("AcDbAttribute").Append(Nl);
            sb.Append("74").Append(Nl).Append('0').Append(Nl);
            sb.Append('2').Append(Nl).Append(tag).Append(Nl);
            sb.Append("70").Append(Nl).Append(invisible ? '1' : '0').Append(Nl);
        }

        /// <summary>Closes a Form A attribute sequence. Mirrors the sample's SEQENDs: handle +
        /// AcDbEntity + the INSERT's layer, and no owner group.</summary>
        private static void AppendSeqend(StringBuilder sb, HandleAllocator handles, string layer)
        {
            sb.Append('0').Append(Nl).Append("SEQEND").Append(Nl);
            sb.Append('5').Append(Nl).Append(handles.Next()).Append(Nl);
            sb.Append("100").Append(Nl).Append("AcDbEntity").Append(Nl);
            sb.Append('8').Append(Nl).Append(layer).Append(Nl);
        }

        /// <summary>
        /// One marker TEXT: left/baseline justified (like the tekenplus reference), height
        /// <see cref="LabelTextHeight"/>, style <c>RZ_Area</c> (defined in the preamble's STYLE
        /// table). The AcDbText group layout mirrors the text portion of the sample's ATTDEFs;
        /// the trailing second <c>100 AcDbText</c> + group 73 pair is how AutoCAD serialises
        /// TEXT's vertical-justification subclass split.
        /// </summary>
        private static void AppendText(StringBuilder sb, HandleAllocator handles, string layer, double x, double y, string value)
        {
            sb.Append('0').Append(Nl).Append("TEXT").Append(Nl);
            sb.Append('5').Append(Nl).Append(handles.Next()).Append(Nl);
            sb.Append("330").Append(Nl).Append(ModelSpaceHandle).Append(Nl);
            sb.Append("100").Append(Nl).Append("AcDbEntity").Append(Nl);
            sb.Append("67").Append(Nl).Append('0').Append(Nl);
            sb.Append('8').Append(Nl).Append(layer).Append(Nl);
            sb.Append("62").Append(Nl).Append("256").Append(Nl);
            sb.Append('6').Append(Nl).Append("ByLayer").Append(Nl);
            sb.Append("370").Append(Nl).Append("-1").Append(Nl);
            sb.Append("48").Append(Nl).Append("1.0").Append(Nl);
            sb.Append("60").Append(Nl).Append('0').Append(Nl);
            sb.Append("100").Append(Nl).Append("AcDbText").Append(Nl);
            sb.Append("10").Append(Nl).Append(F(x)).Append(Nl);
            sb.Append("20").Append(Nl).Append(F(y)).Append(Nl);
            sb.Append("30").Append(Nl).Append("0.0").Append(Nl);
            sb.Append("40").Append(Nl).Append(F(LabelTextHeight)).Append(Nl);
            sb.Append('1').Append(Nl).Append(value ?? "").Append(Nl);
            sb.Append("50").Append(Nl).Append("0.0").Append(Nl);
            sb.Append("51").Append(Nl).Append("0.0").Append(Nl);
            sb.Append("41").Append(Nl).Append("1.0").Append(Nl);
            sb.Append('7').Append(Nl).Append("RZ_Area").Append(Nl);
            sb.Append("210").Append(Nl).Append("0.0").Append(Nl);
            sb.Append("220").Append(Nl).Append("0.0").Append(Nl);
            sb.Append("230").Append(Nl).Append("1.0").Append(Nl);
            sb.Append("71").Append(Nl).Append('0').Append(Nl);
            sb.Append("72").Append(Nl).Append('0').Append(Nl);
            sb.Append("100").Append(Nl).Append("AcDbText").Append(Nl);
            sb.Append("73").Append(Nl).Append('0').Append(Nl);
        }

        /// <summary>
        /// Formats a coordinate/measurement value the way the sample does: always a decimal
        /// point, up to 6 fractional digits with insignificant trailing zeros trimmed. The
        /// sample itself carries up to ~15 digits of double precision, but that exact
        /// shortest-round-trip formatting differs subtly between net48 and net8's default
        /// double.ToString() — a fixed 6-decimal format (sub-micron at the file's centimetre
        /// scale) is exact and identical on both targets.
        /// </summary>
        private static string F(double value)
        {
            return value.ToString("0.0#####", CultureInfo.InvariantCulture);
        }

        private static BBox BoundingBox(IEnumerable<Point2D> points)
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            var any = false;
            foreach (var p in points)
            {
                any = true;
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }

            if (!any)
            {
                return new BBox(0, 0, 0, 0);
            }

            return new BBox(minX, minY, maxX, maxY);
        }

        private readonly struct BBox
        {
            public BBox(double minX, double minY, double maxX, double maxY)
            {
                MinX = minX;
                MinY = minY;
                MaxX = maxX;
                MaxY = maxY;
            }

            public double MinX { get; }
            public double MinY { get; }
            public double MaxX { get; }
            public double MaxY { get; }

            public BBox Expand(double margin) => new BBox(MinX - margin, MinY - margin, MaxX + margin, MaxY + margin);

            public BBox Translate(double dx, double dy) => new BBox(MinX + dx, MinY + dy, MaxX + dx, MaxY + dy);
        }

        private static string? _cachedPreamble;
        private static string? _cachedPostamble;

        private static string LoadTemplate(string fileName, ref string? cache)
        {
            if (cache != null)
            {
                return cache;
            }

            var assembly = typeof(DxfWriter).GetTypeInfo().Assembly;
            var resourceName = "RVTuk.Core.RishuiZamin.DxfTemplates." + fileName;

            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded DXF template resource '{resourceName}' not found.");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            // The whole file must be uniformly CRLF like the generated ENTITIES section, but the
            // embedded resource's endings depend on how git checked the repo out (the resource is
            // committed with LF) — normalise instead of trusting the checkout.
            cache = reader.ReadToEnd().Replace("\r\n", "\n").Replace("\n", Nl);
            return cache;
        }
    }
}

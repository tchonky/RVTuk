using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace RVTuk.Core.AreaSubmission
{
    /// <summary>
    /// Generates an ASCII DXF matching the Rishui Zamin (רישוי זמין) "רכיב אוטומטי לחישוב
    /// שטחים" schema: <c>RZ_FRAME</c>/<c>RZ_FLOOR</c>/<c>RZ_AREA</c> layers, closed
    /// <c>LWPOLYLINE</c> polygons, and <c>RZ_*_SYM</c> block <c>INSERT</c>s carrying the
    /// attribute tags (<c>ATTRIB</c>) the robot reads. The HEADER/TABLES/BLOCKS preamble is
    /// captured verbatim from a real sample (<c>tests/Examples Autoarea/Garmoshka.dxf</c>,
    /// see <c>docs/autoarea/rishui-zamin-notes.md</c> §5b-bis) and embedded as a resource;
    /// only the ENTITIES section is generated per call. See
    /// <c>tests/RVTuk.Core.Tests/AreaSubmission/Fixtures/one_area.dxf</c> for the golden
    /// byte-format reference for a single area's entities.
    /// </summary>
    public static class DxfWriter
    {
        private const string Nl = "\r\n";

        // ATTDEF offsets (block-local X/Y, block insertion scale/rotation always 1.0/0.0 in the
        // sample) and text height, transcribed verbatim from the RZ_*_SYM block definitions in
        // Garmoshka.dxf's BLOCKS section. ATTRIB absolute position = INSERT point + this offset.
        private const double FrameTextHeight = 2.66666666666667;
        private const double FloorTextHeight = 2.66666666666667;
        private const double AreaTextHeight = 1.0;

        private static readonly (double X, double Y) PageNoOffset = (-18.915864696529, -0.103198527319176);

        private static readonly (double X, double Y) FloorTagOffset = (-30.5957224776758, -0.118543481047163);
        private static readonly (double X, double Y) BuildingNoOffset = (-30.5957224776758, -5.57129379138933);
        private static readonly (double X, double Y) LevelElevationOffset = (-30.5957224776758, -11.0240441017315);
        private static readonly (double X, double Y) IsUndergroundOffset = (-30.5957224776758, -15.9745853111437);

        private static readonly (double X, double Y) UsageTypeOffset = (0.0, 0.0);
        private static readonly (double X, double Y) UsageTypeOldOffset = (0.0, -2.72910819608965);
        private static readonly (double X, double Y) AreaOffset = (0.0, -4.72910819608965);
        private static readonly (double X, double Y) AssetOffset = (0.0, -6.72910819608965);

        private const double FrameMargin = 200.0; // cm; keeps RZ_FRAME strictly outside RZ_FLOOR
        private const double FloorMargin = 50.0;   // cm; keeps RZ_FLOOR strictly outside its areas

        /// <summary>
        /// Builds the full ASCII DXF text for the given areas and submission config: the
        /// captured preamble, then one <c>RZ_FRAME</c> per distinct <see cref="AreaRecord.PageNo"/>,
        /// one <c>RZ_FLOOR</c> per distinct <see cref="AreaRecord.Floor"/> within that page, then
        /// each area's <c>RZ_AREA</c> polygon + <c>RZ_AREA_SYM</c> insert, then the closing
        /// <c>ENDSEC</c>/<c>EOF</c>.
        /// </summary>
        public static string Build(IReadOnlyList<AreaRecord> areas, AreaSubmissionConfig config)
        {
            if (areas == null) throw new ArgumentNullException(nameof(areas));
            if (config == null) throw new ArgumentNullException(nameof(config));

            var sb = new StringBuilder();
            sb.Append(LoadPreamble());

            sb.Append('0').Append(Nl).Append("SECTION").Append(Nl)
              .Append('2').Append(Nl).Append("ENTITIES").Append(Nl);

            foreach (var pageGroup in areas
                .Where(a => a.BoundaryLoops.Any(l => l.Count >= 3))
                .GroupBy(a => a.PageNo)
                .OrderBy(g => g.Key))
            {
                var pageBox = BoundingBox(pageGroup.SelectMany(AllPoints)).Expand(FrameMargin);
                AppendFrame(sb, pageGroup.Key, pageBox);

                foreach (var floorGroup in pageGroup.GroupBy(a => a.Floor))
                {
                    var floorBox = BoundingBox(floorGroup.SelectMany(AllPoints)).Expand(FloorMargin);
                    var first = floorGroup.First();
                    AppendFloor(sb, floorGroup.Key, first.Level, config.BuildingNo, first.IsUnderground, floorBox);

                    foreach (var area in floorGroup)
                    {
                        AppendArea(sb, area, config);
                    }
                }
            }

            sb.Append('0').Append(Nl).Append("ENDSEC").Append(Nl);
            sb.Append('0').Append(Nl).Append("EOF").Append(Nl);
            return sb.ToString();
        }

        private static IEnumerable<Point2D> AllPoints(AreaRecord a) => a.BoundaryLoops.SelectMany(loop => loop);

        private static void AppendFrame(StringBuilder sb, int pageNo, BBox box)
        {
            var corners = RectCorners(box);
            AppendPolyline(sb, "RZ_FRAME", corners);

            var insertion = (X: box.MaxX, Y: box.MaxY);
            AppendInsert(sb, "RZ_FRAME", "RZ_FRAME_SYM", insertion);

            AppendAttrib(sb, insertion, PageNoOffset, FrameTextHeight, "PAGE_NO", pageNo.ToString(CultureInfo.InvariantCulture), fieldFlag: 0, justify: 2);

            AppendSeqend(sb, "RZ_FRAME");
        }

        private static void AppendFloor(StringBuilder sb, string floor, string level, int buildingNo, bool isUnderground, BBox box)
        {
            var corners = RectCorners(box);
            AppendPolyline(sb, "RZ_FLOOR", corners);

            var insertion = (X: box.MaxX, Y: box.MaxY);
            AppendInsert(sb, "RZ_FLOOR", "RZ_FLOOR_SYM", insertion);

            AppendAttrib(sb, insertion, FloorTagOffset, FloorTextHeight, "FLOOR", floor ?? "", fieldFlag: 0, justify: 2);
            AppendAttrib(sb, insertion, BuildingNoOffset, FloorTextHeight, "BUILDING_NO", buildingNo.ToString(CultureInfo.InvariantCulture), fieldFlag: 0, justify: 2);
            AppendAttrib(sb, insertion, LevelElevationOffset, FloorTextHeight, "LEVEL_ELEVATION", level ?? "", fieldFlag: 0, justify: 2);
            AppendAttrib(sb, insertion, IsUndergroundOffset, FloorTextHeight, "IS_UNDERGROUND", isUnderground ? "1" : "0", fieldFlag: 0, justify: 2);

            AppendSeqend(sb, "RZ_FLOOR");
        }

        private static void AppendArea(StringBuilder sb, AreaRecord area, AreaSubmissionConfig config)
        {
            var loop = area.BoundaryLoops.First(l => l.Count >= 3);
            AppendPolyline(sb, "RZ_AREA", loop.Select(p => (p.X, p.Y)));

            var insertion = Centroid(loop);
            AppendInsert(sb, "RZ_AREA", "RZ_AREA_SYM", insertion);

            var usageType = area.UsageCode?.ToString(CultureInfo.InvariantCulture) ?? "";
            // USAGE_TYPE_OLD mirrors USAGE_TYPE: both real areas in the Garmoshka sample carry
            // the same value in USAGE_TYPE and USAGE_TYPE_OLD (status-quo/new-work submission,
            // not a change-of-use). See docs/autoarea/rishui-zamin-notes.md §5b.
            var usageTypeOld = usageType;
            // AREA ("area as existed in permit") is left empty in both real sample areas —
            // it's a manual historical field the robot does not recompute, not our proposed
            // AreaValue (which the robot derives from the polygon geometry itself).
            var areaField = "";
            var asset = config.Asset ?? "";

            AppendAttrib(sb, insertion, UsageTypeOffset, AreaTextHeight, "USAGE_TYPE", usageType, fieldFlag: 0, justify: 4);
            AppendAttrib(sb, insertion, UsageTypeOldOffset, AreaTextHeight, "USAGE_TYPE_OLD", usageTypeOld, fieldFlag: 0, justify: 4);
            AppendAttrib(sb, insertion, AreaOffset, AreaTextHeight, "AREA", areaField, fieldFlag: 1, justify: 4);
            AppendAttrib(sb, insertion, AssetOffset, AreaTextHeight, "ASSET", asset, fieldFlag: 1, justify: 4);

            AppendSeqend(sb, "RZ_AREA");
        }

        private static (double X, double Y) Centroid(List<Point2D> loop)
        {
            var cx = loop.Average(p => p.X);
            var cy = loop.Average(p => p.Y);
            return (cx, cy);
        }

        private static IEnumerable<(double X, double Y)> RectCorners(BBox box)
        {
            yield return (box.MinX, box.MinY);
            yield return (box.MinX, box.MaxY);
            yield return (box.MaxX, box.MaxY);
            yield return (box.MaxX, box.MinY);
        }

        private static void AppendPolyline(StringBuilder sb, string layer, IEnumerable<(double X, double Y)> points)
        {
            var list = points.ToList();

            sb.Append('0').Append(Nl).Append("LWPOLYLINE").Append(Nl);
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

        private static void AppendInsert(StringBuilder sb, string layer, string blockName, (double X, double Y) insertion)
        {
            sb.Append('0').Append(Nl).Append("INSERT").Append(Nl);
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
        }

        private static void AppendAttrib(
            StringBuilder sb,
            (double X, double Y) insertion,
            (double X, double Y) offset,
            double textHeight,
            string tag,
            string value,
            int fieldFlag,
            int justify)
        {
            var x = insertion.X + offset.X;
            var y = insertion.Y + offset.Y;

            sb.Append('0').Append(Nl).Append("ATTRIB").Append(Nl);
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
            sb.Append("70").Append(Nl).Append(fieldFlag.ToString(CultureInfo.InvariantCulture)).Append(Nl);
        }

        private static void AppendSeqend(StringBuilder sb, string layer)
        {
            sb.Append('0').Append(Nl).Append("SEQEND").Append(Nl);
            sb.Append("100").Append(Nl).Append("AcDbEntity").Append(Nl);
            sb.Append('8').Append(Nl).Append(layer).Append(Nl);
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
        }

        private static string? _cachedPreamble;

        private static string LoadPreamble()
        {
            if (_cachedPreamble != null)
            {
                return _cachedPreamble;
            }

            var assembly = typeof(DxfWriter).GetTypeInfo().Assembly;
            const string resourceName = "RVTuk.Core.AreaSubmission.DxfTemplates.Preamble.dxf";

            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded DXF preamble resource '{resourceName}' not found.");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            _cachedPreamble = reader.ReadToEnd();
            return _cachedPreamble;
        }
    }
}

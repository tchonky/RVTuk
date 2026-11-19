using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using RVTuk.Core.Shared.Geometry;
using RVTuk.Core.TopoTools;
using Xunit;
using Xunit.Abstractions;

namespace RVTuk.Core.Tests.TopoTools;

/// <summary>
/// Rule N, pinned by the reference's test vectors (Fixtures/outline-corner-heights-vectors.txt,
/// produced by docs/tools/topo-tools/specs/2026-11-19-outline-corner-heights-reference.py).
/// </summary>
public class OutlineCornerHeightsTests
{
    private readonly ITestOutputHelper _output;

    public OutlineCornerHeightsTests(ITestOutputHelper output) => _output = output;

    private const double Tolerance = 1e-6;

    // ---- the fixture ----------------------------------------------------------------------

    private sealed class Scene
    {
        public Scene(int number, string name) { Number = number; Name = name; }
        public int Number { get; }
        public string Name { get; }
        public List<CornerLine> Lines { get; } = new();
        public List<CornerLine> Bridges { get; } = new();
    }

    private sealed class Point
    {
        public Point(int scene, double x, double y, double? height, string why)
        {
            Scene = scene; X = x; Y = y; Height = height; Why = why;
        }

        public int Scene { get; }
        public double X { get; }
        public double Y { get; }
        public double? Height { get; }
        public string Why { get; }
    }

    private sealed class Vectors
    {
        public Dictionary<int, Scene> Scenes { get; } = new();
        public List<Point> Points { get; } = new();
    }

    private static readonly Lazy<Vectors> Fixture = new(Read);

    private static readonly Dictionary<int, OutlineCornerHeights> PreparedScenes = new();

    private static double Num(string s) => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static XyPoint Xy(string s)
    {
        var parts = s.Split(',');
        return new XyPoint(Num(parts[0]), Num(parts[1]));
    }

    private static Vectors Read()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TopoTools", "Fixtures", "outline-corner-heights-vectors.txt");
        var vectors = new Vectors();
        Scene? scene = null;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
            var f = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            switch (f[0])
            {
                case "scene":
                    scene = new Scene(int.Parse(f[1], CultureInfo.InvariantCulture), f.Length > 2 ? f[2] : "");
                    vectors.Scenes.Add(scene.Number, scene);
                    break;
                case "line":
                case "bridge":
                    var poly = new List<XyPoint>();
                    for (var k = 2; k < f.Length; k++) poly.Add(Xy(f[k]));
                    var cl = new CornerLine(Num(f[1]), poly);
                    (f[0] == "line" ? scene!.Lines : scene!.Bridges).Add(cl);
                    break;
                case "point":
                    vectors.Points.Add(new Point(
                        int.Parse(f[1], CultureInfo.InvariantCulture), Num(f[2]), Num(f[3]),
                        f[4] == "keep" ? null : Num(f[4]), f.Length > 5 ? f[5] : ""));
                    break;
                default:
                    throw new InvalidDataException("Unknown fixture line: " + line);
            }
        }
        return vectors;
    }

    private static OutlineCornerHeights PreparedScene(int number)
    {
        lock (PreparedScenes)
        {
            if (!PreparedScenes.TryGetValue(number, out var prepared))
            {
                prepared = OutlineCornerHeights.Prepare(Fixture.Value.Scenes[number].Lines);
                PreparedScenes.Add(number, prepared);
            }
            return prepared;
        }
    }

    public static IEnumerable<object[]> PointIndexes()
    {
        for (var i = 0; i < Fixture.Value.Points.Count; i++) yield return new object[] { i };
    }

    public static IEnumerable<object[]> SceneNumbers()
    {
        foreach (var n in Fixture.Value.Scenes.Keys) yield return new object[] { n };
    }

    // ---- the reference's values ---------------------------------------------------------

    [Fact]
    public void TheFixtureHasEveryScenePointAndBridge()
    {
        Assert.Equal(228, Fixture.Value.Scenes.Count);
        Assert.Equal(698, Fixture.Value.Points.Count);
        Assert.Equal(33, Fixture.Value.Scenes.Values.Sum(s => s.Bridges.Count));
    }

    [Theory]
    [MemberData(nameof(PointIndexes))]
    public void MatchesEveryReferencePoint(int index)
    {
        var p = Fixture.Value.Points[index];
        var h = PreparedScene(p.Scene).HeightAt(new XyPoint(p.X, p.Y));

        if (p.Height is null)
        {
            Assert.True(h is null, $"scene {p.Scene} ({p.X}, {p.Y}): expected keep ({p.Why}), got {h}");
        }
        else
        {
            Assert.True(h is not null, $"scene {p.Scene} ({p.X}, {p.Y}): expected {p.Height} ({p.Why}), got keep");
            Assert.True(Math.Abs(h!.Value - p.Height.Value) <= Tolerance,
                $"scene {p.Scene} ({p.X}, {p.Y}): expected {p.Height} ({p.Why}), got {h}");
        }
    }

    [Theory]
    [MemberData(nameof(SceneNumbers))]
    public void BuildsTheReferenceBridges(int number)
    {
        var expected = Fixture.Value.Scenes[number].Bridges;
        var actual = PreparedScene(number).Bridges;

        Assert.Equal(expected.Count, actual.Count);
        var used = new bool[expected.Count];
        foreach (var b in actual)
        {
            Assert.Equal(2, b.PolylineMetres.Count);
            var found = false;
            for (var k = 0; k < expected.Count && !found; k++)
            {
                if (used[k] || !SameBridge(b, expected[k])) continue;
                used[k] = true;
                found = true;
            }
            Assert.True(found, $"scene {number}: unexpected bridge {b.HeightMetres} " +
                $"({b.PolylineMetres[0]}) - ({b.PolylineMetres[1]})");
        }
    }

    private static bool Near(XyPoint a, XyPoint b) =>
        Math.Abs(a.X - b.X) <= Tolerance && Math.Abs(a.Y - b.Y) <= Tolerance;

    private static bool SameBridge(CornerLine a, CornerLine b)
    {
        if (Math.Abs(a.HeightMetres - b.HeightMetres) > Tolerance) return false;
        var a0 = a.PolylineMetres[0];
        var a1 = a.PolylineMetres[a.PolylineMetres.Count - 1];
        var b0 = b.PolylineMetres[0];
        var b1 = b.PolylineMetres[b.PolylineMetres.Count - 1];
        return (Near(a0, b0) && Near(a1, b1)) || (Near(a0, b1) && Near(a1, b0));
    }


    // ---- order and direction ------------------------------------------------------------

    public static IEnumerable<object[]> Rearrangements() => new[]
    {
        new object[] { "reverse-lines" },
        new object[] { "reverse-polylines" },
        new object[] { "reverse-both" },
        new object[] { "shuffle-1" },
        new object[] { "shuffle-2" },
        new object[] { "shuffle-3" },
        new object[] { "shuffle-4" },
        new object[] { "shuffle-5" },
    };

    /// <summary>The scene's lines rearranged: reversed in order, in direction, or both; or shuffled
    /// with a fixed seed, each polyline reversed or not at random.</summary>
    private static List<CornerLine> Rearrange(IReadOnlyList<CornerLine> lines, string how)
    {
        List<CornerLine> Flip(IEnumerable<CornerLine> ls) =>
            ls.Select(l => new CornerLine(l.HeightMetres, l.PolylineMetres.Reverse().ToList())).ToList();

        switch (how)
        {
            case "reverse-lines": return lines.Reverse().ToList();
            case "reverse-polylines": return Flip(lines);
            case "reverse-both": return Flip(lines.Reverse());
        }
        var random = new Random(int.Parse(how.Substring("shuffle-".Length), CultureInfo.InvariantCulture));
        var list = lines.ToList();
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list.Select(l => random.Next(2) == 0 ? l : Flip(new[] { l })[0]).ToList();
    }

    /// <summary>The bridges as an order-free list: height, then both ends in (x, y) order.</summary>
    private static List<string> BridgeKeys(IReadOnlyList<CornerLine> bridges)
    {
        string P(XyPoint p) => p.X.ToString("R", CultureInfo.InvariantCulture) + "," + p.Y.ToString("R", CultureInfo.InvariantCulture);
        var keys = new List<string>();
        foreach (var b in bridges)
        {
            var ends = new[] { b.PolylineMetres[0], b.PolylineMetres[b.PolylineMetres.Count - 1] }
                .OrderBy(p => p.X).ThenBy(p => p.Y).ToArray();
            keys.Add(b.HeightMetres.ToString("R", CultureInfo.InvariantCulture) + " " + P(ends[0]) + " " + P(ends[1]));
        }
        keys.Sort(StringComparer.Ordinal);
        return keys;
    }

    [Theory]
    [MemberData(nameof(Rearrangements))]
    public void OrderAndDirectionDoNotMatter(string how)
    {
        var vectors = Fixture.Value;
        var rearranged = new Dictionary<int, OutlineCornerHeights>();
        var differences = new List<string>();
        foreach (var scene in vectors.Scenes.Values)
        {
            var other = OutlineCornerHeights.Prepare(Rearrange(scene.Lines, how));
            rearranged.Add(scene.Number, other);
            var a = BridgeKeys(PreparedScene(scene.Number).Bridges);
            var b = BridgeKeys(other.Bridges);
            if (!a.SequenceEqual(b))
            {
                differences.Add($"scene {scene.Number}: bridges [{string.Join("; ", a)}] vs [{string.Join("; ", b)}]");
            }
        }

        foreach (var p in vectors.Points)
        {
            var v = new XyPoint(p.X, p.Y);
            var a = PreparedScene(p.Scene).HeightAt(v);
            var b = rearranged[p.Scene].HeightAt(v);
            if (!Nullable.Equals(a, b)) differences.Add($"scene {p.Scene} ({p.X}, {p.Y}): {a} vs {b}");
        }
        Assert.True(differences.Count == 0, how + ":" + Environment.NewLine + string.Join(Environment.NewLine, differences));
    }

    // ---- spot checks with the spec's numbers ---------------------------------------------

    private static CornerLine Line(double z, params (double X, double Y)[] pts) =>
        new(z, pts.Select(p => new XyPoint(p.X, p.Y)).ToList());

    /// <summary>The height at (x, y), failing with the corner and the scene named when it is left alone.</summary>
    private static double HeightOf(OutlineCornerHeights rule, double x, double y, string scene)
    {
        var h = rule.HeightAt(new XyPoint(x, y));
        Assert.True(h is not null, $"{scene}: the corner ({x}, {y}) was left alone");
        return h!.Value;
    }

    [Fact]
    public void BetweenTwoParallelLinesTheHeightIsInProportion()
    {
        var rule = OutlineCornerHeights.Prepare(new[]
        {
            Line(41.0, (-20, 0), (20, 0)),
            Line(43.0, (-20, 10), (20, 10)),
        });

        Assert.Equal(41.5, HeightOf(rule, 0.3, 2.5, "parallel"), 3);
    }

    [Fact]
    public void DanielsScreenshotCornersTakeTheirHeights()
    {
        // Three fixture scenes are called "screenshot"; scene 58 is the one with the three
        // labelled lines (41.30, 41.00, 43.00).
        Assert.True(Fixture.Value.Scenes.TryGetValue(58, out var scene), "the fixture has no scene 58");
        var heights = string.Join(" ", scene!.Lines.Select(l => l.HeightMetres.ToString(CultureInfo.InvariantCulture)));
        Assert.True(scene.Name == "screenshot" && heights == "41.3 41 43",
            $"scene 58 is not the screenshot's three labelled lines: '{scene.Name}' with heights {heights}");
        var rule = OutlineCornerHeights.Prepare(scene.Lines);

        Assert.Equal(41.199, HeightOf(rule, 7.09, -4.03, "screenshot corner 1"), 3);
        Assert.Equal(41.498, HeightOf(rule, 12.41, -6.71, "screenshot corner 2"), 3);
        Assert.Equal(42.886, HeightOf(rule, 12.41, -10.95, "screenshot corner 3"), 3);
    }

    [Fact]
    public void PastTheEndOfTheTopLineWithNothingBeyondTheCornerKeepsItsHeight()
    {
        var rule = OutlineCornerHeights.Prepare(new[]
        {
            Line(41.0, (-30, 0), (30, 0)),
            Line(41.3, (-30, 1.5), (30, 1.5)),
            Line(43.0, (-30, 8), (0, 8)),
        });

        Assert.Null(rule.HeightAt(new XyPoint(0.0, 8.10)));
    }

    [Theory]
    [InlineData(50.0, 1)]
    [InlineData(70.0, 0)]
    public void FreeEndsFaceEachOtherWithinSixtyDegrees(double degrees, int bridges)
    {
        // Line A ends at the origin heading +x. Line B ends 10 m away at `degrees` off A's heading,
        // and heads straight back at A's end, so only A's angle decides.
        var a = Math.PI * degrees / 180.0;
        double fx = 10 * Math.Cos(a), fy = 10 * Math.Sin(a);
        var rule = OutlineCornerHeights.Prepare(new[]
        {
            Line(41.0, (-10, 0), (0, 0)),
            Line(41.0, (fx + 10 * Math.Cos(a), fy + 10 * Math.Sin(a)), (fx, fy)),
        });

        Assert.Equal(bridges, rule.Bridges.Count);
    }

    [Fact]
    public void NoLinesLeaveEveryPointAlone()
    {
        var rule = OutlineCornerHeights.Prepare(Array.Empty<CornerLine>());

        Assert.Empty(rule.Bridges);
        Assert.Null(rule.HeightAt(new XyPoint(1, 2)));
    }

    [Fact]
    public void LinesWithAHeightOrACoordinateThatIsNotANumberAreLeftOut()
    {
        var rule = OutlineCornerHeights.Prepare(new[]
        {
            Line(double.NaN, (-10, 1), (10, 1)),
            Line(41.0, (-10, 0), (10, 0)),
            Line(43.0, (-10, double.PositiveInfinity), (10, 2)),
            Line(43.0, (double.NaN, 3), (10, 3)),
            Line(double.NegativeInfinity, (-10, 4), (10, 4)),
        });

        Assert.Empty(rule.Bridges);
        // Only the 41.00 line is left: nothing beyond it, within 5 cm.
        Assert.Equal(41.0, HeightOf(rule, 0, 0.01, "non-finite lines"), 9);
    }

    // ---- every number of the rule, at its edge ----------------------------------------------

    [Theory]
    [InlineData(0.049, 41.0)]   // nothing beyond, within 5 cm: the line's height
    [InlineData(0.051, null)]   // nothing beyond, further: left alone
    public void NothingBeyondTakesTheLineOnlyWithinFiveCentimetres(double y, double? expected)
    {
        var rule = OutlineCornerHeights.Prepare(new[] { Line(41.0, (-10, 0), (10, 0)) });

        var h = rule.HeightAt(new XyPoint(0, y));

        if (expected is null) Assert.True(h is null, $"(0, {y}) took {h}");
        else Assert.Equal(expected.Value, HeightOf(rule, 0, y, "5 cm"), 9);
    }

    [Theory]
    [InlineData(0.0009, 41.0)]       // on the line: its height exactly
    [InlineData(0.0011, 41.00022)]   // off it: in proportion to the 43.00 line 10 m away
    public void ACornerWithinOneMillimetreIsOnTheLine(double y, double expected)
    {
        var rule = OutlineCornerHeights.Prepare(new[]
        {
            Line(41.0, (-20, 0), (20, 0)),
            Line(43.0, (-20, 10), (20, 10)),
        });

        Assert.Equal(expected, HeightOf(rule, 0, y, "1 mm"), 9);
    }

    [Theory]
    [InlineData(0.0004, 41.0)]       // the same height: only L1's height beyond
    [InlineData(0.0005, 41.0)]       // still the same height (5 tenths of a millimetre)
    [InlineData(0.0006, 41.00015)]   // another height: in proportion, 2.5 m of 10 m
    public void HeightsWithinHalfAMillimetreAreTheSameForTheLineBeyond(double dz, double expected)
    {
        var rule = OutlineCornerHeights.Prepare(new[]
        {
            Line(41.0, (-20, 0), (20, 0)),
            Line(41.0 + dz, (-20, 10), (20, 10)),
        });

        Assert.Equal(expected, HeightOf(rule, 0, 2.5, $"dz {dz}"), 9);
    }

    [Theory]
    [InlineData(0.0004, 1)]
    [InlineData(0.0005, 1)]
    [InlineData(0.0006, 0)]
    public void HeightsWithinHalfAMillimetreAreTheSameForBridges(double dz, int bridges)
    {
        var rule = OutlineCornerHeights.Prepare(new[]
        {
            Line(41.0, (-10, 0), (-1, 0)),
            Line(41.0 + dz, (1, 0), (10, 0)),
        });

        Assert.Equal(bridges, rule.Bridges.Count);
        if (bridges == 1) Assert.Equal(41.0 + dz, rule.Bridges[0].HeightMetres, 9);
    }

    [Theory]
    [InlineData(0.0000009, null)]   // the two vertices merge: no line is left
    [InlineData(0.0000011, 41.0)]   // a line 1.1 µm long, 1 cm from the corner
    public void VerticesCloserThanOneMicrometreMerge(double length, double? expected)
    {
        var rule = OutlineCornerHeights.Prepare(new[] { Line(41.0, (0, 0), (length, 0)) });

        var h = rule.HeightAt(new XyPoint(0, 0.01));

        if (expected is null) Assert.True(h is null, $"a {length} m line was kept: {h}");
        else Assert.Equal(expected.Value, HeightOf(rule, 0, 0.01, "1 µm"), 9);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothRawEndVerticesAreKeptWhicheverWayTheLineIsDrawn(bool reversed)
    {
        // Line A's last two vertices are 0.4 µm apart: they merge, but the end stays the raw end,
        // drawn either way, so the bridge to line B starts there.
        var a = new List<XyPoint> { new(-10, 0), new(-1, 0), new(-0.9999996, 0) };
        if (reversed) a.Reverse();
        var rule = OutlineCornerHeights.Prepare(new[]
        {
            new CornerLine(41.0, a),
            Line(41.0, (1, 0), (10, 0)),
        });

        Assert.Single(rule.Bridges);
        var ends = rule.Bridges[0].PolylineMetres.Select(p => p.X).OrderBy(x => x).ToArray();
        Assert.Equal(-0.9999996, ends[0], 12);
        Assert.Equal(1.0, ends[1], 12);
    }

    [Fact]
    public void HeightsCountInTenthsOfAMillimetre()
    {
        var rule = OutlineCornerHeights.Prepare(new[] { Line(41.00004, (-10, 0), (10, 0)) });

        Assert.Equal(41.0, HeightOf(rule, 0, 0.0005, "tenths"), 12);
    }

    [Theory]
    [InlineData(0.5e-9, 41.664457)]  // A and B equally near: A, whose nearest point has the smaller x
    [InlineData(2e-9, 41.0)]         // B nearer: the far side is to the left, with no other height
    public void DistancesWithinANanometreAreEqual(double delta, double expected)
    {
        // V at the origin. A (41.00) passes 1 m + delta below it, B (41.00) 1 m to its right; a
        // 43.00 line lies above, right of x = 0.2.
        var rule = OutlineCornerHeights.Prepare(new[]
        {
            Line(41.0, (-0.5, -1 - delta), (0.5, -1 - delta)),
            Line(41.0, (1, -0.5), (1, 0.5)),
            Line(43.0, (0.2, 2), (1, 2)),
        });

        Assert.Equal(expected, HeightOf(rule, 0, 0, $"delta {delta}"), 6);
    }

    [Theory]
    [InlineData(0.5e-9, null)]        // inside the 1e-9 m margin: not on the far side
    [InlineData(2e-9, 41.666667)]     // past it: the line beyond counts
    public void TheFarSideStartsANanometrePastTheCorner(double y, double? expected)
    {
        var rule = OutlineCornerHeights.Prepare(new[]
        {
            Line(41.0, (-10, -1), (10, -1)),
            Line(43.0, (2, y), (10, y)),
        });

        var h = rule.HeightAt(new XyPoint(0, 0));

        if (expected is null) Assert.True(h is null, $"a line {y} m past the corner counted: {h}");
        else Assert.Equal(expected.Value, HeightOf(rule, 0, 0, "far side"), 5);
    }

    [Theory]
    [InlineData(0.5e-9, null)]        // a far part 0.5 nm long is ignored
    [InlineData(2e-9, 41.666667)]     // 2 nm counts
    public void FarPartsOfANanometreOrLessAreIgnored(double length, double? expected)
    {
        var rule = OutlineCornerHeights.Prepare(new[]
        {
            Line(41.0, (-10, -1), (10, -1)),
            Line(43.0, (2, -1), (2, 1e-9 + length)),
        });

        var h = rule.HeightAt(new XyPoint(0, 0));

        if (expected is null) Assert.True(h is null, $"a far part {length} m long counted: {h}");
        else Assert.Equal(expected.Value, HeightOf(rule, 0, 0, "short part"), 5);
    }

    [Theory]
    [InlineData(0.5e-9, 0)]   // the 43.00 line stops 0.5 nm short of the gap's line: it touches
    [InlineData(2e-9, 1)]     // 2 nm short: the bridge passes
    public void SegmentsWithinANanometreTouch(double gap, int bridges)
    {
        var rule = OutlineCornerHeights.Prepare(new[]
        {
            Line(41.0, (-10, 0), (-1, 0)),
            Line(41.0, (1, 0), (10, 0)),
            Line(43.0, (0, gap), (0, 5)),
        });

        Assert.Equal(bridges, rule.Bridges.Count);
    }

    [Theory]
    [InlineData(true, 41.0)]          // a 41.00 line crosses the sight line 2.5 mm out: 43.00 is hidden
    [InlineData(false, 41.461538)]    // without it, 43.00 at 5 mm is L2
    public void ACandidateFiveMillimetresAwayIsHiddenByALineInBetween(bool blocker, double expected)
    {
        // V at the origin, L1 (41.00) 1.5 mm below, so u = +y. The 43.00 line's nearest point is
        // 5 mm away at 45 degrees. The blocker crosses the sight line 2.47 mm from V, off the walk
        // straight on (its vertices are more than 1 mm to the side), so only the sight test can
        // hide 43.00; a candidate within 2 mm would skip that test.
        var r = Math.Sqrt(0.5);
        var lines = new List<CornerLine>
        {
            Line(41.0, (-10, -0.0015), (10, -0.0015)),
            Line(43.0, (0.005 * r + 0.002 * r, 0.005 * r - 0.002 * r), (0.005 * r - 1.0 * r, 0.005 * r + 1.0 * r)),
        };
        if (blocker) lines.Add(Line(41.0, (0.003, 0.0005), (0.0012, 0.0023)));
        var rule = OutlineCornerHeights.Prepare(lines);

        Assert.Equal(expected, HeightOf(rule, 0, 0, blocker ? "blocked" : "in view"), 6);
    }

    [Fact]
    public void TheWalkMeetsANearlyParallelLineOfTheSameHeight()
    {
        // V at the origin, L1 (41.00) 1 m below, so the walk goes up x = 0. A 4 km 41.00 line runs
        // nearly up it (7.5e-7 rad off), crossing it at y = 2010 m with both ends 1.5 mm to the
        // side (out of the vertex clause's reach). The 43.00 line at y = 3000 m, off to the side
        // (so the sight line passes the 41.00 line), lies beyond that crossing: no L2. Only a walk
        // that skipped segments more than 1e-12 off parallel would miss the crossing.
        var rule = OutlineCornerHeights.Prepare(new[]
        {
            Line(41.0, (-10, -1), (10, -1)),
            Line(41.0, (-0.0015, 10), (0.0015, 4010)),
            Line(43.0, (5, 3000), (10, 3000)),
        });

        Assert.Equal(41.0, HeightOf(rule, 0, 0, "walk"), 9);
    }

    // ---- speed ------------------------------------------------------------------------------

    public static IEnumerable<object[]> TimingScenes() => new[]
    {
        new object[] { "contours" },
        new object[] { "arcs" },
    };

    /// <summary>200 lines of 240 segments, 0.25 m apart in height, over a 100 m site: wavy contours
    /// across it, or concentric 270-degree arcs (tessellated arcs carry many segments).</summary>
    private static List<CornerLine> TimingLines(string scene)
    {
        var lines = new List<CornerLine>();
        for (var i = 0; i < 200; i++)
        {
            var poly = new List<XyPoint>();
            for (var k = 0; k <= 240; k++)
            {
                if (scene == "contours")
                {
                    var x = k * 100.0 / 240;
                    poly.Add(new XyPoint(x, i * 0.5 + 0.2 * Math.Sin(x / 7.0 + i)));
                }
                else
                {
                    var r = 0.5 + 0.25 * i;
                    var a = 1.5 * Math.PI * k / 240;
                    poly.Add(new XyPoint(50 + r * Math.Cos(a), 50 + r * Math.Sin(a)));
                }
            }
            lines.Add(new CornerLine(40.0 + 0.25 * i, poly));
        }
        return lines;
    }

    [Theory]
    [MemberData(nameof(TimingScenes))]
    public void TwoThousandPointsAgainstTwoHundredLinesOfTwoHundredFortySegments(string scene)
    {
        var lines = TimingLines(scene);
        var points = new List<XyPoint>();
        var random = new Random(1);
        for (var n = 0; n < 2000; n++) points.Add(new XyPoint(random.NextDouble() * 100.0, random.NextDouble() * 100.0));

        var moved = 0;
        TimeSpan Run()
        {
            var watch = Stopwatch.StartNew();
            var rule = OutlineCornerHeights.Prepare(lines);
            moved = 0;
            foreach (var v in points) if (rule.HeightAt(v) is not null) moved++;
            watch.Stop();
            return watch.Elapsed;
        }

        Run(); // warm-up: the JIT and the caches
        var times = new[] { Run(), Run(), Run() };
        var text = string.Join(", ", times.Select(t => $"{t.TotalMilliseconds:F0} ms"));

        _output.WriteLine($"{scene}: Prepare + 2000 HeightAt, three runs: {text}; {moved} moved");
        Assert.True(times.Min() < TimeSpan.FromSeconds(2), $"{scene}: best of three runs over 2 s: {text}");
    }
}

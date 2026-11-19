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
    [InlineData(0.049, 41.0)]          // nothing beyond, within 5 cm: the line's height
    [InlineData(0.051, null)]          // nothing beyond, further: left alone
    [InlineData(0.0500000005, 41.0)]   // 5 cm + 0.5 nm: within the 1e-9 m tie
    [InlineData(0.050000002, null)]    // 5 cm + 2 nm: past it
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
    [InlineData(false, 0.5e-9, null)]        // inside the 1e-9 m margin: not on the far side
    [InlineData(false, 2e-9, 41.666667)]     // past it: the line beyond counts
    // With a rise at its far end the line reaches well into the far side, so only the segment's
    // own ends decide: inside the margin only the rise counts, 10 m away.
    [InlineData(true, 0.5e-9, 41.181818)]
    [InlineData(true, 2e-9, 41.666667)]
    public void TheFarSideStartsANanometrePastTheCorner(bool rise, double y, double? expected)
    {
        var rule = OutlineCornerHeights.Prepare(new[]
        {
            Line(41.0, (-10, -1), (10, -1)),
            rise ? Line(43.0, (2, y), (10, y), (10, 5)) : Line(43.0, (2, y), (10, y)),
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
    // The bridge's test segment runs from (-0.999, 0) to (0.999, 0). A 43.00 line ends or passes
    // `gap` from it, once for each of the four endpoint-to-segment distances. Square to the axes
    // (turn 0) the boxes are flat, so the box check alone decides; turned 30 degrees the boxes
    // overlap and the distances decide.
    [InlineData(0, "its first end above", 0.5e-9, 0)]
    [InlineData(0, "its first end above", 2e-9, 1)]
    [InlineData(0, "its second end below", 0.5e-9, 0)]
    [InlineData(0, "its second end below", 2e-9, 1)]
    [InlineData(0, "past the test segment's first end", 0.5e-9, 0)]
    [InlineData(0, "past the test segment's first end", 2e-9, 1)]
    [InlineData(0, "past the test segment's second end", 0.5e-9, 0)]
    [InlineData(0, "past the test segment's second end", 2e-9, 1)]
    [InlineData(30, "its first end above", 0.5e-9, 0)]
    [InlineData(30, "its first end above", 2e-9, 1)]
    [InlineData(30, "its second end below", 0.5e-9, 0)]
    [InlineData(30, "its second end below", 2e-9, 1)]
    [InlineData(30, "past the test segment's first end", 0.5e-9, 0)]
    [InlineData(30, "past the test segment's first end", 2e-9, 1)]
    [InlineData(30, "past the test segment's second end", 0.5e-9, 0)]
    [InlineData(30, "past the test segment's second end", 2e-9, 1)]
    public void SegmentsWithinANanometreTouch(double turn, string where, double gap, int bridges)
    {
        var blocker = where switch
        {
            "its first end above" => TurnedLine(turn, 43.0, (0, gap), (0, 5)),
            "its second end below" => TurnedLine(turn, 43.0, (0, -5), (0, -gap)),
            "past the test segment's first end" => TurnedLine(turn, 43.0, (-0.999 - gap, -5), (-0.999 - gap, 5)),
            _ => TurnedLine(turn, 43.0, (0.999 + gap, -5), (0.999 + gap, 5)),
        };
        var rule = OutlineCornerHeights.Prepare(new[]
        {
            TurnedLine(turn, 41.0, (-10, 0), (-1, 0)),
            TurnedLine(turn, 41.0, (1, 0), (10, 0)),
            blocker,
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

    // ---- each clause of the rule, at its edge -------------------------------------------------
    // The constants above are shared by several clauses; each scene below moves one clause across
    // its threshold. Lines A (41.00, ends at the origin heading +x) and B (41.00, ends at (10, 0)
    // heading -x) face each other across a gap unless the scene says otherwise.

    private static CornerLine LineA() => Line(41.0, (-10, 0), (0, 0));
    private static CornerLine LineB() => Line(41.0, (20, 0), (10, 0));

    [Theory]
    [InlineData(0.0009, 0)]   // A's ends 0.9 mm apart: A is closed, has no ends, no bridge
    [InlineData(0.0011, 1)]   // 1.1 mm: open, its end at the origin bridges to B
    public void ALineWhoseEndsAreWithinOneMillimetreIsClosed(double gap, int bridges)
    {
        var rule = OutlineCornerHeights.Prepare(new[]
        {
            Line(41.0, (0, 0), (-10, 0), (-10, -5), (0, -5), (0, -gap)),
            LineB(),
        });

        Assert.Equal(bridges, rule.Bridges.Count);
    }

    /// <summary>(x, y) turned by <paramref name="degrees"/> about the origin.</summary>
    private static (double X, double Y) Turn(double degrees, double x, double y)
    {
        var a = degrees * Math.PI / 180.0;
        return (x * Math.Cos(a) - y * Math.Sin(a), x * Math.Sin(a) + y * Math.Cos(a));
    }

    private static CornerLine TurnedLine(double degrees, double z, params (double X, double Y)[] pts) =>
        new(z, pts.Select(p => Turn(degrees, p.X, p.Y)).Select(p => new XyPoint(p.X, p.Y)).ToList());

    [Theory]
    // A and B turned 30 degrees, so both the x and the y part of the 1 mm count.
    [InlineData(true, 0.0009, 1)]    // a 43.00 line across the gap 0.9 mm from A's end: inside the 1 mm left out
    [InlineData(true, 0.0011, 0)]    // 1.1 mm: it crosses the tested part
    [InlineData(false, 0.0009, 1)]   // the same at B's end
    [InlineData(false, 0.0011, 0)]
    public void TheBridgeIsTestedOneMillimetreShortOfEachEnd(bool atA, double c, int bridges)
    {
        const double turn = 30;
        var x = atA ? c : 10 - c;
        var rule = OutlineCornerHeights.Prepare(new[]
        {
            TurnedLine(turn, 41.0, (-10, 0), (0, 0)),
            TurnedLine(turn, 41.0, (20, 0), (10, 0)),
            TurnedLine(turn, 43.0, (x, -1), (x, 1)),
        });

        Assert.Equal(bridges, rule.Bridges.Count);
    }

    [Theory]
    [InlineData(0.0009, 1)]   // the last vertex but one is 0.9 mm back: the direction comes from (-10, 0)
    [InlineData(0.0011, 0)]   // 1.1 mm back: from it, 70 degrees off B
    public void AnEndsDirectionComesFromTheFirstVertexOneMillimetreBack(double g, int bridges)
    {
        var a = 70.0 * Math.PI / 180.0;
        var rule = OutlineCornerHeights.Prepare(new[]
        {
            Line(41.0, (-10, 0), (-g * Math.Cos(a), -g * Math.Sin(a)), (0, 0)),
            LineB(),
        });

        Assert.Equal(bridges, rule.Bridges.Count);
    }

    [Theory]
    // Another 41.00 line `h` from A's end: above it, or a U round it (so the end lies inside the
    // other line's box and only the distance decides).
    [InlineData(false, 0.0009, 0)]   // 0.9 mm: the end is not free
    [InlineData(false, 0.0011, 1)]   // 1.1 mm: free, so it bridges to B
    [InlineData(true, 0.0009, 0)]
    [InlineData(true, 0.0011, 1)]
    public void AnEndIsFreeWithNoLineOfItsHeightWithinOneMillimetre(bool round, double h, int bridges)
    {
        var other = round
            ? Line(41.0, (-h, -0.01), (-h, h), (h, h), (h, 0.01))
            : Line(41.0, (0, h), (0, 5));
        var rule = OutlineCornerHeights.Prepare(new[] { LineA(), LineB(), other });

        Assert.Equal(bridges, rule.Bridges.Count);
    }

    [Theory]
    [InlineData(0.0004, 0)]   // that line at 41.0004 counts as the same height: not free
    [InlineData(0.0005, 0)]
    [InlineData(0.0006, 1)]   // 41.0006 is another height: free
    public void AnEndIsFreeOfLinesOfAnotherHeight(double dz, int bridges)
    {
        var rule = OutlineCornerHeights.Prepare(new[] { LineA(), LineB(), Line(41.0 + dz, (0, 0.0005), (0, 5)) });

        Assert.Equal(bridges, rule.Bridges.Count);
    }

    [Theory]
    [InlineData(0.0009, 0)]   // ends 0.9 mm apart are not free (each line is within 1 mm of the other end)
    [InlineData(0.0015, 1)]   // 1.5 mm: free, more than 1 mm apart, so they bridge
    public void EndsMoreThanOneMillimetreApartBridge(double g, int bridges)
    {
        var rule = OutlineCornerHeights.Prepare(new[] { LineA(), Line(41.0, (10 + g, 0), (g, 0)) });

        Assert.Equal(bridges, rule.Bridges.Count);
    }

    [Theory]
    [InlineData(0.0004, 1)]   // a line at 41.0004 across the gap is the bridge's height: no block
    [InlineData(0.0005, 1)]
    [InlineData(0.0006, 0)]   // 41.0006 blocks
    public void OnlyALineOfAnotherHeightBlocksABridge(double dz, int bridges)
    {
        var rule = OutlineCornerHeights.Prepare(new[] { LineA(), LineB(), Line(41.0 + dz, (5, -1), (5, 1)) });

        Assert.Equal(bridges, rule.Bridges.Count);
    }

    [Theory]
    [InlineData(0.5e-9, 8.660254)]   // B2's end is 0.5 nm further than B1's: equal, so the smaller x
    [InlineData(2e-9, 10.0)]         // 2 nm further: B1's end, the nearer
    public void AnEndPicksTheNearestFacingEndWithinANanometreBySmallerX(double delta, double otherEndX)
    {
        // A's end faces B1's end (10, 0) straight ahead and B2's end 30 degrees up, both pointing
        // back at it; B1 and B2 do not face each other.
        var a = 30.0 * Math.PI / 180.0;
        var k = 1 + delta / 10;
        double fx = 10 * Math.Cos(a) * k, fy = 10 * Math.Sin(a) * k;
        var rule = OutlineCornerHeights.Prepare(new[]
        {
            LineA(),
            Line(41.0, (20, 0), (10, 0)),
            Line(41.0, (fx + 10 * Math.Cos(a), fy + 10 * Math.Sin(a)), (fx, fy)),
        });

        var bridge = Assert.Single(rule.Bridges);
        Assert.Equal(otherEndX, bridge.PolylineMetres.Max(p => p.X), 6);
    }

    [Theory]
    [InlineData(true)]    // a nearer end 70 degrees off A's heading, pointing at A's end
    [InlineData(false)]   // a nearer end on A's heading, pointing 70 degrees off A's end
    public void BothEndsMustFaceEachOtherWithinSixtyDegrees(bool offAsHeading)
    {
        // Either way the nearer end does not face A's end, so A still bridges to B. A test that
        // checked only one of the two ends would let A pick the nearer end and lose the bridge.
        var a = 70.0 * Math.PI / 180.0;
        var nearer = offAsHeading
            ? Line(41.0, (10 * Math.Cos(a), 10 * Math.Sin(a)), (5 * Math.Cos(a), 5 * Math.Sin(a)))
            : Line(41.0, (5 + 5 * Math.Cos(a), -5 * Math.Sin(a)), (5, 0));
        var rule = OutlineCornerHeights.Prepare(new[] { LineA(), LineB(), nearer });

        var bridge = Assert.Single(rule.Bridges);
        Assert.Equal(10.0, bridge.PolylineMetres.Max(p => p.X), 9);
    }

    [Fact]
    public void ALineOnTheCornerIsFoundEvenWhenANearerLowerLineIsMeasuredFirst()
    {
        // 41.00 passes 0.2 mm from V, 43.00 0.8 mm: both are on V; the higher wins.
        var rule = OutlineCornerHeights.Prepare(new[]
        {
            Line(41.0, (-10, -0.0002), (10, -0.0002)),
            Line(43.0, (-10, 0.0008), (10, 0.0008)),
        });

        Assert.Equal(43.0, HeightOf(rule, 0, 0, "on two lines"), 9);
    }

    [Theory]
    // V at the origin. Line A (43.00) passes 1 m + delta below it, its nearest point (0, -1 - delta);
    // line B (41.00) passes 1 m to one side. Equally near, A is L1 (the higher): u = +y and B beyond
    // gives 42.00. B nearer: L1 = B, u points away from A, nothing beyond: left alone.
    // With the decoy (a 41.00 line 3 m away whose box holds V, so it is measured first) A is
    // measured before B too; the decoy is then the only line beyond B, at B's height: 41.00.
    [InlineData(-1.0, 0.5e-9, false, 42.0)]
    [InlineData(-1.0, 2e-9, false, null)]
    [InlineData(1.0, 0.5e-9, false, 42.0)]
    [InlineData(1.0, 2e-9, false, null)]
    [InlineData(-1.0, 0.5e-9, true, 42.0)]
    [InlineData(-1.0, 2e-9, true, 41.0)]
    [InlineData(1.0, 0.5e-9, true, 42.0)]
    [InlineData(1.0, 2e-9, true, 41.0)]
    public void TheNearestLinesWithinANanometreGiveTheHighest(double side, double delta, bool decoy, double? expected)
    {
        var a = side < 0 ? Line(43.0, (-0.5, -1 - delta), (0, -1 - delta)) : Line(43.0, (0, -1 - delta), (0.5, -1 - delta));
        var b = Line(41.0, (side, 0), (side, 1));
        var rule = OutlineCornerHeights.Prepare(decoy
            ? new[] { b, a, Line(41.0, (-3, 0.5), (-3, -3), (0.5, -3)) }
            : new[] { a, b });

        var h = rule.HeightAt(new XyPoint(0, 0));

        Assert.Empty(rule.Bridges);
        if (expected is null) Assert.True(h is null, $"side {side}, delta {delta}, decoy {decoy}: took {h}");
        else Assert.Equal(expected.Value, HeightOf(rule, 0, 0, $"side {side}, delta {delta}, decoy {decoy}"), 6);
    }

    [Theory]
    // As above with delta 0: A (43.00) and B (43.00 - dz, to the left) are equally near.
    [InlineData(0.0004, null)]      // within 0.5 mm of the highest: both stay, B has the smaller x: nothing beyond
    [InlineData(0.0005, null)]
    [InlineData(0.0006, 42.9997)]   // B drops out: L1 = A, B beyond
    public void TheNearestLinesWithinHalfAMillimetreOfTheHighestStay(double dz, double? expected)
    {
        var rule = OutlineCornerHeights.Prepare(new[]
        {
            Line(43.0, (-0.5, -1), (0, -1)),
            Line(43.0 - dz, (-1, 0), (-1, 1)),
        });

        var h = rule.HeightAt(new XyPoint(0, 0));

        Assert.Empty(rule.Bridges);
        if (expected is null) Assert.True(h is null, $"dz {dz}: took {h}");
        else Assert.Equal(expected.Value, HeightOf(rule, 0, 0, $"dz {dz}"), 9);
    }

    [Theory]
    // L1 (41.00) 0.5 m below V; 43.00 at y = 1 and 45.00 at y = 1 + delta, both straight beyond.
    [InlineData(0.5e-9, 42.333333)]   // equally near: the higher, 45.00
    [InlineData(2e-9, 41.666667)]     // 43.00 nearer
    public void CandidatesWithinANanometreGoHigherFirst(double delta, double expected)
    {
        var rule = OutlineCornerHeights.Prepare(new[]
        {
            Line(41.0, (-10, -0.5), (10, -0.5)),
            Line(43.0, (-10, 1), (10, 1)),
            Line(45.0, (-10, 1 + delta), (10, 1 + delta)),
        });

        Assert.Equal(expected, HeightOf(rule, 0, 0, $"delta {delta}"), 6);
    }

    [Theory]
    // L1 (41.00) 0.5 m below V; another 41.00 line at y = 1 (the nearest thing beyond, and the walk
    // meets it at t = 1); 43.00 at y = 1 + delta.
    [InlineData(0.5e-9, 41.666667)]   // within 1e-9 of the nearest beyond: L2 at once
    [InlineData(2e-9, 41.0)]          // beyond the walk's meeting: no L2
    public void ACandidateWithinANanometreOfTheNearestBeyondIsL2AtOnce(double delta, double expected)
    {
        var rule = OutlineCornerHeights.Prepare(new[]
        {
            Line(41.0, (-10, -0.5), (10, -0.5)),
            Line(41.0, (-10, 1), (10, 1)),
            Line(43.0, (-10, 1 + delta), (10, 1 + delta)),
        });

        Assert.Equal(expected, HeightOf(rule, 0, 0, $"delta {delta}"), 6);
    }

    [Theory]
    [InlineData(0.0009, 41.0)]        // a 41.00 vertex 0.9 mm beside the walk: met at t = 3, 43.00 is beyond
    [InlineData(0.0011, 41.331954)]   // 1.1 mm: not met; 43.00 at (-0.5, 5) is in view
    public void TheWalkMeetsAVertexWithinOneMillimetreOfIt(double w, double expected)
    {
        var rule = OutlineCornerHeights.Prepare(new[]
        {
            Line(41.0, (-10, -1), (10, -1)),
            Line(41.0, (w, 3), (5, 3)),
            Line(43.0, (-10, 5), (-0.5, 5)),
        });

        Assert.Equal(expected, HeightOf(rule, 0, 0, $"w {w}"), 6);
    }

    [Theory]
    // L1 (41.00) 1 m below V. A 41.00 line at 1.27 m hides line S from V; S crosses the walk at
    // y = 3 m. 43.00 at (3, 5) is in view, past S's crossing.
    [InlineData(0.0005, 41.0)]        // S at 41.0005 is h1's height: the walk meets it, 43.00 is beyond
    [InlineData(0.0006, 41.292785)]   // S at 41.0006 is another height (hidden): the walk goes on
    public void TheWalkMeetsOnlyLinesOfL1sHeight(double dz, double expected)
    {
        var rule = OutlineCornerHeights.Prepare(new[]
        {
            Line(41.0, (-10, -1), (10, -1)),
            Line(41.0, (-0.8, 1.25), (-0.2, 1.25)),
            Line(41.0 + dz, (-1, 2.5), (1, 3.5)),
            Line(43.0, (3, 5), (10, 5)),
        });

        Assert.Equal(expected, HeightOf(rule, 0, 0, $"dz {dz}"), 6);
    }

    [Theory]
    // L1 (41.00) 1 m below V; 43.00's nearest point is (3, 4), 5 m out. A 41.00 line crosses the
    // sight line `b` before that point.
    [InlineData(0.0009, 41.333333)]   // inside the last 1 mm, which the sight line leaves out: in view
    [InlineData(0.0011, 41.0)]        // 1.1 mm before: hidden
    public void TheSightLineStopsOneMillimetreShortOfTheCandidate(double b, double expected)
    {
        double ux = 0.6, uy = 0.8, tx = -0.8, ty = 0.6;
        double mx = ux * (5 - b), my = uy * (5 - b);
        var rule = OutlineCornerHeights.Prepare(new[]
        {
            Line(41.0, (-10, -1), (10, -1)),
            Line(43.0, (5 * ux + tx, 5 * uy + ty), (5 * ux - tx, 5 * uy - ty)),
            Line(41.0, (mx + 0.5 * tx, my + 0.5 * ty), (mx - 0.5 * tx, my - 0.5 * ty)),
        });

        Assert.Empty(rule.Bridges);
        Assert.Equal(expected, HeightOf(rule, 0, 0, $"b {b}"), 6);
    }

    [Theory]
    // One 41.00 line: 1 m + delta below V (its first segment), then 15 short segments round to a
    // last segment 1 m to the right of V, in the next run of 16 segments. A 43.00 line lies above.
    // With `belowFirst` the line goes on round V's left, so the run holding the part below V has the
    // nearer box and is measured first.
    [InlineData(false, 0.5e-9, 41.664457)]   // equally near: the nearest point with the smaller x, below V: u = +y
    [InlineData(false, 2e-9, 41.0)]          // the right one nearer: u = -x, nothing of another height beyond
    [InlineData(true, 0.5e-9, 41.664457)]
    [InlineData(true, 2e-9, 41.0)]
    public void TheNearestPointWithinANanometreHasTheSmallerXAcrossRunsOfSegments(bool belowFirst, double delta, double expected)
    {
        var poly = new List<XyPoint> { new(1, 0.5), new(1, -0.5) };
        for (var k = 1; k <= 15; k++) poly.Add(new XyPoint(1 - 0.5 * k / 15, -0.5 - (0.5 + delta) * k / 15));
        poly.Add(new XyPoint(-0.5, -1 - delta));
        if (belowFirst)
        {
            poly.Add(new XyPoint(-3, -1));
            poly.Add(new XyPoint(-3, 3));
        }
        var rule = OutlineCornerHeights.Prepare(new[]
        {
            new CornerLine(41.0, poly),
            Line(43.0, (0.2, 2), (1, 2)),
        });

        Assert.Equal(expected, HeightOf(rule, 0, 0, $"delta {delta}"), 6);
    }

    [Theory]
    // L1 (41.00) 1 m below V. The 43.00 line's far part is nearest at two points: (6, 8 - e), 10 m
    // - delta away, first; (-6, 8), 10 m, in a later run of segments, alone in it or not. A 41.00
    // line hides (-6, 8) from V.
    [InlineData(true, 0.5e-9, 41.0)]          // equally near: the smaller x, hidden: no L2
    [InlineData(true, 2e-9, 41.181818)]       // (6, 8 - e) nearer: in view
    [InlineData(false, 0.5e-9, 41.0)]
    [InlineData(false, 2e-9, 41.181818)]
    public void TheFarPartsNearestPointWithinANanometreHasTheSmallerXAcrossRunsOfSegments(bool leftAlone, double delta, double expected)
    {
        var yr = Math.Sqrt((10 - delta) * (10 - delta) - 36);
        var poly = new List<XyPoint> { new(7, yr), new(6, yr) };
        var rightRise = leftAlone ? 14 : 15;
        for (var k = 1; k <= rightRise; k++) poly.Add(new XyPoint(6, yr + (20 - yr) * k / rightRise));
        poly.Add(new XyPoint(-6, 20));
        for (var k = 1; k <= 5; k++) poly.Add(new XyPoint(-6, 20 - 12.0 * k / 5));
        poly.Add(new XyPoint(-7, 8));
        var rule = OutlineCornerHeights.Prepare(new[]
        {
            Line(41.0, (-10, -1), (10, -1)),
            Line(41.0, (-4, 4), (-2, 4)),
            new CornerLine(43.0, poly),
        });

        Assert.Equal(expected, HeightOf(rule, 0, 0, $"delta {delta}"), 6);
    }

    [Fact]
    public void ALastVertexThatMergesTakesEarlierOnesWithinAMicrometreWithIt()
    {
        // The last two vertices before the end lie within a micrometre of it (the first of them off
        // the line): read from either end the line keeps (0, 0), (10, 2e-6) and the end (10, 0), so
        // every corner gets the same height.
        var forward = new List<XyPoint>
        {
            new(0, 0), new(10, 0.000002), new(10.0000004, 0.0000005), new(10, -0.0000006), new(10, 0),
        };
        var backward = forward.AsEnumerable().Reverse().ToList();
        var top = Line(43.0, (-10, 10), (20, 10));
        var a = OutlineCornerHeights.Prepare(new[] { new CornerLine(41.0, forward), top });
        var b = OutlineCornerHeights.Prepare(new[] { new CornerLine(41.0, backward), top });

        for (var x = 0.5; x < 12; x += 0.5)
        {
            foreach (var y in new[] { 0.3, 1.0, 4.0 })
            {
                Assert.Equal(a.HeightAt(new XyPoint(x, y)), b.HeightAt(new XyPoint(x, y)));
            }
        }
    }

    [Fact]
    public void ALineThatCollapsesOntoItsFirstVertexIsLeftOutEitherWay()
    {
        var forward = new List<XyPoint> { new(0, 0), new(0.0000012, 0), new(0.0000006, 0) };
        var backward = forward.AsEnumerable().Reverse().ToList();

        Assert.Null(OutlineCornerHeights.Prepare(new[] { new CornerLine(41.0, forward) }).HeightAt(new XyPoint(0, 0.01)));
        Assert.Null(OutlineCornerHeights.Prepare(new[] { new CornerLine(41.0, backward) }).HeightAt(new XyPoint(0, 0.01)));
    }

    // ---- speed ------------------------------------------------------------------------------

    /// <summary>The scene and the limit on the best of three runs: the spec's 2,000 corners x 200
    /// lines of 20 segments under 1 s; 240-segment lines (tessellated arcs) under 2 s.</summary>
    public static IEnumerable<object[]> TimingScenes() => new[]
    {
        new object[] { "contours20", 1.0 },
        new object[] { "contours", 2.0 },
        new object[] { "arcs", 2.0 },
    };

    /// <summary>200 lines, 0.25 m apart in height, over a 100 m site: wavy contours across it (20
    /// or 240 segments each), or concentric 270-degree arcs of 240 segments.</summary>
    private static List<CornerLine> TimingLines(string scene)
    {
        var segments = scene == "contours20" ? 20 : 240;
        var lines = new List<CornerLine>();
        for (var i = 0; i < 200; i++)
        {
            var poly = new List<XyPoint>();
            for (var k = 0; k <= segments; k++)
            {
                if (scene != "arcs")
                {
                    var x = k * 100.0 / segments;
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
    public void TwoThousandPointsAgainstTwoHundredLines(string scene, double limitSeconds)
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
        Assert.True(times.Min() < TimeSpan.FromSeconds(limitSeconds),
            $"{scene}: best of three runs over {limitSeconds} s: {text}");
    }
}

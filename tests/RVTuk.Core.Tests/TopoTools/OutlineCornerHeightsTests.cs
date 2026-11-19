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

    [Fact]
    public void OrderAndDirectionDoNotMatter()
    {
        var vectors = Fixture.Value;
        var reversed = new Dictionary<int, OutlineCornerHeights>();
        foreach (var scene in vectors.Scenes.Values)
        {
            var lines = new List<CornerLine>();
            for (var i = scene.Lines.Count - 1; i >= 0; i--)
            {
                var poly = scene.Lines[i].PolylineMetres.ToList();
                poly.Reverse();
                lines.Add(new CornerLine(scene.Lines[i].HeightMetres, poly));
            }
            reversed.Add(scene.Number, OutlineCornerHeights.Prepare(lines));
        }

        var differences = new List<string>();
        foreach (var p in vectors.Points)
        {
            var v = new XyPoint(p.X, p.Y);
            var a = PreparedScene(p.Scene).HeightAt(v);
            var b = reversed[p.Scene].HeightAt(v);
            if (!Nullable.Equals(a, b)) differences.Add($"scene {p.Scene} ({p.X}, {p.Y}): {a} vs {b}");
        }
        Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences));
    }

    // ---- spot checks with the spec's numbers ---------------------------------------------

    private static CornerLine Line(double z, params (double X, double Y)[] pts) =>
        new(z, pts.Select(p => new XyPoint(p.X, p.Y)).ToList());

    [Fact]
    public void BetweenTwoParallelLinesTheHeightIsInProportion()
    {
        var rule = OutlineCornerHeights.Prepare(new[]
        {
            Line(41.0, (-20, 0), (20, 0)),
            Line(43.0, (-20, 10), (20, 10)),
        });

        Assert.Equal(41.5, rule.HeightAt(new XyPoint(0.3, 2.5))!.Value, 3);
    }

    [Fact]
    public void DanielsScreenshotCornersTakeTheirHeights()
    {
        var scene = Fixture.Value.Scenes.Values.First(s => s.Name.Contains("screenshot"));
        var rule = OutlineCornerHeights.Prepare(scene.Lines);

        Assert.Equal(41.199, rule.HeightAt(new XyPoint(7.09, -4.03))!.Value, 3);
        Assert.Equal(41.498, rule.HeightAt(new XyPoint(12.41, -6.71))!.Value, 3);
        Assert.Equal(42.886, rule.HeightAt(new XyPoint(12.41, -10.95))!.Value, 3);
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
    public void TwoThousandPointsAgainstTwoHundredLinesRunWellUnderASecond()
    {
        // 200 gently wavy contours of 21 vertices each, 0.25 m apart in height, across a 100 m site.
        var lines = new List<CornerLine>();
        for (var i = 0; i < 200; i++)
        {
            var poly = new List<XyPoint>();
            for (var k = 0; k <= 20; k++)
            {
                var x = k * 5.0;
                poly.Add(new XyPoint(x, i * 0.5 + 0.2 * Math.Sin(x / 7.0 + i)));
            }
            lines.Add(new CornerLine(40.0 + 0.25 * i, poly));
        }
        var points = new List<XyPoint>();
        var random = new Random(1);
        for (var n = 0; n < 2000; n++) points.Add(new XyPoint(random.NextDouble() * 100.0, random.NextDouble() * 100.0));

        var watch = Stopwatch.StartNew();
        var rule = OutlineCornerHeights.Prepare(lines);
        var prepared = watch.Elapsed;
        var moved = 0;
        foreach (var v in points) if (rule.HeightAt(v) is not null) moved++;
        watch.Stop();

        _output.WriteLine($"Prepare {prepared.TotalMilliseconds:F0} ms; Prepare + 2000 HeightAt {watch.Elapsed.TotalMilliseconds:F0} ms; {moved} moved");
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), $"took {watch.Elapsed.TotalMilliseconds:F0} ms");
    }
}

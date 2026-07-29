# Topo Tools Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Detail lines drawn on a `Topo_Line` style, each carrying a `TOPO_Elevation` shared
elevation, become points at that height on the toposolids beneath them — re-runnably.

**Architecture:** A dockable pane drives three Revit external events (setup, discover, apply).
Sampling, polygon containment and ledger encoding are pure Core code with xunit tests; everything
touching `SlabShapeEditor` lives in `RVTuk.Revit`. Each toposolid carries an Extensible Storage
ledger of `source line id → the points it owns there`, which is what makes a second run replace
rather than duplicate.

**Tech Stack:** C# (net48 for Revit 2024, net8.0-windows for 2025), Revit API 2024/2025, WPF/MVVM,
xunit.

**Spec:** [../specs/2026-07-29-topo-tools-design.md](../specs/2026-07-29-topo-tools-design.md) — read
it before starting; it carries the reasoning this plan only executes.

## Global Constraints

- **Revit 2024/2025 only.** `Toposolid` does not exist in 2023. Nothing in this plan may be added to
  `src/KKarea.Revit`, and no file here may be linked into it.
- **Canonical tool name `TopoTools`; ribbon label "Topo Tools".** Folder per project is `TopoTools/`
  and **namespace = root namespace + folder path, exactly** (`RVTuk.Core.TopoTools`,
  `RVTuk.UI.TopoTools.ViewModels`, `RVTuk.Revit.TopoTools`).
- **Layer discipline:** `RVTuk.Core` must not reference any Revit or WPF type. `RVTuk.UI` must not
  reference any Revit type — Revit reaches it only as `Func<>`/`Action` delegates.
- **Fixed names, spelled exactly:** line style `Topo_Line`; shared parameter `TOPO_Elevation`
  (Length, instance, bound to the Lines category); Extensible Storage vendor id `KnafoKlimor`.
- **Match tolerance** for finding a recorded vertex: `0.1 / 304.8` feet (0.1 mm).
- **Minimum point separation** when sampling: `spacing / 10`.
- All Revit API work runs on Revit's main thread through `ExternalEvent` +
  `ManualResetEventSlim` ping-pong. The pane calls those delegates from the thread pool, never from
  the WPF UI thread (it is Revit's main thread and would deadlock).
- Projects are SDK-style with default globbing: **new files need no `.csproj` edits.**
- Build check for any task touching `src/`: `dotnet build RVTuk.sln -c Release2024` and
  `dotnet build RVTuk.sln -c Release2025`.

---

### Task 1: Move `XyPoint` to Shared

`XyPoint` sits in `RVTuk.Core.AutoDimensions` and Topo Tools needs it too. The repo's rule is that a
file lives in a tool folder only while one tool uses it. Pure move — no behaviour changes.

**Files:**
- Create: `src/RVTuk.Core/Shared/Geometry/XyPoint.cs`
- Delete: `src/RVTuk.Core/AutoDimensions/XyPoint.cs`
- Modify (add one `using`): `src/RVTuk.Core/AutoDimensions/CandidateMatcher.cs`,
  `WallCrossingFinder.cs`, `WallCandidate.cs`, `ReferenceAlignment.cs`, `OpeningSegment.cs`;
  `src/RVTuk.Revit/AutoDimensions/DimensionCandidateCollector.cs`, `DimensionRunner.cs`;
  `tests/RVTuk.Core.Tests/AutoDimensions/WallCrossingFinderTests.cs`,
  `ReferenceAlignmentTests.cs`, `OpeningSegmentTests.cs`, `CandidateMatcherTests.cs`

**Interfaces:**
- Produces: `RVTuk.Core.Shared.Geometry.XyPoint` — `readonly record struct XyPoint(double X, double Y)`.
  Every later task uses this type.

- [ ] **Step 1: Confirm the suite is green before touching anything**

```bash
dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj
```

Expected: all tests pass. If they do not, stop — the baseline is broken and this move would mask it.

- [ ] **Step 2: Create the file in its new home**

Create `src/RVTuk.Core/Shared/Geometry/XyPoint.cs`:

```csharp
namespace RVTuk.Core.Shared.Geometry
{
    /// <summary>A plain 2D point (view-plane projection — Z is deliberately not represented).</summary>
    public readonly record struct XyPoint(double X, double Y);
}
```

- [ ] **Step 3: Delete the old file**

```bash
git rm src/RVTuk.Core/AutoDimensions/XyPoint.cs
```

- [ ] **Step 4: Add the using to every file that referenced it**

Add `using RVTuk.Core.Shared.Geometry;` to each of the eleven files listed above, next to the
existing `using` lines (in the four test files, above the `namespace` line — they use file-scoped
namespaces). Add nothing else; the type name is unchanged everywhere.

- [ ] **Step 5: Build both configurations**

```bash
dotnet build RVTuk.sln -c Release2024
```

Expected: build succeeded. Any `CS0246: The type or namespace name 'XyPoint' could not be found` names
a file that still needs the using.

```bash
dotnet build RVTuk.sln -c Release2025
```

Expected: build succeeded.

- [ ] **Step 6: Re-run the suite**

```bash
dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj
```

Expected: the same tests pass as in Step 1.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "refactor(core): XyPoint is shared, not Auto Dimensions' alone"
```

---

### Task 2: `TopoLineSampler`

The heart of the tool, and pure. Turns a tessellated polyline plus a spacing into the points that
will land on the toposolid.

**Files:**
- Create: `src/RVTuk.Core/TopoTools/TopoLineSampler.cs`
- Test: `tests/RVTuk.Core.Tests/TopoTools/TopoLineSamplerTests.cs`

**Interfaces:**
- Consumes: `RVTuk.Core.Shared.Geometry.XyPoint` (Task 1).
- Produces: `RVTuk.Core.TopoTools.TopoLineSampler.Sample(IReadOnlyList<XyPoint> polyline, double spacing)`
  → `IReadOnlyList<XyPoint>`. Throws `ArgumentOutOfRangeException` when `spacing <= 0`.

- [ ] **Step 1: Write the failing tests**

Create `tests/RVTuk.Core.Tests/TopoTools/TopoLineSamplerTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using RVTuk.Core.Shared.Geometry;
using RVTuk.Core.TopoTools;
using Xunit;

namespace RVTuk.Core.Tests.TopoTools;

public class TopoLineSamplerTests
{
    private const double Tolerance = 1e-9;

    private static List<XyPoint> Line(params (double X, double Y)[] points)
    {
        var polyline = new List<XyPoint>();
        foreach (var (x, y) in points) polyline.Add(new XyPoint(x, y));
        return polyline;
    }

    [Fact]
    public void DividesALongSegmentEvenly()
    {
        var sampled = TopoLineSampler.Sample(Line((0, 0), (10, 0)), 2);

        Assert.Equal(6, sampled.Count);
        for (int i = 0; i < sampled.Count; i++)
        {
            Assert.Equal(i * 2.0, sampled[i].X, Tolerance);
            Assert.Equal(0, sampled[i].Y, Tolerance);
        }
    }

    [Fact]
    public void DividesUnevenLengthsSoNoGapExceedsTheSpacing()
    {
        // 5 long at spacing 2 → 3 divisions of 1.667, never a gap over 2.
        var sampled = TopoLineSampler.Sample(Line((0, 0), (5, 0)), 2);

        Assert.Equal(4, sampled.Count);
        for (int i = 1; i < sampled.Count; i++)
            Assert.True(sampled[i].X - sampled[i - 1].X <= 2 + Tolerance);
    }

    [Fact]
    public void KeepsEveryVertexOfAPolylineWhenSegmentsAreShorterThanTheSpacing()
    {
        var sampled = TopoLineSampler.Sample(Line((0, 0), (0, 3), (4, 3)), 10);

        Assert.Equal(3, sampled.Count);
        Assert.Equal(new XyPoint(0, 0), sampled[0]);
        Assert.Equal(new XyPoint(0, 3), sampled[1]);
        Assert.Equal(new XyPoint(4, 3), sampled[2]);
    }

    [Fact]
    public void DropsTheRepeatedClosingPointOfAClosedLoop()
    {
        // Curve.Tessellate() hands back the start point again as the last point.
        var sampled = TopoLineSampler.Sample(
            Line((0, 0), (10, 0), (10, 10), (0, 10), (0, 0)), 50);

        Assert.Equal(4, sampled.Count);
        Assert.Equal(new XyPoint(0, 10), sampled[3]);
    }

    [Fact]
    public void DropsANearDuplicateVertex()
    {
        // A tessellated tight arc emits vertices a hair apart; they must not become slivers.
        var sampled = TopoLineSampler.Sample(Line((0, 0), (0.01, 0), (10, 0)), 5);

        Assert.Equal(3, sampled.Count);
        Assert.Equal(0, sampled[0].X, Tolerance);
        Assert.Equal(10, sampled[2].X, Tolerance);
    }

    [Fact]
    public void CollapsesALineShorterThanATenthOfTheSpacingToOnePoint()
    {
        var sampled = TopoLineSampler.Sample(Line((0, 0), (0.5, 0)), 100);

        Assert.Single(sampled);
    }

    [Fact]
    public void ReturnsNothingForAnEmptyPolyline()
    {
        Assert.Empty(TopoLineSampler.Sample(new List<XyPoint>(), 1));
    }

    [Fact]
    public void RejectsANonPositiveSpacing()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TopoLineSampler.Sample(Line((0, 0), (1, 0)), 0));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj --filter TopoLineSamplerTests
```

Expected: FAIL to compile — `CS0246: The type or namespace name 'TopoLineSampler' could not be found`.

- [ ] **Step 3: Write the implementation**

Create `src/RVTuk.Core/TopoTools/TopoLineSampler.cs`:

```csharp
using System;
using System.Collections.Generic;
using RVTuk.Core.Shared.Geometry;

namespace RVTuk.Core.TopoTools
{
    /// <summary>
    /// Turns a tessellated polyline into the points a topo line contributes to the toposolid.
    ///
    /// Every vertex is a candidate, and any segment longer than the spacing is divided evenly, so
    /// no gap exceeds the spacing and arcs keep their shape. A candidate is dropped when it lands
    /// within a tenth of the spacing of the previous kept point **or of the first** — the second
    /// test is what stops a closed loop, whose tessellation repeats the start point at the end,
    /// from stacking two coincident points. Vertices are not exempt: a tight arc tessellates into
    /// vertices millimetres apart, and those are slivers, not detail.
    ///
    /// Even division already keeps interior candidates more than half a spacing apart, so the drop
    /// test only ever fires on genuine near-duplicates.
    /// </summary>
    public static class TopoLineSampler
    {
        public static IReadOnlyList<XyPoint> Sample(IReadOnlyList<XyPoint> polyline, double spacing)
        {
            if (spacing <= 0)
                throw new ArgumentOutOfRangeException(nameof(spacing), "Spacing must be positive.");

            var kept = new List<XyPoint>();
            if (polyline == null || polyline.Count == 0) return kept;

            double minSeparation = spacing / 10.0;

            void Offer(XyPoint candidate)
            {
                if (kept.Count == 0) { kept.Add(candidate); return; }
                if (Distance(kept[kept.Count - 1], candidate) < minSeparation) return;
                if (Distance(kept[0], candidate) < minSeparation) return;
                kept.Add(candidate);
            }

            Offer(polyline[0]);

            for (int i = 1; i < polyline.Count; i++)
            {
                var from = polyline[i - 1];
                var to = polyline[i];
                double length = Distance(from, to);

                if (length > spacing)
                {
                    int divisions = (int)Math.Ceiling(length / spacing);
                    for (int step = 1; step < divisions; step++)
                    {
                        double t = (double)step / divisions;
                        Offer(new XyPoint(
                            from.X + (to.X - from.X) * t,
                            from.Y + (to.Y - from.Y) * t));
                    }
                }

                Offer(to);
            }

            return kept;
        }

        private static double Distance(XyPoint a, XyPoint b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj --filter TopoLineSamplerTests
```

Expected: PASS, 8 tests.

- [ ] **Step 5: Commit**

```bash
git add src/RVTuk.Core/TopoTools/TopoLineSampler.cs tests/RVTuk.Core.Tests/TopoTools/TopoLineSamplerTests.cs
git commit -m "feat(topo-tools): a line becomes evenly spaced points, without the slivers"
```

---

### Task 3: `PolygonContainment`

Decides whether a sampled point falls inside a toposolid's footprint.

**Files:**
- Create: `src/RVTuk.Core/TopoTools/PolygonContainment.cs`
- Test: `tests/RVTuk.Core.Tests/TopoTools/PolygonContainmentTests.cs`

**Interfaces:**
- Consumes: `RVTuk.Core.Shared.Geometry.XyPoint` (Task 1).
- Produces: `RVTuk.Core.TopoTools.PolygonContainment.Contains(IReadOnlyList<IReadOnlyList<XyPoint>> loops, XyPoint point, double edgeTolerance = 1e-9)`
  → `bool`. Even-odd across all loops, so a second loop reads as a hole; a point on a boundary
  counts as inside.

- [ ] **Step 1: Write the failing tests**

Create `tests/RVTuk.Core.Tests/TopoTools/PolygonContainmentTests.cs`:

```csharp
using System.Collections.Generic;
using RVTuk.Core.Shared.Geometry;
using RVTuk.Core.TopoTools;
using Xunit;

namespace RVTuk.Core.Tests.TopoTools;

public class PolygonContainmentTests
{
    private static IReadOnlyList<XyPoint> Loop(params (double X, double Y)[] points)
    {
        var loop = new List<XyPoint>();
        foreach (var (x, y) in points) loop.Add(new XyPoint(x, y));
        return loop;
    }

    private static IReadOnlyList<IReadOnlyList<XyPoint>> Loops(params IReadOnlyList<XyPoint>[] loops)
        => new List<IReadOnlyList<XyPoint>>(loops);

    private static readonly IReadOnlyList<XyPoint> Square =
        Loop((0, 0), (10, 0), (10, 10), (0, 10));

    [Fact]
    public void FindsAPointInsideASquare()
    {
        Assert.True(PolygonContainment.Contains(Loops(Square), new XyPoint(5, 5)));
    }

    [Fact]
    public void RejectsAPointOutsideASquare()
    {
        Assert.False(PolygonContainment.Contains(Loops(Square), new XyPoint(15, 5)));
    }

    [Fact]
    public void CountsAPointOnAnEdgeAsInside()
    {
        Assert.True(PolygonContainment.Contains(Loops(Square), new XyPoint(10, 5)));
    }

    [Fact]
    public void CountsAPointOnACornerAsInside()
    {
        Assert.True(PolygonContainment.Contains(Loops(Square), new XyPoint(0, 0)));
    }

    [Fact]
    public void RejectsAPointInTheNotchOfAConcaveFootprint()
    {
        // An L: the notch is the missing top-right quadrant.
        var shape = Loop((0, 0), (10, 0), (10, 5), (5, 5), (5, 10), (0, 10));

        Assert.True(PolygonContainment.Contains(Loops(shape), new XyPoint(2, 8)));
        Assert.False(PolygonContainment.Contains(Loops(shape), new XyPoint(8, 8)));
    }

    [Fact]
    public void ReadsASecondLoopAsAHole()
    {
        var hole = Loop((4, 4), (6, 4), (6, 6), (4, 6));

        Assert.False(PolygonContainment.Contains(Loops(Square, hole), new XyPoint(5, 5)));
        Assert.True(PolygonContainment.Contains(Loops(Square, hole), new XyPoint(1, 1)));
    }

    [Fact]
    public void ToleratesTheRepeatedClosingPointTessellationLeaves()
    {
        var closed = Loop((0, 0), (10, 0), (10, 10), (0, 10), (0, 0));

        Assert.True(PolygonContainment.Contains(Loops(closed), new XyPoint(5, 5)));
        Assert.False(PolygonContainment.Contains(Loops(closed), new XyPoint(50, 5)));
    }

    [Fact]
    public void RejectsEverythingWhenThereAreNoLoops()
    {
        Assert.False(PolygonContainment.Contains(Loops(), new XyPoint(0, 0)));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj --filter PolygonContainmentTests
```

Expected: FAIL to compile — `CS0246: The type or namespace name 'PolygonContainment' could not be found`.

- [ ] **Step 3: Write the implementation**

Create `src/RVTuk.Core/TopoTools/PolygonContainment.cs`:

```csharp
using System;
using System.Collections.Generic;
using RVTuk.Core.Shared.Geometry;

namespace RVTuk.Core.TopoTools
{
    /// <summary>
    /// Even-odd containment across every loop of a footprint, so a second loop reads as a hole
    /// without anyone having to say which loop is the outer one.
    ///
    /// A point on a boundary counts as inside: the ray-crossing rule alone answers edge cases
    /// arbitrarily, and a sampled point landing exactly on a toposolid's edge belongs to it.
    /// The loops arrive tessellated curve by curve, so they contain repeated points and
    /// zero-length segments; both are handled rather than assumed away.
    /// </summary>
    public static class PolygonContainment
    {
        public static bool Contains(
            IReadOnlyList<IReadOnlyList<XyPoint>> loops, XyPoint point, double edgeTolerance = 1e-9)
        {
            if (loops == null || loops.Count == 0) return false;

            foreach (var loop in loops)
            {
                if (loop == null || loop.Count < 2) continue;
                for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
                {
                    if (DistanceToSegment(point, loop[j], loop[i]) <= edgeTolerance) return true;
                }
            }

            bool inside = false;
            foreach (var loop in loops)
            {
                if (loop == null || loop.Count < 3) continue;
                for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
                {
                    var a = loop[i];
                    var b = loop[j];
                    if ((a.Y > point.Y) != (b.Y > point.Y) &&
                        point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                    {
                        inside = !inside;
                    }
                }
            }
            return inside;
        }

        private static double DistanceToSegment(XyPoint point, XyPoint a, XyPoint b)
        {
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double lengthSquared = dx * dx + dy * dy;

            if (lengthSquared <= 0) return Distance(point, a);

            double t = ((point.X - a.X) * dx + (point.Y - a.Y) * dy) / lengthSquared;
            t = Math.Max(0, Math.Min(1, t));
            return Distance(point, new XyPoint(a.X + t * dx, a.Y + t * dy));
        }

        private static double Distance(XyPoint a, XyPoint b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj --filter PolygonContainmentTests
```

Expected: PASS, 8 tests.

- [ ] **Step 5: Commit**

```bash
git add src/RVTuk.Core/TopoTools/PolygonContainment.cs tests/RVTuk.Core.Tests/TopoTools/PolygonContainmentTests.cs
git commit -m "feat(topo-tools): a point belongs to the footprint that encloses it"
```

---

### Task 4: `XyzPoint` and `TopoPointLedgerCodec`

The ledger's storage format. Extensible Storage has no 64-bit integer field and no map key type, so
the whole map becomes one string — the same constraint `IdMapCodec` solved for Auto Dimensions.

**Files:**
- Create: `src/RVTuk.Core/TopoTools/XyzPoint.cs`
- Create: `src/RVTuk.Core/TopoTools/TopoPointLedgerCodec.cs`
- Test: `tests/RVTuk.Core.Tests/TopoTools/TopoPointLedgerCodecTests.cs`

**Interfaces:**
- Produces: `RVTuk.Core.TopoTools.XyzPoint` — `readonly record struct XyzPoint(double X, double Y, double Z)`.
- Produces: `RVTuk.Core.TopoTools.TopoPointLedgerCodec.Encode(IReadOnlyDictionary<long, IReadOnlyList<XyzPoint>>)`
  → `string`, and `.Decode(string?)` → `IReadOnlyDictionary<long, IReadOnlyList<XyzPoint>>`.
  Decoding anything malformed yields an empty map rather than throwing.

- [ ] **Step 1: Write the failing tests**

Create `tests/RVTuk.Core.Tests/TopoTools/TopoPointLedgerCodecTests.cs`:

```csharp
using System.Collections.Generic;
using RVTuk.Core.TopoTools;
using Xunit;

namespace RVTuk.Core.Tests.TopoTools;

public class TopoPointLedgerCodecTests
{
    private static Dictionary<long, IReadOnlyList<XyzPoint>> Ledger(
        params (long LineId, XyzPoint[] Points)[] entries)
    {
        var ledger = new Dictionary<long, IReadOnlyList<XyzPoint>>();
        foreach (var (lineId, points) in entries) ledger[lineId] = points;
        return ledger;
    }

    [Fact]
    public void RoundTripsSeveralLinesAndPoints()
    {
        var original = Ledger(
            (412, new[] { new XyzPoint(1.5, -2.25, 40.125), new XyzPoint(3, 4, 40.125) }),
            (9007199254740993, new[] { new XyzPoint(-0.1, 0, -12.75) }));

        var decoded = TopoPointLedgerCodec.Decode(TopoPointLedgerCodec.Encode(original));

        Assert.Equal(2, decoded.Count);
        Assert.Equal(2, decoded[412].Count);
        Assert.Equal(new XyzPoint(1.5, -2.25, 40.125), decoded[412][0]);
        Assert.Equal(new XyzPoint(3, 4, 40.125), decoded[412][1]);
        Assert.Equal(new XyzPoint(-0.1, 0, -12.75), decoded[9007199254740993][0]);
    }

    [Fact]
    public void SurvivesCoordinatesThatNeedFullPrecision()
    {
        var point = new XyzPoint(1.0 / 3.0, 2.0 / 7.0, 123.456789012345);
        var original = Ledger((1, new[] { point }));

        var decoded = TopoPointLedgerCodec.Decode(TopoPointLedgerCodec.Encode(original));

        Assert.Equal(point, decoded[1][0]);
    }

    [Fact]
    public void EncodesAnEmptyLedgerAsAnEmptyString()
    {
        Assert.Equal("", TopoPointLedgerCodec.Encode(Ledger()));
    }

    [Fact]
    public void SkipsALineWithNoPoints()
    {
        var encoded = TopoPointLedgerCodec.Encode(Ledger(
            (1, new XyzPoint[0]),
            (2, new[] { new XyzPoint(0, 0, 0) })));

        var decoded = TopoPointLedgerCodec.Decode(encoded);

        Assert.Single(decoded);
        Assert.True(decoded.ContainsKey(2));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a ledger")]
    [InlineData("12=1,2")]
    [InlineData("abc=1,2,3")]
    public void DecodesAnythingMalformedAsEmpty(string? encoded)
    {
        Assert.Empty(TopoPointLedgerCodec.Decode(encoded));
    }

    [Fact]
    public void KeepsTheGoodEntriesWhenOneIsCorrupt()
    {
        var decoded = TopoPointLedgerCodec.Decode("5=1,2,3;broken;7=4,5,6");

        Assert.Equal(2, decoded.Count);
        Assert.Equal(new XyzPoint(1, 2, 3), decoded[5][0]);
        Assert.Equal(new XyzPoint(4, 5, 6), decoded[7][0]);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj --filter TopoPointLedgerCodecTests
```

Expected: FAIL to compile — `CS0246: The type or namespace name 'XyzPoint' could not be found`.

- [ ] **Step 3: Write `XyzPoint`**

Create `src/RVTuk.Core/TopoTools/XyzPoint.cs`:

```csharp
namespace RVTuk.Core.TopoTools
{
    /// <summary>
    /// A point in model space, in Revit's internal units. Only Topo Tools needs three dimensions —
    /// the shared <see cref="RVTuk.Core.Shared.Geometry.XyPoint"/> stays two-dimensional.
    /// </summary>
    public readonly record struct XyzPoint(double X, double Y, double Z);
}
```

- [ ] **Step 4: Write the codec**

Create `src/RVTuk.Core/TopoTools/TopoPointLedgerCodec.cs`:

```csharp
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RVTuk.Core.TopoTools
{
    /// <summary>
    /// The per-toposolid ledger as one string: "lineId=x,y,z|x,y,z;lineId=x,y,z". Extensible
    /// Storage documents neither a 64-bit integer field nor a map key type, and Revit 2024+ element
    /// ids are 64-bit — the same corner <see cref="IdMapCodec"/> turned for Auto Dimensions.
    ///
    /// Coordinates use round-trip ("R") formatting: a ledger position is matched back to a live
    /// vertex within a tenth of a millimetre, so a lossy format would quietly orphan points.
    /// Decoding never throws — a ledger written by a future version, or damaged, must degrade to
    /// "this toposolid has no tracked points" rather than break the run.
    /// </summary>
    public static class TopoPointLedgerCodec
    {
        public static string Encode(IReadOnlyDictionary<long, IReadOnlyList<XyzPoint>> ledger)
        {
            if (ledger == null) return "";

            return string.Join(";", ledger
                .Where(entry => entry.Value != null && entry.Value.Count > 0)
                .Select(entry =>
                    entry.Key.ToString(CultureInfo.InvariantCulture) + "=" +
                    string.Join("|", entry.Value.Select(Format))));
        }

        public static IReadOnlyDictionary<long, IReadOnlyList<XyzPoint>> Decode(string? encoded)
        {
            var ledger = new Dictionary<long, IReadOnlyList<XyzPoint>>();
            if (string.IsNullOrWhiteSpace(encoded)) return ledger;

            foreach (var entry in encoded!.Split(';'))
            {
                var halves = entry.Split('=');
                if (halves.Length != 2) continue;
                if (!long.TryParse(halves[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var lineId))
                    continue;

                var points = new List<XyzPoint>();
                foreach (var text in halves[1].Split('|'))
                {
                    var parts = text.Split(',');
                    if (parts.Length != 3) continue;
                    if (!TryParse(parts[0], out var x)) continue;
                    if (!TryParse(parts[1], out var y)) continue;
                    if (!TryParse(parts[2], out var z)) continue;
                    points.Add(new XyzPoint(x, y, z));
                }

                if (points.Count > 0) ledger[lineId] = points;
            }
            return ledger;
        }

        private static string Format(XyzPoint point) =>
            point.X.ToString("R", CultureInfo.InvariantCulture) + "," +
            point.Y.ToString("R", CultureInfo.InvariantCulture) + "," +
            point.Z.ToString("R", CultureInfo.InvariantCulture);

        private static bool TryParse(string text, out double value) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj --filter TopoPointLedgerCodecTests
```

Expected: PASS, 11 tests (the `[Theory]` contributes 6).

- [ ] **Step 6: Commit**

```bash
git add src/RVTuk.Core/TopoTools tests/RVTuk.Core.Tests/TopoTools/TopoPointLedgerCodecTests.cs
git commit -m "feat(topo-tools): the ledger survives a round trip through one string"
```

---

### Task 5: Core scope models and the persisted spacing

What the pane is handed, and where the spacing lives between sessions. No logic, so no tests — the
gate is that both configurations build and the suite stays green.

**Files:**
- Create: `src/RVTuk.Core/TopoTools/TopoLineStatus.cs`
- Create: `src/RVTuk.Core/TopoTools/TopoLineInfo.cs`
- Create: `src/RVTuk.Core/TopoTools/TopoScope.cs`
- Modify: `src/RVTuk.Core/Shared/Config/AppConfig.cs` (add one property)

**Interfaces:**
- Produces: `enum TopoLineStatus { Ready, NoElevation, OutsideToposolid }`
- Produces: `sealed record TopoLineInfo(long LineId, string ElevationText, string LengthText, int PointCount, TopoLineStatus Status)`
  with computed `string StatusText` and `string DisplayName` for the pane to bind.
- Produces: `sealed record TopoScope(bool IsProjectSetUp, string ViewName, IReadOnlyList<TopoLineInfo> Lines, int ToposolidCount, string Message)`
- Produces: `AppConfig.TopoPointSpacingMillimetres` (double, default 1000).

- [ ] **Step 1: Write the status enum**

Create `src/RVTuk.Core/TopoTools/TopoLineStatus.cs`:

```csharp
namespace RVTuk.Core.TopoTools
{
    /// <summary>Why a topo line will or will not contribute points on the next run.</summary>
    public enum TopoLineStatus
    {
        /// <summary>Has an elevation, and its points land on a toposolid.</summary>
        Ready,

        /// <summary>On the Topo_Line style but its TOPO_Elevation was never filled in.</summary>
        NoElevation,

        /// <summary>Has an elevation, but no sampled point falls inside any toposolid's footprint.</summary>
        OutsideToposolid,
    }
}
```

- [ ] **Step 2: Write the line info record**

Create `src/RVTuk.Core/TopoTools/TopoLineInfo.cs`:

```csharp
namespace RVTuk.Core.TopoTools
{
    /// <summary>
    /// One row of the pane's list: what this line is about to do, before anything is pressed.
    /// The two texts are pre-formatted by the Revit layer through <c>UnitFormatUtils</c>, because
    /// only it knows the document's display units — Core never guesses at millimetres.
    /// </summary>
    public sealed record TopoLineInfo(
        long LineId,
        string ElevationText,
        string LengthText,
        int PointCount,
        TopoLineStatus Status)
    {
        public string DisplayName => $"Line {LineId}";

        public string StatusText => Status switch
        {
            TopoLineStatus.Ready => $"{ElevationText} · {LengthText} · {PointCount} point(s)",
            TopoLineStatus.NoElevation => $"{LengthText} · no elevation set",
            TopoLineStatus.OutsideToposolid => $"{ElevationText} · outside every toposolid",
            _ => ElevationText,
        };

        public bool IsReady => Status == TopoLineStatus.Ready;
    }
}
```

- [ ] **Step 3: Write the scope record**

Create `src/RVTuk.Core/TopoTools/TopoScope.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace RVTuk.Core.TopoTools
{
    /// <summary>Everything one discovery pass tells the pane.</summary>
    public sealed record TopoScope(
        bool IsProjectSetUp,
        string ViewName,
        IReadOnlyList<TopoLineInfo> Lines,
        int ToposolidCount,
        string Message)
    {
        public static TopoScope NotSetUp(string viewName) =>
            new TopoScope(false, viewName, Array.Empty<TopoLineInfo>(), 0,
                "This project has no Topo_Line style or TOPO_Elevation parameter yet.");

        public static TopoScope Unavailable(string viewName, string message) =>
            new TopoScope(true, viewName, Array.Empty<TopoLineInfo>(), 0, message);
    }
}
```

- [ ] **Step 4: Add the spacing to `AppConfig`**

In `src/RVTuk.Core/Shared/Config/AppConfig.cs`, add this property inside the `AppConfig` class,
immediately after the `DwgExportSheetSetName` property:

```csharp
        /// <summary>Topo Tools: distance between generated toposolid points, in millimetres.</summary>
        public double TopoPointSpacingMillimetres { get; set; } = 1000;
```

- [ ] **Step 5: Build both configurations and run the suite**

```bash
dotnet build RVTuk.sln -c Release2024
```

Expected: build succeeded.

```bash
dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj
```

Expected: every existing test still passes, including the three new Topo Tools test classes.

- [ ] **Step 6: Commit**

```bash
git add src/RVTuk.Core
git commit -m "feat(topo-tools): what the pane is told, and a spacing that persists"
```

---

### Task 6: The `Topo_Line` style, the `TOPO_Elevation` parameter, and the setup event

Everything the project needs before a topo line can be drawn at all. This is why setup is a button
and not a side effect of running.

**Files:**
- Create: `src/RVTuk.Revit/TopoTools/TopoLineStyle.cs`
- Create: `src/RVTuk.Revit/TopoTools/TopoElevationParameter.cs`
- Create: `src/RVTuk.Revit/TopoTools/ExternalEvents/TopoSetupEventHandler.cs`

**Interfaces:**
- Produces: `TopoLineStyle.LineStyleName` (`"Topo_Line"`), `.Exists(Document)`, `.EnsureExists(Document)`,
  `.IsTopoLine(CurveElement)`.
- Produces: `TopoElevationParameter.ParamName` (`"TOPO_Elevation"`), `.IsBound(Document)`,
  `.EnsureBound(Document)`, `.TryGetElevation(Element, out double elevationFeet)`.
- Produces: `TopoSetupEventHandler` with `Reset()`, `WaitForCompletion()`, `Summary` (string),
  and `GetName()`.

- [ ] **Step 1: Write the line style helper**

Create `src/RVTuk.Revit/TopoTools/TopoLineStyle.cs`:

```csharp
using Autodesk.Revit.DB;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// The dedicated line subcategory ("Topo_Line") that marks a detail line as a topo contour —
    /// the same arrangement Auto Dimensions uses for "Dimensions_Line". Unlike that one this is not
    /// auto-created on first use: you cannot draw the line before the style exists, so creating it
    /// is the pane's explicit setup action.
    /// </summary>
    public static class TopoLineStyle
    {
        public const string LineStyleName = "Topo_Line";

        public static bool Exists(Document doc) =>
            doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines)
                .SubCategories.Contains(LineStyleName);

        public static void EnsureExists(Document doc)
        {
            var linesCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            if (linesCategory.SubCategories.Contains(LineStyleName)) return;
            doc.Settings.Categories.NewSubcategory(linesCategory, LineStyleName);
        }

        public static bool IsTopoLine(CurveElement curveElement)
        {
            return curveElement.LineStyle is GraphicsStyle style
                && style.GraphicsStyleCategory != null
                && style.GraphicsStyleCategory.Name == LineStyleName;
        }
    }
}
```

- [ ] **Step 2: Write the parameter helper**

Create `src/RVTuk.Revit/TopoTools/TopoElevationParameter.cs`:

```csharp
using System;
using System.IO;
using System.Text;
using Autodesk.Revit.DB;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// The shared parameter carrying a topo line's shared (survey) elevation. A Length, so the
    /// value is typed and displayed in the project's own units instead of a bare number with an
    /// assumed unit.
    ///
    /// Bound through a temporary shared-parameter file because the API cannot create non-shared
    /// project parameters — the technique <c>UsageKeyScheduleBuilder</c> already uses for the RZ_*
    /// parameters, sharing its temp file so a project only ever grows one.
    /// </summary>
    public static class TopoElevationParameter
    {
        public const string ParamName = "TOPO_Elevation";

        // Stable for the life of the tool: changing it would orphan every value already typed.
        private static readonly Guid ParamGuid = new Guid("b8f4a1d6-3e27-4c95-9a10-2d7c6e5b8f34");

        public static bool IsBound(Document doc)
        {
            var linesCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);

            var iterator = doc.ParameterBindings.ForwardIterator();
            while (iterator.MoveNext())
            {
                if (iterator.Key is Definition definition &&
                    definition.Name == ParamName &&
                    iterator.Current is ElementBinding binding &&
                    binding.Categories.Contains(linesCategory))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Caller must already be inside a transaction.</summary>
        public static void EnsureBound(Document doc)
        {
            if (IsBound(doc)) return;

            var app = doc.Application;
            var originalFile = app.SharedParametersFilename;
            try
            {
                var tempFile = Path.Combine(Path.GetTempPath(), "RVTuk_SharedParams.txt");
                if (!File.Exists(tempFile))
                {
                    File.WriteAllText(tempFile, "", Encoding.Unicode);
                }

                app.SharedParametersFilename = tempFile;
                var sharedFile = app.OpenSharedParameterFile()
                    ?? throw new InvalidOperationException(
                        "Could not open the temporary shared-parameter file.");

                var group = sharedFile.Groups.get_Item("RVTuk") ?? sharedFile.Groups.Create("RVTuk");
                var definition = group.Definitions.get_Item(ParamName) as ExternalDefinition
                    ?? (ExternalDefinition)group.Definitions.Create(
                        new ExternalDefinitionCreationOptions(ParamName, SpecTypeId.Length)
                        {
                            GUID = ParamGuid,
                        });

                var categories = app.Create.NewCategorySet();
                categories.Insert(doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines));

                // GroupTypeId.General is today's ForgeTypeId for what Revit's UI used to label
                // "Other" — keeps this out of the crowded Identity Data group.
                doc.ParameterBindings.Insert(
                    definition, app.Create.NewInstanceBinding(categories), GroupTypeId.General);
            }
            finally
            {
                app.SharedParametersFilename = originalFile;
            }
        }

        /// <summary>
        /// The line's shared elevation in internal units, or false when it was never filled in.
        /// Judged by <c>HasValue</c>, never by the number: 0.000 is a legitimate shared elevation
        /// and must not read as "forgotten".
        /// </summary>
        public static bool TryGetElevation(Element line, out double elevationFeet)
        {
            elevationFeet = 0;

            var parameter = line.LookupParameter(ParamName);
            if (parameter == null || !parameter.HasValue) return false;
            if (parameter.StorageType != StorageType.Double) return false;

            elevationFeet = parameter.AsDouble();
            return true;
        }
    }
}
```

- [ ] **Step 3: Write the setup external event**

Create `src/RVTuk.Revit/TopoTools/ExternalEvents/TopoSetupEventHandler.cs`:

```csharp
using System;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RVTuk.Revit.TopoTools.ExternalEvents
{
    /// <summary>
    /// Creates the Topo_Line style and binds TOPO_Elevation, on Revit's main thread and inside one
    /// transaction. Follows the LevelDiscoveryEventHandler ping-pong pattern: the pane raises this
    /// from the pool and blocks on WaitForCompletion.
    /// </summary>
    public class TopoSetupEventHandler : IExternalEventHandler
    {
        private readonly ManualResetEventSlim _done = new(false);

        public string Summary { get; private set; } = "";

        public void Reset() => _done.Reset();
        public void WaitForCompletion() => _done.Wait();

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    Summary = "No document is open.";
                    return;
                }

                using (var tx = new Transaction(doc, "Topo Tools — set up this project"))
                {
                    tx.Start();
                    try
                    {
                        TopoLineStyle.EnsureExists(doc);
                        TopoElevationParameter.EnsureBound(doc);
                        tx.Commit();
                        Summary = "Ready: draw detail lines on the Topo_Line style and give each a " +
                                  "TOPO_Elevation.";
                    }
                    catch (Exception ex)
                    {
                        tx.RollBack();
                        Summary = "Setup failed, and nothing was changed: " + ex.Message;
                    }
                }
            }
            catch (Exception ex)
            {
                Summary = "Setup failed: " + ex.Message;
            }
            finally
            {
                _done.Set();
            }
        }

        public string GetName() => "RVTuk.TopoToolsSetupEventHandler";
    }
}
```

- [ ] **Step 4: Build both configurations**

```bash
dotnet build RVTuk.sln -c Release2024
```

Expected: build succeeded.

```bash
dotnet build RVTuk.sln -c Release2025
```

Expected: build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/RVTuk.Revit/TopoTools
git commit -m "feat(topo-tools): a project gets a Topo_Line style and a TOPO_Elevation"
```

---

### Task 7: Collecting the lines and the toposolids, and routing between them

**Files:**
- Create: `src/RVTuk.Revit/TopoTools/TopoLineCollector.cs`
- Create: `src/RVTuk.Revit/TopoTools/ToposolidTarget.cs`
- Create: `src/RVTuk.Revit/TopoTools/ToposolidCollector.cs`
- Create: `src/RVTuk.Revit/TopoTools/ToposolidRouter.cs`

**Interfaces:**
- Consumes: `TopoLineStyle.IsTopoLine`, `TopoElevationParameter.TryGetElevation` (Task 6);
  `PolygonContainment.Contains` (Task 3); `XyPoint` (Task 1).
- Produces: `sealed record TopoLineCandidate(long LineId, double? ElevationFeet, IReadOnlyList<XyPoint> Polyline, double LengthFeet)`
- Produces: `TopoLineCollector.Collect(Document, View)` → `IReadOnlyList<TopoLineCandidate>`
- Produces: `sealed class ToposolidTarget` with `Solid` (`Toposolid`), `Loops`
  (`IReadOnlyList<IReadOnlyList<XyPoint>>`), `MinZ`, `MaxZ` (double), `Id` (long)
- Produces: `ToposolidCollector.Collect(Document, View)` → `IReadOnlyList<ToposolidTarget>`
- Produces: `ToposolidRouter.Route(IReadOnlyList<ToposolidTarget>, XyPoint, double z)` → `ToposolidTarget?`

- [ ] **Step 1: Write the line collector**

Create `src/RVTuk.Revit/TopoTools/TopoLineCollector.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RVTuk.Core.Shared.Geometry;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>One topo line as the runner needs it: where it goes, and how high.</summary>
    public sealed record TopoLineCandidate(
        long LineId,
        double? ElevationFeet,
        IReadOnlyList<XyPoint> Polyline,
        double LengthFeet);

    /// <summary>
    /// Reads the view's topo lines. Detail curves only: a model line given the Topo_Line style
    /// would appear in every plan at once, which is exactly the confusion view-specific lines were
    /// chosen to avoid, so it is ignored rather than half-supported.
    ///
    /// The curve's Z is the view's sketch plane and is discarded — which is only sound in a plan
    /// view, and the runner is what enforces that.
    /// </summary>
    public static class TopoLineCollector
    {
        public static IReadOnlyList<TopoLineCandidate> Collect(Document doc, View view)
        {
            var candidates = new List<TopoLineCandidate>();

            var lines = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(CurveElement))
                .Cast<CurveElement>()
                .Where(curveElement => curveElement.CurveElementType == CurveElementType.DetailCurve)
                .Where(TopoLineStyle.IsTopoLine);

            foreach (var line in lines)
            {
                var curve = line.GeometryCurve;
                if (curve == null) continue;

                var polyline = curve.Tessellate()
                    .Select(point => new XyPoint(point.X, point.Y))
                    .ToList();
                if (polyline.Count < 2) continue;

                double? elevation = TopoElevationParameter.TryGetElevation(line, out var feet)
                    ? feet
                    : (double?)null;

                candidates.Add(new TopoLineCandidate(line.Id.Value, elevation, polyline, curve.Length));
            }

            return candidates;
        }
    }
}
```

- [ ] **Step 2: Write the toposolid target**

Create `src/RVTuk.Revit/TopoTools/ToposolidTarget.cs`:

```csharp
using System.Collections.Generic;
using Autodesk.Revit.DB;
using RVTuk.Core.Shared.Geometry;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// A toposolid with the two things routing needs: its plan footprint and its vertical extent.
    /// Both are read once per run — the footprint costs a sketch traversal and the extent a
    /// bounding box, and a run asks about them once per sampled point.
    /// </summary>
    public sealed class ToposolidTarget
    {
        public ToposolidTarget(
            Toposolid solid, IReadOnlyList<IReadOnlyList<XyPoint>> loops, double minZ, double maxZ)
        {
            Solid = solid;
            Loops = loops;
            MinZ = minZ;
            MaxZ = maxZ;
        }

        public Toposolid Solid { get; }
        public IReadOnlyList<IReadOnlyList<XyPoint>> Loops { get; }
        public double MinZ { get; }
        public double MaxZ { get; }

        public long Id => Solid.Id.Value;
        public string Name => Solid.Name;
    }
}
```

- [ ] **Step 3: Write the toposolid collector**

Create `src/RVTuk.Revit/TopoTools/ToposolidCollector.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RVTuk.Core.Shared.Geometry;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// The toposolids a run may write to: those visible in the view, so phase, design option and
    /// worksets already decide what "the toposolid below" means without this tool re-implementing
    /// any of it.
    /// </summary>
    public static class ToposolidCollector
    {
        public static IReadOnlyList<ToposolidTarget> Collect(Document doc, View view)
        {
            var targets = new List<ToposolidTarget>();

            var solids = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_Toposolid)
                .WhereElementIsNotElementType()
                .OfType<Toposolid>();

            foreach (var solid in solids)
            {
                // A subdivision carries a shape of its own; its points belong to the host, and
                // writing to both would have them fight each other.
                if (solid.HostTopoId != null && solid.HostTopoId != ElementId.InvalidElementId) continue;

                var loops = ReadLoops(doc, solid);
                if (loops.Count == 0) continue;

                var box = solid.get_BoundingBox(null);
                if (box == null) continue;

                targets.Add(new ToposolidTarget(solid, loops, box.Min.Z, box.Max.Z));
            }

            return targets;
        }

        /// <summary>
        /// The footprint, loop by loop. Each curve is tessellated separately, so consecutive curves
        /// repeat their shared endpoint — harmless, and <see cref="RVTuk.Core.TopoTools.PolygonContainment"/>
        /// is written to tolerate exactly that.
        /// </summary>
        private static IReadOnlyList<IReadOnlyList<XyPoint>> ReadLoops(Document doc, Toposolid solid)
        {
            var loops = new List<IReadOnlyList<XyPoint>>();

            if (doc.GetElement(solid.SketchId) is not Sketch sketch) return loops;

            foreach (CurveArray loop in sketch.Profile)
            {
                var points = new List<XyPoint>();
                foreach (Curve curve in loop)
                {
                    foreach (var point in curve.Tessellate())
                    {
                        points.Add(new XyPoint(point.X, point.Y));
                    }
                }
                if (points.Count >= 3) loops.Add(points);
            }

            return loops;
        }
    }
}
```

- [ ] **Step 4: Write the router**

Create `src/RVTuk.Revit/TopoTools/ToposolidRouter.cs`:

```csharp
using System.Collections.Generic;
using RVTuk.Core.Shared.Geometry;
using RVTuk.Core.TopoTools;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// "The toposolid below (or above)", decided per point: the one whose footprint encloses it
    /// and whose vertical extent is nearest it. Ties go to the lower element id, so a re-run never
    /// shuffles a point between two overlapping toposolids.
    /// </summary>
    public static class ToposolidRouter
    {
        public static ToposolidTarget? Route(
            IReadOnlyList<ToposolidTarget> targets, XyPoint point, double z)
        {
            ToposolidTarget? best = null;
            double bestDistance = 0;

            foreach (var target in targets)
            {
                if (!PolygonContainment.Contains(target.Loops, point)) continue;

                double distance =
                    z < target.MinZ ? target.MinZ - z :
                    z > target.MaxZ ? z - target.MaxZ :
                    0;

                if (best == null ||
                    distance < bestDistance ||
                    (distance == bestDistance && target.Id < best.Id))
                {
                    best = target;
                    bestDistance = distance;
                }
            }

            return best;
        }
    }
}
```

- [ ] **Step 5: Build both configurations**

```bash
dotnet build RVTuk.sln -c Release2024
```

Expected: build succeeded. If `HostTopoId` reports a type mismatch, read its declared type from the
error and adjust the guard — the intent is "skip subdivisions", however that id is exposed.

```bash
dotnet build RVTuk.sln -c Release2025
```

Expected: build succeeded.

- [ ] **Step 6: Commit**

```bash
git add src/RVTuk.Revit/TopoTools
git commit -m "feat(topo-tools): find the lines, the toposolids, and which belongs to which"
```

---

### Task 8: The ledger store and the point applier

The only code that writes geometry, and the only code that has to cope with points having no
identity.

**Files:**
- Create: `src/RVTuk.Revit/TopoTools/TopoLedgerStore.cs`
- Create: `src/RVTuk.Revit/TopoTools/TopoPointApplier.cs`

**Interfaces:**
- Consumes: `TopoPointLedgerCodec`, `XyzPoint` (Task 4); `ToposolidTarget` (Task 7).
- Produces: `TopoLedgerStore.Read(Element toposolid)` → `IReadOnlyDictionary<long, IReadOnlyList<XyzPoint>>`,
  and `.Write(Element toposolid, IReadOnlyDictionary<long, IReadOnlyList<XyzPoint>> ledger)`.
- Produces: `sealed record ApplyOutcome(int Added, int Removed, int Missing)`
- Produces: `TopoPointApplier.Apply(ToposolidTarget target, IReadOnlyCollection<long> lineIdsSeen, IReadOnlyDictionary<long, IReadOnlyList<XyzPoint>> newPointsByLine, Func<long, bool> lineStillExists)`
  → `ApplyOutcome`. Caller must already be inside a transaction.
- Produces: `TopoPointApplier.MatchToleranceFeet` (0.1 mm in feet).

- [ ] **Step 1: Write the ledger store**

Create `src/RVTuk.Revit/TopoTools/TopoLedgerStore.cs`:

```csharp
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using RVTuk.Core.TopoTools;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// Per toposolid: which points each topo line owns on it. Invisible to the user and travelling
    /// with the element.
    ///
    /// Deliberately stored on the **toposolid**, not on the line as AutoDimensionTracker does.
    /// Storage on a line dies with the line, orphaning its points forever; on the toposolid, a run
    /// sees a recorded line id that no longer resolves and cleans up after it.
    /// </summary>
    public static class TopoLedgerStore
    {
        private static readonly Guid SchemaGuid = new Guid("3a6c50f1-8b47-4d2e-95c3-1f8a4b7e0d62");
        private const string SchemaName = "RVTukTopoToolsPointLedger";
        private const string FieldName = "PointsByLineId";

        public static IReadOnlyDictionary<long, IReadOnlyList<XyzPoint>> Read(Element toposolid)
        {
            var schema = Schema.Lookup(SchemaGuid);
            if (schema == null) return new Dictionary<long, IReadOnlyList<XyzPoint>>();

            var entity = toposolid.GetEntity(schema);
            if (!entity.IsValid()) return new Dictionary<long, IReadOnlyList<XyzPoint>>();

            return TopoPointLedgerCodec.Decode(entity.Get<string>(FieldName));
        }

        /// <summary>Caller must already be inside a transaction.</summary>
        public static void Write(
            Element toposolid, IReadOnlyDictionary<long, IReadOnlyList<XyzPoint>> ledger)
        {
            var schema = GetOrCreateSchema();
            var entity = new Entity(schema);
            entity.Set(FieldName, TopoPointLedgerCodec.Encode(ledger));
            toposolid.SetEntity(entity);
        }

        private static Schema GetOrCreateSchema()
        {
            var existing = Schema.Lookup(SchemaGuid);
            if (existing != null) return existing;

            var builder = new SchemaBuilder(SchemaGuid);
            builder.SetSchemaName(SchemaName);
            builder.SetVendorId("KnafoKlimor");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField(FieldName, typeof(string));
            return builder.Finish();
        }
    }
}
```

- [ ] **Step 2: Write the applier**

Create `src/RVTuk.Revit/TopoTools/TopoPointApplier.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RVTuk.Core.TopoTools;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>What one toposolid's share of a run did.</summary>
    public sealed record ApplyOutcome(int Added, int Removed, int Missing);

    /// <summary>
    /// Writes one toposolid's points, and the only place that copes with a point having no
    /// identity: <c>SlabShapeVertex</c> is a position, not an element, so the ledger stores
    /// coordinates and a re-run matches them back within a tenth of a millimetre.
    ///
    /// The positions recorded are the ones <c>AddPoints</c> hands back, not the ones asked for, so
    /// snapping or rounding inside Revit cannot drift the ledger away from the model. A recorded
    /// point with no vertex within tolerance was moved or deleted by hand since the last run: it is
    /// left alone and counted, never guessed at.
    /// </summary>
    public static class TopoPointApplier
    {
        /// <summary>0.1 mm, in Revit's internal feet.</summary>
        public const double MatchToleranceFeet = 0.1 / 304.8;

        /// <summary>Caller must already be inside a transaction.</summary>
        public static ApplyOutcome Apply(
            ToposolidTarget target,
            IReadOnlyCollection<long> lineIdsSeen,
            IReadOnlyDictionary<long, IReadOnlyList<XyzPoint>> newPointsByLine,
            Func<long, bool> lineStillExists)
        {
            var ledger = new Dictionary<long, IReadOnlyList<XyzPoint>>();
            foreach (var entry in TopoLedgerStore.Read(target.Solid)) ledger[entry.Key] = entry.Value;

            var editor = target.Solid.GetSlabShapeEditor();
            if (editor == null) return new ApplyOutcome(0, 0, 0);
            if (!editor.IsEnabled) editor.Enable();

            // Every line this run saw, plus every line since deleted. Not only the lines producing
            // points: a line whose elevation was cleared must lose last run's points too.
            var staleLineIds = ledger.Keys
                .Where(lineId => lineIdsSeen.Contains(lineId) || !lineStillExists(lineId))
                .ToList();

            int removed = 0;
            int missing = 0;

            if (staleLineIds.Count > 0)
            {
                var live = new List<SlabShapeVertex>();
                foreach (SlabShapeVertex vertex in editor.SlabShapeVertices) live.Add(vertex);

                foreach (var lineId in staleLineIds)
                {
                    foreach (var recorded in ledger[lineId])
                    {
                        var match = FindVertex(live, recorded);
                        if (match == null)
                        {
                            missing++;
                            continue;
                        }

                        live.Remove(match);
                        if (match.IsValidObject && editor.DeletePoint(match)) removed++;
                        else missing++;
                    }
                    ledger.Remove(lineId);
                }
            }

            int added = 0;
            foreach (var entry in newPointsByLine)
            {
                if (entry.Value.Count == 0) continue;

                IList<XYZ> points = entry.Value
                    .Select(point => new XYZ(point.X, point.Y, point.Z))
                    .ToList();

                IList<SlabShapeVertex> created = editor.AddPoints(points);

                var recorded = new List<XyzPoint>();
                foreach (var vertex in created)
                {
                    var position = vertex.Position;
                    recorded.Add(new XyzPoint(position.X, position.Y, position.Z));
                }

                if (recorded.Count > 0) ledger[entry.Key] = recorded;
                added += recorded.Count;
            }

            TopoLedgerStore.Write(target.Solid, ledger);
            return new ApplyOutcome(added, removed, missing);
        }

        private static SlabShapeVertex? FindVertex(List<SlabShapeVertex> live, XyzPoint recorded)
        {
            foreach (var vertex in live)
            {
                var position = vertex.Position;
                if (Math.Abs(position.X - recorded.X) <= MatchToleranceFeet &&
                    Math.Abs(position.Y - recorded.Y) <= MatchToleranceFeet &&
                    Math.Abs(position.Z - recorded.Z) <= MatchToleranceFeet)
                {
                    return vertex;
                }
            }
            return null;
        }
    }
}
```

- [ ] **Step 3: Build both configurations**

```bash
dotnet build RVTuk.sln -c Release2024
```

Expected: build succeeded.

**If `editor.AddPoints(points)` fails to convert to `IList<SlabShapeVertex>`,** the reference
assembly reports only `IList` for that return. Read the exact type from the compiler error and use
it; if it is a non-generic `IList`, change the loop to
`foreach (SlabShapeVertex vertex in created)` and declare `created` as `System.Collections.IList`.
Do **not** fall back to recording the requested positions — recording what Revit returned is the
point of this design.

```bash
dotnet build RVTuk.sln -c Release2025
```

Expected: build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/RVTuk.Revit/TopoTools
git commit -m "feat(topo-tools): points a run owns, remembered by where Revit put them"
```

---

### Task 9: The runner and the discover/apply events

Where the four stages become one operation, and the only place that decides a run is impossible.

**Files:**
- Create: `src/RVTuk.Revit/TopoTools/TopoRunner.cs`
- Create: `src/RVTuk.Revit/TopoTools/ExternalEvents/TopoDiscoveryEventHandler.cs`
- Create: `src/RVTuk.Revit/TopoTools/ExternalEvents/TopoApplyEventHandler.cs`

**Interfaces:**
- Consumes: everything from Tasks 2–8.
- Produces: `TopoRunner.Discover(Document, View?, double spacingFeet)` → `TopoScope`
- Produces: `TopoRunner.Apply(Document, View?, double spacingFeet)` → `string` summary
- Produces: `TopoDiscoveryEventHandler` — `Prepare(double spacingMillimetres)`, `Reset()`,
  `WaitForCompletion()`, `Result` (`TopoScope`)
- Produces: `TopoApplyEventHandler` — `Prepare(double spacingMillimetres)`, `Reset()`,
  `WaitForCompletion()`, `Summary` (string)

- [ ] **Step 1: Write the runner**

Create `src/RVTuk.Revit/TopoTools/TopoRunner.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RVTuk.Core.Shared.Geometry;
using RVTuk.Core.TopoTools;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// Collect, sample, route, apply. Discovery and application share one planning pass, so what
    /// the pane lists is exactly what a run will do — there is no second implementation to drift.
    /// </summary>
    public static class TopoRunner
    {
        private sealed class PlannedLine
        {
            public long LineId;
            public TopoLineStatus Status;
            public string ElevationText = "";
            public string LengthText = "";
            public readonly List<(ToposolidTarget Target, XyzPoint Point)> Points = new();
        }

        public static TopoScope Discover(Document doc, View? activeView, double spacingFeet)
        {
            string viewName = activeView?.Name ?? "";

            if (!TopoLineStyle.Exists(doc) || !TopoElevationParameter.IsBound(doc))
                return TopoScope.NotSetUp(viewName);

            if (activeView is not ViewPlan plan)
                return TopoScope.Unavailable(viewName,
                    "Topo lines are read from plan views only — open a floor or site plan.");

            var targets = ToposolidCollector.Collect(doc, plan);
            var planned = BuildPlan(doc, plan, spacingFeet, targets);

            var lines = planned
                .Select(line => new TopoLineInfo(
                    line.LineId, line.ElevationText, line.LengthText, line.Points.Count, line.Status))
                .ToList();

            return new TopoScope(true, plan.Name, lines, targets.Count, Describe(lines, targets.Count));
        }

        public static string Apply(Document doc, View? activeView, double spacingFeet)
        {
            if (!TopoLineStyle.Exists(doc) || !TopoElevationParameter.IsBound(doc))
                return "This project has no Topo_Line style or TOPO_Elevation parameter yet.";

            if (activeView is not ViewPlan plan)
                return "Topo lines are read from plan views only — open a floor or site plan.";

            var targets = ToposolidCollector.Collect(doc, plan);
            if (targets.Count == 0)
                return "No toposolid is visible in this view, so there is nothing to shape.";

            var planned = BuildPlan(doc, plan, spacingFeet, targets);
            var lineIdsSeen = planned.Select(line => line.LineId).ToList();

            using (var tx = new Transaction(doc, "Topo Tools — apply points"))
            {
                tx.Start();
                try
                {
                    int added = 0, removed = 0, missing = 0;

                    // Every target, not only those receiving points: a line dragged onto the
                    // neighbouring toposolid must lose its points on the one it left, and that
                    // toposolid's ledger is the only record of them.
                    foreach (var target in targets)
                    {
                        var newPointsByLine = new Dictionary<long, IReadOnlyList<XyzPoint>>();
                        foreach (var line in planned.Where(line => line.Status == TopoLineStatus.Ready))
                        {
                            var points = line.Points
                                .Where(entry => entry.Target.Id == target.Id)
                                .Select(entry => entry.Point)
                                .ToList();
                            if (points.Count > 0) newPointsByLine[line.LineId] = points;
                        }

                        var outcome = TopoPointApplier.Apply(
                            target,
                            lineIdsSeen,
                            newPointsByLine,
                            lineId => doc.GetElement(new ElementId(lineId)) != null);

                        added += outcome.Added;
                        removed += outcome.Removed;
                        missing += outcome.Missing;
                    }

                    tx.Commit();
                    return Summarise(planned, added, removed, missing);
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return "Topo Tools failed, and nothing was changed: " + ex.Message;
                }
            }
        }

        private static List<PlannedLine> BuildPlan(
            Document doc, ViewPlan view, double spacingFeet, IReadOnlyList<ToposolidTarget> targets)
        {
            // The whole shared→internal conversion: the shared elevation of the internal origin,
            // subtracted. Rotation and true north do not enter into Z.
            double sharedElevationOfInternalZero =
                doc.ActiveProjectLocation.GetProjectPosition(XYZ.Zero).Elevation;

            var planned = new List<PlannedLine>();

            foreach (var candidate in TopoLineCollector.Collect(doc, view))
            {
                var line = new PlannedLine
                {
                    LineId = candidate.LineId,
                    LengthText = FormatLength(doc, candidate.LengthFeet),
                };

                if (candidate.ElevationFeet == null)
                {
                    line.Status = TopoLineStatus.NoElevation;
                    line.ElevationText = "—";
                    planned.Add(line);
                    continue;
                }

                line.ElevationText = FormatLength(doc, candidate.ElevationFeet.Value);
                double z = candidate.ElevationFeet.Value - sharedElevationOfInternalZero;

                foreach (var xy in TopoLineSampler.Sample(candidate.Polyline, spacingFeet))
                {
                    var target = ToposolidRouter.Route(targets, xy, z);
                    if (target == null) continue;
                    line.Points.Add((target, new XyzPoint(xy.X, xy.Y, z)));
                }

                line.Status = line.Points.Count == 0
                    ? TopoLineStatus.OutsideToposolid
                    : TopoLineStatus.Ready;

                planned.Add(line);
            }

            return planned;
        }

        private static string FormatLength(Document doc, double feet) =>
            UnitFormatUtils.Format(doc.GetUnits(), SpecTypeId.Length, feet, false);

        private static string Describe(IReadOnlyList<TopoLineInfo> lines, int toposolidCount)
        {
            if (toposolidCount == 0)
                return "No toposolid is visible in this view.";
            if (lines.Count == 0)
                return "No topo lines in this view — draw detail lines on the Topo_Line style.";

            int ready = lines.Count(line => line.Status == TopoLineStatus.Ready);
            int points = lines.Sum(line => line.PointCount);
            return $"{ready} of {lines.Count} line(s) ready, {points} point(s) over " +
                   $"{toposolidCount} toposolid(s).";
        }

        private static string Summarise(
            IReadOnlyList<PlannedLine> planned, int added, int removed, int missing)
        {
            var parts = new List<string> { $"{added} point(s) added, {removed} replaced" };

            int noElevation = planned.Count(line => line.Status == TopoLineStatus.NoElevation);
            if (noElevation > 0) parts.Add($"{noElevation} line(s) skipped for having no elevation");

            int outside = planned.Count(line => line.Status == TopoLineStatus.OutsideToposolid);
            if (outside > 0) parts.Add($"{outside} line(s) fell outside every toposolid");

            if (missing > 0)
                parts.Add($"{missing} earlier point(s) had been moved by hand and were left alone");

            return string.Join("; ", parts) + ".";
        }
    }
}
```

- [ ] **Step 2: Write the discovery event**

Create `src/RVTuk.Revit/TopoTools/ExternalEvents/TopoDiscoveryEventHandler.cs`:

```csharp
using System;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RVTuk.Core.TopoTools;

namespace RVTuk.Revit.TopoTools.ExternalEvents
{
    /// <summary>
    /// Marshals one discovery pass onto Revit's main thread for the pane, which triggers it from a
    /// background thread (raise + WaitForCompletion on the UI thread would deadlock). Read-only —
    /// no transaction.
    /// </summary>
    public class TopoDiscoveryEventHandler : IExternalEventHandler
    {
        private readonly ManualResetEventSlim _done = new(false);
        private double _spacingMillimetres = 1000;

        public TopoScope Result { get; private set; } = TopoScope.Unavailable("", "No document is open.");

        public void Prepare(double spacingMillimetres) => _spacingMillimetres = spacingMillimetres;
        public void Reset() => _done.Reset();
        public void WaitForCompletion() => _done.Wait();

        public void Execute(UIApplication app)
        {
            try
            {
                var uiDoc = app.ActiveUIDocument;
                var doc = uiDoc?.Document;
                if (doc == null)
                {
                    Result = TopoScope.Unavailable("", "No document is open.");
                    return;
                }

                double spacingFeet = UnitUtils.ConvertToInternalUnits(
                    _spacingMillimetres, UnitTypeId.Millimeters);

                Result = TopoRunner.Discover(doc, uiDoc!.ActiveView, spacingFeet);
            }
            catch (Exception ex)
            {
                Result = TopoScope.Unavailable("", "Could not read this view: " + ex.Message);
            }
            finally
            {
                _done.Set();
            }
        }

        public string GetName() => "RVTuk.TopoToolsDiscoveryEventHandler";
    }
}
```

- [ ] **Step 3: Write the apply event**

Create `src/RVTuk.Revit/TopoTools/ExternalEvents/TopoApplyEventHandler.cs`:

```csharp
using System;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RVTuk.Revit.TopoTools.ExternalEvents
{
    /// <summary>
    /// Runs the points onto the toposolids on Revit's main thread. The transaction lives inside
    /// TopoRunner.Apply, so a failure there rolls back and comes out as a message rather than an
    /// exception.
    /// </summary>
    public class TopoApplyEventHandler : IExternalEventHandler
    {
        private readonly ManualResetEventSlim _done = new(false);
        private double _spacingMillimetres = 1000;

        public string Summary { get; private set; } = "";

        public void Prepare(double spacingMillimetres) => _spacingMillimetres = spacingMillimetres;
        public void Reset() => _done.Reset();
        public void WaitForCompletion() => _done.Wait();

        public void Execute(UIApplication app)
        {
            try
            {
                var uiDoc = app.ActiveUIDocument;
                var doc = uiDoc?.Document;
                if (doc == null)
                {
                    Summary = "No document is open.";
                    return;
                }

                double spacingFeet = UnitUtils.ConvertToInternalUnits(
                    _spacingMillimetres, UnitTypeId.Millimeters);

                Summary = TopoRunner.Apply(doc, uiDoc!.ActiveView, spacingFeet);
            }
            catch (Exception ex)
            {
                Summary = "Topo Tools failed: " + ex.Message;
            }
            finally
            {
                _done.Set();
            }
        }

        public string GetName() => "RVTuk.TopoToolsApplyEventHandler";
    }
}
```

- [ ] **Step 4: Build both configurations**

```bash
dotnet build RVTuk.sln -c Release2024
```

Expected: build succeeded.

```bash
dotnet build RVTuk.sln -c Release2025
```

Expected: build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/RVTuk.Revit/TopoTools
git commit -m "feat(topo-tools): one pass plans the run, and the same pass performs it"
```

---

### Task 10: The pane

**Files:**
- Create: `src/RVTuk.UI/TopoTools/ViewModels/TopoToolsPaneViewModel.cs`
- Create: `src/RVTuk.UI/TopoTools/Views/TopoToolsPaneView.xaml`
- Create: `src/RVTuk.UI/TopoTools/Views/TopoToolsPaneView.xaml.cs`

**Interfaces:**
- Consumes: `RVTuk.Core.TopoTools.TopoScope`, `TopoLineInfo` (Task 5);
  `RVTuk.Core.Shared.Config.ConfigManager`/`AppConfig` (Task 5).
- Produces: `TopoToolsPaneViewModel(Func<double, TopoScope> discover, Func<double, string> apply, Func<string> setUpProject)`
  with `Refresh()`, and bindable `SpacingMillimetres`, `Lines`, `ViewName`, `IsProjectSetUp`,
  `NeedsSetup`, `StatusMessage`, `IsBusy`, `SetUpCommand`, `RefreshCommand`, `ApplyCommand`.
- Produces: `TopoToolsPaneView` (a `UserControl`).

- [ ] **Step 1: Write the view model**

Create `src/RVTuk.UI/TopoTools/ViewModels/TopoToolsPaneViewModel.cs`:

```csharp
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Threading;
using RVTuk.Core.Shared.Config;
using RVTuk.Core.TopoTools;
using RVTuk.UI.Shared.ViewModels;

namespace RVTuk.UI.TopoTools.ViewModels
{
    /// <summary>
    /// The Topo Tools pane. All three Revit interactions arrive as delegates that block on an
    /// ExternalEvent ping-pong, so all three are invoked from the thread pool and their results
    /// marshalled back through the dispatcher — never called on the WPF UI thread, which is Revit's
    /// main thread and would deadlock waiting for its own event.
    /// </summary>
    public class TopoToolsPaneViewModel : ViewModelBase
    {
        private readonly Func<double, TopoScope> _discover;
        private readonly Func<double, string> _apply;
        private readonly Func<string> _setUpProject;
        private readonly Dispatcher _dispatcher;

        public TopoToolsPaneViewModel(
            Func<double, TopoScope> discover,
            Func<double, string> apply,
            Func<string> setUpProject)
        {
            _discover = discover;
            _apply = apply;
            _setUpProject = setUpProject;
            _dispatcher = Dispatcher.CurrentDispatcher;

            Lines = new ObservableCollection<TopoLineInfo>();
            SetUpCommand = new RelayCommand(RunSetUp, () => !IsBusy);
            RefreshCommand = new RelayCommand(Refresh, () => !IsBusy);
            ApplyCommand = new RelayCommand(RunApply, () => !IsBusy && IsProjectSetUp);

            try
            {
                _spacingMillimetres = ConfigManager.LoadConfig().TopoPointSpacingMillimetres;
            }
            catch
            {
                _spacingMillimetres = 1000;
            }
            if (_spacingMillimetres <= 0) _spacingMillimetres = 1000;
        }

        public ObservableCollection<TopoLineInfo> Lines { get; }
        public RelayCommand SetUpCommand { get; }
        public RelayCommand RefreshCommand { get; }
        public RelayCommand ApplyCommand { get; }

        private double _spacingMillimetres;

        /// <summary>Millimetres, because the UI layer has no access to the document's units.</summary>
        public double SpacingMillimetres
        {
            get => _spacingMillimetres;
            set
            {
                if (value <= 0) return;
                if (Math.Abs(_spacingMillimetres - value) < 0.0001) return;

                SetProperty(ref _spacingMillimetres, value);
                SaveSpacing(value);
            }
        }

        private string _viewName = "";
        public string ViewName
        {
            get => _viewName;
            private set => SetProperty(ref _viewName, value);
        }

        private bool _isProjectSetUp = true;
        public bool IsProjectSetUp
        {
            get => _isProjectSetUp;
            private set
            {
                SetProperty(ref _isProjectSetUp, value);
                OnPropertyChanged(nameof(NeedsSetup));
            }
        }

        /// <summary>The inverse, so the setup banner can bind with the built-in converter.</summary>
        public bool NeedsSetup => !_isProjectSetUp;

        private string _statusMessage = "Open a plan view and press Refresh.";
        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            private set => SetProperty(ref _isBusy, value);
        }

        /// <summary>Re-reads the active view's topo lines. Returns immediately.</summary>
        public void Refresh()
        {
            if (IsBusy) return;

            IsBusy = true;
            StatusMessage = "Reading this view's topo lines…";
            double spacing = _spacingMillimetres;

            Task.Run(() =>
            {
                TopoScope scope;
                try
                {
                    scope = _discover(spacing);
                }
                catch (Exception ex)
                {
                    _dispatcher.Invoke(() =>
                    {
                        StatusMessage = "Could not read this view: " + ex.Message;
                        IsBusy = false;
                    });
                    return;
                }

                _dispatcher.Invoke(() =>
                {
                    Populate(scope);
                    IsBusy = false;
                });
            });
        }

        private void Populate(TopoScope scope)
        {
            IsProjectSetUp = scope.IsProjectSetUp;
            ViewName = scope.ViewName;

            Lines.Clear();
            foreach (var line in scope.Lines) Lines.Add(line);

            StatusMessage = scope.Message;
        }

        private void RunSetUp()
        {
            IsBusy = true;
            StatusMessage = "Setting this project up…";

            Task.Run(() =>
            {
                string summary;
                try
                {
                    summary = _setUpProject();
                }
                catch (Exception ex)
                {
                    summary = "Setup failed: " + ex.Message;
                }

                _dispatcher.Invoke(() =>
                {
                    StatusMessage = summary;
                    IsBusy = false;
                    Refresh();
                });
            });
        }

        private void RunApply()
        {
            IsBusy = true;
            StatusMessage = "Applying points…";
            double spacing = _spacingMillimetres;

            Task.Run(() =>
            {
                string summary;
                try
                {
                    summary = _apply(spacing);
                }
                catch (Exception ex)
                {
                    summary = "Topo Tools failed: " + ex.Message;
                }

                _dispatcher.Invoke(() =>
                {
                    StatusMessage = summary;
                    IsBusy = false;
                    Refresh();
                });
            });
        }

        private static void SaveSpacing(double millimetres)
        {
            try
            {
                var config = ConfigManager.LoadConfig();
                config.TopoPointSpacingMillimetres = millimetres;
                ConfigManager.SaveConfig(config);
            }
            catch
            {
                // A spacing that fails to persist is not worth interrupting the user over.
            }
        }
    }
}
```

- [ ] **Step 2: Write the view**

Create `src/RVTuk.UI/TopoTools/Views/TopoToolsPaneView.xaml`:

```xml
<UserControl x:Class="RVTuk.UI.TopoTools.Views.TopoToolsPaneView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <UserControl.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="/RVTuk.UI;component/Shared/Themes/DarkTheme.xaml"/>
            </ResourceDictionary.MergedDictionaries>
            <BooleanToVisibilityConverter x:Key="BoolVis"/>
        </ResourceDictionary>
    </UserControl.Resources>

    <DockPanel Background="{StaticResource Brush.Bg}" LastChildFill="True">

        <Border DockPanel.Dock="Top"
                Background="{StaticResource Brush.Panel}"
                BorderBrush="{StaticResource Brush.Border}" BorderThickness="0,0,0,1"
                Padding="10,8"
                Visibility="{Binding NeedsSetup, Converter={StaticResource BoolVis}}">
            <StackPanel>
                <TextBlock Text="This project has no Topo_Line style or TOPO_Elevation parameter yet."
                           Foreground="{StaticResource Brush.Warning}"
                           TextWrapping="Wrap" FontSize="11"/>
                <Button Content="Set up this project"
                        Command="{Binding SetUpCommand}"
                        Padding="10,5" Margin="0,6,0,0"
                        HorizontalAlignment="Stretch"/>
            </StackPanel>
        </Border>

        <StackPanel DockPanel.Dock="Top" Margin="10,10,10,8">
            <TextBlock Text="ACTIVE VIEW"
                       Foreground="{StaticResource Brush.TextMuted}"
                       FontSize="10" FontWeight="SemiBold"
                       Margin="0,0,0,2"/>
            <TextBlock Text="{Binding ViewName}"
                       Foreground="{StaticResource Brush.Text}"
                       TextWrapping="Wrap"/>
        </StackPanel>

        <StackPanel DockPanel.Dock="Top" Margin="10,0,10,8"
                    ToolTip="Distance between generated points along each line, in millimetres.">
            <TextBlock Text="POINT SPACING (MM)"
                       Foreground="{StaticResource Brush.TextMuted}"
                       FontSize="10" FontWeight="SemiBold"
                       Margin="0,0,0,4"/>
            <TextBox Text="{Binding SpacingMillimetres, Mode=TwoWay, UpdateSourceTrigger=LostFocus}"/>
        </StackPanel>

        <Border DockPanel.Dock="Bottom"
                Background="{StaticResource Brush.Panel}"
                BorderBrush="{StaticResource Brush.Border}" BorderThickness="0,1,0,0"
                Padding="10,8">
            <StackPanel>
                <Button Content="Apply Points"
                        Command="{Binding ApplyCommand}"
                        Padding="10,5"
                        HorizontalAlignment="Stretch"/>
                <Button Content="Refresh"
                        Command="{Binding RefreshCommand}"
                        Padding="10,3" Margin="0,4,0,0"
                        HorizontalAlignment="Stretch"/>
                <TextBlock Text="{Binding StatusMessage}"
                           Foreground="{StaticResource Brush.TextMuted}"
                           FontSize="11" TextWrapping="Wrap"
                           Margin="0,6,0,0"/>
            </StackPanel>
        </Border>

        <TextBlock DockPanel.Dock="Top"
                   Text="TOPO LINES IN THIS VIEW"
                   Foreground="{StaticResource Brush.TextMuted}"
                   FontSize="10" FontWeight="SemiBold"
                   Margin="10,0,10,4"/>

        <Border Background="{StaticResource Brush.Input}"
                BorderBrush="{StaticResource Brush.Border}" BorderThickness="1"
                Margin="10,0,10,8">
            <ScrollViewer VerticalScrollBarVisibility="Auto"
                          HorizontalScrollBarVisibility="Disabled">
                <ItemsControl ItemsSource="{Binding Lines}" Margin="6">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <StackPanel Margin="0,0,0,6">
                                <TextBlock Text="{Binding DisplayName}"
                                           Foreground="{StaticResource Brush.Text}"
                                           FontWeight="SemiBold"/>
                                <TextBlock Text="{Binding StatusText}"
                                           Foreground="{StaticResource Brush.TextMuted}"
                                           FontSize="11" TextWrapping="Wrap"/>
                            </StackPanel>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </ScrollViewer>
        </Border>
    </DockPanel>
</UserControl>
```

- [ ] **Step 3: Write the code-behind**

Create `src/RVTuk.UI/TopoTools/Views/TopoToolsPaneView.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace RVTuk.UI.TopoTools.Views
{
    public partial class TopoToolsPaneView : UserControl
    {
        public TopoToolsPaneView()
        {
            InitializeComponent();
        }
    }
}
```

- [ ] **Step 4: Build both configurations**

```bash
dotnet build RVTuk.sln -c Release2024
```

Expected: build succeeded. If a `Brush.*` resource is reported missing, open
`src/RVTuk.UI/Shared/Themes/DarkTheme.xaml` and use the names it actually defines — the theme is the
authority, not this plan.

```bash
dotnet build RVTuk.sln -c Release2025
```

Expected: build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/RVTuk.UI/TopoTools
git commit -m "feat(topo-tools): a pane that says what a run will do before it does it"
```

---

### Task 11: Wiring — pane registration, ribbon button, icon

**Files:**
- Modify: `src/RVTuk.Revit/Application.cs`
- Create: `src/RVTuk.Revit/TopoTools/TopoToolsPaneProvider.cs`
- Create: `src/RVTuk.Revit/TopoTools/TopoToolsPaneCommand.cs`

**Interfaces:**
- Consumes: `TopoToolsPaneViewModel` (Task 10); the three event handlers (Tasks 6, 9).
- Produces: `Application.TopoToolsPaneViewModel` (static), `TopoToolsPaneProvider.PaneId`,
  `TopoToolsPaneCommand`.

- [ ] **Step 1: Write the pane provider**

Create `src/RVTuk.Revit/TopoTools/TopoToolsPaneProvider.cs`:

```csharp
using System;
using Autodesk.Revit.UI;
using RVTuk.UI.TopoTools.Views;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// Registers the Topo Tools pane docked to the right.
    ///
    /// Deliberately NOT tabbed behind another custom pane: Revit only creates a registered custom
    /// pane the first time it is shown, so tabbing behind one that has never been shown leaves the
    /// pane with nowhere to go and Show() silently does nothing.
    /// </summary>
    public class TopoToolsPaneProvider : IDockablePaneProvider
    {
        public static readonly DockablePaneId PaneId =
            new DockablePaneId(new Guid("a17c46e9-5b83-4d20-8f6a-9c4e2b70d135"));

        private readonly TopoToolsPaneView _view;

        public TopoToolsPaneProvider(TopoToolsPaneView view)
        {
            _view = view;
        }

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            data.FrameworkElement = _view;
            data.InitialState = new DockablePaneState
            {
                DockPosition = DockPosition.Right,
            };
        }
    }
}
```

- [ ] **Step 2: Write the command**

Create `src/RVTuk.Revit/TopoTools/TopoToolsPaneCommand.cs`:

```csharp
using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// Shows the Topo Tools pane and refreshes it. Refresh returns immediately (it does its Revit
    /// work on a background thread via an ExternalEvent), so this command never waits on an event
    /// it is itself blocking. Everything is wrapped: a pane that fails to show must say why.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class TopoToolsPaneCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                if (Application.TopoToolsPaneViewModel is null)
                {
                    message = "The Topo Tools pane was not registered when RVTuk started " +
                        "(RegisterTopoTools is off in this build).";
                    TaskDialog.Show("RVTuk – Topo Tools", message);
                    return Result.Failed;
                }

                var pane = commandData.Application.GetDockablePane(TopoToolsPaneProvider.PaneId);
                pane.Show();
                Application.TopoToolsPaneViewModel.Refresh();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("RVTuk – Topo Tools (error)", ex.ToString());
                return Result.Failed;
            }
        }
    }
}
```

- [ ] **Step 3: Add the usings and the feature flag in `Application.cs`**

In `src/RVTuk.Revit/Application.cs`, add to the `using` block at the top:

```csharp
using RVTuk.Core.TopoTools;
using RVTuk.Revit.TopoTools;
using RVTuk.Revit.TopoTools.ExternalEvents;
```

Directly after the `RegisterAutoDimensions` field, add:

```csharp
        /// <summary>
        /// Topo Tools ships on its own panel on Revit's Massing &amp; Site tab, not on the RVTuk
        /// Add-Ins panel — it belongs with the site tools it works alongside. Its single entry
        /// point is the dockable pane.
        /// </summary>
        private static readonly bool RegisterTopoTools = true;
```

- [ ] **Step 4: Declare the handlers and the view model in `Application.cs`**

After the `AutoDimensionsPaneViewModel` property declaration, add:

```csharp
        public static TopoSetupEventHandler TopoSetupHandler { get; private set; } = null!;
        public static ExternalEvent TopoSetupEvent { get; private set; } = null!;
        public static TopoDiscoveryEventHandler TopoDiscoveryHandler { get; private set; } = null!;
        public static ExternalEvent TopoDiscoveryEvent { get; private set; } = null!;
        public static TopoApplyEventHandler TopoApplyHandler { get; private set; } = null!;
        public static ExternalEvent TopoApplyEvent { get; private set; } = null!;
        public static RVTuk.UI.TopoTools.ViewModels.TopoToolsPaneViewModel TopoToolsPaneViewModel { get; private set; } = null!;
```

- [ ] **Step 5: Register the pane in `OnStartup`**

In `OnStartup`, directly after the closing brace of the `if (RegisterAutoDimensions) { … }` block
and before the `try { CreateRibbon(application); }`, add:

```csharp
            if (RegisterTopoTools)
            {
                TopoSetupHandler     = new TopoSetupEventHandler();
                TopoSetupEvent       = ExternalEvent.Create(TopoSetupHandler);
                TopoDiscoveryHandler = new TopoDiscoveryEventHandler();
                TopoDiscoveryEvent   = ExternalEvent.Create(TopoDiscoveryHandler);
                TopoApplyHandler     = new TopoApplyEventHandler();
                TopoApplyEvent       = ExternalEvent.Create(TopoApplyHandler);

                // The pane (UI project) only ever sees these delegates — no Revit types cross over.
                // All three block on WaitForCompletion, so the view model calls them from the pool.
                Func<double, TopoScope> discoverTopo = spacingMillimetres =>
                {
                    TopoDiscoveryHandler.Reset();
                    TopoDiscoveryHandler.Prepare(spacingMillimetres);
                    TopoDiscoveryEvent.Raise();
                    TopoDiscoveryHandler.WaitForCompletion();
                    return TopoDiscoveryHandler.Result;
                };

                Func<double, string> applyTopo = spacingMillimetres =>
                {
                    TopoApplyHandler.Reset();
                    TopoApplyHandler.Prepare(spacingMillimetres);
                    TopoApplyEvent.Raise();
                    TopoApplyHandler.WaitForCompletion();
                    return TopoApplyHandler.Summary;
                };

                Func<string> setUpTopoProject = () =>
                {
                    TopoSetupHandler.Reset();
                    TopoSetupEvent.Raise();
                    TopoSetupHandler.WaitForCompletion();
                    return TopoSetupHandler.Summary;
                };

                TopoToolsPaneViewModel =
                    new RVTuk.UI.TopoTools.ViewModels.TopoToolsPaneViewModel(
                        discoverTopo, applyTopo, setUpTopoProject);

                var topoView = new RVTuk.UI.TopoTools.Views.TopoToolsPaneView
                {
                    DataContext = TopoToolsPaneViewModel
                };
                application.RegisterDockablePane(
                    TopoToolsPaneProvider.PaneId,
                    "Topo Tools",
                    new TopoToolsPaneProvider(topoView));
            }
```

- [ ] **Step 6: Add the ribbon button in `CreateRibbon`**

In `CreateRibbon`, directly after the `if (RegisterAutoDimensions) { … }` block and before
`if (!RegisterNeoProperties) return;`, add:

```csharp
            if (RegisterTopoTools)
            {
                // Autodesk.Revit.UI.Tab offers only AddIns and Analyze, so the built-in tab can
                // only be named through the string overload — and whether Revit resolves built-in
                // tabs that way is not guaranteed. Falling back to the RVTuk panel keeps the tool
                // reachable either way rather than losing the button to an exception.
                RibbonPanel topoPanel;
                try
                {
                    topoPanel = app.CreateRibbonPanel("Massing & Site", "RVTuk");
                }
                catch (Exception)
                {
                    topoPanel = panel;
                }

                var topoBtn = new PushButtonData(
                    "TopoTools",
                    "Topo\nTools",
                    assemblyPath,
                    typeof(TopoToolsPaneCommand).FullName!)
                {
                    ToolTip = "Open the Topo Tools pane: turn topo lines into points on the toposolid below"
                };
                topoBtn.LargeImage = CreateTopoToolsIcon(32);
                topoBtn.Image      = CreateTopoToolsIcon(16);

                topoPanel.AddItem(topoBtn);
            }
```

- [ ] **Step 7: Add the icon**

In `Application.cs`, next to `CreateAutoDimensionsIcon`, add:

```csharp
        private static BitmapSource CreateTopoToolsIcon(int size)
        {
            var dv = new DrawingVisual();
            using (var ctx = dv.RenderOpen())
            {
                double s = size;
                ctx.DrawRectangle(new SolidColorBrush(WpfColor.FromRgb(0x25, 0x25, 0x26)), null,
                    new Rect(0, 0, s, s));

                // Three nested contour lines, with a point sitting on the middle one.
                var pen = new Pen(new SolidColorBrush(WpfColor.FromRgb(0xFF, 0x8C, 0x00)),
                    Math.Max(1, s * 0.06));
                pen.Freeze();

                ctx.DrawEllipse(null, pen, new WpfPoint(s * 0.5, s * 0.55), s * 0.36, s * 0.24);
                ctx.DrawEllipse(null, pen, new WpfPoint(s * 0.5, s * 0.55), s * 0.23, s * 0.15);
                ctx.DrawEllipse(null, pen, new WpfPoint(s * 0.5, s * 0.55), s * 0.10, s * 0.07);

                ctx.DrawEllipse(new SolidColorBrush(Colors.White), null,
                    new WpfPoint(s * 0.5, s * 0.31), s * 0.07, s * 0.07);
            }
            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            return bmp;
        }
```

- [ ] **Step 8: Build both configurations**

```bash
dotnet build RVTuk.sln -c Release2024
```

Expected: build succeeded.

```bash
dotnet build RVTuk.sln -c Release2025
```

Expected: build succeeded.

- [ ] **Step 9: Run the whole suite once more**

```bash
dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 10: Commit**

```bash
git add src/RVTuk.Revit
git commit -m "feat(topo-tools): a button on Massing & Site, and a pane behind it"
```

---

### Task 12: Docs, and verifying it in Revit

The four questions the reference assemblies could not answer, plus the docs the repo's layout
requires for every tool.

**Files:**
- Create: `docs/tools/topo-tools/README.md`
- Create: `docs/tools/topo-tools/backlog.md`
- Modify: `CLAUDE.md`

- [ ] **Step 1: Deploy and verify in Revit**

```bash
dotnet build RVTuk.sln -c Release2024
```

Then, in an **elevated** PowerShell with Revit closed:

```bash
./Deploy.ps1 2024
```

Open Revit 2024 on a project containing a toposolid and work through this checklist, recording each
answer in `backlog.md` (Step 3) if it differs from what the plan assumed:

1. **Ribbon placement** — is the "Topo Tools" button on the **Massing & Site** tab, or did it fall
   back to the RVTuk panel on Add-Ins? This is the open question from the spec. If it fell back,
   note it and leave the fallback in place; do not chase it further in this task.
2. **Setup** — the pane shows its banner; pressing *Set up this project* creates the `Topo_Line`
   line style (Manage → Object Styles → Lines) and puts **TOPO_Elevation** on a detail line's
   Properties palette as a Length.
3. **A first run** — draw two detail lines on `Topo_Line` over the toposolid in a floor plan, give
   each a different `TOPO_Elevation`, press Refresh (both list as *ready* with a point count), then
   *Apply Points*. The toposolid deforms, and the points sit at the elevations given.
4. **Idempotence** — press *Apply Points* again without changing anything. The summary reports the
   same number added and that number replaced, and the toposolid is unchanged. **This is the single
   most important check in the list.**
5. **A moved line** — drag one line, re-run: its old points are gone and the new ones are in place.
6. **A cleared elevation** — clear one line's `TOPO_Elevation`, re-run: it reports as skipped for
   having no elevation, **and its points from the previous run are gone.**
7. **A deleted line** — delete a line, re-run: its points are gone.
8. **Undo** — one Ctrl+Z takes the whole run back.
9. **A section view** — with a section active, the pane says topo lines are read from plan views
   only.
10. **`AddPoints` on a coincident point** — put a line's point directly over an existing toposolid
    vertex and re-run twice; check the summary does not accumulate "moved by hand" counts, which
    would mean Revit merged the point and the ledger recorded something that is not there.

- [ ] **Step 2: Write the tool README**

Create `docs/tools/topo-tools/README.md`:

```markdown
# Topo Tools

**What it is:** draw detail lines on the dedicated "Topo_Line" style in a plan view, give each a
`TOPO_Elevation` (a shared/survey elevation), and the tool puts points at that height along each
line on the toposolids beneath them. Re-runnable: move a line, change its height or delete it, run
again, and the result is what you would have got by drawing it that way to begin with.

**Heights are shared (survey) elevations,** absolute as a surveyor quotes them. The conversion is
one subtraction — `ProjectPosition.Elevation` at the internal origin — which does mean the tool
depends on the document's shared-coordinate setup: relocating the survey point changes what a
stored value resolves to. For site work that is the correct dependency, and a re-run corrects the
model.

**Detail lines, and plan views only.** View-specific lines keep site working lines out of every
other view and out of 3D. A detail curve's geometry comes back on the view's sketch plane, so
dropping Z only recovers the drawn shape where that plane is horizontal — in a section the same
projection would collapse the line to a streak, and the pane refuses.

**A point has no identity.** There is no `Toposolid.AddPoints`; points go through
`GetSlabShapeEditor()`, and `SlabShapeVertex` is a position rather than an element. So each
toposolid carries an Extensible Storage ledger of `source line id → the points it owns there`,
recording the positions `AddPoints` returned, and a re-run matches them back within 0.1 mm. A
recorded point that has moved was moved by hand: it is left alone and reported.

**The ledger lives on the toposolid, not the line** — unlike `AutoDimensionTracker`. Storage on a
line dies with the line and orphans its points; on the toposolid a run sees a line id that no longer
resolves and cleans up. It also means a line merely absent from the scanned view is never mistaken
for a deleted one.

**Status:** registered on its own panel, gated by `RegisterTopoTools` in
`src/RVTuk.Revit/Application.cs` (on). Revit 2024/2025 only — `Toposolid` does not exist in 2023, so
KKarea never ships it.

**Names:** code `TopoTools`; ribbon button "Topo Tools"; line style `Topo_Line`; parameter
`TOPO_Elevation`.

## Code

| Layer | Folder |
|-------|--------|
| Core  | `src/RVTuk.Core/TopoTools/` (sampler, containment, ledger codec, scope models) |
| UI    | `src/RVTuk.UI/TopoTools/` (pane view + view model) |
| Revit | `src/RVTuk.Revit/TopoTools/` (line style, parameter, collectors, router, applier, ledger store, runner, pane, external events) |
| Tests | `tests/RVTuk.Core.Tests/TopoTools/` |

## Docs

- [specs/2026-07-29-topo-tools-design.md](specs/2026-07-29-topo-tools-design.md) — approved design
- [plans/2026-07-29-topo-tools.md](plans/2026-07-29-topo-tools.md) — implementation plan
- [backlog.md](backlog.md) — bugs / improvements / ideas
```

- [ ] **Step 3: Write the backlog**

Create `docs/tools/topo-tools/backlog.md`, recording anything Step 1 turned up plus the features the
spec deliberately deferred:

```markdown
# Topo Tools — backlog

## Verification

- [ ] Record the outcome of the in-Revit checklist (plans/2026-07-29-topo-tools.md, Task 12 Step 1),
      especially whether the ribbon button landed on Massing & Site or fell back to the RVTuk panel.

## Ideas (deliberately out of v1)

- Read contours from an imported survey DWG — the sampler and router are reused untouched; only a
  new collector is needed.
- Create a toposolid from the topo lines when none exists yet.
- Subdivisions and split lines/creases (`SlabShapeEditor.DrawSplitLine`).
- Multi-view scope, as Auto Dimensions has for levels and views.
- A toposolid hidden in the scanned view keeps stale points: nothing in the run can reach its
  ledger. Only matters on phased or design-option sites.
```

- [ ] **Step 4: Register the tool in `CLAUDE.md`**

Four edits, all in `CLAUDE.md`:

1. In the **Terminology** table's "Tool" row, add Topo Tools to the list of tools.
2. In the paragraph beginning "Canonical tool names in code and folders", add `TopoTools` to the
   list.
3. In the **v1 launch surface** paragraph, note that Topo Tools is registered on its own panel on
   the Massing & Site tab, gated by `RegisterTopoTools`.
4. In the **Features** list, after the Auto Dimensions bullet, add:

```markdown
- **Topo Tools** (ribbon "Topo Tools") — draw detail lines on the dedicated "Topo_Line" style in a plan view, give each a `TOPO_Elevation` (shared/survey elevation), and the tool puts points at that height along each line on the toposolids beneath them — re-runnable after the lines move, change height or are deleted. Its only entry point is a dockable pane, on its own panel on Revit's **Massing & Site** tab. Revit 2024/2025 only (`Toposolid` does not exist in 2023). See [`docs/tools/topo-tools/README.md`](docs/tools/topo-tools/README.md).
```

- [ ] **Step 5: Commit**

```bash
git add docs/tools/topo-tools CLAUDE.md
git commit -m "docs(topo-tools): what it is, what is left, and where it lives"
```

---

## Self-review notes

Checked against the spec:

- **Every spec section has a task.** API findings → Tasks 7–9; parameter and style → Task 6; ledger
  → Tasks 4 and 8; the four pipeline stages → Tasks 2, 3, 7, 8, 9; pane → Task 10; ribbon and its
  fallback → Task 11; layering and the `XyPoint` move → Tasks 1 and 5; errors → Task 9's runner;
  tests → Tasks 2, 3, 4; "to verify in Revit" → Task 12 Step 1, all four questions present.
- **Type consistency:** `XyPoint` (Task 1) is used unqualified from Task 2 onward; `XyzPoint` (Task
  4) by Tasks 8 and 9; `ToposolidTarget` (Task 7) by Tasks 8 and 9; `TopoScope`/`TopoLineInfo`/
  `TopoLineStatus` (Task 5) by Tasks 9 and 10. The pane's three delegates are declared identically
  in Task 10's constructor and Task 11's wiring: `Func<double, TopoScope>`, `Func<double, string>`,
  `Func<string>`.
- **Two known-uncertain API points** are called out where they bite rather than left to be
  discovered: `AddPoints`' return type (Task 8 Step 3) and `HostTopoId`'s type (Task 7 Step 5).

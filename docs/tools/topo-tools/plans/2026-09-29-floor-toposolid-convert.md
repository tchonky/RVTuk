# Floor ⇄ Toposolid Converter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A ⇄ button in the Topo Tools pane that turns every selected floor into a toposolid and
every selected toposolid into a floor. Floor → toposolid → floor gives back the same outline,
level, offset, top surface, parameter values and RVTuk storage. The type is the one exception:
it is Revit's last-placed type.

**Architecture:** The geometry decisions are pure Core code with xunit tests:
- which vertices to raise and which to add;
- how to chain sketch curves;
- the height of a sloped plane;
- the parked-parameter encoding;
- the status-line text.

The Revit side reads the source, deletes it, creates the target flat from the source's sketch, and
replays the shape, parameters and storage onto it. There is one transaction per element, all
inside one transaction group. Dependents are confirmed in dialogs before any transaction opens.
The pane reaches Revit through one more external event, in the same pattern as ▶ Apply.

Beyond the spec's file table:
- Core also gets `LoopChainer`, `PlaneHeight` and `ConvertReport`, so that more of the logic is
  unit-tested.
- The Revit shape code is split out of `SlabConverter` into `SlabShape`.
- Loop ordering moves from Room Floor to `Shared/LoopOrder`, because both tools need it.

**Tech Stack:** C#, net48 (Revit 2024) and net8.0-windows (Revit 2025), Revit API (Nice3point
packages), WPF/MVVM, xunit.

**Spec:** [../specs/2026-09-29-floor-toposolid-convert-design.md](../specs/2026-09-29-floor-toposolid-convert-design.md).
Read it first; it carries the reasoning this plan only executes.

## Global Constraints

- **Revit 2024/2025 only.** The code goes in `RVTuk.Core`, `RVTuk.UI` and `RVTuk.Revit`. Nothing
  goes in `KKarea.Revit`.
- **Core stays free of Revit and WPF types.** UI never references a Revit type; it receives
  delegates.
- **Every project compiles for both `net48` (Release2024) and `net8.0-windows` (Release2025)**, with
  nullable enabled. No BCL API newer than .NET Framework 4.8: no `string.Contains(char)`, no
  `KeyValuePair` deconstruction, no `Math.Clamp`. `new ElementId(long)` and `ElementId.Value` are
  fine, since both exist in 2024+.
- **Namespace = root namespace + folder path, exactly:**
  - `RVTuk.Core.TopoTools`;
  - `RVTuk.Revit.TopoTools`;
  - `RVTuk.Revit.TopoTools.ExternalEvents`;
  - `RVTuk.Revit.Shared`;
  - tests in `RVTuk.Core.Tests.TopoTools`.
- **A file used by two tools lives in `Shared/`.**
- **Vertex matching tolerance:** `0.1 / 304.8` ft (0.1 mm), by XY.
- **Types:**
  - floor: `doc.GetDefaultElementTypeId(ElementTypeGroup.FloorType)` if it is a `FloorType`, else
    the first `FloorType` with `IsFoundationSlab == false`;
  - toposolid: `doc.GetDefaultFamilyTypeId(new ElementId(BuiltInCategory.OST_Toposolid))` if it is
    a `ToposolidType`, else the first `ToposolidType` by name.
- **Extensible Storage, parked parameters:**
  - schema name `RVTukTopoToolsParkedParameters`;
  - GUID `309b77b4-64cd-45c7-bd73-d6a81b1f85ae`;
  - vendor `KnafoKlimor`;
  - public read and write;
  - one string field, `Entries`.
- **Verbatim strings:**
  - button content `&#x21C4;`;
  - tooltip `Convert selected floors ⇄ toposolids`;
  - transaction group `Convert Floor ⇄ Toposolid`;
  - `Select floors or toposolids first.`;
  - `Cancelled — nothing converted.`;
  - dialog instructions `Convert floor → toposolid?` and `Convert toposolid → floor?`;
  - command links `Convert and delete them` and `Skip this one`;
  - verification text `Do the same for the rest of the selection`.
- **Bidi.** Text shown to users that embeds a Revit name (type name, user name) wraps it in
  first-strong isolates, `"⁨" + name + "⁩"`, as `RoomFloorCommand.Label` does. Tests of
  text use Hebrew mixed with Latin on one line, never Hebrew alone.
- **Commits** end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

These are the inputs the spec implies but a straightforward implementation would not survive. Each
is pinned by a test in the task named.

1. **Two source vertices on one XY.** A toposolid can carry one, typically after a survey import.
   `AddPoints` rejects the *whole batch* if two points share an XY, so every interior point would
   be lost. **Expected:** the planner sends the first and drops the rest. Test in Task 1
   (`TwoSourcePointsOnOneXyAreSentOnce`).
2. **Hole corners.** Corners of an inner loop are vertices too. **Expected:** a raised hole corner
   becomes an offset on the target's hole vertex, not an `AddPoints` on the boundary, which Revit
   would reject. Test in Task 1 (`HoleCornersMatchTheirOwnTargetVertices`).
3. **Sketch curves out of order, reversed, or a circle drawn as two arcs.** `CurveLoop.Append`
   throws unless the curves run head to tail. **Expected:** the curves are chained, and a loop
   that really does not close is refused with a readable reason. Tests in Task 2.
4. **Parameter text containing the codec's own separators, `%`, an empty string, or Hebrew mixed
   with Latin.** **Expected:** byte-for-byte round trip. Tests in Task 3.
5. **A run where every element was refused or skipped.** **Expected:** the status line says
   "Nothing converted." followed by the reasons, never "0 floors → toposolids". Test in Task 4.

---

### Task 1: Shape replay planner (Core)

**Files:**
- Create: `src/RVTuk.Core/TopoTools/ShapeReplayPlanner.cs`
- Test: `tests/RVTuk.Core.Tests/TopoTools/ShapeReplayPlannerTests.cs`

**Interfaces:**
- Consumes: `XyzPoint` (existing, `src/RVTuk.Core/TopoTools/XyzPoint.cs`):
  `readonly record struct XyzPoint(double X, double Y, double Z)`.
- Produces:
  - `readonly record struct VertexOffset(int TargetIndex, double Offset)`
  - `readonly record struct CreaseEnds(XyzPoint Start, XyzPoint End)`
  - `readonly record struct VertexPair(int StartIndex, int EndIndex)`
  - `sealed record ShapeReplayPlan(IReadOnlyList<VertexOffset> Offsets, IReadOnlyList<XyzPoint> Additions)`
  - `sealed record CreaseReplayPlan(IReadOnlyList<VertexPair> Pairs, int Dropped)`
  - `static class ShapeReplayPlanner`:
    - `const double ToleranceFeet`
    - `ShapeReplayPlan Plan(IReadOnlyList<XyzPoint> source, IReadOnlyList<XyzPoint> target)`
    - `CreaseReplayPlan PlanCreases(IReadOnlyList<XyzPoint> live, IReadOnlyList<CreaseEnds> creases)`
    - `int IndexOfXy(IReadOnlyList<XyzPoint> points, XyzPoint point)`, which returns -1 when there
      is none.

- [ ] **Step 1: Write the failing tests**

`tests/RVTuk.Core.Tests/TopoTools/ShapeReplayPlannerTests.cs`:

```csharp
using System.Collections.Generic;
using RVTuk.Core.TopoTools;
using Xunit;

namespace RVTuk.Core.Tests.TopoTools;

public class ShapeReplayPlannerTests
{
    // A freshly created 10 × 10 ft target, flat at Z = 10: its four corners.
    private static readonly XyzPoint[] FlatSquare =
    {
        new(0, 0, 10), new(10, 0, 10), new(10, 10, 10), new(0, 10, 10),
    };

    [Fact]
    public void RaisedAndLoweredCornersBecomeOffsetsFromTheTargetHeight()
    {
        var source = new[]
        {
            new XyzPoint(0, 0, 10), new XyzPoint(10, 0, 11),
            new XyzPoint(10, 10, 10), new XyzPoint(0, 10, 9.5),
        };

        var plan = ShapeReplayPlanner.Plan(source, FlatSquare);

        Assert.Equal(new[] { new VertexOffset(1, 1.0), new VertexOffset(3, -0.5) }, plan.Offsets);
        Assert.Empty(plan.Additions);
    }

    [Fact]
    public void ACornerAlreadyAtItsHeightNeedsNoOffset()
    {
        var plan = ShapeReplayPlanner.Plan(FlatSquare, FlatSquare);

        Assert.Empty(plan.Offsets);
        Assert.Empty(plan.Additions);
    }

    [Fact]
    public void MatchesACornerWithinATenthOfAMillimetreButNotBeyond()
    {
        double within = 0.05 / 304.8;
        double beyond = 0.5 / 304.8;
        var source = new[] { new XyzPoint(10 + within, 0, 12), new XyzPoint(beyond, 10, 12) };

        var plan = ShapeReplayPlanner.Plan(source, FlatSquare);

        Assert.Equal(new[] { new VertexOffset(1, 2.0) }, plan.Offsets);
        Assert.Equal(new[] { new XyzPoint(beyond, 10, 12) }, plan.Additions);
    }

    [Fact]
    public void InteriorPointsAreAddedAtTheirAbsolutePosition()
    {
        var source = new[] { new XyzPoint(5, 5, 12.25), new XyzPoint(2, 7, 9) };

        var plan = ShapeReplayPlanner.Plan(source, FlatSquare);

        Assert.Empty(plan.Offsets);
        Assert.Equal(source, plan.Additions);
    }

    [Fact]
    public void HoleCornersMatchTheirOwnTargetVertices()
    {
        var target = new List<XyzPoint>(FlatSquare)
        {
            new(4, 4, 10), new(6, 4, 10), new(6, 6, 10), new(4, 6, 10),
        };
        var source = new[] { new XyzPoint(6, 4, 10.5), new XyzPoint(4, 6, 10.5) };

        var plan = ShapeReplayPlanner.Plan(source, target);

        Assert.Equal(new[] { new VertexOffset(5, 0.5), new VertexOffset(7, 0.5) }, plan.Offsets);
        Assert.Empty(plan.Additions);
    }

    [Fact]
    public void TwoSourcePointsOnOneXyAreSentOnce()
    {
        // AddPoints rejects a whole batch that holds two points on one XY.
        var source = new[]
        {
            new XyzPoint(5, 5, 12), new XyzPoint(5, 5, 13),
            new XyzPoint(10, 10, 11), new XyzPoint(10, 10, 14),
        };

        var plan = ShapeReplayPlanner.Plan(source, FlatSquare);

        Assert.Equal(new[] { new XyzPoint(5, 5, 12) }, plan.Additions);
        Assert.Equal(new[] { new VertexOffset(2, 1.0) }, plan.Offsets);
    }

    [Fact]
    public void AnEmptySourceGivesAnEmptyPlan()
    {
        var plan = ShapeReplayPlanner.Plan(new XyzPoint[0], FlatSquare);

        Assert.Empty(plan.Offsets);
        Assert.Empty(plan.Additions);
    }

    [Fact]
    public void ACreaseIsPlannedBetweenTheVerticesAtItsEnds()
    {
        var live = new[] { new XyzPoint(0, 0, 10), new XyzPoint(10, 10, 10), new XyzPoint(5, 5, 12) };
        var creases = new[] { new CreaseEnds(new XyzPoint(0, 0, 10), new XyzPoint(5, 5, 12)) };

        var plan = ShapeReplayPlanner.PlanCreases(live, creases);

        Assert.Equal(new[] { new VertexPair(0, 2) }, plan.Pairs);
        Assert.Equal(0, plan.Dropped);
    }

    [Fact]
    public void ACreaseWithAnUnmatchedOrCollapsedEndIsDroppedAndCounted()
    {
        var live = new[] { new XyzPoint(0, 0, 10), new XyzPoint(10, 10, 10) };
        var creases = new[]
        {
            new CreaseEnds(new XyzPoint(0, 0, 10), new XyzPoint(3, 3, 10)),       // nothing at (3, 3)
            new CreaseEnds(new XyzPoint(0, 0, 10), new XyzPoint(0, 0.00001, 10)), // both ends on one vertex
            new CreaseEnds(new XyzPoint(0, 0, 10), new XyzPoint(10, 10, 10)),
        };

        var plan = ShapeReplayPlanner.PlanCreases(live, creases);

        Assert.Equal(new[] { new VertexPair(0, 1) }, plan.Pairs);
        Assert.Equal(2, plan.Dropped);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj --filter "FullyQualifiedName~ShapeReplayPlannerTests"`
Expected: build FAILS with `The name 'ShapeReplayPlanner' does not exist in the current context`.

- [ ] **Step 3: Write the implementation**

`src/RVTuk.Core/TopoTools/ShapeReplayPlanner.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace RVTuk.Core.TopoTools
{
    /// <summary>A vertex of the new element to raise or lower, by its index in that element's
    /// vertex list, relative to where it sits now.</summary>
    public readonly record struct VertexOffset(int TargetIndex, double Offset);

    /// <summary>A split line drawn by the user, by the positions of its two ends.</summary>
    public readonly record struct CreaseEnds(XyzPoint Start, XyzPoint End);

    /// <summary>A split line to draw, by the indices of its two end vertices.</summary>
    public readonly record struct VertexPair(int StartIndex, int EndIndex);

    public sealed record ShapeReplayPlan(
        IReadOnlyList<VertexOffset> Offsets, IReadOnlyList<XyzPoint> Additions);

    public sealed record CreaseReplayPlan(IReadOnlyList<VertexPair> Pairs, int Dropped);

    /// <summary>
    /// How to give a freshly created, flat floor or toposolid the top surface of the element it
    /// replaces. Both have the same outline, so every corner of the source already exists on the
    /// target: those are raised or lowered in place (<c>ModifySubElement</c>). Everything else
    /// (interior points, points on an edge) is added at its absolute position (<c>AddPoints</c>).
    ///
    /// Vertices are matched by XY alone, within the same 0.1 mm the Topo Tools point ledger
    /// matches with, so a round trip leaves the ledger still pointing at real vertices.
    /// </summary>
    public static class ShapeReplayPlanner
    {
        /// <summary>0.1 mm, in Revit's internal feet.</summary>
        public const double ToleranceFeet = 0.1 / 304.8;

        public static ShapeReplayPlan Plan(IReadOnlyList<XyzPoint> source, IReadOnlyList<XyzPoint> target)
        {
            var offsets = new List<VertexOffset>();
            var additions = new List<XyzPoint>();
            var matchedTargets = new HashSet<int>();

            foreach (var point in source)
            {
                int index = IndexOfXy(target, point);
                if (index >= 0)
                {
                    // A second source vertex on the same corner has nowhere to go.
                    if (!matchedTargets.Add(index)) continue;

                    double offset = point.Z - target[index].Z;
                    if (Math.Abs(offset) > ToleranceFeet) offsets.Add(new VertexOffset(index, offset));
                    continue;
                }

                // AddPoints rejects the whole batch if two points share an XY, so the first wins
                // and the rest are dropped here rather than losing every point there.
                if (IndexOfXy(additions, point) >= 0) continue;
                additions.Add(point);
            }

            return new ShapeReplayPlan(offsets, additions);
        }

        /// <summary>
        /// Maps each user-drawn split line onto the vertices that exist once the points are in.
        /// A line whose end has no vertex, or whose two ends land on one vertex, cannot be drawn:
        /// it is counted rather than guessed at.
        /// </summary>
        public static CreaseReplayPlan PlanCreases(
            IReadOnlyList<XyzPoint> live, IReadOnlyList<CreaseEnds> creases)
        {
            var pairs = new List<VertexPair>();
            int dropped = 0;

            foreach (var crease in creases)
            {
                int start = IndexOfXy(live, crease.Start);
                int end = IndexOfXy(live, crease.End);
                if (start < 0 || end < 0 || start == end)
                {
                    dropped++;
                    continue;
                }
                pairs.Add(new VertexPair(start, end));
            }

            return new CreaseReplayPlan(pairs, dropped);
        }

        /// <summary>The first point within tolerance of <paramref name="point"/> in plan, else -1.</summary>
        public static int IndexOfXy(IReadOnlyList<XyzPoint> points, XyzPoint point)
        {
            for (int i = 0; i < points.Count; i++)
            {
                if (Math.Abs(points[i].X - point.X) <= ToleranceFeet &&
                    Math.Abs(points[i].Y - point.Y) <= ToleranceFeet)
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj --filter "FullyQualifiedName~ShapeReplayPlannerTests"`
Expected: PASS, 9 tests.

Run: `dotnet build src\RVTuk.Core\RVTuk.Core.csproj -c Release2024`
Expected: `Build succeeded`. This proves the net48 compile.

- [ ] **Step 5: Commit**

```bash
git add src/RVTuk.Core/TopoTools/ShapeReplayPlanner.cs tests/RVTuk.Core.Tests/TopoTools/ShapeReplayPlannerTests.cs
git commit -m "feat(topo-tools): plan which vertices to raise and which to add when replaying a slab shape

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Loop chainer and plane height (Core)

**Files:**
- Create: `src/RVTuk.Core/TopoTools/LoopChainer.cs`
- Create: `src/RVTuk.Core/TopoTools/PlaneHeight.cs`
- Test: `tests/RVTuk.Core.Tests/TopoTools/LoopChainerTests.cs`
- Test: `tests/RVTuk.Core.Tests/TopoTools/PlaneHeightTests.cs`

**Interfaces:**
- Consumes: `XyzPoint`.
- Produces:
  - `readonly record struct Segment(XyzPoint Start, XyzPoint End)`
  - `readonly record struct ChainLink(int Index, bool Reversed)`
  - `static class LoopChainer`: `IReadOnlyList<ChainLink>? Chain(IReadOnlyList<Segment> segments, double tolerance)`.
    It returns `null` when the segments do not form one closed loop.
  - `static class PlaneHeight`: `double? At(XyzPoint origin, XyzPoint normal, double x, double y)`.
    It returns `null` for a vertical plane.

- [ ] **Step 1: Write the failing tests**

`tests/RVTuk.Core.Tests/TopoTools/LoopChainerTests.cs`:

```csharp
using RVTuk.Core.TopoTools;
using Xunit;

namespace RVTuk.Core.Tests.TopoTools;

public class LoopChainerTests
{
    private const double Tolerance = 0.0005;

    private static readonly XyzPoint A = new(0, 0, 0);
    private static readonly XyzPoint B = new(10, 0, 0);
    private static readonly XyzPoint C = new(10, 10, 0);
    private static readonly XyzPoint D = new(0, 10, 0);

    [Fact]
    public void AnOrderedLoopComesBackAsItIs()
    {
        var chain = LoopChainer.Chain(
            new[] { new Segment(A, B), new Segment(B, C), new Segment(C, D), new Segment(D, A) }, Tolerance);

        Assert.Equal(
            new[] { new ChainLink(0, false), new ChainLink(1, false), new ChainLink(2, false), new ChainLink(3, false) },
            chain);
    }

    [Fact]
    public void ShuffledSegmentsAreChainedHeadToTail()
    {
        var chain = LoopChainer.Chain(
            new[] { new Segment(A, B), new Segment(C, D), new Segment(B, C), new Segment(D, A) }, Tolerance);

        Assert.Equal(
            new[] { new ChainLink(0, false), new ChainLink(2, false), new ChainLink(1, false), new ChainLink(3, false) },
            chain);
    }

    [Fact]
    public void AReversedSegmentIsFlagged()
    {
        var chain = LoopChainer.Chain(
            new[] { new Segment(A, B), new Segment(C, B), new Segment(C, D), new Segment(D, A) }, Tolerance);

        Assert.Equal(
            new[] { new ChainLink(0, false), new ChainLink(1, true), new ChainLink(2, false), new ChainLink(3, false) },
            chain);
    }

    [Fact]
    public void ACircleDrawnAsTwoArcsTheSameWayRoundCloses()
    {
        // Two half-circles both running A → C: the second has to be walked backwards.
        var chain = LoopChainer.Chain(new[] { new Segment(A, C), new Segment(A, C) }, Tolerance);

        Assert.Equal(new[] { new ChainLink(0, false), new ChainLink(1, true) }, chain);
    }

    [Fact]
    public void ASingleClosedCurveIsALoop()
    {
        Assert.Equal(new[] { new ChainLink(0, false) }, LoopChainer.Chain(new[] { new Segment(A, A) }, Tolerance));
    }

    [Fact]
    public void AnOpenChainIsRejected()
    {
        Assert.Null(LoopChainer.Chain(
            new[] { new Segment(A, B), new Segment(B, C), new Segment(C, D) }, Tolerance));
    }

    [Fact]
    public void AGapWithinToleranceCloses_ButOneBeyondDoesNot()
    {
        var nearA = new XyzPoint(0.0001, 0, 0);
        var farA = new XyzPoint(0.01, 0, 0);

        Assert.NotNull(LoopChainer.Chain(
            new[] { new Segment(A, B), new Segment(B, C), new Segment(C, D), new Segment(D, nearA) }, Tolerance));
        Assert.Null(LoopChainer.Chain(
            new[] { new Segment(A, B), new Segment(B, C), new Segment(C, D), new Segment(D, farA) }, Tolerance));
    }

    [Fact]
    public void NoSegmentsIsNotALoop()
    {
        Assert.Null(LoopChainer.Chain(new Segment[0], Tolerance));
    }
}
```

`tests/RVTuk.Core.Tests/TopoTools/PlaneHeightTests.cs`:

```csharp
using RVTuk.Core.TopoTools;
using Xunit;

namespace RVTuk.Core.Tests.TopoTools;

public class PlaneHeightTests
{
    [Fact]
    public void AHorizontalPlaneHasItsOwnHeightEverywhere()
    {
        var z = PlaneHeight.At(new XyzPoint(3, 4, 12.5), new XyzPoint(0, 0, 1), -20, 55);

        Assert.Equal(12.5, z!.Value);
    }

    [Fact]
    public void ASlopeRisesAlongItsFall()
    {
        // 1 in 10 up along +X. The normal leans back against the rise and is deliberately not
        // unit length: the formula must not care.
        var z = PlaneHeight.At(new XyzPoint(0, 0, 5), new XyzPoint(-0.1, 0, 1), 20, 7);

        Assert.Equal(7.0, z!.Value, 9);
    }

    [Fact]
    public void AVerticalPlaneHasNoHeight()
    {
        Assert.Null(PlaneHeight.At(new XyzPoint(0, 0, 0), new XyzPoint(1, 0, 0), 5, 5));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj --filter "FullyQualifiedName~LoopChainerTests|FullyQualifiedName~PlaneHeightTests"`
Expected: build FAILS with `The type or namespace name 'Segment' could not be found`.

- [ ] **Step 3: Write the implementation**

`src/RVTuk.Core/TopoTools/LoopChainer.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace RVTuk.Core.TopoTools
{
    /// <summary>One curve of a sketch loop, by its two ends.</summary>
    public readonly record struct Segment(XyzPoint Start, XyzPoint End);

    /// <summary>A curve's place in a loop: which one, and whether it runs backwards.</summary>
    public readonly record struct ChainLink(int Index, bool Reversed);

    /// <summary>
    /// Puts a sketch loop's curves head to tail. <c>Sketch.Profile</c> hands back each loop's
    /// curves, but nothing promises they come in order or all run the same way round, and
    /// <c>CurveLoop.Append</c> throws unless each curve starts where the last one ended. A circle
    /// is the everyday case: Revit draws it as two arcs that may both run the same way.
    /// </summary>
    public static class LoopChainer
    {
        /// <returns>The curves in order, or null when they do not close into exactly one loop.</returns>
        public static IReadOnlyList<ChainLink>? Chain(IReadOnlyList<Segment> segments, double tolerance)
        {
            if (segments.Count == 0) return null;

            var used = new bool[segments.Count];
            var chain = new List<ChainLink> { new ChainLink(0, false) };
            used[0] = true;
            var loopStart = segments[0].Start;
            var end = segments[0].End;

            for (int step = 1; step < segments.Count; step++)
            {
                int next = -1;
                bool reversed = false;
                for (int i = 0; i < segments.Count && next < 0; i++)
                {
                    if (used[i]) continue;
                    if (Near(segments[i].Start, end, tolerance))
                    {
                        next = i;
                        reversed = false;
                    }
                    else if (Near(segments[i].End, end, tolerance))
                    {
                        next = i;
                        reversed = true;
                    }
                }

                if (next < 0) return null;

                used[next] = true;
                chain.Add(new ChainLink(next, reversed));
                end = reversed ? segments[next].Start : segments[next].End;
            }

            return Near(end, loopStart, tolerance) ? chain : null;
        }

        private static bool Near(XyzPoint a, XyzPoint b, double tolerance) =>
            Math.Abs(a.X - b.X) <= tolerance &&
            Math.Abs(a.Y - b.Y) <= tolerance &&
            Math.Abs(a.Z - b.Z) <= tolerance;
    }
}
```

`src/RVTuk.Core/TopoTools/PlaneHeight.cs`:

```csharp
using System;

namespace RVTuk.Core.TopoTools
{
    /// <summary>
    /// Where a vertical line meets a plane. A floor sloped by a slope arrow has no shape vertices
    /// to copy, yet its top is not flat. Its corners' heights are read off its top face this way
    /// and handed to the new element as ordinary corner offsets.
    /// </summary>
    public static class PlaneHeight
    {
        /// <param name="normal">Any length; only its direction matters.</param>
        /// <returns>The plane's Z above (x, y), or null for a vertical plane, which has none.</returns>
        public static double? At(XyzPoint origin, XyzPoint normal, double x, double y)
        {
            if (Math.Abs(normal.Z) < 1e-9) return null;
            return origin.Z - (normal.X * (x - origin.X) + normal.Y * (y - origin.Y)) / normal.Z;
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj --filter "FullyQualifiedName~LoopChainerTests|FullyQualifiedName~PlaneHeightTests"`
Expected: PASS, 11 tests.

Run: `dotnet build src\RVTuk.Core\RVTuk.Core.csproj -c Release2024`
Expected: `Build succeeded`.

- [ ] **Step 5: Commit**

```bash
git add src/RVTuk.Core/TopoTools/LoopChainer.cs src/RVTuk.Core/TopoTools/PlaneHeight.cs tests/RVTuk.Core.Tests/TopoTools/LoopChainerTests.cs tests/RVTuk.Core.Tests/TopoTools/PlaneHeightTests.cs
git commit -m "feat(topo-tools): chain sketch curves head to tail, read a sloped plane's height

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Parked parameters and their codec (Core)

**Files:**
- Create: `src/RVTuk.Core/TopoTools/ParkedParameter.cs`
- Create: `src/RVTuk.Core/TopoTools/ParkedParameterCodec.cs`
- Test: `tests/RVTuk.Core.Tests/TopoTools/ParkedParameterCodecTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `enum ParkedKeyKind { BuiltIn, Shared, Project }`
  - `enum ParkedStorage { Integer, Double, String, ElementId }`
  - `sealed record ParkedParameter(ParkedKeyKind KeyKind, string Key, ParkedStorage Storage, string Value)`:
    - factories `OfInteger(kind, key, int)`, `OfDouble(kind, key, double)`,
      `OfString(kind, key, string)` and `OfElementId(kind, key, long)`;
    - readers `bool TryGetInteger(out int)`, `bool TryGetDouble(out double)` and
      `bool TryGetElementId(out long)`.
  - `static class ParkedParameterCodec`: `string Encode(IReadOnlyList<ParkedParameter>)` and
    `IReadOnlyList<ParkedParameter> Decode(string?)`. Decode never throws.

- [ ] **Step 1: Write the failing tests**

`tests/RVTuk.Core.Tests/TopoTools/ParkedParameterCodecTests.cs`:

```csharp
using System.Collections.Generic;
using RVTuk.Core.TopoTools;
using Xunit;

namespace RVTuk.Core.Tests.TopoTools;

public class ParkedParameterCodecTests
{
    private const string SharedGuid = "0b4c9f4e-2d3a-4c1b-9e8f-7a6b5c4d3e2f";

    private static IReadOnlyList<ParkedParameter> RoundTrip(params ParkedParameter[] entries) =>
        ParkedParameterCodec.Decode(ParkedParameterCodec.Encode(entries));

    [Fact]
    public void RoundTripsEveryStorageType()
    {
        var entries = new[]
        {
            ParkedParameter.OfInteger(ParkedKeyKind.BuiltIn, "-1001203", 1),
            ParkedParameter.OfDouble(ParkedKeyKind.Shared, SharedGuid, 1.0 / 3.0),
            ParkedParameter.OfString(ParkedKeyKind.Project, "Finish Code", "P-12"),
            ParkedParameter.OfElementId(ParkedKeyKind.BuiltIn, "-1012101", 9007199254740993),
        };

        var decoded = RoundTrip(entries);

        Assert.Equal(entries, decoded);
        Assert.True(decoded[0].TryGetInteger(out var flag));
        Assert.Equal(1, flag);
        Assert.True(decoded[1].TryGetDouble(out var third));
        Assert.Equal(1.0 / 3.0, third);
        Assert.True(decoded[3].TryGetElementId(out var id));
        Assert.Equal(9007199254740993, id);
    }

    [Theory]
    [InlineData("ריצוף A-12")]
    [InlineData("מטבח 2 / LIQUI HARD")]
    [InlineData("KEMIKO גמר 60x60")]
    public void MixedHebrewAndLatinTextSurvivesExactly(string text)
    {
        var decoded = RoundTrip(ParkedParameter.OfString(ParkedKeyKind.Shared, SharedGuid, text));

        Assert.Equal(text, Assert.Single(decoded).Value);
    }

    [Theory]
    [InlineData("a|b;c")]
    [InlineData("50%")]
    [InlineData("%7C and %3B and %25, literally")]
    [InlineData("")]
    public void SeparatorsPercentSignsAndEmptyTextSurvive(string text)
    {
        var decoded = RoundTrip(
            ParkedParameter.OfString(ParkedKeyKind.Project, "Note|v2;x%", text),
            ParkedParameter.OfInteger(ParkedKeyKind.BuiltIn, "-1001203", 0));

        Assert.Equal(2, decoded.Count);
        Assert.Equal("Note|v2;x%", decoded[0].Key);
        Assert.Equal(text, decoded[0].Value);
        Assert.Equal("0", decoded[1].Value);
    }

    [Fact]
    public void AnEmptyListEncodesAsAnEmptyString()
    {
        Assert.Equal("", ParkedParameterCodec.Encode(new ParkedParameter[0]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("X|k|T|v")]    // unknown key kind
    [InlineData("B|k|Q|v")]    // unknown storage
    [InlineData("B|%ZZ|T|v")]  // broken escape
    [InlineData("B||T|v")]     // empty key
    [InlineData("B|k|T|v%2")]  // truncated escape
    public void DamagedInputDecodesToNothingWithoutThrowing(string? encoded)
    {
        Assert.Empty(ParkedParameterCodec.Decode(encoded));
    }

    [Fact]
    public void ADamagedEntryIsSkippedAndTheRestKept()
    {
        var good = ParkedParameterCodec.Encode(new[]
        {
            ParkedParameter.OfString(ParkedKeyKind.Project, "Mark 2", "A1"),
        });

        var decoded = ParkedParameterCodec.Decode("B|%ZZ|T|v;" + good);

        Assert.Equal("A1", Assert.Single(decoded).Value);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj --filter "FullyQualifiedName~ParkedParameterCodecTests"`
Expected: build FAILS with `The name 'ParkedParameter' does not exist in the current context`.

- [ ] **Step 3: Write the implementation**

`src/RVTuk.Core/TopoTools/ParkedParameter.cs`:

```csharp
using System.Globalization;

namespace RVTuk.Core.TopoTools
{
    /// <summary>How a parameter is found again on another element.</summary>
    public enum ParkedKeyKind
    {
        /// <summary>Key is the <c>BuiltInParameter</c> as an invariant integer.</summary>
        BuiltIn,
        /// <summary>Key is the shared parameter's GUID.</summary>
        Shared,
        /// <summary>Key is the name of a project parameter that is neither built-in nor shared.</summary>
        Project,
    }

    /// <summary>Mirrors Revit's <c>StorageType</c>, minus <c>None</c>.</summary>
    public enum ParkedStorage { Integer, Double, String, ElementId }

    /// <summary>
    /// One instance parameter value, detached from its element. The converter snapshots a source's
    /// values this way before deleting it. Any the new element cannot take (a floor's Structural
    /// flag, a shared parameter bound to Floors only) are parked on it for the return trip.
    /// <see cref="Value"/> is always invariant text, so the record compares and encodes the same
    /// on net48 and net8.
    /// </summary>
    public sealed record ParkedParameter(ParkedKeyKind KeyKind, string Key, ParkedStorage Storage, string Value)
    {
        public static ParkedParameter OfInteger(ParkedKeyKind kind, string key, int value) =>
            new(kind, key, ParkedStorage.Integer, value.ToString(CultureInfo.InvariantCulture));

        /// <summary>"R" formatting, as <see cref="TopoPointLedgerCodec"/> uses for the same reason.</summary>
        public static ParkedParameter OfDouble(ParkedKeyKind kind, string key, double value) =>
            new(kind, key, ParkedStorage.Double, value.ToString("R", CultureInfo.InvariantCulture));

        public static ParkedParameter OfString(ParkedKeyKind kind, string key, string value) =>
            new(kind, key, ParkedStorage.String, value ?? "");

        public static ParkedParameter OfElementId(ParkedKeyKind kind, string key, long value) =>
            new(kind, key, ParkedStorage.ElementId, value.ToString(CultureInfo.InvariantCulture));

        public bool TryGetInteger(out int value) =>
            int.TryParse(Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

        public bool TryGetDouble(out double value) =>
            double.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        public bool TryGetElementId(out long value) =>
            long.TryParse(Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
```

`src/RVTuk.Core/TopoTools/ParkedParameterCodec.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RVTuk.Core.TopoTools
{
    /// <summary>
    /// The parked parameters as one Extensible Storage string:
    /// <c>kind|key|storage|value;kind|key|storage|value</c>, with the kind as B, S or P and the
    /// storage as I, D, T or E.
    ///
    /// Keys and values are free text, and a project parameter's name or a Comments value can hold
    /// anything, including the separators. So <c>%</c>, <c>|</c> and <c>;</c> are escaped as
    /// <c>%25</c>, <c>%7C</c> and <c>%3B</c>, and nothing else is touched. Hebrew is stored as
    /// the UTF-16 it already is. Decoding never throws: a damaged entry is skipped, so a bad
    /// record costs the parked values and never the conversion.
    /// </summary>
    public static class ParkedParameterCodec
    {
        public static string Encode(IReadOnlyList<ParkedParameter> entries)
        {
            if (entries == null) return "";

            return string.Join(";", entries.Select(entry =>
                KindCode(entry.KeyKind) + "|" + Escape(entry.Key) + "|" +
                StorageCode(entry.Storage) + "|" + Escape(entry.Value)));
        }

        public static IReadOnlyList<ParkedParameter> Decode(string? encoded)
        {
            var entries = new List<ParkedParameter>();
            if (string.IsNullOrEmpty(encoded)) return entries;

            foreach (var text in encoded!.Split(';'))
            {
                var fields = text.Split('|');
                if (fields.Length != 4) continue;
                if (!TryKind(fields[0], out var kind)) continue;
                if (!TryStorage(fields[2], out var storage)) continue;

                var key = Unescape(fields[1]);
                if (string.IsNullOrEmpty(key)) continue;

                var value = Unescape(fields[3]);
                if (value == null) continue;

                entries.Add(new ParkedParameter(kind, key!, storage, value));
            }

            return entries;
        }

        private static string KindCode(ParkedKeyKind kind) => kind switch
        {
            ParkedKeyKind.BuiltIn => "B",
            ParkedKeyKind.Shared => "S",
            _ => "P",
        };

        private static bool TryKind(string code, out ParkedKeyKind kind)
        {
            switch (code)
            {
                case "B": kind = ParkedKeyKind.BuiltIn; return true;
                case "S": kind = ParkedKeyKind.Shared; return true;
                case "P": kind = ParkedKeyKind.Project; return true;
                default: kind = default; return false;
            }
        }

        private static string StorageCode(ParkedStorage storage) => storage switch
        {
            ParkedStorage.Integer => "I",
            ParkedStorage.Double => "D",
            ParkedStorage.String => "T",
            _ => "E",
        };

        private static bool TryStorage(string code, out ParkedStorage storage)
        {
            switch (code)
            {
                case "I": storage = ParkedStorage.Integer; return true;
                case "D": storage = ParkedStorage.Double; return true;
                case "T": storage = ParkedStorage.String; return true;
                case "E": storage = ParkedStorage.ElementId; return true;
                default: storage = default; return false;
            }
        }

        private static string Escape(string text) =>
            (text ?? "").Replace("%", "%25").Replace("|", "%7C").Replace(";", "%3B");

        /// <returns>Null on any escape this codec never writes.</returns>
        private static string? Unescape(string text)
        {
            var result = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c != '%')
                {
                    result.Append(c);
                    continue;
                }

                if (i + 2 >= text.Length) return null;
                string code = text.Substring(i + 1, 2);
                if (code == "25") result.Append('%');
                else if (code == "7C") result.Append('|');
                else if (code == "3B") result.Append(';');
                else return null;
                i += 2;
            }
            return result.ToString();
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj --filter "FullyQualifiedName~ParkedParameterCodecTests"`
Expected: PASS, 18 tests. The theories count once per row.

Run: `dotnet build src\RVTuk.Core\RVTuk.Core.csproj -c Release2024`
Expected: `Build succeeded`.

- [ ] **Step 5: Commit**

```bash
git add src/RVTuk.Core/TopoTools/ParkedParameter.cs src/RVTuk.Core/TopoTools/ParkedParameterCodec.cs tests/RVTuk.Core.Tests/TopoTools/ParkedParameterCodecTests.cs
git commit -m "feat(topo-tools): parked parameter values and their storage encoding

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: The status-line report (Core)

**Files:**
- Create: `src/RVTuk.Core/TopoTools/ConvertReport.cs`
- Test: `tests/RVTuk.Core.Tests/TopoTools/ConvertReportTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `sealed class ConvertReport` with:
  - settable `int` properties: `FloorsToToposolids`, `ToposolidsToFloors`, `PointsLost`,
    `CreasesLost`, `ParametersNotCarried` and `WarningsDismissed`;
  - `List<string> Skipped { get; }`;
  - `string Summarise()`;
  - `const string NothingSelected = "Select floors or toposolids first."`;
  - `const string Cancelled = "Cancelled — nothing converted."`;
  - `static string DescribeDependents(int hosted, int annotations, int subdivisions)`.

- [ ] **Step 1: Write the failing tests**

`tests/RVTuk.Core.Tests/TopoTools/ConvertReportTests.cs`:

```csharp
using RVTuk.Core.TopoTools;
using Xunit;

namespace RVTuk.Core.Tests.TopoTools;

public class ConvertReportTests
{
    [Fact]
    public void CountsEachDirectionWithItsOwnPlural()
    {
        var report = new ConvertReport { FloorsToToposolids = 2, ToposolidsToFloors = 1 };

        Assert.Equal("2 floors → toposolids, 1 toposolid → floor.", report.Summarise());
    }

    [Fact]
    public void ARunThatConvertedNothingSaysSoRatherThanCountingZero()
    {
        var report = new ConvertReport();
        report.Skipped.Add("Toposolid 5521: it is a subdivision");

        Assert.Equal("Nothing converted. Skipped 1: Toposolid 5521: it is a subdivision.", report.Summarise());
    }

    [Fact]
    public void ListsSkipsLossesAndDismissedWarnings()
    {
        var report = new ConvertReport
        {
            FloorsToToposolids = 1,
            PointsLost = 3,
            CreasesLost = 1,
            ParametersNotCarried = 2,
            WarningsDismissed = 1,
        };
        report.Skipped.Add("Floor 418732: it is in a model group");
        report.Skipped.Add("Floor 418733: kept its 2 hosted elements");

        Assert.Equal(
            "1 floor → toposolid. " +
            "Skipped 2: Floor 418732: it is in a model group; Floor 418733: kept its 2 hosted elements. " +
            "3 shape points and 1 split line could not be copied. " +
            "2 parameter values not carried. " +
            "1 Revit warning dismissed.",
            report.Summarise());
    }

    [Theory]
    [InlineData(2, 0, 0, "2 hosted elements")]
    [InlineData(0, 1, 0, "1 tag or dimension")]
    [InlineData(1, 0, 3, "1 hosted element and 3 subdivisions")]
    [InlineData(2, 3, 1, "2 hosted elements, 3 tags or dimensions and 1 subdivision")]
    [InlineData(0, 0, 0, "")]
    public void DescribesOnlyTheDependentsThereAre(int hosted, int annotations, int subdivisions, string expected)
    {
        Assert.Equal(expected, ConvertReport.DescribeDependents(hosted, annotations, subdivisions));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj --filter "FullyQualifiedName~ConvertReportTests"`
Expected: build FAILS with `The type or namespace name 'ConvertReport' could not be found`.

- [ ] **Step 3: Write the implementation**

`src/RVTuk.Core/TopoTools/ConvertReport.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;

namespace RVTuk.Core.TopoTools
{
    /// <summary>
    /// What one ⇄ run did, in the words the pane's status line uses. Kept in Core, and tested, as
    /// the only text the converter shows every time.
    ///
    /// Skip entries name elements by category and id ("Floor 418732"), never by type name: the
    /// status line is a left-to-right TextBlock, and a Hebrew type name there would drag the
    /// punctuation around it into its own run.
    /// </summary>
    public sealed class ConvertReport
    {
        public const string NothingSelected = "Select floors or toposolids first.";
        public const string Cancelled = "Cancelled — nothing converted.";

        public int FloorsToToposolids { get; set; }
        public int ToposolidsToFloors { get; set; }
        public List<string> Skipped { get; } = new List<string>();
        public int PointsLost { get; set; }
        public int CreasesLost { get; set; }
        public int ParametersNotCarried { get; set; }
        public int WarningsDismissed { get; set; }

        public string Summarise()
        {
            var sentences = new List<string>();

            var converted = new List<string>();
            if (FloorsToToposolids > 0)
                converted.Add(Count(FloorsToToposolids, "floor → toposolid", "floors → toposolids"));
            if (ToposolidsToFloors > 0)
                converted.Add(Count(ToposolidsToFloors, "toposolid → floor", "toposolids → floors"));
            sentences.Add(converted.Count > 0 ? string.Join(", ", converted) + "." : "Nothing converted.");

            if (Skipped.Count > 0)
                sentences.Add($"Skipped {Skipped.Count}: {string.Join("; ", Skipped)}.");

            var lost = new List<string>();
            if (PointsLost > 0) lost.Add(Count(PointsLost, "shape point", "shape points"));
            if (CreasesLost > 0) lost.Add(Count(CreasesLost, "split line", "split lines"));
            if (lost.Count > 0) sentences.Add(string.Join(" and ", lost) + " could not be copied.");

            if (ParametersNotCarried > 0)
                sentences.Add(Count(ParametersNotCarried, "parameter value", "parameter values") + " not carried.");
            if (WarningsDismissed > 0)
                sentences.Add(Count(WarningsDismissed, "Revit warning", "Revit warnings") + " dismissed.");

            return string.Join(" ", sentences);
        }

        /// <summary>
        /// "2 hosted elements, 3 tags or dimensions and 1 subdivision". Only the counts that are
        /// not zero; "" when all are.
        /// </summary>
        public static string DescribeDependents(int hosted, int annotations, int subdivisions)
        {
            var parts = new List<string>();
            if (hosted > 0) parts.Add(Count(hosted, "hosted element", "hosted elements"));
            if (annotations > 0) parts.Add(Count(annotations, "tag or dimension", "tags or dimensions"));
            if (subdivisions > 0) parts.Add(Count(subdivisions, "subdivision", "subdivisions"));

            if (parts.Count == 0) return "";
            if (parts.Count == 1) return parts[0];
            return string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[parts.Count - 1];
        }

        private static string Count(int n, string singular, string plural) =>
            n + " " + (n == 1 ? singular : plural);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj --filter "FullyQualifiedName~ConvertReportTests"`
Expected: PASS, 8 tests.

Run: `dotnet build src\RVTuk.Core\RVTuk.Core.csproj -c Release2024`
Expected: `Build succeeded`.

- [ ] **Step 5: Commit**

```bash
git add src/RVTuk.Core/TopoTools/ConvertReport.cs tests/RVTuk.Core.Tests/TopoTools/ConvertReportTests.cs
git commit -m "feat(topo-tools): the converter's status-line report

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Shared default types, dependents and loop order; Room Floor uses them (Revit)

The converter needs three things Room Floor already has privately: the default floor type, the
dependents counts and outer-loop-first ordering. Per the repo's rule they move to `Shared/`, and
Room Floor calls them there. **Room Floor's behaviour must not change.**

**Files:**
- Create: `src/RVTuk.Revit/Shared/DefaultTypes.cs`
- Create: `src/RVTuk.Revit/Shared/DependentsCounter.cs`
- Create: `src/RVTuk.Revit/Shared/LoopOrder.cs`
- Modify: `src/RVTuk.Revit/RoomFloors/RoomFloorBuilder.cs`
- Modify: `src/RVTuk.Revit/RoomFloors/Commands/RoomFloorCommand.cs:33`

**Interfaces:**
- Consumes: nothing new.
- Produces:
  - `RVTuk.Revit.Shared.DefaultTypes.FloorTypeId(Document) : ElementId`
  - `DefaultTypes.ToposolidTypeId(Document) : ElementId`
  - Both return `ElementId.InvalidElementId` when there is no usable type.
  - `DependentsCounter.Hosted(Element) : int`
  - `DependentsCounter.Annotations(Element) : int`
  - `DependentsCounter.Subdivisions(Element) : int`
  - `LoopOrder.OuterFirst(IEnumerable<CurveLoop>) : List<CurveLoop>`

- [ ] **Step 1: Create `src/RVTuk.Revit/Shared/DefaultTypes.cs`**

```csharp
using System;
using System.Linq;
using Autodesk.Revit.DB;

namespace RVTuk.Revit.Shared
{
    /// <summary>
    /// The type Revit's own tools would place next. Shared because Room Floor and the Topo Tools
    /// converter both create elements of "the type you last used" rather than asking.
    /// </summary>
    public static class DefaultTypes
    {
        /// <summary>The project's default floor type, else its first non-foundation floor type;
        /// <see cref="ElementId.InvalidElementId"/> when it has neither.</summary>
        public static ElementId FloorTypeId(Document doc)
        {
            var id = doc.GetDefaultElementTypeId(ElementTypeGroup.FloorType);
            if (id != ElementId.InvalidElementId && doc.GetElement(id) is FloorType) return id;

            return new FilteredElementCollector(doc)
                .OfClass(typeof(FloorType))
                .Cast<FloorType>()
                .Where(t => !t.IsFoundationSlab)
                .Select(t => t.Id)
                .FirstOrDefault() ?? ElementId.InvalidElementId;
        }

        /// <summary>
        /// Toposolids have no <see cref="ElementTypeGroup"/>, so this asks for the per-category
        /// default Revit keeps for family types and takes it only if it is a toposolid type.
        /// Whether Revit keeps one for this system category is unverified, and is the first in-Revit
        /// check. The fallback, the first toposolid type by name, keeps the tool working either way.
        /// </summary>
        public static ElementId ToposolidTypeId(Document doc)
        {
            try
            {
                var id = doc.GetDefaultFamilyTypeId(new ElementId(BuiltInCategory.OST_Toposolid));
                if (id != null && id != ElementId.InvalidElementId && doc.GetElement(id) is ToposolidType)
                    return id;
            }
            catch (Exception)
            {
                // Not answered for this category; fall through to the first type by name.
            }

            return new FilteredElementCollector(doc)
                .OfClass(typeof(ToposolidType))
                .Cast<ToposolidType>()
                .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(t => t.Id)
                .FirstOrDefault() ?? ElementId.InvalidElementId;
        }
    }
}
```

- [ ] **Step 2: Create `src/RVTuk.Revit/Shared/DependentsCounter.cs`**

```csharp
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace RVTuk.Revit.Shared
{
    /// <summary>
    /// What deleting an element would silently delete with it. Room Floor refuses to replace a
    /// floor that has any; the Topo Tools converter asks first. Shared for that reason.
    /// </summary>
    public static class DependentsCounter
    {
        /// <summary>Family instances, openings and hosted sweeps (e.g. slab edges).</summary>
        public static int Hosted(Element element)
        {
            var filter = new LogicalOrFilter(new List<ElementFilter>
            {
                new ElementClassFilter(typeof(FamilyInstance)),
                new ElementClassFilter(typeof(Opening)),
                new ElementClassFilter(typeof(HostedSweep)),
            });
            return element.GetDependentElements(filter).Count;
        }

        /// <summary>Tags and dimensions (e.g. floor tags, spot elevations).</summary>
        public static int Annotations(Element element)
        {
            var filter = new LogicalOrFilter(
                new ElementClassFilter(typeof(IndependentTag)),
                new ElementClassFilter(typeof(Dimension)));
            return element.GetDependentElements(filter).Count;
        }

        /// <summary>A toposolid's subdivisions; 0 for anything else.</summary>
        public static int Subdivisions(Element element) =>
            element is Toposolid toposolid ? toposolid.GetSubDivisionIds().Count : 0;
    }
}
```

- [ ] **Step 3: Create `src/RVTuk.Revit/Shared/LoopOrder.cs`**

```csharp
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RVTuk.Revit.Shared
{
    /// <summary>
    /// Outer loop first. <c>Floor.Create</c> and <c>Toposolid.Create</c> may sort their loops
    /// themselves; ordering them here costs nothing and removes the question. Shared because Room
    /// Floor and the Topo Tools converter both build slabs from loops.
    /// </summary>
    public static class LoopOrder
    {
        public static List<CurveLoop> OuterFirst(IEnumerable<CurveLoop> loops) =>
            loops.OrderByDescending(PlanExtentArea).ToList();

        /// <summary>Area of the loop's plan bounding box: enough to tell the outer loop from the
        /// holes, which it always encloses.</summary>
        private static double PlanExtentArea(CurveLoop loop)
        {
            var points = loop.SelectMany(c => c.Tessellate()).ToList();
            return (points.Max(p => p.X) - points.Min(p => p.X))
                 * (points.Max(p => p.Y) - points.Min(p => p.Y));
        }
    }
}
```

- [ ] **Step 4: Point Room Floor at the shared helpers**

In `src/RVTuk.Revit/RoomFloors/RoomFloorBuilder.cs`:

1. Add `using RVTuk.Revit.Shared;` to the usings.
2. Delete the whole `DefaultFloorTypeId` method, from its `/// <summary>The project's default floor type…` comment through its closing brace.
3. Replace
   ```csharp
               int hostedCount = old.Sum(CountHostedElements);
               int annotationCount = old.Sum(CountAnnotations);
   ```
   with
   ```csharp
               int hostedCount = old.Sum(f => DependentsCounter.Hosted(f));
               int annotationCount = old.Sum(f => DependentsCounter.Annotations(f));
   ```
4. Replace
   ```csharp
               // Outer loop first. Revit may sort the loops itself; ordering them here costs
               // nothing and removes the question.
               return loops.OrderByDescending(PlanExtentArea).ToList();
   ```
   with
   ```csharp
               return LoopOrder.OuterFirst(loops);
   ```
5. Delete the private methods `PlanExtentArea`, `CountHostedElements` and `CountAnnotations`, each
   with its `///` comment.

In `src/RVTuk.Revit/RoomFloors/Commands/RoomFloorCommand.cs`:

1. Add `using RVTuk.Revit.Shared;`.
2. Replace `var floorTypeId = RoomFloorBuilder.DefaultFloorTypeId(doc);` with
   `var floorTypeId = DefaultTypes.FloorTypeId(doc);`.

The class `<summary>` of `RoomFloorBuilder` still reads correctly and stays as it is.

- [ ] **Step 5: Verify nothing else referenced the removed members**

Run: `git grep -n "DefaultFloorTypeId\|CountHostedElements\|CountAnnotations\|PlanExtentArea" -- src`
Expected: matches only in `src/RVTuk.Revit/Shared/` (`PlanExtentArea` in `LoopOrder.cs`). There
must be none in `RoomFloors/`.

- [ ] **Step 6: Build both Revit years**

Run: `dotnet build src\RVTuk.Revit\RVTuk.Revit.csproj -c Release2024`
Expected: `Build succeeded`, 0 errors.

Run: `dotnet build src\RVTuk.Revit\RVTuk.Revit.csproj -c Release2025`
Expected: `Build succeeded`, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add src/RVTuk.Revit/Shared/DefaultTypes.cs src/RVTuk.Revit/Shared/DependentsCounter.cs src/RVTuk.Revit/Shared/LoopOrder.cs src/RVTuk.Revit/RoomFloors/RoomFloorBuilder.cs src/RVTuk.Revit/RoomFloors/Commands/RoomFloorCommand.cs
git commit -m "refactor(shared): default types, dependents and loop order move out of Room Floor

The Topo Tools converter needs all three. Room Floor's behaviour is unchanged.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Parked-parameter storage and the parameter carrier (Revit)

**Files:**
- Create: `src/RVTuk.Revit/TopoTools/ParkedParameterStore.cs`
- Create: `src/RVTuk.Revit/TopoTools/ParameterCarrier.cs`

**Interfaces:**
- Consumes: `ParkedParameter`, `ParkedKeyKind`, `ParkedStorage` and `ParkedParameterCodec` (Task 3).
- Produces:
  - `ParkedParameterStore.SchemaGuid : Guid` (public static readonly)
  - `ParkedParameterStore.Read(Element) : IReadOnlyList<ParkedParameter>`
  - `ParkedParameterStore.Write(Element, IReadOnlyList<ParkedParameter>)`: inside a transaction;
    an empty list removes the record.
  - `sealed record CarryOutcome(int Copied, int Parked, int Failed)`
  - `ParameterCarrier.Snapshot(Element source) : IReadOnlyList<ParkedParameter>`: called before
    the source is deleted.
  - `ParameterCarrier.Apply(Element target, IReadOnlyList<ParkedParameter> values) : CarryOutcome`:
    inside a transaction; it writes the parked record itself.

- [ ] **Step 1: Create `src/RVTuk.Revit/TopoTools/ParkedParameterStore.cs`**

```csharp
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using RVTuk.Core.TopoTools;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// Parameter values a converted element could not take, kept on it for the return trip. A
    /// floor's Structural flag, or a shared parameter bound to Floors only, has no home on a
    /// toposolid: it waits here and goes back when the toposolid becomes a floor again.
    /// </summary>
    public static class ParkedParameterStore
    {
        public static readonly Guid SchemaGuid = new Guid("309b77b4-64cd-45c7-bd73-d6a81b1f85ae");
        private const string SchemaName = "RVTukTopoToolsParkedParameters";
        private const string FieldName = "Entries";

        public static IReadOnlyList<ParkedParameter> Read(Element element)
        {
            var schema = Schema.Lookup(SchemaGuid);
            if (schema == null) return Array.Empty<ParkedParameter>();

            var entity = element.GetEntity(schema);
            if (!entity.IsValid()) return Array.Empty<ParkedParameter>();

            return ParkedParameterCodec.Decode(entity.Get<string>(FieldName));
        }

        /// <summary>Caller must already be inside a transaction. An empty list removes the record.</summary>
        public static void Write(Element element, IReadOnlyList<ParkedParameter> entries)
        {
            if (entries.Count == 0)
            {
                var existing = Schema.Lookup(SchemaGuid);
                if (existing != null && element.GetEntity(existing).IsValid()) element.DeleteEntity(existing);
                return;
            }

            var entity = new Entity(GetOrCreateSchema());
            entity.Set(FieldName, ParkedParameterCodec.Encode(entries));
            element.SetEntity(entity);
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

- [ ] **Step 2: Create `src/RVTuk.Revit/TopoTools/ParameterCarrier.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using RVTuk.Core.TopoTools;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>What happened to one element's parameter values.</summary>
    public sealed record CarryOutcome(int Copied, int Parked, int Failed);

    /// <summary>
    /// Carries instance parameter values from a converted element to its replacement.
    ///
    /// A parameter is found again by its built-in id, its shared GUID, or, for a project
    /// parameter, its name. A value the new element has a writable home for is set. Any other is
    /// parked on the new element (<see cref="ParkedParameterStore"/>), and every conversion offers
    /// the parked values again, so a Floors-only value survives floor → toposolid → floor. Nothing
    /// here is fatal: a value Revit refuses is counted and the conversion goes on.
    /// </summary>
    public static class ParameterCarrier
    {
        /// <summary>Set explicitly by the converter, or never meaningful to copy.</summary>
        private static readonly HashSet<BuiltInParameter> Handled = new HashSet<BuiltInParameter>
        {
            BuiltInParameter.LEVEL_PARAM,
            BuiltInParameter.SCHEDULE_LEVEL_PARAM,
            BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM,
            BuiltInParameter.TOPOSOLID_HEIGHTABOVELEVEL_PARAM,
            BuiltInParameter.ELEM_TYPE_PARAM,
            BuiltInParameter.ELEM_FAMILY_PARAM,
            BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM,
            BuiltInParameter.SYMBOL_ID_PARAM,
            BuiltInParameter.DESIGN_OPTION_ID,
        };

        private static readonly string PhaseCreatedKey =
            ((int)BuiltInParameter.PHASE_CREATED).ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// The source's own writable instance values, then whatever an earlier conversion parked on
        /// it. Read before the source is deleted.
        /// </summary>
        public static IReadOnlyList<ParkedParameter> Snapshot(Element source)
        {
            var values = new List<ParkedParameter>();
            var seen = new HashSet<string>();

            foreach (Parameter parameter in source.Parameters)
            {
                var value = Read(parameter);
                if (value != null && seen.Add(Identity(value))) values.Add(value);
            }

            foreach (var parked in ParkedParameterStore.Read(source))
            {
                if (seen.Add(Identity(parked))) values.Add(parked);
            }

            // Phase Created first: Revit refuses a demolition phase earlier than the creation phase.
            return values
                .OrderBy(v => v.KeyKind == ParkedKeyKind.BuiltIn && v.Key == PhaseCreatedKey ? 0 : 1)
                .ToList();
        }

        /// <summary>Caller must already be inside a transaction.</summary>
        public static CarryOutcome Apply(Element target, IReadOnlyList<ParkedParameter> values)
        {
            int copied = 0;
            int failed = 0;
            var parked = new List<ParkedParameter>();

            foreach (var value in values)
            {
                var parameter = Find(target, value);
                if (parameter == null || parameter.IsReadOnly || StorageOf(parameter) != value.Storage)
                {
                    parked.Add(value);
                    continue;
                }

                if (TrySet(parameter, value)) copied++;
                else failed++;
            }

            ParkedParameterStore.Write(target, parked);
            return new CarryOutcome(copied, parked.Count, failed);
        }

        private static string Identity(ParkedParameter value) => value.KeyKind + ":" + value.Key;

        private static ParkedParameter? Read(Parameter parameter)
        {
            if (parameter.IsReadOnly || !parameter.HasValue) return null;

            var definition = parameter.Definition;
            if (definition == null) return null;

            ParkedKeyKind kind;
            string key;
            var builtIn = (definition as InternalDefinition)?.BuiltInParameter ?? BuiltInParameter.INVALID;
            if (builtIn != BuiltInParameter.INVALID)
            {
                if (Handled.Contains(builtIn)) return null;
                kind = ParkedKeyKind.BuiltIn;
                key = ((int)builtIn).ToString(CultureInfo.InvariantCulture);
            }
            else if (parameter.IsShared)
            {
                kind = ParkedKeyKind.Shared;
                key = parameter.GUID.ToString("D");
            }
            else
            {
                kind = ParkedKeyKind.Project;
                key = definition.Name;
            }

            switch (parameter.StorageType)
            {
                case StorageType.Integer: return ParkedParameter.OfInteger(kind, key, parameter.AsInteger());
                case StorageType.Double: return ParkedParameter.OfDouble(kind, key, parameter.AsDouble());
                case StorageType.String: return ParkedParameter.OfString(kind, key, parameter.AsString() ?? "");
                case StorageType.ElementId: return ParkedParameter.OfElementId(kind, key, parameter.AsElementId().Value);
                default: return null;
            }
        }

        private static Parameter? Find(Element target, ParkedParameter value)
        {
            switch (value.KeyKind)
            {
                case ParkedKeyKind.BuiltIn:
                    return int.TryParse(value.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                        ? target.get_Parameter((BuiltInParameter)id)
                        : null;
                case ParkedKeyKind.Shared:
                    return Guid.TryParse(value.Key, out var guid) ? target.get_Parameter(guid) : null;
                default:
                    return target.GetParameters(value.Key).FirstOrDefault(p =>
                        !p.IsShared &&
                        ((p.Definition as InternalDefinition)?.BuiltInParameter ?? BuiltInParameter.INVALID)
                            == BuiltInParameter.INVALID);
            }
        }

        private static ParkedStorage? StorageOf(Parameter parameter) => parameter.StorageType switch
        {
            StorageType.Integer => ParkedStorage.Integer,
            StorageType.Double => ParkedStorage.Double,
            StorageType.String => ParkedStorage.String,
            StorageType.ElementId => ParkedStorage.ElementId,
            _ => null,
        };

        private static bool TrySet(Parameter parameter, ParkedParameter value)
        {
            try
            {
                switch (value.Storage)
                {
                    case ParkedStorage.Integer: return value.TryGetInteger(out var i) && parameter.Set(i);
                    case ParkedStorage.Double: return value.TryGetDouble(out var d) && parameter.Set(d);
                    case ParkedStorage.String: return parameter.Set(value.Value);
                    case ParkedStorage.ElementId: return value.TryGetElementId(out var e) && parameter.Set(new ElementId(e));
                    default: return false;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
```

- [ ] **Step 3: Build both Revit years**

Run: `dotnet build src\RVTuk.Revit\RVTuk.Revit.csproj -c Release2024`
Expected: `Build succeeded`, 0 errors.

Run: `dotnet build src\RVTuk.Revit\RVTuk.Revit.csproj -c Release2025`
Expected: `Build succeeded`, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/RVTuk.Revit/TopoTools/ParkedParameterStore.cs src/RVTuk.Revit/TopoTools/ParameterCarrier.cs
git commit -m "feat(topo-tools): carry parameter values across a conversion, parking the one-sided ones

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Slab shape and the one-element converter (Revit)

**Files:**
- Create: `src/RVTuk.Revit/TopoTools/SlabShape.cs`
- Create: `src/RVTuk.Revit/TopoTools/SlabConverter.cs`

**Interfaces:**
- Consumes:
  - `ShapeReplayPlanner`, `CreaseEnds` and `ShapeReplayPlan` (Task 1);
  - `LoopChainer`, `Segment`, `ChainLink` and `PlaneHeight` (Task 2);
  - `LoopOrder.OuterFirst` (Task 5);
  - `ParameterCarrier` and `ParkedParameterStore.SchemaGuid` (Task 6).
- Produces:
  - `sealed record SourceShape(IReadOnlyList<XyzPoint> Vertices, IReadOnlyList<CreaseEnds> Creases)`
  - `sealed record ReplayOutcome(int PointsLost, int CreasesLost)`
  - `SlabShape.Read(Element source, IReadOnlyList<CurveLoop> loops) : SourceShape`
  - `SlabShape.Replay(Document doc, Element target, SourceShape shape) : ReplayOutcome`: inside a
    transaction.
  - `sealed record ConvertOutcome(ElementId NewId, bool BecameToposolid, int PointsLost, int CreasesLost, int ParametersNotCarried)`
  - `SlabConverter.Convert(Document doc, Element source, ElementId targetTypeId) : ConvertOutcome`:
    inside the caller's transaction. It throws `InvalidOperationException` with a short
    lower-case reason ("it has no sketch to copy") when it cannot convert.

- [ ] **Step 1: Create `src/RVTuk.Revit/TopoTools/SlabShape.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RVTuk.Core.TopoTools;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>A floor's or toposolid's top surface as positions, read before the element is deleted.</summary>
    public sealed record SourceShape(IReadOnlyList<XyzPoint> Vertices, IReadOnlyList<CreaseEnds> Creases)
    {
        public static readonly SourceShape Flat =
            new SourceShape(Array.Empty<XyzPoint>(), Array.Empty<CreaseEnds>());
    }

    public sealed record ReplayOutcome(int PointsLost, int CreasesLost);

    /// <summary>
    /// Reads a slab's shape and replays it onto a freshly created flat one with the same outline.
    /// <see cref="ShapeReplayPlanner"/> decides what to do; this class only talks to
    /// <c>SlabShapeEditor</c>.
    ///
    /// Vertices are looked up by position every time they are needed, never held across edits.
    /// Whether a <c>SlabShapeVertex</c> stays valid after its neighbours change is unverified, and
    /// a position cannot go stale.
    /// </summary>
    public static class SlabShape
    {
        public static SlabShapeEditor? EditorOf(Element element) => element switch
        {
            Floor floor => floor.GetSlabShapeEditor(),
            Toposolid toposolid => toposolid.GetSlabShapeEditor(),
            _ => null,
        };

        public static SourceShape Read(Element source, IReadOnlyList<CurveLoop> loops)
        {
            var editor = EditorOf(source);
            if (editor != null && editor.IsEnabled)
            {
                var vertices = Positions(editor);
                if (vertices.Count > 0)
                {
                    var creases = new List<CreaseEnds>();
                    foreach (SlabShapeCrease crease in editor.SlabShapeCreases)
                    {
                        if (crease.CreaseType != SlabShapeCreaseType.UserDrawn) continue;
                        var curve = crease.Curve;
                        creases.Add(new CreaseEnds(ToPoint(curve.GetEndPoint(0)), ToPoint(curve.GetEndPoint(1))));
                    }
                    return new SourceShape(vertices, creases);
                }
            }

            // No shape points, yet a floor with a slope arrow is not flat: read its corners off
            // the top face instead.
            if (source is Floor floor)
                return new SourceShape(SlopedCorners(floor, loops), Array.Empty<CreaseEnds>());

            return SourceShape.Flat;
        }

        /// <summary>Caller must be inside a transaction; <paramref name="target"/> must be freshly
        /// created and regenerated, so it is still flat.</summary>
        public static ReplayOutcome Replay(Document doc, Element target, SourceShape shape)
        {
            if (shape.Vertices.Count == 0) return new ReplayOutcome(0, 0);

            var editor = EditorOf(target)
                ?? throw new InvalidOperationException("the new element cannot take shape points");
            if (!editor.IsEnabled) editor.Enable();
            doc.Regenerate();

            var flat = Positions(editor);
            if (flat.Count == 0) return new ReplayOutcome(shape.Vertices.Count, shape.Creases.Count);

            // Fresh and flat: every vertex sits at the top, which is where ModifySubElement
            // offsets and DrawPoint locations are measured from.
            double topZ = flat[0].Z;
            var plan = ShapeReplayPlanner.Plan(shape.Vertices, flat);

            // Points first, while the surface is still flat, so DrawPoint's fallback location at
            // topZ really is on the top face.
            int pointsLost = AddPoints(editor, plan.Additions, topZ);

            foreach (var offset in plan.Offsets)
            {
                var vertex = VertexAt(editor, flat[offset.TargetIndex]);
                if (vertex == null)
                {
                    pointsLost++;
                    continue;
                }

                try
                {
                    editor.ModifySubElement(vertex, offset.Offset);
                }
                catch (Exception)
                {
                    pointsLost++;
                }
            }

            int creasesLost = 0;
            if (shape.Creases.Count > 0)
            {
                doc.Regenerate();
                var live = Positions(editor);
                var creasePlan = ShapeReplayPlanner.PlanCreases(live, shape.Creases);
                creasesLost = creasePlan.Dropped;

                foreach (var pair in creasePlan.Pairs)
                {
                    var start = VertexAt(editor, live[pair.StartIndex]);
                    var end = VertexAt(editor, live[pair.EndIndex]);
                    if (start == null || end == null)
                    {
                        creasesLost++;
                        continue;
                    }

                    try
                    {
                        editor.DrawSplitLine(start, end);
                    }
                    catch (Exception)
                    {
                        creasesLost++;
                    }
                }
            }

            return new ReplayOutcome(pointsLost, creasesLost);
        }

        /// <summary>
        /// All at once when Revit takes them. <c>AddPoints</c> wants points strictly inside the
        /// boundary, so a single point on an edge fails the whole batch. Then each point is tried
        /// on its own, falling back to <c>DrawPoint</c> on the flat top plus an offset. Returns how
        /// many could not be placed at all.
        /// </summary>
        private static int AddPoints(SlabShapeEditor editor, IReadOnlyList<XyzPoint> points, double topZ)
        {
            if (points.Count == 0) return 0;

            try
            {
                editor.AddPoints(points.Select(ToRevit).ToList());
                return 0;
            }
            catch (Exception)
            {
                // Retry one by one below.
            }

            int lost = 0;
            foreach (var point in points)
            {
                try
                {
                    editor.AddPoints(new List<XYZ> { ToRevit(point) });
                    continue;
                }
                catch (Exception)
                {
                    // Try drawing it instead.
                }

                try
                {
                    var vertex = editor.DrawPoint(new XYZ(point.X, point.Y, topZ));
                    if (vertex == null)
                    {
                        lost++;
                        continue;
                    }
                    editor.ModifySubElement(vertex, point.Z - topZ);
                }
                catch (Exception)
                {
                    lost++;
                }
            }
            return lost;
        }

        private static IReadOnlyList<XyzPoint> SlopedCorners(Floor floor, IReadOnlyList<CurveLoop> loops)
        {
            PlanarFace? top = null;
            try
            {
                foreach (var reference in HostObjectUtils.GetTopFaces(floor))
                {
                    if (floor.GetGeometryObjectFromReference(reference) is PlanarFace face)
                    {
                        top = face;
                        break;
                    }
                }
            }
            catch (Exception)
            {
                return Array.Empty<XyzPoint>();
            }

            // Flat, or no planar top to read: nothing to replay.
            if (top == null || Math.Abs(top.FaceNormal.Z) > 1 - 1e-9) return Array.Empty<XyzPoint>();

            var origin = ToPoint(top.Origin);
            var normal = ToPoint(top.FaceNormal);
            var corners = new List<XyzPoint>();
            foreach (var loop in loops)
            {
                foreach (Curve curve in loop)
                {
                    var start = curve.GetEndPoint(0);
                    var z = PlaneHeight.At(origin, normal, start.X, start.Y);
                    if (z != null) corners.Add(new XyzPoint(start.X, start.Y, z.Value));
                }
            }
            return corners;
        }

        private static List<XyzPoint> Positions(SlabShapeEditor editor)
        {
            var positions = new List<XyzPoint>();
            foreach (SlabShapeVertex vertex in editor.SlabShapeVertices) positions.Add(ToPoint(vertex.Position));
            return positions;
        }

        private static SlabShapeVertex? VertexAt(SlabShapeEditor editor, XyzPoint at)
        {
            foreach (SlabShapeVertex vertex in editor.SlabShapeVertices)
            {
                var position = vertex.Position;
                if (Math.Abs(position.X - at.X) <= ShapeReplayPlanner.ToleranceFeet &&
                    Math.Abs(position.Y - at.Y) <= ShapeReplayPlanner.ToleranceFeet)
                {
                    return vertex;
                }
            }
            return null;
        }

        internal static XyzPoint ToPoint(XYZ point) => new XyzPoint(point.X, point.Y, point.Z);

        private static XYZ ToRevit(XyzPoint point) => new XYZ(point.X, point.Y, point.Z);
    }
}
```

- [ ] **Step 2: Create `src/RVTuk.Revit/TopoTools/SlabConverter.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using RVTuk.Core.TopoTools;
using RVTuk.Revit.Shared;

namespace RVTuk.Revit.TopoTools
{
    public sealed record ConvertOutcome(
        ElementId NewId, bool BecameToposolid, int PointsLost, int CreasesLost, int ParametersNotCarried);

    /// <summary>
    /// One floor becomes a toposolid, or one toposolid a floor, inside the caller's transaction.
    ///
    /// Everything is read first, then the source is deleted, then the target is created. With the
    /// source gone the new element never overlaps it, so Revit raises no overlap warning; if
    /// anything later fails, the caller's rollback brings the source back. The top surface is what
    /// is preserved; the thickness and bottom follow the new type.
    /// </summary>
    public static class SlabConverter
    {
        private const string OurVendorId = "KnafoKlimor";

        /// <summary>Caller must already be inside a transaction.</summary>
        /// <exception cref="InvalidOperationException">With a short reason, e.g. "it has no sketch to copy".</exception>
        public static ConvertOutcome Convert(Document doc, Element source, ElementId targetTypeId)
        {
            bool toToposolid = source is Floor;
            var sourceId = source.Id;

            var loops = ReadLoops(doc, source);
            var levelId = source.LevelId;
            if (levelId == ElementId.InvalidElementId) throw new InvalidOperationException("it has no level");
            double offset = OffsetParameter(source)?.AsDouble() ?? 0;
            var shape = SlabShape.Read(source, loops);
            var parameters = ParameterCarrier.Snapshot(source);
            var entities = OurEntities(source);
            bool pinned = source.Pinned;

            if (pinned) source.Pinned = false;
            doc.Delete(sourceId);

            Element target = toToposolid
                ? Toposolid.Create(doc, loops, targetTypeId, levelId)
                : Floor.Create(doc, loops, targetTypeId, levelId);
            OffsetParameter(target)?.Set(offset);
            doc.Regenerate();

            var replay = SlabShape.Replay(doc, target, shape);
            var carry = ParameterCarrier.Apply(target, parameters);
            foreach (var entity in entities) target.SetEntity(entity);
            if (pinned) target.Pinned = true;

            return new ConvertOutcome(target.Id, toToposolid, replay.PointsLost, replay.CreasesLost, carry.Failed);
        }

        /// <summary>The source's sketch, loop by loop, curves as they are. Arcs stay arcs, holes stay holes.</summary>
        private static List<CurveLoop> ReadLoops(Document doc, Element source)
        {
            var sketchId = source switch
            {
                Floor floor => floor.SketchId,
                Toposolid toposolid => toposolid.SketchId,
                _ => ElementId.InvalidElementId,
            };
            if (doc.GetElement(sketchId) is not Sketch sketch)
                throw new InvalidOperationException("it has no sketch to copy");

            double tolerance = doc.Application.VertexTolerance;
            var loops = new List<CurveLoop>();
            foreach (CurveArray array in sketch.Profile)
            {
                var curves = new List<Curve>();
                foreach (Curve curve in array) curves.Add(curve);

                var segments = curves
                    .Select(c => new Segment(SlabShape.ToPoint(c.GetEndPoint(0)), SlabShape.ToPoint(c.GetEndPoint(1))))
                    .ToList();
                var chain = LoopChainer.Chain(segments, tolerance)
                    ?? throw new InvalidOperationException("its sketch has a loop that does not close");

                var loop = new CurveLoop();
                foreach (var link in chain)
                    loop.Append(link.Reversed ? curves[link.Index].CreateReversed() : curves[link.Index]);
                loops.Add(loop);
            }

            if (loops.Count == 0) throw new InvalidOperationException("its sketch is empty");
            return LoopOrder.OuterFirst(loops);
        }

        private static Parameter? OffsetParameter(Element element) =>
            element.get_Parameter(element is Toposolid
                ? BuiltInParameter.TOPOSOLID_HEIGHTABOVELEVEL_PARAM
                : BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);

        /// <summary>
        /// RVTuk's own storage on the source: today the Topo Tools point ledger and the Room Floor
        /// link. The parked-parameter record is left out because <see cref="ParameterCarrier"/>
        /// writes an updated one.
        /// </summary>
        private static IReadOnlyList<Entity> OurEntities(Element source)
        {
            var entities = new List<Entity>();
            foreach (var guid in source.GetEntitySchemaGuids())
            {
                if (guid == ParkedParameterStore.SchemaGuid) continue;

                var schema = Schema.Lookup(guid);
                if (schema == null) continue;
                if (!string.Equals(schema.VendorId, OurVendorId, StringComparison.OrdinalIgnoreCase)) continue;
                if (!schema.ReadAccessGranted() || !schema.WriteAccessGranted()) continue;

                var entity = source.GetEntity(schema);
                if (entity.IsValid()) entities.Add(entity);
            }
            return entities;
        }
    }
}
```

- [ ] **Step 3: Build both Revit years**

Run: `dotnet build src\RVTuk.Revit\RVTuk.Revit.csproj -c Release2024`
Expected: `Build succeeded`, 0 errors.

Run: `dotnet build src\RVTuk.Revit\RVTuk.Revit.csproj -c Release2025`
Expected: `Build succeeded`, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/RVTuk.Revit/TopoTools/SlabShape.cs src/RVTuk.Revit/TopoTools/SlabConverter.cs
git commit -m "feat(topo-tools): convert one floor or toposolid, replaying its outline, shape and data

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: The run: refusals, dialogs, transactions (Revit)

**Files:**
- Create: `src/RVTuk.Revit/TopoTools/ConvertRunner.cs`
- Create: `src/RVTuk.Revit/TopoTools/ExternalEvents/TopoConvertEventHandler.cs`

**Interfaces:**
- Consumes:
  - `ConvertReport` (Task 4);
  - `DefaultTypes` and `DependentsCounter` (Task 5);
  - `SlabConverter.Convert` and `ConvertOutcome` (Task 7).
- Produces:
  - `ConvertRunner.Run(UIDocument uiDoc) : string` (the status-line summary);
  - `TopoConvertEventHandler` with `Reset()`, `WaitForCompletion()`, `string Summary` and
    `GetName()`.

- [ ] **Step 1: Create `src/RVTuk.Revit/TopoTools/ConvertRunner.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RVTuk.Core.TopoTools;
using RVTuk.Revit.Shared;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// The ⇄ button. It works in three steps:
    /// <list type="number">
    /// <item><b>Refuse</b> what cannot be converted: subdivisions, group members, elements in
    /// another design option, elements borrowed by someone else.</item>
    /// <item><b>Ask</b> about anything whose deletion would take other elements with it, all
    /// before any change. Cancel therefore really does mean nothing happened, and no dialog is
    /// ever up while a transaction is open.</item>
    /// <item><b>Convert</b> in one transaction group, so one Ctrl+Z undoes the run. Each element
    /// gets its own transaction inside it, so one Revit refuses rolls back alone.</item>
    /// </list>
    /// </summary>
    public static class ConvertRunner
    {
        private const string Title = "Topo Tools";
        private const string TransactionName = "Convert Floor ⇄ Toposolid";

        private sealed class Candidate
        {
            public ElementId Id = ElementId.InvalidElementId;
            public bool IsFloor;
            public string Label = "";
            public string TypeName = "";
            public string Dependents = "";
        }

        public static string Run(UIDocument uiDoc)
        {
            var doc = uiDoc.Document;

            var selected = uiDoc.Selection.GetElementIds()
                .Select(doc.GetElement)
                .Where(e => e is Floor || e is Toposolid)
                .ToList();
            if (selected.Count == 0) return ConvertReport.NothingSelected;

            var report = new ConvertReport();
            var floorTypeId = DefaultTypes.FloorTypeId(doc);
            var toposolidTypeId = DefaultTypes.ToposolidTypeId(doc);
            var activeOptionId = DesignOption.GetActiveDesignOptionId(doc);

            var candidates = new List<Candidate>();
            foreach (var element in selected)
            {
                var label = Label(element);
                var refusal = RefusalOf(doc, element, activeOptionId, floorTypeId, toposolidTypeId);
                if (refusal != null)
                {
                    report.Skipped.Add($"{label}: {refusal}");
                    continue;
                }

                candidates.Add(new Candidate
                {
                    Id = element.Id,
                    IsFloor = element is Floor,
                    Label = label,
                    TypeName = doc.GetElement(element.GetTypeId())?.Name ?? "",
                    Dependents = ConvertReport.DescribeDependents(
                        DependentsCounter.Hosted(element),
                        DependentsCounter.Annotations(element),
                        DependentsCounter.Subdivisions(element)),
                });
            }

            var approved = Ask(candidates, report);
            if (approved == null) return ConvertReport.Cancelled;

            var newIds = ConvertAll(doc, approved, floorTypeId, toposolidTypeId, report);
            if (newIds.Count > 0) uiDoc.Selection.SetElementIds(newIds);

            return report.Summarise();
        }

        private static string? RefusalOf(
            Document doc, Element element, ElementId activeOptionId, ElementId floorTypeId, ElementId toposolidTypeId)
        {
            if (element is Toposolid toposolid &&
                toposolid.HostTopoId != null && toposolid.HostTopoId != ElementId.InvalidElementId)
                return "it is a subdivision";

            if (element.GroupId != ElementId.InvalidElementId) return "it is in a model group";

            // The replacement is created in the option being edited (or the main model), so
            // anything elsewhere would change option on the way through.
            var optionId = element.DesignOption?.Id ?? ElementId.InvalidElementId;
            if (optionId != activeOptionId)
            {
                return optionId == ElementId.InvalidElementId
                    ? "it is in the main model while a design option is being edited"
                    : "it is in a design option that is not being edited";
            }

            if (doc.IsWorkshared &&
                WorksharingUtils.GetCheckoutStatus(doc, element.Id, out string owner) == CheckoutStatus.OwnedByOtherUser)
                return $"it is borrowed by {Isolate(owner)}";

            if (element is Floor && toposolidTypeId == ElementId.InvalidElementId)
                return "the project has no toposolid types";
            if (element is Toposolid && floorTypeId == ElementId.InvalidElementId)
                return "the project has no floor types";

            return null;
        }

        /// <returns>The candidates to convert, or null when the user cancelled.</returns>
        private static List<Candidate>? Ask(List<Candidate> candidates, ConvertReport report)
        {
            var approved = new List<Candidate>();
            int stillToAsk = candidates.Count(c => c.Dependents.Length > 0);
            bool? standingAnswer = null;

            foreach (var candidate in candidates)
            {
                if (candidate.Dependents.Length == 0)
                {
                    approved.Add(candidate);
                    continue;
                }
                stillToAsk--;

                bool convert;
                if (standingAnswer != null)
                {
                    convert = standingAnswer.Value;
                }
                else
                {
                    var dialog = new TaskDialog(Title)
                    {
                        MainInstruction = candidate.IsFloor ? "Convert floor → toposolid?" : "Convert toposolid → floor?",
                        MainContent =
                            $"{(candidate.IsFloor ? "Floor" : "Toposolid")} {Isolate(candidate.TypeName)} " +
                            $"(id {candidate.Id.Value}) carries {candidate.Dependents} that converting deletes.",
                        AllowCancellation = true,
                    };
                    dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Convert and delete them");
                    dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Skip this one");
                    if (stillToAsk > 0) dialog.VerificationText = "Do the same for the rest of the selection";

                    var result = dialog.Show();
                    if (result == TaskDialogResult.CommandLink1) convert = true;
                    else if (result == TaskDialogResult.CommandLink2) convert = false;
                    else return null;

                    if (stillToAsk > 0 && dialog.WasVerificationChecked()) standingAnswer = convert;
                }

                if (convert) approved.Add(candidate);
                else report.Skipped.Add($"{candidate.Label}: kept its {candidate.Dependents}");
            }

            return approved;
        }

        private static List<ElementId> ConvertAll(
            Document doc, List<Candidate> approved, ElementId floorTypeId, ElementId toposolidTypeId, ConvertReport report)
        {
            var newIds = new List<ElementId>();
            if (approved.Count == 0) return newIds;

            using (var group = new TransactionGroup(doc, TransactionName))
            {
                group.Start();

                foreach (var candidate in approved)
                {
                    var source = doc.GetElement(candidate.Id);
                    if (source == null)
                    {
                        report.Skipped.Add($"{candidate.Label}: deleted by an earlier conversion in this run");
                        continue;
                    }

                    var failures = new FailureCollector();
                    using (var tx = new Transaction(doc, TransactionName))
                    {
                        tx.Start();
                        var options = tx.GetFailureHandlingOptions();
                        options.SetFailuresPreprocessor(failures);
                        options.SetClearAfterRollback(true);
                        tx.SetFailureHandlingOptions(options);

                        ConvertOutcome outcome;
                        try
                        {
                            outcome = SlabConverter.Convert(doc, source, candidate.IsFloor ? toposolidTypeId : floorTypeId);
                        }
                        catch (Exception ex)
                        {
                            tx.RollBack();
                            report.Skipped.Add($"{candidate.Label}: {ex.Message}");
                            continue;
                        }

                        if (tx.Commit() != TransactionStatus.Committed)
                        {
                            report.Skipped.Add($"{candidate.Label}: {failures.Error ?? "Revit rejected the new element"}");
                            continue;
                        }

                        if (outcome.BecameToposolid) report.FloorsToToposolids++;
                        else report.ToposolidsToFloors++;
                        report.PointsLost += outcome.PointsLost;
                        report.CreasesLost += outcome.CreasesLost;
                        report.ParametersNotCarried += outcome.ParametersNotCarried;
                        report.WarningsDismissed += failures.Warnings;
                        newIds.Add(outcome.NewId);
                    }
                }

                // Nothing converted: leave no empty entry on the undo list.
                if (newIds.Count > 0) group.Assimilate();
                else group.RollBack();
            }

            return newIds;
        }

        private static string Label(Element element) =>
            $"{(element is Floor ? "Floor" : "Toposolid")} {element.Id.Value}";

        /// <summary>First-strong isolates (U+2068…U+2069), as Room Floor uses: a Hebrew name ending
        /// in a number would otherwise drag the text after it into its right-to-left run.</summary>
        private static string Isolate(string text) => "⁨" + text + "⁩";

        /// <summary>
        /// Twenty elements must not mean twenty warning popups: warnings are dismissed and counted.
        /// An error rolls back that element alone; its first description becomes the skip reason.
        /// </summary>
        private sealed class FailureCollector : IFailuresPreprocessor
        {
            public int Warnings { get; private set; }
            public string? Error { get; private set; }

            public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
            {
                bool hasError = false;
                foreach (var failure in accessor.GetFailureMessages())
                {
                    if (failure.GetSeverity() == FailureSeverity.Warning)
                    {
                        accessor.DeleteWarning(failure);
                        Warnings++;
                    }
                    else
                    {
                        hasError = true;
                        Error ??= failure.GetDescriptionText();
                    }
                }
                return hasError ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
            }
        }
    }
}
```

- [ ] **Step 2: Create `src/RVTuk.Revit/TopoTools/ExternalEvents/TopoConvertEventHandler.cs`**

```csharp
using System;
using System.Threading;
using Autodesk.Revit.UI;

namespace RVTuk.Revit.TopoTools.ExternalEvents
{
    /// <summary>
    /// Runs the ⇄ conversion on Revit's main thread. Its dialogs are shown from here, on that
    /// thread, while the pane's worker thread waits. ConvertRunner owns the transactions, so a
    /// failure comes back as text rather than as an exception.
    /// </summary>
    public class TopoConvertEventHandler : IExternalEventHandler
    {
        private readonly ManualResetEventSlim _done = new(false);

        public string Summary { get; private set; } = "";

        public void Reset() => _done.Reset();
        public void WaitForCompletion() => _done.Wait();

        public void Execute(UIApplication app)
        {
            try
            {
                var uiDoc = app.ActiveUIDocument;
                if (uiDoc?.Document == null)
                {
                    Summary = "No document is open.";
                    return;
                }

                Summary = ConvertRunner.Run(uiDoc);
            }
            catch (Exception ex)
            {
                Summary = "Convert failed: " + ex.Message;
            }
            finally
            {
                _done.Set();
            }
        }

        public string GetName() => "RVTuk.TopoToolsConvertEventHandler";
    }
}
```

- [ ] **Step 3: Build both Revit years**

Run: `dotnet build src\RVTuk.Revit\RVTuk.Revit.csproj -c Release2024`
Expected: `Build succeeded`, 0 errors.

Run: `dotnet build src\RVTuk.Revit\RVTuk.Revit.csproj -c Release2025`
Expected: `Build succeeded`, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/RVTuk.Revit/TopoTools/ConvertRunner.cs src/RVTuk.Revit/TopoTools/ExternalEvents/TopoConvertEventHandler.cs
git commit -m "feat(topo-tools): the convert run, with refusals, dependents dialogs and one-step undo

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: The ⇄ button and its wiring (UI + Revit)

**Files:**
- Modify: `src/RVTuk.UI/TopoTools/ViewModels/TopoToolsPaneViewModel.cs`
- Modify: `src/RVTuk.UI/TopoTools/Views/TopoToolsPaneView.xaml`
- Modify: `src/RVTuk.Revit/Application.cs`

**Interfaces:**
- Consumes: `TopoConvertEventHandler` (Task 8).
- Produces:
  - `TopoToolsPaneViewModel` constructor gains a fifth parameter, `Func<string> convert`.
  - `RelayCommand ConvertCommand { get; }`.

**Note:** today a Refresh overwrites the status line with the view's scope message, so any
summary set just before it vanishes. ▶ Apply and a height commit both lose theirs this way; that
is recorded in the backlog in Task 10 and **not** fixed here. The converter must keep its summary,
so Refresh gains a private variant that restores a given message.

- [ ] **Step 1: Add the convert delegate and command to the view model**

In `src/RVTuk.UI/TopoTools/ViewModels/TopoToolsPaneViewModel.cs`:

1. Change the class `<summary>` sentence
   `All three Revit interactions arrive as delegates that block on an ExternalEvent ping-pong, so all three are invoked from the thread pool`
   to
   `Every Revit interaction arrives as a delegate that blocks on an ExternalEvent ping-pong, so each is invoked from the thread pool`.
   Leave the rest of the comment as it is.
2. After `private readonly Action<IReadOnlyList<long>> _selectInView;` add
   `private readonly Func<string> _convert;`.
3. Replace the constructor signature and first assignments:
   ```csharp
        public TopoToolsPaneViewModel(
            Func<double, TopoScope> discover,
            Func<double, string> apply,
            Func<IReadOnlyList<long>, string, string> setElevation,
            Action<IReadOnlyList<long>> selectInView,
            Func<string> convert)
        {
            _discover = discover;
            _apply = apply;
            _setElevation = setElevation;
            _selectInView = selectInView;
            _convert = convert;
            _dispatcher = Dispatcher.CurrentDispatcher;

            Lines = new ObservableCollection<TopoLineRowViewModel>();
            RefreshCommand = new RelayCommand(Refresh, () => !IsBusy);
            ApplyCommand = new RelayCommand(RunApply, () => !IsBusy);
            ConvertCommand = new RelayCommand(RunConvert, () => !IsBusy);
   ```
   The rest of the constructor (the spacing load) stays unchanged.
4. After `public RelayCommand ApplyCommand { get; }` add `public RelayCommand ConvertCommand { get; }`.
5. Replace the `Refresh` method header and its `Populate` call so a caller can keep its own
   message:
   ```csharp
        /// <summary>Re-reads the active view's topo lines. Returns immediately.</summary>
        public void Refresh() => RefreshCore(null);

        /// <param name="statusAfter">Shown instead of the view's own message once the rows are
        /// rebuilt, so a run's summary survives the refresh that follows it.</param>
        private void RefreshCore(string? statusAfter)
        {
            if (IsBusy) return;
   ```
   Keep the method body that follows unchanged except for the success dispatch, which becomes
   ```csharp
                _dispatcher.Invoke(() =>
                {
                    Populate(scope, statusAfter);
                    IsBusy = false;
                });
   ```
6. Change `private void Populate(TopoScope scope)` to
   `private void Populate(TopoScope scope, string? statusAfter)`, and its last line
   `StatusMessage = scope.Message;` to `StatusMessage = statusAfter ?? scope.Message;`.
7. After the `RunApply` method add:
   ```csharp
        /// <summary>
        /// ⇄: converts the floors and toposolids selected in Revit. Revit may show dialogs while
        /// this waits, which is why it runs off the UI thread like everything else here. The
        /// summary stays on the status line through the refresh that follows, because the run can
        /// change which toposolids the view has.
        /// </summary>
        private void RunConvert()
        {
            IsBusy = true;
            StatusMessage = "Converting…";

            Task.Run(() =>
            {
                string summary;
                try
                {
                    summary = _convert();
                }
                catch (Exception ex)
                {
                    summary = "Convert failed: " + ex.Message;
                }

                _dispatcher.Invoke(() =>
                {
                    IsBusy = false;
                    RefreshCore(summary);
                });
            });
        }
   ```

- [ ] **Step 2: Add the button to the pane**

In `src/RVTuk.UI/TopoTools/Views/TopoToolsPaneView.xaml`, inside the horizontal `StackPanel` that
holds the ▶ and ⟳ buttons, add after the ⟳ (`RefreshCommand`) button:

```xml
                <Button Command="{Binding ConvertCommand}"
                        Width="40" Height="40" FontSize="17"
                        Content="&#x21C4;" Margin="6,0,0,0"
                        ToolTip="Convert selected floors &#x21C4; toposolids"/>
```

Also update the XAML comment above that grid, `Settings on the left, the two verbs on the right`,
to read `Settings on the left, the verbs on the right`.

- [ ] **Step 3: Wire the event in `Application.cs`**

In `src/RVTuk.Revit/Application.cs`:

1. After `public static ExternalEvent TopoSelectLinesEvent { get; private set; } = null!;` add:
   ```csharp
        public static TopoConvertEventHandler TopoConvertHandler { get; private set; } = null!;
        public static ExternalEvent TopoConvertEvent { get; private set; } = null!;
   ```
2. After `TopoSelectLinesEvent    = ExternalEvent.Create(TopoSelectLinesHandler);` add:
   ```csharp
                TopoConvertHandler      = new TopoConvertEventHandler();
                TopoConvertEvent        = ExternalEvent.Create(TopoConvertHandler);
   ```
3. After the `selectTopoLines` delegate's closing `};` add:
   ```csharp
                Func<string> convertTopo = () =>
                {
                    TopoConvertHandler.Reset();
                    TopoConvertEvent.Raise();
                    TopoConvertHandler.WaitForCompletion();
                    return TopoConvertHandler.Summary;
                };
   ```
4. Change the view model construction to pass it:
   ```csharp
                TopoToolsPaneViewModel =
                    new RVTuk.UI.TopoTools.ViewModels.TopoToolsPaneViewModel(
                        discoverTopo, applyTopo, setTopoElevation, selectTopoLines, convertTopo);
   ```
5. In the comment `All three block on WaitForCompletion`, change `All three` to `All of them`.

- [ ] **Step 4: Build everything and run the Core suite**

Run: `dotnet build RVTuk.sln -c Release2024`
Expected: `Build succeeded`, 0 errors.

Run: `dotnet build RVTuk.sln -c Release2025`
Expected: `Build succeeded`, 0 errors.

Run: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj`
Expected: every test passes, the existing suite plus the 46 added in Tasks 1–4.

- [ ] **Step 5: Commit**

```bash
git add src/RVTuk.UI/TopoTools/ViewModels/TopoToolsPaneViewModel.cs src/RVTuk.UI/TopoTools/Views/TopoToolsPaneView.xaml src/RVTuk.Revit/Application.cs
git commit -m "feat(topo-tools): the pane's convert button

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 10: Docs and the in-Revit checklist

**Files:**
- Modify: `docs/tools/topo-tools/README.md`
- Modify: `docs/tools/topo-tools/backlog.md`
- Modify: `docs/tools/room-floors/README.md`
- Modify: `CLAUDE.md`

**Interfaces:** documentation only. It names `ConvertRunner`, `SlabConverter`, `SlabShape`,
`ParameterCarrier`, `ParkedParameterStore`, `DefaultTypes` and `DependentsCounter` exactly as
Tasks 5–8 define them.

- [ ] **Step 1: Add the converter to `docs/tools/topo-tools/README.md`**

Insert this section immediately before the `**Status:**` paragraph:

```markdown
## Floor ⇄ toposolid

**⇄ in the pane converts the selection.** Every selected floor becomes a toposolid and every
selected toposolid a floor. A mixed selection flips each element its own way, and the new elements
are left selected, so pressing ⇄ again flips them straight back. One Ctrl+Z undoes the whole run.

**The type is Revit's last-placed type** for the new kind, meaning what Revit's own Floor or
Toposolid tool would place next (`Shared/DefaultTypes`). A round trip therefore restores
everything below *except* the type.

**What carries across:**
- **The outline:** the sketch as it is, so arcs stay arcs and holes stay holes.
- **Level and height offset.**
- **The top surface, point for point.** Corners are raised in place, interior and edge points
  added, and user-drawn split lines redrawn (`SlabShape`). The thickness and the bottom follow the
  new type.
- **Parameters.** Every writable value both kinds carry is copied: Comments, Mark, phases,
  workset, and shared or project parameters bound to both. A value only one kind can hold, such as
  a floor's *Structural* flag or a Floors-only shared parameter, is **parked** in Extensible
  Storage (`ParkedParameterStore`) and put back on the return trip.
- **RVTuk's own storage:** the point ledger, so ▶ Apply Points stays idempotent across a round
  trip, and the Room Floor link.
- **Pinned.**

**Lost:**
- A sloped floor's slope arrow. It comes back as a flat floor with raised corners and the same
  geometry.
- Join Geometry.
- Per-view graphic overrides.
- Excavation relationships (Revit 2025).
- Any point or split line Revit refuses. This is counted on the status line.

**Dependents are confirmed first.** Hosted families, openings, slab edges, tags, dimensions and
subdivisions would be deleted with the source, so each element that has any gets a dialog —
*Convert and delete them* or *Skip this one* — with *Do the same for the rest of the selection*.
Esc cancels the whole run before anything has changed.

**Refused, with the reason on the status line:**
- toposolid subdivisions;
- elements in a model group;
- elements outside the design option being edited;
- elements borrowed by another user.

**Code:** `ConvertRunner` (the run), `SlabConverter` (one element), `SlabShape`,
`ParameterCarrier`, `ParkedParameterStore`, with `DefaultTypes` and `DependentsCounter` in
`RVTuk.Revit/Shared/`. The pure parts are in Core with tests: `ShapeReplayPlanner`,
`LoopChainer`, `PlaneHeight`, `ParkedParameterCodec`, `ConvertReport`.

### In-Revit checklist: floor ⇄ toposolid

Tick each case only once it has actually been checked in Revit.

| # | Case | Expected | 2024 | 2025 |
|---|------|----------|------|------|
| 1 | **Default toposolid type probe.** Place a toposolid with a type that is *not* first alphabetically, then convert a floor | Record which type the new toposolid gets: the placed one means `GetDefaultFamilyTypeId` answers; the first by name means the fallback ran | [ ] | [ ] |
| 2 | Flat rectangular floor → toposolid | Same outline, level and offset; top at the same height | [ ] | [ ] |
| 3 | Floor with a hole and an arc edge → toposolid → floor | Hole and arc intact both ways | [ ] | [ ] |
| 4 | Shape-edited floor (raised corners, interior points) → toposolid → floor | Every vertex back at its Z within 0.1 mm | [ ] | [ ] |
| 5 | Survey toposolid (hundreds of points) → floor → toposolid | Same surface; acceptable speed | [ ] | [ ] |
| 6 | Apply Points, round trip the toposolid, Apply Points again | Same counts added and replaced; nothing reported "moved by hand" | [ ] | [ ] |
| 7 | User-drawn split line on a floor → toposolid | Split line present on the toposolid | [ ] | [ ] |
| 8 | Floor with points on a boundary edge → toposolid | Edge points present (via `AddPoints` or the `DrawPoint` fallback) | [ ] | [ ] |
| 9 | Sloped floor (slope arrow) → toposolid → floor | Same geometry; comes back shape-edited with no slope arrow | [ ] | [ ] |
| 10 | Structural floor with a Floors-only shared parameter set to `ריצוף A-12` → toposolid → floor | Structural ticked again; parameter reads `ריצוף A-12` | [ ] | [ ] |
| 11 | Comments, Mark, Phase Created, Workset | Copied both ways | [ ] | [ ] |
| 12 | A Room Floor floor → toposolid → floor, then click its room with Room Floor | The floor is replaced, not duplicated | [ ] | [ ] |
| 13 | Floor with a hosted family: Convert / Skip / Esc | Deleted and converted / untouched, "kept its 1 hosted element" / "Cancelled — nothing converted." | [ ] | [ ] |
| 14 | Three floors with hosted families, "Do the same" ticked on the first | One dialog only | [ ] | [ ] |
| 15 | Floor whose type name is Hebrew with Latin and a number, e.g. `ריצוף LIQUI HARD 200`, with a hosted family | Dialog text reads correctly; the name and "(id …)" do not swap places | [ ] | [ ] |
| 16 | Mixed selection: 2 floors, 1 toposolid, 1 wall | Each flips; wall ignored; new elements selected | [ ] | [ ] |
| 17 | A toposolid subdivision selected | Skipped: "it is a subdivision" | [ ] | [ ] |
| 18 | An element in a model group; one outside the design option being edited | Both skipped with their reasons | [ ] | [ ] |
| 19 | Ctrl+Z after converting three elements | All three back in one step | [ ] | [ ] |
| 20 | Workshared: an element borrowed by another user | Skipped, naming the owner | [ ] | [ ] |
| 21 | Pinned floor → toposolid | The toposolid is pinned | [ ] | [ ] |
| 22 | Nothing relevant selected, press ⇄ | "Select floors or toposolids first." | [ ] | [ ] |
| 23 | The status line after any conversion | The summary stays after the pane refreshes | [ ] | [ ] |
```

In the same README's `## Docs` list, add:

```markdown
- [specs/2026-09-29-floor-toposolid-convert-design.md](specs/2026-09-29-floor-toposolid-convert-design.md) — floor ⇄ toposolid converter design
- [plans/2026-09-29-floor-toposolid-convert.md](plans/2026-09-29-floor-toposolid-convert.md) — its implementation plan
```

In the README's `## Code` table, change the Revit row to:

```markdown
| Revit | `src/RVTuk.Revit/TopoTools/` (line style, elevation store, collectors, router, applier, ledger store, runner, pane, selection handler, external events, floor ⇄ toposolid converter) |
```

- [ ] **Step 2: Add verification and the found defect to `docs/tools/topo-tools/backlog.md`**

After the `## To check after the 2026-08-02 UI round` section, add:

```markdown
## To check: floor ⇄ toposolid converter (2026-09-29)

Work through the checklist in [README.md](README.md#in-revit-checklist-floor--toposolid). The
two unknowns the design leans on are cases 1 and 8: whether Revit answers
`GetDefaultFamilyTypeId` for toposolids, and whether `AddPoints` takes points on a boundary edge.
Case 6 is the most important: a round trip must not break Apply Points' idempotence.

Also unverified, and only visible in Revit:
- whether an `Entity` read from an element stays usable for `SetEntity` after that element is
  deleted (cases 6 and 12 cover it);
- whether `Sketch.Profile` of a sloped floor includes the slope arrow (case 9).
```

Under `## Next`, add as a new first bullet:

```markdown
- **▶ Apply's and a height commit's summaries vanish at once.** Both set the status line and then
  call `Refresh()`, whose `Populate` overwrites it with the view's scope message. Found while
  building the converter, which avoids it with `RefreshCore(summary)`. The fix is to pass the
  summary the same way from `RunApply` and `CommitElevation`.
```

- [ ] **Step 3: Note the limitation in `docs/tools/room-floors/README.md`**

After the paragraph that starts `**Stops at the wall face.**`, add:

```markdown
**Toposolids.** Topo Tools' ⇄ can turn a room's floor into a toposolid and back, and the room link
travels with it. While it is a toposolid, though, Room Floor does not see it (`FindLinkedFloors`
collects floors only), so clicking the room then creates a second floor. Convert it back first.
```

- [ ] **Step 4: Mention the converter in `CLAUDE.md`**

In the **Topo Tools** feature bullet, before the sentence starting `All four line styles`, add:

```markdown
The pane's **⇄** button converts the selected floors into toposolids and toposolids into floors (Revit's last-placed type; outline, top-surface points, parameters — one-sided ones parked in Extensible Storage for the return trip — and RVTuk storage carry across; dependents are confirmed in a dialog first). See [`docs/tools/topo-tools/specs/2026-09-29-floor-toposolid-convert-design.md`](docs/tools/topo-tools/specs/2026-09-29-floor-toposolid-convert-design.md).
```

- [ ] **Step 5: Commit**

```bash
git add docs/tools/topo-tools/README.md docs/tools/topo-tools/backlog.md docs/tools/room-floors/README.md CLAUDE.md
git commit -m "docs(topo-tools): floor ⇄ toposolid converter, its in-Revit checklist, and a found defect

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

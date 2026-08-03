# Line Styles, Dimension Rings and Reference Lines Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rename both tools' line styles to the office's `_DP-` convention, create them on first run instead of behind a setup button, split Auto Dimensions into an outer and an inner ring that own their openings by priority, and add a `_DP-Dim Ref` pointer line that puts a mark on a wall's end face.

**Architecture:** Core (`RVTuk.Core`) holds the pure 2D rules and is unit-tested; the Revit layer (`RVTuk.Revit`) resolves geometry and references against the API. Ring priority is a sort key in `CandidateMatcher`. A ref line is never itself dimensioned — Core decides which strings it meets and which of its ends aims away, and the Revit layer turns that end into a reference on a wall's **end face**, which is the one face on a parallel wall a string can legally measure.

**Tech Stack:** C# (net48 for Release2024, net8.0-windows for Release2025), WPF, Revit API 2024/2025, xunit.

## Global Constraints

- **Style names, exactly:** `_DP-Dim Outer`, `_DP-Dim Inner`, `_DP-Dim Ref`, `_DP-Topo Line`.
- **No migration.** The old `Dimensions_Line` and `Topo_Line` subcategories are never read, never renamed, never deleted.
- **Layer rule:** `RVTuk.Core` must not reference any Revit or WPF type. `RVTuk.UI` must not reference any Revit type.
- **Namespace = root namespace + folder path, exactly.**
- **A file lives in a tool folder iff only that tool uses it** — otherwise `Shared/`.
- **Build both configs before every commit that touches Revit or UI code:** `dotnet build RVTuk.sln -c Release2024` and `-c Release2025`. `Release2023` builds `KKarea.Revit`, which touches none of this.
- **Core tests must stay green:** `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj`.
- Only `RVTuk.Core` and `RVTuk.Core.Tests` are unit-testable. Revit-layer tasks verify by compiling both configs plus the in-Revit checklist in Task 6.

---

### Task 1: Dimension rings and ownership priority (Core)

**Files:**
- Create: `src/RVTuk.Core/AutoDimensions/DimensionRing.cs`
- Modify: `src/RVTuk.Core/AutoDimensions/CandidateMatcher.cs`
- Test: `tests/RVTuk.Core.Tests/AutoDimensions/CandidateMatcherTests.cs`

**Interfaces:**
- Consumes: `XyPoint` (`RVTuk.Core.Shared.Geometry`), `WallCandidate`, `CandidateMatch`, `WallCrossingFinder.TryGetCrossingParameter`.
- Produces:
  - `enum DimensionRing { Outer = 0, Inner = 1 }`
  - `record ReferenceLine(XyPoint Start, XyPoint End, DimensionRing Ring = DimensionRing.Outer)`
  - `readonly record struct CandidateMatchResult(int Index, double T)`
  - `CandidateMatcher.FindMatchesForLines(IReadOnlyList<ReferenceLine>, IReadOnlyList<WallCandidate>, IReadOnlyList<CandidateMatch>, IReadOnlyList<WallCandidate>) → IReadOnlyList<IReadOnlyList<CandidateMatchResult>>`
  - `CandidateMatcher.FindMatchIndicesForLines(...)` — unchanged signature, now a wrapper.

- [ ] **Step 1: Create the ring enum**

Create `src/RVTuk.Core/AutoDimensions/DimensionRing.cs`:

```csharp
namespace RVTuk.Core.AutoDimensions
{
    /// <summary>
    /// Which ring of dimension strings a reference line belongs to.
    ///
    /// THE VALUE IS THE PRIORITY. Ownership of an opening is settled by ring first, so Outer
    /// must sort before Inner. Do not renumber these.
    /// </summary>
    public enum DimensionRing
    {
        Outer = 0,
        Inner = 1,
    }
}
```

- [ ] **Step 2: Write the failing tests**

Add `using System.Linq;` to the top of `tests/RVTuk.Core.Tests/AutoDimensions/CandidateMatcherTests.cs` (it currently has `System.Collections.Generic`, `RVTuk.Core.AutoDimensions`, `RVTuk.Core.Shared.Geometry`, `Xunit`), then append these six tests inside the class:

```csharp
    [Fact]
    public void AnOpeningVisibleToBothRingsGoesToTheOuterLine()
    {
        // A facade window at y=10. The inner string stands 2 away and the outer 10 away, and
        // the outer takes it anyway — that is the whole point of the ring. The inner line is
        // listed first so this cannot pass merely by preferring the earlier line.
        var segments = new List<WallCandidate> { new(new XyPoint(4, 10), new XyPoint(5, 10)) };
        var modes = new[] { CandidateMatch.Alongside };
        var lines = new[]
        {
            new ReferenceLine(new XyPoint(0, 8), new XyPoint(10, 8), DimensionRing.Inner),
            new ReferenceLine(new XyPoint(0, 0), new XyPoint(10, 0), DimensionRing.Outer),
        };

        var result = CandidateMatcher.FindMatchIndicesForLines(lines, segments, modes, NoOccluders);

        Assert.Empty(result[0]);
        Assert.Equal(new[] { 0 }, result[1]);
    }

    [Fact]
    public void AnOpeningWalledOffFromEveryOuterLineFallsToTheInner()
    {
        // The wall at y=5 stands between the opening and the outer string, but not between it
        // and the inner one — so the outer cannot claim it and the inner does.
        var segments = new List<WallCandidate> { new(new XyPoint(4, 10), new XyPoint(5, 10)) };
        var modes = new[] { CandidateMatch.Alongside };
        var occluders = new List<WallCandidate> { new(new XyPoint(0, 5), new XyPoint(10, 5)) };
        var lines = new[]
        {
            new ReferenceLine(new XyPoint(0, 0), new XyPoint(10, 0), DimensionRing.Outer),
            new ReferenceLine(new XyPoint(0, 8), new XyPoint(10, 8), DimensionRing.Inner),
        };

        var result = CandidateMatcher.FindMatchIndicesForLines(lines, segments, modes, occluders);

        Assert.Empty(result[0]);
        Assert.Equal(new[] { 0 }, result[1]);
    }

    [Fact]
    public void NearestStillWinsWithinARing()
    {
        var segments = new List<WallCandidate> { new(new XyPoint(4, 10), new XyPoint(5, 10)) };
        var modes = new[] { CandidateMatch.Alongside };
        var lines = new[]
        {
            new ReferenceLine(new XyPoint(0, 0), new XyPoint(10, 0), DimensionRing.Outer),
            new ReferenceLine(new XyPoint(0, 6), new XyPoint(10, 6), DimensionRing.Outer),
        };

        var result = CandidateMatcher.FindMatchIndicesForLines(lines, segments, modes, NoOccluders);

        Assert.Empty(result[0]);
        Assert.Equal(new[] { 0 }, result[1]);
    }

    [Fact]
    public void ATieWithinARingKeepsTheEarlierLine()
    {
        // Equidistant on either side, so re-running must not shuffle the opening between them.
        var segments = new List<WallCandidate> { new(new XyPoint(4, 0), new XyPoint(5, 0)) };
        var modes = new[] { CandidateMatch.Alongside };
        var lines = new[]
        {
            new ReferenceLine(new XyPoint(0, -3), new XyPoint(10, -3), DimensionRing.Outer),
            new ReferenceLine(new XyPoint(0, 3), new XyPoint(10, 3), DimensionRing.Outer),
        };

        var result = CandidateMatcher.FindMatchIndicesForLines(lines, segments, modes, NoOccluders);

        Assert.Equal(new[] { 0 }, result[0]);
        Assert.Empty(result[1]);
    }

    [Fact]
    public void AWallCrossedByBothRingsIsMeasuredByBoth()
    {
        // Only openings are owned. A wall crossed by two strings is measured by both, which is
        // what a chained string is — the ring must not change that.
        var segments = new List<WallCandidate> { new(new XyPoint(5, -20), new XyPoint(5, 20)) };
        var modes = new[] { CandidateMatch.Crossing };
        var lines = new[]
        {
            new ReferenceLine(new XyPoint(0, 0), new XyPoint(10, 0), DimensionRing.Outer),
            new ReferenceLine(new XyPoint(0, 8), new XyPoint(10, 8), DimensionRing.Inner),
        };

        var result = CandidateMatcher.FindMatchIndicesForLines(lines, segments, modes, NoOccluders);

        Assert.Equal(new[] { 0 }, result[0]);
        Assert.Equal(new[] { 0 }, result[1]);
    }

    [Fact]
    public void FindMatchesForLinesReportsTheStationAlongTheLine()
    {
        // The runner needs T to slot ref-line marks into the right place in the string.
        var segments = new List<WallCandidate>
        {
            new(new XyPoint(8, -5), new XyPoint(8, 5)),
            new(new XyPoint(2, -5), new XyPoint(2, 5)),
        };
        var modes = new[] { CandidateMatch.Crossing, CandidateMatch.Crossing };
        var lines = new[] { new ReferenceLine(LineStart, LineEnd) };

        var result = CandidateMatcher.FindMatchesForLines(lines, segments, modes, NoOccluders);

        Assert.Equal(new[] { 1, 0 }, result[0].Select(m => m.Index));
        Assert.Equal(0.2, result[0][0].T, 6);
        Assert.Equal(0.8, result[0][1].T, 6);
    }
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj --filter "FullyQualifiedName~CandidateMatcherTests"`

Expected: compile error — `ReferenceLine` has no three-argument constructor, and `CandidateMatcher.FindMatchesForLines` does not exist.

- [ ] **Step 4: Add the ring to `ReferenceLine`**

In `src/RVTuk.Core/AutoDimensions/CandidateMatcher.cs`, replace:

```csharp
    /// <summary>One reference line, as plain 2D endpoints.</summary>
    public record ReferenceLine(XyPoint Start, XyPoint End);
```

with:

```csharp
    /// <summary>
    /// One reference line, as plain 2D endpoints, plus the ring its style puts it in. The ring
    /// defaults to Outer so the single-line <see cref="CandidateMatcher.FindMatchIndices"/>
    /// wrapper stays meaningful — one line's ring cannot matter.
    /// </summary>
    public record ReferenceLine(XyPoint Start, XyPoint End,
                                DimensionRing Ring = DimensionRing.Outer);

    /// <summary>One matched candidate: its index, and where along the line it sits.</summary>
    public readonly record struct CandidateMatchResult(int Index, double T);
```

- [ ] **Step 5: Replace `FindMatchIndicesForLines` with the T-carrying version plus a wrapper**

In the same file, replace the whole `FindMatchIndicesForLines` method (its doc comment and body) with:

```csharp
        /// <summary>
        /// Matches for every reference line at once. Walls go to each line that crosses them; an
        /// opening goes to the single line that owns it, so a facade with three stacked
        /// dimension strings dimensions each door once.
        ///
        /// Ownership is settled by (ring, then distance, then line order). An OUTER line that
        /// qualifies beats every inner line outright, however much closer the inner one stands:
        /// a facade window belongs on the facade string, not on the interior string that happens
        /// to sit nearer it. Only what no outer line can see falls through to the inner lines,
        /// settled among themselves the same way. Within a ring the nearest wins, and a tie keeps
        /// the earlier line so re-running never shuffles an opening between strings.
        ///
        /// Only openings are owned. A wall crossed by three strings is measured by all three —
        /// that is what a chained string is.
        ///
        /// Ownership is decided among the lines an opening actually qualifies for (parallel host
        /// wall, centre within the line's span, nothing in the way), not merely the nearest line:
        /// a door the nearest line cannot see must still fall to one that can.
        ///
        /// <paramref name="occluders"/> is every wall that could stand in the way, which is not
        /// the same list as the walls being dimensioned — a wall still blocks when the user has
        /// unticked Walls.
        /// </summary>
        public static IReadOnlyList<IReadOnlyList<CandidateMatchResult>> FindMatchesForLines(
            IReadOnlyList<ReferenceLine> lines,
            IReadOnlyList<WallCandidate> segments,
            IReadOnlyList<CandidateMatch> matchModes,
            IReadOnlyList<WallCandidate> occluders)
        {
            var perLine = new List<List<CandidateMatchResult>>();
            for (int i = 0; i < lines.Count; i++) perLine.Add(new List<CandidateMatchResult>());

            for (int c = 0; c < segments.Count; c++)
            {
                var mode = c < matchModes.Count ? matchModes[c] : CandidateMatch.Crossing;

                if (mode == CandidateMatch.Crossing)
                {
                    for (int l = 0; l < lines.Count; l++)
                    {
                        if (WallCrossingFinder.TryGetCrossingParameter(
                                lines[l].Start, lines[l].End, segments[c], out var t))
                            perLine[l].Add(new CandidateMatchResult(c, t));
                    }
                    continue;
                }

                var owner = -1;
                var ownerT = 0.0;
                var ownerRing = DimensionRing.Outer;
                var ownerDistance = double.MaxValue;
                for (int l = 0; l < lines.Count; l++)
                {
                    if (!TryGetAlongside(lines[l].Start, lines[l].End, segments[c],
                            out var t, out var offset)) continue;
                    if (IsBlocked(lines[l].Start, lines[l].End, occluders, t, offset)) continue;

                    var ring = lines[l].Ring;
                    var distance = Math.Abs(offset);

                    // Ring first, then distance. Both strict, so a tie on both keeps the earlier
                    // line. The seeded ownerRing is never read — the guard skips the comparison
                    // until there is an owner to compare against.
                    if (owner >= 0)
                    {
                        if (ring > ownerRing) continue;
                        if (ring == ownerRing && distance >= ownerDistance) continue;
                    }

                    owner = l;
                    ownerT = t;
                    ownerRing = ring;
                    ownerDistance = distance;
                }

                if (owner >= 0) perLine[owner].Add(new CandidateMatchResult(c, ownerT));
            }

            return perLine
                .Select(m => (IReadOnlyList<CandidateMatchResult>)m.OrderBy(x => x.T).ToList())
                .ToList();
        }

        /// <summary>
        /// The indices alone, in order along each line. A thin wrapper over
        /// <see cref="FindMatchesForLines"/> for callers that do not need the station.
        /// </summary>
        public static IReadOnlyList<IReadOnlyList<int>> FindMatchIndicesForLines(
            IReadOnlyList<ReferenceLine> lines,
            IReadOnlyList<WallCandidate> segments,
            IReadOnlyList<CandidateMatch> matchModes,
            IReadOnlyList<WallCandidate> occluders)
        {
            return FindMatchesForLines(lines, segments, matchModes, occluders)
                .Select(m => (IReadOnlyList<int>)m.Select(x => x.Index).ToList())
                .ToList();
        }
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj`

Expected: PASS, all tests, including the pre-existing `CandidateMatcherTests` cases which go through the unchanged wrapper.

- [ ] **Step 7: Commit**

```bash
git add src/RVTuk.Core/AutoDimensions/DimensionRing.cs src/RVTuk.Core/AutoDimensions/CandidateMatcher.cs tests/RVTuk.Core.Tests/AutoDimensions/CandidateMatcherTests.cs && git commit -m "feat(auto-dimensions): an outer string outranks a nearer inner one"
```

---

### Task 2: `RefLineMatcher` — the 2D half of a reference line (Core)

**Files:**
- Create: `src/RVTuk.Core/AutoDimensions/RefLineMatcher.cs`
- Test: `tests/RVTuk.Core.Tests/AutoDimensions/RefLineMatcherTests.cs`

**Interfaces:**
- Consumes: `ReferenceLine` and `XyPoint` from Task 1.
- Produces:
  - `readonly record struct RefLineHit(int LineIndex, double T)`
  - `sealed record RefLineMatch(IReadOnlyList<RefLineHit> Hits, XyPoint TargetEnd)`
  - `RefLineMatcher.Match(XyPoint refStart, XyPoint refEnd, IReadOnlyList<ReferenceLine> lines, double touchTolerance) → RefLineMatch?`

- [ ] **Step 1: Write the failing tests**

Create `tests/RVTuk.Core.Tests/AutoDimensions/RefLineMatcherTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using RVTuk.Core.AutoDimensions;
using RVTuk.Core.Shared.Geometry;
using Xunit;

namespace RVTuk.Core.Tests.AutoDimensions;

public class RefLineMatcherTests
{
    // An outer string along y=0 and an inner string along y=3, both running x=0..10.
    private static readonly IReadOnlyList<ReferenceLine> TwoStrings = new[]
    {
        new ReferenceLine(new XyPoint(0, 0), new XyPoint(10, 0), DimensionRing.Outer),
        new ReferenceLine(new XyPoint(0, 3), new XyPoint(10, 3), DimensionRing.Inner),
    };

    /// <summary>Feet, matching what the Revit side passes: about 3 mm of slack.</summary>
    private const double Tolerance = 0.01;

    [Fact]
    public void AnEndpointTouchingAStringMatchesItAndTheOtherEndIsTheTarget()
    {
        // Drawn from a wall end at (5,6) down to the inner string, stopping on it.
        var match = RefLineMatcher.Match(
            new XyPoint(5, 6), new XyPoint(5, 3), TwoStrings, Tolerance);

        Assert.NotNull(match);
        Assert.Equal(new[] { 1 }, match!.Hits.Select(h => h.LineIndex));
        Assert.Equal(0.5, match.Hits[0].T, 6);
        Assert.Equal(new XyPoint(5, 6), match.TargetEnd);
    }

    [Fact]
    public void ALineCrossingBothStringsMatchesBoth()
    {
        // Run out through both and both get the mark — how far you draw is the control.
        var match = RefLineMatcher.Match(
            new XyPoint(5, 6), new XyPoint(5, -1), TwoStrings, Tolerance);

        Assert.NotNull(match);
        Assert.Equal(new[] { 0, 1 }, match!.Hits.Select(h => h.LineIndex).OrderBy(i => i));
        Assert.All(match.Hits, hit => Assert.Equal(0.5, hit.T, 6));
        Assert.Equal(new XyPoint(5, 6), match.TargetEnd);
    }

    [Fact]
    public void ALineCrossingAStringAndRunningPastItStillTargetsTheFarEnd()
    {
        // Only the outer string here, crossed mid-span with the line carrying on beyond it.
        var strings = new[] { TwoStrings[0] };

        var match = RefLineMatcher.Match(
            new XyPoint(5, 8), new XyPoint(5, -2), strings, Tolerance);

        Assert.NotNull(match);
        Assert.Equal(new XyPoint(5, 8), match!.TargetEnd);
    }

    [Fact]
    public void ALineTouchingNoStringMatchesNothing()
    {
        // Stops short at y=4, above both strings.
        Assert.Null(RefLineMatcher.Match(
            new XyPoint(5, 6), new XyPoint(5, 4), TwoStrings, Tolerance));
    }

    [Fact]
    public void ALineWithBothEndsOnStringsIsRejected()
    {
        // Spans string to string, so neither end points at anything to reference.
        Assert.Null(RefLineMatcher.Match(
            new XyPoint(5, 3), new XyPoint(5, 0), TwoStrings, Tolerance));
    }

    [Fact]
    public void ALineMeetingAStringPastItsEndIsNotAMatch()
    {
        // x=15 is off the end of both strings, which stop at x=10.
        Assert.Null(RefLineMatcher.Match(
            new XyPoint(15, 6), new XyPoint(15, -1), TwoStrings, Tolerance));
    }

    [Fact]
    public void ALineRunningAlongAStringDoesNotMeetIt()
    {
        // Parallel: there is no point at which it meets, only an overlap.
        var strings = new[] { TwoStrings[0] };

        Assert.Null(RefLineMatcher.Match(
            new XyPoint(2, 0), new XyPoint(8, 0), strings, Tolerance));
    }

    [Fact]
    public void ADegenerateLineIsRejected()
    {
        Assert.Null(RefLineMatcher.Match(
            new XyPoint(5, 3), new XyPoint(5, 3), TwoStrings, Tolerance));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj --filter "FullyQualifiedName~RefLineMatcherTests"`

Expected: compile error — `RefLineMatcher` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/RVTuk.Core/AutoDimensions/RefLineMatcher.cs`:

```csharp
using System;
using System.Collections.Generic;
using RVTuk.Core.Shared.Geometry;

namespace RVTuk.Core.AutoDimensions
{
    /// <summary>Where a ref line meets one string: which string, and where along it.</summary>
    public readonly record struct RefLineHit(int LineIndex, double T);

    /// <summary>
    /// Which strings a ref line joins, and which of its own ends points away from them at the
    /// thing to be referenced.
    /// </summary>
    public sealed record RefLineMatch(IReadOnlyList<RefLineHit> Hits, XyPoint TargetEnd);

    /// <summary>
    /// The 2D half of a _DP-Dim Ref line: which dimension strings it touches or crosses, at what
    /// station along each, and which of its endpoints aims at the wall. Everything the Revit side
    /// needs before it goes looking for a face.
    ///
    /// A ref line joins EVERY string it meets — how far it is drawn is the control. Stopping it
    /// at the inner string marks only the inner string; running it out through both marks both.
    ///
    /// The target end is the endpoint farther from the strings it met, which is the natural
    /// reading of a line drawn from a wall end out to a string. Overshooting PAST the string by
    /// more than the wall's own distance from it flips which end is read as the target; the wall
    /// search then finds nothing and the run summary says so.
    /// </summary>
    public static class RefLineMatcher
    {
        private const double MinimumLength = 1e-12;

        public static RefLineMatch? Match(
            XyPoint refStart,
            XyPoint refEnd,
            IReadOnlyList<ReferenceLine> lines,
            double touchTolerance)
        {
            var refX = refEnd.X - refStart.X;
            var refY = refEnd.Y - refStart.Y;
            var refLengthSquared = refX * refX + refY * refY;
            if (refLengthSquared < MinimumLength) return null;

            // The tolerance is a length; the parameter it buys depends on how long the line is.
            var slack = touchTolerance / Math.Sqrt(refLengthSquared);

            var hits = new List<RefLineHit>();
            var minU = double.MaxValue;
            var maxU = double.MinValue;

            for (int l = 0; l < lines.Count; l++)
            {
                if (!TryIntersect(refStart, refX, refY, lines[l], out var u, out var t)) continue;

                // Slack on the ref line's own span, so a line stopping just short of the string
                // still counts as touching it. None on the string's span: a ref line meeting a
                // string past its end does not join it.
                if (u < -slack || u > 1 + slack) continue;
                if (t < 0 || t > 1) continue;

                hits.Add(new RefLineHit(l, t));
                if (u < minU) minU = u;
                if (u > maxU) maxU = u;
            }

            if (hits.Count == 0) return null;

            // Both ends sitting on strings leaves no end pointing at anything.
            if (minU <= slack && maxU >= 1 - slack) return null;

            var targetEnd = minU > 1 - maxU ? refStart : refEnd;
            return new RefLineMatch(hits, targetEnd);
        }

        /// <summary>
        /// Intersection in the two lines' own parameters: u along the ref line, t along the
        /// string. False when they are parallel — a ref line running along a string never meets
        /// it at a point.
        /// </summary>
        private static bool TryIntersect(
            XyPoint refStart,
            double refX,
            double refY,
            ReferenceLine line,
            out double u,
            out double t)
        {
            u = 0;
            t = 0;

            var lineX = line.End.X - line.Start.X;
            var lineY = line.End.Y - line.Start.Y;

            var denominator = refX * lineY - refY * lineX;
            if (Math.Abs(denominator) < MinimumLength) return false;

            var dx = line.Start.X - refStart.X;
            var dy = line.Start.Y - refStart.Y;

            u = (dx * lineY - dy * lineX) / denominator;
            t = (dx * refY - dy * refX) / denominator;
            return true;
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj`

Expected: PASS, all tests.

- [ ] **Step 5: Commit**

```bash
git add src/RVTuk.Core/AutoDimensions/RefLineMatcher.cs tests/RVTuk.Core.Tests/AutoDimensions/RefLineMatcherTests.cs && git commit -m "feat(auto-dimensions): which strings a ref line meets, and which end aims at the wall"
```

---

### Task 3: `LineStyleCreator`, and Topo Tools loses its setup button

**Files:**
- Create: `src/RVTuk.Revit/Shared/LineStyleCreator.cs`
- Modify: `src/RVTuk.Revit/TopoTools/TopoLineStyle.cs`
- Modify: `src/RVTuk.Revit/TopoTools/ExternalEvents/TopoDiscoveryEventHandler.cs`
- Delete: `src/RVTuk.Revit/TopoTools/ExternalEvents/TopoSetupEventHandler.cs`
- Modify: `src/RVTuk.Revit/TopoTools/TopoRunner.cs`
- Modify: `src/RVTuk.Core/TopoTools/TopoScope.cs`
- Modify: `src/RVTuk.UI/TopoTools/ViewModels/TopoToolsPaneViewModel.cs`
- Modify: `src/RVTuk.UI/TopoTools/Views/TopoToolsPaneView.xaml`
- Modify: `src/RVTuk.Revit/Application.cs`

**Interfaces:**
- Produces:
  - `RVTuk.Revit.Shared.LineStyleCreator.Exists(Document, string) → bool`
  - `RVTuk.Revit.Shared.LineStyleCreator.EnsureExists(Document, string)` — requires an open transaction
  - `RVTuk.Revit.Shared.LineStyleCreator.TryEnsureInOwnTransaction(Document, params string[]) → bool`
  - `TopoLineStyle.LineStyleName == "_DP-Topo Line"`
  - `TopoScope(string ViewName, IReadOnlyList<TopoLineInfo> Lines, int ToposolidCount, string Message)` — the `IsProjectSetUp` member is gone.

- [ ] **Step 1: Create the shared helper**

Create `src/RVTuk.Revit/Shared/LineStyleCreator.cs`:

```csharp
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace RVTuk.Revit.Shared
{
    /// <summary>
    /// Creates the dedicated line subcategories the tools drive their work off. Shared because
    /// Auto Dimensions and Topo Tools both need it, and both create theirs on first run rather
    /// than behind a setup button — you cannot draw a line on a style that does not exist yet.
    /// </summary>
    public static class LineStyleCreator
    {
        public static bool Exists(Document doc, string name) =>
            doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines)
                .SubCategories.Contains(name);

        /// <summary>Must be called inside an open transaction.</summary>
        public static void EnsureExists(Document doc, string name)
        {
            var linesCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            if (linesCategory.SubCategories.Contains(name)) return;
            doc.Settings.Categories.NewSubcategory(linesCategory, name);
        }

        /// <summary>
        /// Creates whichever of <paramref name="names"/> are missing, in a transaction of its own.
        ///
        /// Opens that transaction ONLY when something is actually missing, so the ordinary
        /// refresh — every refresh after the first — stays read-only and does not mark the
        /// document modified. Returns false rather than throwing when the document cannot take a
        /// transaction: a read-only model must still refresh, it simply refreshes without the
        /// styles, which reads the same as a project nobody has drawn in yet.
        /// </summary>
        public static bool TryEnsureInOwnTransaction(Document doc, params string[] names)
        {
            try
            {
                var linesCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);

                var missing = new List<string>();
                foreach (var name in names)
                {
                    if (!linesCategory.SubCategories.Contains(name)) missing.Add(name);
                }

                if (missing.Count == 0) return true;
                if (doc.IsReadOnly) return false;

                using (var tx = new Transaction(doc, "RVTuk — create line styles"))
                {
                    tx.Start();
                    try
                    {
                        foreach (var name in missing) EnsureExists(doc, name);
                        tx.Commit();
                        return true;
                    }
                    catch
                    {
                        tx.RollBack();
                        return false;
                    }
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
```

- [ ] **Step 2: Rename the Topo style and delegate to the helper**

Replace the whole of `src/RVTuk.Revit/TopoTools/TopoLineStyle.cs`:

```csharp
using Autodesk.Revit.DB;
using RVTuk.Revit.Shared;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// The dedicated line subcategory ("_DP-Topo Line") that marks a detail line as a topo
    /// contour — the same arrangement Auto Dimensions uses for its own styles. Created on the
    /// pane's first refresh, because you cannot draw the line before the style exists.
    /// </summary>
    public static class TopoLineStyle
    {
        public const string LineStyleName = "_DP-Topo Line";

        public static bool Exists(Document doc) => LineStyleCreator.Exists(doc, LineStyleName);

        /// <summary>Must be called inside an open transaction.</summary>
        public static void EnsureExists(Document doc) =>
            LineStyleCreator.EnsureExists(doc, LineStyleName);

        public static bool IsTopoLine(CurveElement curveElement)
        {
            return curveElement.LineStyle is GraphicsStyle style
                && style.GraphicsStyleCategory != null
                && style.GraphicsStyleCategory.Name == LineStyleName;
        }
    }
}
```

- [ ] **Step 3: Create the style on discovery**

In `src/RVTuk.Revit/TopoTools/ExternalEvents/TopoDiscoveryEventHandler.cs`, add `using RVTuk.Revit.Shared;` to the usings, and insert the ensure call immediately before the `Result = TopoRunner.Discover(...)` line so the block reads:

```csharp
                double spacingFeet = UnitUtils.ConvertToInternalUnits(
                    _spacingCentimetres, UnitTypeId.Centimeters);

                // First run in this project creates the style. Cheap after that: the helper only
                // opens a transaction when the style is actually absent.
                LineStyleCreator.TryEnsureInOwnTransaction(doc, TopoLineStyle.LineStyleName);

                Result = TopoRunner.Discover(doc, uiDoc!.ActiveView, spacingFeet);
```

Also update the class doc comment's last line from `Read-only — no transaction.` to:

```csharp
    /// Read-only, apart from creating the line style on a project that has none yet.
```

- [ ] **Step 4: Delete the setup handler**

```bash
git rm src/RVTuk.Revit/TopoTools/ExternalEvents/TopoSetupEventHandler.cs
```

- [ ] **Step 5: Drop the not-set-up state from `TopoScope`**

Replace the whole of `src/RVTuk.Core/TopoTools/TopoScope.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace RVTuk.Core.TopoTools
{
    /// <summary>Everything one discovery pass tells the pane.</summary>
    public sealed record TopoScope(
        string ViewName,
        IReadOnlyList<TopoLineInfo> Lines,
        int ToposolidCount,
        string Message)
    {
        public static TopoScope Unavailable(string viewName, string message) =>
            new TopoScope(viewName, Array.Empty<TopoLineInfo>(), 0, message);
    }
}
```

- [ ] **Step 6: Drop the style guards from `TopoRunner`**

In `src/RVTuk.Revit/TopoTools/TopoRunner.cs`:

In `Discover`, delete these two lines (discovery has just created the style, so a project without one has no lines, which the message already covers):

```csharp
            if (!TopoLineStyle.Exists(doc))
                return TopoScope.NotSetUp(viewName);
```

and change the return at the end of `Discover` from

```csharp
            return new TopoScope(true, plan.Name, lines, targets.Count, Describe(lines, targets.Count));
```

to

```csharp
            return new TopoScope(plan.Name, lines, targets.Count, Describe(lines, targets.Count));
```

In `Apply`, delete these two lines:

```csharp
            if (!TopoLineStyle.Exists(doc))
                return "This project has no Topo_Line style yet.";
```

There is deliberately no `EnsureExists` in `Apply` to replace it: `Apply` builds its plan before opening its transaction, so creating the style there could not give that run any lines. Discovery is what guarantees the style, and a project without one simply reports no lines.

In `Describe`, update the message:

```csharp
            if (lines.Count == 0)
                return "No topo lines in this view — draw detail lines on the _DP-Topo Line style.";
```

- [ ] **Step 7: Strip the setup path from the pane view model**

In `src/RVTuk.UI/TopoTools/ViewModels/TopoToolsPaneViewModel.cs`:

Delete the field `private readonly Func<string> _setUpProject;`.

Change the constructor signature and body — remove the `Func<string> setUpProject` parameter and its assignment, so it reads:

```csharp
        public TopoToolsPaneViewModel(
            Func<double, TopoScope> discover,
            Func<double, string> apply,
            Func<IReadOnlyList<long>, string, string> setElevation,
            Action<IReadOnlyList<long>> selectInView)
        {
            _discover = discover;
            _apply = apply;
            _setElevation = setElevation;
            _selectInView = selectInView;
            _dispatcher = Dispatcher.CurrentDispatcher;

            Lines = new ObservableCollection<TopoLineRowViewModel>();
            RefreshCommand = new RelayCommand(Refresh, () => !IsBusy);
            ApplyCommand = new RelayCommand(RunApply, () => !IsBusy);
```

(the rest of the constructor — the config load and the spacing fallback — is unchanged).

Delete the property `public RelayCommand SetUpCommand { get; }`.

Delete the whole `IsProjectSetUp` property, the `NeedsSetup` property, and the `_isProjectSetUp` field.

Delete the whole `RunSetUp()` method.

In `Populate`, delete the line `IsProjectSetUp = scope.IsProjectSetUp;`.

- [ ] **Step 8: Remove the setup banner from the pane**

In `src/RVTuk.UI/TopoTools/Views/TopoToolsPaneView.xaml`, delete the entire `<Border DockPanel.Dock="Top" ... Visibility="{Binding NeedsSetup, ...}">` element and its contents (the warning `TextBlock` and the "Set up this project" `Button`).

Then delete the now-unused converter from the resources:

```xml
            <BooleanToVisibilityConverter x:Key="BoolVis"/>
```

- [ ] **Step 9: Unwire the setup event**

In `src/RVTuk.Revit/Application.cs`:

Delete these two static properties:

```csharp
        public static TopoSetupEventHandler TopoSetupHandler { get; private set; } = null!;
        public static ExternalEvent TopoSetupEvent { get; private set; } = null!;
```

Inside `if (RegisterTopoTools)`, delete these two lines:

```csharp
                TopoSetupHandler     = new TopoSetupEventHandler();
                TopoSetupEvent       = ExternalEvent.Create(TopoSetupHandler);
```

Delete the whole `setUpTopoProject` delegate:

```csharp
                Func<string> setUpTopoProject = () =>
                {
                    TopoSetupHandler.Reset();
                    TopoSetupEvent.Raise();
                    TopoSetupHandler.WaitForCompletion();
                    return TopoSetupHandler.Summary;
                };
```

And drop it from the view model construction:

```csharp
                TopoToolsPaneViewModel =
                    new RVTuk.UI.TopoTools.ViewModels.TopoToolsPaneViewModel(
                        discoverTopo, applyTopo, setTopoElevation, selectTopoLines);
```

- [ ] **Step 10: Build both configs**

Run: `dotnet build RVTuk.sln -c Release2024`
Expected: Build succeeded, 0 errors.

Run: `dotnet build RVTuk.sln -c Release2025`
Expected: Build succeeded, 0 errors.

Run: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj`
Expected: PASS, all tests.

- [ ] **Step 11: Commit**

```bash
git add -A src/RVTuk.Revit/Shared src/RVTuk.Revit/TopoTools src/RVTuk.Core/TopoTools src/RVTuk.UI/TopoTools src/RVTuk.Revit/Application.cs && git commit -m "feat(topo-tools): the style makes itself on first run, so the setup button goes"
```

---

### Task 4: Auto Dimensions' three styles, created on first run, rings wired through

**Files:**
- Modify: `src/RVTuk.Revit/AutoDimensions/DimensionLineStyle.cs`
- Modify: `src/RVTuk.Revit/AutoDimensions/LevelScopeFinder.cs`
- Modify: `src/RVTuk.Revit/AutoDimensions/DimensionRunner.cs`
- Modify: `src/RVTuk.Revit/AutoDimensions/ExternalEvents/LevelDiscoveryEventHandler.cs`
- Modify: `src/RVTuk.Core/AutoDimensions/LevelScope.cs`

**Interfaces:**
- Consumes: `DimensionRing` and `CandidateMatchResult` from Task 1; `LineStyleCreator` from Task 3.
- Produces:
  - `DimensionLineStyle.OuterLineStyleName`, `.InnerLineStyleName`, `.RefLineStyleName`, `.AllStyleNames`
  - `DimensionLineStyle.TryGetRing(CurveElement, out DimensionRing) → bool`
  - `DimensionLineStyle.IsRefLine(CurveElement) → bool`
  - `sealed record RingLine(DetailLine Line, DimensionRing Ring)` in `RVTuk.Revit.AutoDimensions`
  - `DimensionRunner.CollectReferenceLines(Document, View) → IReadOnlyList<RingLine>`

- [ ] **Step 1: Replace `DimensionLineStyle`**

Replace the whole of `src/RVTuk.Revit/AutoDimensions/DimensionLineStyle.cs`:

```csharp
using Autodesk.Revit.DB;
using RVTuk.Core.AutoDimensions;
using RVTuk.Revit.Shared;

namespace RVTuk.Revit.AutoDimensions
{
    /// <summary>
    /// The dedicated line subcategories Auto Dimensions drives off. Two of them are dimension
    /// strings — an outer ring and an inner one, which differ only in who owns a contested
    /// opening (see CandidateMatcher) — and the third points at a reference the automatic pass
    /// structurally cannot find (see WallEndResolver). All three auto-create on first use.
    /// </summary>
    public static class DimensionLineStyle
    {
        public const string OuterLineStyleName = "_DP-Dim Outer";
        public const string InnerLineStyleName = "_DP-Dim Inner";
        public const string RefLineStyleName = "_DP-Dim Ref";

        public static readonly string[] AllStyleNames =
        {
            OuterLineStyleName, InnerLineStyleName, RefLineStyleName,
        };

        /// <summary>Must be called inside an open transaction.</summary>
        public static void EnsureExists(Document doc)
        {
            foreach (var name in AllStyleNames) LineStyleCreator.EnsureExists(doc, name);
        }

        /// <summary>
        /// Whether this is a dimension string, and which ring it belongs to. False for a ref
        /// line, which is not a string and never receives a dimension of its own.
        /// </summary>
        public static bool TryGetRing(CurveElement curveElement, out DimensionRing ring)
        {
            ring = DimensionRing.Outer;

            var name = StyleName(curveElement);
            if (name == OuterLineStyleName) return true;
            if (name == InnerLineStyleName)
            {
                ring = DimensionRing.Inner;
                return true;
            }
            return false;
        }

        public static bool IsRefLine(CurveElement curveElement) =>
            StyleName(curveElement) == RefLineStyleName;

        private static string? StyleName(CurveElement curveElement) =>
            curveElement.LineStyle is GraphicsStyle style
                ? style.GraphicsStyleCategory?.Name
                : null;
    }
}
```

- [ ] **Step 2: Accept either ring as a reference view**

In `src/RVTuk.Revit/AutoDimensions/LevelScopeFinder.cs`, change the filter line

```csharp
                    .Where(DimensionLineStyle.IsDimensionsLine)
```

to

```csharp
                    .Where(l => DimensionLineStyle.TryGetRing(l, out _))
```

and update the class doc comment's first sentence to:

```csharp
    /// Discovers, per level, which of its plan views can act as the reference view (owns at least
    /// one _DP-Dim Outer or _DP-Dim Inner detail line) and which views can receive the fanned-out
    /// dimensions. A view holding only ref lines is not a reference view: ref lines with no
    /// string to join produce nothing.
```

- [ ] **Step 3: Update the `LevelScope` doc comment**

In `src/RVTuk.Core/AutoDimensions/LevelScope.cs`, change

```csharp
    /// (if any) owns the Dimensions_Line reference lines the others copy their dimensions from.
```

to

```csharp
    /// (if any) owns the _DP-Dim reference lines the others copy their dimensions from.
```

- [ ] **Step 4: Carry the ring through the runner**

In `src/RVTuk.Revit/AutoDimensions/DimensionRunner.cs`:

Add above the `DimensionRunner` class (after the `DimensionRunTally` class):

```csharp
    /// <summary>One reference line, with the ring its line style puts it in.</summary>
    public sealed record RingLine(DetailLine Line, DimensionRing Ring);
```

Replace `CollectReferenceLines` with:

```csharp
        /// <summary>
        /// Reference lines owned by a view, each tagged with its ring. Document-wide collector
        /// filtered by OwnerViewId, not a view-scoped collector: a view-scoped collector only
        /// returns elements currently visible, and lines hidden by the view template must still
        /// produce dimensions.
        /// </summary>
        public static IReadOnlyList<RingLine> CollectReferenceLines(Document doc, View referenceView)
        {
            var result = new List<RingLine>();

            foreach (var line in new FilteredElementCollector(doc)
                         .OfClass(typeof(CurveElement))
                         .Cast<CurveElement>()
                         .OfType<DetailLine>()
                         .Where(l => l.OwnerViewId == referenceView.Id))
            {
                if (DimensionLineStyle.TryGetRing(line, out var ring))
                    result.Add(new RingLine(line, ring));
            }

            return result;
        }
```

In `RunPair`, change the parameter type from `IReadOnlyList<DetailLine> referenceLines` to `IReadOnlyList<RingLine> referenceLines`, then change the geometry line to

```csharp
            var geometry = referenceLines.Select(l => l.Line.GeometryCurve as Line).ToList();
```

the `ReferenceLine` construction to

```csharp
                straightIndices.Add(i);
                lines.Add(new ReferenceLine(
                    ToXyPoint(geometry[i]!.GetEndPoint(0)),
                    ToXyPoint(geometry[i]!.GetEndPoint(1)),
                    referenceLines[i].Ring));
```

and the `RunLine` call to

```csharp
                    RunLine(doc, referenceLines[i].Line, geometry[i], targetView, candidates,
                        byLine[i] ?? Array.Empty<int>(), dimensionType, tally);
```

Add `using RVTuk.Core.AutoDimensions;` if not already present (it is).

- [ ] **Step 5: Create the styles on discovery**

In `src/RVTuk.Revit/AutoDimensions/ExternalEvents/LevelDiscoveryEventHandler.cs`, add `using RVTuk.Revit.Shared;` and insert the ensure call before the `Result = ...` assignment so the block reads:

```csharp
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null) { Result = Empty; return; }

                // First run in this project creates the styles. Without this you could not draw
                // a reference line before pressing Create Dimensions once, which necessarily did
                // nothing — there was no style to have drawn on.
                LineStyleCreator.TryEnsureInOwnTransaction(doc, DimensionLineStyle.AllStyleNames);

                Result = new AutoDimensionsScope(
```

- [ ] **Step 6: Build both configs**

Run: `dotnet build RVTuk.sln -c Release2024`
Expected: Build succeeded, 0 errors.

Run: `dotnet build RVTuk.sln -c Release2025`
Expected: Build succeeded, 0 errors.

Run: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj`
Expected: PASS, all tests.

- [ ] **Step 7: Commit**

```bash
git add src/RVTuk.Revit/AutoDimensions src/RVTuk.Core/AutoDimensions/LevelScope.cs && git commit -m "feat(auto-dimensions): three _DP- styles, made on first run, and the ring reaches the matcher"
```

---

### Task 5: Ref lines resolve a wall's end face

**Files:**
- Create: `src/RVTuk.Revit/AutoDimensions/WallEndResolver.cs`
- Modify: `src/RVTuk.Revit/AutoDimensions/DimensionCandidateCollector.cs`
- Modify: `src/RVTuk.Revit/AutoDimensions/DimensionRunner.cs`
- Modify: `src/RVTuk.Revit/AutoDimensions/ExternalEvents/CreateDimensionsEventHandler.cs`

**Interfaces:**
- Consumes: `RefLineMatcher`, `RefLineMatch`, `RefLineHit` (Task 2); `RingLine`, `DimensionLineStyle.IsRefLine` (Task 4); `CandidateMatchResult` (Task 1).
- Produces:
  - `DimensionCandidateSet.OccluderItems` — `IReadOnlyList<DimensionCandidate>`, index-aligned with `Occluders`
  - `DimensionCandidateCollector.TryResolveReferences(DimensionCandidateSet, int, XyPoint, XyPoint) → List<Reference>?` (replaces `TryAppendReferences`)
  - `WallEndResolver.TryResolve(DimensionCandidateSet, XyPoint targetEnd, XyPoint stringStart, XyPoint stringEnd) → Reference?`
  - `DimensionRunner.CollectRefLines(Document, View) → IReadOnlyList<DetailLine>`
  - `DimensionRunner.RunPair(Document, IReadOnlyList<RingLine>, IReadOnlyList<DetailLine>, View, DimensionCategories, DimensionType?, DimensionRunTally)`
  - `DimensionRunTally.RefLinesUnattached`, `.RefLinesUnresolved`

- [ ] **Step 1: Keep every collected wall as an element**

In `src/RVTuk.Revit/AutoDimensions/DimensionCandidateCollector.cs`:

Add to `DimensionCandidateSet`, after the `Occluders` property:

```csharp
        /// <summary>
        /// Every collected wall as an element, index-aligned with <see cref="Occluders"/>. A ref
        /// line must resolve a wall end whether or not Walls is ticked — the same reasoning that
        /// already makes every wall an occluder.
        /// </summary>
        public IReadOnlyList<DimensionCandidate> OccluderItems { get; set; } =
            new List<DimensionCandidate>();
```

Add to `Accumulator`, after the `Occluders` field:

```csharp
            public readonly List<DimensionCandidate> OccluderItems = new List<DimensionCandidate>();
```

In `Collect`, add to the returned object initialiser, after `Occluders = accumulated.Occluders,`:

```csharp
                OccluderItems = accumulated.OccluderItems,
```

In `AddWalls`, replace the block from `// Every wall stands in the way...` to the end of the loop body with:

```csharp
                // One instance in both lists: every wall stands in the way of the openings behind
                // it, dimensioned or not, and a ref line may point at any of their ends.
                var item = new DimensionCandidate
                {
                    Kind = DimensionCandidateKind.Wall,
                    Wall = wall,
                    Link = link,
                };

                accumulated.Occluders.Add(segment);
                accumulated.OccluderItems.Add(item);
                if (!dimensionThem) continue;

                accumulated.Add(item, segment, CandidateMatch.Crossing);
```

- [ ] **Step 2: Turn `TryAppendReferences` into `TryResolveReferences`**

In the same file, change the signature and doc comment of `TryAppendReferences` to:

```csharp
        /// <summary>
        /// The candidate's two references (a wall's side faces, an opening's Left/Right), or null
        /// when either side can't be resolved — so one unreadable element doesn't cost the whole
        /// line its dimension. Returned rather than appended, because the runner interleaves them
        /// with ref line marks by position along the line before building the array.
        /// </summary>
        public static List<Reference>? TryResolveReferences(
            DimensionCandidateSet candidates,
            int index,
            XyPoint lineStart,
            XyPoint lineEnd)
```

Remove the `ReferenceArray target` parameter. Inside the body, replace every `return false;` with `return null;`, and replace the final three lines

```csharp
                target.Append(first);
                target.Append(second);
                return true;
```

with

```csharp
                return new List<Reference> { first, second };
```

and the trailing

```csharp
            catch
            {
                return false;
            }
```

with

```csharp
            catch
            {
                return null;
            }
```

- [ ] **Step 3: Create the wall-end resolver**

Create `src/RVTuk.Revit/AutoDimensions/WallEndResolver.cs`:

```csharp
using System;
using Autodesk.Revit.DB;
using RVTuk.Core.AutoDimensions;
using RVTuk.Core.Shared.Geometry;

namespace RVTuk.Revit.AutoDimensions
{
    /// <summary>
    /// Turns a _DP-Dim Ref line's target end into a dimension reference on a wall's END face.
    ///
    /// The end face, not a side face, is the whole point. ReferenceAlignment requires a
    /// reference's normal to run along the dimension direction: a wall parallel to a string has
    /// side faces facing ACROSS it — which is exactly why parallel walls are invisible to the
    /// automatic pass — and an end face facing ALONG it, which a string can measure. So this
    /// reaches the one reference on a parallel wall a string is entitled to, rather than
    /// bolting on an exception.
    ///
    /// Resolved view-independently so one answer serves every fanned-out view.
    /// </summary>
    public static class WallEndResolver
    {
        /// <summary>
        /// Feet. Generous on purpose: the user may snap to the wall's face corner rather than its
        /// location-curve end, and the two differ by half the wall's thickness — 1 ft covers
        /// walls to 600 mm.
        /// </summary>
        public const double WallEndTolerance = 1.0;

        /// <summary>Degrees. The same parallel tolerance the crossing and alignment rules use.</summary>
        private const double NormalToleranceDegrees = WallCrossingFinder.ParallelToleranceDegrees;

        /// <summary>Feet. A face normal with more vertical than this is a top or bottom.</summary>
        private const double VerticalTolerance = 0.001;

        /// <summary>
        /// Null when there is no wall end within tolerance of the target point, when the wall
        /// runs too far off parallel to the string for its end face to be measurable, or when the
        /// end face itself cannot be resolved — a wall joined into another at that end has its
        /// end face clipped or consumed by Revit, and there may be no planar face there at all.
        /// </summary>
        public static Reference? TryResolve(
            DimensionCandidateSet candidates,
            XyPoint targetEnd,
            XyPoint stringStart,
            XyPoint stringEnd)
        {
            var index = NearestWallEnd(candidates, targetEnd, out var endPoint);
            if (index < 0) return null;

            // An end face faces along its wall, so this is the AlongSegment case — the same test
            // an opening's jambs pass, and for the same geometric reason.
            if (!ReferenceAlignment.CanDimension(
                    stringStart, stringEnd, candidates.Occluders[index], ReferenceNormal.AlongSegment))
                return null;

            var candidate = candidates.OccluderItems[index];
            if (candidate.Wall == null) return null;

            return TryEndFace(candidate.Wall, endPoint, candidate.Link);
        }

        /// <summary>
        /// The nearest end of any collected wall to the target point, within
        /// <see cref="WallEndTolerance"/>. Returns its index into Occluders/OccluderItems, and
        /// hands back the end point itself so the face search knows which end to look at.
        /// </summary>
        private static int NearestWallEnd(
            DimensionCandidateSet candidates, XyPoint target, out XyPoint endPoint)
        {
            endPoint = default;

            var best = -1;
            var bestDistance = WallEndTolerance;

            for (int i = 0; i < candidates.Occluders.Count; i++)
            {
                // OccluderItems is index-aligned with Occluders, but be defensive: a mismatch
                // would otherwise throw deep inside a run.
                if (i >= candidates.OccluderItems.Count) break;

                var segment = candidates.Occluders[i];
                foreach (var end in new[] { segment.Start, segment.End })
                {
                    var dx = end.X - target.X;
                    var dy = end.Y - target.Y;
                    var distance = Math.Sqrt(dx * dx + dy * dy);
                    if (distance >= bestDistance) continue;

                    best = i;
                    bestDistance = distance;
                    endPoint = end;
                }
            }

            return best;
        }

        /// <summary>
        /// The wall's end face nearest the target point: a vertical planar face whose normal runs
        /// along the wall (its ends) rather than across it (its sides).
        ///
        /// ComputeReferences is required or the face's Reference comes back null, and the options
        /// carry no View on purpose — a view-specific resolution would have to be redone for every
        /// fanned-out view, and could differ between them.
        /// </summary>
        private static Reference? TryEndFace(Wall wall, XyPoint target, RevitLinkInstance? link)
        {
            try
            {
                if ((wall.Location as LocationCurve)?.Curve is not Line centerline) return null;

                var localTarget = ToLocal(target, link);
                var direction = centerline.Direction;
                var wallAngle = Math.Atan2(direction.Y, direction.X);

                var options = new Options
                {
                    ComputeReferences = true,
                    IncludeNonVisibleObjects = false,
                    DetailLevel = ViewDetailLevel.Medium,
                };

                Reference? best = null;
                var bestDistance = double.MaxValue;

                foreach (var geometryObject in wall.get_Geometry(options))
                {
                    if (geometryObject is not Solid solid) continue;

                    foreach (Face face in solid.Faces)
                    {
                        if (face is not PlanarFace planar) continue;
                        if (planar.Reference == null) continue;

                        // Vertical check FIRST: a top or bottom face has no horizontal normal, and
                        // Atan2(0, 0) is 0, which would read as perfectly parallel to the wall.
                        var normal = planar.FaceNormal;
                        if (Math.Abs(normal.Z) > VerticalTolerance) continue;

                        var normalAngle = Math.Atan2(normal.Y, normal.X);
                        if (Angle2D.FromParallelDegrees(wallAngle, normalAngle) > NormalToleranceDegrees)
                            continue;

                        var origin = planar.Origin;
                        var dx = origin.X - localTarget.X;
                        var dy = origin.Y - localTarget.Y;
                        var distance = Math.Sqrt(dx * dx + dy * dy);
                        if (distance >= bestDistance) continue;

                        best = planar.Reference;
                        bestDistance = distance;
                    }
                }

                if (best == null) return null;

                // A reference resolved inside a linked document is meaningless to the host view
                // until it is re-expressed through the link instance that places it.
                return link == null ? best : best.CreateLinkReference(link);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// The target point in the wall's own document. Occluder segments are in host
        /// coordinates, but a linked wall's geometry is in the link's.
        /// </summary>
        private static XyPoint ToLocal(XyPoint host, RevitLinkInstance? link)
        {
            if (link == null) return host;

            try
            {
                var point = link.GetTotalTransform().Inverse.OfPoint(new XYZ(host.X, host.Y, 0));
                return new XyPoint(point.X, point.Y);
            }
            catch
            {
                return host;
            }
        }
    }
}
```

- [ ] **Step 4: Add the two tally counters**

In `src/RVTuk.Revit/AutoDimensions/DimensionRunner.cs`, add to `DimensionRunTally` after `CoincidentMerged`:

```csharp
        /// <summary>Ref lines that met no string, or whose both ends sat on one.</summary>
        public int RefLinesUnattached;

        /// <summary>
        /// Ref lines that met a string but found no reference: no wall end within tolerance, a
        /// wall too far off parallel to the string, or no planar end face — which is what a wall
        /// joined into another at that end leaves behind.
        /// </summary>
        public int RefLinesUnresolved;
```

and extend `HasExclusions`:

```csharp
        public bool HasExclusions =>
            ExcludedNotCut > 0 || ExcludedNoReferences > 0 || CoincidentMerged > 0
            || RefLinesUnattached > 0 || RefLinesUnresolved > 0;
```

- [ ] **Step 5: Collect ref lines and resolve them in `RunPair`**

In `src/RVTuk.Revit/AutoDimensions/DimensionRunner.cs`, add after `CollectReferenceLines`:

```csharp
        /// <summary>
        /// Feet (~3 mm). How near a ref line must come to a string to count as touching it —
        /// Revit's snaps make it exact in practice, and this covers a line drawn by eye.
        /// </summary>
        public const double RefLineTouchTolerance = 0.01;

        /// <summary>
        /// The reference view's _DP-Dim Ref lines. Same collection rule as the strings: owned by
        /// the view, found document-wide so template-hidden lines still count.
        /// </summary>
        public static IReadOnlyList<DetailLine> CollectRefLines(Document doc, View referenceView)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(CurveElement))
                .Cast<CurveElement>()
                .OfType<DetailLine>()
                .Where(l => l.OwnerViewId == referenceView.Id && DimensionLineStyle.IsRefLine(l))
                .ToList();
        }
```

Replace the whole of `RunPair` with:

```csharp
        /// <summary>
        /// Runs every reference line against one target view. The lines may be owned by a
        /// different view of the same level — that is exactly the fan-out the scope pane performs.
        /// </summary>
        public static void RunPair(
            Document doc,
            IReadOnlyList<RingLine> referenceLines,
            IReadOnlyList<DetailLine> refLines,
            View targetView,
            DimensionCategories categories,
            DimensionType? dimensionType,
            DimensionRunTally tally)
        {
            var candidates = DimensionCandidateCollector.Collect(doc, targetView, categories);
            tally.ExcludedNotCut += candidates.ExcludedNotCut;

            // Matching happens for all the level's lines at once, not line by line: an opening
            // belongs to the line that owns it, which can only be known by comparing them.
            var geometry = referenceLines.Select(l => l.Line.GeometryCurve as Line).ToList();

            var straightIndices = new List<int>();
            var lines = new List<ReferenceLine>();
            for (int i = 0; i < geometry.Count; i++)
            {
                if (geometry[i] == null) continue;

                straightIndices.Add(i);
                lines.Add(new ReferenceLine(
                    ToXyPoint(geometry[i]!.GetEndPoint(0)),
                    ToXyPoint(geometry[i]!.GetEndPoint(1)),
                    referenceLines[i].Ring));
            }

            var matches = CandidateMatcher.FindMatchesForLines(
                lines, candidates.Segments, candidates.MatchModes, candidates.Occluders);

            var byLine = new IReadOnlyList<CandidateMatchResult>?[referenceLines.Count];
            for (int k = 0; k < straightIndices.Count; k++) byLine[straightIndices[k]] = matches[k];

            var refByLine = ResolveRefLines(refLines, lines, straightIndices, candidates,
                referenceLines.Count, tally);

            // Every line goes through the loop, including any whose geometry isn't a Line: the
            // stale-dimension delete must stay unconditional.
            for (int i = 0; i < referenceLines.Count; i++)
            {
                try
                {
                    RunLine(doc, referenceLines[i].Line, geometry[i], targetView, candidates,
                        byLine[i] ?? Array.Empty<CandidateMatchResult>(),
                        refByLine[i], dimensionType, tally);
                }
                catch
                {
                    // One unreadable line never aborts the run; the transaction still guarantees
                    // all-or-nothing for a hard Revit-level failure.
                    tally.Skipped++;
                }
            }
        }

        /// <summary>
        /// Each ref line's marks, bucketed by the reference line they join. A ref line joins
        /// EVERY string it meets, so one line can contribute a mark to several — how far it is
        /// drawn is the control.
        /// </summary>
        private static List<(double T, Reference Reference)>?[] ResolveRefLines(
            IReadOnlyList<DetailLine> refLines,
            IReadOnlyList<ReferenceLine> lines,
            IReadOnlyList<int> straightIndices,
            DimensionCandidateSet candidates,
            int referenceLineCount,
            DimensionRunTally tally)
        {
            var byLine = new List<(double T, Reference Reference)>?[referenceLineCount];

            foreach (var refLine in refLines)
            {
                if (refLine.GeometryCurve is not Line refGeometry)
                {
                    tally.RefLinesUnattached++;
                    continue;
                }

                var match = RefLineMatcher.Match(
                    ToXyPoint(refGeometry.GetEndPoint(0)),
                    ToXyPoint(refGeometry.GetEndPoint(1)),
                    lines,
                    RefLineTouchTolerance);

                if (match == null)
                {
                    tally.RefLinesUnattached++;
                    continue;
                }

                foreach (var hit in match.Hits)
                {
                    var reference = WallEndResolver.TryResolve(
                        candidates,
                        match.TargetEnd,
                        lines[hit.LineIndex].Start,
                        lines[hit.LineIndex].End);

                    if (reference == null)
                    {
                        tally.RefLinesUnresolved++;
                        continue;
                    }

                    var target = straightIndices[hit.LineIndex];
                    (byLine[target] ??= new List<(double, Reference)>()).Add((hit.T, reference));
                }
            }

            return byLine;
        }
```

- [ ] **Step 6: Interleave the marks in `RunLine`**

In the same file, replace the whole of `RunLine` with:

```csharp
        private static void RunLine(
            Document doc,
            DetailLine line,
            Line? geometryLine,
            View targetView,
            DimensionCandidateSet candidates,
            IReadOnlyList<CandidateMatchResult> matches,
            IReadOnlyList<(double T, Reference Reference)>? refMarks,
            DimensionType? dimensionType,
            DimensionRunTally tally)
        {
            // Always first, and independent of whether this line still has matches: a line whose
            // walls were deleted or moved away must still lose its stale dimension.
            var tracked = AutoDimensionTracker.TryGetTrackedDimension(line, targetView.Id);
            if (tracked != null) doc.Delete(tracked.Id);

            if (geometryLine == null)
            {
                tally.Skipped++;
                return;
            }

            if (matches.Count == 0 && (refMarks == null || refMarks.Count == 0))
            {
                tally.Skipped++;
                return;
            }

            var lineStart = ToXyPoint(geometryLine.GetEndPoint(0));
            var lineEnd = ToXyPoint(geometryLine.GetEndPoint(1));

            // Candidate marks and ref line marks are ordered together by position along the line,
            // so a wall end lands between the two walls it sits between rather than at the end.
            var entries = new List<(double T, List<Reference> References)>();

            foreach (var match in matches)
            {
                var resolved = DimensionCandidateCollector.TryResolveReferences(
                    candidates, match.Index, lineStart, lineEnd);

                if (resolved == null) tally.ExcludedNoReferences++;
                else entries.Add((match.T, resolved));
            }

            if (refMarks != null)
            {
                foreach (var mark in refMarks)
                    entries.Add((mark.T, new List<Reference> { mark.Reference }));
            }

            var referenceArray = new ReferenceArray();
            foreach (var entry in entries.OrderBy(e => e.T))
            {
                foreach (var reference in entry.References) referenceArray.Append(reference);
            }

            // Revit needs two. A lone ref line mark is one, which the old wall-only path could
            // never produce.
            if (referenceArray.Size < 2)
            {
                tally.Skipped++;
                return;
            }

            var dimensionLine = ToTargetViewPlane(geometryLine, targetView);
            var dimension = CreateDimension(
                doc, targetView, dimensionLine, referenceArray, dimensionType);
            dimension = RemoveCoincidentReferences(
                doc, targetView, dimensionLine, dimension, dimensionType, tally);
            if (dimension == null)
            {
                tally.Skipped++;
                return;
            }

            AutoDimensionTracker.SetTrackedDimension(line, targetView.Id, dimension.Id);
            tally.Created++;
        }
```

- [ ] **Step 7: Pass ref lines in, and report the two new causes**

In `src/RVTuk.Revit/AutoDimensions/ExternalEvents/CreateDimensionsEventHandler.cs`:

Replace

```csharp
                        var referenceLines = DimensionRunner.CollectReferenceLines(doc, referenceView);
```

with

```csharp
                        var referenceLines = DimensionRunner.CollectReferenceLines(doc, referenceView);
                        var refLines = DimensionRunner.CollectRefLines(doc, referenceView);
```

Replace the `RunPair` call with

```csharp
                            DimensionRunner.RunPair(
                                doc, referenceLines, refLines, targetView, categories,
                                dimensionType, tally);
```

Add the two new counters to the totals accumulation, after `totals.CoincidentMerged += tally.CoincidentMerged;`:

```csharp
                            totals.RefLinesUnattached += tally.RefLinesUnattached;
                            totals.RefLinesUnresolved += tally.RefLinesUnresolved;
```

And in `AppendExclusions`, replace the final `CoincidentMerged` line so the block ends:

```csharp
            if (totals.CoincidentMerged > 0)
                report.AppendLine($"  • {totals.CoincidentMerged} reference(s) merged for sharing a position along the line (joined walls).");
            if (totals.RefLinesUnattached > 0)
                report.AppendLine($"  • {totals.RefLinesUnattached} reference line(s) touching no dimension string — draw one from a wall end to the string it should mark.");
            if (totals.RefLinesUnresolved > 0)
                report.Append($"  • {totals.RefLinesUnresolved} reference line(s) with no wall end to mark — nothing within reach of the far end, the wall too far off parallel to the string, or its end face consumed by a join with another wall.");
```

(note the last line uses `Append`, not `AppendLine`, matching how the block previously ended).

- [ ] **Step 8: Build both configs**

Run: `dotnet build RVTuk.sln -c Release2024`
Expected: Build succeeded, 0 errors.

Run: `dotnet build RVTuk.sln -c Release2025`
Expected: Build succeeded, 0 errors.

Run: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj`
Expected: PASS, all tests.

- [ ] **Step 9: Commit**

```bash
git add src/RVTuk.Revit/AutoDimensions && git commit -m "feat(auto-dimensions): a ref line puts a mark on the end of a parallel wall"
```

---

### Task 6: Docs, and the in-Revit pass

**Files:**
- Modify: `docs/tools/auto-dimensions/README.md`
- Modify: `docs/tools/topo-tools/README.md`
- Modify: `docs/tools/auto-dimensions/backlog.md`
- Modify: `docs/tools/topo-tools/backlog.md`
- Modify: `CLAUDE.md`
- Modify: `docs/toolkit/specs/2026-08-02-line-style-naming-and-dimension-rings-design.md`

- [ ] **Step 1: Update the Auto Dimensions README**

In `docs/tools/auto-dimensions/README.md`, replace the opening `**What it is:**` sentence's `the dedicated "Dimensions_Line" style` with `one of the two dedicated string styles, "_DP-Dim Outer" or "_DP-Dim Inner"`, and add these two paragraphs after the "**An opening is dimensioned once...**" paragraph:

```markdown
**Outer strings outrank inner ones.** Ownership of an opening is settled by ring first,
then distance, then line order. An outer line that qualifies beats every inner line
outright, however much closer the inner one stands — a facade window belongs on the facade
string, not on the interior string that happens to sit nearer it. Only what no outer string
can see falls through to the inner strings, settled among themselves the same way. Walls
are untouched by this: a wall crossed by three strings is measured by all three, inner and
outer alike.

**A `_DP-Dim Ref` line points at what the pass cannot see.** Draw it from a wall's end to a
string and that wall end gets a mark on that string — the case being a wall running parallel
to the string, which the automatic pass structurally cannot measure. The line is a pointer,
never itself a reference: detail lines are view-specific, so dimensioning one would work in
the reference view and produce nothing in the fanned-out ones, and the mark would sit where
the line is rather than where the wall is. The tool resolves the wall's **end face** instead,
which is the one face on a parallel wall a string is geometrically entitled to measure — its
side faces face across the string, its end face along it. A ref line joins **every** string it
touches, so how far you draw it is the control. Known limit: a wall joined into another at
that end has its end face clipped away by Revit and cannot be marked; the run summary says so.
```

Also update the **Status** line to note the styles are created on first run:

```markdown
**Status:** registered on the RVTuk ribbon panel, gated by `RegisterAutoDimensions` in
`src/RVTuk.Revit/Application.cs` (on). All three line styles are created on the pane's first
refresh — there is no setup step. In-Revit verification pass still outstanding.
```

- [ ] **Step 2: Update the Topo Tools README**

In `docs/tools/topo-tools/README.md`, replace every occurrence of `Topo_Line` with `_DP-Topo Line`, and replace any sentence describing the "Set up this project" button with:

```markdown
The `_DP-Topo Line` style is created on the pane's first refresh — there is no setup step
and no setup button. You cannot draw a line on a style that does not exist, so waiting for
an explicit action was the wrong shape.
```

- [ ] **Step 3: Update both backlogs**

Append to the Done section of `docs/tools/topo-tools/backlog.md`:

```markdown
- **Setup button removed; the style creates itself.** `_DP-Topo Line` is made on the pane's
  first refresh. The helper only opens a transaction when the style is actually missing, so
  later refreshes stay read-only and don't mark the document modified. (2026-08-03)
```

Append to the Done section of `docs/tools/auto-dimensions/backlog.md`:

```markdown
- **Outer/inner rings.** `_DP-Dim Outer` and `_DP-Dim Inner` replace `Dimensions_Line`.
  Ownership of an opening is settled by (ring, distance, line order), so a facade window
  lands on the facade string rather than on a nearer interior one. (2026-08-03)
- **`_DP-Dim Ref` lines.** Draw from a wall end to a string to mark that wall end on it.
  Resolves the wall's end face, not the detail line, so it survives the fan-out and follows
  the wall. Wall ends only for now; joined ends can't be marked and are reported. (2026-08-03)
- **Styles create themselves on the pane's first refresh.** Previously the style was created
  only by a Create Dimensions run, so the first press necessarily did nothing — there had
  been no style to draw a reference line on. (2026-08-03)
```

- [ ] **Step 4: Update `CLAUDE.md`**

In the **Auto Dimensions** feature bullet, replace `draw a detail line on the dedicated "Dimensions_Line" style as a positional reference` with:

```markdown
draw a detail line on `_DP-Dim Outer` or `_DP-Dim Inner` as a positional reference (outer strings outrank inner ones when both can see the same opening), plus `_DP-Dim Ref` lines drawn from a wall end to a string to mark ends the automatic pass cannot see
```

In the **Topo Tools** feature bullet, replace `the dedicated "Topo_Line" style` with `the dedicated "_DP-Topo Line" style`, and add to the end of that bullet:

```markdown
All four line styles (three for Auto Dimensions, one for Topo Tools) are created by `RVTuk.Revit/Shared/LineStyleCreator` on each pane's first refresh — there is no setup button.
```

- [ ] **Step 5: Mark the spec implemented**

In `docs/toolkit/specs/2026-08-02-line-style-naming-and-dimension-rings-design.md`, change the status line to:

```markdown
**Status:** implemented — see [the plan](../plans/2026-08-03-line-styles-rings-and-ref-lines.md). In-Revit verification pass outstanding.
```

- [ ] **Step 6: Commit**

```bash
git add docs CLAUDE.md && git commit -m "docs: the _DP- styles, the rings, and what a ref line can and cannot mark"
```

- [ ] **Step 7: In-Revit verification (manual — record results in the backlogs)**

Deploy with `.\Deploy.ps1 2024` from an elevated shell, restart Revit, and check:

1. **First run.** Open a project with none of the four styles. Show the Topo Tools pane, then the Auto Dimensions pane. Manage → Object Styles → Lines should list `_DP-Dim Inner`, `_DP-Dim Outer`, `_DP-Dim Ref`, `_DP-Topo Line`. Topo Tools should show no setup banner and no setup button.
2. **No spurious modification.** Save, then refresh each pane again. The document must not become modified (no asterisk / no save prompt on close).
3. **Read-only model.** Open a model as read-only (or a linked document's own view) and refresh each pane. It must report normally, not throw.
4. **Rings.** In a plan with a facade window, draw an outer string outside the facade and an inner string inside it, closer to the window. Run: the window must be measured by the outer string only. Delete the outer string, re-run: the inner string takes it.
5. **Ref line, reference view.** Draw a `_DP-Dim Ref` line from a free-ending wall parallel to the outer string, out to that string. Run: the wall end gets a mark, positioned along the string where the ref line meets it.
6. **Ref line, fan-out.** Tick a second view of the same level and run. The mark must appear there too — this is the check that would fail if the ref line itself were being referenced.
7. **Ref line follows the wall.** Move the wall along its own length, re-run, and confirm the mark moves with it.
8. **Ref line through both strings.** Extend the ref line out through the inner string as well. Both strings must carry the mark.
9. **Joined wall end.** Draw a ref line at a wall end that is joined into another wall. The run must complete and the summary must report it under "Not marked:", not throw.

---

## Self-Review

**Spec coverage:** §1 names → Tasks 3, 4. §2 first-run creation → Tasks 3, 4. §3 rings → Task 1 (Core) and Task 4 (wiring). §4 ref lines → Task 2 (Core) and Task 5 (Revit). §5 callers → Tasks 3, 4, 5. Error handling → `LineStyleCreator` fallbacks (Task 3), the two tally counters (Task 5). Testing → Tasks 1, 2, and Task 6 Step 7. Docs → Task 6.

**Type consistency:** `DimensionRing`, `CandidateMatchResult`, `ReferenceLine`, `RefLineHit`, `RefLineMatch`, `RingLine`, `TryResolveReferences`, `OccluderItems`, `RefLinesUnattached` / `RefLinesUnresolved` are each defined in one task and used with the same names and shapes in every later task.

**Known deviation from the spec:** the spec says `TopoRunner.Apply` keeps ensuring the style inside its own transaction. Task 3 Step 6 drops it instead, because `Apply` builds its plan *before* opening the transaction — creating the style there could not give that run any lines, so the call would be dead code. Discovery guarantees the style; `Apply` on a project without one reports no lines, which is correct. `CreateDimensionsEventHandler` keeps its existing `EnsureExists` call unchanged.

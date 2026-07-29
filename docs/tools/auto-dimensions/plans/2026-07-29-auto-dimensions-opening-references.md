# Auto Dimensions — Occlusion and Dimension Type Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the user-set "openings within X mm of the line" reach with a geometric rule — an opening is dimensioned by a line unless another parallel wall stands between them — and let the run apply a dimension type chosen in the pane, so Revit's *Show Opening Height* becomes reachable.

**Architecture:** `RVTuk.Core.AutoDimensions.CandidateMatcher` gains an `occluders` list of 2D wall segments and loses its `alongsideReach` parameter; `RVTuk.Revit`'s collector fills that list from every wall the view cuts, whether or not walls are being dimensioned. Separately, the scope pane's third control changes from a millimetre text box to a dimension-type dropdown, whose id is persisted and applied to every `NewDimension` call.

**Tech Stack:** C# (net48 for Release2024, net8.0-windows for Release2025), WPF/MVVM, Revit API 2024/2025, xunit.

**Spec:** [../specs/2026-07-29-auto-dimensions-opening-references-design.md](../specs/2026-07-29-auto-dimensions-opening-references-design.md)

## Global Constraints

- `RVTuk.Core` must not reference any Revit API or WPF type. `RVTuk.UI` must not reference any Revit type — Revit work reaches it as `Func<>`/`Action` delegates from `RVTuk.Revit`.
- Namespace = root namespace + folder path, exactly.
- Revit API calls run on Revit's main thread only, marshalled via `ExternalEvent` + `ManualResetEventSlim`. The pane's delegates block, so the view model calls them from the thread pool and marshals results back through the `Dispatcher`.
- Both solution configurations must build: `Release2024` (net48) and `Release2025` (net8.0-windows). `Release2023` does not build `RVTuk.Revit` and is unaffected by this work.
- `BlockingEpsilon` is `1e-6`, in Revit's internal feet.
- An opening contributes **exactly two references**, `FamilyInstanceReferenceType.Left` and `.Right`. No other reference type is ever consulted.
- Commit messages end with `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.

## File Structure

**Task 1 — Core rule**
- Modify: `src/RVTuk.Core/AutoDimensions/CandidateMatcher.cs` — occlusion replaces reach
- Modify: `tests/RVTuk.Core.Tests/AutoDimensions/CandidateMatcherTests.cs` — reach cases become occlusion cases

**Task 2 — Revit and UI wiring**
- Modify: `src/RVTuk.Core/AutoDimensions/AutoDimensionsScope.cs` — `DimensionTypeInfo`, `ScopeSelection.DimensionTypeId`, delete `ScopeDefaults`
- Create: `src/RVTuk.Revit/AutoDimensions/DimensionTypeFinder.cs`
- Modify: `src/RVTuk.Revit/AutoDimensions/DimensionCandidateCollector.cs` — `Occluders`, walls collected unconditionally
- Modify: `src/RVTuk.Revit/AutoDimensions/DimensionRunner.cs` — occluders in, reach out, dimension type applied
- Modify: `src/RVTuk.Revit/AutoDimensions/ScopeSelectionStore.cs` — schema V3
- Modify: `src/RVTuk.Revit/AutoDimensions/ExternalEvents/LevelDiscoveryEventHandler.cs` — report dimension types
- Modify: `src/RVTuk.Revit/AutoDimensions/ExternalEvents/CreateDimensionsEventHandler.cs` — resolve and pass the type
- Modify: `src/RVTuk.Revit/Application.cs:150-160` — delegate signature
- Modify: `src/RVTuk.UI/AutoDimensions/ViewModels/AutoDimensionsPaneViewModel.cs` — dropdown state
- Modify: `src/RVTuk.UI/AutoDimensions/Views/AutoDimensionsPaneView.xaml:55-66` — dropdown replaces the text box

**Task 3 — Docs**
- Modify: `docs/tools/auto-dimensions/README.md`, `docs/tools/auto-dimensions/backlog.md`, `CLAUDE.md`

**Note on ordering:** Task 1 changes a public Core signature that `RVTuk.Revit` calls, so `RVTuk.Revit` does not compile between Task 1 and Task 2. That is intended — Task 1's deliverable is the Core rule proven by `dotnet test`, which builds only `RVTuk.Core`. Task 2 restores the solution build. Do not reorder them.

---

### Task 1: Occlusion replaces reach in the matcher

**Files:**
- Modify: `src/RVTuk.Core/AutoDimensions/CandidateMatcher.cs`
- Test: `tests/RVTuk.Core.Tests/AutoDimensions/CandidateMatcherTests.cs`

**Interfaces:**
- Consumes: `WallCandidate(XyPoint Start, XyPoint End)`, `XyPoint(double X, double Y)`, `Angle2D.FromParallelDegrees(double, double)`, `WallCrossingFinder.ParallelToleranceDegrees` (5.0), `WallCrossingFinder.TryGetCrossingParameter(XyPoint, XyPoint, WallCandidate, out double)` — all unchanged.
- Produces:
  - `CandidateMatcher.FindMatchIndices(XyPoint lineStart, XyPoint lineEnd, IReadOnlyList<WallCandidate> segments, IReadOnlyList<CandidateMatch> matchModes, IReadOnlyList<WallCandidate> occluders) → IReadOnlyList<int>`
  - `CandidateMatcher.FindMatchIndicesForLines(IReadOnlyList<ReferenceLine> lines, IReadOnlyList<WallCandidate> segments, IReadOnlyList<CandidateMatch> matchModes, IReadOnlyList<WallCandidate> occluders) → IReadOnlyList<IReadOnlyList<int>>`
  - The `double alongsideReach` parameter is gone from both.

- [ ] **Step 1: Replace the test file's reach cases with occlusion cases**

Replace the whole of `tests/RVTuk.Core.Tests/AutoDimensions/CandidateMatcherTests.cs` with:

```csharp
using System.Collections.Generic;
using RVTuk.Core.AutoDimensions;
using Xunit;

namespace RVTuk.Core.Tests.AutoDimensions;

public class CandidateMatcherTests
{
    // Reference line: horizontal, from (0,0) to (10,0). Distances are in the same unit.
    private static readonly XyPoint LineStart = new(0, 0);
    private static readonly XyPoint LineEnd = new(10, 0);

    /// <summary>Nothing standing in the way — the default for cases not about occlusion.</summary>
    private static readonly IReadOnlyList<WallCandidate> NoOccluders = new WallCandidate[0];

    [Fact]
    public void CrossingCandidatesBehaveAsBefore()
    {
        var segments = new List<WallCandidate>
        {
            new(new XyPoint(8, -5), new XyPoint(8, 5)),
            new(new XyPoint(2, -5), new XyPoint(2, 5)),
        };
        var modes = new[] { CandidateMatch.Crossing, CandidateMatch.Crossing };

        var result = CandidateMatcher.FindMatchIndices(LineStart, LineEnd, segments, modes, NoOccluders);

        Assert.Equal(new[] { 1, 0 }, result);
    }

    [Fact]
    public void AnOpeningInAParallelWallIsMatched()
    {
        // A door 2 away from the line, in a wall running along it.
        var segments = new List<WallCandidate> { new(new XyPoint(4, 2), new XyPoint(5, 2)) };
        var modes = new[] { CandidateMatch.Alongside };

        var result = CandidateMatcher.FindMatchIndices(LineStart, LineEnd, segments, modes, NoOccluders);

        Assert.Equal(new[] { 0 }, result);
    }

    [Fact]
    public void AnOpeningIsMatchedHoweverFarFromTheLine()
    {
        // The case the old reach test rejected: with a clear line of sight, distance is not the
        // question. An exterior string sits far outside the facade it dimensions.
        var segments = new List<WallCandidate> { new(new XyPoint(4, 40), new XyPoint(5, 40)) };
        var modes = new[] { CandidateMatch.Alongside };

        var result = CandidateMatcher.FindMatchIndices(LineStart, LineEnd, segments, modes, NoOccluders);

        Assert.Equal(new[] { 0 }, result);
    }

    [Fact]
    public void OpeningsAreMatchedOnBothSidesOfTheLine()
    {
        var segments = new List<WallCandidate> { new(new XyPoint(4, -2), new XyPoint(5, -2)) };
        var modes = new[] { CandidateMatch.Alongside };

        Assert.Single(CandidateMatcher.FindMatchIndices(LineStart, LineEnd, segments, modes, NoOccluders));
    }

    [Fact]
    public void AnOpeningInAPerpendicularWallIsNotMatched()
    {
        // The old rule's only case, and the one whose jambs lie parallel to the line.
        var segments = new List<WallCandidate> { new(new XyPoint(5, 1.5), new XyPoint(5, 2.5)) };
        var modes = new[] { CandidateMatch.Alongside };

        Assert.Empty(CandidateMatcher.FindMatchIndices(LineStart, LineEnd, segments, modes, NoOccluders));
    }

    [Fact]
    public void AnOpeningPastTheEndOfTheLineIsNotMatched()
    {
        // Close to the line's infinite extension, but off the end of the drawn segment.
        var segments = new List<WallCandidate> { new(new XyPoint(12, 1), new XyPoint(13, 1)) };
        var modes = new[] { CandidateMatch.Alongside };

        Assert.Empty(CandidateMatcher.FindMatchIndices(LineStart, LineEnd, segments, modes, NoOccluders));
    }

    [Fact]
    public void CrossingsAndOpeningsInterleaveInOrderAlongTheLine()
    {
        // What an elevation dimension string actually reads like: a cross wall, two openings
        // in the facade, then another cross wall — sorted by position, not by category.
        var segments = new List<WallCandidate>
        {
            new(new XyPoint(9, -5), new XyPoint(9, 5)),      // 0: cross wall at 9
            new(new XyPoint(2.5, 1), new XyPoint(3.5, 1)),   // 1: opening centred at 3
            new(new XyPoint(1, -5), new XyPoint(1, 5)),      // 2: cross wall at 1
            new(new XyPoint(5.5, 1), new XyPoint(6.5, 1)),   // 3: opening centred at 6
        };
        var modes = new[]
        {
            CandidateMatch.Crossing,
            CandidateMatch.Alongside,
            CandidateMatch.Crossing,
            CandidateMatch.Alongside,
        };

        var result = CandidateMatcher.FindMatchIndices(LineStart, LineEnd, segments, modes, NoOccluders);

        Assert.Equal(new[] { 2, 1, 3, 0 }, result);
    }

    // ── What stands in the way ────────────────────────────────────────────────
    // An opening belongs to a line when nothing parallel comes between them. This replaces the
    // reach setting: a facade string reaches its whole facade and stops at the first wall behind.

    [Fact]
    public void AParallelWallBetweenTheOpeningAndTheLineBlocksIt()
    {
        // Door 4 away, with a parallel wall at 2 running right past it. The line dimensions the
        // facade in front, never the interior wall behind it.
        var segments = new List<WallCandidate> { new(new XyPoint(4, 4), new XyPoint(5, 4)) };
        var modes = new[] { CandidateMatch.Alongside };
        var occluders = new List<WallCandidate> { new(new XyPoint(0, 2), new XyPoint(10, 2)) };

        Assert.Empty(CandidateMatcher.FindMatchIndices(LineStart, LineEnd, segments, modes, occluders));
    }

    [Fact]
    public void AParallelWallOnTheOtherSideOfTheLineDoesNotBlock()
    {
        var segments = new List<WallCandidate> { new(new XyPoint(4, 4), new XyPoint(5, 4)) };
        var modes = new[] { CandidateMatch.Alongside };
        var occluders = new List<WallCandidate> { new(new XyPoint(0, -2), new XyPoint(10, -2)) };

        var result = CandidateMatcher.FindMatchIndices(LineStart, LineEnd, segments, modes, occluders);

        Assert.Equal(new[] { 0 }, result);
    }

    [Fact]
    public void AParallelWallBeyondTheOpeningDoesNotBlock()
    {
        // Behind the door, not in front of it.
        var segments = new List<WallCandidate> { new(new XyPoint(4, 2), new XyPoint(5, 2)) };
        var modes = new[] { CandidateMatch.Alongside };
        var occluders = new List<WallCandidate> { new(new XyPoint(0, 4), new XyPoint(10, 4)) };

        var result = CandidateMatcher.FindMatchIndices(LineStart, LineEnd, segments, modes, occluders);

        Assert.Equal(new[] { 0 }, result);
    }

    [Fact]
    public void AParallelWallThatStopsShortOfTheOpeningDoesNotBlock()
    {
        // The wall covers x 0–3; the door is centred at 4.5. A line sees through a gap in a facade.
        var segments = new List<WallCandidate> { new(new XyPoint(4, 4), new XyPoint(5, 4)) };
        var modes = new[] { CandidateMatch.Alongside };
        var occluders = new List<WallCandidate> { new(new XyPoint(0, 2), new XyPoint(3, 2)) };

        var result = CandidateMatcher.FindMatchIndices(LineStart, LineEnd, segments, modes, occluders);

        Assert.Equal(new[] { 0 }, result);
    }

    [Fact]
    public void AWallTheLineCrossesNeverBlocks()
    {
        // Perpendicular, and passing between the line and the door — but a wall the line crosses
        // is something it dimensions, not something in its way.
        var segments = new List<WallCandidate> { new(new XyPoint(4, 4), new XyPoint(5, 4)) };
        var modes = new[] { CandidateMatch.Alongside };
        var occluders = new List<WallCandidate> { new(new XyPoint(4.5, -5), new XyPoint(4.5, 5)) };

        var result = CandidateMatcher.FindMatchIndices(LineStart, LineEnd, segments, modes, occluders);

        Assert.Equal(new[] { 0 }, result);
    }

    [Fact]
    public void AnOpeningsOwnHostWallDoesNotBlockIt()
    {
        // The host is in the occluder list like every other wall. A door sits ON its host's
        // location curve, so "strictly nearer" excludes it with no special case.
        var segments = new List<WallCandidate> { new(new XyPoint(4, 2), new XyPoint(5, 2)) };
        var modes = new[] { CandidateMatch.Alongside };
        var occluders = new List<WallCandidate> { new(new XyPoint(0, 2), new XyPoint(10, 2)) };

        var result = CandidateMatcher.FindMatchIndices(LineStart, LineEnd, segments, modes, occluders);

        Assert.Equal(new[] { 0 }, result);
    }

    // ── One opening, one owner ────────────────────────────────────────────────
    // Facades carry stacked dimension strings. Every one of them qualifies for the same door,
    // and dimensioning it from all of them is noise — the nearest string owns it.

    private static readonly ReferenceLine NearLine = new(new XyPoint(0, 0), new XyPoint(10, 0));
    private static readonly ReferenceLine FarLine = new(new XyPoint(0, -2), new XyPoint(10, -2));

    [Fact]
    public void AnOpeningGoesToTheNearestLineOnly()
    {
        // Door centred at (4.5, 1): 1 from the near line, 3 from the far one. Both qualify.
        var segments = new List<WallCandidate> { new(new XyPoint(4, 1), new XyPoint(5, 1)) };
        var modes = new[] { CandidateMatch.Alongside };

        var result = CandidateMatcher.FindMatchIndicesForLines(
            new[] { NearLine, FarLine }, segments, modes, NoOccluders);

        Assert.Equal(new[] { 0 }, result[0]);
        Assert.Empty(result[1]);
    }

    [Fact]
    public void TheLosingLineStillGetsItsOwnWallCrossings()
    {
        // Losing a door must not cost a line the walls it crosses — only openings are owned.
        var segments = new List<WallCandidate>
        {
            new(new XyPoint(4, 1), new XyPoint(5, 1)),    // 0: door, nearest to the near line
            new(new XyPoint(6, -5), new XyPoint(6, 5)),   // 1: wall crossing both lines
        };
        var modes = new[] { CandidateMatch.Alongside, CandidateMatch.Crossing };

        var result = CandidateMatcher.FindMatchIndicesForLines(
            new[] { NearLine, FarLine }, segments, modes, NoOccluders);

        Assert.Equal(new[] { 0, 1 }, result[0]);
        Assert.Equal(new[] { 1 }, result[1]);
    }

    [Fact]
    public void AnOpeningBlockedFromEveryLineGoesToNobody()
    {
        // Door at 8, with a parallel wall at 6 standing between it and both lines (at 0 and -2).
        var segments = new List<WallCandidate> { new(new XyPoint(4, 8), new XyPoint(5, 8)) };
        var modes = new[] { CandidateMatch.Alongside };
        var occluders = new List<WallCandidate> { new(new XyPoint(0, 6), new XyPoint(10, 6)) };

        var result = CandidateMatcher.FindMatchIndicesForLines(
            new[] { NearLine, FarLine }, segments, modes, occluders);

        Assert.Empty(result[0]);
        Assert.Empty(result[1]);
    }

    [Fact]
    public void ALineBlockedFromTheOpeningDoesNotCompeteForIt()
    {
        // Door at -4.5. The near line at 0 is closer, but a parallel wall at -1 stands between
        // them; the far line at -2 has that wall on its other side and takes the door. Ownership
        // is decided among the lines that QUALIFY, not by distance alone.
        var segments = new List<WallCandidate> { new(new XyPoint(4, -4.5), new XyPoint(5, -4.5)) };
        var modes = new[] { CandidateMatch.Alongside };
        var occluders = new List<WallCandidate> { new(new XyPoint(0, -1), new XyPoint(10, -1)) };

        var result = CandidateMatcher.FindMatchIndicesForLines(
            new[] { NearLine, FarLine }, segments, modes, occluders);

        Assert.Empty(result[0]);
        Assert.Equal(new[] { 0 }, result[1]);
    }

    [Fact]
    public void AnOpeningFallsToAFartherLineWhenTheNearestDoesNotSpanIt()
    {
        // The short line is nearer (1 away) but stops at x=3, so the door at x=4.5 projects off
        // its end. The far line is 3 away and spans it.
        var shortNearLine = new ReferenceLine(new XyPoint(0, 1.9), new XyPoint(3, 1.9));
        var segments = new List<WallCandidate> { new(new XyPoint(4, 1), new XyPoint(5, 1)) };
        var modes = new[] { CandidateMatch.Alongside };

        var result = CandidateMatcher.FindMatchIndicesForLines(
            new[] { shortNearLine, FarLine }, segments, modes, NoOccluders);

        Assert.Empty(result[0]);
        Assert.Equal(new[] { 0 }, result[1]);
    }

    [Fact]
    public void TwoOpeningsCanBelongToDifferentLines()
    {
        var segments = new List<WallCandidate>
        {
            new(new XyPoint(2, 1), new XyPoint(3, 1)),      // 0: 1 from near, 3 from far
            new(new XyPoint(6, -3), new XyPoint(7, -3)),    // 1: 3 from near, 1 from far
        };
        var modes = new[] { CandidateMatch.Alongside, CandidateMatch.Alongside };

        var result = CandidateMatcher.FindMatchIndicesForLines(
            new[] { NearLine, FarLine }, segments, modes, NoOccluders);

        Assert.Equal(new[] { 0 }, result[0]);
        Assert.Equal(new[] { 1 }, result[1]);
    }

    [Fact]
    public void AnEquidistantOpeningGoesToTheFirstLineDeterministically()
    {
        // Centred at (4.5, -1): exactly 1 from both lines. Either answer is defensible; the
        // same answer every run is not optional, or re-running would shuffle dimensions
        // between strings.
        var segments = new List<WallCandidate> { new(new XyPoint(4, -1), new XyPoint(5, -1)) };
        var modes = new[] { CandidateMatch.Alongside };

        var result = CandidateMatcher.FindMatchIndicesForLines(
            new[] { NearLine, FarLine }, segments, modes, NoOccluders);

        Assert.Equal(new[] { 0 }, result[0]);
        Assert.Empty(result[1]);
    }

    [Fact]
    public void OpeningsStillOrderAlongTheLineThatOwnsThem()
    {
        var segments = new List<WallCandidate>
        {
            new(new XyPoint(7.5, 1), new XyPoint(8.5, 1)),  // 0: centred at 8
            new(new XyPoint(1.5, 1), new XyPoint(2.5, 1)),  // 1: centred at 2
            new(new XyPoint(4.5, 1), new XyPoint(5.5, 1)),  // 2: centred at 5
        };
        var modes = new[]
        {
            CandidateMatch.Alongside, CandidateMatch.Alongside, CandidateMatch.Alongside,
        };

        var result = CandidateMatcher.FindMatchIndicesForLines(
            new[] { NearLine }, segments, modes, NoOccluders);

        Assert.Equal(new[] { 1, 2, 0 }, result[0]);
    }

    [Fact]
    public void NoLinesYieldsNoResults()
    {
        var segments = new List<WallCandidate> { new(new XyPoint(4, 1), new XyPoint(5, 1)) };
        var modes = new[] { CandidateMatch.Alongside };

        Assert.Empty(CandidateMatcher.FindMatchIndicesForLines(
            new ReferenceLine[0], segments, modes, NoOccluders));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj --filter FullyQualifiedName~CandidateMatcherTests`

Expected: FAIL to **compile**, with errors on every `FindMatchIndices`/`FindMatchIndicesForLines` call — CS1503 (`cannot convert from 'System.Collections.Generic.IReadOnlyList<WallCandidate>' to 'double'`) or CS1929. That compile failure is the red state for this task.

- [ ] **Step 3: Rewrite the matcher**

Replace the whole of `src/RVTuk.Core/AutoDimensions/CandidateMatcher.cs` with:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace RVTuk.Core.AutoDimensions
{
    /// <summary>How a candidate earns its place on a reference line.</summary>
    public enum CandidateMatch
    {
        /// <summary>The line passes through it — a wall, measured across its thickness.</summary>
        Crossing,

        /// <summary>
        /// The line runs alongside it — an opening in a wall parallel to the line, measured
        /// across its width.
        /// </summary>
        Alongside,
    }

    /// <summary>One reference line, as plain 2D endpoints.</summary>
    public record ReferenceLine(XyPoint Start, XyPoint End);

    /// <summary>
    /// Which candidates a reference line dimensions, in order along it.
    ///
    /// The two kinds are matched by opposite tests, because their references face opposite ways
    /// (see <see cref="ReferenceAlignment"/>). A wall is measured across its thickness, so the
    /// line must cross it. An opening is measured across its width, so the line must run ALONG
    /// its host wall — a line crossing that wall lies parallel to the jambs and cannot measure
    /// them at all.
    ///
    /// Running alongside has no intersection to key on, so an opening qualifies on three counts:
    /// its host wall is near-parallel to the line, its centre falls within the line's span, and
    /// nothing parallel stands between its host wall and the line. That last is what a distance
    /// setting used to approximate: a dimension string reaches the whole run it belongs to and
    /// stops at the first wall behind it, which is a question of what is in the way, not of how
    /// many millimetres away it sits.
    ///
    /// Both kinds come back interleaved in one order along the line, which is what makes a
    /// dimension string read correctly: cross wall, jamb, jamb, cross wall.
    /// </summary>
    public static class CandidateMatcher
    {
        private const double BoundaryEpsilon = 1e-6;

        /// <summary>
        /// How much nearer the line a wall must be than an opening to stand in its way. Chiefly
        /// there so an opening's own host wall is not read as blocking it: a door sits on its
        /// host's location curve, so the two differ by rounding alone.
        /// </summary>
        private const double BlockingEpsilon = 1e-6;

        /// <summary>Matches for a single line. A thin wrapper — one line owns everything.</summary>
        public static IReadOnlyList<int> FindMatchIndices(
            XyPoint lineStart,
            XyPoint lineEnd,
            IReadOnlyList<WallCandidate> segments,
            IReadOnlyList<CandidateMatch> matchModes,
            IReadOnlyList<WallCandidate> occluders)
        {
            var lines = new[] { new ReferenceLine(lineStart, lineEnd) };
            return FindMatchIndicesForLines(lines, segments, matchModes, occluders)[0];
        }

        /// <summary>
        /// Matches for every reference line at once. Walls go to each line that crosses them; an
        /// opening goes to the single nearest line that qualifies, so a facade with three stacked
        /// dimension strings dimensions each door once, from the innermost.
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
        public static IReadOnlyList<IReadOnlyList<int>> FindMatchIndicesForLines(
            IReadOnlyList<ReferenceLine> lines,
            IReadOnlyList<WallCandidate> segments,
            IReadOnlyList<CandidateMatch> matchModes,
            IReadOnlyList<WallCandidate> occluders)
        {
            var perLine = new List<List<(int Index, double T)>>();
            for (int i = 0; i < lines.Count; i++) perLine.Add(new List<(int, double)>());

            for (int c = 0; c < segments.Count; c++)
            {
                var mode = c < matchModes.Count ? matchModes[c] : CandidateMatch.Crossing;

                if (mode == CandidateMatch.Crossing)
                {
                    for (int l = 0; l < lines.Count; l++)
                    {
                        if (WallCrossingFinder.TryGetCrossingParameter(
                                lines[l].Start, lines[l].End, segments[c], out var t))
                            perLine[l].Add((c, t));
                    }
                    continue;
                }

                var owner = -1;
                var ownerT = 0.0;
                var ownerDistance = double.MaxValue;
                for (int l = 0; l < lines.Count; l++)
                {
                    if (!TryGetAlongside(lines[l].Start, lines[l].End, segments[c],
                            out var t, out var offset)) continue;
                    if (IsBlocked(lines[l].Start, lines[l].End, occluders, t, offset)) continue;

                    // Strict: a tie keeps the earlier line, so a door exactly between two
                    // strings lands the same way on every re-run rather than shuffling.
                    var distance = Math.Abs(offset);
                    if (distance >= ownerDistance) continue;

                    owner = l;
                    ownerT = t;
                    ownerDistance = distance;
                }

                if (owner >= 0) perLine[owner].Add((c, ownerT));
            }

            return perLine
                .Select(m => (IReadOnlyList<int>)m.OrderBy(x => x.T).Select(x => x.Index).ToList())
                .ToList();
        }

        /// <summary>
        /// Where the opening's centre projects along the line, if its wall runs parallel to the
        /// line and the centre falls inside the line's span. Also hands back the SIGNED
        /// perpendicular offset: its magnitude decides ownership between lines, and its sign says
        /// which side of the line the opening is on, which is what makes "in the way" answerable.
        /// </summary>
        private static bool TryGetAlongside(
            XyPoint lineStart,
            XyPoint lineEnd,
            WallCandidate segment,
            out double t,
            out double offset)
        {
            t = 0;
            offset = 0;

            var lineX = lineEnd.X - lineStart.X;
            var lineY = lineEnd.Y - lineStart.Y;
            var lineLengthSquared = lineX * lineX + lineY * lineY;
            if (lineLengthSquared < 1e-24) return false;

            var segmentX = segment.End.X - segment.Start.X;
            var segmentY = segment.End.Y - segment.Start.Y;
            if (Math.Abs(segmentX) < 1e-12 && Math.Abs(segmentY) < 1e-12) return false;

            if (!IsParallelToLine(lineX, lineY, segmentX, segmentY)) return false;

            var centerX = (segment.Start.X + segment.End.X) / 2.0;
            var centerY = (segment.Start.Y + segment.End.Y) / 2.0;

            var candidateT = ProjectOnLine(
                lineStart, lineX, lineY, lineLengthSquared, centerX, centerY);
            if (candidateT <= BoundaryEpsilon || candidateT >= 1 - BoundaryEpsilon) return false;

            t = candidateT;
            offset = SignedOffset(
                lineStart, lineX, lineY, Math.Sqrt(lineLengthSquared), centerX, centerY);
            return true;
        }

        /// <summary>
        /// Whether a parallel wall stands between the opening and the line: on the same side of
        /// it, strictly nearer, and spanning the opening's station along the line.
        ///
        /// The opening's own host wall excludes itself here with no special case — a door's
        /// location sits on its host's location curve, so "strictly nearer" fails for it. A wall
        /// the line crosses is never parallel, so it never blocks. A wall that stops short of the
        /// opening's station does not block either, which is what lets a line see an opening
        /// through a gap in the wall run in front of it.
        /// </summary>
        private static bool IsBlocked(
            XyPoint lineStart,
            XyPoint lineEnd,
            IReadOnlyList<WallCandidate> occluders,
            double openingT,
            double openingOffset)
        {
            var lineX = lineEnd.X - lineStart.X;
            var lineY = lineEnd.Y - lineStart.Y;
            var lineLengthSquared = lineX * lineX + lineY * lineY;
            if (lineLengthSquared < 1e-24) return false;
            var lineLength = Math.Sqrt(lineLengthSquared);

            foreach (var wall in occluders)
            {
                var wallX = wall.End.X - wall.Start.X;
                var wallY = wall.End.Y - wall.Start.Y;
                if (Math.Abs(wallX) < 1e-12 && Math.Abs(wallY) < 1e-12) continue;
                if (!IsParallelToLine(lineX, lineY, wallX, wallY)) continue;

                // Parallel, so every point of it shares one offset; the midpoint speaks for it.
                var wallOffset = SignedOffset(
                    lineStart, lineX, lineY, lineLength,
                    (wall.Start.X + wall.End.X) / 2.0,
                    (wall.Start.Y + wall.End.Y) / 2.0);

                if (wallOffset * openingOffset <= 0) continue; // other side of the line
                if (Math.Abs(wallOffset) >= Math.Abs(openingOffset) - BlockingEpsilon) continue;

                var t0 = ProjectOnLine(
                    lineStart, lineX, lineY, lineLengthSquared, wall.Start.X, wall.Start.Y);
                var t1 = ProjectOnLine(
                    lineStart, lineX, lineY, lineLengthSquared, wall.End.X, wall.End.Y);
                if (t0 > t1) (t0, t1) = (t1, t0);

                if (openingT > t0 + BoundaryEpsilon && openingT < t1 - BoundaryEpsilon) return true;
            }

            return false;
        }

        private static bool IsParallelToLine(
            double lineX, double lineY, double segmentX, double segmentY)
        {
            var lineAngle = Math.Atan2(lineY, lineX);
            var segmentAngle = Math.Atan2(segmentY, segmentX);
            return Angle2D.FromParallelDegrees(lineAngle, segmentAngle)
                < WallCrossingFinder.ParallelToleranceDegrees;
        }

        /// <summary>How far along the line a point projects — 0 at its start, 1 at its end.</summary>
        private static double ProjectOnLine(
            XyPoint lineStart,
            double lineX,
            double lineY,
            double lineLengthSquared,
            double x,
            double y)
        {
            var dx = x - lineStart.X;
            var dy = y - lineStart.Y;
            return (dx * lineX + dy * lineY) / lineLengthSquared;
        }

        /// <summary>
        /// Perpendicular distance from the line, signed: the two sides get opposite signs, which
        /// is all the caller needs (which sign means which side is arbitrary and never asked).
        /// </summary>
        private static double SignedOffset(
            XyPoint lineStart,
            double lineX,
            double lineY,
            double lineLength,
            double x,
            double y)
        {
            var dx = x - lineStart.X;
            var dy = y - lineStart.Y;
            return (dx * lineY - dy * lineX) / lineLength;
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj`

Expected: PASS, all tests, zero failures. (The whole suite, not just the matcher — nothing else should have moved.)

- [ ] **Step 5: Commit**

```bash
git add src/RVTuk.Core/AutoDimensions/CandidateMatcher.cs tests/RVTuk.Core.Tests/AutoDimensions/CandidateMatcherTests.cs
git commit -m "feat(auto-dimensions): a wall between, not a distance, decides an opening's line

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 2: Occluders, and a dimension type the pane chooses

**Files:**
- Modify: `src/RVTuk.Core/AutoDimensions/AutoDimensionsScope.cs`
- Create: `src/RVTuk.Revit/AutoDimensions/DimensionTypeFinder.cs`
- Modify: `src/RVTuk.Revit/AutoDimensions/DimensionCandidateCollector.cs`
- Modify: `src/RVTuk.Revit/AutoDimensions/DimensionRunner.cs`
- Modify: `src/RVTuk.Revit/AutoDimensions/ScopeSelectionStore.cs`
- Modify: `src/RVTuk.Revit/AutoDimensions/ExternalEvents/LevelDiscoveryEventHandler.cs`
- Modify: `src/RVTuk.Revit/AutoDimensions/ExternalEvents/CreateDimensionsEventHandler.cs`
- Modify: `src/RVTuk.Revit/Application.cs`
- Modify: `src/RVTuk.UI/AutoDimensions/ViewModels/AutoDimensionsPaneViewModel.cs`
- Modify: `src/RVTuk.UI/AutoDimensions/Views/AutoDimensionsPaneView.xaml`

**Interfaces:**
- Consumes from Task 1: `CandidateMatcher.FindMatchIndicesForLines(lines, segments, matchModes, occluders)`.
- Produces:
  - `record DimensionTypeInfo(long Id, string Name)` in `RVTuk.Core.AutoDimensions`
  - `record ScopeSelection(int CategoryMask, IReadOnlyList<long> CheckedViewIds, long DimensionTypeId)`
  - `record AutoDimensionsScope(IReadOnlyList<LevelScope> Levels, ScopeSelection? Selection, IReadOnlyList<DimensionTypeInfo> DimensionTypes)`
  - `DimensionTypeFinder.Find(Document doc) → IReadOnlyList<DimensionTypeInfo>`
  - `DimensionCandidateSet.Occluders` (`IReadOnlyList<WallCandidate>`)
  - `DimensionRunner.RunPair(Document doc, IReadOnlyList<DetailLine> referenceLines, View targetView, DimensionCategories categories, DimensionType? dimensionType, DimensionRunTally tally)`
  - `CreateDimensionsEventHandler.Prepare(int categoryMask, IReadOnlyList<long> checkedViewIds, long dimensionTypeId)`
  - `ScopeSelectionStore.Write(Document doc, int categoryMask, IReadOnlyList<long> checkedViewIds, long dimensionTypeId)`
  - The pane delegate becomes `Func<int, IReadOnlyList<long>, long, string>`
  - `ScopeDefaults` no longer exists.

- [ ] **Step 1: Swap the reach out of the Core scope model**

Replace the whole of `src/RVTuk.Core/AutoDimensions/AutoDimensionsScope.cs` with:

```csharp
using System.Collections.Generic;

namespace RVTuk.Core.AutoDimensions
{
    /// <summary>What the pane persisted on its last successful run; null when it never ran.</summary>
    public record ScopeSelection(
        int CategoryMask,
        IReadOnlyList<long> CheckedViewIds,
        long DimensionTypeId);

    /// <summary>
    /// One of the project's dimension types, as the pane's dropdown sees it. Which type a run
    /// uses is the user's choice because it carries their office's appearance — and because
    /// Revit's "Show Opening Height", which prints a door's height under its width, lives on the
    /// type rather than on the dimension.
    /// </summary>
    public record DimensionTypeInfo(long Id, string Name);

    /// <summary>Everything one discovery pass tells the pane about the open project.</summary>
    public record AutoDimensionsScope(
        IReadOnlyList<LevelScope> Levels,
        ScopeSelection? Selection,
        IReadOnlyList<DimensionTypeInfo> DimensionTypes);
}
```

- [ ] **Step 2: Add the dimension-type finder**

Create `src/RVTuk.Revit/AutoDimensions/DimensionTypeFinder.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RVTuk.Core.AutoDimensions;

namespace RVTuk.Revit.AutoDimensions
{
    /// <summary>
    /// The project's linear dimension types, for the pane's dropdown — linear being the only
    /// style <see cref="Autodesk.Revit.Creation.Document.NewDimension(View, Line, ReferenceArray)"/>
    /// produces.
    ///
    /// Falls back to every dimension type when that filter finds none: an empty dropdown would
    /// leave the user with no way to run at all, which is a worse failure than offering a type
    /// the run may not be able to use.
    /// </summary>
    public static class DimensionTypeFinder
    {
        public static IReadOnlyList<DimensionTypeInfo> Find(Document doc)
        {
            var all = new FilteredElementCollector(doc)
                .OfClass(typeof(DimensionType))
                .Cast<DimensionType>()
                .Where(t => !string.IsNullOrWhiteSpace(t.Name))
                .ToList();

            var linear = all.Where(IsLinear).ToList();

            return (linear.Count > 0 ? linear : all)
                .Select(t => new DimensionTypeInfo(t.Id.Value, t.Name))
                .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private static bool IsLinear(DimensionType type)
        {
            try
            {
                return type.StyleType == DimensionStyleType.Linear;
            }
            catch
            {
                // A type whose style Revit will not report is not one to offer.
                return false;
            }
        }
    }
}
```

- [ ] **Step 3: Collect occluders in the candidate collector**

In `src/RVTuk.Revit/AutoDimensions/DimensionCandidateCollector.cs`, add the `Occluders` property to `DimensionCandidateSet`, immediately after the `MatchModes` property:

```csharp
        /// <summary>
        /// Every straight wall the view cuts, host and linked, in host coordinates — whether or
        /// not walls are being dimensioned. An opening is only dimensioned by a line with nothing
        /// parallel between them, and unticking Walls means "do not dimension walls", not
        /// "pretend walls are not there".
        /// </summary>
        public IReadOnlyList<WallCandidate> Occluders { get; set; } = new List<WallCandidate>();
```

- [ ] **Step 4: Fill it — accumulator, collection and the two call sites**

Still in `DimensionCandidateCollector.cs`, make four edits.

First, add `Occluders = accumulated.Occluders,` to the object initialiser in `Collect`, after `MatchModes = accumulated.MatchModes,`.

Second, add the list to `Accumulator`, after the `MatchModes` field:

```csharp
            public readonly List<WallCandidate> Occluders = new List<WallCandidate>();
```

Third, replace the whole `AddWalls` method with:

```csharp
        private static void AddWalls(
            IEnumerable<Wall> walls,
            RevitLinkInstance? link,
            Transform transform,
            double? cutZ,
            bool dimensionThem,
            Accumulator accumulated)
        {
            // Straight walls only: Core's finder is a 2D segment intersection, and Revit can't
            // linear-dimension a curved face against a straight line anyway.
            foreach (var wall in walls)
            {
                if ((wall.Location as LocationCurve)?.Curve is not Line centerline) continue;
                if (!ReachesCutPlane(wall, transform, cutZ))
                {
                    // Only counted when walls were asked for: the tally reports what the user
                    // wanted and did not get, and a wall collected purely to occlude was never
                    // wanted. Counting them would bury the real exclusions under hundreds.
                    if (dimensionThem) accumulated.ExcludedNotCut++;
                    continue;
                }

                var segment = new WallCandidate(
                    ToXyPoint(transform.OfPoint(centerline.GetEndPoint(0))),
                    ToXyPoint(transform.OfPoint(centerline.GetEndPoint(1))));

                // Every wall stands in the way of the openings behind it, dimensioned or not.
                accumulated.Occluders.Add(segment);
                if (!dimensionThem) continue;

                accumulated.Add(
                    new DimensionCandidate
                    {
                        Kind = DimensionCandidateKind.Wall,
                        Wall = wall,
                        Link = link,
                    },
                    segment,
                    CandidateMatch.Crossing);
            }
        }
```

Fourth, make both callers unconditional. In `CollectHost`, replace:

```csharp
            if (categories.HasFlag(DimensionCategories.Walls))
            {
                AddWalls(
                    new FilteredElementCollector(doc, view.Id).OfClass(typeof(Wall)).Cast<Wall>(),
                    null, Transform.Identity, cutZ, accumulated);
            }
```

with:

```csharp
            AddWalls(
                new FilteredElementCollector(doc, view.Id).OfClass(typeof(Wall)).Cast<Wall>(),
                null, Transform.Identity, cutZ,
                categories.HasFlag(DimensionCategories.Walls), accumulated);
```

and in `CollectLinks`, replace:

```csharp
                    if (categories.HasFlag(DimensionCategories.Walls))
                    {
                        AddWalls(
                            new FilteredElementCollector(linkDoc).OfClass(typeof(Wall)).Cast<Wall>(),
                            link, transform, cutZ, accumulated);
                    }
```

with:

```csharp
                    AddWalls(
                        new FilteredElementCollector(linkDoc).OfClass(typeof(Wall)).Cast<Wall>(),
                        link, transform, cutZ,
                        categories.HasFlag(DimensionCategories.Walls), accumulated);
```

- [ ] **Step 5: State the Left/Right invariant where the references are built**

Still in `DimensionCandidateCollector.cs`, inside `TryAppendReferences`, replace this block:

```csharp
                    var left = candidate.Instance.GetReferences(FamilyInstanceReferenceType.Left);
                    var right = candidate.Instance.GetReferences(FamilyInstanceReferenceType.Right);
                    if (left.Count == 0 || right.Count == 0) return false;

                    first = left[0];
                    second = right[0];
```

with:

```csharp
                    // Left and Right ONLY — never CenterLeftRight, Front/Back or Strong/Weak.
                    // Exactly two references, both belonging to this instance, is what makes the
                    // segment a jamb-to-jamb measure of one opening; that is the shape Revit's
                    // "Show Opening Height" recognises, and a stray centre reference would split
                    // it into two meaningless halves. These are the reference planes' "Is
                    // Reference" property inside the family, not their names.
                    var left = candidate.Instance.GetReferences(FamilyInstanceReferenceType.Left);
                    var right = candidate.Instance.GetReferences(FamilyInstanceReferenceType.Right);
                    if (left.Count == 0 || right.Count == 0) return false;

                    // Expected to be one apiece; a family exposing several (nested families being
                    // the likely source) is served by the first, which at least stays stable.
                    first = left[0];
                    second = right[0];
```

- [ ] **Step 6: Pass occluders and the dimension type through the runner**

In `src/RVTuk.Revit/AutoDimensions/DimensionRunner.cs`, replace the `RunPair` signature and its matcher call. Replace:

```csharp
        public static void RunPair(
            Document doc,
            IReadOnlyList<DetailLine> referenceLines,
            View targetView,
            DimensionCategories categories,
            double openingReach,
            DimensionRunTally tally)
```

with:

```csharp
        public static void RunPair(
            Document doc,
            IReadOnlyList<DetailLine> referenceLines,
            View targetView,
            DimensionCategories categories,
            DimensionType? dimensionType,
            DimensionRunTally tally)
```

and replace:

```csharp
            var matches = CandidateMatcher.FindMatchIndicesForLines(
                lines, candidates.Segments, candidates.MatchModes, openingReach);
```

with:

```csharp
            var matches = CandidateMatcher.FindMatchIndicesForLines(
                lines, candidates.Segments, candidates.MatchModes, candidates.Occluders);
```

Then replace the `RunLine` call inside the loop:

```csharp
                    RunLine(doc, referenceLines[i], geometry[i], targetView, candidates,
                        byLine[i] ?? Array.Empty<int>(), tally);
```

with:

```csharp
                    RunLine(doc, referenceLines[i], geometry[i], targetView, candidates,
                        byLine[i] ?? Array.Empty<int>(), dimensionType, tally);
```

- [ ] **Step 7: Apply the type on both creation paths**

Still in `DimensionRunner.cs`, change `RunLine`'s signature — replace:

```csharp
            DimensionCandidateSet candidates,
            IReadOnlyList<int> matchIndices,
            DimensionRunTally tally)
```

with:

```csharp
            DimensionCandidateSet candidates,
            IReadOnlyList<int> matchIndices,
            DimensionType? dimensionType,
            DimensionRunTally tally)
```

Replace the creation pair inside `RunLine`:

```csharp
            var dimension = doc.Create.NewDimension(targetView, dimensionLine, referenceArray);
            dimension = RemoveCoincidentReferences(doc, targetView, dimensionLine, dimension, tally);
```

with:

```csharp
            var dimension = CreateDimension(
                doc, targetView, dimensionLine, referenceArray, dimensionType);
            dimension = RemoveCoincidentReferences(
                doc, targetView, dimensionLine, dimension, dimensionType, tally);
```

Change `RemoveCoincidentReferences`'s signature — replace:

```csharp
        private static Dimension? RemoveCoincidentReferences(
            Document doc, View view, Line dimensionLine, Dimension dimension, DimensionRunTally tally)
```

with:

```csharp
        private static Dimension? RemoveCoincidentReferences(
            Document doc,
            View view,
            Line dimensionLine,
            Dimension dimension,
            DimensionType? dimensionType,
            DimensionRunTally tally)
```

and its final line — replace:

```csharp
            return doc.Create.NewDimension(view, dimensionLine, filtered);
```

with:

```csharp
            return CreateDimension(doc, view, dimensionLine, filtered, dimensionType);
```

Finally add this helper just below `RemoveCoincidentReferences`:

```csharp
        /// <summary>
        /// One place both creation paths go through, so the recreated (de-duplicated) dimension
        /// cannot quietly fall back to the view's default type while the original carried the
        /// chosen one. Null means no type was chosen, or the chosen one no longer exists — the
        /// view's default is then the only sensible answer.
        /// </summary>
        private static Dimension CreateDimension(
            Document doc,
            View view,
            Line dimensionLine,
            ReferenceArray references,
            DimensionType? dimensionType)
        {
            return dimensionType == null
                ? doc.Create.NewDimension(view, dimensionLine, references)
                : doc.Create.NewDimension(view, dimensionLine, references, dimensionType);
        }
```

- [ ] **Step 8: Persist the dimension type instead of the reach**

Replace the whole of `src/RVTuk.Revit/AutoDimensions/ScopeSelectionStore.cs` with:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using RVTuk.Core.AutoDimensions;

namespace RVTuk.Revit.AutoDimensions
{
    /// <summary>
    /// Per-project memory of the scope pane's last successful run — the checked category mask,
    /// the checked view ids and the chosen dimension type — on the document's ProjectInformation
    /// element. Written in the same transaction as the run itself, never on a checkbox toggle: a
    /// selection the user never ran isn't worth a transaction, and the persisted state should
    /// mean "what was last built".
    ///
    /// Ids live in string fields (see IdListCodec for why, not array fields; extensible storage
    /// has no long field at all, which settles it for the dimension type).
    /// </summary>
    public static class ScopeSelectionStore
    {
        // Bumped when the opening-reach field gave way to the dimension type: a Schema is
        // immutable once registered in a session, so a changed field means a new guid. Any
        // selection saved under the previous schema is simply orphaned, and the pane falls back
        // to its defaults once.
        private static readonly Guid SchemaGuid = new Guid("6f2c81d4-9a7b-4e35-8c10-b4d6e27f9a51");
        private const string SchemaName = "RVTukAutoDimensionsScopeSelectionV3";
        private const string CategoryMaskField = "CategoryMask";
        private const string ViewIdsField = "CheckedViewIds";
        private const string DimensionTypeIdField = "DimensionTypeId";

        public static ScopeSelection? Read(Document doc)
        {
            var schema = Schema.Lookup(SchemaGuid);
            if (schema == null) return null;

            var projectInfo = doc.ProjectInformation;
            if (projectInfo == null) return null;

            var entity = projectInfo.GetEntity(schema);
            if (!entity.IsValid()) return null;

            return new ScopeSelection(
                entity.Get<int>(CategoryMaskField),
                IdListCodec.Decode(entity.Get<string>(ViewIdsField)),
                ParseId(entity.Get<string>(DimensionTypeIdField)));
        }

        /// <summary>Must be called inside an open transaction.</summary>
        public static void Write(
            Document doc,
            int categoryMask,
            IReadOnlyList<long> checkedViewIds,
            long dimensionTypeId)
        {
            var projectInfo = doc.ProjectInformation;
            if (projectInfo == null) return;

            var entity = new Entity(GetOrCreateSchema());
            entity.Set(CategoryMaskField, categoryMask);
            entity.Set(ViewIdsField, IdListCodec.Encode(checkedViewIds));
            entity.Set(
                DimensionTypeIdField,
                dimensionTypeId.ToString(CultureInfo.InvariantCulture));
            projectInfo.SetEntity(entity);
        }

        /// <summary>Zero for anything unreadable — the pane reads that as "nothing chosen".</summary>
        private static long ParseId(string? stored)
        {
            return long.TryParse(
                stored, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                ? id
                : 0;
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
            builder.AddSimpleField(CategoryMaskField, typeof(int));
            builder.AddSimpleField(ViewIdsField, typeof(string));
            builder.AddSimpleField(DimensionTypeIdField, typeof(string));
            return builder.Finish();
        }
    }
}
```

- [ ] **Step 9: Report the dimension types from discovery**

In `src/RVTuk.Revit/AutoDimensions/ExternalEvents/LevelDiscoveryEventHandler.cs`, replace:

```csharp
        private static readonly AutoDimensionsScope Empty =
            new AutoDimensionsScope(Array.Empty<LevelScope>(), null);
```

with:

```csharp
        private static readonly AutoDimensionsScope Empty =
            new AutoDimensionsScope(
                Array.Empty<LevelScope>(), null, Array.Empty<DimensionTypeInfo>());
```

and replace:

```csharp
                Result = new AutoDimensionsScope(
                    LevelScopeFinder.Find(doc),
                    ScopeSelectionStore.Read(doc));
```

with:

```csharp
                Result = new AutoDimensionsScope(
                    LevelScopeFinder.Find(doc),
                    ScopeSelectionStore.Read(doc),
                    DimensionTypeFinder.Find(doc));
```

- [ ] **Step 10: Resolve and pass the type in the run handler**

In `src/RVTuk.Revit/AutoDimensions/ExternalEvents/CreateDimensionsEventHandler.cs`, replace:

```csharp
        private int _openingReachMillimetres = ScopeDefaults.OpeningReachMillimetres;

        public string Summary { get; private set; } = string.Empty;

        public void Prepare(int categoryMask, IReadOnlyList<long> checkedViewIds, int openingReachMillimetres)
        {
            _categoryMask = categoryMask;
            _checkedViewIds = checkedViewIds;
            _openingReachMillimetres = openingReachMillimetres;
            _done.Reset();
        }
```

with:

```csharp
        private long _dimensionTypeId;

        public string Summary { get; private set; } = string.Empty;

        public void Prepare(int categoryMask, IReadOnlyList<long> checkedViewIds, long dimensionTypeId)
        {
            _categoryMask = categoryMask;
            _checkedViewIds = checkedViewIds;
            _dimensionTypeId = dimensionTypeId;
            _done.Reset();
        }
```

Replace the unit conversion:

```csharp
            var selectedViewIds = new HashSet<long>(_checkedViewIds);
            // The pane speaks millimetres; everything past here is Revit's internal feet.
            var openingReach = UnitUtils.ConvertToInternalUnits(
                _openingReachMillimetres, UnitTypeId.Millimeters);
```

with:

```csharp
            var selectedViewIds = new HashSet<long>(_checkedViewIds);
            // Resolved once for the whole fan-out. Null when nothing was chosen or the chosen
            // type has since been deleted — the runner then falls back to each view's default.
            var dimensionType = _dimensionTypeId > 0
                ? doc.GetElement(new ElementId(_dimensionTypeId)) as DimensionType
                : null;
```

Replace the run call:

```csharp
                            DimensionRunner.RunPair(
                                doc, referenceLines, targetView, categories, openingReach, tally);
```

with:

```csharp
                            DimensionRunner.RunPair(
                                doc, referenceLines, targetView, categories, dimensionType, tally);
```

And replace the persist call:

```csharp
                    ScopeSelectionStore.Write(
                        doc, _categoryMask, _checkedViewIds, _openingReachMillimetres);
```

with:

```csharp
                    ScopeSelectionStore.Write(
                        doc, _categoryMask, _checkedViewIds, _dimensionTypeId);
```

- [ ] **Step 11: Widen the delegate in the add-in entry point**

In `src/RVTuk.Revit/Application.cs`, replace:

```csharp
                Func<int, IReadOnlyList<long>, int, string> createDimensions = (mask, viewIds, reachMm) =>
                {
                    CreateDimensionsHandler.Prepare(mask, viewIds, reachMm);
```

with:

```csharp
                Func<int, IReadOnlyList<long>, long, string> createDimensions = (mask, viewIds, dimensionTypeId) =>
                {
                    CreateDimensionsHandler.Prepare(mask, viewIds, dimensionTypeId);
```

- [ ] **Step 12: Swap the pane's reach box for dimension-type state**

In `src/RVTuk.UI/AutoDimensions/ViewModels/AutoDimensionsPaneViewModel.cs`, replace the delegate field and constructor parameter — replace:

```csharp
        private readonly Func<int, IReadOnlyList<long>, int, string> _createDimensions;
```

with:

```csharp
        private readonly Func<int, IReadOnlyList<long>, long, string> _createDimensions;
```

and replace:

```csharp
            Func<int, IReadOnlyList<long>, int, string> createDimensions)
```

with:

```csharp
            Func<int, IReadOnlyList<long>, long, string> createDimensions)
```

In the constructor body, add this line immediately after the `Levels = new ObservableCollection<LevelNodeViewModel>();` line:

```csharp
            DimensionTypes = new ObservableCollection<DimensionTypeInfo>();
```

Next to `public ObservableCollection<LevelNodeViewModel> Levels { get; }`, add:

```csharp
        public ObservableCollection<DimensionTypeInfo> DimensionTypes { get; }
```

Replace the whole reach field and property:

```csharp
        private int _openingReachMillimetres = ScopeDefaults.OpeningReachMillimetres;

        /// <summary>
        /// How far from the reference line a door's or window's wall may sit and still be
        /// dimensioned by it. Openings are measured across their width, so they only qualify on
        /// walls running ALONG the line — and a line never touches such a wall, so this distance
        /// is what stands in for "crossing". Drafting convention, hence the user's to set:
        /// exterior dimension strings commonly sit further out than the 1000 mm default.
        /// </summary>
        public int OpeningReachMillimetres
        {
            get => _openingReachMillimetres;
            set => SetProperty(ref _openingReachMillimetres, value < 0 ? 0 : value);
        }
```

with:

```csharp
        private DimensionTypeInfo? _selectedDimensionType;

        /// <summary>
        /// The type every dimension the run creates is given. Without it each target view
        /// contributes its own default, so one fan-out could produce several appearances — and
        /// "Show Opening Height", which prints a door's height under its width, is a property of
        /// the type, so choosing the type is the only way to reach it.
        /// </summary>
        public DimensionTypeInfo? SelectedDimensionType
        {
            get => _selectedDimensionType;
            set => SetProperty(ref _selectedDimensionType, value);
        }
```

In `Populate`, replace:

```csharp
            OpeningReachMillimetres = scope.Selection?.OpeningReachMillimetres
                ?? ScopeDefaults.OpeningReachMillimetres;
```

with:

```csharp
            DimensionTypes.Clear();
            foreach (var dimensionType in scope.DimensionTypes) DimensionTypes.Add(dimensionType);

            // The persisted type if it still exists, else the first — never nothing, or the run
            // button would sit enabled over a dropdown the user never touched.
            var persistedTypeId = scope.Selection?.DimensionTypeId ?? 0;
            SelectedDimensionType =
                DimensionTypes.FirstOrDefault(t => t.Id == persistedTypeId)
                ?? DimensionTypes.FirstOrDefault();
```

And in `RunCreateDimensions`, replace:

```csharp
            var reach = OpeningReachMillimetres;
```

with:

```csharp
            var dimensionTypeId = SelectedDimensionType?.Id ?? 0;
```

and replace:

```csharp
                    summary = _createDimensions(mask, viewIds, reach);
```

with:

```csharp
                    summary = _createDimensions(mask, viewIds, dimensionTypeId);
```

- [ ] **Step 13: Swap the pane's reach box for a dropdown**

In `src/RVTuk.UI/AutoDimensions/Views/AutoDimensionsPaneView.xaml`, replace the whole reach `StackPanel` (the block starting `<StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Margin="10,0,10,10"` and ending with its `</StackPanel>`) with:

```xml
        <StackPanel DockPanel.Dock="Top" Margin="10,0,10,10"
                    ToolTip="The type given to every dimension this tool creates. Tick 'Show Opening Height' on it in Revit and doors and windows print their height under their width.">
            <TextBlock Text="DIMENSION TYPE"
                       Foreground="{StaticResource Brush.TextMuted}"
                       FontSize="10" FontWeight="SemiBold"
                       Margin="0,0,0,4"/>
            <ComboBox ItemsSource="{Binding DimensionTypes}"
                      SelectedItem="{Binding SelectedDimensionType, Mode=TwoWay}"
                      DisplayMemberPath="Name"/>
        </StackPanel>
```

- [ ] **Step 14: Build both configurations**

Run: `dotnet build RVTuk.sln -c Release2024`
Expected: `Build succeeded`, 0 errors. (Warnings pre-existing in this repo are fine.)

Run: `dotnet build RVTuk.sln -c Release2025`
Expected: `Build succeeded`, 0 errors.

If either fails on a leftover reference to `ScopeDefaults` or `OpeningReachMillimetres`, that call site was missed — the deleted names appear nowhere after this task. Confirm with:

Run: `git grep -n "OpeningReach\|ScopeDefaults" -- src tests`
Expected: no output.

- [ ] **Step 15: Run the tests**

Run: `dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj`
Expected: PASS, zero failures.

- [ ] **Step 16: Commit**

```bash
git add src tests
git commit -m "feat(auto-dimensions): walls occlude openings, and the pane chooses the dimension type

Walls are now collected whether or not they are being dimensioned, because
they stand in the way of the openings behind them either way. The reach
setting it replaces is gone from the pane, the run and the stored selection.

In its place the pane chooses a dimension type, applied on both creation
paths, which is what puts Revit's Show Opening Height within reach.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 3: Documentation

**Files:**
- Modify: `docs/tools/auto-dimensions/README.md`
- Modify: `docs/tools/auto-dimensions/backlog.md`
- Modify: `CLAUDE.md`

**Interfaces:**
- Consumes: the finished behaviour of Tasks 1 and 2. Produces nothing code depends on.

- [ ] **Step 1: Update the README**

In `docs/tools/auto-dimensions/README.md`, make four edits.

First, replace:

```markdown
just that one view ticked). Levels collapse; their views are listed alphabetically.
```

with:

```markdown
just that one view ticked). Levels collapse; their views are listed alphabetically. The pane also
picks the **dimension type** every created dimension is given — the only way to reach Revit's
*Show Opening Height*, which prints a door's or window's height under its width. Tick that on the
type in Revit: the tool applies types, it never edits them.
```

Second, replace:

```markdown
intersection to key on, so an opening qualifies when its wall is near-parallel to the line, its
centre falls within the line's span, and it sits within the pane's **"openings within N mm of
the line"** distance (default 1000). That distance is drafting convention rather than geometry,
which is why it's the user's to set — exterior dimension strings often sit further out.
```

with:

```markdown
intersection to key on, so an opening qualifies when its wall is near-parallel to the line, its
centre falls within the line's span, and **no other parallel wall stands between that wall and
the line**. There is no distance limit — a string reaches the whole run it belongs to, however
far out it sits, and stops at the first wall behind it. Walls are therefore collected on every
run whether or not they are being dimensioned: unticking Walls means "don't dimension walls",
not "pretend walls aren't there". Only the walls actually asked for are counted in the run
summary's exclusions.
```

Third, replace:

```markdown
among the lines that actually qualify, not merely the nearest, so a door the closest string
can't reach or doesn't span still falls to one that does.
```

with:

```markdown
among the lines that actually qualify, not merely the nearest, so a door the closest string
can't see or doesn't span still falls to one that does.
```

Fourth, under `## Docs`, add these two entries after the `specs/2026-07-23-…` line:

```markdown
- [specs/2026-07-29-auto-dimensions-opening-references-design.md](specs/2026-07-29-auto-dimensions-opening-references-design.md) — approved design (occlusion, dimension type)
- [plans/2026-07-29-auto-dimensions-opening-references.md](plans/2026-07-29-auto-dimensions-opening-references.md) — implementation plan (occlusion, dimension type)
```

- [ ] **Step 2: Close the fixed bug and record what replaced it**

In `docs/tools/auto-dimensions/backlog.md`, delete this bug entirely (it is what Task 1 fixed):

```markdown
- [ ] **An opening's nearest parallel wall isn't preferred.** Openings are deduplicated across
  *lines* (nearest qualifying line owns each one), but not across *walls*: a generous reach on a
  line running along a facade still pulls in openings from a parallel interior wall behind it,
  because both walls' openings qualify for the same line. Ignoring openings with another parallel
  wall between them and the line would settle it.
```

Under `## ⏳ Release`, add these two verification items after the existing "In-Revit verification pass" item:

```markdown
- [ ] **Verify *Show Opening Height* actually fires.** Tick it on a dimension type, pick that type
  in the pane, run over a plan with doors and windows, and confirm the height prints under the
  width — in the host model *and* through a Revit link, which is the documented sore spot. If it
  does not fire on `GetReferences(Left/Right)` references, the reference strategy needs revisiting,
  not the type.
- [ ] **Verify occlusion on a real plan.** Draw a reference line outside a facade and confirm the
  string dimensions the facade's openings and none from the interior walls behind it; then untick
  Walls and confirm the same openings are matched (walls still occlude) and that the run summary's
  "does not cut" count does not balloon.
```

Under `## 🚀 Ideas`, add:

```markdown
- [ ] **A family exposing several Left/Right references.** `GetReferences` returns a list and the
  first is used. Expected to be a list of one — Revit's *Is Reference* flag is set per reference
  plane, and nested families are the plausible source of duplicates. Choosing between them would
  need each reference's position, which family-instance references do not readily give up.
```

- [ ] **Step 3: Point CLAUDE.md at the new spec**

In `CLAUDE.md`, find the Auto Dimensions bullet under Features. Replace:

```markdown
  and [`docs/tools/auto-dimensions/specs/2026-07-23-auto-dimensions-scope-pane-design.md`](docs/tools/auto-dimensions/specs/2026-07-23-auto-dimensions-scope-pane-design.md).
```

with:

```markdown
  [`docs/tools/auto-dimensions/specs/2026-07-23-auto-dimensions-scope-pane-design.md`](docs/tools/auto-dimensions/specs/2026-07-23-auto-dimensions-scope-pane-design.md)
  and [`docs/tools/auto-dimensions/specs/2026-07-29-auto-dimensions-opening-references-design.md`](docs/tools/auto-dimensions/specs/2026-07-29-auto-dimensions-opening-references-design.md).
```

- [ ] **Step 4: Check nothing still documents the removed setting**

Run: `git grep -rn "Openings within\|OpeningReach\|opening reach" -- docs CLAUDE.md`
Expected: matches only inside `docs/tools/auto-dimensions/specs/` (the older spec and the new one, both of which describe history deliberately) and `docs/archive/`. Any match in `README.md`, `backlog.md` or `CLAUDE.md` is a miss — fix it.

- [ ] **Step 5: Commit**

```bash
git add docs CLAUDE.md
git commit -m "docs(auto-dimensions): occlusion and the dimension-type picker

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Verification in Revit (manual, after Task 3)

Neither change has a test that touches Revit, so these are the checks that actually close the work. They need a Revit session and a real project — the user runs them.

1. Deploy: `.\Deploy.ps1 2025` in an elevated shell, then restart Revit.
2. Open a plan with a "Dimensions_Line" detail line drawn outside a facade. Run with Walls, Doors and Windows ticked. Expect the facade's openings dimensioned and none from parallel interior walls behind it.
3. Untick Walls, re-run. Expect the same openings matched — walls still occlude — and the "does not cut" line in the summary not to jump by hundreds.
4. Draw a second reference line further out. Expect each opening dimensioned once, by the inner line.
5. Tick *Show Opening Height* on a dimension type, choose it in the pane, re-run. Expect that type's appearance on every dimension, and door/window heights printed under their widths.
6. Re-open the project and the pane. Expect the dropdown to come back on the type last run with.
7. Repeat step 5 against a Revit link, which is where the feature is documented to be unreliable.

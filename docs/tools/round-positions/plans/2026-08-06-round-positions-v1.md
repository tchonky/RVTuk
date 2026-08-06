# Round Positions v1 — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A ribbon button that translates every eligible element in the active Revit model onto a whole-centimetre grid (optionally allowing half centimetres), reporting what it moved and what it refused to touch.

**Architecture:** All snapping maths lives in `RVTuk.Core.RoundPositions` as pure functions over plain structs and is unit-tested. `RVTuk.Revit.RoundPositions` holds six collection passes that read geometry, ask Core for a delta, and write it back inside one `Transaction` with a `SubTransaction` per moved element. `RVTuk.UI.RoundPositions` holds a one-checkbox pre-run dialog and a results window. Everything runs inside the command's own API context — no external events.

**Tech Stack:** C# (net48 for Revit 2024, net8.0-windows for 2025), WPF, xunit, Revit API 2024/2025.

Spec: [2026-08-06-round-positions-design.md](../specs/2026-08-06-round-positions-design.md)

## Global Constraints

- **v1 translates only.** No length, angle, endpoint or sketch is ever modified. Geometry repair is v2.
- **Grid anchored at Revit's internal origin**, step 0.5 cm or 1.0 cm by user choice, `MidpointRounding.AwayFromZero`, 1 ft = 30.48 cm exactly.
- **Orthogonality is a displacement test, not an angle test**: a line runs along X iff its Y extent is ≤ 0.01 cm and its X extent is > 0.01 cm. This avoids the angle test's length-scaling problem, where 0.01° over a 40 m wall is 0.7 cm of drift.
- **`RVTuk.Core` must never reference a Revit or WPF type.** `RVTuk.UI` must never reference a Revit type.
- **Namespace = root namespace + folder path, exactly.**
- Revit 2024/2025 only (`RVTuk.Revit`). KKarea/2023 does not ship this tool.
- Core uses block-scoped namespaces; the test project uses file-scoped. Match the surrounding files.
- Minimal code comments — comment *why*, never *what*.
- Build with `dotnet build RVTuk.sln -c Release2024` and `-c Release2025`. Tests: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj`.
- New `.cs` files need no `.csproj` edit — all projects are SDK-style and auto-glob.

---

## Phase 1 — Core maths (pure, TDD)

### Task 1: GridPoint and GridSnapper

**Files:**
- Create: `src/RVTuk.Core/RoundPositions/GridPoint.cs`
- Create: `src/RVTuk.Core/RoundPositions/GridSnapper.cs`
- Test: `tests/RVTuk.Core.Tests/RoundPositions/GridSnapperTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `GridPoint(double X, double Y, double Z)`; `GridSnapper.StepFor(bool) → double`, `.FromFeet(double) → double`, `.ToFeet(double) → double`, `.Snap(double, double step) → double`, `.Snap(GridPoint, double step) → GridPoint`, `.IsOnGrid(double, double step) → bool`; constants `CentimetresPerFoot`, `HalfCentimetreStep`, `WholeCentimetreStep`, `EpsilonCentimetres`.

- [ ] **Step 1: Write the failing test**

Create `tests/RVTuk.Core.Tests/RoundPositions/GridSnapperTests.cs`:

```csharp
using RVTuk.Core.RoundPositions;
using Xunit;

namespace RVTuk.Core.Tests.RoundPositions;

public class GridSnapperTests
{
    private const double Half = GridSnapper.HalfCentimetreStep;
    private const double Whole = GridSnapper.WholeCentimetreStep;
    private const double Tolerance = 1e-9;

    [Theory]
    [InlineData(0.1, 0.0)]
    [InlineData(0.34, 0.5)]
    [InlineData(0.5, 0.5)]
    [InlineData(1000.4, 1000.5)]
    [InlineData(1000.2, 1000.0)]
    public void HalfCentimetreGridSnapsToTheNearestHalf(double input, double expected)
    {
        Assert.Equal(expected, GridSnapper.Snap(input, Half), Tolerance);
    }

    [Theory]
    [InlineData(0.1, 0.0)]
    [InlineData(0.34, 0.0)]
    [InlineData(0.5, 1.0)]
    [InlineData(1000.4, 1000.0)]
    public void WholeCentimetreGridSnapsToTheNearestCentimetre(double input, double expected)
    {
        Assert.Equal(expected, GridSnapper.Snap(input, Whole), Tolerance);
    }

    [Fact]
    public void ExactMidpointsResolveAwayFromZeroOnBothSides()
    {
        // Banker's rounding would send 0.5 and 1.5 both to even values, so a model mirrored
        // about the origin would round asymmetrically.
        Assert.Equal(1.0, GridSnapper.Snap(0.5, Whole), Tolerance);
        Assert.Equal(-1.0, GridSnapper.Snap(-0.5, Whole), Tolerance);
        Assert.Equal(0.5, GridSnapper.Snap(0.25, Half), Tolerance);
        Assert.Equal(-0.5, GridSnapper.Snap(-0.25, Half), Tolerance);
    }

    [Theory]
    [InlineData(-0.1, 0.0)]
    [InlineData(-0.34, -0.5)]
    [InlineData(-1000.4, -1000.5)]
    public void NegativeCoordinatesSnapSymmetrically(double input, double expected)
    {
        Assert.Equal(expected, GridSnapper.Snap(input, Half), Tolerance);
    }

    [Theory]
    [InlineData(Half)]
    [InlineData(Whole)]
    public void NothingEverMovesFurtherThanHalfAStep(double step)
    {
        for (var raw = -50.0; raw <= 50.0; raw += 0.017)
        {
            var moved = System.Math.Abs(GridSnapper.Snap(raw, step) - raw);
            Assert.True(moved <= step / 2 + Tolerance, $"{raw} moved {moved}");
        }
    }

    [Fact]
    public void FeetAndCentimetresRoundTrip()
    {
        Assert.Equal(30.48, GridSnapper.FromFeet(1.0), Tolerance);
        Assert.Equal(1.0, GridSnapper.ToFeet(30.48), Tolerance);
        Assert.Equal(12.34, GridSnapper.ToFeet(GridSnapper.FromFeet(12.34)), Tolerance);
    }

    [Fact]
    public void StepForPicksTheGridTheCheckboxAsksFor()
    {
        Assert.Equal(Half, GridSnapper.StepFor(true), Tolerance);
        Assert.Equal(Whole, GridSnapper.StepFor(false), Tolerance);
    }

    [Fact]
    public void IsOnGridToleratesFloatDustButNotDrift()
    {
        Assert.True(GridSnapper.IsOnGrid(1.5 + 1e-12, Half));
        Assert.False(GridSnapper.IsOnGrid(1.5 + 1e-4, Half));
    }

    [Fact]
    public void SnappingAPointSnapsAllThreeAxes()
    {
        var snapped = GridSnapper.Snap(new GridPoint(1.001, 5.001, 2.4), Half);
        Assert.Equal(1.0, snapped.X, Tolerance);
        Assert.Equal(5.0, snapped.Y, Tolerance);
        Assert.Equal(2.5, snapped.Z, Tolerance);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj --filter GridSnapperTests
```

Expected: FAIL — build error, `RVTuk.Core.RoundPositions` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/RVTuk.Core/RoundPositions/GridPoint.cs`:

```csharp
namespace RVTuk.Core.RoundPositions
{
    /// <summary>A point in centimetres. Revit hands out decimal feet; GridSnapper converts at the
    /// boundary so no other file has to remember the factor.</summary>
    public readonly record struct GridPoint(double X, double Y, double Z);
}
```

Create `src/RVTuk.Core/RoundPositions/GridSnapper.cs`:

```csharp
using System;

namespace RVTuk.Core.RoundPositions
{
    /// <summary>
    /// The whole-centimetre grid every position lands on, anchored at Revit's internal origin.
    /// The step is the user's choice, so it travels as a parameter rather than sitting in a
    /// constant.
    /// </summary>
    public static class GridSnapper
    {
        public const double CentimetresPerFoot = 30.48;

        public const double HalfCentimetreStep = 0.5;
        public const double WholeCentimetreStep = 1.0;

        /// <summary>Values equal to within this are the same value — float dust, not drift.</summary>
        public const double EpsilonCentimetres = 1e-9;

        public static double StepFor(bool allowHalfCentimetres) =>
            allowHalfCentimetres ? HalfCentimetreStep : WholeCentimetreStep;

        public static double FromFeet(double feet) => feet * CentimetresPerFoot;

        public static double ToFeet(double centimetres) => centimetres / CentimetresPerFoot;

        /// <summary>
        /// AwayFromZero rather than .NET's banker's rounding: an exact midpoint must resolve the
        /// same distance from the origin on both sides, so on the 1 cm grid 0.5 goes to 1.0 and
        /// −0.5 to −1.0.
        /// </summary>
        public static double Snap(double centimetres, double step) =>
            Math.Round(centimetres / step, MidpointRounding.AwayFromZero) * step;

        public static GridPoint Snap(GridPoint point, double step) =>
            new GridPoint(Snap(point.X, step), Snap(point.Y, step), Snap(point.Z, step));

        public static bool IsOnGrid(double centimetres, double step) =>
            Math.Abs(centimetres - Snap(centimetres, step)) <= EpsilonCentimetres;
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj --filter GridSnapperTests
```

Expected: PASS, 9 tests (including theory cases).

- [ ] **Step 5: Commit**

```bash
git add src/RVTuk.Core/RoundPositions tests/RVTuk.Core.Tests/RoundPositions && git commit -m "feat(round-positions): the grid everything lands on"
```

---

### Task 2: AxisClassifier

**Files:**
- Create: `src/RVTuk.Core/RoundPositions/AxisClassifier.cs`
- Test: `tests/RVTuk.Core.Tests/RoundPositions/AxisClassifierTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `enum LineAxis { AlongX, AlongY, NotOrthogonal }`; `AxisClassifier.Classify(double deltaX, double deltaY, double toleranceCentimetres) → LineAxis`; constant `AxisClassifier.ToleranceCentimetres`.

- [ ] **Step 1: Write the failing test**

Create `tests/RVTuk.Core.Tests/RoundPositions/AxisClassifierTests.cs`:

```csharp
using RVTuk.Core.RoundPositions;
using Xunit;

namespace RVTuk.Core.Tests.RoundPositions;

public class AxisClassifierTests
{
    private const double Tol = AxisClassifier.ToleranceCentimetres;

    [Fact]
    public void AHorizontalLineRunsAlongX()
    {
        Assert.Equal(LineAxis.AlongX, AxisClassifier.Classify(400.0, 0.0, Tol));
    }

    [Fact]
    public void AHorizontalLineDrawnRightToLeftAlsoRunsAlongX()
    {
        // Which end Revit calls "start" is arbitrary; the test must be undirected.
        Assert.Equal(LineAxis.AlongX, AxisClassifier.Classify(-400.0, 0.0, Tol));
    }

    [Fact]
    public void AVerticalLineRunsAlongY()
    {
        Assert.Equal(LineAxis.AlongY, AxisClassifier.Classify(0.0, 400.0, Tol));
        Assert.Equal(LineAxis.AlongY, AxisClassifier.Classify(0.0, -400.0, Tol));
    }

    [Fact]
    public void FloatDustInTheStoredGeometryStillCountsAsOrthogonal()
    {
        Assert.Equal(LineAxis.AlongX, AxisClassifier.Classify(400.0, 1e-9, Tol));
    }

    [Fact]
    public void ANearOrthogonalWallIsNotOrthogonal()
    {
        // 89.97° over 4 m is ~0.2 cm of drift. v1 skips it rather than sliding a crooked wall
        // sideways; straightening is v2.
        Assert.Equal(LineAxis.NotOrthogonal, AxisClassifier.Classify(0.2, 400.0, Tol));
    }

    [Fact]
    public void ADiagonalIsNotOrthogonal()
    {
        Assert.Equal(LineAxis.NotOrthogonal, AxisClassifier.Classify(300.0, 400.0, Tol));
        Assert.Equal(LineAxis.NotOrthogonal, AxisClassifier.Classify(400.0, 400.0, Tol));
    }

    [Fact]
    public void ADegenerateLineIsNotOrthogonal()
    {
        Assert.Equal(LineAxis.NotOrthogonal, AxisClassifier.Classify(0.0, 0.0, Tol));
        Assert.Equal(LineAxis.NotOrthogonal, AxisClassifier.Classify(1e-9, 1e-9, Tol));
    }

    [Fact]
    public void ToleranceIsADisplacementSoItDoesNotScaleWithLength()
    {
        // The same 0.5 cm of drift is a rounding error on a 40 m wall and a real angle on a
        // 40 cm one. An angle test would call the first orthogonal and the second not; the
        // displacement test calls both crooked, which is the honest answer.
        Assert.Equal(LineAxis.NotOrthogonal, AxisClassifier.Classify(4000.0, 0.5, Tol));
        Assert.Equal(LineAxis.NotOrthogonal, AxisClassifier.Classify(40.0, 0.5, Tol));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj --filter AxisClassifierTests
```

Expected: FAIL — build error, `AxisClassifier` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/RVTuk.Core/RoundPositions/AxisClassifier.cs`:

```csharp
using System;

namespace RVTuk.Core.RoundPositions
{
    /// <summary>Which world axis a line runs along, in plan.</summary>
    public enum LineAxis
    {
        /// <summary>Runs along X, so its Y is the coordinate that gets snapped.</summary>
        AlongX,

        /// <summary>Runs along Y, so its X is the coordinate that gets snapped.</summary>
        AlongY,

        /// <summary>Diagonal, near-orthogonal or degenerate. v1 skips all three.</summary>
        NotOrthogonal,
    }

    public static class AxisClassifier
    {
        /// <summary>
        /// A displacement, not an angle. An angular tolerance scales with length — 0.01° is float
        /// dust on a 40 cm wall and 0.7 cm of real drift on a 40 m one — so the same tolerance
        /// would mean two different things depending on what it was applied to.
        /// </summary>
        public const double ToleranceCentimetres = 0.01;

        public static LineAxis Classify(double deltaX, double deltaY, double toleranceCentimetres)
        {
            var spansX = Math.Abs(deltaX) > toleranceCentimetres;
            var spansY = Math.Abs(deltaY) > toleranceCentimetres;

            if (spansX && !spansY) return LineAxis.AlongX;
            if (spansY && !spansX) return LineAxis.AlongY;
            return LineAxis.NotOrthogonal;
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj --filter AxisClassifierTests
```

Expected: PASS, 8 tests.

- [ ] **Step 5: Commit**

```bash
git add src/RVTuk.Core/RoundPositions tests/RVTuk.Core.Tests/RoundPositions && git commit -m "feat(round-positions): orthogonal means a displacement test, not an angle"
```

---

### Task 3: LineOffset

**Files:**
- Create: `src/RVTuk.Core/RoundPositions/LineOffset.cs`
- Test: `tests/RVTuk.Core.Tests/RoundPositions/LineOffsetTests.cs`

**Interfaces:**
- Consumes: `GridPoint`, `GridSnapper`, `AxisClassifier`, `LineAxis`.
- Produces: `enum LineOffsetOutcome { Offset, AlreadyOnGrid, NotOrthogonal }`; `readonly record struct LineOffsetResult(LineOffsetOutcome Outcome, double DeltaX, double DeltaY)`; `LineOffset.For(GridPoint start, GridPoint end, double step, double toleranceCentimetres) → LineOffsetResult`.

- [ ] **Step 1: Write the failing test**

Create `tests/RVTuk.Core.Tests/RoundPositions/LineOffsetTests.cs`:

```csharp
using RVTuk.Core.RoundPositions;
using Xunit;

namespace RVTuk.Core.Tests.RoundPositions;

public class LineOffsetTests
{
    private const double Half = GridSnapper.HalfCentimetreStep;
    private const double Tol = AxisClassifier.ToleranceCentimetres;
    private const double Epsilon = 1e-9;

    private static LineOffsetResult For(GridPoint a, GridPoint b) => LineOffset.For(a, b, Half, Tol);

    [Fact]
    public void AHorizontalLineMovesInYOnly()
    {
        // The spec's worked example: (10.3, 5.2) → (410.9, 5.2) becomes y = 5.0, x untouched.
        var result = For(new GridPoint(10.3, 5.2, 0), new GridPoint(410.9, 5.2, 0));

        Assert.Equal(LineOffsetOutcome.Offset, result.Outcome);
        Assert.Equal(0.0, result.DeltaX, Epsilon);
        Assert.Equal(-0.2, result.DeltaY, Epsilon);
    }

    [Fact]
    public void AVerticalLineMovesInXOnly()
    {
        var result = For(new GridPoint(5.2, 10.3, 0), new GridPoint(5.2, 410.9, 0));

        Assert.Equal(LineOffsetOutcome.Offset, result.Outcome);
        Assert.Equal(-0.2, result.DeltaX, Epsilon);
        Assert.Equal(0.0, result.DeltaY, Epsilon);
    }

    [Fact]
    public void TheAlongAxisCoordinatesAreNeverTouched()
    {
        // This is the whole point of translating rather than reshaping: lengths cannot change,
        // so two walls sharing a corner cannot pull it apart.
        var result = For(new GridPoint(10.3, 5.2, 0), new GridPoint(410.9, 5.2, 0));
        Assert.Equal(0.0, result.DeltaX, Epsilon);
    }

    [Fact]
    public void ALineAlreadyOnTheGridReportsNoMovement()
    {
        var result = For(new GridPoint(10.3, 5.0, 0), new GridPoint(410.9, 5.0, 0));

        Assert.Equal(LineOffsetOutcome.AlreadyOnGrid, result.Outcome);
        Assert.Equal(0.0, result.DeltaX, Epsilon);
        Assert.Equal(0.0, result.DeltaY, Epsilon);
    }

    [Fact]
    public void ANonOrthogonalLineIsRejectedWithNoDelta()
    {
        var result = For(new GridPoint(0, 0, 0), new GridPoint(301.2, 400.1, 0));

        Assert.Equal(LineOffsetOutcome.NotOrthogonal, result.Outcome);
        Assert.Equal(0.0, result.DeltaX, Epsilon);
        Assert.Equal(0.0, result.DeltaY, Epsilon);
    }

    [Fact]
    public void TheWholeCentimetreGridUsesTheWholeCentimetreStep()
    {
        var result = LineOffset.For(
            new GridPoint(10.3, 5.4, 0), new GridPoint(410.9, 5.4, 0),
            GridSnapper.WholeCentimetreStep, Tol);

        Assert.Equal(LineOffsetOutcome.Offset, result.Outcome);
        Assert.Equal(-0.4, result.DeltaY, Epsilon);
    }

    [Fact]
    public void ZIsNeverPartOfTheOffset()
    {
        // A wall's height comes from its level and base offset, which other passes own.
        var result = For(new GridPoint(10.0, 5.0, 3.7), new GridPoint(410.0, 5.0, 3.7));
        Assert.Equal(LineOffsetOutcome.AlreadyOnGrid, result.Outcome);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj --filter LineOffsetTests
```

Expected: FAIL — build error, `LineOffset` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/RVTuk.Core/RoundPositions/LineOffset.cs`:

```csharp
using System;

namespace RVTuk.Core.RoundPositions
{
    public enum LineOffsetOutcome
    {
        Offset,
        AlreadyOnGrid,
        NotOrthogonal,
    }

    /// <summary>The translation that brings one straight line onto the grid, in centimetres. Only
    /// ever one of the two deltas is non-zero.</summary>
    public readonly record struct LineOffsetResult(
        LineOffsetOutcome Outcome, double DeltaX, double DeltaY);

    /// <summary>
    /// v1 moves a line parallel to itself: the coordinate it does not run along is snapped, and
    /// the along-axis coordinates are left exactly where they were. Lengths and angles never
    /// change — which is what stops two walls sharing a corner from each moving it and splitting
    /// it in two.
    /// </summary>
    public static class LineOffset
    {
        public static LineOffsetResult For(
            GridPoint start, GridPoint end, double step, double toleranceCentimetres)
        {
            var axis = AxisClassifier.Classify(end.X - start.X, end.Y - start.Y, toleranceCentimetres);

            var deltaX = 0.0;
            var deltaY = 0.0;
            switch (axis)
            {
                case LineAxis.AlongX:
                    deltaY = GridSnapper.Snap(start.Y, step) - start.Y;
                    break;
                case LineAxis.AlongY:
                    deltaX = GridSnapper.Snap(start.X, step) - start.X;
                    break;
                default:
                    return new LineOffsetResult(LineOffsetOutcome.NotOrthogonal, 0, 0);
            }

            var moves = Math.Abs(deltaX) > GridSnapper.EpsilonCentimetres
                     || Math.Abs(deltaY) > GridSnapper.EpsilonCentimetres;

            return moves
                ? new LineOffsetResult(LineOffsetOutcome.Offset, deltaX, deltaY)
                : new LineOffsetResult(LineOffsetOutcome.AlreadyOnGrid, 0, 0);
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj --filter LineOffsetTests
```

Expected: PASS, 7 tests.

- [ ] **Step 5: Commit**

```bash
git add src/RVTuk.Core/RoundPositions tests/RVTuk.Core.Tests/RoundPositions && git commit -m "feat(round-positions): a line slides parallel to itself"
```

---

### Task 4: The report model

**Files:**
- Create: `src/RVTuk.Core/RoundPositions/SkipReason.cs`
- Create: `src/RVTuk.Core/RoundPositions/RoundingReport.cs`
- Test: `tests/RVTuk.Core.Tests/RoundPositions/RoundingReportTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `enum SkipReason`; `SkipReasonText.Describe(SkipReason) → string`; `readonly record struct SkippedElement(long ElementId, string Category, SkipReason Reason)`; `readonly record struct ReleasedConstraint(long ElementId, string Category, string Description)`; `class RoundingReportBuilder` with `CountExamined()`, `RecordMoved()`, `RecordAlreadyClean()`, `RecordSkipped(long, string, SkipReason)`, `RecordReleasedConstraint(long, string, string)`, `Build() → RoundingReport`; `record RoundingReport(int Examined, int Moved, int AlreadyClean, IReadOnlyList<SkippedElement> Skipped, IReadOnlyList<ReleasedConstraint> ReleasedConstraints)` with `GroupedByReason() → IReadOnlyList<SkipGroup>` and `record SkipGroup(SkipReason Reason, string Description, IReadOnlyList<SkippedElement> Elements)`.

- [ ] **Step 1: Write the failing test**

Create `tests/RVTuk.Core.Tests/RoundPositions/RoundingReportTests.cs`:

```csharp
using System.Linq;
using RVTuk.Core.RoundPositions;
using Xunit;

namespace RVTuk.Core.Tests.RoundPositions;

public class RoundingReportTests
{
    [Fact]
    public void CountsAddUp()
    {
        var builder = new RoundingReportBuilder();
        builder.CountExamined();
        builder.RecordMoved();
        builder.CountExamined();
        builder.RecordAlreadyClean();
        builder.CountExamined();
        builder.RecordSkipped(1234, "Walls", SkipReason.NotOrthogonal);

        var report = builder.Build();

        Assert.Equal(3, report.Examined);
        Assert.Equal(1, report.Moved);
        Assert.Equal(1, report.AlreadyClean);
        Assert.Single(report.Skipped);
    }

    [Fact]
    public void SkipsAreGroupedByReasonWithTheirDescription()
    {
        var builder = new RoundingReportBuilder();
        builder.RecordSkipped(1, "Walls", SkipReason.NotOrthogonal);
        builder.RecordSkipped(2, "Walls", SkipReason.NotOrthogonal);
        builder.RecordSkipped(3, "Furniture", SkipReason.InGroup);

        var groups = builder.Build().GroupedByReason();

        Assert.Equal(2, groups.Count);
        var notOrthogonal = groups.Single(g => g.Reason == SkipReason.NotOrthogonal);
        Assert.Equal(2, notOrthogonal.Elements.Count);
        Assert.False(string.IsNullOrWhiteSpace(notOrthogonal.Description));
    }

    [Fact]
    public void TheLargestGroupComesFirstSoTheWorstProblemIsVisible()
    {
        var builder = new RoundingReportBuilder();
        builder.RecordSkipped(1, "Furniture", SkipReason.InGroup);
        builder.RecordSkipped(2, "Walls", SkipReason.NotOrthogonal);
        builder.RecordSkipped(3, "Walls", SkipReason.NotOrthogonal);

        var groups = builder.Build().GroupedByReason();

        Assert.Equal(SkipReason.NotOrthogonal, groups[0].Reason);
    }

    [Fact]
    public void ReleasedConstraintsAreRecordedSeparatelyFromSkips()
    {
        // Nothing in a Revit model shows what used to be constrained, so this list is the only
        // record that the change happened.
        var builder = new RoundingReportBuilder();
        builder.RecordReleasedConstraint(42, "Doors", "Unlocked dimension 991");

        var report = builder.Build();

        Assert.Empty(report.Skipped);
        Assert.Single(report.ReleasedConstraints);
        Assert.Equal(42, report.ReleasedConstraints[0].ElementId);
    }

    [Fact]
    public void EverySkipReasonHasADescription()
    {
        foreach (SkipReason reason in System.Enum.GetValues(typeof(SkipReason)))
        {
            Assert.False(string.IsNullOrWhiteSpace(SkipReasonText.Describe(reason)),
                $"{reason} has no description");
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj --filter RoundingReportTests
```

Expected: FAIL — build error, `RoundingReportBuilder` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/RVTuk.Core/RoundPositions/SkipReason.cs`:

```csharp
namespace RVTuk.Core.RoundPositions
{
    public enum SkipReason
    {
        InRevitLink,
        ImportedCad,
        InGroup,
        InDesignOption,
        OwnedByOtherUser,
        NotOrthogonal,
        UnexpectedClipState,
        RevitRejected,
    }

    public static class SkipReasonText
    {
        public static string Describe(SkipReason reason) => reason switch
        {
            SkipReason.InRevitLink =>
                "In a linked model — linked models are never moved",
            SkipReason.ImportedCad =>
                "Imported or linked CAD — the drawing the model was traced against stays put",
            SkipReason.InGroup =>
                "In a group — moving one member would rewrite every instance of the group",
            SkipReason.InDesignOption =>
                "In a design option",
            SkipReason.OwnedByOtherUser =>
                "Checked out by another user",
            SkipReason.NotOrthogonal =>
                "Not orthogonal — straightening crooked geometry comes in v2",
            SkipReason.UnexpectedClipState =>
                "Base point in an unexpected clip state — moving it could shift shared coordinates",
            SkipReason.RevitRejected =>
                "Revit rejected the move, even after releasing what was holding it",
            _ => reason.ToString(),
        };
    }
}
```

Create `src/RVTuk.Core/RoundPositions/RoundingReport.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;

namespace RVTuk.Core.RoundPositions
{
    public readonly record struct SkippedElement(long ElementId, string Category, SkipReason Reason);

    /// <summary>A dimension unlocked or an alignment constraint deleted to let a move through.</summary>
    public readonly record struct ReleasedConstraint(long ElementId, string Category, string Description);

    public record SkipGroup(SkipReason Reason, string Description, IReadOnlyList<SkippedElement> Elements);

    public record RoundingReport(
        int Examined,
        int Moved,
        int AlreadyClean,
        IReadOnlyList<SkippedElement> Skipped,
        IReadOnlyList<ReleasedConstraint> ReleasedConstraints)
    {
        /// <summary>Largest group first — on an inherited model the biggest pile is the story.</summary>
        public IReadOnlyList<SkipGroup> GroupedByReason() =>
            Skipped
                .GroupBy(s => s.Reason)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key)
                .Select(g => new SkipGroup(g.Key, SkipReasonText.Describe(g.Key), g.ToList()))
                .ToList();
    }

    public class RoundingReportBuilder
    {
        private readonly List<SkippedElement> _skipped = new List<SkippedElement>();
        private readonly List<ReleasedConstraint> _released = new List<ReleasedConstraint>();

        public int Examined { get; private set; }
        public int Moved { get; private set; }
        public int AlreadyClean { get; private set; }

        public void CountExamined() => Examined++;

        public void RecordMoved() => Moved++;

        public void RecordAlreadyClean() => AlreadyClean++;

        public void RecordSkipped(long elementId, string category, SkipReason reason) =>
            _skipped.Add(new SkippedElement(elementId, category, reason));

        public void RecordReleasedConstraint(long elementId, string category, string description) =>
            _released.Add(new ReleasedConstraint(elementId, category, description));

        public RoundingReport Build() =>
            new RoundingReport(Examined, Moved, AlreadyClean, _skipped.ToList(), _released.ToList());
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj --filter RoundingReportTests
```

Expected: PASS, 5 tests.

- [ ] **Step 5: Run the whole Core suite for regressions**

```bash
dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj
```

Expected: PASS, no failures.

- [ ] **Step 6: Commit**

```bash
git add src/RVTuk.Core/RoundPositions tests/RVTuk.Core.Tests/RoundPositions && git commit -m "feat(round-positions): the run's report, grouped by why an element was left alone"
```

---

## Phase 2 — Revit vertical slice

At the end of this phase there is a working button that rounds **walls only**. Task 10 is the first
in-Revit run and the first chance to test the risky API assumptions. Do not skip its checklist.

### Task 5: The setting and the pre-run dialog

**Files:**
- Modify: `src/RVTuk.Core/Shared/Config/AppConfig.cs` (add one property after `DwgExportPdfOn`, around line 47)
- Create: `src/RVTuk.UI/RoundPositions/ViewModels/RoundPositionsViewModel.cs`
- Create: `src/RVTuk.UI/RoundPositions/Views/RoundPositionsWindow.xaml`
- Create: `src/RVTuk.UI/RoundPositions/Views/RoundPositionsWindow.xaml.cs`

**Interfaces:**
- Consumes: `RVTuk.UI.Shared.ViewModels.ViewModelBase`.
- Produces: `AppConfig.RoundPositionsWholeCentimetresOnly` (bool); `RoundPositionsViewModel` with `bool AllowHalfCentimetres { get; set; }`; `RoundPositionsWindow(RoundPositionsViewModel vm)` whose `ShowDialog()` returns `true` when Run was clicked.

- [ ] **Step 1: Add the setting**

In `src/RVTuk.Core/Shared/Config/AppConfig.cs`, directly after the `DwgExportPdfOn` property:

```csharp
        /// <summary>Round Positions' grid step, stored inverted (false = the default, half
        /// centimetres allowed) for the same reason as <see cref="DwgExportDwgOff"/>: net48's
        /// DataContractJsonSerializer skips property initializers, so a true-default property
        /// would flip to false when a config file written before this feature existed is loaded.</summary>
        public bool RoundPositionsWholeCentimetresOnly { get; set; }
```

- [ ] **Step 2: Write the view model**

Create `src/RVTuk.UI/RoundPositions/ViewModels/RoundPositionsViewModel.cs`:

```csharp
using RVTuk.UI.Shared.ViewModels;

namespace RVTuk.UI.RoundPositions.ViewModels
{
    public class RoundPositionsViewModel : ViewModelBase
    {
        private bool _allowHalfCentimetres = true;

        /// <summary>On: the grid is 0.5 cm. Off: 1 cm, and 0.5 rounds up to 1.</summary>
        public bool AllowHalfCentimetres
        {
            get => _allowHalfCentimetres;
            set => SetProperty(ref _allowHalfCentimetres, value);
        }
    }
}
```

- [ ] **Step 3: Write the window**

Create `src/RVTuk.UI/RoundPositions/Views/RoundPositionsWindow.xaml`:

```xml
<Window x:Class="RVTuk.UI.RoundPositions.Views.RoundPositionsWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="RVTuk — Round Positions"
        Width="420" SizeToContent="Height"
        WindowStartupLocation="CenterScreen" ResizeMode="NoResize"
        ShowInTaskbar="False">

    <Window.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="/RVTuk.UI;component/Shared/Themes/DarkTheme.xaml"/>
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Window.Resources>

    <Window.Background><StaticResource ResourceKey="Brush.Bg"/></Window.Background>
    <Window.Foreground><StaticResource ResourceKey="Brush.Text"/></Window.Foreground>

    <StackPanel Margin="14">
        <TextBlock TextWrapping="Wrap" Margin="0,0,0,10"
                   Text="Moves every eligible element in this model onto a whole-centimetre grid. Lengths, angles and sketches are never changed."/>

        <CheckBox IsChecked="{Binding AllowHalfCentimetres}" Margin="0,0,0,4"
                  Content="Allow half centimetres"/>
        <TextBlock Margin="20,0,0,14" Opacity="0.7" TextWrapping="Wrap"
                   Text="On: 0.34 → 0.5.   Off: 0.34 → 0, and 0.5 → 1."/>

        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
            <Button x:Name="RunButton" Content="Run" Width="90" Height="26" Margin="0,0,8,0"
                    IsDefault="True" Click="OnRun"/>
            <Button Content="Cancel" Width="90" Height="26" IsCancel="True"/>
        </StackPanel>
    </StackPanel>
</Window>
```

Create `src/RVTuk.UI/RoundPositions/Views/RoundPositionsWindow.xaml.cs`:

```csharp
using System.Windows;
using RVTuk.UI.RoundPositions.ViewModels;

namespace RVTuk.UI.RoundPositions.Views
{
    public partial class RoundPositionsWindow : Window
    {
        public RoundPositionsWindow(RoundPositionsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private void OnRun(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
```

- [ ] **Step 4: Build both configurations**

```bash
dotnet build RVTuk.sln -c Release2024 && dotnet build RVTuk.sln -c Release2025
```

Expected: both succeed, no warnings from the new files.

- [ ] **Step 5: Commit**

```bash
git add src/RVTuk.Core/Shared/Config/AppConfig.cs src/RVTuk.UI/RoundPositions && git commit -m "feat(round-positions): one checkbox decides the grid step"
```

---

### Task 6: ElementEligibility

**Files:**
- Create: `src/RVTuk.Revit/RoundPositions/ElementEligibility.cs`

**Interfaces:**
- Consumes: `RVTuk.Core.RoundPositions.SkipReason`.
- Produces: `ElementEligibility.ReasonToSkip(Document doc, Element element) → SkipReason?` (null means eligible); `ElementEligibility.CategoryName(Element element) → string`.

There is no unit test here — every line touches a Revit type, which `RVTuk.Core.Tests` cannot
reference. It is covered by the in-Revit checklist in Task 10.

- [ ] **Step 1: Write the implementation**

Create `src/RVTuk.Revit/RoundPositions/ElementEligibility.cs`:

```csharp
using Autodesk.Revit.DB;
using RVTuk.Core.RoundPositions;

namespace RVTuk.Revit.RoundPositions
{
    /// <summary>
    /// Why an element must be left alone, checked before anything is measured. Pinning is
    /// deliberately absent: a pinned element is unpinned, moved and re-pinned rather than skipped.
    /// </summary>
    public static class ElementEligibility
    {
        public static SkipReason? ReasonToSkip(Document doc, Element element)
        {
            if (element is RevitLinkInstance) return SkipReason.InRevitLink;
            if (element is ImportInstance) return SkipReason.ImportedCad;

            if (element.GroupId != ElementId.InvalidElementId) return SkipReason.InGroup;
            if (element.DesignOption != null) return SkipReason.InDesignOption;

            if (doc.IsWorkshared)
            {
                // NotOwned means nobody holds it, which is fine — only another user's checkout
                // actually blocks us.
                var status = WorksharingUtils.GetCheckoutStatus(doc, element.Id);
                if (status == CheckoutStatus.OwnedByOtherUser) return SkipReason.OwnedByOtherUser;
            }

            return null;
        }

        public static string CategoryName(Element element) =>
            element.Category?.Name ?? element.GetType().Name;
    }
}
```

- [ ] **Step 2: Build both configurations**

```bash
dotnet build RVTuk.sln -c Release2024 && dotnet build RVTuk.sln -c Release2025
```

Expected: both succeed.

- [ ] **Step 3: Commit**

```bash
git add src/RVTuk.Revit/RoundPositions && git commit -m "feat(round-positions): decide up front what must not be touched"
```

---

### Task 7: Failure handling and constraint release

**Files:**
- Create: `src/RVTuk.Revit/RoundPositions/RoundingFailurePreprocessor.cs`
- Create: `src/RVTuk.Revit/RoundPositions/ConstraintReleaser.cs`

**Interfaces:**
- Consumes: `RVTuk.Core.RoundPositions.RoundingReportBuilder`.
- Produces: `RoundingFailurePreprocessor : IFailuresPreprocessor`; `ConstraintReleaser.Release(Document doc, Element element, RoundingReportBuilder report) → bool` (true when something was released and a retry is worth attempting).

- [ ] **Step 1: Write the failure preprocessor**

Create `src/RVTuk.Revit/RoundPositions/RoundingFailurePreprocessor.cs`:

```csharp
using Autodesk.Revit.DB;

namespace RVTuk.Revit.RoundPositions
{
    /// <summary>
    /// A model-wide run raises hundreds of "elements are joined but do not intersect"-class
    /// warnings. Without this every one of them stops the run with a dialog.
    /// </summary>
    public class RoundingFailurePreprocessor : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            foreach (var failure in accessor.GetFailureMessages())
            {
                if (failure.GetSeverity() == FailureSeverity.Warning)
                {
                    accessor.DeleteWarning(failure);
                }
            }

            // Errors are left for the caller: the sub-transaction that raised one gets rolled
            // back, which is how an element ends up recorded as skipped rather than half-moved.
            return FailureProcessingResult.Continue;
        }
    }
}
```

- [ ] **Step 2: Write the constraint releaser**

Create `src/RVTuk.Revit/RoundPositions/ConstraintReleaser.cs`:

```csharp
using System.Collections.Generic;
using Autodesk.Revit.DB;
using RVTuk.Core.RoundPositions;

namespace RVTuk.Revit.RoundPositions
{
    /// <summary>
    /// Releases whatever is holding an element in place, but only after a move has already been
    /// refused — never pre-emptively. A locked dimension is unlocked rather than deleted, so the
    /// annotation and its design intent survive and only the constraint goes.
    /// </summary>
    public static class ConstraintReleaser
    {
        public static bool Release(Document doc, Element element, RoundingReportBuilder report)
        {
            var category = ElementEligibility.CategoryName(element);
            var released = false;

            foreach (var id in DependentIds(element, BuiltInCategory.OST_Dimensions))
            {
                if (!(doc.GetElement(id) is Dimension dimension) || !dimension.IsLocked) continue;

                dimension.IsLocked = false;
                report.RecordReleasedConstraint(
                    element.Id.Value, category, $"Unlocked dimension {id.Value}");
                released = true;
            }

            var alignmentIds = DependentIds(element, BuiltInCategory.OST_Constraints);
            foreach (var id in alignmentIds)
            {
                // An alignment lock exists only to constrain, so there is nothing to preserve by
                // keeping it.
                report.RecordReleasedConstraint(
                    element.Id.Value, category, $"Deleted alignment constraint {id.Value}");
                released = true;
            }
            if (alignmentIds.Count > 0) doc.Delete(alignmentIds);

            return released;
        }

        private static IList<ElementId> DependentIds(Element element, BuiltInCategory category) =>
            element.GetDependentElements(new ElementCategoryFilter(category));
    }
}
```

> **Revit 2024 note:** `ElementId.Value` is 2024+. If a compile error appears on `Id.Value`,
> the shared-source pattern used by `RVTuk.Revit/RishuiZamin` wraps this in `#if REVIT2023`;
> this project never builds for 2023, so `.Value` is correct here.

- [ ] **Step 3: Build both configurations**

```bash
dotnet build RVTuk.sln -c Release2024 && dotnet build RVTuk.sln -c Release2025
```

Expected: both succeed.

- [ ] **Step 4: Commit**

```bash
git add src/RVTuk.Revit/RoundPositions && git commit -m "feat(round-positions): unlock what refuses to move, and only then"
```

---

### Task 8: The line pass and the runner (walls only)

**Files:**
- Modify: `src/RVTuk.Revit/RoundPositions/ConstraintReleaser.cs` (Task 7's file — it must return what it released instead of recording it; see Step 0)
- Create: `src/RVTuk.Revit/RoundPositions/MoveAttempt.cs`
- Create: `src/RVTuk.Revit/RoundPositions/LinePass.cs`
- Create: `src/RVTuk.Revit/RoundPositions/RoundPositionsRunner.cs`

**Interfaces:**
- Consumes: `LineOffset`, `LineOffsetOutcome`, `GridPoint`, `GridSnapper`, `AxisClassifier`, `RoundingReportBuilder`, `SkipReason`, `ElementEligibility`, `ConstraintReleaser`, `RoundingFailurePreprocessor`.
- Produces: `LinePass.Run(Document doc, IReadOnlyList<Element> elements, double step, RoundingReportBuilder report)`; `RoundPositionsRunner.Run(Document doc, bool allowHalfCentimetres) → RoundingReport`; `MoveAttempt.TryApply(Document doc, Element element, Action apply, RoundingReportBuilder report) → bool` and `MoveAttempt.TryMove(Document doc, Element element, XYZ translation, RoundingReportBuilder report) → bool` (a thin wrapper over `TryApply`). Later passes that change something other than position — a level's elevation, an instance's rotation — call `TryApply` directly rather than re-implementing the unpin/rollback/retry wrapper.

- [ ] **Step 0: Make the constraint releaser report nothing itself**

As written in Task 7, `ConstraintReleaser.Release` writes to the report the moment it unlocks or
deletes something. That is a lie waiting to happen: the release runs inside a sub-transaction, and
if that sub-transaction rolls back the model reverts while the report entries survive — naming
constraints as released that are still in the model. Since that report is *the only record a
constraint ever existed*, it must never over-claim.

So `Release` returns what it released and records nothing. The caller records only after the
commit succeeds.

In `src/RVTuk.Revit/RoundPositions/ConstraintReleaser.cs`, change the signature to:

```csharp
        public static IReadOnlyList<ReleasedConstraint> Release(Document doc, Element element)
```

Drop the `RoundingReportBuilder` parameter. Replace each `report.RecordReleasedConstraint(...)` call
with adding to a local `var released = new List<ReleasedConstraint>();`, built with the same element
id, category and wording as before, and return it. Drop the `released` bool — the list's emptiness
carries that. `ReleasedConstraint` is `RVTuk.Core.RoundPositions.ReleasedConstraint(long ElementId,
string Category, string Description)`.

Also capture `doc.Delete(alignmentIds)`'s return value and record only the ids it actually deleted:
Revit can delete fewer ids than requested, and the report must not claim the difference.

- [ ] **Step 1: Write the shared move helper**

Five passes move elements; all of them go through this one file, so unpinning, the constraint
retry and the roll-back-on-failure rule are written once.

Create `src/RVTuk.Revit/RoundPositions/MoveAttempt.cs`:

```csharp
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using RVTuk.Core.RoundPositions;

namespace RVTuk.Revit.RoundPositions
{
    /// <summary>
    /// The one place an element is actually moved. A failure rolls its sub-transaction back —
    /// which restores the pin along with the position — then retries once after releasing
    /// whatever was holding it.
    /// </summary>
    public static class MoveAttempt
    {
        /// <summary>
        /// Applies one change to one element under the rules every pass shares: unpin first,
        /// re-pin after, roll back cleanly on failure, then retry once with whatever was holding
        /// the element released. <paramref name="apply"/> is the only part that differs between
        /// passes — a translation, a rotation, or writing a level's elevation.
        /// </summary>
        public static bool TryApply(
            Document doc, Element element, Action apply, RoundingReportBuilder report)
        {
            var wasPinned = element.Pinned;

            if (TryOnce(doc, element, apply, wasPinned)) return true;
            if (!TryRelease(doc, element, report)) return false;

            return TryOnce(doc, element, apply, wasPinned);
        }

        /// <summary>
        /// Releases what is holding the element and commits that on its own, before the retry.
        /// Keeping the two apart is what stops the report claiming a constraint was removed that
        /// a failed retry then rolled back — and the run is still one transaction, so Ctrl+Z
        /// takes the release with it either way.
        /// </summary>
        private static bool TryRelease(Document doc, Element element, RoundingReportBuilder report)
        {
            IReadOnlyList<ReleasedConstraint> released;

            using (var sub = new SubTransaction(doc))
            {
                sub.Start();
                try
                {
                    released = ConstraintReleaser.Release(doc, element);
                    if (released.Count == 0)
                    {
                        sub.RollBack();
                        return false;
                    }

                    sub.Commit();
                }
                catch (Exception)
                {
                    sub.RollBack();
                    return false;
                }
            }

            foreach (var constraint in released)
            {
                report.RecordReleasedConstraint(
                    constraint.ElementId, constraint.Category, constraint.Description);
            }

            return true;
        }

        public static bool TryMove(
            Document doc, Element element, XYZ translation, RoundingReportBuilder report) =>
            TryApply(doc, element,
                () => ElementTransformUtils.MoveElement(doc, element.Id, translation), report);

        private static bool TryOnce(Document doc, Element element, Action apply, bool wasPinned)
        {
            using (var sub = new SubTransaction(doc))
            {
                sub.Start();
                try
                {
                    if (wasPinned) element.Pinned = false;
                    apply();
                    if (wasPinned) element.Pinned = true;
                    sub.Commit();
                    return true;
                }
                catch (Exception)
                {
                    // Rolling back restores the pin along with the position.
                    sub.RollBack();
                    return false;
                }
            }
        }
    }
}
```

- [ ] **Step 2: Write the line pass**

Create `src/RVTuk.Revit/RoundPositions/LinePass.cs`:

```csharp
using System.Collections.Generic;
using Autodesk.Revit.DB;
using RVTuk.Core.RoundPositions;

namespace RVTuk.Revit.RoundPositions
{
    /// <summary>
    /// Straight-line elements — walls, framing, model and detail lines. Only exactly-orthogonal
    /// ones move, and they slide parallel to themselves.
    /// </summary>
    public static class LinePass
    {
        public static void Run(
            Document doc, IReadOnlyList<Element> elements, double step, RoundingReportBuilder report)
        {
            foreach (var element in elements)
            {
                report.CountExamined();

                var skip = ElementEligibility.ReasonToSkip(doc, element);
                if (skip.HasValue)
                {
                    report.RecordSkipped(element.Id.Value, ElementEligibility.CategoryName(element), skip.Value);
                    continue;
                }

                if (!(element.Location is LocationCurve locationCurve) ||
                    !(locationCurve.Curve is Line line))
                {
                    report.RecordSkipped(
                        element.Id.Value, ElementEligibility.CategoryName(element), SkipReason.NotOrthogonal);
                    continue;
                }

                var start = ToGridPoint(line.GetEndPoint(0));
                var end = ToGridPoint(line.GetEndPoint(1));
                var offset = LineOffset.For(start, end, step, AxisClassifier.ToleranceCentimetres);

                switch (offset.Outcome)
                {
                    case LineOffsetOutcome.AlreadyOnGrid:
                        report.RecordAlreadyClean();
                        break;

                    case LineOffsetOutcome.NotOrthogonal:
                        report.RecordSkipped(
                            element.Id.Value, ElementEligibility.CategoryName(element), SkipReason.NotOrthogonal);
                        break;

                    default:
                        var translation = new XYZ(
                            GridSnapper.ToFeet(offset.DeltaX), GridSnapper.ToFeet(offset.DeltaY), 0);

                        if (MoveAttempt.TryMove(doc, element, translation, report))
                        {
                            report.RecordMoved();
                        }
                        else
                        {
                            report.RecordSkipped(
                                element.Id.Value, ElementEligibility.CategoryName(element), SkipReason.RevitRejected);
                        }
                        break;
                }
            }
        }

        private static GridPoint ToGridPoint(XYZ point) => new GridPoint(
            GridSnapper.FromFeet(point.X), GridSnapper.FromFeet(point.Y), GridSnapper.FromFeet(point.Z));
    }
}
```

- [ ] **Step 3: Write the runner**

Create `src/RVTuk.Revit/RoundPositions/RoundPositionsRunner.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RVTuk.Core.RoundPositions;

namespace RVTuk.Revit.RoundPositions
{
    /// <summary>
    /// One transaction for the whole run, so the cleanup is a single Ctrl+Z, with a
    /// sub-transaction per element that actually needs to move.
    /// </summary>
    public static class RoundPositionsRunner
    {
        public static RoundingReport Run(Document doc, bool allowHalfCentimetres)
        {
            var step = GridSnapper.StepFor(allowHalfCentimetres);
            var report = new RoundingReportBuilder();

            using (var transaction = new Transaction(doc, "Round Positions"))
            {
                transaction.Start();

                var options = transaction.GetFailureHandlingOptions();
                options.SetForcedModalHandling(false);
                options.SetClearAfterRollback(true);
                options.SetFailuresPreprocessor(new RoundingFailurePreprocessor());
                transaction.SetFailureHandlingOptions(options);

                LinePass.Run(doc, Collect(doc, BuiltInCategory.OST_Walls), step, report);

                transaction.Commit();
            }

            return report.Build();
        }

        private static IReadOnlyList<Element> Collect(Document doc, BuiltInCategory category) =>
            new FilteredElementCollector(doc)
                .OfCategory(category)
                .WhereElementIsNotElementType()
                .ToList();
    }
}
```

- [ ] **Step 4: Build both configurations**

```bash
dotnet build RVTuk.sln -c Release2024 && dotnet build RVTuk.sln -c Release2025
```

Expected: both succeed.

- [ ] **Step 5: Commit**

```bash
git add src/RVTuk.Revit/RoundPositions && git commit -m "feat(round-positions): walls slide onto the grid, in one undoable transaction"
```

---

### Task 9: The results window

**Files:**
- Create: `src/RVTuk.UI/RoundPositions/ViewModels/RoundPositionsResultViewModel.cs`
- Create: `src/RVTuk.UI/RoundPositions/Views/RoundPositionsResultWindow.xaml`
- Create: `src/RVTuk.UI/RoundPositions/Views/RoundPositionsResultWindow.xaml.cs`

**Interfaces:**
- Consumes: `RoundingReport`, `SkipGroup`, `ReleasedConstraint`, `ViewModelBase`.
- Produces: `RoundPositionsResultViewModel(RoundingReport report, Action<IReadOnlyList<long>> selectInModel)` exposing `Summary`, `Groups`, `ReleasedConstraints`, `HasReleasedConstraints`, `SelectGroupCommand`; `RoundPositionsResultWindow(RoundPositionsResultViewModel vm)`.

- [ ] **Step 1: Write the view model**

Create `src/RVTuk.UI/RoundPositions/ViewModels/RoundPositionsResultViewModel.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using RVTuk.Core.RoundPositions;
using RVTuk.UI.Shared.ViewModels;

namespace RVTuk.UI.RoundPositions.ViewModels
{
    public class RoundPositionsResultViewModel : ViewModelBase
    {
        private readonly Action<IReadOnlyList<long>> _selectInModel;

        public RoundPositionsResultViewModel(
            RoundingReport report, Action<IReadOnlyList<long>> selectInModel)
        {
            _selectInModel = selectInModel;

            Summary =
                $"Examined {report.Examined}   ·   Moved {report.Moved}   ·   " +
                $"Already clean {report.AlreadyClean}   ·   Skipped {report.Skipped.Count}";

            Groups = report.GroupedByReason();
            ReleasedConstraints = report.ReleasedConstraints;
            SelectGroupCommand = new RelayCommand<SkipGroup>(SelectGroup);
        }

        public string Summary { get; }

        public IReadOnlyList<SkipGroup> Groups { get; }

        public IReadOnlyList<ReleasedConstraint> ReleasedConstraints { get; }

        public bool HasReleasedConstraints => ReleasedConstraints.Count > 0;

        public ICommand SelectGroupCommand { get; }

        private void SelectGroup(SkipGroup? group)
        {
            if (group == null) return;
            _selectInModel(group.Elements.Select(e => e.ElementId).ToList());
        }
    }
}
```

> Check `src/RVTuk.UI/Shared/ViewModels/RelayCommandT.cs` for the generic command's exact
> constructor signature and namespace before wiring `SelectGroupCommand`; match it rather than
> assuming the shape above.

- [ ] **Step 2: Write the window**

Create `src/RVTuk.UI/RoundPositions/Views/RoundPositionsResultWindow.xaml`:

```xml
<Window x:Class="RVTuk.UI.RoundPositions.Views.RoundPositionsResultWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="RVTuk — Round Positions: results"
        Width="620" Height="520" MinWidth="480" MinHeight="360"
        WindowStartupLocation="CenterScreen" ShowInTaskbar="False">

    <Window.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="/RVTuk.UI;component/Shared/Themes/DarkTheme.xaml"/>
            </ResourceDictionary.MergedDictionaries>
            <BooleanToVisibilityConverter x:Key="BoolVis"/>
        </ResourceDictionary>
    </Window.Resources>

    <Window.Background><StaticResource ResourceKey="Brush.Bg"/></Window.Background>
    <Window.Foreground><StaticResource ResourceKey="Brush.Text"/></Window.Foreground>

    <Grid Margin="12">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <TextBlock Grid.Row="0" Text="{Binding Summary}" FontWeight="SemiBold" Margin="0,0,0,10"/>

        <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto">
            <ItemsControl ItemsSource="{Binding Groups}">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <Border BorderBrush="{StaticResource Brush.Border}" BorderThickness="1"
                                Background="{StaticResource Brush.Panel}"
                                CornerRadius="3" Padding="10,8" Margin="0,0,0,8">
                            <StackPanel>
                                <DockPanel Margin="0,0,0,4">
                                    <Button DockPanel.Dock="Right" Content="Select in model"
                                            Padding="8,2"
                                            Command="{Binding DataContext.SelectGroupCommand,
                                                      RelativeSource={RelativeSource AncestorType=Window}}"
                                            CommandParameter="{Binding}"/>
                                    <TextBlock Text="{Binding Description}" FontWeight="SemiBold"
                                               TextWrapping="Wrap"/>
                                </DockPanel>
                                <TextBlock Opacity="0.7">
                                    <Run Text="{Binding Elements.Count, Mode=OneWay}"/>
                                    <Run Text="element(s)"/>
                                </TextBlock>
                            </StackPanel>
                        </Border>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </ScrollViewer>

        <Expander Grid.Row="2" Margin="0,8,0,0"
                  Visibility="{Binding HasReleasedConstraints, Converter={StaticResource BoolVis}}"
                  Header="Constraints released">
            <ItemsControl ItemsSource="{Binding ReleasedConstraints}" Margin="0,6,0,0">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <TextBlock Opacity="0.8" Margin="0,1">
                            <Run Text="{Binding Category, Mode=OneWay}"/>
                            <Run Text="—"/>
                            <Run Text="{Binding Description, Mode=OneWay}"/>
                        </TextBlock>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </Expander>

        <Button Grid.Row="3" Content="Close" Width="90" Height="26" Margin="0,12,0,0"
                HorizontalAlignment="Right" IsCancel="True" IsDefault="True"/>
    </Grid>
</Window>
```

Create `src/RVTuk.UI/RoundPositions/Views/RoundPositionsResultWindow.xaml.cs`:

```csharp
using System.Windows;
using RVTuk.UI.RoundPositions.ViewModels;

namespace RVTuk.UI.RoundPositions.Views
{
    public partial class RoundPositionsResultWindow : Window
    {
        public RoundPositionsResultWindow(RoundPositionsResultViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
```

- [ ] **Step 3: Build both configurations**

```bash
dotnet build RVTuk.sln -c Release2024 && dotnet build RVTuk.sln -c Release2025
```

Expected: both succeed.

- [ ] **Step 4: Commit**

```bash
git add src/RVTuk.UI/RoundPositions && git commit -m "feat(round-positions): the skip list is the product, so show it properly"
```

---

### Task 10: The command, the ribbon button, and the first in-Revit run

**Files:**
- Create: `src/RVTuk.Revit/RoundPositions/Commands/RoundPositionsCommand.cs`
- Modify: `src/RVTuk.Revit/Application.cs` (add `using`, add the button in `CreateRibbon` after the DWG button, add the icon factory next to `CreateDwgExportIcon`)

**Interfaces:**
- Consumes: `RoundPositionsRunner.Run`, `RoundPositionsViewModel`, `RoundPositionsWindow`, `RoundPositionsResultViewModel`, `RoundPositionsResultWindow`, `ConfigManager`.
- Produces: `RoundPositionsCommand : IExternalCommand`.

- [ ] **Step 1: Write the command**

Create `src/RVTuk.Revit/RoundPositions/Commands/RoundPositionsCommand.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RVTuk.Core.Shared.Config;
using RVTuk.UI.RoundPositions.ViewModels;
using RVTuk.UI.RoundPositions.Views;

namespace RVTuk.Revit.RoundPositions.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class RoundPositionsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            // Revit never creates a WPF Application; make one we own (never auto-shutdown).
            if (System.Windows.Application.Current == null)
            {
                new System.Windows.Application
                {
                    ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown
                };
            }

            var uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null) return Result.Cancelled;
            var doc = uidoc.Document;

            try
            {
                var config = ConfigManager.Load();
                var viewModel = new RoundPositionsViewModel
                {
                    AllowHalfCentimetres = !config.RoundPositionsWholeCentimetresOnly,
                };

                var dialog = new RoundPositionsWindow(viewModel);
                new System.Windows.Interop.WindowInteropHelper(dialog)
                {
                    Owner = commandData.Application.MainWindowHandle,
                };
                if (dialog.ShowDialog() != true) return Result.Cancelled;

                config.RoundPositionsWholeCentimetresOnly = !viewModel.AllowHalfCentimetres;
                ConfigManager.Save(config);

                var report = RoundPositionsRunner.Run(doc, viewModel.AllowHalfCentimetres);

                Action<IReadOnlyList<long>> selectInModel = ids =>
                    uidoc.Selection.SetElementIds(ids.Select(id => new ElementId(id)).ToList());

                var resultWindow = new RoundPositionsResultWindow(
                    new RoundPositionsResultViewModel(report, selectInModel));
                new System.Windows.Interop.WindowInteropHelper(resultWindow)
                {
                    Owner = commandData.Application.MainWindowHandle,
                };
                resultWindow.ShowDialog();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("RVTuk — Round Positions", $"The run failed: {ex.Message}");
                return Result.Failed;
            }
        }
    }
}
```

> Check `ConfigManager`'s actual `Load`/`Save` signatures in
> `src/RVTuk.Core/Shared/Config/ConfigManager.cs` and match them; the names above are the
> expected shape, not a verified one.

- [ ] **Step 2: Register the ribbon button**

In `src/RVTuk.Revit/Application.cs`, add near the other tool usings (after line 15):

```csharp
using RVTuk.Revit.RoundPositions.Commands;
```

In `CreateRibbon`, immediately after `panel.AddItem(dwgBtn);`:

```csharp
            var roundBtn = new PushButtonData(
                "RoundPositions",
                "Round\nPositions",
                assemblyPath,
                typeof(RoundPositionsCommand).FullName!)
            {
                ToolTip = "Move every eligible element onto a whole-centimetre grid (lengths and angles are never changed)"
            };
            roundBtn.LargeImage = CreateRoundPositionsIcon(32);
            roundBtn.Image      = CreateRoundPositionsIcon(16);

            panel.AddItem(roundBtn);
```

Add the icon factory next to `CreateDwgExportIcon`:

```csharp
        private static BitmapSource CreateRoundPositionsIcon(int size)
        {
            var dv = new DrawingVisual();
            using (var ctx = dv.RenderOpen())
            {
                double s = size;
                ctx.DrawRectangle(new SolidColorBrush(WpfColor.FromRgb(0x25, 0x25, 0x26)), null,
                    new Rect(0, 0, s, s));

                // A faint grid with one element snapped onto an intersection.
                var gridPen = new Pen(new SolidColorBrush(WpfColor.FromRgb(0x5A, 0x5A, 0x5A)),
                    Math.Max(1, s * 0.04));
                gridPen.Freeze();
                foreach (var f in new[] { 0.30, 0.55, 0.80 })
                {
                    ctx.DrawLine(gridPen, new WpfPoint(s * 0.12, s * f), new WpfPoint(s * 0.88, s * f));
                    ctx.DrawLine(gridPen, new WpfPoint(s * f, s * 0.12), new WpfPoint(s * f, s * 0.88));
                }

                var arrowPen = new Pen(new SolidColorBrush(WpfColor.FromRgb(0xFF, 0x8C, 0x00)),
                    Math.Max(1, s * 0.07));
                arrowPen.Freeze();
                ctx.DrawLine(arrowPen, new WpfPoint(s * 0.72, s * 0.24), new WpfPoint(s * 0.58, s * 0.38));
                ctx.DrawLine(arrowPen, new WpfPoint(s * 0.58, s * 0.38), new WpfPoint(s * 0.58, s * 0.28));
                ctx.DrawLine(arrowPen, new WpfPoint(s * 0.58, s * 0.38), new WpfPoint(s * 0.68, s * 0.38));

                ctx.DrawEllipse(new SolidColorBrush(WpfColor.FromRgb(0xFF, 0x8C, 0x00)), null,
                    new WpfPoint(s * 0.55, s * 0.55), s * 0.09, s * 0.09);
            }
            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            return bmp;
        }
```

- [ ] **Step 3: Build and deploy**

```bash
dotnet build RVTuk.sln -c Release2024 && dotnet build RVTuk.sln -c Release2025
```

Then, in an **elevated** PowerShell with Revit closed:

```powershell
.\Deploy.ps1 2024
```

- [ ] **Step 4: In-Revit checklist (user)**

Open a **copy** of a real model — never the original, this is the first run.

- [ ] The "Round Positions" button appears on the RVTuk panel with its icon.
- [ ] Clicking it opens the dialog; the checkbox defaults to on; Cancel does nothing to the model.
- [ ] Run with half centimetres **on**: orthogonal walls land on 0.5 cm positions; wall *lengths* are unchanged.
- [ ] Run with half centimetres **off** on a fresh copy: walls land on whole centimetres.
- [ ] **Ctrl+Z once** undoes the entire run.
- [ ] A wall at 89.97° appears in the skip list under "Not orthogonal", and has *not* moved.
- [ ] **Joined walls stay joined** — check a few corners for gaps. Note what happens; this is the assumption most likely to be wrong.
- [ ] No Revit warning dialog interrupts the run.
- [ ] A pinned wall moves and is **still pinned** afterwards.
- [ ] A wall locked to a grid by a dimension: it moves, and the "Constraints released" list names the dimension. Confirm the dimension still exists and is merely unlocked.
- [ ] Walls in a group are skipped and listed; walls in a linked model are untouched.
- [ ] "Select in model" on a skip group selects those elements.
- [ ] **Time the run** on the largest model to hand and write the number in the backlog — this is the speed risk from the spec.

- [ ] **Step 5: Commit**

```bash
git add src/RVTuk.Revit && git commit -m "feat(round-positions): a button that rounds walls, end to end"
```

---

## Phase 3 — the remaining passes

Each task here is small because the machinery already exists. Each adds one collection to the
runner and, where the API differs, one pass.

### Task 11: Framing, model lines and detail lines

**Files:**
- Modify: `src/RVTuk.Revit/RoundPositions/RoundPositionsRunner.cs`

**Interfaces:**
- Consumes: `LinePass.Run` unchanged.
- Produces: no new public members.

These three categories all expose a `LocationCurve`, so `LinePass` already handles them — only the
collection changes.

- [ ] **Step 1: Extend the runner**

In `RoundPositionsRunner.Run`, replace the single `LinePass.Run(...)` call with:

```csharp
                LinePass.Run(doc, Collect(doc, BuiltInCategory.OST_Walls), step, report);
                LinePass.Run(doc, Collect(doc, BuiltInCategory.OST_StructuralFraming), step, report);
                LinePass.Run(doc, CollectCurveElements(doc), step, report);
```

And add alongside `Collect`:

```csharp
        /// <summary>
        /// Model and detail lines. This includes the _DP-Dim and _DP-Topo styles that Auto
        /// Dimensions and Topo Tools read as input — cleaner input for both, but it does mean a
        /// run should be followed by a re-run of those tools where they are in use.
        /// </summary>
        private static IReadOnlyList<Element> CollectCurveElements(Document doc) =>
            new FilteredElementCollector(doc)
                .OfClass(typeof(CurveElement))
                .WhereElementIsNotElementType()
                .ToList();
```

- [ ] **Step 2: Build both configurations**

```bash
dotnet build RVTuk.sln -c Release2024 && dotnet build RVTuk.sln -c Release2025
```

Expected: both succeed.

- [ ] **Step 3: In-Revit check (user)**

- [ ] An orthogonal beam moves onto the grid; a diagonal brace is skipped as "Not orthogonal".
- [ ] A model line moves; a detail line in a plan view moves **within its view** and does not change its sketch plane or elevation.

- [ ] **Step 4: Commit**

```bash
git add src/RVTuk.Revit/RoundPositions && git commit -m "feat(round-positions): beams and drafting lines take the same path as walls"
```

---

### Task 12: Levels

**Files:**
- Create: `src/RVTuk.Revit/RoundPositions/LevelPass.cs`
- Modify: `src/RVTuk.Revit/RoundPositions/RoundPositionsRunner.cs`

**Interfaces:**
- Consumes: `GridSnapper`, `RoundingReportBuilder`, `ElementEligibility`, `MoveAttempt.TryApply`.
- Produces: `LevelPass.Run(Document doc, double step, RoundingReportBuilder report)`.

A level is moved by writing `Elevation`, not by transforming it, so it needs its own pass.

- [ ] **Step 1: Write the pass**

Create `src/RVTuk.Revit/RoundPositions/LevelPass.cs`:

```csharp
using System;
using System.Linq;
using Autodesk.Revit.DB;
using RVTuk.Core.RoundPositions;

namespace RVTuk.Revit.RoundPositions
{
    /// <summary>
    /// The largest single change the tool makes: every wall, floor and family hosted on a level
    /// rides along with it. A level moves by having its elevation written, not by being
    /// transformed, so it cannot share the line pass's machinery.
    /// </summary>
    public static class LevelPass
    {
        public static void Run(Document doc, double step, RoundingReportBuilder report)
        {
            var levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .ToList();

            foreach (var level in levels)
            {
                report.CountExamined();

                var skip = ElementEligibility.ReasonToSkip(doc, level);
                if (skip.HasValue)
                {
                    report.RecordSkipped(level.Id.Value, ElementEligibility.CategoryName(level), skip.Value);
                    continue;
                }

                var current = GridSnapper.FromFeet(level.Elevation);
                var snapped = GridSnapper.Snap(current, step);
                if (Math.Abs(snapped - current) <= GridSnapper.EpsilonCentimetres)
                {
                    report.RecordAlreadyClean();
                    continue;
                }

                var elevationFeet = GridSnapper.ToFeet(snapped);
                if (MoveAttempt.TryApply(doc, level, () => level.Elevation = elevationFeet, report))
                {
                    report.RecordMoved();
                }
                else
                {
                    report.RecordSkipped(
                        level.Id.Value, ElementEligibility.CategoryName(level), SkipReason.RevitRejected);
                }
            }
        }
    }
}
```

- [ ] **Step 2: Call it first in the runner**

In `RoundPositionsRunner.Run`, immediately before the first `LinePass.Run(...)`:

```csharp
                LevelPass.Run(doc, step, report);
```

- [ ] **Step 3: Build both configurations**

```bash
dotnet build RVTuk.sln -c Release2024 && dotnet build RVTuk.sln -c Release2025
```

Expected: both succeed.

- [ ] **Step 4: In-Revit check (user)**

On a copy of a real model with a level at a non-round elevation:

- [ ] The level's elevation becomes round.
- [ ] **Everything hosted on it moved with it** — walls, floors, families. Confirm in a section view.
- [ ] Elements with an explicit offset from that level kept their offset.
- [ ] Ctrl+Z restores the level *and* everything that rode along, in one step.

- [ ] **Step 5: Commit**

```bash
git add src/RVTuk.Revit/RoundPositions && git commit -m "feat(round-positions): levels land on the grid, and the building follows"
```

---

### Task 13: Grids and reference planes

**Files:**
- Create: `src/RVTuk.Revit/RoundPositions/DatumPass.cs`
- Modify: `src/RVTuk.Revit/RoundPositions/RoundPositionsRunner.cs`

**Interfaces:**
- Consumes: `LineOffset`, `GridSnapper`, `AxisClassifier`, `MoveAttempt`, `ElementEligibility`, `RoundingReportBuilder`.
- Produces: `DatumPass.Run(Document doc, double step, RoundingReportBuilder report)`.

`Grid.Curve` and `ReferencePlane`'s ends are read-only — both move by transform, which is why they
cannot go through `LinePass`.

- [ ] **Step 1: Write the pass**

Create `src/RVTuk.Revit/RoundPositions/DatumPass.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RVTuk.Core.RoundPositions;

namespace RVTuk.Revit.RoundPositions
{
    /// <summary>
    /// Grids and reference planes. Their geometry is read-only — unlike a wall you cannot assign
    /// a new curve — so the offset Core computes is applied as a translation instead.
    /// </summary>
    public static class DatumPass
    {
        public static void Run(Document doc, double step, RoundingReportBuilder report)
        {
            foreach (var element in Collect(doc))
            {
                report.CountExamined();

                var skip = ElementEligibility.ReasonToSkip(doc, element);
                if (skip.HasValue)
                {
                    report.RecordSkipped(element.Id.Value, ElementEligibility.CategoryName(element), skip.Value);
                    continue;
                }

                if (!TryEnds(element, out var start, out var end))
                {
                    report.RecordSkipped(
                        element.Id.Value, ElementEligibility.CategoryName(element), SkipReason.NotOrthogonal);
                    continue;
                }

                var offset = LineOffset.For(start, end, step, AxisClassifier.ToleranceCentimetres);
                switch (offset.Outcome)
                {
                    case LineOffsetOutcome.AlreadyOnGrid:
                        report.RecordAlreadyClean();
                        break;

                    case LineOffsetOutcome.NotOrthogonal:
                        report.RecordSkipped(
                            element.Id.Value, ElementEligibility.CategoryName(element), SkipReason.NotOrthogonal);
                        break;

                    default:
                        var translation = new XYZ(
                            GridSnapper.ToFeet(offset.DeltaX), GridSnapper.ToFeet(offset.DeltaY), 0);

                        if (MoveAttempt.TryMove(doc, element, translation, report))
                        {
                            report.RecordMoved();
                        }
                        else
                        {
                            report.RecordSkipped(
                                element.Id.Value, ElementEligibility.CategoryName(element), SkipReason.RevitRejected);
                        }
                        break;
                }
            }
        }

        private static bool TryEnds(Element element, out GridPoint start, out GridPoint end)
        {
            start = default;
            end = default;

            switch (element)
            {
                case Grid grid when grid.Curve is Line line:
                    start = ToGridPoint(line.GetEndPoint(0));
                    end = ToGridPoint(line.GetEndPoint(1));
                    return true;

                case ReferencePlane plane:
                    start = ToGridPoint(plane.BubbleEnd);
                    end = ToGridPoint(plane.FreeEnd);
                    return true;

                default:
                    return false;
            }
        }

        private static IReadOnlyList<Element> Collect(Document doc)
        {
            var grids = new FilteredElementCollector(doc)
                .OfClass(typeof(Grid))
                .WhereElementIsNotElementType()
                .ToList();

            var planes = new FilteredElementCollector(doc)
                .OfClass(typeof(ReferencePlane))
                .WhereElementIsNotElementType()
                .ToList();

            return grids.Concat(planes).ToList();
        }

        private static GridPoint ToGridPoint(XYZ point) => new GridPoint(
            GridSnapper.FromFeet(point.X), GridSnapper.FromFeet(point.Y), GridSnapper.FromFeet(point.Z));
    }
}
```

- [ ] **Step 2: Call it in the runner**

In `RoundPositionsRunner.Run`, between `LevelPass.Run(...)` and the first `LinePass.Run(...)`:

```csharp
                DatumPass.Run(doc, step, report);
```

- [ ] **Step 3: Build both configurations**

```bash
dotnet build RVTuk.sln -c Release2024 && dotnet build RVTuk.sln -c Release2025
```

Expected: both succeed.

- [ ] **Step 4: In-Revit check (user)**

- [ ] An orthogonal grid line at a non-round position moves onto the grid, and its bubbles and 3D extents survive.
- [ ] A reference plane moves.
- [ ] Elements *hosted on* a reference plane move with it — note whether they do; this affects whether the pass should run before or after the families.
- [ ] A diagonal grid line is skipped.

- [ ] **Step 5: Commit**

```bash
git add src/RVTuk.Revit/RoundPositions && git commit -m "feat(round-positions): datums move by transform, not by curve"
```

---

### Task 14: Point-positioned families

**Files:**
- Create: `src/RVTuk.Revit/RoundPositions/PointInstancePass.cs`
- Modify: `src/RVTuk.Revit/RoundPositions/RoundPositionsRunner.cs`

**Interfaces:**
- Consumes: `GridPoint`, `GridSnapper`, `MoveAttempt`, `ElementEligibility`, `RoundingReportBuilder`.
- Produces: `PointInstancePass.Run(Document doc, double step, RoundingReportBuilder report)`.

- [ ] **Step 1: Write the pass**

Create `src/RVTuk.Revit/RoundPositions/PointInstancePass.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RVTuk.Core.RoundPositions;

namespace RVTuk.Revit.RoundPositions
{
    /// <summary>
    /// Unhosted family instances with a LocationPoint. Hosted ones (doors, windows) are handled
    /// by HostedInstancePass, which slides them along their wall instead.
    /// </summary>
    public static class PointInstancePass
    {
        /// <summary>Within half a degree of a multiple of 45° counts as "meant to be square".
        /// A family deliberately placed at 37.4° is left alone.</summary>
        public const double RotationToleranceDegrees = 0.5;

        public static void Run(Document doc, double step, RoundingReportBuilder report)
        {
            var instances = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .Where(fi => fi.Host == null && fi.Location is LocationPoint)
                .ToList();

            foreach (var instance in instances)
            {
                report.CountExamined();

                var skip = ElementEligibility.ReasonToSkip(doc, instance);
                if (skip.HasValue)
                {
                    report.RecordSkipped(instance.Id.Value, ElementEligibility.CategoryName(instance), skip.Value);
                    continue;
                }

                var location = (LocationPoint)instance.Location;
                var current = new GridPoint(
                    GridSnapper.FromFeet(location.Point.X),
                    GridSnapper.FromFeet(location.Point.Y),
                    GridSnapper.FromFeet(location.Point.Z));
                var snapped = GridSnapper.Snap(current, step);

                var translation = new XYZ(
                    GridSnapper.ToFeet(snapped.X - current.X),
                    GridSnapper.ToFeet(snapped.Y - current.Y),
                    GridSnapper.ToFeet(snapped.Z - current.Z));

                var rotation = RotationCorrection(location.Rotation);
                var needsMove = translation.GetLength() > GridSnapper.ToFeet(GridSnapper.EpsilonCentimetres);
                var needsRotation = Math.Abs(rotation) > 1e-12;

                if (!needsMove && !needsRotation)
                {
                    report.RecordAlreadyClean();
                    continue;
                }

                var translated = false;
                if (needsMove)
                {
                    translated = MoveAttempt.TryMove(doc, instance, translation, report);
                }

                // A family that could not be translated is being held by something; rotating it
                // around a position we failed to correct would only obscure what went wrong.
                var rotated = false;
                if (needsRotation && (!needsMove || translated))
                {
                    var point = ((LocationPoint)instance.Location).Point;
                    var axis = Line.CreateBound(point, point + XYZ.BasisZ);
                    rotated = MoveAttempt.TryApply(doc, instance,
                        () => ElementTransformUtils.RotateElement(doc, instance.Id, axis, rotation),
                        report);
                }

                // Translation and rotation are independent: a family that moved but could not be
                // rotated has still been moved, and calling it skipped would both misreport it
                // and stop the counts adding up.
                if (translated || rotated)
                {
                    report.RecordMoved();
                }
                else
                {
                    report.RecordSkipped(
                        instance.Id.Value, ElementEligibility.CategoryName(instance), SkipReason.RevitRejected);
                }
            }
        }

        /// <summary>The angle to add to bring the instance onto the nearest multiple of 45°, or 0
        /// when it is further away than the tolerance.</summary>
        private static double RotationCorrection(double currentRadians)
        {
            const double quarterTurn = Math.PI / 4.0;
            var nearest = Math.Round(currentRadians / quarterTurn, MidpointRounding.AwayFromZero) * quarterTurn;
            var correction = nearest - currentRadians;
            var toleranceRadians = RotationToleranceDegrees * Math.PI / 180.0;
            return Math.Abs(correction) <= toleranceRadians ? correction : 0.0;
        }
    }
}
```

- [ ] **Step 2: Call it in the runner**

In `RoundPositionsRunner.Run`, after the `LinePass.Run(...)` calls:

```csharp
                PointInstancePass.Run(doc, step, report);
```

- [ ] **Step 3: Build both configurations**

```bash
dotnet build RVTuk.sln -c Release2024 && dotnet build RVTuk.sln -c Release2025
```

Expected: both succeed.

- [ ] **Step 4: In-Revit check (user)**

- [ ] A piece of furniture at 1.001, 5.001 lands at 1, 5.
- [ ] A column at 44.7° snaps to 45°; one at 37.4° keeps its angle.
- [ ] A family's Z lands on the grid, and it stays on its level.
- [ ] A pinned family moves and stays pinned; a grouped one is skipped and listed.

- [ ] **Step 5: Commit**

```bash
git add src/RVTuk.Revit/RoundPositions && git commit -m "feat(round-positions): point families land on the grid and square up"
```

---

### Task 15: Wall-hosted families

**Files:**
- Create: `src/RVTuk.Revit/RoundPositions/HostedInstancePass.cs`
- Modify: `src/RVTuk.Revit/RoundPositions/LinePass.cs` (record which walls were resolved)
- Modify: `src/RVTuk.Revit/RoundPositions/RoundPositionsRunner.cs`

**Interfaces:**
- Consumes: `GridSnapper`, `MoveAttempt`, `ElementEligibility`, `RoundingReportBuilder`.
- Produces: `LinePass.Run(Document doc, IReadOnlyList<Element> elements, double step, RoundingReportBuilder report, ISet<long>? resolvedIds = null)`; `HostedInstancePass.Run(Document doc, double step, ISet<long> resolvedWallIds, RoundingReportBuilder report)`.

- [ ] **Step 1: Let the line pass record what it resolved**

In `LinePass.Run`, change the signature and record successes:

```csharp
        public static void Run(
            Document doc, IReadOnlyList<Element> elements, double step,
            RoundingReportBuilder report, ISet<long>? resolvedIds = null)
```

Then, in the `AlreadyOnGrid` case, after `report.RecordAlreadyClean();`:

```csharp
                        resolvedIds?.Add(element.Id.Value);
```

And in the `default` case, inside the `if (MoveAttempt.TryMove(...))` branch, after `report.RecordMoved();`:

```csharp
                            resolvedIds?.Add(element.Id.Value);
```

- [ ] **Step 2: Write the pass**

Create `src/RVTuk.Revit/RoundPositions/HostedInstancePass.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RVTuk.Core.RoundPositions;

namespace RVTuk.Revit.RoundPositions
{
    /// <summary>
    /// Doors and windows. They are never moved in X/Y independently — on any wall that is not
    /// axis-aligned that would push them out of their host. Instead each slides along its own
    /// wall, and only if that wall was itself resolved by the line pass.
    /// </summary>
    public static class HostedInstancePass
    {
        public static void Run(
            Document doc, double step, ISet<long> resolvedWallIds, RoundingReportBuilder report)
        {
            var instances = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .Where(fi => fi.Host is Wall && fi.Location is LocationPoint)
                .ToList();

            foreach (var instance in instances)
            {
                report.CountExamined();

                var skip = ElementEligibility.ReasonToSkip(doc, instance);
                if (skip.HasValue)
                {
                    report.RecordSkipped(instance.Id.Value, ElementEligibility.CategoryName(instance), skip.Value);
                    continue;
                }

                var wall = (Wall)instance.Host;
                if (!resolvedWallIds.Contains(wall.Id.Value))
                {
                    // Its wall was skipped, so sliding the opening along it would only move a
                    // door inside a crooked wall.
                    report.RecordSkipped(
                        instance.Id.Value, ElementEligibility.CategoryName(instance), SkipReason.NotOrthogonal);
                    continue;
                }

                if (!(wall.Location is LocationCurve locationCurve) || !(locationCurve.Curve is Line line))
                {
                    report.RecordSkipped(
                        instance.Id.Value, ElementEligibility.CategoryName(instance), SkipReason.NotOrthogonal);
                    continue;
                }

                var origin = line.GetEndPoint(0);
                var direction = (line.GetEndPoint(1) - origin).Normalize();
                var point = ((LocationPoint)instance.Location).Point;

                var alongFeet = (point - origin).DotProduct(direction);
                var alongCm = GridSnapper.FromFeet(alongFeet);
                var snappedCm = GridSnapper.Snap(alongCm, step);

                var shiftCm = snappedCm - alongCm;
                if (Math.Abs(shiftCm) <= GridSnapper.EpsilonCentimetres)
                {
                    report.RecordAlreadyClean();
                    continue;
                }

                var translation = direction.Multiply(GridSnapper.ToFeet(shiftCm));

                if (MoveAttempt.TryMove(doc, instance, translation, report))
                {
                    report.RecordMoved();
                }
                else
                {
                    report.RecordSkipped(
                        instance.Id.Value, ElementEligibility.CategoryName(instance), SkipReason.RevitRejected);
                }
            }
        }
    }
}
```

- [ ] **Step 3: Wire it into the runner**

In `RoundPositionsRunner.Run`, replace the wall line with a tracked one and add the hosted pass last:

```csharp
                var resolvedWallIds = new HashSet<long>();
                LinePass.Run(doc, Collect(doc, BuiltInCategory.OST_Walls), step, report, resolvedWallIds);
                LinePass.Run(doc, Collect(doc, BuiltInCategory.OST_StructuralFraming), step, report);
                LinePass.Run(doc, CollectCurveElements(doc), step, report);
                PointInstancePass.Run(doc, step, report);
                HostedInstancePass.Run(doc, step, resolvedWallIds, report);
```

Add `using System.Collections.Generic;` if it is not already present.

- [ ] **Step 4: Build both configurations**

```bash
dotnet build RVTuk.sln -c Release2024 && dotnet build RVTuk.sln -c Release2025
```

Expected: both succeed.

- [ ] **Step 5: In-Revit check (user)**

- [ ] A door in an orthogonal wall slides so its distance from the wall's start is round, and it **stays in the wall**.
- [ ] A door in a skipped (crooked) wall is itself skipped and listed.
- [ ] A window whose position is locked by a dimension: it moves, and the dimension appears in "Constraints released".

- [ ] **Step 6: Commit**

```bash
git add src/RVTuk.Revit/RoundPositions && git commit -m "feat(round-positions): openings slide along their wall, never off it"
```

---

### Task 16: Base points

**Files:**
- Create: `src/RVTuk.Revit/RoundPositions/BasePointPass.cs`
- Modify: `src/RVTuk.Revit/RoundPositions/RoundPositionsRunner.cs`

**Interfaces:**
- Consumes: `GridSnapper`, `MoveAttempt`, `RoundingReportBuilder`, `SkipReason`.
- Produces: `BasePointPass.Run(Document doc, double step, RoundingReportBuilder report)`.

This is the highest-consequence pass in the tool. It runs **first**, and it refuses anything it does
not fully understand.

- [ ] **Step 1: Write the pass**

Create `src/RVTuk.Revit/RoundPositions/BasePointPass.cs`:

```csharp
using System.Linq;
using Autodesk.Revit.DB;
using RVTuk.Core.RoundPositions;

namespace RVTuk.Revit.RoundPositions
{
    /// <summary>
    /// The Project Base Point and Survey Point. Because the grid is anchored to Revit's internal
    /// origin, a base point at a non-round offset would leave every project coordinate dirty even
    /// after a clean run — snapping the points themselves is what makes project and survey
    /// coordinates come out round too.
    ///
    /// Moving the Survey Point is not a local change: every linked model positioned by shared
    /// coordinates shifts relative to this one. So a point is only touched when it is UNCLIPPED,
    /// where the move relocates the marker rather than the coordinate system. A clipped point is
    /// reported and left exactly where it is.
    /// </summary>
    public static class BasePointPass
    {
        public static void Run(Document doc, double step, RoundingReportBuilder report)
        {
            var points = new FilteredElementCollector(doc)
                .OfClass(typeof(BasePoint))
                .Cast<BasePoint>()
                .ToList();

            foreach (var point in points)
            {
                report.CountExamined();

                var clipped = point.get_Parameter(BuiltInParameter.BASEPOINT_CLIPPED_PARAM);
                if (clipped == null || clipped.AsInteger() != 0)
                {
                    report.RecordSkipped(
                        point.Id.Value, ElementEligibility.CategoryName(point), SkipReason.UnexpectedClipState);
                    continue;
                }

                var position = point.Position;
                var currentX = GridSnapper.FromFeet(position.X);
                var currentY = GridSnapper.FromFeet(position.Y);
                var currentZ = GridSnapper.FromFeet(position.Z);

                var translation = new XYZ(
                    GridSnapper.ToFeet(GridSnapper.Snap(currentX, step) - currentX),
                    GridSnapper.ToFeet(GridSnapper.Snap(currentY, step) - currentY),
                    GridSnapper.ToFeet(GridSnapper.Snap(currentZ, step) - currentZ));

                if (translation.GetLength() <= GridSnapper.ToFeet(GridSnapper.EpsilonCentimetres))
                {
                    report.RecordAlreadyClean();
                    continue;
                }

                if (MoveAttempt.TryMove(doc, point, translation, report))
                {
                    report.RecordMoved();
                }
                else
                {
                    report.RecordSkipped(
                        point.Id.Value, ElementEligibility.CategoryName(point), SkipReason.RevitRejected);
                }
            }
        }
    }
}
```

- [ ] **Step 2: Call it first in the runner**

In `RoundPositionsRunner.Run`, immediately before `LevelPass.Run(...)`:

```csharp
                BasePointPass.Run(doc, step, report);
```

- [ ] **Step 3: Build both configurations**

```bash
dotnet build RVTuk.sln -c Release2024 && dotnet build RVTuk.sln -c Release2025
```

Expected: both succeed.

- [ ] **Step 4: In-Revit check (user) — do this on a throwaway copy**

This is the checklist that decides whether the pass ships at all.

- [ ] **Confirm what `BASEPOINT_CLIPPED_PARAM` actually reads** for each point in both states. If the value is not 0-for-unclipped, fix the test before going further.
- [ ] With an **unclipped** Project Base Point at a non-round position: it moves onto the grid, and **the model geometry does not move**.
- [ ] Spot-coordinate and spot-elevation tags now read round numbers.
- [ ] A **clipped** point is skipped and appears in the results under "unexpected clip state".
- [ ] With a linked model positioned by shared coordinates: confirm whether it shifted. **If it did, stop and report back** — the pass needs to refuse Survey Point moves in coordinated models.
- [ ] Ctrl+Z restores the point.

- [ ] **Step 5: Commit**

```bash
git add src/RVTuk.Revit/RoundPositions && git commit -m "feat(round-positions): snap the base points, and refuse the ones we don't understand"
```

---

## Phase 4 — documentation

### Task 17: Docs, backlog and registration

**Files:**
- Create: `docs/tools/round-positions/README.md`
- Create: `docs/tools/round-positions/backlog.md`
- Modify: `CLAUDE.md` (tool list around line 32; canonical names around line 20; v1 launch surface around line 25)
- Modify: `docs/BACKLOG.md` (per-tool backlog index near line 5)

- [ ] **Step 1: Write the README**

Create `docs/tools/round-positions/README.md`:

```markdown
# Round Positions

**Status:** v1 shipped · **Ribbon:** RVTuk → "Round Positions" · **Revit:** 2024, 2025

Moves every eligible element in the active model onto a whole-centimetre grid. A checkbox
chooses whether half centimetres are allowed (0.5 cm grid) or not (1 cm grid).

**v1 translates only — it never reshapes.** Lengths, angles and sketches are untouched. An
element either slides onto the grid or is skipped and listed.

## What moves

| | How |
|---|---|
| Project Base Point, Survey Point | Snapped, **unclipped points only** |
| Levels | Elevation snapped; everything hosted rides along |
| Grids, reference planes | Translated (their geometry is read-only) |
| Walls, framing, model/detail lines | **Orthogonal only**, slid parallel to themselves |
| Unhosted point families | Point snapped; rotation snapped to 45° multiples within 0.5° |
| Doors, windows | Slid along their host wall, only if that wall resolved |

## What is skipped, and reported

Linked models and imported CAD, group members, design options, elements checked out by another
user, **anything not orthogonal**, arcs, sketch-based elements (floors, roofs, ceilings), clipped
base points, and anything Revit refused.

Pinned elements are unpinned, moved and re-pinned. A move Revit refuses triggers one retry after
releasing what held it: locked dimensions are *unlocked*, alignment constraints are deleted. Every
release is listed in the results — nothing in a Revit model shows what used to be constrained.

The whole run is one transaction, so Ctrl+Z undoes all of it.

## Entry points

- `src/RVTuk.Revit/RoundPositions/Commands/RoundPositionsCommand.cs` — ribbon command
- `src/RVTuk.Revit/RoundPositions/RoundPositionsRunner.cs` — the six passes, in order
- `src/RVTuk.Core/RoundPositions/` — all the maths, unit-tested

## Docs

- [Design](specs/2026-08-06-round-positions-design.md)
- [v1 plan](plans/2026-08-06-round-positions-v1.md)
- [Backlog](backlog.md)
```

- [ ] **Step 2: Write the backlog with the v2 queue**

Create `docs/tools/round-positions/backlog.md`:

```markdown
# Round Positions — Backlog

Check off with `[x]` and the commit hash when shipped.

## 🐞 Bugs / things to fix

*(none tracked)*

## ✨ v2 — geometry repair

The v1 skip list is this queue. All of it was deliberately deferred; see the
[design](specs/2026-08-06-round-positions-design.md) for why.

- [ ] **Straighten near-orthogonal lines.** A wall at 89.97° is skipped today. Straightening it
      and preserving corners needs a global rectification pass: cluster the axis values of all
      near-horizontal and near-vertical lines, then anchor the along-axis endpoints against the
      perpendicular lines' assigned positions. Per-element straightening does not work — two walls
      sharing a corner each move it without telling the other.
- [ ] **Round wall lengths.** Depends on straightening: a length is only meaningfully round once
      the wall is actually orthogonal.
- [ ] **Sketch-based elements** — floors, roofs, ceilings. Needs `SketchEditScope`, and sketch
      curves are often locked to wall faces.
- [ ] **Arcs** — arc walls, arc grids, arc framing.
- [ ] Stairs and railings.

## 📋 Measurements to record

- [ ] Run time on the largest real model, and whether the per-element sub-transaction cost is
      acceptable. Fallback if not: batch sub-transactions per category, losing per-element skip
      attribution.

## ✅ Done

- [x] v1 — translate-only rounding of base points, levels, datums, orthogonal lines, point
      families and wall-hosted openings.
```

- [ ] **Step 3: Update CLAUDE.md**

Add to the canonical tool-name list (the paragraph beginning "Canonical tool names in code and folders"):

```
`RoundPositions`
```

Add to the v1 launch surface sentence so it names Round Positions alongside the other panel buttons.

Add to the Features list, after the DWG Exporter bullet:

```markdown
- **Round Positions** (ribbon "Round Positions") — moves every eligible element in the active model onto a whole-centimetre grid; a checkbox chooses whether half centimetres are allowed. **v1 translates only — it never reshapes**: lengths, angles and sketches are untouched, and anything not orthogonal is skipped and reported. Covers base points (unclipped only), levels, grids/reference planes, orthogonal walls/framing/lines, point families, and wall-hosted openings. Pinned elements are unpinned/moved/re-pinned; a refused move retries once after unlocking dimensions and deleting alignment constraints. One transaction, so one Ctrl+Z. Revit 2024/25 only. See [`docs/tools/round-positions/README.md`](docs/tools/round-positions/README.md).
```

- [ ] **Step 4: Update the toolkit backlog index**

In `docs/BACKLOG.md`, add to the per-tool list:

```markdown
- [Round Positions](tools/round-positions/backlog.md)
```

- [ ] **Step 5: Full verification**

```bash
dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj
```

Expected: PASS, no failures.

```bash
dotnet build RVTuk.sln -c Release2024 && dotnet build RVTuk.sln -c Release2025
```

Expected: both succeed.

- [ ] **Step 6: Commit**

```bash
git add docs CLAUDE.md && git commit -m "docs(round-positions): what v1 moves, and what it leaves for v2"
```

---

## Spec deviations recorded during planning

Two things changed from the spec while writing this plan. Both are already reflected above; the
spec needs the same edits.

1. **Orthogonality is a displacement test, not an angle test.** The spec says "within 0.01° of 0°
   or 90°". An angular tolerance scales with length — 0.01° is float dust on a 40 cm wall and
   0.7 cm of real drift on a 40 m one — so the same number would mean two different things. The
   implementation tests the off-axis extent against 0.01 cm instead.

2. **Base points are only touched when unclipped.** The spec says the tool "refuses to touch a
   point whose clip state it does not expect" without saying which state that is. Task 16 fixes it
   as *unclipped only*, and its checklist verifies the parameter actually reads that way before
   the pass is trusted.

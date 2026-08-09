# Round Positions v2 — Scan and Fix — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the one-shot ribbon button with a dockable pane that scans the model for off-grid elements, shows them as a dependency-ordered Category → Level → element tree, and fixes any node — fast enough to use routinely.

**Architecture:** A read-only scan (no transaction, no regeneration) classifies every candidate element into dirty / already-clean / cannot-fix. One `CategoryPass` type does both scan and fix for every category, parameterised by a collector and an offset reader, so adding a category is a reader plus one registry line. Fixes are applied in batches inside a single sub-transaction — one regeneration per batch instead of per element — falling back to per-element only inside a batch that failed.

**Tech Stack:** C# (net48 for Revit 2024, net8.0-windows for 2025), WPF, xunit, Revit API 2024/2025.

Spec: [2026-08-06-round-positions-design.md](../specs/2026-08-06-round-positions-design.md)
Supersedes the interaction model in [2026-08-06-round-positions-v1.md](2026-08-06-round-positions-v1.md), whose Tasks 1–7 remain the accurate record of the Core and engine code this plan builds on.

## Global Constraints

- **v1 translates only.** No length, angle, endpoint or sketch is ever modified. Geometry repair is v2 of the *tool*, not of this plan.
- **Grid anchored at Revit's internal origin**, step 0.5 cm or 1.0 cm by user choice, `MidpointRounding.AwayFromZero`, 1 ft = 30.48 cm exactly.
- **Orthogonality is a displacement test**: a line runs along X iff its Y extent is ≤ 0.01 cm and its X extent > 0.01 cm.
- **The scan is a snapshot to decide from, never a list of instructions to replay.** Every element is re-read at fix time; one deleted or since-cleaned is passed over, not an error.
- **The report never over-claims.** Constraint releases are recorded only after the release commits; a non-`Committed` transaction status returns no work-claiming report.
- **`RVTuk.Core` never references a Revit or WPF type. `RVTuk.UI` never references a Revit type** — Revit work crosses as `Func<>`/`Action` delegates.
- **Namespace = root namespace + folder path, exactly.**
- Revit 2024/2025 only (`RVTuk.Revit`). `ElementId.Value` / `new ElementId(long)` are correct; no `#if REVIT2023`.
- Core uses block-scoped namespaces; the test project uses file-scoped.
- Minimal code comments — comment *why*, never *what*.
- Build: `dotnet build RVTuk.sln -c Release2024` and `-c Release2025`. Tests: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj`.
- New `.cs`/`.xaml` files need no `.csproj` edit — all projects are SDK-style and auto-glob.

## Already built and reviewed — do not rewrite

`GridPoint`, `GridSnapper`, `AxisClassifier`, `LineOffset`, `RoundingReport`/`SkipReason`/`RoundingReportBuilder` (Core); `ElementEligibility`, `ConstraintReleaser`, `RoundingFailurePreprocessor`, `MoveAttempt` (Revit). 371 Core tests pass. `MoveAttempt.TryApply`/`TryMove` become the per-element fallback and are used unchanged.

---

## Phase A — Core additions (TDD)

### Task 1: The scan result and its tree

**Files:**
- Create: `src/RVTuk.Core/RoundPositions/FixTier.cs`
- Create: `src/RVTuk.Core/RoundPositions/ScanResult.cs`
- Test: `tests/RVTuk.Core.Tests/RoundPositions/ScanResultTests.cs`

**Interfaces:**
- Consumes: `SkippedElement`, `SkipReason` (existing).
- Produces: `enum FixTier`; `readonly record struct DirtyElement(long ElementId, FixTier Tier, string Category, string Level, double? LevelElevationCm, string Name)`; `record LevelNode(string Level, int Count, IReadOnlyList<DirtyElement> Elements)`; `record CategoryNode(FixTier Tier, string Category, int Count, IReadOnlyList<LevelNode> Levels)`; `record ScanResult(int Examined, int AlreadyClean, IReadOnlyList<DirtyElement> Dirty, IReadOnlyList<SkippedElement> Skipped)` with `GroupedByCategoryAndLevel() → IReadOnlyList<CategoryNode>`; `class ScanResultBuilder` with `CountExamined()`, `RecordAlreadyClean()`, `RecordDirty(DirtyElement)`, `RecordSkipped(long, string, SkipReason)`, `Build()`.

- [ ] **Step 1: Write the failing test**

Create `tests/RVTuk.Core.Tests/RoundPositions/ScanResultTests.cs`:

```csharp
using System.Linq;
using RVTuk.Core.RoundPositions;
using Xunit;

namespace RVTuk.Core.Tests.RoundPositions;

public class ScanResultTests
{
    private static DirtyElement Wall(long id, string level, double? elevation) =>
        new DirtyElement(id, FixTier.Walls, "Walls", level, elevation, $"Wall {id}");

    [Fact]
    public void CountsComeStraightFromTheBuilder()
    {
        var builder = new ScanResultBuilder();
        builder.CountExamined();
        builder.RecordAlreadyClean();
        builder.CountExamined();
        builder.RecordDirty(Wall(1, "Level 1", 0));
        builder.CountExamined();
        builder.RecordSkipped(3, "Walls", SkipReason.NotOrthogonal);

        var result = builder.Build();

        Assert.Equal(3, result.Examined);
        Assert.Equal(1, result.AlreadyClean);
        Assert.Single(result.Dirty);
        Assert.Single(result.Skipped);
    }

    [Fact]
    public void CategoriesSortByTierNotAlphabetically()
    {
        // The order is the point: fixing walls before levels means the level fix moves those
        // walls again. Alphabetically "Levels" would follow "Grids" and both would follow "Walls".
        var builder = new ScanResultBuilder();
        builder.RecordDirty(Wall(1, "Level 1", 0));
        builder.RecordDirty(new DirtyElement(2, FixTier.Datums, "Grids", "", null, "Grid A"));
        builder.RecordDirty(new DirtyElement(3, FixTier.Levels, "Levels", "", null, "Level 1"));

        var categories = builder.Build().GroupedByCategoryAndLevel();

        Assert.Equal(new[] { FixTier.Levels, FixTier.Datums, FixTier.Walls },
            categories.Select(c => c.Tier).ToArray());
    }

    [Fact]
    public void LevelsSortBottomToTopByElevation()
    {
        var builder = new ScanResultBuilder();
        builder.RecordDirty(Wall(1, "Level 3", 700));
        builder.RecordDirty(Wall(2, "Level 1", 0));
        builder.RecordDirty(Wall(3, "Level 2", 350));

        var levels = builder.Build().GroupedByCategoryAndLevel().Single().Levels;

        Assert.Equal(new[] { "Level 1", "Level 2", "Level 3" },
            levels.Select(l => l.Level).ToArray());
    }

    [Fact]
    public void ElementsWithNoLevelSortFirstUnderTheirOwnLabel()
    {
        // Grids and base points belong to no level. They must still appear under their category
        // rather than being dropped from the tree.
        var builder = new ScanResultBuilder();
        builder.RecordDirty(Wall(1, "Level 1", 0));
        builder.RecordDirty(Wall(2, "", null));

        var levels = builder.Build().GroupedByCategoryAndLevel().Single().Levels;

        Assert.Equal(ScanResult.NoLevelLabel, levels[0].Level);
        Assert.Equal("Level 1", levels[1].Level);
    }

    [Fact]
    public void CountsRollUpFromElementsToLevelToCategory()
    {
        var builder = new ScanResultBuilder();
        builder.RecordDirty(Wall(1, "Level 1", 0));
        builder.RecordDirty(Wall(2, "Level 1", 0));
        builder.RecordDirty(Wall(3, "Level 2", 350));

        var category = builder.Build().GroupedByCategoryAndLevel().Single();

        Assert.Equal(3, category.Count);
        Assert.Equal(2, category.Levels[0].Count);
        Assert.Equal(1, category.Levels[1].Count);
    }

    [Fact]
    public void TwoCategoriesInTheSameTierSortByName()
    {
        var builder = new ScanResultBuilder();
        builder.RecordDirty(new DirtyElement(1, FixTier.Datums, "Reference Planes", "", null, "RP 1"));
        builder.RecordDirty(new DirtyElement(2, FixTier.Datums, "Grids", "", null, "Grid A"));

        var categories = builder.Build().GroupedByCategoryAndLevel();

        Assert.Equal(new[] { "Grids", "Reference Planes" },
            categories.Select(c => c.Category).ToArray());
    }

    [Fact]
    public void AnEmptyScanGroupsToNothingRatherThanThrowing()
    {
        var result = new ScanResultBuilder().Build();

        Assert.Empty(result.GroupedByCategoryAndLevel());
        Assert.Equal(0, result.Examined);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj --filter ScanResultTests
```

Expected: FAIL — build error, `FixTier` / `ScanResultBuilder` do not exist.

- [ ] **Step 3: Write the implementation**

Create `src/RVTuk.Core/RoundPositions/FixTier.cs`:

```csharp
namespace RVTuk.Core.RoundPositions
{
    /// <summary>
    /// The dependency hierarchy, and the only ordering the tree uses. Each tier can move what sits
    /// below it — a level carries its whole floor, a wall drags its openings — so reading the tree
    /// top to bottom is reading the order it is safe to work in.
    /// </summary>
    public enum FixTier
    {
        BasePoints = 0,
        Levels = 1,
        Datums = 2,
        Walls = 3,
        Framing = 4,
        DraftingLines = 5,
        PointFamilies = 6,
        HostedFamilies = 7,
    }
}
```

Create `src/RVTuk.Core/RoundPositions/ScanResult.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;

namespace RVTuk.Core.RoundPositions
{
    /// <summary>One element the scan found off the grid. <paramref name="LevelElevationCm"/> is
    /// null for things that belong to no level, such as grids and base points.</summary>
    public readonly record struct DirtyElement(
        long ElementId,
        FixTier Tier,
        string Category,
        string Level,
        double? LevelElevationCm,
        string Name);

    public record LevelNode(string Level, int Count, IReadOnlyList<DirtyElement> Elements);

    public record CategoryNode(FixTier Tier, string Category, int Count, IReadOnlyList<LevelNode> Levels);

    public record ScanResult(
        int Examined,
        int AlreadyClean,
        IReadOnlyList<DirtyElement> Dirty,
        IReadOnlyList<SkippedElement> Skipped)
    {
        /// <summary>Shown for grids, base points and anything else with no owning level.</summary>
        public const string NoLevelLabel = "(no level)";

        public IReadOnlyList<CategoryNode> GroupedByCategoryAndLevel() =>
            Dirty
                .GroupBy(d => (d.Tier, d.Category))
                .OrderBy(g => g.Key.Tier)
                .ThenBy(g => g.Key.Category, System.StringComparer.OrdinalIgnoreCase)
                .Select(g => new CategoryNode(
                    g.Key.Tier,
                    g.Key.Category,
                    g.Count(),
                    g.GroupBy(d => d.Level)
                        // No-level first, then bottom to top: an architect reads a building upwards,
                        // and level names sort alphabetically in the wrong order past Level 9.
                        .OrderBy(l => l.First().LevelElevationCm ?? double.NegativeInfinity)
                        .Select(l => new LevelNode(
                            string.IsNullOrEmpty(l.Key) ? NoLevelLabel : l.Key,
                            l.Count(),
                            l.OrderBy(d => d.Name, System.StringComparer.OrdinalIgnoreCase).ToList()))
                        .ToList()))
                .ToList();
    }

    public class ScanResultBuilder
    {
        private readonly List<DirtyElement> _dirty = new List<DirtyElement>();
        private readonly List<SkippedElement> _skipped = new List<SkippedElement>();

        public int Examined { get; private set; }
        public int AlreadyClean { get; private set; }

        public void CountExamined() => Examined++;

        public void RecordAlreadyClean() => AlreadyClean++;

        public void RecordDirty(DirtyElement element) => _dirty.Add(element);

        public void RecordSkipped(long elementId, string category, SkipReason reason) =>
            _skipped.Add(new SkippedElement(elementId, category, reason));

        public ScanResult Build() =>
            new ScanResult(Examined, AlreadyClean, _dirty.ToList(), _skipped.ToList());
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj --filter ScanResultTests
```

Expected: PASS, 7 tests.

- [ ] **Step 5: Mutation-probe the ordering**

The two orderings are the whole point of this type, so prove the tests defend them. Change `.OrderBy(g => g.Key.Tier)` to `.OrderBy(g => g.Key.Category, StringComparer.OrdinalIgnoreCase)` — `CategoriesSortByTierNotAlphabetically` must FAIL. Restore. Then remove `?? double.NegativeInfinity` and substitute `?? double.PositiveInfinity` — `ElementsWithNoLevelSortFirstUnderTheirOwnLabel` must FAIL. Restore, and confirm `git diff` on the source is clean. Report both outcomes; **a probe that survives is a finding, not a failure**.

- [ ] **Step 6: Run the full Core suite**

```bash
dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj
```

Expected: PASS, no failures.

- [ ] **Step 7: Commit**

```bash
git add src/RVTuk.Core/RoundPositions tests/RVTuk.Core.Tests/RoundPositions && git commit -m "feat(round-positions): a scan result that reads in the order it is safe to fix"
```

---

### Task 2: Batch splitting

**Files:**
- Create: `src/RVTuk.Core/RoundPositions/Batches.cs`
- Test: `tests/RVTuk.Core.Tests/RoundPositions/BatchesTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `Batches.DefaultSize` (int, 250); `Batches.Split<T>(IReadOnlyList<T> items, int size) → IReadOnlyList<IReadOnlyList<T>>`.

- [ ] **Step 1: Write the failing test**

Create `tests/RVTuk.Core.Tests/RoundPositions/BatchesTests.cs`:

```csharp
using System;
using System.Linq;
using RVTuk.Core.RoundPositions;
using Xunit;

namespace RVTuk.Core.Tests.RoundPositions;

public class BatchesTests
{
    private static int[] Range(int n) => Enumerable.Range(1, n).ToArray();

    [Fact]
    public void AnExactMultipleSplitsEvenly()
    {
        var batches = Batches.Split(Range(6), 3);

        Assert.Equal(2, batches.Count);
        Assert.Equal(new[] { 1, 2, 3 }, batches[0]);
        Assert.Equal(new[] { 4, 5, 6 }, batches[1]);
    }

    [Fact]
    public void ARemainderBecomesAShorterFinalBatch()
    {
        var batches = Batches.Split(Range(7), 3);

        Assert.Equal(3, batches.Count);
        Assert.Equal(new[] { 7 }, batches[2]);
    }

    [Fact]
    public void FewerItemsThanOneBatchIsOneBatch()
    {
        var batches = Batches.Split(Range(2), 250);

        Assert.Single(batches);
        Assert.Equal(2, batches[0].Count);
    }

    [Fact]
    public void NothingToSplitYieldsNoBatchesRatherThanOneEmptyOne()
    {
        // A caller looping over batches must not open a sub-transaction for nothing.
        Assert.Empty(Batches.Split(Array.Empty<int>(), 250));
    }

    [Fact]
    public void EveryItemAppearsExactlyOnceAndInOrder()
    {
        var batches = Batches.Split(Range(1000), 250);

        Assert.Equal(Range(1000), batches.SelectMany(b => b).ToArray());
    }

    [Fact]
    public void ANonPositiveSizeIsRejectedRatherThanLoopingForever()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Batches.Split(Range(3), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Batches.Split(Range(3), -1));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj --filter BatchesTests
```

Expected: FAIL — build error, `Batches` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/RVTuk.Core/RoundPositions/Batches.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace RVTuk.Core.RoundPositions
{
    public static class Batches
    {
        /// <summary>
        /// A guess, not a measurement. Revit regenerates once per sub-transaction commit, so a
        /// bigger batch means fewer regenerations — but a batch that fails is re-run element by
        /// element, so a bigger batch also makes the fallback more expensive. The first in-Revit
        /// timing decides whether this moves.
        /// </summary>
        public const int DefaultSize = 250;

        public static IReadOnlyList<IReadOnlyList<T>> Split<T>(IReadOnlyList<T> items, int size)
        {
            if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));

            var batches = new List<IReadOnlyList<T>>();
            for (var start = 0; start < items.Count; start += size)
            {
                var take = Math.Min(size, items.Count - start);
                var batch = new List<T>(take);
                for (var i = 0; i < take; i++) batch.Add(items[start + i]);
                batches.Add(batch);
            }

            return batches;
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj --filter BatchesTests
```

Expected: PASS, 6 tests.

- [ ] **Step 5: Run the full Core suite and commit**

```bash
dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj
git add src/RVTuk.Core/RoundPositions tests/RVTuk.Core.Tests/RoundPositions && git commit -m "feat(round-positions): split a fix into batches Revit regenerates once each"
```

---

## Phase B — the engine

### Task 3: The offset verdict and the wall reader

**Files:**
- Create: `src/RVTuk.Revit/RoundPositions/OffsetVerdict.cs`
- Create: `src/RVTuk.Revit/RoundPositions/Readers/LineReader.cs`
- Delete: `src/RVTuk.Revit/RoundPositions/LinePass.cs`

**Interfaces:**
- Consumes: `LineOffset`, `GridSnapper`, `AxisClassifier`, `SkipReason`, `ElementEligibility`.
- Produces: `enum OffsetOutcome { NeedsMove, AlreadyOnGrid, CannotFix }`; `readonly struct OffsetVerdict` with static factories `OffsetVerdict.Move(XYZ translation)`, `OffsetVerdict.Clean()`, `OffsetVerdict.Cannot(SkipReason reason)` and fields `Outcome`, `Translation`, `Reason`; `LineReader.Read(Document doc, Element element, double step) → OffsetVerdict`.

This is the shape every category reuses: **one function that both the scan and the fix call**. The scan uses the verdict to sort an element into a bucket; the fix uses the same verdict's translation to move it. There is exactly one place that decides what an element needs, so the two can never disagree.

- [ ] **Step 1: Write the verdict type**

Create `src/RVTuk.Revit/RoundPositions/OffsetVerdict.cs`:

```csharp
using Autodesk.Revit.DB;
using RVTuk.Core.RoundPositions;

namespace RVTuk.Revit.RoundPositions
{
    public enum OffsetOutcome
    {
        NeedsMove,
        AlreadyOnGrid,
        CannotFix,
    }

    /// <summary>
    /// What one element needs, as read from the model. The scan and the fix both go through this,
    /// so a fix can never act on a different answer than the one the tree showed.
    /// </summary>
    public readonly struct OffsetVerdict
    {
        private OffsetVerdict(OffsetOutcome outcome, XYZ? translation, SkipReason reason)
        {
            Outcome = outcome;
            Translation = translation;
            Reason = reason;
        }

        public OffsetOutcome Outcome { get; }

        /// <summary>Only meaningful when <see cref="Outcome"/> is <see cref="OffsetOutcome.NeedsMove"/>.</summary>
        public XYZ? Translation { get; }

        /// <summary>Only meaningful when <see cref="Outcome"/> is <see cref="OffsetOutcome.CannotFix"/>.</summary>
        public SkipReason Reason { get; }

        public static OffsetVerdict Move(XYZ translation) =>
            new OffsetVerdict(OffsetOutcome.NeedsMove, translation, default);

        public static OffsetVerdict Clean() =>
            new OffsetVerdict(OffsetOutcome.AlreadyOnGrid, null, default);

        public static OffsetVerdict Cannot(SkipReason reason) =>
            new OffsetVerdict(OffsetOutcome.CannotFix, null, reason);
    }
}
```

- [ ] **Step 2: Write the line reader**

Create `src/RVTuk.Revit/RoundPositions/Readers/LineReader.cs`. This is the logic lifted out of the deleted `LinePass`, unchanged in behaviour:

```csharp
using System;
using Autodesk.Revit.DB;
using RVTuk.Core.RoundPositions;

namespace RVTuk.Revit.RoundPositions.Readers
{
    /// <summary>
    /// Straight-line elements — walls, framing, model and detail lines. Only exactly-orthogonal
    /// ones move, and they slide parallel to themselves so no length or angle can change.
    /// </summary>
    public static class LineReader
    {
        public static OffsetVerdict Read(Document doc, Element element, double step)
        {
            if (!(element.Location is LocationCurve locationCurve) ||
                !(locationCurve.Curve is Line line))
            {
                return OffsetVerdict.Cannot(SkipReason.NotOrthogonal);
            }

            var start = ToGridPoint(line.GetEndPoint(0));
            var end = ToGridPoint(line.GetEndPoint(1));
            var offset = LineOffset.For(start, end, step, AxisClassifier.ToleranceCentimetres);

            switch (offset.Outcome)
            {
                case LineOffsetOutcome.AlreadyOnGrid:
                    return OffsetVerdict.Clean();

                case LineOffsetOutcome.NotOrthogonal:
                    return OffsetVerdict.Cannot(SkipReason.NotOrthogonal);

                case LineOffsetOutcome.Offset:
                    return OffsetVerdict.Move(new XYZ(
                        GridSnapper.ToFeet(offset.DeltaX), GridSnapper.ToFeet(offset.DeltaY), 0));

                default:
                    throw new InvalidOperationException(
                        $"Unhandled {nameof(LineOffsetOutcome)}: {offset.Outcome}");
            }
        }

        private static GridPoint ToGridPoint(XYZ point) => new GridPoint(
            GridSnapper.FromFeet(point.X), GridSnapper.FromFeet(point.Y), GridSnapper.FromFeet(point.Z));
    }
}
```

- [ ] **Step 3: Delete the old pass**

```bash
git rm src/RVTuk.Revit/RoundPositions/LinePass.cs
```

`RoundPositionsRunner` still calls it and will not compile — Task 4 replaces the runner. Do **not** patch the runner here; leaving it broken for one task is deliberate so the two changes stay reviewable apart. If you would rather not commit a non-building tree, do Tasks 3 and 4 as one commit and say so in your report.

- [ ] **Step 4: Commit**

```bash
git add -A src/RVTuk.Revit/RoundPositions && git commit -m "refactor(round-positions): one reader answers both the scan and the fix"
```

---

### Task 4: CategoryPass, BatchMover, and the runner

**Files:**
- Create: `src/RVTuk.Revit/RoundPositions/BatchMover.cs`
- Create: `src/RVTuk.Revit/RoundPositions/CategoryPass.cs`
- Create: `src/RVTuk.Revit/RoundPositions/PassRegistry.cs`
- Modify: `src/RVTuk.Revit/RoundPositions/RoundPositionsRunner.cs` (replace wholesale)

**Interfaces:**
- Consumes: `OffsetVerdict`, `LineReader.Read`, `MoveAttempt.TryMove`, `Batches.Split`, `ScanResultBuilder`, `RoundingReportBuilder`, `ElementEligibility`, `RoundingFailurePreprocessor`.
- Produces: `BatchMover.Move(Document doc, IReadOnlyList<(Element Element, XYZ Translation)> moves, int batchSize, RoundingReportBuilder report) → int` (count moved); `CategoryPass` class with ctor `(FixTier tier, string category, Func<Document, IReadOnlyList<Element>> collect, Func<Document, Element, double, OffsetVerdict> read)` and methods `Scan(Document, double, ScanResultBuilder)` and `Apply(Document, double, ISet<long>, int, RoundingReportBuilder)`; `PassRegistry.All → IReadOnlyList<CategoryPass>` (tier order); `RoundPositionsRunner.Scan(Document, bool) → ScanResult` and `RoundPositionsRunner.Fix(Document, bool, ISet<long>) → RoundingReport`.

- [ ] **Step 1: Write the batch mover**

Create `src/RVTuk.Revit/RoundPositions/BatchMover.cs`:

```csharp
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using RVTuk.Core.RoundPositions;

namespace RVTuk.Revit.RoundPositions
{
    /// <summary>
    /// Optimistic batch, pessimistic retry. Revit regenerates when a sub-transaction commits, not
    /// on each API call, so moving a whole batch inside one sub-transaction costs one regeneration
    /// instead of one per element — which is the entire difference between this tool being usable
    /// and not. A batch that throws is rolled back and re-run element by element, which restores
    /// exact per-element attribution and pays the slow path only where the trouble actually is.
    /// </summary>
    public static class BatchMover
    {
        public static int Move(
            Document doc,
            IReadOnlyList<(Element Element, XYZ Translation)> moves,
            int batchSize,
            RoundingReportBuilder report)
        {
            var moved = 0;

            foreach (var batch in Batches.Split(moves, batchSize))
            {
                if (TryWholeBatch(doc, batch))
                {
                    moved += batch.Count;
                    continue;
                }

                foreach (var (element, translation) in batch)
                {
                    if (MoveAttempt.TryMove(doc, element, translation, report))
                    {
                        moved++;
                    }
                    else
                    {
                        report.RecordSkipped(
                            element.Id.Value,
                            ElementEligibility.CategoryName(element),
                            SkipReason.RevitRejected);
                    }
                }
            }

            return moved;
        }

        private static bool TryWholeBatch(
            Document doc, IReadOnlyList<(Element Element, XYZ Translation)> batch)
        {
            using (var sub = new SubTransaction(doc))
            {
                sub.Start();
                try
                {
                    foreach (var (element, translation) in batch)
                    {
                        // Unpinning inside the batch is safe: a rollback restores every pin along
                        // with every position, so a failed batch leaves nothing half-done.
                        var wasPinned = element.Pinned;
                        if (wasPinned) element.Pinned = false;
                        ElementTransformUtils.MoveElement(doc, element.Id, translation);
                        if (wasPinned) element.Pinned = true;
                    }

                    sub.Commit();
                    return true;
                }
                catch (Exception)
                {
                    sub.RollBack();
                    return false;
                }
            }
        }
    }
}
```

- [ ] **Step 2: Write the category pass**

Create `src/RVTuk.Revit/RoundPositions/CategoryPass.cs`:

```csharp
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using RVTuk.Core.RoundPositions;

namespace RVTuk.Revit.RoundPositions
{
    /// <summary>
    /// One category's half of both operations. Scan and Apply share the same collector and the
    /// same reader, so the tree can never promise something the fix would decide differently.
    /// </summary>
    public sealed class CategoryPass
    {
        private readonly Func<Document, IReadOnlyList<Element>> _collect;
        private readonly Func<Document, Element, double, OffsetVerdict> _read;

        public CategoryPass(
            FixTier tier,
            string category,
            Func<Document, IReadOnlyList<Element>> collect,
            Func<Document, Element, double, OffsetVerdict> read)
        {
            Tier = tier;
            Category = category;
            _collect = collect;
            _read = read;
        }

        public FixTier Tier { get; }

        public string Category { get; }

        public void Scan(Document doc, double step, ScanResultBuilder builder)
        {
            foreach (var element in _collect(doc))
            {
                builder.CountExamined();

                var skip = ElementEligibility.ReasonToSkip(doc, element);
                if (skip.HasValue)
                {
                    builder.RecordSkipped(element.Id.Value, Category, skip.Value);
                    continue;
                }

                var verdict = _read(doc, element, step);
                switch (verdict.Outcome)
                {
                    case OffsetOutcome.AlreadyOnGrid:
                        builder.RecordAlreadyClean();
                        break;

                    case OffsetOutcome.CannotFix:
                        builder.RecordSkipped(element.Id.Value, Category, verdict.Reason);
                        break;

                    case OffsetOutcome.NeedsMove:
                        builder.RecordDirty(new DirtyElement(
                            element.Id.Value, Tier, Category,
                            LevelNaming.NameOf(doc, element),
                            LevelNaming.ElevationCentimetresOf(doc, element),
                            ElementNaming.NameOf(element)));
                        break;

                    default:
                        throw new InvalidOperationException(
                            $"Unhandled {nameof(OffsetOutcome)}: {verdict.Outcome}");
                }
            }
        }

        public void Apply(
            Document doc, double step, ISet<long> ids, int batchSize, RoundingReportBuilder report)
        {
            var moves = new List<(Element, XYZ)>();

            foreach (var element in _collect(doc))
            {
                if (!ids.Contains(element.Id.Value)) continue;

                report.CountExamined();

                var skip = ElementEligibility.ReasonToSkip(doc, element);
                if (skip.HasValue)
                {
                    report.RecordSkipped(element.Id.Value, Category, skip.Value);
                    continue;
                }

                // Re-read rather than trusting the scan: the model can have changed since, and an
                // element that is already clean now is passed over rather than treated as an error.
                var verdict = _read(doc, element, step);
                switch (verdict.Outcome)
                {
                    case OffsetOutcome.AlreadyOnGrid:
                        report.RecordAlreadyClean();
                        break;

                    case OffsetOutcome.CannotFix:
                        report.RecordSkipped(element.Id.Value, Category, verdict.Reason);
                        break;

                    case OffsetOutcome.NeedsMove:
                        moves.Add((element, verdict.Translation!));
                        break;

                    default:
                        throw new InvalidOperationException(
                            $"Unhandled {nameof(OffsetOutcome)}: {verdict.Outcome}");
                }
            }

            var moved = BatchMover.Move(doc, moves, batchSize, report);
            for (var i = 0; i < moved; i++) report.RecordMoved();
        }
    }
}
```

- [ ] **Step 3: Write the naming helpers**

Still in `src/RVTuk.Revit/RoundPositions/CategoryPass.cs`, below the class, add:

```csharp
    /// <summary>The level a dirty element is filed under in the tree.</summary>
    internal static class LevelNaming
    {
        public static string NameOf(Document doc, Element element)
        {
            var level = doc.GetElement(element.LevelId) as Level;
            return level?.Name ?? string.Empty;
        }

        public static double? ElevationCentimetresOf(Document doc, Element element)
        {
            var level = doc.GetElement(element.LevelId) as Level;
            return level == null ? (double?)null : GridSnapper.FromFeet(level.Elevation);
        }
    }

    internal static class ElementNaming
    {
        /// <summary>
        /// The id is always shown because a name alone is rarely unique — a plan can hold two
        /// hundred elements all called "Basic Wall".
        /// </summary>
        public static string NameOf(Element element)
        {
            var name = element.Name;
            return string.IsNullOrWhiteSpace(name)
                ? element.Id.Value.ToString()
                : $"{name} · {element.Id.Value}";
        }
    }
```

> `Element.LevelId` returns `ElementId.InvalidElementId` for elements with no level, and
> `doc.GetElement` on that returns null — which is exactly the "(no level)" path Task 1's
> `ScanResult` handles. Verify this in Revit rather than assuming it; note it in your report.

- [ ] **Step 4: Write the registry**

Create `src/RVTuk.Revit/RoundPositions/PassRegistry.cs`. It holds walls only for now; later tasks add one entry each, **in tier order**:

```csharp
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RVTuk.Core.RoundPositions;
using RVTuk.Revit.RoundPositions.Readers;

namespace RVTuk.Revit.RoundPositions
{
    /// <summary>
    /// Every category the tool handles, in the dependency order the tree and Fix all both use.
    /// Adding a category is one entry here plus a reader — nothing else changes.
    /// </summary>
    public static class PassRegistry
    {
        public static IReadOnlyList<CategoryPass> All { get; } = new List<CategoryPass>
        {
            new CategoryPass(FixTier.Walls, "Walls",
                doc => OfCategory(doc, BuiltInCategory.OST_Walls), LineReader.Read),
        };

        internal static IReadOnlyList<Element> OfCategory(Document doc, BuiltInCategory category) =>
            new FilteredElementCollector(doc)
                .OfCategory(category)
                .WhereElementIsNotElementType()
                .ToList();
    }
}
```

- [ ] **Step 5: Replace the runner**

Replace the whole contents of `src/RVTuk.Revit/RoundPositions/RoundPositionsRunner.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Autodesk.Revit.DB;
using RVTuk.Core.RoundPositions;

namespace RVTuk.Revit.RoundPositions
{
    public static class RoundPositionsRunner
    {
        /// <summary>
        /// Read-only: opens no transaction and triggers no regeneration, so it is cheap enough to
        /// re-run after every fix.
        /// </summary>
        public static ScanResult Scan(Document doc, bool allowHalfCentimetres)
        {
            var step = GridSnapper.StepFor(allowHalfCentimetres);
            var builder = new ScanResultBuilder();

            foreach (var pass in PassRegistry.All) pass.Scan(doc, step, builder);

            return builder.Build();
        }

        /// <summary>
        /// One transaction, so a fix is one Ctrl+Z. Passes run in tier order because each tier can
        /// move what sits below it — a level carries its whole floor, a wall drags its openings.
        /// </summary>
        public static RoundingReport Fix(Document doc, bool allowHalfCentimetres, ISet<long> ids)
        {
            var step = GridSnapper.StepFor(allowHalfCentimetres);
            var report = new RoundingReportBuilder();
            var stopwatch = Stopwatch.StartNew();
            TransactionStatus status;

            using (var transaction = new Transaction(doc, "Round Positions"))
            {
                transaction.Start();

                var options = transaction.GetFailureHandlingOptions();
                options.SetForcedModalHandling(false);
                options.SetClearAfterRollback(true);
                options.SetFailuresPreprocessor(new RoundingFailurePreprocessor());
                transaction.SetFailureHandlingOptions(options);

                foreach (var pass in PassRegistry.All)
                    pass.Apply(doc, step, ids, Batches.DefaultSize, report);

                status = transaction.Commit();
            }

            stopwatch.Stop();

            // Revit can still roll the whole transaction back at commit time — an error it signalled
            // by posting rather than throwing lets its sub-transaction commit, so the report by then
            // already counts moves and released constraints. Returning it would claim work on a model
            // that is byte-identical to before the fix, and the constraint list is the only record
            // those constraints ever existed.
            if (status != TransactionStatus.Committed)
            {
                throw new InvalidOperationException(
                    "Revit rolled the whole fix back, so nothing in the model changed: no elements " +
                    "were moved and no alignment constraints were deleted. This happens when Revit " +
                    "cannot accept one of the moves — resolve what Revit reported, then try again.");
            }

            return report.Build() with { ElapsedMilliseconds = stopwatch.ElapsedMilliseconds };
        }
    }
}
```

- [ ] **Step 6: Add the elapsed time to the report**

`RoundingReport` needs the new member the runner sets. In `src/RVTuk.Core/RoundPositions/RoundingReport.cs`, add a property to the record with a default so no existing construction breaks:

```csharp
        /// <summary>How long the fix took. The pane shows it because the first version of this
        /// tool was abandoned mid-run for being slow with nothing on screen to say so.</summary>
        public long ElapsedMilliseconds { get; init; }
```

- [ ] **Step 7: Build both configurations and run the Core suite**

```bash
dotnet build RVTuk.sln -c Release2024 && dotnet build RVTuk.sln -c Release2025
dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj
```

Expected: both build with 0 errors; Core suite passes. `RoundPositionsCommand` still references the old `Run` and will fail to compile — fix it minimally here by calling `Scan` then `Fix` with every dirty id, and note in your report that Task 7 replaces it properly.

- [ ] **Step 8: Commit**

```bash
git add -A src/RVTuk && git commit -m "feat(round-positions): scan reads, fix batches, and one registry orders both"
```

---

### Task 5: The timing gate — first in-Revit measurement

**Files:** none. This task produces a number, not code.

**This is a gate.** Everything after it assumes batching delivered. If it did not, stop and report rather than continuing to build a pane over an engine that is still too slow.

- [ ] **Step 1: Build and deploy**

```bash
dotnet build RVTuk.sln -c Release2024
```

Then, in an **elevated** PowerShell with Revit closed:

```powershell
.\Deploy.ps1 2024
```

- [ ] **Step 2: In-Revit measurement (user)**

On a **copy** of the model that was too slow before:

- [ ] Run the command. It now scans, then fixes every dirty wall.
- [ ] **Record the elapsed milliseconds the report carries**, and the number of walls moved.
- [ ] Record roughly how long the scan alone felt before the fix began.
- [ ] Compare against the previous behaviour — the run that was abandoned.

- [ ] **Step 3: Decide**

- **Under ~10 seconds for a few thousand walls** — batching worked. Continue to Task 6.
- **Still minutes** — batching did not deliver, and the assumption underneath this plan is wrong. **Stop.** Report the number. The likely causes to investigate before writing more UI: Revit regenerating per `MoveElement` regardless of sub-transaction, or the cost being in `FilteredElementCollector`/`ElementEligibility` rather than the moves. Either way the next step is a profile, not a pane.

- [ ] **Step 4: Record the number in the backlog**

Add the measurement to `docs/tools/round-positions/backlog.md` under "Measurements", with the model size and the batch size used. Commit.

---

## Phase C — the pane

### Task 6: The pane view model

**Files:**
- Create: `src/RVTuk.UI/RoundPositions/ViewModels/RoundPositionsPaneViewModel.cs`
- Create: `src/RVTuk.UI/RoundPositions/ViewModels/TreeNodes.cs`

**Interfaces:**
- Consumes: `ScanResult`, `CategoryNode`, `LevelNode`, `DirtyElement`, `RoundingReport`, `SkipGroup`, `ViewModelBase`, `RelayCommand<T>`.
- Produces: `RoundPositionsPaneViewModel(Func<bool, ScanResult> scan, Func<bool, IReadOnlyList<long>, RoundingReport> fix, Action<IReadOnlyList<long>> selectInModel, bool allowHalfCentimetres, Action<bool> persistSetting)` exposing `AllowHalfCentimetres`, `Categories`, `SkipGroups`, `HasSkips`, `Status`, `IsBusy`, `HasScanned`, `ScanCommand`, `FixAllCommand`, `FixNodeCommand`, `SelectNodeCommand`, `SelectSkipGroupCommand`; node types `CategoryRow`, `LevelRow`, `ElementRow`, all deriving from `TreeRow` and exposing `Label`, `Count`, `ElementIds`, `IsExpanded`.

- [ ] **Step 1: Write the row types**

Create `src/RVTuk.UI/RoundPositions/ViewModels/TreeNodes.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using RVTuk.Core.RoundPositions;
using RVTuk.UI.Shared.ViewModels;

namespace RVTuk.UI.RoundPositions.ViewModels
{
    /// <summary>Anything in the tree that can be selected or fixed. One shape for all three tiers
    /// keeps the commands single — Fix and Select never need to know which tier they were given.</summary>
    public abstract class TreeRow : ViewModelBase
    {
        private bool _isExpanded;

        protected TreeRow(string label, IReadOnlyList<long> elementIds)
        {
            Label = label;
            ElementIds = elementIds;
        }

        public string Label { get; }

        public IReadOnlyList<long> ElementIds { get; }

        public int Count => ElementIds.Count;

        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }
    }

    public sealed class ElementRow : TreeRow
    {
        public ElementRow(DirtyElement element)
            : base(element.Name, new[] { element.ElementId })
        {
        }
    }

    public sealed class LevelRow : TreeRow
    {
        public LevelRow(LevelNode node)
            : base(node.Level, node.Elements.Select(e => e.ElementId).ToList())
        {
            Elements = node.Elements.Select(e => new ElementRow(e)).ToList();
        }

        public IReadOnlyList<ElementRow> Elements { get; }
    }

    public sealed class CategoryRow : TreeRow
    {
        public CategoryRow(CategoryNode node)
            : base(node.Category, node.Levels.SelectMany(l => l.Elements).Select(e => e.ElementId).ToList())
        {
            Levels = node.Levels.Select(l => new LevelRow(l)).ToList();
        }

        public IReadOnlyList<LevelRow> Levels { get; }
    }
}
```

- [ ] **Step 2: Write the view model**

Create `src/RVTuk.UI/RoundPositions/ViewModels/RoundPositionsPaneViewModel.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using RVTuk.Core.RoundPositions;
using RVTuk.UI.Shared.ViewModels;

namespace RVTuk.UI.RoundPositions.ViewModels
{
    public class RoundPositionsPaneViewModel : ViewModelBase
    {
        private readonly Func<bool, ScanResult> _scan;
        private readonly Func<bool, IReadOnlyList<long>, RoundingReport> _fix;
        private readonly Action<IReadOnlyList<long>> _selectInModel;
        private readonly Action<bool> _persistSetting;

        private bool _allowHalfCentimetres;
        private bool _isBusy;
        private bool _hasScanned;
        private string _status = "Not scanned yet.";

        public RoundPositionsPaneViewModel(
            Func<bool, ScanResult> scan,
            Func<bool, IReadOnlyList<long>, RoundingReport> fix,
            Action<IReadOnlyList<long>> selectInModel,
            bool allowHalfCentimetres,
            Action<bool> persistSetting)
        {
            _scan = scan ?? throw new ArgumentNullException(nameof(scan));
            _fix = fix ?? throw new ArgumentNullException(nameof(fix));
            _selectInModel = selectInModel ?? throw new ArgumentNullException(nameof(selectInModel));
            _persistSetting = persistSetting ?? throw new ArgumentNullException(nameof(persistSetting));
            _allowHalfCentimetres = allowHalfCentimetres;

            ScanCommand = new RelayCommand<object>(_ => RunScan(), _ => !IsBusy);
            FixAllCommand = new RelayCommand<object>(_ => RunFix(AllDirtyIds()), _ => !IsBusy && HasScanned);
            FixNodeCommand = new RelayCommand<TreeRow>(row => RunFix(row!.ElementIds), _ => !IsBusy);
            SelectNodeCommand = new RelayCommand<TreeRow>(row => _selectInModel(row!.ElementIds));
            SelectSkipGroupCommand = new RelayCommand<SkipGroup>(
                group => _selectInModel(group!.Elements.Select(e => e.ElementId).ToList()));
        }

        public ObservableCollection<CategoryRow> Categories { get; } = new ObservableCollection<CategoryRow>();

        public ObservableCollection<SkipGroup> SkipGroups { get; } = new ObservableCollection<SkipGroup>();

        /// <summary>A bool, not the collection's Count: binding an int through a
        /// BooleanToVisibilityConverter silently does nothing.</summary>
        public bool HasSkips => SkipGroups.Count > 0;

        public bool AllowHalfCentimetres
        {
            get => _allowHalfCentimetres;
            set
            {
                if (_allowHalfCentimetres == value) return;
                SetProperty(ref _allowHalfCentimetres, value);
                _persistSetting(value);

                // The grid changed, so every count in the tree is now about a different question.
                Categories.Clear();
                SkipGroups.Clear();
                HasScanned = false;
                Status = "Grid changed — scan again.";
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set { SetProperty(ref _isBusy, value); RaiseCanExecuteChanged(); }
        }

        public bool HasScanned
        {
            get => _hasScanned;
            private set { SetProperty(ref _hasScanned, value); RaiseCanExecuteChanged(); }
        }

        public string Status
        {
            get => _status;
            private set => SetProperty(ref _status, value);
        }

        public ICommand ScanCommand { get; }
        public ICommand FixAllCommand { get; }
        public ICommand FixNodeCommand { get; }
        public ICommand SelectNodeCommand { get; }
        public ICommand SelectSkipGroupCommand { get; }

        private IReadOnlyList<long> AllDirtyIds() =>
            Categories.SelectMany(c => c.ElementIds).ToList();

        private void RunScan()
        {
            // The delegates block on Revit's main thread, so calling them from the UI thread would
            // freeze the pane. Off to the pool, back through the dispatcher.
            Run(() =>
            {
                var result = _scan(AllowHalfCentimetres);
                return () =>
                {
                    Categories.Clear();
                    foreach (var node in result.GroupedByCategoryAndLevel())
                        Categories.Add(new CategoryRow(node));

                    SkipGroups.Clear();
                    foreach (var group in result.GroupedSkips())
                        SkipGroups.Add(group);
                    OnPropertyChanged(nameof(HasSkips));

                    HasScanned = true;
                    Status = result.Dirty.Count == 0
                        ? $"Nothing off grid. {result.Examined} examined, {result.AlreadyClean} already clean."
                        : $"{result.Dirty.Count} off grid, {result.AlreadyClean} already clean, " +
                          $"{result.Skipped.Count} cannot be fixed.";
                };
            });
        }

        private void RunFix(IReadOnlyList<long> ids)
        {
            if (ids.Count == 0) return;

            Run(() =>
            {
                var report = _fix(AllowHalfCentimetres, ids);
                var scan = _scan(AllowHalfCentimetres);
                return () =>
                {
                    Categories.Clear();
                    foreach (var node in scan.GroupedByCategoryAndLevel())
                        Categories.Add(new CategoryRow(node));

                    SkipGroups.Clear();
                    foreach (var group in scan.GroupedSkips())
                        SkipGroups.Add(group);
                    OnPropertyChanged(nameof(HasSkips));

                    HasScanned = true;
                    Status =
                        $"Moved {report.Moved} in {report.ElapsedMilliseconds} ms. " +
                        $"{scan.Dirty.Count} still off grid.";
                };
            });
        }

        private void Run(Func<Action> work)
        {
            IsBusy = true;
            Status = "Working…";

            Task.Run(work).ContinueWith(t =>
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                Action apply = t.IsFaulted
                    ? () => Status = $"Failed: {t.Exception?.GetBaseException().Message}"
                    : t.Result;

                Action finish = () => { apply(); IsBusy = false; };
                if (dispatcher == null) finish();
                else dispatcher.Invoke(finish);
            });
        }

        private void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
    }
}
```

- [ ] **Step 3: Add the skip grouping to ScanResult**

`ScanResult` needs the same reason-grouping `RoundingReport` already has. In `src/RVTuk.Core/RoundPositions/ScanResult.cs`, add to the `ScanResult` record:

```csharp
        /// <summary>Largest group first — on an inherited model the biggest pile is the story.</summary>
        public IReadOnlyList<SkipGroup> GroupedSkips() =>
            Skipped
                .GroupBy(s => s.Reason)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key)
                .Select(g => new SkipGroup(g.Key, SkipReasonText.Describe(g.Key), g.ToList()))
                .ToList();
```

Add a test to `ScanResultTests` pinning it:

```csharp
    [Fact]
    public void SkipsGroupByReasonLargestFirst()
    {
        var builder = new ScanResultBuilder();
        builder.RecordSkipped(1, "Furniture", SkipReason.InGroup);
        builder.RecordSkipped(2, "Walls", SkipReason.NotOrthogonal);
        builder.RecordSkipped(3, "Walls", SkipReason.NotOrthogonal);

        var groups = builder.Build().GroupedSkips();

        Assert.Equal(SkipReason.NotOrthogonal, groups[0].Reason);
        Assert.Equal(2, groups[0].Elements.Count);
    }
```

- [ ] **Step 4: Build both configurations and run the Core suite**

```bash
dotnet build RVTuk.sln -c Release2024 && dotnet build RVTuk.sln -c Release2025
dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj
```

Expected: both build; Core suite passes with the new test.

- [ ] **Step 5: Commit**

```bash
git add -A src/RVTuk tests && git commit -m "feat(round-positions): a pane view model that scans, fixes and never blocks the UI"
```

---

### Task 7: The pane view, its handlers, and retiring the modals

**Files:**
- Create: `src/RVTuk.UI/RoundPositions/Views/RoundPositionsPaneView.xaml` + `.xaml.cs`
- Create: `src/RVTuk.Revit/RoundPositions/RoundPositionsPaneProvider.cs`
- Create: `src/RVTuk.Revit/RoundPositions/ExternalEvents/ScanEventHandler.cs`, `FixEventHandler.cs`, `SelectEventHandler.cs`
- Modify: `src/RVTuk.Revit/RoundPositions/Commands/RoundPositionsCommand.cs` (opens the pane)
- Modify: `src/RVTuk.Revit/Application.cs` (register the pane, wire the delegates)
- Delete: `src/RVTuk.UI/RoundPositions/Views/RoundPositionsWindow.xaml` + `.xaml.cs`, `src/RVTuk.UI/RoundPositions/ViewModels/RoundPositionsViewModel.cs`, `src/RVTuk.UI/RoundPositions/Views/RoundPositionsResultWindow.xaml` + `.xaml.cs`, `src/RVTuk.UI/RoundPositions/ViewModels/RoundPositionsResultViewModel.cs`

**Interfaces:**
- Consumes: `RoundPositionsPaneViewModel`, `RoundPositionsRunner.Scan`/`.Fix`, `ConfigManager.LoadConfig`/`SaveConfig`.
- Produces: `RoundPositionsPaneProvider.PaneId`; `Application.RoundPositionsPaneViewModel` static.

Read `src/RVTuk.Revit/TopoTools/TopoToolsPaneProvider.cs` and the `RegisterTopoTools` block in `Application.cs` **before writing anything** — the ExternalEvent + `ManualResetEventSlim` ping-pong, the pane provider shape and the delegate wiring are all established there, and this must match rather than invent. If that branch is not in your working tree, use the `RegisterAutoDimensions` block in `Application.cs`, which has the same shape.

- [ ] **Step 1: Write the pane view**

Create `src/RVTuk.UI/RoundPositions/Views/RoundPositionsPaneView.xaml`. Match the dark-theme merge and brush keys used by `src/RVTuk.UI/RoundPositions/Views/RoundPositionsResultWindow.xaml` before you delete it:

```xml
<UserControl x:Class="RVTuk.UI.RoundPositions.Views.RoundPositionsPaneView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:RVTuk.UI.RoundPositions.ViewModels">

    <UserControl.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="/RVTuk.UI;component/Shared/Themes/DarkTheme.xaml"/>
            </ResourceDictionary.MergedDictionaries>
            <BooleanToVisibilityConverter x:Key="BoolVis"/>
        </ResourceDictionary>
    </UserControl.Resources>

    <Grid Background="{StaticResource Brush.Bg}" TextElement.Foreground="{StaticResource Brush.Text}">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <StackPanel Grid.Row="0" Margin="8">
            <CheckBox Content="Allow half centimetres" IsChecked="{Binding AllowHalfCentimetres}"/>
            <TextBlock Opacity="0.7" Margin="20,2,0,8" TextWrapping="Wrap"
                       Text="On: 0.34 → 0.5.   Off: 0.34 → 0, and 0.5 → 1."/>
            <StackPanel Orientation="Horizontal">
                <Button Content="Scan" Width="80" Height="24" Margin="0,0,8,0"
                        Command="{Binding ScanCommand}"/>
                <Button Content="Fix all" Width="80" Height="24"
                        Command="{Binding FixAllCommand}"/>
            </StackPanel>
            <TextBlock Text="{Binding Status}" Opacity="0.8" Margin="0,8,0,0" TextWrapping="Wrap"/>
        </StackPanel>

        <!-- VirtualizingStackPanel with CanContentScroll: a level can hold hundreds of element
             rows, and a plain ItemsControl in a ScrollViewer realises every one at expand time. -->
        <TreeView Grid.Row="1" ItemsSource="{Binding Categories}" Margin="8,0"
                  Background="Transparent" BorderThickness="0"
                  VirtualizingStackPanel.IsVirtualizing="True"
                  VirtualizingStackPanel.VirtualizationMode="Recycling"
                  ScrollViewer.CanContentScroll="True">
            <TreeView.Resources>
                <HierarchicalDataTemplate DataType="{x:Type vm:CategoryRow}" ItemsSource="{Binding Levels}">
                    <StackPanel Orientation="Horizontal">
                        <TextBlock Text="{Binding Label}" FontWeight="SemiBold"/>
                        <TextBlock Opacity="0.7" Margin="6,0,0,0">
                            <Run Text="("/><Run Text="{Binding Count, Mode=OneWay}"/><Run Text=")"/>
                        </TextBlock>
                        <Button Content="Fix" Padding="6,0" Margin="8,0,0,0"
                                Command="{Binding DataContext.FixNodeCommand,
                                          RelativeSource={RelativeSource AncestorType=TreeView}}"
                                CommandParameter="{Binding}"/>
                        <Button Content="Select" Padding="6,0" Margin="4,0,0,0"
                                Command="{Binding DataContext.SelectNodeCommand,
                                          RelativeSource={RelativeSource AncestorType=TreeView}}"
                                CommandParameter="{Binding}"/>
                    </StackPanel>
                </HierarchicalDataTemplate>

                <HierarchicalDataTemplate DataType="{x:Type vm:LevelRow}" ItemsSource="{Binding Elements}">
                    <StackPanel Orientation="Horizontal">
                        <TextBlock Text="{Binding Label}"/>
                        <TextBlock Opacity="0.7" Margin="6,0,0,0">
                            <Run Text="("/><Run Text="{Binding Count, Mode=OneWay}"/><Run Text=")"/>
                        </TextBlock>
                        <Button Content="Fix" Padding="6,0" Margin="8,0,0,0"
                                Command="{Binding DataContext.FixNodeCommand,
                                          RelativeSource={RelativeSource AncestorType=TreeView}}"
                                CommandParameter="{Binding}"/>
                        <Button Content="Select" Padding="6,0" Margin="4,0,0,0"
                                Command="{Binding DataContext.SelectNodeCommand,
                                          RelativeSource={RelativeSource AncestorType=TreeView}}"
                                CommandParameter="{Binding}"/>
                    </StackPanel>
                </HierarchicalDataTemplate>

                <DataTemplate DataType="{x:Type vm:ElementRow}">
                    <StackPanel Orientation="Horizontal">
                        <TextBlock Text="{Binding Label}" Opacity="0.8"/>
                        <Button Content="Fix" Padding="6,0" Margin="8,0,0,0"
                                Command="{Binding DataContext.FixNodeCommand,
                                          RelativeSource={RelativeSource AncestorType=TreeView}}"
                                CommandParameter="{Binding}"/>
                        <Button Content="Select" Padding="6,0" Margin="4,0,0,0"
                                Command="{Binding DataContext.SelectNodeCommand,
                                          RelativeSource={RelativeSource AncestorType=TreeView}}"
                                CommandParameter="{Binding}"/>
                    </StackPanel>
                </DataTemplate>
            </TreeView.Resources>
        </TreeView>

        <Expander Grid.Row="2" Margin="8" Header="Cannot be fixed" IsExpanded="True"
                  Foreground="{StaticResource Brush.Text}"
                  Visibility="{Binding HasSkips, Converter={StaticResource BoolVis}}">
            <ItemsControl ItemsSource="{Binding SkipGroups}" Margin="0,6,0,0">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <DockPanel Margin="0,1">
                            <Button DockPanel.Dock="Right" Content="Select" Padding="6,0"
                                    Command="{Binding DataContext.SelectSkipGroupCommand,
                                              RelativeSource={RelativeSource AncestorType=UserControl}}"
                                    CommandParameter="{Binding}"/>
                            <TextBlock Opacity="0.8" TextWrapping="Wrap">
                                <Run Text="{Binding Elements.Count, Mode=OneWay}"/>
                                <Run Text="—"/>
                                <Run Text="{Binding Description, Mode=OneWay}"/>
                            </TextBlock>
                        </DockPanel>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </Expander>
    </Grid>
</UserControl>
```

Create the code-behind `RoundPositionsPaneView.xaml.cs` with just `InitializeComponent()`.

- [ ] **Step 2: Write the three event handlers**

Read `src/RVTuk.Revit/AutoDimensions/ExternalEvents/LevelDiscoveryEventHandler.cs` first and match
its `Reset` / `Prepare` / `Execute` / `WaitForCompletion` shape — if it differs from what follows,
the real file wins.

Create `src/RVTuk.Revit/RoundPositions/ExternalEvents/ScanEventHandler.cs`:

```csharp
using System;
using System.Threading;
using Autodesk.Revit.UI;
using RVTuk.Core.RoundPositions;

namespace RVTuk.Revit.RoundPositions.ExternalEvents
{
    /// <summary>
    /// The scan is read-only, but the Revit API may still only be touched on Revit's own thread,
    /// so it goes through an external event like everything else.
    /// </summary>
    public class ScanEventHandler : IExternalEventHandler
    {
        private readonly ManualResetEventSlim _done = new ManualResetEventSlim(false);

        private bool _allowHalfCentimetres;

        public ScanResult? Result { get; private set; }

        public Exception? Error { get; private set; }

        public void Prepare(bool allowHalfCentimetres)
        {
            _allowHalfCentimetres = allowHalfCentimetres;
            Result = null;
            Error = null;
            _done.Reset();
        }

        public void Execute(UIApplication app)
        {
            try
            {
                Result = RoundPositionsRunner.Scan(app.ActiveUIDocument.Document, _allowHalfCentimetres);
            }
            catch (Exception ex)
            {
                // Never let this escape: an unhandled throw here kills the event loop, and the
                // caller would block on WaitForCompletion forever.
                Error = ex;
            }
            finally
            {
                _done.Set();
            }
        }

        public void WaitForCompletion() => _done.Wait();

        public string GetName() => "RVTuk Round Positions scan";
    }
}
```

`FixEventHandler` is the same shape: `Prepare(bool allowHalfCentimetres, ISet<long> ids)`, calls
`RoundPositionsRunner.Fix`, exposes `RoundingReport? Report` and `Exception? Error`. The `Error`
member is not optional — `Fix` throws by design when Revit rolls the transaction back, and that
message must reach the pane's status line rather than being swallowed.

`SelectEventHandler` is simpler: `Prepare(IReadOnlyList<long> ids)`, and `Execute` does
`app.ActiveUIDocument.Selection.SetElementIds(ids.Select(id => new ElementId(id)).ToList())`.

- [ ] **Step 3: Register the pane and wire the delegates**

In `Application.cs`, add `private static readonly bool RegisterRoundPositions = true;` next to the
other flags, static properties for the three handlers and their events, and this block inside
`OnStartup`, modelled on `RegisterAutoDimensions`:

```csharp
            if (RegisterRoundPositions)
            {
                RoundScanHandler = new ScanEventHandler();
                RoundScanEvent   = ExternalEvent.Create(RoundScanHandler);
                RoundFixHandler  = new FixEventHandler();
                RoundFixEvent    = ExternalEvent.Create(RoundFixHandler);
                RoundSelectHandler = new SelectEventHandler();
                RoundSelectEvent   = ExternalEvent.Create(RoundSelectHandler);

                // The pane (UI project) only ever sees these delegates — no Revit type crosses over.
                // All three block on WaitForCompletion, so the view model calls them from the pool.
                Func<bool, ScanResult> scan = allowHalf =>
                {
                    RoundScanHandler.Prepare(allowHalf);
                    RoundScanEvent.Raise();
                    RoundScanHandler.WaitForCompletion();
                    if (RoundScanHandler.Error != null) throw RoundScanHandler.Error;
                    return RoundScanHandler.Result!;
                };

                Func<bool, IReadOnlyList<long>, RoundingReport> fix = (allowHalf, ids) =>
                {
                    RoundFixHandler.Prepare(allowHalf, new HashSet<long>(ids));
                    RoundFixEvent.Raise();
                    RoundFixHandler.WaitForCompletion();
                    if (RoundFixHandler.Error != null) throw RoundFixHandler.Error;
                    return RoundFixHandler.Report!;
                };

                Action<IReadOnlyList<long>> selectInModel = ids =>
                {
                    RoundSelectHandler.Prepare(ids);
                    RoundSelectEvent.Raise();
                    RoundSelectHandler.WaitForCompletion();
                };

                // Stored inverted: the config default (false) is "half centimetres allowed".
                var config = ConfigManager.LoadConfig();
                Action<bool> persist = allowHalf =>
                {
                    var current = ConfigManager.LoadConfig();
                    current.RoundPositionsWholeCentimetresOnly = !allowHalf;
                    ConfigManager.SaveConfig(current);
                };

                RoundPositionsPaneViewModel =
                    new RVTuk.UI.RoundPositions.ViewModels.RoundPositionsPaneViewModel(
                        scan, fix, selectInModel,
                        !config.RoundPositionsWholeCentimetresOnly, persist);

                var roundView = new RVTuk.UI.RoundPositions.Views.RoundPositionsPaneView
                {
                    DataContext = RoundPositionsPaneViewModel
                };
                application.RegisterDockablePane(
                    RoundPositionsPaneProvider.PaneId,
                    "Round Positions",
                    new RoundPositionsPaneProvider(roundView));
            }
```

**The negation appears twice above and getting either wrong is silent** — `false` in the config
means "half centimetres allowed". Check both the load (`!config.…`) and the save (`= !allowHalf`).

- [ ] **Step 4: Point the command at the pane**

Replace `RoundPositionsCommand.Execute`'s body with the pane-showing form used by
`src/RVTuk.Revit/AutoDimensions/Commands/AutoDimensionsPaneCommand.cs` — get the pane by id and
call `Show()`.

- [ ] **Step 5: Delete the two modal windows and their view models**

```bash
git rm src/RVTuk.UI/RoundPositions/Views/RoundPositionsWindow.xaml src/RVTuk.UI/RoundPositions/Views/RoundPositionsWindow.xaml.cs src/RVTuk.UI/RoundPositions/ViewModels/RoundPositionsViewModel.cs src/RVTuk.UI/RoundPositions/Views/RoundPositionsResultWindow.xaml src/RVTuk.UI/RoundPositions/Views/RoundPositionsResultWindow.xaml.cs src/RVTuk.UI/RoundPositions/ViewModels/RoundPositionsResultViewModel.cs
```

- [ ] **Step 6: Build both configurations**

```bash
dotnet build RVTuk.sln -c Release2024 && dotnet build RVTuk.sln -c Release2025
```

Expected: both succeed, 0 errors, no warnings from new files.

- [ ] **Step 7: Verify the pane renders out-of-process**

A green build does not check `StaticResource` lookups into a merged dictionary, and it does not
check that a `HierarchicalDataTemplate` matches its type. Load the built net8 `RVTuk.UI.dll` in an
STA host, construct `RoundPositionsPaneView` with a `RoundPositionsPaneViewModel` over stub
delegates and a fabricated `ScanResult` of several categories, levels and elements, lay it out, and
confirm: all three tiers realise; every brush key resolves; the WPF binding trace is empty. Report
the observations, not conclusions.

- [ ] **Step 8: Commit**

```bash
git add -A src && git commit -m "feat(round-positions): the tool becomes a pane you scan, read and fix from"
```

---

## Phase D — the remaining categories

Each task here is a **reader plus one registry line**. The machinery is done.

### Task 8: Structural framing, model and detail lines

**Files:** Modify `src/RVTuk.Revit/RoundPositions/PassRegistry.cs`.

Both reuse `LineReader.Read` unchanged. Add to `PassRegistry.All`, after the walls entry:

```csharp
            new CategoryPass(FixTier.Framing, "Structural Framing",
                doc => OfCategory(doc, BuiltInCategory.OST_StructuralFraming), LineReader.Read),

            // Includes the _DP-Dim and _DP-Topo Line styles Auto Dimensions and Topo Tools read as
            // input — cleaner input for both, but a fix should be followed by a re-run of those.
            new CategoryPass(FixTier.DraftingLines, "Model and Detail Lines",
                doc => new FilteredElementCollector(doc)
                    .OfClass(typeof(CurveElement))
                    .WhereElementIsNotElementType()
                    .ToList(),
                LineReader.Read),
```

- [ ] Build both configurations. Expected: 0 errors.
- [ ] In-Revit (user): an orthogonal beam moves; a diagonal brace appears under "Not orthogonal"; a detail line moves within its view without changing its sketch plane.
- [ ] Commit: `feat(round-positions): beams and drafting lines take the wall's path`

### Task 9: Levels

**Files:** Create `src/RVTuk.Revit/RoundPositions/Readers/LevelReader.cs`; modify `PassRegistry.cs`.

A level moves by having its `Elevation` written, not by being transformed, so `BatchMover`'s
`MoveElement` cannot serve it. Give `CategoryPass` an optional applier override:

```csharp
using System;
using Autodesk.Revit.DB;
using RVTuk.Core.RoundPositions;

namespace RVTuk.Revit.RoundPositions.Readers
{
    /// <summary>
    /// The largest single change the tool makes: every wall, floor and family hosted on a level
    /// rides along with it.
    /// </summary>
    public static class LevelReader
    {
        public static OffsetVerdict Read(Document doc, Element element, double step)
        {
            if (!(element is Level level)) return OffsetVerdict.Cannot(SkipReason.RevitRejected);

            var current = GridSnapper.FromFeet(level.Elevation);
            var snapped = GridSnapper.Snap(current, step);
            if (Math.Abs(snapped - current) <= GridSnapper.EpsilonCentimetres) return OffsetVerdict.Clean();

            // Z-only: MoveElement on a Level is not supported, so the applier writes Elevation
            // directly. The translation is carried anyway so the scan and fix agree on the delta.
            return OffsetVerdict.Move(new XYZ(0, 0, GridSnapper.ToFeet(snapped - current)));
        }

        public static bool Apply(Document doc, Element element, XYZ translation, RoundingReportBuilder report)
        {
            var level = (Level)element;
            var target = level.Elevation + translation.Z;
            return MoveAttempt.TryApply(doc, level, () => level.Elevation = target, report);
        }
    }
}
```

Add an optional `Func<Document, Element, XYZ, RoundingReportBuilder, bool>? applyOne` parameter to
`CategoryPass`'s constructor; when it is non-null, `Apply` calls it per element instead of
collecting into `BatchMover`. Register:

```csharp
            new CategoryPass(FixTier.Levels, "Levels",
                doc => new FilteredElementCollector(doc).OfClass(typeof(Level))
                    .WhereElementIsNotElementType().ToList(),
                LevelReader.Read,
                LevelReader.Apply),
```

- [ ] Build both configurations.
- [ ] In-Revit (user): a level at a non-round elevation becomes round; **everything hosted on it moved with it** — confirm in a section; elements with an explicit offset kept their offset; one Ctrl+Z restores all of it.
- [ ] Commit: `feat(round-positions): levels land on the grid, and the building follows`

### Task 10: Grids and reference planes

**Files:** Create `src/RVTuk.Revit/RoundPositions/Readers/DatumReader.cs`; modify `PassRegistry.cs`.

`Grid.Curve` and `ReferencePlane`'s ends are read-only, so these move by transform — which
`BatchMover` already does. Only the reading differs:

```csharp
using Autodesk.Revit.DB;
using RVTuk.Core.RoundPositions;

namespace RVTuk.Revit.RoundPositions.Readers
{
    /// <summary>Grids and reference planes: their geometry is read-only, so the offset Core
    /// computes is applied as a translation rather than by assigning a curve.</summary>
    public static class DatumReader
    {
        public static OffsetVerdict Read(Document doc, Element element, double step)
        {
            GridPoint start, end;
            switch (element)
            {
                case Grid grid when grid.Curve is Line line:
                    start = ToGridPoint(line.GetEndPoint(0));
                    end = ToGridPoint(line.GetEndPoint(1));
                    break;
                case ReferencePlane plane:
                    start = ToGridPoint(plane.BubbleEnd);
                    end = ToGridPoint(plane.FreeEnd);
                    break;
                default:
                    return OffsetVerdict.Cannot(SkipReason.NotOrthogonal);
            }

            var offset = LineOffset.For(start, end, step, AxisClassifier.ToleranceCentimetres);
            switch (offset.Outcome)
            {
                case LineOffsetOutcome.AlreadyOnGrid: return OffsetVerdict.Clean();
                case LineOffsetOutcome.NotOrthogonal: return OffsetVerdict.Cannot(SkipReason.NotOrthogonal);
                default:
                    return OffsetVerdict.Move(new XYZ(
                        GridSnapper.ToFeet(offset.DeltaX), GridSnapper.ToFeet(offset.DeltaY), 0));
            }
        }

        private static GridPoint ToGridPoint(XYZ point) => new GridPoint(
            GridSnapper.FromFeet(point.X), GridSnapper.FromFeet(point.Y), GridSnapper.FromFeet(point.Z));
    }
}
```

Register two entries at `FixTier.Datums` — "Grids" over `typeof(Grid)` and "Reference Planes" over
`typeof(ReferencePlane)`, both using `DatumReader.Read`.

- [ ] Build both configurations.
- [ ] In-Revit (user): an orthogonal grid moves and keeps its bubbles and 3D extents; a reference plane moves; note whether elements hosted on a reference plane move with it; a diagonal grid is reported.
- [ ] Commit: `feat(round-positions): datums move by transform, not by curve`

### Task 11: Point-positioned families

**Files:** Create `src/RVTuk.Revit/RoundPositions/Readers/PointInstanceReader.cs`; modify `PassRegistry.cs`.

```csharp
using System;
using Autodesk.Revit.DB;
using RVTuk.Core.RoundPositions;

namespace RVTuk.Revit.RoundPositions.Readers
{
    /// <summary>Unhosted family instances with a LocationPoint. Hosted ones slide along their wall
    /// instead and are handled separately.</summary>
    public static class PointInstanceReader
    {
        /// <summary>Within half a degree of a multiple of 45° counts as meant to be square. A
        /// family deliberately placed at 37.4° is left alone.</summary>
        public const double RotationToleranceDegrees = 0.5;

        public static OffsetVerdict Read(Document doc, Element element, double step)
        {
            if (!(element.Location is LocationPoint location)) return OffsetVerdict.Cannot(SkipReason.RevitRejected);

            var point = location.Point;
            var dx = GridSnapper.ToFeet(
                GridSnapper.Snap(GridSnapper.FromFeet(point.X), step) - GridSnapper.FromFeet(point.X));
            var dy = GridSnapper.ToFeet(
                GridSnapper.Snap(GridSnapper.FromFeet(point.Y), step) - GridSnapper.FromFeet(point.Y));
            var dz = GridSnapper.ToFeet(
                GridSnapper.Snap(GridSnapper.FromFeet(point.Z), step) - GridSnapper.FromFeet(point.Z));

            var translation = new XYZ(dx, dy, dz);
            return translation.GetLength() <= GridSnapper.ToFeet(GridSnapper.EpsilonCentimetres)
                ? OffsetVerdict.Clean()
                : OffsetVerdict.Move(translation);
        }
    }
}
```

Register at `FixTier.PointFamilies`, category "Families", collecting `FamilyInstance` where
`Host == null && Location is LocationPoint`.

> **Rotation snapping is deliberately not in this task.** It is a different operation from a
> translation and does not fit `BatchMover`'s move-by-vector shape. Add it as a backlog item in
> Task 13 rather than bolting it on here.

- [ ] Build both configurations.
- [ ] In-Revit (user): furniture at 1.001, 5.001 lands at 1, 5; a family's Z lands on grid and it stays on its level; a pinned family moves and stays pinned; a grouped one is reported.
- [ ] Commit: `feat(round-positions): point families land on the grid`

### Task 12: Wall-hosted openings

**Files:** Create `src/RVTuk.Revit/RoundPositions/Readers/HostedInstanceReader.cs`; modify `PassRegistry.cs`.

Runs last, so its host walls are already resolved by the tier order.

```csharp
using System;
using Autodesk.Revit.DB;
using RVTuk.Core.RoundPositions;

namespace RVTuk.Revit.RoundPositions.Readers
{
    /// <summary>
    /// Doors and windows are never moved in X/Y independently — on any wall that is not
    /// axis-aligned that would push them out of their host. Each slides along its own wall instead.
    /// </summary>
    public static class HostedInstanceReader
    {
        public static OffsetVerdict Read(Document doc, Element element, double step)
        {
            if (!(element is FamilyInstance instance) ||
                !(instance.Host is Wall wall) ||
                !(instance.Location is LocationPoint location) ||
                !(wall.Location is LocationCurve hostCurve) ||
                !(hostCurve.Curve is Line line))
            {
                return OffsetVerdict.Cannot(SkipReason.NotOrthogonal);
            }

            var origin = line.GetEndPoint(0);
            var direction = (line.GetEndPoint(1) - origin).Normalize();
            var alongCm = GridSnapper.FromFeet((location.Point - origin).DotProduct(direction));
            var shiftCm = GridSnapper.Snap(alongCm, step) - alongCm;

            return Math.Abs(shiftCm) <= GridSnapper.EpsilonCentimetres
                ? OffsetVerdict.Clean()
                : OffsetVerdict.Move(direction.Multiply(GridSnapper.ToFeet(shiftCm)));
        }
    }
}
```

Register at `FixTier.HostedFamilies`, category "Doors and Windows", collecting `FamilyInstance`
where `Host is Wall && Location is LocationPoint`.

- [ ] Build both configurations.
- [ ] In-Revit (user): a door in an orthogonal wall slides so its distance from the wall's start is round and **stays in the wall**; a door in a crooked wall is reported; a window locked by a dimension moves and the dimension appears under constraints released.
- [ ] Commit: `feat(round-positions): openings slide along their wall, never off it`

### Task 13: Base points

**Files:** Create `src/RVTuk.Revit/RoundPositions/Readers/BasePointReader.cs`; modify `PassRegistry.cs`; add backlog items.

The highest-consequence pass, and the one that refuses anything it does not fully understand.

```csharp
using Autodesk.Revit.DB;
using RVTuk.Core.RoundPositions;

namespace RVTuk.Revit.RoundPositions.Readers
{
    /// <summary>
    /// The Project Base Point and Survey Point. Because the grid is anchored to Revit's internal
    /// origin, a base point at a non-round offset leaves every project coordinate dirty even after
    /// a clean run — snapping the points is what makes project coordinates round too.
    ///
    /// Moving the Survey Point is not a local change: every linked model positioned by shared
    /// coordinates shifts relative to this one. So only an UNCLIPPED point is touched, where the
    /// move relocates the marker rather than the coordinate system.
    /// </summary>
    public static class BasePointReader
    {
        public static OffsetVerdict Read(Document doc, Element element, double step)
        {
            if (!(element is BasePoint point)) return OffsetVerdict.Cannot(SkipReason.RevitRejected);

            var clipped = point.get_Parameter(BuiltInParameter.BASEPOINT_CLIPPED_PARAM);
            if (clipped == null || clipped.AsInteger() != 0)
                return OffsetVerdict.Cannot(SkipReason.UnexpectedClipState);

            var position = point.Position;
            var translation = new XYZ(
                GridSnapper.ToFeet(GridSnapper.Snap(GridSnapper.FromFeet(position.X), step) - GridSnapper.FromFeet(position.X)),
                GridSnapper.ToFeet(GridSnapper.Snap(GridSnapper.FromFeet(position.Y), step) - GridSnapper.FromFeet(position.Y)),
                GridSnapper.ToFeet(GridSnapper.Snap(GridSnapper.FromFeet(position.Z), step) - GridSnapper.FromFeet(position.Z)));

            return translation.GetLength() <= GridSnapper.ToFeet(GridSnapper.EpsilonCentimetres)
                ? OffsetVerdict.Clean()
                : OffsetVerdict.Move(translation);
        }
    }
}
```

Register **first** in `PassRegistry.All`, at `FixTier.BasePoints`, category "Base Points", over
`typeof(BasePoint)`.

- [ ] Build both configurations.
- [ ] **In-Revit (user) — on a throwaway copy. This checklist decides whether the pass ships.**
  - [ ] Confirm what `BASEPOINT_CLIPPED_PARAM` actually reads for each point in both states. If it is not 0-for-unclipped, fix the test before going further.
  - [ ] An unclipped Project Base Point at a non-round position moves onto the grid, and **the model geometry does not move**.
  - [ ] Spot-coordinate and spot-elevation tags now read round numbers.
  - [ ] A clipped point is reported under "unexpected clip state".
  - [ ] With a linked model positioned by shared coordinates: confirm whether it shifted. **If it did, stop and report** — the pass must then refuse Survey Point moves in coordinated models.
- [ ] Add to `docs/tools/round-positions/backlog.md`: rotation snapping for point families (45° multiples within 0.5°), deferred because it is not a translation and does not fit the batch mover.
- [ ] Commit: `feat(round-positions): snap the base points, and refuse the ones we don't understand`

---

## Phase E — documentation

### Task 14: Docs, backlog and registration

**Files:**
- Rewrite: `docs/tools/round-positions/README.md`
- Modify: `docs/tools/round-positions/backlog.md`
- Modify: `CLAUDE.md` (tool list, canonical names, v1 launch surface)
- Modify: `docs/BACKLOG.md` (per-tool index)

- [ ] **Step 1: Rewrite the README** to describe the pane — scan, the Category → Level → element tree in dependency order, Fix buttons on every tier, Fix all, and the "cannot be fixed" list as the v2 queue. Name the entry points: `Commands/RoundPositionsCommand.cs`, `RoundPositionsRunner.cs`, `PassRegistry.cs` (where a new category is added), `src/RVTuk.Core/RoundPositions/`.

- [ ] **Step 2: Update the backlog** — carry over the v1 plan's v2 list (straightening near-orthogonal lines, wall lengths, sketch-based elements, arcs, stairs and railings), add the measurement from Task 5, and add the items the v1 reviews surfaced: element rows need virtualization if the tree is ever flattened, adjacent `<Run>`s pick up XAML indentation whitespace, released constraints have no per-row select, rotation snapping for point families.

- [ ] **Step 3: Update CLAUDE.md** — add `RoundPositions` to the canonical tool-name list, add Round Positions to the v1 launch surface sentence, and add a Features bullet:

```markdown
- **Round Positions** (ribbon "Round Positions") — a dockable pane that scans the active model for elements off a whole-centimetre grid and fixes them by group. A checkbox chooses whether half centimetres are allowed. **It translates only — it never reshapes**: lengths, angles and sketches are untouched, and anything not orthogonal is reported rather than straightened. The tree runs Category → Level → element in dependency order (base points, levels, datums, walls, framing, lines, families, openings), because fixing walls before levels means the level fix moves them again. Fixes apply in batches inside one sub-transaction — one regeneration per batch, not per element — falling back to per-element only inside a batch that failed. Revit 2024/25 only. See [`docs/tools/round-positions/README.md`](docs/tools/round-positions/README.md).
```

- [ ] **Step 4: Update `docs/BACKLOG.md`** — add `- [Round Positions](tools/round-positions/backlog.md)` to the per-tool list.

- [ ] **Step 5: Full verification**

```bash
dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj
dotnet build RVTuk.sln -c Release2024 && dotnet build RVTuk.sln -c Release2025
```

- [ ] **Step 6: Commit** — `docs(round-positions): scan, read the tree, fix what you choose`

---

## Deviations from the spec recorded while planning

1. **Rotation snapping for point families is deferred to the backlog.** The spec has it in *What v1
   moves*. It is a rotation, not a translation, so it does not fit `BatchMover`'s move-by-vector
   shape, and bolting a second operation onto the batch path for one category is not worth it.
   Task 13 records it as a backlog item.
2. **Levels need an applier override.** The spec treats every category the same. A level moves by
   having `Elevation` written, not by `MoveElement`, so `CategoryPass` takes an optional per-element
   applier — used by levels alone.

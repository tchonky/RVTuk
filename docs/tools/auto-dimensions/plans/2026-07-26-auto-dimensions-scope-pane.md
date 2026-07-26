# Auto Dimensions — Scope Pane Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a dockable Auto Dimensions pane that lets the user pick which categories
(Walls/Doors/Windows) act as dimension references and which views of each level receive the
dimensions, then fans the existing per-line pipeline out across every selected (level → view)
pair from one "Create Dimensions" click.

**Architecture:** All new pure logic (level/view scope models, the category bitmask, the
opening-segment geometry, and the two persisted-string codecs) lives in `RVTuk.Core` and is
unit-tested with plain data — no Revit session needed. `RVTuk.Revit` gains a level/reference-view
discovery pass, a category-tagged candidate collector, and an extracted `DimensionRunner` holding
the per-line pipeline that both the existing ribbon command and the new pane run through, so the
two entry points can't drift. `RVTuk.UI` gains an MVVM pane whose only contact with Revit is two
plain delegates handed in by the host, each wrapping an `ExternalEvent` + `ManualResetEventSlim`
ping-pong (raised from a background thread, never from the WPF UI thread).

**Tech Stack:** C#, Revit API (`ViewPlan.GenLevel`, `FilteredElementCollector`, `HostObjectUtils`,
`FamilyInstance.GetReferences`, `Document.Create.NewDimension`, `ExtensibleStorage`,
`IDockablePaneProvider`, `IExternalEventHandler`), WPF (MVVM), xunit.

## Global Constraints

- `RVTuk.Core` must stay free of Revit API and WPF types; `RVTuk.UI` must stay free of Revit API
  types (existing architecture rule in `CLAUDE.md`). Revit interactions reach the UI as plain
  `Func<>`/`Action` delegates.
- Namespace = root namespace + folder path, exactly (`CLAUDE.md` rule). New folders:
  `src/RVTuk.UI/AutoDimensions/{ViewModels,Views}`,
  `src/RVTuk.Revit/AutoDimensions/ExternalEvents`.
- V1 category scope: **Walls, Doors, Windows** only. Ceilings and Floors appear in the checklist
  **disabled**, tooltip **"Coming in v2"**, and must never reach the run logic
  (`CategoryMask.FromMask` clamps them off).
- Views with no `Level` (3D, sections, drafting, schedules, sheets) are excluded from discovery
  entirely — only non-template `ViewPlan`s with a non-null `GenLevel` are ever levels' views.
- If more than one view under a level owns `Dimensions_Line` lines, the **first by ascending
  `ElementId`** is that level's reference view (arbitrary but deterministic).
- Reference-line lookup is always the document-wide `CurveElement` collector filtered by
  `OwnerViewId` — never a view-scoped collector, because lines hidden by a view template must
  still count (existing rule from the 2026-07-04 spec).
- The whole Create Dimensions run is **one transaction**, including the selection write; a hard
  failure rolls back everything.
- Deleting a line's previously-tracked dimension for the target view happens **unconditionally**,
  before checking whether the line currently has any crossings.
- Build both `Release2024` and `Release2025` before considering a Revit/UI task done; the final
  task also builds `Release2023` (Core + UI + KKarea) since Core and UI changes ship there too.
- Branch: create `auto-dimensions-scope-pane` off `master` before Task 1. **The working tree
  currently holds uncommitted Family Browser work, including `src/RVTuk.Revit/Application.cs` and
  `src/RVTuk.Revit/RVTuk.Revit.csproj` — commit or stash it first**, because Task 11 edits
  `Application.cs`.
- Full spec: `docs/tools/auto-dimensions/specs/2026-07-23-auto-dimensions-scope-pane-design.md`.

### Three deliberate deviations from the spec (decided here, in the plan)

1. **Extensible Storage field types.** The spec says the tracker becomes a `Map<long,long>` and the
   selection store uses `AddArrayField<long>`. Extensible Storage's documented simple/key types are
   `bool, short, int, double, float, string, Guid, ElementId, XYZ, UV, Entity` — `long` is not among
   them, and the tracker's *existing* `AddSimpleField(…, typeof(long))` has never been verified in a
   live Revit session (the tool is unreleased; `docs/tools/auto-dimensions/backlog.md` still lists
   the in-Revit pass as pending). Both are therefore stored as **`string`** fields encoded by pure
   Core codecs (`IdMapCodec`, `IdListCodec`) — unambiguously supported by ES, and the round-trip
   becomes unit-testable in `RVTuk.Core.Tests`, which the spec's Testing section anticipated
   ("a persisted-selection round-trip helper, if one emerges during implementation").
2. **The Walls checkbox is honoured, not implied.** The spec's data-flow line says "walls always",
   but its Goals list Walls among the "checked/wired up in v1" checkboxes. A checkbox that does
   nothing is worse than either, so all three checkboxes are honoured at run time and the *default*
   (no persisted selection) is Walls + Doors + Windows checked. `CreateDimensionsCommand` is
   disabled when no category, or no view, is checked.
3. **`ScopeViewInfo`, not `ViewInfo`.** Same record, renamed to keep it unambiguous inside the
   Revit project, where `using Autodesk.Revit.DB;` is always in scope.

Also note: the existing ribbon `AutoDimensionsCommand` keeps its exact behaviour (active view,
**walls only**) per the spec's non-goal "it stays as-is" — it just routes through the extracted
`DimensionRunner` so the pipeline lives in one place.

---

## File structure

**Create — `RVTuk.Core` (pure, unit-tested):**

| File | Responsibility |
|------|----------------|
| `src/RVTuk.Core/AutoDimensions/ScopeViewInfo.cs` | One view's id + name, as plain data |
| `src/RVTuk.Core/AutoDimensions/LevelScope.cs` | One level: id, name, reference-view id, its views |
| `src/RVTuk.Core/AutoDimensions/AutoDimensionsScope.cs` | Discovery result: levels + persisted selection |
| `src/RVTuk.Core/AutoDimensions/DimensionCategories.cs` | `[Flags]` category enum + `CategoryMask` encode/decode/clamp |
| `src/RVTuk.Core/AutoDimensions/OpeningSegment.cs` | Door/window centre + host direction + width → `WallCandidate` |
| `src/RVTuk.Core/AutoDimensions/IdListCodec.cs` | `IReadOnlyList<long>` ↔ CSV string |
| `src/RVTuk.Core/AutoDimensions/IdMapCodec.cs` | `IReadOnlyDictionary<long,long>` ↔ `"k:v;k:v"` string |

**Create — `RVTuk.Revit`:**

| File | Responsibility |
|------|----------------|
| `src/RVTuk.Revit/AutoDimensions/LevelScopeFinder.cs` | Levels → their plan views → which one owns `Dimensions_Line` lines |
| `src/RVTuk.Revit/AutoDimensions/DimensionCandidateCollector.cs` | Visible elements of the checked categories → category-tagged 2D candidates |
| `src/RVTuk.Revit/AutoDimensions/DimensionRunner.cs` | The per-line pipeline (shared by command + pane) |
| `src/RVTuk.Revit/AutoDimensions/ScopeSelectionStore.cs` | ES read/write of category mask + checked view ids on `ProjectInformation` |
| `src/RVTuk.Revit/AutoDimensions/ExternalEvents/LevelDiscoveryEventHandler.cs` | Discovery, marshalled to Revit's thread |
| `src/RVTuk.Revit/AutoDimensions/ExternalEvents/CreateDimensionsEventHandler.cs` | The fan-out run, one transaction, summary |
| `src/RVTuk.Revit/AutoDimensions/AutoDimensionsPaneProvider.cs` | `IDockablePaneProvider` + stable pane guid |
| `src/RVTuk.Revit/AutoDimensions/AutoDimensionsPaneCommand.cs` | Ribbon command: show pane + refresh |

**Create — `RVTuk.UI`:**

| File | Responsibility |
|------|----------------|
| `src/RVTuk.UI/AutoDimensions/ViewModels/CategoryOptionViewModel.cs` | One category checkbox |
| `src/RVTuk.UI/AutoDimensions/ViewModels/ViewNodeViewModel.cs` | One checkable target view |
| `src/RVTuk.UI/AutoDimensions/ViewModels/LevelNodeViewModel.cs` | One level row + its views + cascade |
| `src/RVTuk.UI/AutoDimensions/ViewModels/AutoDimensionsPaneViewModel.cs` | Pane state, discovery/run orchestration |
| `src/RVTuk.UI/AutoDimensions/Views/AutoDimensionsPaneView.xaml(.cs)` | The pane layout |

**Modify:** `src/RVTuk.Revit/AutoDimensions/AutoDimensionTracker.cs`,
`src/RVTuk.Revit/AutoDimensions/AutoDimensionsCommand.cs`, `src/RVTuk.Revit/Application.cs`,
`docs/tools/auto-dimensions/README.md`, `docs/tools/auto-dimensions/backlog.md`, `CLAUDE.md`.

---

### Task 1: Core — scope models + category bitmask

**Files:**
- Create: `src/RVTuk.Core/AutoDimensions/ScopeViewInfo.cs`
- Create: `src/RVTuk.Core/AutoDimensions/LevelScope.cs`
- Create: `src/RVTuk.Core/AutoDimensions/AutoDimensionsScope.cs`
- Create: `src/RVTuk.Core/AutoDimensions/DimensionCategories.cs`
- Test: `tests/RVTuk.Core.Tests/AutoDimensions/CategoryMaskTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `record ScopeViewInfo(long ViewId, string ViewName)`
  - `record LevelScope(long LevelId, string LevelName, long? ReferenceViewId, IReadOnlyList<ScopeViewInfo> Views)`
    with computed `bool HasReferenceView => ReferenceViewId.HasValue`
  - `record ScopeSelection(int CategoryMask, IReadOnlyList<long> CheckedViewIds)`
  - `record AutoDimensionsScope(IReadOnlyList<LevelScope> Levels, ScopeSelection? Selection)`
  - `[Flags] enum DimensionCategories { None = 0, Walls = 1, Doors = 2, Windows = 4, Ceilings = 8, Floors = 16 }`
  - `static class CategoryMask` with
    `const DimensionCategories Supported`, `const DimensionCategories Default`,
    `int ToMask(DimensionCategories)`, `DimensionCategories FromMask(int)`.
  - Consumed by Tasks 7, 8, 9, 10, 11.

- [ ] **Step 1: Write the failing test**

Create `tests/RVTuk.Core.Tests/AutoDimensions/CategoryMaskTests.cs`:

```csharp
using RVTuk.Core.AutoDimensions;
using Xunit;

namespace RVTuk.Core.Tests.AutoDimensions;

public class CategoryMaskTests
{
    [Fact]
    public void DefaultIsWallsDoorsWindows()
    {
        Assert.Equal(
            DimensionCategories.Walls | DimensionCategories.Doors | DimensionCategories.Windows,
            CategoryMask.Default);
    }

    [Fact]
    public void RoundTripsEverySupportedCombination()
    {
        var categories = DimensionCategories.Walls | DimensionCategories.Windows;

        var restored = CategoryMask.FromMask(CategoryMask.ToMask(categories));

        Assert.Equal(categories, restored);
    }

    [Fact]
    public void ClampsV2CategoriesOff()
    {
        // A stored mask that somehow carries Ceilings/Floors must never switch them on:
        // no v1 reference resolution exists for host objects.
        var stored = CategoryMask.ToMask(
            DimensionCategories.Walls | DimensionCategories.Ceilings | DimensionCategories.Floors);

        var restored = CategoryMask.FromMask(stored);

        Assert.Equal(DimensionCategories.Walls, restored);
    }

    [Fact]
    public void ClampsUnknownBitsOff()
    {
        var restored = CategoryMask.FromMask(0x7FFF_FFFF);

        Assert.Equal(CategoryMask.Supported, restored);
    }

    [Fact]
    public void ZeroMaskDecodesToNone()
    {
        Assert.Equal(DimensionCategories.None, CategoryMask.FromMask(0));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj --filter "FullyQualifiedName~CategoryMaskTests"`
Expected: FAIL to compile — `DimensionCategories`/`CategoryMask` don't exist.

- [ ] **Step 3: Create the scope models**

`src/RVTuk.Core/AutoDimensions/ScopeViewInfo.cs`:
```csharp
namespace RVTuk.Core.AutoDimensions
{
    /// <summary>One view a level's dimensions can be fanned out to, as plain data.</summary>
    public record ScopeViewInfo(long ViewId, string ViewName);
}
```

`src/RVTuk.Core/AutoDimensions/LevelScope.cs`:
```csharp
using System.Collections.Generic;

namespace RVTuk.Core.AutoDimensions
{
    /// <summary>
    /// One level of the project: every plan view that belongs to it, plus which of those views
    /// (if any) owns the Dimensions_Line reference lines the others copy their dimensions from.
    /// A level with no reference view has nothing to fan out and is shown inert in the pane.
    /// </summary>
    public record LevelScope(
        long LevelId,
        string LevelName,
        long? ReferenceViewId,
        IReadOnlyList<ScopeViewInfo> Views)
    {
        public bool HasReferenceView => ReferenceViewId.HasValue;
    }
}
```

`src/RVTuk.Core/AutoDimensions/AutoDimensionsScope.cs`:
```csharp
using System.Collections.Generic;

namespace RVTuk.Core.AutoDimensions
{
    /// <summary>What the pane persisted on its last successful run; null when it never ran.</summary>
    public record ScopeSelection(int CategoryMask, IReadOnlyList<long> CheckedViewIds);

    /// <summary>Everything one discovery pass tells the pane about the open project.</summary>
    public record AutoDimensionsScope(IReadOnlyList<LevelScope> Levels, ScopeSelection? Selection);
}
```

- [ ] **Step 4: Implement the category bitmask**

`src/RVTuk.Core/AutoDimensions/DimensionCategories.cs`:
```csharp
using System;

namespace RVTuk.Core.AutoDimensions
{
    /// <summary>Which Revit categories participate as dimension references in a run.</summary>
    [Flags]
    public enum DimensionCategories
    {
        None = 0,
        Walls = 1,
        Doors = 2,
        Windows = 4,
        // v2 — host objects needing top/bottom-face resolution. Present so the persisted mask
        // has room for them; CategoryMask.FromMask clamps them off until that logic exists.
        Ceilings = 8,
        Floors = 16,
    }

    /// <summary>Round-trips the category set through the single int persisted per project.</summary>
    public static class CategoryMask
    {
        /// <summary>The categories v1 can actually resolve references for.</summary>
        public const DimensionCategories Supported =
            DimensionCategories.Walls | DimensionCategories.Doors | DimensionCategories.Windows;

        /// <summary>What a project with no persisted selection starts with.</summary>
        public const DimensionCategories Default = Supported;

        public static int ToMask(DimensionCategories categories) => (int)categories;

        /// <summary>
        /// Clamped to <see cref="Supported"/>: a mask carrying v2 or unknown bits (a newer build,
        /// a hand-edited model) must never switch on behaviour this build doesn't implement.
        /// </summary>
        public static DimensionCategories FromMask(int mask) => (DimensionCategories)mask & Supported;
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj --filter "FullyQualifiedName~CategoryMaskTests"`
Expected: PASS (5 tests).

- [ ] **Step 6: Commit**

```bash
git add src/RVTuk.Core/AutoDimensions tests/RVTuk.Core.Tests/AutoDimensions && git commit -m "feat(auto-dimensions): add level-scope models and category bitmask (Core)"
```

---

### Task 2: Core — OpeningSegment (door/window → 2D candidate)

**Files:**
- Create: `src/RVTuk.Core/AutoDimensions/OpeningSegment.cs`
- Test: `tests/RVTuk.Core.Tests/AutoDimensions/OpeningSegmentTests.cs`

**Interfaces:**
- Consumes: `XyPoint`, `WallCandidate` (both already exist in `src/RVTuk.Core/AutoDimensions/`).
- Produces: `OpeningSegment.FromCenter(XyPoint center, XyPoint direction, double width) : WallCandidate`
  — the opening's 2D segment, centred on `center`, `width` long, along the (unnormalised)
  `direction`. Consumed by Task 6.

Why Core: this is the door/window equivalent of a wall's centreline, and it is pure 2D maths. The
spec assumed it could only be checked in Revit; keeping the arithmetic here makes it testable and
leaves the Revit side with nothing but parameter reads.

- [ ] **Step 1: Write the failing test**

Create `tests/RVTuk.Core.Tests/AutoDimensions/OpeningSegmentTests.cs`:

```csharp
using RVTuk.Core.AutoDimensions;
using Xunit;

namespace RVTuk.Core.Tests.AutoDimensions;

public class OpeningSegmentTests
{
    private const double Tolerance = 1e-9;

    [Fact]
    public void CentersTheSegmentOnTheOpeningAlongAUnitDirection()
    {
        var segment = OpeningSegment.FromCenter(new XyPoint(5, 0), new XyPoint(1, 0), 4);

        Assert.Equal(3, segment.Start.X, Tolerance);
        Assert.Equal(0, segment.Start.Y, Tolerance);
        Assert.Equal(7, segment.End.X, Tolerance);
        Assert.Equal(0, segment.End.Y, Tolerance);
    }

    [Fact]
    public void NormalisesANonUnitDirection()
    {
        // (3,4) has length 5; a width of 10 puts each end 5 away along (0.6, 0.8).
        var segment = OpeningSegment.FromCenter(new XyPoint(0, 0), new XyPoint(3, 4), 10);

        Assert.Equal(-3, segment.Start.X, Tolerance);
        Assert.Equal(-4, segment.Start.Y, Tolerance);
        Assert.Equal(3, segment.End.X, Tolerance);
        Assert.Equal(4, segment.End.Y, Tolerance);
    }

    [Fact]
    public void HandlesAVerticalDirection()
    {
        var segment = OpeningSegment.FromCenter(new XyPoint(2, 2), new XyPoint(0, 1), 2);

        Assert.Equal(2, segment.Start.X, Tolerance);
        Assert.Equal(1, segment.Start.Y, Tolerance);
        Assert.Equal(2, segment.End.X, Tolerance);
        Assert.Equal(3, segment.End.Y, Tolerance);
    }

    [Fact]
    public void DegenerateDirectionCollapsesToThePoint()
    {
        // Nothing sane to build; WallCrossingFinder rejects zero-length candidates anyway.
        var segment = OpeningSegment.FromCenter(new XyPoint(1, 1), new XyPoint(0, 0), 3);

        Assert.Equal(segment.Start, segment.End);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj --filter "FullyQualifiedName~OpeningSegmentTests"`
Expected: FAIL to compile — `OpeningSegment` doesn't exist.

- [ ] **Step 3: Implement it**

`src/RVTuk.Core/AutoDimensions/OpeningSegment.cs`:
```csharp
using System;

namespace RVTuk.Core.AutoDimensions
{
    /// <summary>
    /// Builds the 2D candidate segment for an opening (door/window). A wall contributes its own
    /// centreline; an opening has none, so it contributes a segment as wide as the opening,
    /// centred on its location point and running along its host wall's direction — deliberately
    /// the host's direction, not the instance's own facing, to sidestep flip/orientation quirks.
    /// </summary>
    public static class OpeningSegment
    {
        private const double MinimumDirectionLength = 1e-12;

        public static WallCandidate FromCenter(XyPoint center, XyPoint direction, double width)
        {
            var length = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
            if (length < MinimumDirectionLength) return new WallCandidate(center, center);

            var halfX = direction.X / length * width / 2.0;
            var halfY = direction.Y / length * width / 2.0;

            return new WallCandidate(
                new XyPoint(center.X - halfX, center.Y - halfY),
                new XyPoint(center.X + halfX, center.Y + halfY));
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj --filter "FullyQualifiedName~OpeningSegmentTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/RVTuk.Core/AutoDimensions/OpeningSegment.cs tests/RVTuk.Core.Tests/AutoDimensions/OpeningSegmentTests.cs && git commit -m "feat(auto-dimensions): add OpeningSegment (door/window 2D candidate, Core)"
```

---

### Task 3: Core — id codecs for Extensible Storage strings

**Files:**
- Create: `src/RVTuk.Core/AutoDimensions/IdListCodec.cs`
- Create: `src/RVTuk.Core/AutoDimensions/IdMapCodec.cs`
- Test: `tests/RVTuk.Core.Tests/AutoDimensions/IdCodecTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `IdListCodec.Encode(IEnumerable<long> ids) : string` / `IdListCodec.Decode(string? encoded) : IReadOnlyList<long>`
  - `IdMapCodec.Encode(IReadOnlyDictionary<long,long> map) : string` / `IdMapCodec.Decode(string? encoded) : IReadOnlyDictionary<long,long>`
  - Consumed by Task 4 (`AutoDimensionTracker`, map) and Task 8 (`ScopeSelectionStore`, list).

Why strings: see "deliberate deviations" above — Extensible Storage does not document `long` as a
supported field or map-key type, and 64-bit `ElementId` values must survive the round trip exactly.

- [ ] **Step 1: Write the failing test**

Create `tests/RVTuk.Core.Tests/AutoDimensions/IdCodecTests.cs`:

```csharp
using System.Collections.Generic;
using RVTuk.Core.AutoDimensions;
using Xunit;

namespace RVTuk.Core.Tests.AutoDimensions;

public class IdCodecTests
{
    [Fact]
    public void ListRoundTripsIncludingLargeIds()
    {
        var ids = new long[] { 1, 4_294_967_296L, 9_007_199_254_740_993L };

        var restored = IdListCodec.Decode(IdListCodec.Encode(ids));

        Assert.Equal(ids, restored);
    }

    [Fact]
    public void EmptyListRoundTripsToEmpty()
    {
        Assert.Empty(IdListCodec.Decode(IdListCodec.Encode(new long[0])));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ListDecodeOfNothingIsEmpty(string? encoded)
    {
        Assert.Empty(IdListCodec.Decode(encoded));
    }

    [Fact]
    public void ListDecodeDropsUnparseableEntries()
    {
        // A hand-edited or truncated entity must not take the whole selection down with it.
        Assert.Equal(new long[] { 7, 9 }, IdListCodec.Decode("7,,oops,9"));
    }

    [Fact]
    public void MapRoundTripsIncludingLargeIds()
    {
        var map = new Dictionary<long, long>
        {
            [1] = 2,
            [4_294_967_296L] = 9_007_199_254_740_993L,
        };

        var restored = IdMapCodec.Decode(IdMapCodec.Encode(map));

        Assert.Equal(2L, restored[1]);
        Assert.Equal(9_007_199_254_740_993L, restored[4_294_967_296L]);
        Assert.Equal(2, restored.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MapDecodeOfNothingIsEmpty(string? encoded)
    {
        Assert.Empty(IdMapCodec.Decode(encoded));
    }

    [Fact]
    public void MapDecodeDropsMalformedPairsAndKeepsTheLastValueForARepeatedKey()
    {
        var restored = IdMapCodec.Decode("5:6;bad;7:;:8;5:9");

        Assert.Equal(9L, restored[5]);
        Assert.Single(restored);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj --filter "FullyQualifiedName~IdCodecTests"`
Expected: FAIL to compile — `IdListCodec`/`IdMapCodec` don't exist.

- [ ] **Step 3: Implement `IdListCodec`**

`src/RVTuk.Core/AutoDimensions/IdListCodec.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RVTuk.Core.AutoDimensions
{
    /// <summary>
    /// Element ids as one comma-separated string, so they can live in a single Extensible
    /// Storage string field (ES doesn't document a 64-bit integer field type, and the ids are
    /// 64-bit from Revit 2024 on). Unparseable entries are dropped rather than throwing — a
    /// truncated entity should cost the selection, not the run.
    /// </summary>
    public static class IdListCodec
    {
        public static string Encode(IEnumerable<long> ids) =>
            string.Join(",", ids.Select(id => id.ToString(CultureInfo.InvariantCulture)));

        public static IReadOnlyList<long> Decode(string? encoded)
        {
            if (string.IsNullOrWhiteSpace(encoded)) return Array.Empty<long>();

            var ids = new List<long>();
            foreach (var part in encoded!.Split(','))
            {
                if (long.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                    ids.Add(id);
            }
            return ids;
        }
    }
}
```

- [ ] **Step 4: Implement `IdMapCodec`**

`src/RVTuk.Core/AutoDimensions/IdMapCodec.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RVTuk.Core.AutoDimensions
{
    /// <summary>
    /// An id-to-id map as one "key:value;key:value" string — same rationale as
    /// <see cref="IdListCodec"/>. Used for the tracker's target-view-id → dimension-id map.
    /// </summary>
    public static class IdMapCodec
    {
        public static string Encode(IReadOnlyDictionary<long, long> map) =>
            string.Join(";", map.Select(pair =>
                pair.Key.ToString(CultureInfo.InvariantCulture) + ":" +
                pair.Value.ToString(CultureInfo.InvariantCulture)));

        public static IReadOnlyDictionary<long, long> Decode(string? encoded)
        {
            var map = new Dictionary<long, long>();
            if (string.IsNullOrWhiteSpace(encoded)) return map;

            foreach (var entry in encoded!.Split(';'))
            {
                var parts = entry.Split(':');
                if (parts.Length != 2) continue;
                if (!long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var key)) continue;
                if (!long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) continue;
                map[key] = value;
            }
            return map;
        }
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj --filter "FullyQualifiedName~IdCodecTests"`
Expected: PASS (11 tests, counting the `[Theory]` cases).

- [ ] **Step 6: Commit**

```bash
git add src/RVTuk.Core/AutoDimensions/IdListCodec.cs src/RVTuk.Core/AutoDimensions/IdMapCodec.cs tests/RVTuk.Core.Tests/AutoDimensions/IdCodecTests.cs && git commit -m "feat(auto-dimensions): add id list/map string codecs for Extensible Storage (Core)"
```

---

### Task 4: Revit — AutoDimensionTracker becomes per-target-view

**Files:**
- Modify: `src/RVTuk.Revit/AutoDimensions/AutoDimensionTracker.cs` (whole file replaced)

**Interfaces:**
- Consumes: `IdMapCodec.Encode`/`.Decode` (Task 3).
- Produces (both signatures **change** — the `viewId` is now the *target* view, not "the active
  view", and one line can track a dimension per view):
  - `AutoDimensionTracker.TryGetTrackedDimension(Element line, ElementId targetViewId) : Dimension?`
  - `AutoDimensionTracker.SetTrackedDimension(Element line, ElementId targetViewId, ElementId dimensionId) : void`
  - Consumed by Task 6 (`DimensionRunner`).

This is a breaking Extensible Storage schema change, sanctioned by the spec: the tool is unreleased
(hidden behind `RegisterUnreleasedTools`), so there are no live installs to migrate. It takes a
**new schema guid** — reusing the old guid with a different field layout throws once a session has
already seen the old layout.

- [ ] **Step 1: Replace the file**

`src/RVTuk.Revit/AutoDimensions/AutoDimensionTracker.cs`:
```csharp
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using RVTuk.Core.AutoDimensions;

namespace RVTuk.Revit.AutoDimensions
{
    /// <summary>
    /// Tracks, per reference line, the Dimension that line last produced *in each target view* —
    /// via Extensible Storage (invisible to the user, travels with the line through copy/move).
    /// One line now fans out to several views, so the entry is a target-view-id → dimension-id
    /// map rather than a single id.
    ///
    /// Validates the tracked dimension still exists, is a Dimension, and belongs to the view being
    /// processed before ever handing it back for deletion (guards a line copied to another view
    /// from deleting that other view's legitimate dimension).
    ///
    /// The map is stored as one string field: Extensible Storage does not document a 64-bit
    /// integer field or map-key type, and Revit 2024+ element ids are 64-bit.
    /// </summary>
    public static class AutoDimensionTracker
    {
        // New guid: the field layout changed from a single id to a map. The pre-scope-pane schema
        // (2f1c9b6e-…) is abandoned — the tool never shipped, so no model needs migrating.
        private static readonly Guid SchemaGuid = new Guid("7b3c1d92-4e58-4a0f-9c21-6d5f8e3a17b4");
        private const string SchemaName = "RVTukAutoDimensionTrackingByView";
        private const string FieldName = "DimensionIdsByViewId";

        public static Dimension? TryGetTrackedDimension(Element line, ElementId targetViewId)
        {
            var schema = Schema.Lookup(SchemaGuid);
            if (schema == null) return null;

            var entity = line.GetEntity(schema);
            if (!entity.IsValid()) return null;

            var map = IdMapCodec.Decode(entity.Get<string>(FieldName));
            if (!map.TryGetValue(targetViewId.Value, out var dimensionIdValue)) return null;

            if (line.Document.GetElement(new ElementId(dimensionIdValue)) is not Dimension dimension) return null;
            if (dimension.OwnerViewId != targetViewId) return null;

            return dimension;
        }

        /// <summary>
        /// Overwrites this line's entry for one target view, leaving its entries for other views
        /// alone. Stale entries (a view or dimension since deleted) are harmless — reads validate
        /// before returning — and self-heal the next time that view is processed.
        /// </summary>
        public static void SetTrackedDimension(Element line, ElementId targetViewId, ElementId dimensionId)
        {
            var schema = GetOrCreateSchema();

            var map = new Dictionary<long, long>();
            var existing = line.GetEntity(schema);
            if (existing.IsValid())
            {
                foreach (var pair in IdMapCodec.Decode(existing.Get<string>(FieldName)))
                    map[pair.Key] = pair.Value;
            }
            map[targetViewId.Value] = dimensionId.Value;

            var entity = new Entity(schema);
            entity.Set(FieldName, IdMapCodec.Encode(map));
            line.SetEntity(entity);
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

- [ ] **Step 2: Build — the old two-argument call site must now fail**

Run: `dotnet build src/RVTuk.Revit/RVTuk.Revit.csproj -c Release2025`
Expected: FAIL — `AutoDimensionsCommand.cs` line ~142 calls `SetTrackedDimension(line, dimension.Id)`,
now a two-arg call against a three-arg method. That call site is rewritten in Task 6; to keep this
task's build green, apply the one-line stopgap below **now** and let Task 6 delete it:

In `src/RVTuk.Revit/AutoDimensions/AutoDimensionsCommand.cs`, change:
```csharp
                AutoDimensionTracker.SetTrackedDimension(line, dimension.Id);
```
to:
```csharp
                AutoDimensionTracker.SetTrackedDimension(line, view.Id, dimension.Id);
```

- [ ] **Step 3: Build both configs**

Run: `dotnet build src/RVTuk.Revit/RVTuk.Revit.csproj -c Release2024`
Expected: `Build succeeded`.

Run: `dotnet build src/RVTuk.Revit/RVTuk.Revit.csproj -c Release2025`
Expected: `Build succeeded`.

- [ ] **Step 4: Commit**

```bash
git add src/RVTuk.Revit/AutoDimensions/AutoDimensionTracker.cs src/RVTuk.Revit/AutoDimensions/AutoDimensionsCommand.cs && git commit -m "feat(auto-dimensions)!: track one dimension per target view (new ES schema)"
```

---

### Task 5: Revit — DimensionCandidateCollector (category-tagged candidates)

**Files:**
- Create: `src/RVTuk.Revit/AutoDimensions/DimensionCandidateCollector.cs`

**Interfaces:**
- Consumes: `DimensionCategories` (Task 1), `OpeningSegment.FromCenter` (Task 2), `WallCandidate`,
  `XyPoint` (existing Core).
- Produces:
  - `enum DimensionCandidateKind { Wall, Opening }`
  - `sealed class DimensionCandidate` with `Kind`, `Wall? Wall`, `FamilyInstance? Instance`
  - `sealed class DimensionCandidateSet` with
    `IReadOnlyList<DimensionCandidate> Items` and `IReadOnlyList<WallCandidate> Segments`
    (index-aligned, so an index returned by `WallCrossingFinder` maps straight back to an element)
  - `DimensionCandidateCollector.Collect(Document doc, View view, DimensionCategories categories) : DimensionCandidateSet`
  - `DimensionCandidateCollector.TryAppendReferences(DimensionCandidate candidate, ReferenceArray target) : bool`
  - Consumed by Task 6 (`DimensionRunner`).

- [ ] **Step 1: Implement it**

`src/RVTuk.Revit/AutoDimensions/DimensionCandidateCollector.cs`:
```csharp
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RVTuk.Core.AutoDimensions;

namespace RVTuk.Revit.AutoDimensions
{
    public enum DimensionCandidateKind
    {
        Wall,
        Opening,
    }

    /// <summary>One element that may cross a reference line, tagged with how to dimension it.</summary>
    public sealed class DimensionCandidate
    {
        public DimensionCandidateKind Kind { get; set; }
        public Wall? Wall { get; set; }
        public FamilyInstance? Instance { get; set; }
    }

    /// <summary>
    /// Candidates and their 2D segments, index-aligned: WallCrossingFinder returns indices into
    /// <see cref="Segments"/>, which are indices into <see cref="Items"/>.
    /// </summary>
    public sealed class DimensionCandidateSet
    {
        public IReadOnlyList<DimensionCandidate> Items { get; set; } = new List<DimensionCandidate>();
        public IReadOnlyList<WallCandidate> Segments { get; set; } = new List<WallCandidate>();
    }

    /// <summary>
    /// Turns the checked categories' elements — as visible in one target view — into the flat,
    /// category-tagged candidate list the (unchanged) Core crossing finder consumes, and resolves
    /// each matched candidate's two dimension references.
    /// </summary>
    public static class DimensionCandidateCollector
    {
        public static DimensionCandidateSet Collect(Document doc, View view, DimensionCategories categories)
        {
            var items = new List<DimensionCandidate>();
            var segments = new List<WallCandidate>();

            if (categories.HasFlag(DimensionCategories.Walls))
            {
                // Straight walls only: Core's finder is a 2D segment intersection, and Revit can't
                // linear-dimension a curved face against a straight line anyway.
                foreach (var wall in new FilteredElementCollector(doc, view.Id)
                             .OfClass(typeof(Wall))
                             .Cast<Wall>())
                {
                    if ((wall.Location as LocationCurve)?.Curve is not Line centerline) continue;

                    items.Add(new DimensionCandidate { Kind = DimensionCandidateKind.Wall, Wall = wall });
                    segments.Add(new WallCandidate(
                        ToXyPoint(centerline.GetEndPoint(0)),
                        ToXyPoint(centerline.GetEndPoint(1))));
                }
            }

            if (categories.HasFlag(DimensionCategories.Doors))
                CollectOpenings(doc, view, BuiltInCategory.OST_Doors, items, segments);

            if (categories.HasFlag(DimensionCategories.Windows))
                CollectOpenings(doc, view, BuiltInCategory.OST_Windows, items, segments);

            return new DimensionCandidateSet { Items = items, Segments = segments };
        }

        /// <summary>
        /// Appends the candidate's two references (a wall's side faces, an opening's Left/Right)
        /// to the array. Returns false — leaving the array untouched — when either side can't be
        /// resolved, so one unreadable element doesn't cost the whole line its dimension.
        /// </summary>
        public static bool TryAppendReferences(DimensionCandidate candidate, ReferenceArray target)
        {
            if (candidate.Kind == DimensionCandidateKind.Wall)
            {
                if (candidate.Wall == null) return false;

                var exterior = HostObjectUtils.GetSideFaces(candidate.Wall, ShellLayerType.Exterior);
                var interior = HostObjectUtils.GetSideFaces(candidate.Wall, ShellLayerType.Interior);
                if (exterior.Count == 0 || interior.Count == 0) return false;

                target.Append(exterior[0]);
                target.Append(interior[0]);
                return true;
            }

            if (candidate.Instance == null) return false;

            var left = candidate.Instance.GetReferences(FamilyInstanceReferenceType.Left);
            var right = candidate.Instance.GetReferences(FamilyInstanceReferenceType.Right);
            if (left.Count == 0 || right.Count == 0) return false;

            target.Append(left[0]);
            target.Append(right[0]);
            return true;
        }

        private static void CollectOpenings(
            Document doc,
            View view,
            BuiltInCategory category,
            List<DimensionCandidate> items,
            List<WallCandidate> segments)
        {
            foreach (var instance in new FilteredElementCollector(doc, view.Id)
                         .OfCategory(category)
                         .OfClass(typeof(FamilyInstance))
                         .Cast<FamilyInstance>())
            {
                if (!TryBuildOpeningSegment(instance, out var segment)) continue;

                items.Add(new DimensionCandidate
                {
                    Kind = DimensionCandidateKind.Opening,
                    Instance = instance,
                });
                segments.Add(segment);
            }
        }

        private static bool TryBuildOpeningSegment(FamilyInstance instance, out WallCandidate segment)
        {
            segment = default!;

            if (instance.Location is not LocationPoint location) return false;
            if (instance.Host is not Wall host) return false;
            if ((host.Location as LocationCurve)?.Curve is not Line hostLine) return false;

            var width = TryGetWidth(instance);
            if (width == null) return false;

            var direction = hostLine.Direction;
            segment = OpeningSegment.FromCenter(
                ToXyPoint(location.Point),
                new XyPoint(direction.X, direction.Y),
                width.Value);
            return true;
        }

        /// <summary>
        /// Openings carry their width under several names depending on the family's lineage
        /// (built-in door/window width, the generic family width, or a plain shared "Width"),
        /// on the instance or the type. First positive value wins; null means "not an opening we
        /// can size", and the candidate is dropped.
        /// </summary>
        private static double? TryGetWidth(FamilyInstance instance)
        {
            var symbol = instance.Symbol;
            var parameters = new[]
            {
                instance.get_Parameter(BuiltInParameter.DOOR_WIDTH),
                instance.get_Parameter(BuiltInParameter.WINDOW_WIDTH),
                symbol?.get_Parameter(BuiltInParameter.DOOR_WIDTH),
                symbol?.get_Parameter(BuiltInParameter.WINDOW_WIDTH),
                symbol?.get_Parameter(BuiltInParameter.FAMILY_WIDTH_PARAM),
                instance.LookupParameter("Width"),
                symbol?.LookupParameter("Width"),
            };

            foreach (var parameter in parameters)
            {
                if (parameter == null || !parameter.HasValue) continue;
                if (parameter.StorageType != StorageType.Double) continue;

                var value = parameter.AsDouble();
                if (value > 0) return value;
            }
            return null;
        }

        private static XyPoint ToXyPoint(XYZ point) => new(point.X, point.Y);
    }
}
```

- [ ] **Step 2: Build both configs**

Run: `dotnet build src/RVTuk.Revit/RVTuk.Revit.csproj -c Release2024`
Expected: `Build succeeded`.

Run: `dotnet build src/RVTuk.Revit/RVTuk.Revit.csproj -c Release2025`
Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```bash
git add src/RVTuk.Revit/AutoDimensions/DimensionCandidateCollector.cs && git commit -m "feat(auto-dimensions): collect category-tagged candidates (walls + door/window openings)"
```

---

### Task 6: Revit — extract DimensionRunner, route the ribbon command through it

**Files:**
- Create: `src/RVTuk.Revit/AutoDimensions/DimensionRunner.cs`
- Modify: `src/RVTuk.Revit/AutoDimensions/AutoDimensionsCommand.cs`

**Interfaces:**
- Consumes: `DimensionCandidateCollector.Collect`/`.TryAppendReferences` and
  `DimensionCandidateSet` (Task 5), `AutoDimensionTracker.TryGetTrackedDimension`/
  `.SetTrackedDimension` (Task 4), `DimensionCategories` (Task 1),
  `WallCrossingFinder.FindCrossingIndices`, `CoincidentReferenceFilter.KeepIndices`,
  `DimensionLineStyle` (all existing).
- Produces:
  - `sealed class DimensionRunTally { int Created; int Skipped; }` (mutable fields)
  - `DimensionRunner.CollectReferenceLines(Document doc, View referenceView) : IReadOnlyList<DetailLine>`
  - `DimensionRunner.RunPair(Document doc, IReadOnlyList<DetailLine> referenceLines, View targetView, DimensionCategories categories, DimensionRunTally tally) : void`
  - Consumed by `AutoDimensionsCommand` (this task) and Task 8 (`CreateDimensionsEventHandler`).

The whole per-line pipeline moves here verbatim — crossing detection, reference resolution,
zero-segment cleanup, tracking — plus two changes the fan-out needs: candidates come from the
**target** view (so each view dimensions what it actually shows), and the reference line's geometry
is dropped onto the target view's plane before `NewDimension` (a dimension's line must lie in the
plane of the view it's created in; for the reference view itself this is a no-op).

- [ ] **Step 1: Create `DimensionRunner`**

`src/RVTuk.Revit/AutoDimensions/DimensionRunner.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RVTuk.Core.AutoDimensions;

namespace RVTuk.Revit.AutoDimensions
{
    /// <summary>Running totals for one Auto Dimensions run, across however many views it spans.</summary>
    public sealed class DimensionRunTally
    {
        public int Created;
        public int Skipped;
    }

    /// <summary>
    /// The per-reference-line pipeline, shared by the single-view ribbon command and the scope
    /// pane's fan-out so the two entry points can't drift: delete the stale dimension, find the
    /// crossings, resolve references, create, de-duplicate coincident references, track.
    ///
    /// Must be called inside an open transaction.
    /// </summary>
    public static class DimensionRunner
    {
        /// <summary>
        /// Reference lines owned by a view. Document-wide collector filtered by OwnerViewId, not a
        /// view-scoped collector: a view-scoped collector only returns elements currently visible,
        /// and lines hidden by the view template must still produce dimensions.
        /// </summary>
        public static IReadOnlyList<DetailLine> CollectReferenceLines(Document doc, View referenceView)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(CurveElement))
                .Cast<CurveElement>()
                .OfType<DetailLine>()
                .Where(l => l.OwnerViewId == referenceView.Id && DimensionLineStyle.IsDimensionsLine(l))
                .ToList();
        }

        /// <summary>
        /// Runs every reference line against one target view. The lines may be owned by a
        /// different view of the same level — that is exactly the fan-out the scope pane performs.
        /// </summary>
        public static void RunPair(
            Document doc,
            IReadOnlyList<DetailLine> referenceLines,
            View targetView,
            DimensionCategories categories,
            DimensionRunTally tally)
        {
            var candidates = DimensionCandidateCollector.Collect(doc, targetView, categories);

            foreach (var line in referenceLines)
            {
                try
                {
                    RunLine(doc, line, targetView, candidates, tally);
                }
                catch
                {
                    // One unreadable line never aborts the run; the transaction still guarantees
                    // all-or-nothing for a hard Revit-level failure.
                    tally.Skipped++;
                }
            }
        }

        private static void RunLine(
            Document doc,
            DetailLine line,
            View targetView,
            DimensionCandidateSet candidates,
            DimensionRunTally tally)
        {
            // Always first, and independent of whether this line still has crossings: a line whose
            // walls were deleted or moved away must still lose its stale dimension.
            var tracked = AutoDimensionTracker.TryGetTrackedDimension(line, targetView.Id);
            if (tracked != null) doc.Delete(tracked.Id);

            if (line.GeometryCurve is not Line geometryLine)
            {
                tally.Skipped++;
                return;
            }

            var crossingIndices = WallCrossingFinder.FindCrossingIndices(
                ToXyPoint(geometryLine.GetEndPoint(0)),
                ToXyPoint(geometryLine.GetEndPoint(1)),
                candidates.Segments);

            if (crossingIndices.Count == 0)
            {
                tally.Skipped++;
                return;
            }

            var referenceArray = new ReferenceArray();
            var anyResolved = false;
            foreach (var index in crossingIndices)
            {
                if (DimensionCandidateCollector.TryAppendReferences(candidates.Items[index], referenceArray))
                    anyResolved = true;
            }

            if (!anyResolved)
            {
                tally.Skipped++;
                return;
            }

            var dimensionLine = ToTargetViewPlane(geometryLine, targetView);
            var dimension = doc.Create.NewDimension(targetView, dimensionLine, referenceArray);
            dimension = RemoveCoincidentReferences(doc, targetView, dimensionLine, dimension);
            if (dimension == null)
            {
                tally.Skipped++;
                return;
            }

            AutoDimensionTracker.SetTrackedDimension(line, targetView.Id, dimension.Id);
            tally.Created++;
        }

        /// <summary>
        /// A dimension's line has to lie in the plane of the view it's created in. The reference
        /// line sits on its own view's sketch plane, so fanning out to another view of the same
        /// level needs the Z swapped for the target's. A no-op when target == reference view.
        /// </summary>
        private static Line ToTargetViewPlane(Line line, View targetView)
        {
            try
            {
                var plane = targetView.SketchPlane?.GetPlane();
                if (plane == null) return line;

                var z = plane.Origin.Z;
                var start = line.GetEndPoint(0);
                var end = line.GetEndPoint(1);
                if (Math.Abs(start.Z - z) < 1e-9 && Math.Abs(end.Z - z) < 1e-9) return line;

                return Line.CreateBound(new XYZ(start.X, start.Y, z), new XYZ(end.X, end.Y, z));
            }
            catch
            {
                return line;
            }
        }

        /// <summary>
        /// Joined walls (or a door flush with a wall face) can put two references at the same
        /// station along the line, producing zero-length segments. Detects them on the freshly
        /// created dimension and, if any exist, recreates it keeping only the first reference at
        /// each station. Returns null (after deleting the dimension) when fewer than two survive.
        /// </summary>
        private static Dimension? RemoveCoincidentReferences(
            Document doc, View view, Line dimensionLine, Dimension dimension)
        {
            var segmentValues = GetSegmentValues(dimension);
            var keep = CoincidentReferenceFilter.KeepIndices(
                segmentValues, doc.Application.ShortCurveTolerance);
            if (keep.Count == segmentValues.Count + 1) return dimension;

            var references = dimension.References;
            var filtered = new ReferenceArray();
            foreach (var index in keep)
            {
                filtered.Append(references.get_Item(index));
            }

            doc.Delete(dimension.Id);
            if (filtered.Size < 2) return null;
            return doc.Create.NewDimension(view, dimensionLine, filtered);
        }

        /// <summary>
        /// Segment values in order along the line; a single-segment dimension has an empty
        /// Segments collection and exposes its length via Value instead.
        /// </summary>
        private static IReadOnlyList<double> GetSegmentValues(Dimension dimension)
        {
            if (dimension.NumberOfSegments == 0)
            {
                return new[] { dimension.Value ?? 0.0 };
            }

            return dimension.Segments
                .Cast<DimensionSegment>()
                .Select(s => s.Value ?? 0.0)
                .ToList();
        }

        private static XyPoint ToXyPoint(XYZ point) => new(point.X, point.Y);
    }
}
```

- [ ] **Step 2: Replace `AutoDimensionsCommand` with the thin orchestrator**

`src/RVTuk.Revit/AutoDimensions/AutoDimensionsCommand.cs` (whole file):
```csharp
using System;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RVTuk.Core.AutoDimensions;

namespace RVTuk.Revit.AutoDimensions
{
    /// <summary>
    /// The single-view ribbon entry point: dimension every wall crossing a Dimensions_Line detail
    /// line in the active view. Deliberately walls-only and active-view-only — the scope pane
    /// (AutoDimensionsPaneCommand) is the multi-view, multi-category entry point.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class AutoDimensionsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                return Run(commandData, ref message);
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("RVTuk – Auto Dimensions (error)", ex.ToString());
                return Result.Failed;
            }
        }

        private static Result Run(ExternalCommandData commandData, ref string message)
        {
            var uiDoc = commandData.Application.ActiveUIDocument;
            if (uiDoc == null)
            {
                message = "No active document.";
                return Result.Failed;
            }

            var doc = uiDoc.Document;
            var view = uiDoc.ActiveView;
            var tally = new DimensionRunTally();

            using (var tx = new Transaction(doc, "Auto Dimensions"))
            {
                tx.Start();

                try
                {
                    DimensionLineStyle.EnsureExists(doc);
                    var referenceLines = DimensionRunner.CollectReferenceLines(doc, view);
                    DimensionRunner.RunPair(doc, referenceLines, view, DimensionCategories.Walls, tally);
                }
                catch
                {
                    tx.RollBack();
                    throw;
                }

                tx.Commit();
            }

            ShowSummary(tally);
            return Result.Succeeded;
        }

        private static void ShowSummary(DimensionRunTally tally)
        {
            var summary = new StringBuilder();
            if (tally.Created == 0 && tally.Skipped == 0)
            {
                summary.Append("No Dimensions_Line lines found — the line style now exists in " +
                    "this project; draw reference lines and run again.");
            }
            else
            {
                summary.Append($"{tally.Created} dimension(s) created, {tally.Skipped} line(s) skipped " +
                    "(no walls found).");
            }
            TaskDialog.Show("RVTuk – Auto Dimensions", summary.ToString());
        }
    }
}
```

- [ ] **Step 3: Build both configs**

Run: `dotnet build src/RVTuk.Revit/RVTuk.Revit.csproj -c Release2024`
Expected: `Build succeeded`.

Run: `dotnet build src/RVTuk.Revit/RVTuk.Revit.csproj -c Release2025`
Expected: `Build succeeded`.

- [ ] **Step 4: Commit**

```bash
git add src/RVTuk.Revit/AutoDimensions/DimensionRunner.cs src/RVTuk.Revit/AutoDimensions/AutoDimensionsCommand.cs && git commit -m "refactor(auto-dimensions): extract DimensionRunner shared by command and pane"
```

---

### Task 7: Revit — LevelScopeFinder + ScopeSelectionStore + LevelDiscoveryEventHandler

**Files:**
- Create: `src/RVTuk.Revit/AutoDimensions/LevelScopeFinder.cs`
- Create: `src/RVTuk.Revit/AutoDimensions/ScopeSelectionStore.cs`
- Create: `src/RVTuk.Revit/AutoDimensions/ExternalEvents/LevelDiscoveryEventHandler.cs`

**Interfaces:**
- Consumes: `LevelScope`, `ScopeViewInfo`, `AutoDimensionsScope`, `ScopeSelection` (Task 1),
  `IdListCodec` (Task 3), `DimensionLineStyle.IsDimensionsLine` (existing).
- Produces:
  - `LevelScopeFinder.Find(Document doc) : IReadOnlyList<LevelScope>` — every level that owns at
    least one non-template `ViewPlan`, ordered by elevation, each with its views ordered by
    ascending `ElementId` and its reference view resolved. Consumed by Tasks 8, 11.
  - `ScopeSelectionStore.Read(Document doc) : ScopeSelection?` (null when never persisted) and
    `ScopeSelectionStore.Write(Document doc, int categoryMask, IReadOnlyList<long> checkedViewIds) : void`
    (must be called inside an open transaction). Consumed by Task 8.
  - `LevelDiscoveryEventHandler` with `Reset()`, `WaitForCompletion()`,
    `AutoDimensionsScope Result { get; }`. Consumed by Task 11.

- [ ] **Step 1: Implement `LevelScopeFinder`**

`src/RVTuk.Revit/AutoDimensions/LevelScopeFinder.cs`:
```csharp
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RVTuk.Core.AutoDimensions;

namespace RVTuk.Revit.AutoDimensions
{
    /// <summary>
    /// Discovers, per level, which of its plan views can act as the reference view (owns at least
    /// one Dimensions_Line detail line) and which views can receive the fanned-out dimensions.
    /// Views with no level — 3D, sections, drafting views, schedules, sheets — are never
    /// candidates for either role.
    /// </summary>
    public static class LevelScopeFinder
    {
        public static IReadOnlyList<LevelScope> Find(Document doc)
        {
            var planViews = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .Where(v => !v.IsTemplate && v.GenLevel != null)
                .ToList();

            // Document-wide, filtered by OwnerViewId — see DimensionRunner.CollectReferenceLines
            // for why a view-scoped collector would miss template-hidden lines.
            var viewsOwningReferenceLines = new HashSet<ElementId>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(CurveElement))
                    .Cast<CurveElement>()
                    .OfType<DetailLine>()
                    .Where(DimensionLineStyle.IsDimensionsLine)
                    .Select(l => l.OwnerViewId));

            var scopes = new List<(double Elevation, LevelScope Scope)>();
            foreach (var group in planViews.GroupBy(v => v.GenLevel.Id))
            {
                if (doc.GetElement(group.Key) is not Level level) continue;

                // Ascending ElementId: arbitrary but deterministic, per the spec — a level is only
                // expected to hold one reference view, and the first found wins if it holds more.
                var views = group.OrderBy(v => v.Id.Value).ToList();
                var referenceView = views.FirstOrDefault(v => viewsOwningReferenceLines.Contains(v.Id));

                scopes.Add((level.Elevation, new LevelScope(
                    level.Id.Value,
                    level.Name,
                    referenceView?.Id.Value,
                    views.Select(v => new ScopeViewInfo(v.Id.Value, v.Name)).ToList())));
            }

            return scopes
                .OrderBy(s => s.Elevation)
                .Select(s => s.Scope)
                .ToList();
        }
    }
}
```

- [ ] **Step 2: Implement `ScopeSelectionStore`**

`src/RVTuk.Revit/AutoDimensions/ScopeSelectionStore.cs`:
```csharp
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using RVTuk.Core.AutoDimensions;

namespace RVTuk.Revit.AutoDimensions
{
    /// <summary>
    /// Per-project memory of the scope pane's last successful run — the checked category mask and
    /// the checked view ids — on the document's ProjectInformation element. Written in the same
    /// transaction as the run itself, never on a checkbox toggle: a selection the user never ran
    /// isn't worth a transaction, and the persisted state should mean "what was last built".
    ///
    /// The view ids live in one string field (see IdListCodec for why, not an array field).
    /// </summary>
    public static class ScopeSelectionStore
    {
        private static readonly Guid SchemaGuid = new Guid("c48f2a17-5d63-4b90-a1e7-9f2c60d83b5e");
        private const string SchemaName = "RVTukAutoDimensionsScopeSelection";
        private const string CategoryMaskField = "CategoryMask";
        private const string ViewIdsField = "CheckedViewIds";

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
                IdListCodec.Decode(entity.Get<string>(ViewIdsField)));
        }

        /// <summary>Must be called inside an open transaction.</summary>
        public static void Write(Document doc, int categoryMask, IReadOnlyList<long> checkedViewIds)
        {
            var projectInfo = doc.ProjectInformation;
            if (projectInfo == null) return;

            var entity = new Entity(GetOrCreateSchema());
            entity.Set(CategoryMaskField, categoryMask);
            entity.Set(ViewIdsField, IdListCodec.Encode(checkedViewIds));
            projectInfo.SetEntity(entity);
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
            return builder.Finish();
        }
    }
}
```

- [ ] **Step 3: Implement `LevelDiscoveryEventHandler`**

`src/RVTuk.Revit/AutoDimensions/ExternalEvents/LevelDiscoveryEventHandler.cs`:
```csharp
using System;
using System.Threading;
using Autodesk.Revit.UI;
using RVTuk.Core.AutoDimensions;

namespace RVTuk.Revit.AutoDimensions.ExternalEvents
{
    /// <summary>
    /// Marshals a scope discovery pass onto Revit's main thread for the pane, which triggers it
    /// from a background thread (raise + WaitForCompletion would deadlock on the UI thread).
    /// Follows the GetProjectFamiliesEventHandler ping-pong pattern.
    /// </summary>
    public class LevelDiscoveryEventHandler : IExternalEventHandler
    {
        private static readonly AutoDimensionsScope Empty =
            new AutoDimensionsScope(Array.Empty<LevelScope>(), null);

        private readonly ManualResetEventSlim _done = new(false);

        public AutoDimensionsScope Result { get; private set; } = Empty;

        public void Reset() => _done.Reset();
        public void WaitForCompletion() => _done.Wait();

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null) { Result = Empty; return; }

                Result = new AutoDimensionsScope(
                    LevelScopeFinder.Find(doc),
                    ScopeSelectionStore.Read(doc));
            }
            catch
            {
                Result = Empty;
            }
            finally
            {
                _done.Set();
            }
        }

        public string GetName() => "RVTuk.AutoDimensionsLevelDiscoveryEventHandler";
    }
}
```

- [ ] **Step 4: Build both configs**

Run: `dotnet build src/RVTuk.Revit/RVTuk.Revit.csproj -c Release2024`
Expected: `Build succeeded`.

Run: `dotnet build src/RVTuk.Revit/RVTuk.Revit.csproj -c Release2025`
Expected: `Build succeeded`.

- [ ] **Step 5: Commit**

```bash
git add src/RVTuk.Revit/AutoDimensions/LevelScopeFinder.cs src/RVTuk.Revit/AutoDimensions/ScopeSelectionStore.cs src/RVTuk.Revit/AutoDimensions/ExternalEvents/LevelDiscoveryEventHandler.cs && git commit -m "feat(auto-dimensions): discover levels, reference views, and the persisted selection"
```

---

### Task 8: Revit — CreateDimensionsEventHandler (the fan-out run)

**Files:**
- Create: `src/RVTuk.Revit/AutoDimensions/ExternalEvents/CreateDimensionsEventHandler.cs`

**Interfaces:**
- Consumes: `CategoryMask`, `LevelScope` (Task 1), `LevelScopeFinder.Find`,
  `ScopeSelectionStore.Write` (Task 7), `DimensionRunner.CollectReferenceLines`/`.RunPair`,
  `DimensionRunTally` (Task 6), `DimensionLineStyle.EnsureExists` (existing).
- Produces: `CreateDimensionsEventHandler` with
  `Prepare(int categoryMask, IReadOnlyList<long> checkedViewIds)`, `WaitForCompletion()`,
  `string Summary { get; }`. Consumed by Task 11.

- [ ] **Step 1: Implement `CreateDimensionsEventHandler`**

`src/RVTuk.Revit/AutoDimensions/ExternalEvents/CreateDimensionsEventHandler.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RVTuk.Core.AutoDimensions;

namespace RVTuk.Revit.AutoDimensions.ExternalEvents
{
    /// <summary>
    /// The scope pane's Create Dimensions run, marshalled onto Revit's main thread: for every
    /// level with a reference view, for every selected view of that level, run the reference
    /// view's lines against that view. One transaction for the whole fan-out, including the
    /// selection write — a hard failure rolls back every dimension it created.
    /// </summary>
    public class CreateDimensionsEventHandler : IExternalEventHandler
    {
        private readonly ManualResetEventSlim _done = new(false);
        private int _categoryMask;
        private IReadOnlyList<long> _checkedViewIds = Array.Empty<long>();

        public string Summary { get; private set; } = string.Empty;

        public void Prepare(int categoryMask, IReadOnlyList<long> checkedViewIds)
        {
            _categoryMask = categoryMask;
            _checkedViewIds = checkedViewIds;
            _done.Reset();
        }

        public void WaitForCompletion() => _done.Wait();

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    Summary = "No active document.";
                    return;
                }

                Summary = Run(doc);
                TaskDialog.Show("RVTuk – Auto Dimensions", Summary);
            }
            catch (Exception ex)
            {
                Summary = "Auto Dimensions failed: " + ex.Message;
                TaskDialog.Show("RVTuk – Auto Dimensions (error)", ex.ToString());
            }
            finally
            {
                _done.Set();
            }
        }

        private string Run(Document doc)
        {
            var categories = CategoryMask.FromMask(_categoryMask);
            var selectedViewIds = new HashSet<long>(_checkedViewIds);

            // Re-discovered here rather than trusting the pane's snapshot: the model may have
            // changed since the tree was populated.
            var scopes = LevelScopeFinder.Find(doc);
            var levelsWithoutReferenceView = scopes.Count(s => !s.HasReferenceView);

            var report = new StringBuilder();
            var totalCreated = 0;
            var totalSkipped = 0;

            using (var tx = new Transaction(doc, "Auto Dimensions (scope)"))
            {
                tx.Start();

                try
                {
                    DimensionLineStyle.EnsureExists(doc);

                    foreach (var scope in scopes)
                    {
                        if (!scope.HasReferenceView) continue;

                        var selected = scope.Views.Where(v => selectedViewIds.Contains(v.ViewId)).ToList();
                        if (selected.Count == 0) continue;

                        if (doc.GetElement(new ElementId(scope.ReferenceViewId!.Value)) is not View referenceView)
                            continue;

                        var referenceLines = DimensionRunner.CollectReferenceLines(doc, referenceView);

                        foreach (var viewInfo in selected)
                        {
                            if (doc.GetElement(new ElementId(viewInfo.ViewId)) is not View targetView) continue;

                            var tally = new DimensionRunTally();
                            DimensionRunner.RunPair(doc, referenceLines, targetView, categories, tally);

                            totalCreated += tally.Created;
                            totalSkipped += tally.Skipped;
                            report.AppendLine(
                                $"{scope.LevelName} — {viewInfo.ViewName}: " +
                                $"{tally.Created} created, {tally.Skipped} skipped");
                        }
                    }

                    ScopeSelectionStore.Write(doc, _categoryMask, _checkedViewIds);
                }
                catch
                {
                    tx.RollBack();
                    throw;
                }

                tx.Commit();
            }

            if (report.Length == 0) report.AppendLine("No selected view had a reference view to work from.");
            report.AppendLine();
            report.Append($"Total: {totalCreated} dimension(s) created, {totalSkipped} line(s) skipped.");
            if (levelsWithoutReferenceView > 0)
                report.Append($" {levelsWithoutReferenceView} level(s) skipped — no reference view.");

            return report.ToString();
        }

        public string GetName() => "RVTuk.AutoDimensionsCreateDimensionsEventHandler";
    }
}
```

- [ ] **Step 2: Build both configs**

Run: `dotnet build src/RVTuk.Revit/RVTuk.Revit.csproj -c Release2024`
Expected: `Build succeeded`.

Run: `dotnet build src/RVTuk.Revit/RVTuk.Revit.csproj -c Release2025`
Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```bash
git add src/RVTuk.Revit/AutoDimensions/ExternalEvents/CreateDimensionsEventHandler.cs && git commit -m "feat(auto-dimensions): fan dimensions out across selected views, persist the selection"
```

---

### Task 9: UI — pane view models

**Files:**
- Create: `src/RVTuk.UI/AutoDimensions/ViewModels/CategoryOptionViewModel.cs`
- Create: `src/RVTuk.UI/AutoDimensions/ViewModels/ViewNodeViewModel.cs`
- Create: `src/RVTuk.UI/AutoDimensions/ViewModels/LevelNodeViewModel.cs`
- Create: `src/RVTuk.UI/AutoDimensions/ViewModels/AutoDimensionsPaneViewModel.cs`

**Interfaces:**
- Consumes: `AutoDimensionsScope`, `LevelScope`, `ScopeViewInfo`, `DimensionCategories`,
  `CategoryMask` (Task 1); `ViewModelBase`, `RelayCommand` (existing `RVTuk.UI.Shared.ViewModels`).
- Produces:
  - `AutoDimensionsPaneViewModel(Func<AutoDimensionsScope> discover, Func<int, IReadOnlyList<long>, string> createDimensions)`
    exposing `ObservableCollection<CategoryOptionViewModel> Categories`,
    `ObservableCollection<LevelNodeViewModel> Levels`, `RelayCommand CreateDimensionsCommand`,
    `string StatusMessage`, and `void Refresh()`. Consumed by Tasks 10 (binding) and 11 (wiring).
  - `CategoryOptionViewModel`: `Name`, `Category`, `IsEnabled`, `ToolTip`, `IsChecked`.
  - `ViewNodeViewModel`: `ViewId`, `ViewName`, `IsReferenceView`, `DisplayName`, `IsChecked`.
  - `LevelNodeViewModel`: `LevelName`, `DisplayName`, `HasReferenceView`, `HasNoReferenceView`,
    `Views`, `IsChecked`.

No Revit types cross this boundary: the host hands in two delegates, exactly as
`BrowseLibraryCommand` does for the Family Browser.

- [ ] **Step 1: Create `CategoryOptionViewModel`**

`src/RVTuk.UI/AutoDimensions/ViewModels/CategoryOptionViewModel.cs`:
```csharp
using RVTuk.Core.AutoDimensions;
using RVTuk.UI.Shared.ViewModels;

namespace RVTuk.UI.AutoDimensions.ViewModels
{
    /// <summary>One row of the pane's category checklist.</summary>
    public class CategoryOptionViewModel : ViewModelBase
    {
        public CategoryOptionViewModel(
            string name, DimensionCategories category, bool isEnabled, string? toolTip)
        {
            Name = name;
            Category = category;
            IsEnabled = isEnabled;
            ToolTip = toolTip;
        }

        public string Name { get; }
        public DimensionCategories Category { get; }

        /// <summary>False for Ceilings/Floors: shown for layout continuity, inert until v2.</summary>
        public bool IsEnabled { get; }
        public string? ToolTip { get; }

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set => SetProperty(ref _isChecked, value);
        }
    }
}
```

- [ ] **Step 2: Create `ViewNodeViewModel`**

`src/RVTuk.UI/AutoDimensions/ViewModels/ViewNodeViewModel.cs`:
```csharp
using RVTuk.UI.Shared.ViewModels;

namespace RVTuk.UI.AutoDimensions.ViewModels
{
    /// <summary>One checkable target view under a level.</summary>
    public class ViewNodeViewModel : ViewModelBase
    {
        private readonly LevelNodeViewModel _level;

        public ViewNodeViewModel(long viewId, string viewName, bool isReferenceView, LevelNodeViewModel level)
        {
            ViewId = viewId;
            ViewName = viewName;
            IsReferenceView = isReferenceView;
            _level = level;
        }

        public long ViewId { get; }
        public string ViewName { get; }
        public bool IsReferenceView { get; }
        public string DisplayName => IsReferenceView ? ViewName + "   (reference view)" : ViewName;

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                SetProperty(ref _isChecked, value);
                _level.RecomputeFromViews();
            }
        }

        /// <summary>Used by the level's cascade, which recomputes its own state itself.</summary>
        internal void SetCheckedFromLevel(bool value) =>
            SetProperty(ref _isChecked, value, nameof(IsChecked));
    }
}
```

- [ ] **Step 3: Create `LevelNodeViewModel`**

`src/RVTuk.UI/AutoDimensions/ViewModels/LevelNodeViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using System.Linq;
using RVTuk.Core.AutoDimensions;
using RVTuk.UI.Shared.ViewModels;

namespace RVTuk.UI.AutoDimensions.ViewModels
{
    /// <summary>
    /// One level row. A level with no reference view has nothing to fan out, so it carries no
    /// views at all and renders as an inert red line instead of a checkbox.
    /// </summary>
    public class LevelNodeViewModel : ViewModelBase
    {
        private bool _cascading;

        public LevelNodeViewModel(LevelScope scope)
        {
            LevelName = scope.LevelName;
            HasReferenceView = scope.HasReferenceView;
            Views = new ObservableCollection<ViewNodeViewModel>();

            if (!scope.HasReferenceView) return;

            foreach (var view in scope.Views)
            {
                Views.Add(new ViewNodeViewModel(
                    view.ViewId,
                    view.ViewName,
                    view.ViewId == scope.ReferenceViewId,
                    this));
            }
        }

        public string LevelName { get; }
        public bool HasReferenceView { get; }
        public bool HasNoReferenceView => !HasReferenceView;
        public string DisplayName => HasReferenceView ? LevelName : LevelName + "   — no reference view";
        public ObservableCollection<ViewNodeViewModel> Views { get; }

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                SetProperty(ref _isChecked, value);
                if (_cascading) return;

                foreach (var view in Views) view.SetCheckedFromLevel(value);
            }
        }

        /// <summary>The level is checked exactly when every one of its views is.</summary>
        internal void RecomputeFromViews()
        {
            _cascading = true;
            IsChecked = Views.Count > 0 && Views.All(v => v.IsChecked);
            _cascading = false;
        }
    }
}
```

- [ ] **Step 4: Create `AutoDimensionsPaneViewModel`**

`src/RVTuk.UI/AutoDimensions/ViewModels/AutoDimensionsPaneViewModel.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using RVTuk.Core.AutoDimensions;
using RVTuk.UI.Shared.ViewModels;

namespace RVTuk.UI.AutoDimensions.ViewModels
{
    /// <summary>
    /// The Auto Dimensions scope pane. Both Revit interactions arrive as delegates that block on
    /// an ExternalEvent ping-pong, so both are invoked from the thread pool and their results
    /// marshalled back through the dispatcher — never called on the WPF UI thread, which is
    /// Revit's main thread and would deadlock waiting for its own event.
    /// </summary>
    public class AutoDimensionsPaneViewModel : ViewModelBase
    {
        private readonly Func<AutoDimensionsScope> _discover;
        private readonly Func<int, IReadOnlyList<long>, string> _createDimensions;
        private readonly Dispatcher _dispatcher;

        public AutoDimensionsPaneViewModel(
            Func<AutoDimensionsScope> discover,
            Func<int, IReadOnlyList<long>, string> createDimensions)
        {
            _discover = discover;
            _createDimensions = createDimensions;
            _dispatcher = Dispatcher.CurrentDispatcher;

            Categories = new ObservableCollection<CategoryOptionViewModel>
            {
                new CategoryOptionViewModel("Walls", DimensionCategories.Walls, true, null),
                new CategoryOptionViewModel("Doors", DimensionCategories.Doors, true, null),
                new CategoryOptionViewModel("Windows", DimensionCategories.Windows, true, null),
                new CategoryOptionViewModel("Ceilings", DimensionCategories.Ceilings, false, "Coming in v2"),
                new CategoryOptionViewModel("Floors", DimensionCategories.Floors, false, "Coming in v2"),
            };
            Levels = new ObservableCollection<LevelNodeViewModel>();
            CreateDimensionsCommand = new RelayCommand(RunCreateDimensions, CanCreateDimensions);
        }

        public ObservableCollection<CategoryOptionViewModel> Categories { get; }
        public ObservableCollection<LevelNodeViewModel> Levels { get; }
        public RelayCommand CreateDimensionsCommand { get; }

        private string _statusMessage = "Open the pane to read this project's levels.";
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

        /// <summary>Re-reads levels, views and the persisted selection. Returns immediately.</summary>
        public void Refresh()
        {
            if (IsBusy) return;

            IsBusy = true;
            StatusMessage = "Reading levels and views…";

            Task.Run(() =>
            {
                AutoDimensionsScope scope;
                try
                {
                    scope = _discover();
                }
                catch (Exception ex)
                {
                    _dispatcher.Invoke(() =>
                    {
                        StatusMessage = "Could not read the project: " + ex.Message;
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

        private void Populate(AutoDimensionsScope scope)
        {
            var categories = scope.Selection == null
                ? CategoryMask.Default
                : CategoryMask.FromMask(scope.Selection.CategoryMask);

            foreach (var option in Categories)
                option.IsChecked = option.IsEnabled && categories.HasFlag(option.Category);

            var persisted = scope.Selection == null
                ? null
                : new HashSet<long>(scope.Selection.CheckedViewIds);

            Levels.Clear();
            foreach (var levelScope in scope.Levels)
            {
                var level = new LevelNodeViewModel(levelScope);
                foreach (var view in level.Views)
                {
                    // No persisted selection yet: start with each level's own reference view.
                    view.IsChecked = persisted == null
                        ? view.IsReferenceView
                        : persisted.Contains(view.ViewId);
                }
                Levels.Add(level);
            }

            StatusMessage = Levels.Count == 0
                ? "No plan views with a level in this project."
                : $"{Levels.Count} level(s); {Levels.Count(l => l.HasNoReferenceView)} without a reference view.";
        }

        private int SelectedCategoryMask => Categories
            .Where(c => c.IsEnabled && c.IsChecked)
            .Aggregate(0, (mask, c) => mask | CategoryMask.ToMask(c.Category));

        private List<long> CheckedViewIds => Levels
            .SelectMany(l => l.Views)
            .Where(v => v.IsChecked)
            .Select(v => v.ViewId)
            .ToList();

        private bool CanCreateDimensions() =>
            !IsBusy && SelectedCategoryMask != 0 && CheckedViewIds.Count > 0;

        private void RunCreateDimensions()
        {
            var mask = SelectedCategoryMask;
            var viewIds = CheckedViewIds;

            IsBusy = true;
            StatusMessage = "Creating dimensions…";

            Task.Run(() =>
            {
                string summary;
                try
                {
                    summary = _createDimensions(mask, viewIds);
                }
                catch (Exception ex)
                {
                    summary = "Auto Dimensions failed: " + ex.Message;
                }

                _dispatcher.Invoke(() =>
                {
                    StatusMessage = summary;
                    IsBusy = false;
                });
            });
        }
    }
}
```

- [ ] **Step 5: Build the UI project, both configs**

Run: `dotnet build src/RVTuk.UI/RVTuk.UI.csproj -c Release2024`
Expected: `Build succeeded`.

Run: `dotnet build src/RVTuk.UI/RVTuk.UI.csproj -c Release2025`
Expected: `Build succeeded`.

- [ ] **Step 6: Commit**

```bash
git add src/RVTuk.UI/AutoDimensions && git commit -m "feat(auto-dimensions): add scope pane view models (UI)"
```

---

### Task 10: UI — the pane view

**Files:**
- Create: `src/RVTuk.UI/AutoDimensions/Views/AutoDimensionsPaneView.xaml`
- Create: `src/RVTuk.UI/AutoDimensions/Views/AutoDimensionsPaneView.xaml.cs`

**Interfaces:**
- Consumes: every binding produced in Task 9.
- Produces: `RVTuk.UI.AutoDimensions.Views.AutoDimensionsPaneView : UserControl`. Consumed by
  Task 11 (`AutoDimensionsPaneProvider` and `Application.OnStartup`).

Layout per the spec: header, category checklist, a bordered scrolling level/view tree that takes
the remaining height, and the Create Dimensions button pinned in a footer **outside** the scroll
region — the tree can grow long and the pane's height is whatever the dock leaves it.

- [ ] **Step 1: Create the XAML**

`src/RVTuk.UI/AutoDimensions/Views/AutoDimensionsPaneView.xaml`:
```xml
<UserControl x:Class="RVTuk.UI.AutoDimensions.Views.AutoDimensionsPaneView"
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

        <TextBlock DockPanel.Dock="Top"
                   Text="REFERENCE CATEGORIES"
                   Foreground="{StaticResource Brush.TextMuted}"
                   FontSize="10" FontWeight="SemiBold"
                   Margin="10,10,10,4"/>

        <ItemsControl DockPanel.Dock="Top" ItemsSource="{Binding Categories}" Margin="10,0,10,8">
            <ItemsControl.ItemsPanel>
                <ItemsPanelTemplate>
                    <WrapPanel/>
                </ItemsPanelTemplate>
            </ItemsControl.ItemsPanel>
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <CheckBox Content="{Binding Name}"
                              IsChecked="{Binding IsChecked, Mode=TwoWay}"
                              IsEnabled="{Binding IsEnabled}"
                              ToolTip="{Binding ToolTip}"
                              Margin="0,2,14,2"/>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>

        <Border DockPanel.Dock="Bottom"
                Background="{StaticResource Brush.Panel}"
                BorderBrush="{StaticResource Brush.Border}" BorderThickness="0,1,0,0"
                Padding="10,8">
            <StackPanel>
                <Button Content="Create Dimensions"
                        Command="{Binding CreateDimensionsCommand}"
                        Padding="10,5"
                        HorizontalAlignment="Stretch"/>
                <TextBlock Text="{Binding StatusMessage}"
                           Foreground="{StaticResource Brush.TextMuted}"
                           FontSize="11" TextWrapping="Wrap"
                           Margin="0,6,0,0"/>
            </StackPanel>
        </Border>

        <TextBlock DockPanel.Dock="Top"
                   Text="LEVELS AND TARGET VIEWS"
                   Foreground="{StaticResource Brush.TextMuted}"
                   FontSize="10" FontWeight="SemiBold"
                   Margin="10,0,10,4"/>

        <Border Background="{StaticResource Brush.Input}"
                BorderBrush="{StaticResource Brush.Border}" BorderThickness="1"
                Margin="10,0,10,8">
            <ScrollViewer VerticalScrollBarVisibility="Auto"
                          HorizontalScrollBarVisibility="Disabled">
                <ItemsControl ItemsSource="{Binding Levels}" Margin="6">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <StackPanel Margin="0,0,0,8">
                                <CheckBox Content="{Binding LevelName}"
                                          IsChecked="{Binding IsChecked, Mode=TwoWay}"
                                          FontWeight="SemiBold"
                                          Visibility="{Binding HasReferenceView, Converter={StaticResource BoolVis}}"/>
                                <TextBlock Text="{Binding DisplayName}"
                                           Foreground="{StaticResource Brush.Warning}"
                                           FontWeight="SemiBold"
                                           TextWrapping="Wrap"
                                           Visibility="{Binding HasNoReferenceView, Converter={StaticResource BoolVis}}"/>
                                <ItemsControl ItemsSource="{Binding Views}"
                                              Margin="18,2,0,0"
                                              Visibility="{Binding HasReferenceView, Converter={StaticResource BoolVis}}">
                                    <ItemsControl.ItemTemplate>
                                        <DataTemplate>
                                            <CheckBox Content="{Binding DisplayName}"
                                                      IsChecked="{Binding IsChecked, Mode=TwoWay}"
                                                      Margin="0,1"/>
                                        </DataTemplate>
                                    </ItemsControl.ItemTemplate>
                                </ItemsControl>
                            </StackPanel>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </ScrollViewer>
        </Border>
    </DockPanel>
</UserControl>
```

- [ ] **Step 2: Create the code-behind**

`src/RVTuk.UI/AutoDimensions/Views/AutoDimensionsPaneView.xaml.cs`:
```csharp
using System.Windows.Controls;
using RVTuk.UI.AutoDimensions.ViewModels;

namespace RVTuk.UI.AutoDimensions.Views
{
    public partial class AutoDimensionsPaneView : UserControl
    {
        public AutoDimensionsPaneView()
        {
            InitializeComponent();

            // The pane instance outlives any one document, so its content is re-read every time
            // it becomes visible — Revit restoring it at startup, the user tabbing back to it, or
            // the ribbon command showing it. Refresh returns immediately and no-ops while busy.
            IsVisibleChanged += (_, _) =>
            {
                if (IsVisible && DataContext is AutoDimensionsPaneViewModel viewModel)
                    viewModel.Refresh();
            };
        }
    }
}
```

- [ ] **Step 3: Build the UI project, both configs**

Run: `dotnet build src/RVTuk.UI/RVTuk.UI.csproj -c Release2024`
Expected: `Build succeeded` (XAML compiles; a binding typo would only show at runtime, so re-read
the bound property names against Task 9 before moving on).

Run: `dotnet build src/RVTuk.UI/RVTuk.UI.csproj -c Release2025`
Expected: `Build succeeded`.

- [ ] **Step 4: Commit**

```bash
git add src/RVTuk.UI/AutoDimensions/Views && git commit -m "feat(auto-dimensions): add scope pane view (categories, level tree, pinned action)"
```

---

### Task 11: Revit — pane provider, ribbon command, Application wiring

**Files:**
- Create: `src/RVTuk.Revit/AutoDimensions/AutoDimensionsPaneProvider.cs`
- Create: `src/RVTuk.Revit/AutoDimensions/AutoDimensionsPaneCommand.cs`
- Modify: `src/RVTuk.Revit/Application.cs`

**Interfaces:**
- Consumes: `AutoDimensionsPaneView`, `AutoDimensionsPaneViewModel` (Tasks 9–10),
  `LevelDiscoveryEventHandler` (Task 7), `CreateDimensionsEventHandler` (Task 8),
  `NeoPropertiesPaneProvider.PaneId` (existing).
- Produces: `AutoDimensionsPaneProvider.PaneId`, a registered dockable pane named
  "Auto Dimensions", a "Dimension Scope" ribbon button, and
  `Application.AutoDimensionsPaneViewModel`. Nothing consumes these — this is the wiring task.

Everything lands inside the existing `if (RegisterUnreleasedTools)` blocks: Auto Dimensions stays
hidden for v1. The Neo Properties pane must be registered **before** this one, since the new pane
tabs behind it.

- [ ] **Step 1: Create the pane provider**

`src/RVTuk.Revit/AutoDimensions/AutoDimensionsPaneProvider.cs`:
```csharp
using System;
using Autodesk.Revit.UI;
using RVTuk.Revit.NeoProperties;
using RVTuk.UI.AutoDimensions.Views;

namespace RVTuk.Revit.AutoDimensions
{
    /// <summary>
    /// Registers the scope pane tabbed alongside Neo Properties — both are utility panes, and
    /// tabbing avoids adding a second permanent dock slot.
    /// </summary>
    public class AutoDimensionsPaneProvider : IDockablePaneProvider
    {
        public static readonly DockablePaneId PaneId =
            new DockablePaneId(new Guid("e5d47b31-2c8a-4f16-b0d9-73a5e91c46f2"));

        private readonly AutoDimensionsPaneView _view;

        public AutoDimensionsPaneProvider(AutoDimensionsPaneView view)
        {
            _view = view;
        }

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            data.FrameworkElement = _view;
            data.InitialState = new DockablePaneState
            {
                DockPosition = DockPosition.Tabbed,
                TabBehind = NeoPropertiesPaneProvider.PaneId,
            };
        }
    }
}
```

- [ ] **Step 2: Create the ribbon command**

`src/RVTuk.Revit/AutoDimensions/AutoDimensionsPaneCommand.cs`:
```csharp
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RVTuk.Revit.AutoDimensions
{
    /// <summary>
    /// Shows the scope pane and refreshes it. Refresh returns immediately (it does its Revit work
    /// on a background thread via an ExternalEvent), so this command never waits on an event it
    /// is itself blocking.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class AutoDimensionsPaneCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var pane = commandData.Application.GetDockablePane(AutoDimensionsPaneProvider.PaneId);
            pane.Show();
            Application.AutoDimensionsPaneViewModel.Refresh();
            return Result.Succeeded;
        }
    }
}
```

- [ ] **Step 3: Add the statics to `Application.cs`**

In `src/RVTuk.Revit/Application.cs`, add to the using block at the top (after
`using RVTuk.Revit.AutoDimensions;`):
```csharp
using System.Collections.Generic;
using RVTuk.Revit.AutoDimensions.ExternalEvents;
using RVTuk.Core.AutoDimensions;
```
(`System.Collections.Generic` is not imported in this file today and the `Func<int,
IReadOnlyList<long>, string>` delegate below needs it.)

Add these properties immediately after the existing
`public static RVTuk.UI.NeoProperties.ViewModels.NeoPropertiesViewModel NeoPropertiesViewModel { get; private set; } = null!;`
line:
```csharp
        public static LevelDiscoveryEventHandler LevelDiscoveryHandler { get; private set; } = null!;
        public static ExternalEvent LevelDiscoveryEvent { get; private set; } = null!;
        public static CreateDimensionsEventHandler CreateDimensionsHandler { get; private set; } = null!;
        public static ExternalEvent CreateDimensionsEvent { get; private set; } = null!;
        public static RVTuk.UI.AutoDimensions.ViewModels.AutoDimensionsPaneViewModel AutoDimensionsPaneViewModel { get; private set; } = null!;
```

- [ ] **Step 4: Register the pane in `OnStartup`**

In the existing `if (RegisterUnreleasedTools)` block in `OnStartup`, **after** the
`application.RegisterDockablePane(NeoPropertiesPaneProvider.PaneId, …);` call (the Auto Dimensions
pane tabs behind it, so Neo must exist first), append:
```csharp
                LevelDiscoveryHandler   = new LevelDiscoveryEventHandler();
                LevelDiscoveryEvent     = ExternalEvent.Create(LevelDiscoveryHandler);
                CreateDimensionsHandler = new CreateDimensionsEventHandler();
                CreateDimensionsEvent   = ExternalEvent.Create(CreateDimensionsHandler);

                // The pane (UI project) only ever sees these delegates — no Revit types cross over.
                // Both block on WaitForCompletion, so the view model calls them from the pool.
                Func<AutoDimensionsScope> discoverScope = () =>
                {
                    LevelDiscoveryHandler.Reset();
                    LevelDiscoveryEvent.Raise();
                    LevelDiscoveryHandler.WaitForCompletion();
                    return LevelDiscoveryHandler.Result;
                };

                Func<int, IReadOnlyList<long>, string> createDimensions = (mask, viewIds) =>
                {
                    CreateDimensionsHandler.Prepare(mask, viewIds);
                    CreateDimensionsEvent.Raise();
                    CreateDimensionsHandler.WaitForCompletion();
                    return CreateDimensionsHandler.Summary;
                };

                AutoDimensionsPaneViewModel =
                    new RVTuk.UI.AutoDimensions.ViewModels.AutoDimensionsPaneViewModel(
                        discoverScope, createDimensions);

                var autoDimView = new RVTuk.UI.AutoDimensions.Views.AutoDimensionsPaneView
                {
                    DataContext = AutoDimensionsPaneViewModel
                };
                application.RegisterDockablePane(
                    AutoDimensionsPaneProvider.PaneId,
                    "Auto Dimensions",
                    new AutoDimensionsPaneProvider(autoDimView));
```

- [ ] **Step 5: Add the ribbon button**

In `CreateRibbon`, inside the existing Auto Dimensions panel block, after
`autoDimPanel.AddItem(autoDimBtn);`, append:
```csharp
            var autoDimPaneBtn = new PushButtonData(
                "AutoDimensionsScope",
                "Dimension\nScope",
                assemblyPath,
                typeof(AutoDimensionsPaneCommand).FullName!)
            {
                ToolTip = "Open the Auto Dimensions scope pane: pick reference categories and which views of each level get dimensions"
            };
            autoDimPaneBtn.LargeImage = CreateAutoDimensionsIcon(32);
            autoDimPaneBtn.Image      = CreateAutoDimensionsIcon(16);

            autoDimPanel.AddItem(autoDimPaneBtn);
```

- [ ] **Step 6: Build both configs**

Run: `dotnet build src/RVTuk.Revit/RVTuk.Revit.csproj -c Release2024`
Expected: `Build succeeded`.

Run: `dotnet build src/RVTuk.Revit/RVTuk.Revit.csproj -c Release2025`
Expected: `Build succeeded`.

- [ ] **Step 7: Commit**

```bash
git add src/RVTuk.Revit/AutoDimensions/AutoDimensionsPaneProvider.cs src/RVTuk.Revit/AutoDimensions/AutoDimensionsPaneCommand.cs src/RVTuk.Revit/Application.cs && git commit -m "feat(auto-dimensions): register the scope pane and its ribbon button"
```

---

### Task 12: Full build, docs, and the in-Revit verification pass

**Files:**
- Modify: `docs/tools/auto-dimensions/README.md`
- Modify: `docs/tools/auto-dimensions/backlog.md`
- Modify: `CLAUDE.md`

- [ ] **Step 1: Run the whole Core suite**

Run: `dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj`
Expected: every test passes, including the 20 new ones from Tasks 1–3.

- [ ] **Step 2: Build all three solution configs**

Run: `dotnet build RVTuk.sln -c Release2024`
Expected: `Build succeeded`.

Run: `dotnet build RVTuk.sln -c Release2025`
Expected: `Build succeeded`.

Run: `dotnet build RVTuk.sln -c Release2023`
Expected: `Build succeeded` (Core + UI + KKarea; the new Core/UI files must compile for 2023 too,
even though KKarea ships only Rishui Zamin).

- [ ] **Step 3: Update the tool README**

In `docs/tools/auto-dimensions/README.md`, replace the "What it is" paragraph and the Code/Docs
tables with:

```markdown
**What it is:** draw a detail line on the dedicated "Dimensions_Line" style as a
positional reference; a ribbon command dimensions every wall crossing it, re-runnable
after model changes without re-picking references. A dockable **scope pane** fans the same
per-line logic out across every selected view of every level, and adds doors and windows as
reference categories alongside walls.

**Status:** code-complete, **hidden for v1** behind the `RegisterUnreleasedTools` flag
in `src/RVTuk.Revit/Application.cs`.

**Names:** code `AutoDimensions`; ribbon buttons "Auto Dimensions" (single view, walls only)
and "Dimension Scope" (the pane). Supersedes the old separate `KKimensions` /
DimensionPropagator project.

## Code

| Layer | Folder |
|-------|--------|
| Core  | `src/RVTuk.Core/AutoDimensions/` (crossing finder, reference filter, scope models, category mask, opening segment, id codecs) |
| UI    | `src/RVTuk.UI/AutoDimensions/` (scope pane view + view models) |
| Revit | `src/RVTuk.Revit/AutoDimensions/` (commands, runner, candidate collector, discovery, tracker, line style, pane provider, external events) |
| Tests | `tests/RVTuk.Core.Tests/AutoDimensions/` |

## Docs

- [specs/2026-07-04-auto-dimensions-design.md](specs/2026-07-04-auto-dimensions-design.md) — approved design (single view, walls)
- [specs/2026-07-23-auto-dimensions-scope-pane-design.md](specs/2026-07-23-auto-dimensions-scope-pane-design.md) — approved design (scope pane)
- [plans/2026-07-04-auto-dimensions.md](plans/2026-07-04-auto-dimensions.md) — implementation plan (v1)
- [plans/2026-07-26-auto-dimensions-scope-pane.md](plans/2026-07-26-auto-dimensions-scope-pane.md) — implementation plan (scope pane)
- [backlog.md](backlog.md) — bugs / improvements / ideas
```

- [ ] **Step 4: Update the tool backlog**

In `docs/tools/auto-dimensions/backlog.md`, replace the `## 🚀 Ideas` section body
(`*(none tracked)*`) with:

```markdown
- [ ] **v2 — Ceilings and Floors as reference categories.** Their checkboxes exist in the scope
  pane but are disabled: both are host objects needing top/bottom-face resolution
  (`HostObjectUtils.GetTopFaces`/`GetBottomFaces`), structurally unlike the family-instance
  Left/Right approach doors and windows use. `DimensionCategories` already reserves their bits and
  `CategoryMask.FromMask` clamps them off until the resolution logic exists.
- [ ] **Surface a level with several reference views.** Today the first by ascending `ElementId`
  silently wins; the convention is one per level, so this is only worth doing if it bites.
```

- [ ] **Step 5: Update `CLAUDE.md`**

In `CLAUDE.md`, replace the **Auto Dimensions** feature bullet with:

```markdown
- **Auto Dimensions** (hidden for v1) — draw a detail line on the dedicated "Dimensions_Line" style as a positional reference; a ribbon command dimensions every wall crossing it, re-runnable after model changes without re-picking references. A dockable scope pane ("Dimension Scope") fans the same logic across every selected view of every level and adds doors/windows as reference categories. See [`docs/tools/auto-dimensions/specs/2026-07-04-auto-dimensions-design.md`](docs/tools/auto-dimensions/specs/2026-07-04-auto-dimensions-design.md) and [`docs/tools/auto-dimensions/specs/2026-07-23-auto-dimensions-scope-pane-design.md`](docs/tools/auto-dimensions/specs/2026-07-23-auto-dimensions-scope-pane-design.md).
```

- [ ] **Step 6: Commit**

```bash
git add docs/tools/auto-dimensions/README.md docs/tools/auto-dimensions/backlog.md CLAUDE.md && git commit -m "docs(auto-dimensions): document the scope pane"
```

- [ ] **Step 7: Deploy and verify in Revit**

Temporarily flip `RegisterUnreleasedTools` to `true` in `src/RVTuk.Revit/Application.cs` (do **not**
commit that flip), then, from an elevated shell at the repo root:

```bash
./Deploy.ps1 2025
```

Restart Revit and work through the spec's manual test plan:

1. Open a project with 2+ levels, some with `Dimensions_Line` lines drawn and one with none. Click
   **Dimension Scope** — confirm the pane appears tabbed with Neo Properties, and the tree matches:
   the level with no reference view is red, has no checkbox and no children.
2. Check only one level's reference view; click **Create Dimensions** — the result matches what the
   single-view "Auto Dimensions" button produces in that view.
3. Check the reference view plus another view of the same level — confirm dimensions appear in both,
   each referencing that view's own visible elements (this is what validates the target-view-plane
   projection in `DimensionRunner.ToTargetViewPlane`).
4. Toggle Doors and Windows on and re-run — confirm door/window Left/Right references appear,
   correctly ordered along the line alongside the wall faces.
5. Close and reopen the pane (and reopen the project) — confirm the category and view selections
   come back from the last successful run, not from the defaults.
6. Move a wall and a door, then re-run — confirm the old dimensions in **every** previously targeted
   view are replaced, not duplicated (this validates the tracker's per-view map).
7. Confirm the Ceilings and Floors checkboxes are visibly disabled, show "Coming in v2" on hover,
   and do nothing when clicked.
8. Uncheck every category, then every view — confirm **Create Dimensions** greys out in both cases.
9. Confirm the summary dialog reports per-level/per-view tallies plus the "N level(s) skipped — no
   reference view" line.
10. Run the old single-view **Auto Dimensions** ribbon button once more — confirm it still works
    unchanged (active view, walls only) after the refactor.

If anything in steps 1–10 fails, fix it before flipping `RegisterUnreleasedTools` back to `false`
and finishing the branch.

---

## Deferred / explicitly out of scope

Per the spec's non-goals — do not implement in this plan:
- Ceilings/Floors reference resolution (their checkboxes stay inert).
- More than one reference view per level, or any UI surfacing of that case.
- Views with no `Level` (3D, sections, drafting, schedules, sheets) as reference or target views.
- Any change to how reference lines are authored — placement stays manual.
- Select-all / clear-all controls for the tree or the category list.
- Reworking the single-view ribbon command's behaviour (it keeps active-view, walls-only).

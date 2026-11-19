# Topo Tools — outline corners follow the lines: implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** At every Update and Accept, each outline point of the edited toposolid or floor that the tool did not place takes the height the topo lines imply (rule N), goes back to its own height when no line influences it, and Cancel puts back what the session moved.

**Architecture:** The rule is a pure Core class ported from the Python reference and pinned by 698 test points. A Core planner turns (candidates, rule heights, stored records) into raise / put-back / flag decisions. The Revit applier runs that pass after placing line points, in the same transaction, and stores each corner's own height in a new Extensible Storage schema. Cancel restores from a copy of that record taken at session start.

**Tech Stack:** C# (net48, net8.0-windows, net10.0-windows), Revit API 2024–2027 via Nice3point packages, xunit.

**Spec:** [docs/tools/topo-tools/specs/2026-11-19-outline-corner-heights-design.md](../specs/2026-11-19-outline-corner-heights-design.md). Reference rule: [2026-11-19-outline-corner-heights-reference.py](../specs/2026-11-19-outline-corner-heights-reference.py) (`corner_height`, `prepare`, `make_bridges`, `_in_view`, `segments_touch`, `ray_hit`, `far_part_nearest`, `nearest_on_polyline`). Where the reference and the spec text differ, **the spec text wins** (its cleaning step −1, height quantisation, set-selection ties, touch-by-distance, 1 mm end direction).

## Global Constraints

- Branch `topo-tools-finish`, worktree `D:\Coding\RVTuk\.claude\worktrees\topo-corners`. Never touch `D:\Coding\RVTuk` itself (Daniel's master checkout).
- Every number in the spec's "The rule" section is part of the rule: 1 mm on-line snap, 0.5 mm same height (tenths of a mm, |dz| ≤ 5), 5 cm keep-or-take, 60° bridge facing, 1e-9 m ties, 1e-6 m vertex cleaning, 1e-12 parallel test, 2 mm in-view shortcut, chord deviation ≤ 1 mm for arcs.
- The rule works in metres on plan XY with a local origin; heights are shared (survey) metres. Revit side: XY feet × 0.3048; internal Z = h / 0.3048 − `doc.ActiveProjectLocation.GetProjectPosition(XYZ.Zero).Elevation` (as `TopoSessionRunner`).
- Outline only: never change a vertex more than 1 mm from every loop edge, nor one the ledger records for any line (X, Y, Z within `TopoPointApplier.MatchToleranceFeet`), nor one within 1 mm (plan) of this run's line points.
- No reach limit. Lines from the session's view only, **as drawn** (not clipped), with readable heights; unreadable ones over no part of the element are left out silently.
- New schema: name `RVTukTopoToolsOutlineHeights`, GUID `cca5f35d-98c8-466c-9175-4d51afe00df1`, vendor `KnafoKlimor`, public read/write, one string field `CornersByXy`. Record text: entries `x,y,ownZ,setZ,flag` joined by `;`, numbers in internal feet with `ToString("R", CultureInfo.InvariantCulture)`, flag `0` or `hand`.
- Summary copy (exact; TopoSessionReport, left-to-right):
  - `1 outline corner took its height from the lines.` / `{n} outline corners took their heights from the lines.`
  - `1 outline corner went back to its own height.` / `{n} outline corners went back to their own heights.`
  - `1 outline corner moved by hand is left alone.` / `{n} outline corners moved by hand are left alone.`
  The first two never make `HasProblems` true; the third (corners newly flagged this run) does.
- Core stays free of Revit and WPF types. Namespaces follow folders. Run `.\build\Test-All.ps1` before every commit; build Release2024–2027 with 0 errors and no new warnings.
- Commits: `feat(topo-tools): …` / `fix(topo-tools): …` / `docs(topo-tools): …`, each ending with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. No push.

## Review Focus

1. **A toposolid whose corner sits exactly on a line's end or crossing** (today's exception): it must be raised once, remembered, and go back when the line is deleted — never deleted by a later stale-line cleanup, never reported "moved by hand".
2. **A floor in the view with no line near it**: Update must not switch its shape editing on (README row 24), even though the pass now runs without a line over the element.
3. **Re-running Update with nothing changed**: identical heights, no new records, no "moved by hand" (heights never read the corners' current Z).
4. **Cancel after two Updates that moved a corner twice**: the corner returns to its height at session start, not to the first Update's.
5. **Edit Boundary or Reset Shape between Updates**: records whose vertex is gone are dropped silently; a new corner at a moved spot starts a new record.

---

### Task 1: Port rule N to Core

**Files:**
- Create: `src/RVTuk.Core/TopoTools/OutlineCornerHeights.cs`
- Create: `tests/RVTuk.Core.Tests/TopoTools/OutlineCornerHeightsTests.cs`
- Modify: `tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj` (copy `TopoTools\Fixtures\*.txt` to the output, as the Rishui Zamin fixtures are)
- Fixture (exists): `tests/RVTuk.Core.Tests/TopoTools/Fixtures/outline-corner-heights-vectors.txt`

**Interfaces:**
- Produces: `public sealed record CornerLine(double HeightMetres, IReadOnlyList<XyPoint> PolylineMetres);` and
  `public sealed class OutlineCornerHeights { public static OutlineCornerHeights Prepare(IReadOnlyList<CornerLine> lines); public IReadOnlyList<CornerLine> Bridges { get; } public double? HeightAt(XyPoint v); }` — `null` = left alone. `XyPoint` is `RVTuk.Core.Shared.Geometry.XyPoint`. `Prepare` does cleaning (step −1: local origin, 1e-6 m vertex merge, quantised heights) and bridges (step 0); `HeightAt` does steps 1–5 with the same local origin.

- [ ] **Step 1: Write the failing tests.** A fixture reader for the text format (header comment lines start with `#`; `scene n case`, `line z x,y …`, `bridge z x,y x,y`, `point scene x y height|keep why`; numbers are Python `repr` floats, parse with `CultureInfo.InvariantCulture`). Tests:
  - `MatchesEveryReferencePoint` — theory over all 698 points: `HeightAt` equals the fixture height within 1e-6 m, or is `null` for `keep`.
  - `BuildsTheReferenceBridges` — per scene, `Bridges` has the fixture's bridges (same count; each matches one fixture bridge's height within 1e-6 and its two ends, either order, within 1e-6 m).
  - `OrderAndDirectionDoNotMatter` — for every scene, reverse the line list and every polyline; all 698 values identical (exact).
  - Spot checks with the spec's numbers: parallel (0.3,2.5) → 41.500; screenshot corners → 41.199 / 41.498 / 42.886 (find their scene by case name in the fixture); W2 (0.0,8.10) → keep.
- [ ] **Step 2: Run** `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj -c Release2025 --filter OutlineCornerHeights` — expect FAIL (type missing).
- [ ] **Step 3: Implement** from the spec's rule text, with the reference functions as the guide to the algorithms (nearest on polyline by clamped projection; half-plane clip `s = (P−V)·u − 1e-9`, cut at `t = sA/(sA−sB)`; touch = segment distance ≤ 1e-9 m via strict orientation signs else min endpoint-to-segment distance, after a bbox reject; ray walk with the 1 mm vertex clause). All ties as set selections, never a sort with a tolerance comparer. No LINQ in the per-point hot loops (2,000 points × 200 lines must run well under a second).
- [ ] **Step 4: Run** the filter on Release2024, Release2025 and Release2027 — expect PASS. If a reference point differs, check the spec text first: the fixture was produced by the reference, and the three independent ports matched it; a difference means the port is wrong unless it is one of the spec's documented text-over-reference changes, which never moved a fixture value by more than 1e-7.
- [ ] **Step 5: Commit** `feat(topo-tools): rule N for outline corner heights, in Core, pinned by the reference's 698 points`.

### Task 2: The corner record — codec, store, frozen identifiers

**Files:**
- Create: `src/RVTuk.Core/TopoTools/OutlineCornerRecordCodec.cs`, `tests/RVTuk.Core.Tests/TopoTools/OutlineCornerRecordCodecTests.cs`
- Create: `src/RVTuk.Revit/TopoTools/OutlineCornerStore.cs` (same shape as `TopoLedgerStore`)
- Modify: `tests/RVTuk.Core.Tests/Shared/FrozenIdentifierTests.cs` (pin the GUID, schema name and field name, one occurrence each)

**Interfaces:**
- Produces: `public sealed record CornerRecord(double X, double Y, double OwnZ, double SetZ, bool ByHand);` (internal feet), `OutlineCornerRecordCodec.Encode(IReadOnlyList<CornerRecord>) → string`, `Decode(string?) → IReadOnlyList<CornerRecord>` (skips malformed entries, never throws); `OutlineCornerStore.Read(Element) → IReadOnlyList<CornerRecord>`, `Write(Element, IReadOnlyList<CornerRecord>)` (an empty list removes the entity: `DeleteEntity`), `Raw(Element) → string` (for the session snapshot).

- [ ] **Step 1: Failing tests:** round trip (including `-0`, 1e-17 and 12345.678901234 feet, `hand` flag); malformed entries (`"1,2,3"`, `"a,b,c,d,0"`, `"1,2,3,4,maybe"`, empty string, null) decode to the well-formed rest; encoding is culture-invariant under `de-DE` and `he-IL`.
- [ ] **Step 2: Run** — FAIL. **Step 3: Implement.** **Step 4: Run** Core tests on the three frameworks — PASS; build `RVTuk.Revit` for Release2024.
- [ ] **Step 5: Commit** `feat(topo-tools): an outline corner record on the element, in its own schema`.

### Task 3: The corner pass, planned in Core

**Files:**
- Create: `src/RVTuk.Core/TopoTools/OutlineCornerPlanner.cs`, `tests/RVTuk.Core.Tests/TopoTools/OutlineCornerPlannerTests.cs`

**Interfaces:**
- Consumes: `CornerRecord` (Task 2).
- Produces:
  - `public static IReadOnlyList<XyzPoint> OutlineCornerPlanner.Candidates(IReadOnlyList<XyzPoint> vertices, IReadOnlyList<IReadOnlyList<XyPoint>> loopsFeet, IReadOnlyList<XyzPoint> ledgered, IReadOnlyList<XyzPoint> linePoints)` — vertices within 1 mm (`ShapeReplayPlanner.EdgeBandFeet`) of a loop edge, not matching a ledgered point in X, Y and Z within 0.1 mm, not within 1 mm in plan of a line point.
  - `public static CornerPlan OutlineCornerPlanner.Plan(IReadOnlyList<XyzPoint> candidates, Func<XyzPoint, double?> targetZ, IReadOnlyList<CornerRecord> records)` where `targetZ` returns internal Z or null (left alone), and
    `public sealed record CornerPlan(IReadOnlyList<(XyzPoint At, double ToZ)> Raises, IReadOnlyList<(XyzPoint At, double ToZ)> PutBacks, IReadOnlyList<CornerRecord> NewRecords, int NewlyByHand);`
- Rules (records matched to candidates by plan XY within 1 mm):
  - Record flagged `hand`: no action, kept.
  - Record whose current Z differs from `SetZ` by more than 0.1 mm: flag `hand`, no action, `NewlyByHand++`.
  - Target Z found: raise when it differs from the current Z by more than 0.1 mm; record `OwnZ` = the record's, or the current Z when new; `SetZ` = target.
  - Left alone with a record: put back to `OwnZ` (if it differs by more than 0.1 mm); drop the record.
  - Left alone without a record: nothing.
  - Record with no candidate at its XY: dropped.
- [ ] **Step 1: Failing tests,** one per rule above, plus: a re-run with the same inputs after applying the plan gives no raises, no put-backs and identical records; a corner raised twice keeps the first `OwnZ`; a vertex 0.5 mm from a ledgered point's plan spot but at another Z is a candidate only if not within 1 mm of a line point (Review Focus 1); a vertex 2 mm inside the outline is not a candidate.
- [ ] **Step 2–4:** run (FAIL), implement, run on three frameworks (PASS).
- [ ] **Step 5: Commit** `feat(topo-tools): plan the outline corner pass in Core`.

### Task 4: Run the pass in Update and Accept

**Files:**
- Modify: `src/RVTuk.Revit/TopoTools/TopoSessionRunner.cs`, `src/RVTuk.Revit/TopoTools/TopoPointApplier.cs`, `src/RVTuk.Core/TopoTools/TopoSessionReport.cs`, `tests/RVTuk.Core.Tests/TopoTools/TopoSessionReportTests.cs`

**Interfaces:**
- Consumes: Tasks 1–3.
- Produces: `ApplyOutcome` gains `CornersSet`, `CornersPutBack`, `CornersByHand` (int); `TopoPointApplier.Apply` gains a parameter `OutlineCornerHeights? corners` plus the shared-elevation offset (`double sharedElevationOfInternalZero`); `TopoSessionReport` gains `CornersSet`, `CornersPutBack`, `CornersByHand` and the three sentences of the Global Constraints.

- [ ] **Step 1: Report tests first** (Core): each sentence singular and plural exactly; `CornersSet`/`CornersPutBack` alone keep `HasProblems` and `ShowAfterUpdate` false when lines were applied; `CornersByHand` > 0 makes `HasProblems` true; the marker note still comes last. Run — FAIL; implement in `TopoSessionReport`; run — PASS.
- [ ] **Step 2: Runner.** Read every collected line's height (not only those over the element): parse with `SurveyHeightText.TryParse`; lines over the element keep today's unreadable report; others with unreadable heights are skipped silently. Build `CornerLine`s in metres (XY × 0.3048; arcs: `TopoLineCollector` tessellates today — for curves that are arcs, re-tessellate to the 1 mm chord rule `n = ceil(sweep / (2·acos(1 − 0.001/r)))`; add a `Curve`-aware path in the collector returning the polyline per rule). `OutlineCornerHeights.Prepare` once per Update; pass it to the applier (null when the view has no readable line).
- [ ] **Step 3: Applier.**
  - The early return becomes: nothing to place, no stale line, **and** (`corners == null` and no corner record). Do not `Enable()` the editor before the corner pass knows it has work; for a disabled editor, the corner candidates come from the loops' vertices (the outline's own corners: `SlabTarget.Loops` points), and the editor is enabled only if the plan has a raise or put-back.
  - Edge-point reuse of an existing vertex widens from `IndexOfXy` (0.1 mm) to 1 mm in plan for vertices on the outline; such a reused vertex is no longer silently raised: it becomes a candidate whose target is the edge point's Z through the record (so it goes back when the line goes). Simplest correct form: skip `PlaceEdgePoint` for an edge point within 1 mm of an outline vertex, and let rule N (step 2: within 1 mm of a line → that line's height) raise it in the corner pass.
  - After all line points: `doc.Regenerate()`, read vertices, `Candidates`, `Plan` with `targetZ = v => corners.HeightAt(v in metres) → internal Z`, apply raises and put-backs with `ModifySubElement(vertex, Z − reference)` (reference: learnt this run, else `ReferenceGuess`, corrected once after the first raise exactly as `PlaceEdgePoint` does), `Regenerate` after each raise (batching is a later optimisation, see spec), write `OutlineCornerStore` in the same transaction.
- [ ] **Step 4: Build** Release2024–2027 (0 errors, no new warnings); `.\build\Test-All.ps1` — all pass.
- [ ] **Step 5: Commit** `feat(topo-tools): Update and Accept give outline corners the lines' heights`.

### Task 5: Cancel puts back what the session moved

**Files:**
- Modify: `src/RVTuk.Revit/TopoTools/TopoSession.cs` (a `CornerSnapshot` string taken in `Start` from `OutlineCornerStore.Raw(target)`), `src/RVTuk.Revit/TopoTools/Commands/TopoSessionActions.cs`
- Create (Core, pure): `OutlineCornerPlanner.Restore(IReadOnlyList<CornerRecord> atStart, IReadOnlyList<CornerRecord> now, IReadOnlyList<XyzPoint> vertices) → CornerPlan` with tests in `OutlineCornerPlannerTests.cs`

**Rules:** for each current record not flagged `hand` that differs from the start (no start record, or other `SetZ`): put back to the start's `SetZ` (start record exists) or to `OwnZ` (none); the records become the start's, except `hand` flags set during the session, which stay.

- [ ] **Step 1: Failing Core tests** (Review Focus 4: two moves → start height; a corner only set during the session → `OwnZ` and no record; a `hand` corner untouched; unchanged records → empty plan).
- [ ] **Step 2–3:** implement `Restore`; in Cancel's transaction, after step 4 (texts), run it when the target exists and the snapshot differs from the current record; raise with the applier's reference logic (expose a small internal helper rather than duplicating `PlaceEdgePoint`'s correction). Add "put back {n} corner(s)" to the Cancel message only when n > 0, in the existing sentence style.
- [ ] **Step 4:** builds and Test-All. **Step 5: Commit** `feat(topo-tools): Cancel puts back the outline corners the session moved`.

### Task 6: Docs

**Files:** `docs/tools/topo-tools/README.md`, `docs/tools/topo-tools/backlog.md`, `CLAUDE.md` (Topo Tools bullet: one sentence on outline corners), `docs/tools/topo-tools/specs/2026-07-29-topo-tools-design.md` (mark "not editing points the tool did not make" as superseded for outline points, with a link).

- [ ] README: what Update writes (the rule in the spec's two sentences, the 5 cm and no-limit choices), replace "The one exception", Cancel paragraph, the three summary sentences, the known weaknesses with their workarounds, the names list (new schema), and checklist rows: the three screenshot corners (41.20 / 41.50 / 42.89 ± 2 cm); a corner on a line's end raised then the line deleted → own height; Update twice → nothing changes, no "moved by hand"; Cancel after two Updates → height at session start; a corner moved by hand → left alone, reported once; a corner shared by two toposolids → one height from both sessions; a floor with no line near → stays a plain floor (row 24 reworded); a floor whose corners take heights; `ModifySubElement` on a Corner vertex in 2024–2027; a level or height offset other than 0.
- [ ] Backlog: close "keeps the line's height after the line moves"; add the weaknesses not fixed (stopped-line jump, wall-top pull, flat valley axis) as Ideas.
- [ ] **Commit** `docs(topo-tools): outline corners follow the lines`.

### Task 7: Verify

- [ ] Build `RVTuk.sln` for Release2024, 2025, 2026, 2027: 0 errors, no new warnings. `.\build\Test-All.ps1`: all pass. Record the counts in the ledger.

# Topo Edit Session Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Topo Tools pane with a temporary "Topo Edit" ribbon panel and editable height texts in the drawing, applying topo lines to one selected toposolid or floor.

**Architecture:** Pure geometry and bookkeeping (clipping a line to the element's outline, label placement, text↔line linking, the summary) go to `RVTuk.Core/TopoTools` with xunit tests. The Revit side gets a session object (document, view, target, snapshot), an optional `IUpdater` that restyles and labels new lines while a session is open, plain `IExternalCommand`s for the panel's buttons (no external events, no pane), and a generalised target that is a `Toposolid` or a `Floor`. The existing ledger and `TopoPointApplier` stay, extended with edge points.

**Tech Stack:** C#, Revit API 2024/2025 (Nice3point packages), xunit. net48 + net8.

**Spec:** [`docs/tools/topo-tools/specs/2026-11-15-topo-edit-session-design.md`](../specs/2026-11-15-topo-edit-session-design.md)

## Global Constraints

- Revit 2024/2025 only (`Toposolid`); nothing here builds for 2023. Build: `dotnet build RVTuk.sln -c Release2024` and `-c Release2025`; tests: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj`.
- Core stays free of Revit and WPF types. Namespaces follow folders exactly.
- Line style name `_DP-Topo Line` (unchanged); text type name `_DP-Topo Height`.
- Heights are **shared (survey) elevations**, parsed with `UnitFormatUtils.TryParse(doc.GetUnits(), SpecTypeId.Length, …)` and written with `TopoLengthFormatter.Format`. Internal Z = shared − `doc.ActiveProjectLocation.GetProjectPosition(XYZ.Zero).Elevation`.
- A new line's height = last typed height, else survey **0**.
- New Extensible Storage schema `RVTukTopoHeightTextLink`, GUID `ebcc7eef-5290-473c-bc7a-79e14b69cea0`, VendorId `KnafoKlimor`, Public read/write, one `long` field `LineId`.
- New updater GUID `0aa14034-8c77-48a4-b5d6-dca41ed6da08`, registered with `isOptional: true`.
- Spacing keeps config key `TopoPointSpacingCentimetres` (default 100).
- Point match tolerance 0.1 mm (`TopoPointApplier.MatchToleranceFeet`); edge tolerance 1 mm (`ShapeReplayPlanner.EdgeBandFeet`).
- Only the selected element is ever written to.
- Nobody uses RVTuk yet: no migration of `RVTukTopoLineElevation` heights.

## Review Focus

1. **A line crossing the outline several times** (a wiggly line along the edge, or through a hole) → each inside run is sampled separately, each crossing gets one edge point, nothing is placed in the hole. *Test in Task 2.*
2. **A text edited to something unreadable** ("42,7x", empty, "abc") → that line is skipped and named; its earlier points on the target are removed, not kept at the old height. *Test in Task 5 (summary) and checklist item in Task 10.*
3. **A line and its text copied together** → the copy's text moves to the copied line, not stealing the original's. *Test in Task 4.*
4. **A line whose middle is vertical or points left** → the text reads left-to-right / bottom-to-top, never upside down. *Test in Task 3.*
5. **Pressing Topo Tools with a session already open, or after the session's document was closed** → a clear message / a fresh session, never a stuck hidden-or-shown panel. *Checklist in Task 10; state handled in Task 8.*

---

### Task 1: Revit probe — floors, edge points on a shaped surface, slope arrows (manual, throwaway)

Three API facts are unverified and change Tasks 6–7. Build a throwaway command, have Daniel run it in Revit 2024, record the answers in the spec's "API constraints", then delete the command.

**Files:**
- Create (throwaway): `src/RVTuk.Revit/TopoTools/TopoProbeCommand.cs`, button `TopoProbe` added temporarily to the RVTuk panel in `Application.cs`.
- Modify: `docs/tools/topo-tools/specs/2026-11-15-topo-edit-session-design.md` (results).

- [ ] **Step 1:** Write `TopoProbeCommand : IExternalCommand`. On the one selected `Floor` or `Toposolid`, in a transaction that is **rolled back** at the end, it:
  (a) calls `GetSlabShapeEditor()`, `Enable()` if needed, `AddPoints` one interior point at the centre of the bounding box with absolute Z = box top + 1000 mm, then reports the returned `SlabShapeVertex.Position.Z` vs the asked Z;
  (b) on an element whose shape is already edited (ask the user to raise one corner first), `DrawPoint` at the midpoint of the first outline edge with Z = the surface Z there (from `ReferenceIntersector` or the top face's `Project`), then `ModifySubElement(vertex, +500 mm)`, and reports whether each returned non-null and the final vertex Z;
  (c) for a floor, reports whether `Enable()` throws, and what `sketch.GetAllElements()` contains (class and category name of each) — so a slope arrow can be recognised.
  Show all results in one `TaskDialog`.
- [ ] **Step 2:** Build `-c Release2024`, ask Daniel to deploy (`.\Deploy.ps1 2024`) and run it on: a flat floor, a shape-edited toposolid, a floor with a slope arrow. **Wait for the results.**
- [ ] **Step 3:** Record the results in the spec. Decide from them:
  - Floor Z: if (a) returns the asked Z → floors use absolute Z like toposolids; else note the offset rule and apply it in Task 6.
  - Edge points: if (b) works → Task 6 draws edge points with `DrawPoint` at surface Z then `ModifySubElement` by (target Z − surface Z). If not → fallback: place each edge point inset 1 mm (`EdgeBandFeet`) inside along the line and add it with the interior batch; note in the spec that seams match to within 1 mm.
  - Slope arrow: the recognisable element found in (c) → `SlabRefusals.HasSlopeArrow` in Task 6.
- [ ] **Step 4:** Delete the probe command and its button. Commit: `docs(topo-tools): record the floor / edge point / slope arrow probe`.

---

### Task 2: Core — clip a topo line to the target and split interior from edge points

**Files:**
- Create: `src/RVTuk.Core/TopoTools/TopoLineClipper.cs`, `src/RVTuk.Core/TopoTools/TopoLinePoints.cs`
- Test: `tests/RVTuk.Core.Tests/TopoTools/TopoLineClipperTests.cs`

**Interfaces:**
- Consumes: `TopoLineSampler.Sample(IReadOnlyList<XyPoint>, double)`, `PolygonContainment.Contains/IsNearEdge`.
- Produces:
  - `public static IReadOnlyList<IReadOnlyList<XyPoint>> TopoLineClipper.Clip(IReadOnlyList<XyPoint> polyline, IReadOnlyList<IReadOnlyList<XyPoint>> loops)` — the inside runs, in order along the line; a run that starts or ends at a crossing has the exact crossing point as its first/last vertex.
  - `public sealed record TopoLinePoints(IReadOnlyList<XyPoint> Interior, IReadOnlyList<XyPoint> Edge)` with `public bool IsEmpty`.
  - `public static TopoLinePoints TopoLineClipper.Points(IReadOnlyList<XyPoint> polyline, IReadOnlyList<IReadOnlyList<XyPoint>> loops, double spacing, double edgeTolerance)` — clips, samples each run with `TopoLineSampler`, puts every sample within `edgeTolerance` of an edge (`PolygonContainment.IsNearEdge`) into `Edge`, the rest into `Interior`.

- [ ] **Step 1: Write the failing tests** (square 0..10 outer loop, optional hole 4..6):
  - `ALineWhollyInsideIsOneRunUnchanged` — (1,1)→(9,1): one run equal to the input.
  - `ALineWhollyOutsideHasNoRuns` — (11,0)→(20,0): `Clip` empty; `Points(...).IsEmpty` true.
  - `ALineCrossingTheOutlineIsCutAtTheCrossing` — (−5,5)→(5,5): one run (0,5)→(5,5).
  - `ALineThroughAHoleBecomesTwoRuns` — (1,5)→(9,5) with hole 4..6: runs (1,5)→(4,5) and (6,5)→(9,5).
  - `ALineWeavingAcrossTheEdgeGetsOneEdgePointPerCrossing` — polyline (−1,2)→(1,2)→(1,4)→(−1,4)→(−1,6)→(1,6): crossings at (0,2), (0,4), (0,6) → `Clip` gives two runs, [(0,2),(1,2),(1,4),(0,4)] and [(0,6),(1,6)]; `Points(…, spacing 1, tol 0.001).Edge` = (0,2), (0,4), (0,6) in that order.
  - `EdgePointsAreNotRepeatedAsInterior` — every point of `Edge` absent from `Interior`.
  - `ALineAlongTheEdgeIsAllEdge` — (0,1)→(0,9): `Interior` empty, `Edge` non-empty.
- [ ] **Step 2:** Run `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj --filter TopoLineClipperTests` → FAIL (types missing).
- [ ] **Step 3: Implement.** `Clip`: for each segment, collect parameters `t` of intersections with every loop edge (proper segment–segment intersection, collinear overlaps contribute their endpoints), sort, split the segment at them, keep sub-segments whose midpoint is `PolygonContainment.Contains` (boundary counts as inside, so a segment running along an edge is kept); join consecutive kept sub-segments into runs; drop zero-length runs.
- [ ] **Step 4:** Run the filter → PASS; run the whole suite → PASS.
- [ ] **Step 5:** Commit `feat(topo-tools): clip a topo line to the target's outline`.

---

### Task 3: Core — where a height text sits

**Files:**
- Create: `src/RVTuk.Core/TopoTools/HeightLabelPlacement.cs`
- Test: `tests/RVTuk.Core.Tests/TopoTools/HeightLabelPlacementTests.cs`

**Interfaces:**
- Produces: `public readonly record struct LabelSpot(XyPoint Point, double AngleRadians)`; `public static LabelSpot HeightLabelPlacement.Midpoint(IReadOnlyList<XyPoint> polyline)` — the point at half the polyline's length, and the direction of the segment it lies on, normalised to (−π/2, π/2] so text never reads upside down.

- [ ] **Step 1: Tests:**
  - `MidpointOfAStraightLine` — (0,0)→(10,0): (5,0), angle 0.
  - `MidpointIsByLengthNotByVertexCount` — (0,0)→(1,0)→(1,9): length 10, mid at (1,4), angle π/2.
  - `ALineDrawnRightToLeftReadsLeftToRight` — (10,0)→(0,0): angle 0 (not π).
  - `ALineDrawnDownwardsReadsUpwards` — (0,10)→(0,0): angle π/2.
  - `ADiagonalPointingDownLeftIsFlipped` — (2,2)→(0,0): angle π/4.
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement (walk segments accumulating length). **Step 4:** Run → PASS.
- [ ] **Step 5:** Commit `feat(topo-tools): place a height text at a line's middle, never upside down`.

---

### Task 4: Core — which text belongs to which line

**Files:**
- Create: `src/RVTuk.Core/TopoTools/HeightTextLinker.cs`
- Test: `tests/RVTuk.Core.Tests/TopoTools/HeightTextLinkerTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record TextClaim(long TextId, long? LineId, XyPoint Position)` — `LineId` null when the text has no link.
  - `public sealed record LineSpot(long LineId, XyPoint Midpoint)`
  - `public sealed record LinkPlan(IReadOnlyDictionary<long, long> LineByText, IReadOnlyList<long> Relinked, IReadOnlyList<long> DeleteTexts, IReadOnlyList<long> UnlinkedTexts, IReadOnlyList<long> LinesNeedingText)`
  - `public static LinkPlan HeightTextLinker.Plan(IReadOnlyList<TextClaim> texts, IReadOnlyList<LineSpot> lines)`
- Rules (spec "The text–line link", "Height Labels"): a text claiming a line that is not in `lines` → `DeleteTexts`. Among texts claiming the same existing line, the nearest to its midpoint keeps it (tie → lower `TextId`); the others, plus texts with no link, are relinked in order of `TextId` to the nearest line that has no text yet (`Relinked` lists those text ids; their new line is in `LineByText`); a text with no free line left → `UnlinkedTexts`. Lines with no text after all that → `LinesNeedingText`.

- [ ] **Step 1: Tests:**
  - `EachTextKeepsItsOwnLine` — 2 lines, 2 texts claiming them → `LineByText` = both, nothing else.
  - `ACopiedLineWithItsTextGetsTheCopy` — lines A (mid 0,0) and A′ (mid 0,10); texts t1 at (0,0.2) and t2 at (0,10.2), both claiming A → t1→A, t2→A′, `Relinked` = [t2].
  - `ATextWhoseLineIsGoneIsDeleted` — text claims line 99 not present → `DeleteTexts` = [it].
  - `ALineWithoutATextNeedsOne` — 1 line, 0 texts → `LinesNeedingText` = [line].
  - `ASurplusTextWithNoFreeLineIsLeftAlone` — 1 line, 2 texts claiming it → loser in `UnlinkedTexts`, not in `DeleteTexts`.
  - `AnUnlinkedTextTakesTheNearestFreeLine` — text with `LineId` null near line B → B.
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement. **Step 4:** Run → PASS.
- [ ] **Step 5:** Commit `feat(topo-tools): link height texts to lines, surviving copies and deletions`.

---

### Task 5: Core — the session summary

**Files:**
- Create: `src/RVTuk.Core/TopoTools/TopoSessionReport.cs`
- Test: `tests/RVTuk.Core.Tests/TopoTools/TopoSessionReportTests.cs`

**Interfaces:**
- Produces: `public sealed class TopoSessionReport` with settable `int LinesApplied, PointsAdded, PointsReplaced, PointsMovedByHand, PointsLost`, `List<string> UnreadableHeights` (the raw text of each), `int UnlinkedTexts`; `string Summarise()`; `bool HasProblems` (true when any of unreadable, moved by hand, lost, unlinked is non-zero).

- [ ] **Step 1: Tests** (exact strings, matching `ConvertReport`'s style):
  - `CountsAppliedLines` — 3 lines, 40 added, 38 replaced → `"3 lines applied: 40 points added, 38 replaced."`
  - `NamesEachUnreadableHeight` — `UnreadableHeights` = ["42,7x", ""] → summary contains `"2 lines skipped: '42,7x', '' are not heights."` and `HasProblems` true.
  - `SingularForOne` — 1 line, 1 added → `"1 line applied: 1 point added, 0 replaced."`
  - `NothingOverTheTarget` — all zero → `"No topo line in this view lies over the selected element."`
  - `MentionsPointsMovedByHandAndLost` — 2 and 1 → contains `"2 earlier points had been moved by hand and were left alone"` and `"1 point could not be placed"`.
- [ ] **Step 2:** FAIL. **Step 3:** Implement. **Step 4:** PASS.
- [ ] **Step 5:** Commit `feat(topo-tools): the edit session's summary`.

---

### Task 6: Revit — the target, the refusals, and applying to one element

**Files:**
- Create: `src/RVTuk.Revit/TopoTools/SlabTarget.cs` (replaces `ToposolidTarget.cs`), `src/RVTuk.Revit/TopoTools/SlabRefusals.cs`, `src/RVTuk.Revit/TopoTools/TopoSessionRunner.cs` (replaces `TopoRunner.cs`)
- Modify: `src/RVTuk.Revit/TopoTools/TopoPointApplier.cs`, `src/RVTuk.Revit/TopoTools/TopoLineCollector.cs`, `src/RVTuk.Revit/TopoTools/ConvertRunner.cs` (use `SlabRefusals`)
- Delete: `ToposolidTarget.cs`, `ToposolidCollector.cs`, `ToposolidRouter.cs`, `TopoRunner.cs`

**Interfaces:**
- Consumes: `TopoLineClipper.Points`, `TopoSessionReport` (Tasks 2, 5). Heights arrive as a parameter (line id → raw text); Task 7 supplies them via `HeightTexts.ReadHeights`.
- Produces:
  - `public sealed class SlabTarget { Element Element; IReadOnlyList<IReadOnlyList<XyPoint>> Loops; long Id; string Label; static SlabTarget? From(Document doc, Element element) }` — `Element` is a `Toposolid` or `Floor`; loops read from `doc.GetElement(element.SketchId)` as `ToposolidCollector.ReadLoops` did.
  - `public static string? SlabRefusals.Of(Document doc, Element element)` — subdivision, model group, design option, borrowed (moved out of `ConvertRunner.RefusalOf`, same wording), plus for a floor with a slope arrow: `"it has a slope arrow, and a sloped floor cannot take shape points"` (detection per Task 1). `ConvertRunner` keeps its own type-availability checks and calls this for the rest.
  - `TopoLineCandidate` loses `ElevationFeet`: `TopoLineCollector.Collect(Document, View)` returns `(long LineId, IReadOnlyList<XyPoint> Polyline)` records for every `_DP-Topo Line` detail curve in the view **not in a group** (`GroupId == InvalidElementId`).
  - `TopoPointApplier.Apply(SlabTarget target, IReadOnlyCollection<long> lineIdsInView, IReadOnlyDictionary<long, LinePointsZ> newPointsByLine, Func<long,bool> lineStillExists)` returning `ApplyOutcome(int Added, int Removed, int Missing, int Lost)`, where `public sealed record LinePointsZ(IReadOnlyList<XyzPoint> Interior, IReadOnlyList<XyzPoint> Edge)`. Stale rule unchanged (ledger lines in view or deleted). Interior points go through `AddPoints` as one batch per line (retry one by one on failure, counting `Lost`, as `SlabShape.AddPoints` does); edge points per the Task 1 result. All positions actually created are recorded in the ledger for that line.
  - `public static TopoSessionReport TopoSessionRunner.Apply(Document doc, ViewPlan view, SlabTarget target, double spacingFeet, IReadOnlyDictionary<long, string> heightTextByLine)` — caller is inside a transaction. For each collected line: clip to `target.Loops` (`edgeTolerance` = `ShapeReplayPlanner.EdgeBandFeet`); empty → not taking part (still in `lineIdsInView`); else parse its height text (missing text = unreadable `""`); unreadable → `UnreadableHeights`; else convert to internal Z and add to `newPointsByLine`. Then one `TopoPointApplier.Apply` on the target.

- [ ] **Step 1:** Implement `SlabTarget`, `SlabRefusals` and switch `ConvertRunner` to it. Build `-c Release2024` → succeeds. Run Core tests → PASS (Convert tests unchanged).
- [ ] **Step 2:** Implement the applier change and `TopoSessionRunner`; delete the four files. The old pane and its handlers still reference `TopoRunner` — leave those handlers compiling by deleting them now if simpler, since Task 9 removes them anyway (then also remove their registration in `Application.cs`, keeping the pane registered with ⇄ only until Task 9).
- [ ] **Step 3:** Build both `-c Release2024` and `-c Release2025` → succeed.
- [ ] **Step 4:** Commit `refactor(topo-tools): apply to one selected toposolid or floor, clipped to it`.

---

### Task 7: Revit — height texts

**Files:**
- Create: `src/RVTuk.Revit/TopoTools/HeightTextType.cs`, `src/RVTuk.Revit/TopoTools/HeightTextLinkStore.cs`, `src/RVTuk.Revit/TopoTools/HeightTexts.cs`, `src/RVTuk.Revit/TopoTools/LastTypedHeights.cs`

**Interfaces:**
- Consumes: `HeightLabelPlacement.Midpoint`, `HeightTextLinker.Plan` (Tasks 3, 4); `TopoLineCollector.Collect`; `TopoLengthFormatter.Format`.
- Produces:
  - `HeightTextType.Name = "_DP-Topo Height"`; `static ElementId EnsureExists(Document doc)` (inside a transaction) — duplicates `doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType)`, sets `TEXT_BACKGROUND` to opaque (0) so the text masks the line under it, like contour labels (my call; the text sits centred on the line).
  - `HeightTextLinkStore.TryGetLineId(TextNote, out long)`, `Set(TextNote, long lineId)` — schema per Global Constraints.
  - `LastTypedHeights.Get(Document) -> double` (shared feet; 0 when none), `Set(Document, double)` — static, in memory, keyed by the `Document` instance (`ConditionalWeakTable` or a dictionary cleared on `DocumentClosing`).
  - `HeightTexts.Label(Document doc, View view, ICollection<ElementId>? created) -> int unlinked` — inside a transaction. Collects topo lines and `_DP-Topo Height` texts in the view, runs `HeightTextLinker.Plan`, deletes `DeleteTexts`, writes new links, creates a text for each of `LinesNeedingText` with `TopoLengthFormatter.Format(doc, LastTypedHeights.Get(doc))`, and moves/rotates every linked text to its line's `Midpoint` (move via `ElementTransformUtils.MoveElement`, rotate about the text's coord by the difference between the wanted angle and `BaseDirection`'s). Created text ids are added to `created` when given (the session tracks them for Cancel).
  - `HeightTexts.CreateFor(Document doc, View view, CurveElement line, double sharedFeet) -> TextNote` — used by the updater.
  - `HeightTexts.ReadHeights(Document doc, View view) -> IReadOnlyDictionary<long, string>` — line id → trimmed text of its linked text.
  - `static bool HeightTexts.TryParse(Document doc, string text, out double sharedFeet)`.

- [ ] **Step 1:** Implement the four files. Wire `TopoSessionRunner.Apply`'s caller (Task 9) to `ReadHeights`. Build both configs → succeed.
- [ ] **Step 2:** Commit `feat(topo-tools): height texts on the lines, linked and re-centred`.

---

### Task 8: Revit — the session and the updater

**Files:**
- Create: `src/RVTuk.Revit/TopoTools/TopoSession.cs`, `src/RVTuk.Revit/TopoTools/TopoSessionUpdater.cs`

**Interfaces:**
- Produces:
  - `public sealed class TopoSession { Document Document; ElementId ViewId; ElementId TargetId; HashSet<ElementId> Created; Dictionary<ElementId,string> TextSnapshot; static TopoSession? Current; static TopoSession Start(Document, ViewPlan, Element target); static void End(); }` — `Start` snapshots the text of every `_DP-Topo Height` text in the view; `End` clears `Current`, disables the updater and hides the panel (via `TopoEditPanel.Hide()`, Task 9).
  - `public sealed class TopoSessionUpdater : IUpdater` — id from Global Constraints; `static void Register(AddInId id)` at startup: `UpdaterRegistry.RegisterUpdater(updater, isOptional: true)`, triggers `ElementClassFilter(typeof(CurveElement))` + `Element.GetChangeTypeElementAddition()` and `ElementClassFilter(typeof(TextNote))` + `Element.GetChangeTypeAny()`, then `DisableUpdater`; `Enable()`/`Disable()` around a session. `Execute`: ignore unless `TopoSession.Current` is this document; for each added detail `CurveElement` with `OwnerViewId == session.ViewId` and `GroupId` invalid → set `LineStyle` to `_DP-Topo Line`'s `GraphicsStyle`, call `HeightTexts.CreateFor(..., LastTypedHeights.Get(doc))`, add both ids to `Created`; for each modified `_DP-Topo Height` text in the session view whose text parses → `LastTypedHeights.Set`. Skip texts the updater itself created in this call.
  - A `DocumentClosing` and `ViewActivated` handler in `Application`: closing the session's document, or activating a view of another document → `TopoSession.End()` (nothing applied, nothing undone).

- [ ] **Step 1:** Implement. Build both configs → succeed.
- [ ] **Step 2:** Commit `feat(topo-tools): an edit session that restyles and labels new lines`.

---

### Task 9: Revit — the Topo Edit panel and its commands; remove the pane

**Files:**
- Create: `src/RVTuk.Revit/TopoTools/TopoEditPanel.cs`, `src/RVTuk.Revit/TopoTools/Commands/TopoStartCommand.cs`, `DrawTopoLinesCommand.cs`, `HeightLabelsCommand.cs`, `TopoUpdateCommand.cs`, `TopoAcceptCommand.cs`, `TopoCancelCommand.cs`, `FloorTopoConvertCommand.cs` (all in `RVTuk.Revit.TopoTools.Commands`)
- Modify: `src/RVTuk.Revit/Application.cs` (Topo Tools button → `TopoStartCommand`; add `FloorTopoConvert` button "Floor ⇄\nTopo" after it; create the hidden panel; register updater and handlers; remove pane, VM, selection handler and topo external events)
- Delete: `TopoToolsPaneCommand.cs`, `TopoToolsPaneProvider.cs`, `TopoToolsSelectionHandler.cs`, `TopoElevationStore.cs`, `ExternalEvents/Topo*.cs` (all five), `src/RVTuk.UI/TopoTools/` (whole folder)

**Interfaces:**
- `TopoEditPanel.Create(UIControlledApplication app, string assemblyPath)` — called right after the RVTuk panel is built: `CreateRibbonPanel("Topo Edit")`, push buttons `TopoDrawLines` "Draw\nLines", `TopoHeightLabels` "Height\nLabels", `TopoUpdate` "Update", `TopoAccept` "Accept", `TopoCancel` "Cancel", a `TextBoxData("TopoSpacing")` (prompt "Spacing (cm)", value from config, `EnterPressed` saves `TopoPointSpacingCentimetres`; a non-positive or unparsable entry restores the saved value), then `Visible = false`. `Show()` / `Hide()` / `double SpacingFeet`.
- `TopoStartCommand`: if `TopoSession.Current != null` → TaskDialog "A Topo Edit session is already open on {label}." Else: active view must be `ViewPlan`, selection exactly one `Toposolid`/`Floor` else `"Select one toposolid or floor in a plan view, then press Topo Tools."`; `SlabRefusals.Of` → its reason; in one transaction ensure `_DP-Topo Line` (`LineStyleCreator.EnsureExists`) and `HeightTextType.EnsureExists`; `TopoSession.Start`; enable updater; `TopoEditPanel.Show()`.
- `DrawTopoLinesCommand`: if the active view is not the session view → `"Go back to {view name} to draw topo lines."`; else `uiApp.PostCommand(RevitCommandId.LookupPostableCommandId(PostableCommand.DetailLine))`.
- `HeightLabelsCommand`: transaction "Topo Height Labels" → `HeightTexts.Label(doc, view, session.Created)`.
- `TopoUpdateCommand`: one transaction "Topo Update" → `HeightTexts.Label`, then `TopoSessionRunner.Apply(doc, view, SlabTarget.From(target), TopoEditPanel.SpacingFeet, HeightTexts.ReadHeights(doc, view))`; status bar text = `Summarise()`; TaskDialog only when `HasProblems`. Shared by Accept via a static `TopoUpdateCommand.Run(UIApplication) -> bool`.
- `TopoAcceptCommand`: `Run`, then `TopoSession.End()` (also when the update reported problems — the summary says what was skipped).
- `TopoCancelCommand`: one transaction "Topo Cancel": for each id in `Created` that is a line, remove its ledger points on the target (via `TopoPointApplier.Apply` with `lineIdsInView` = those ids and no new points, after deleting them so `lineStillExists` is false); delete all `Created` elements still valid; restore `TextSnapshot` texts whose text differs; then `TopoSession.End()`.
- `FloorTopoConvertCommand`: `TaskDialog.Show("Floor ⇄ Topo", ConvertRunner.Run(uiDoc))`.

- [ ] **Step 1:** Implement the panel and commands; rewire `Application.cs`; delete the pane files and the UI folder. Search: `grep -rn "TopoToolsPane\|TopoElevationStore\|TopoDiscovery\|TopoSetElevation\|TopoSelectLines\|TopoApplyEvent\|TopoConvertEvent" src tests` → no hits.
- [ ] **Step 2:** Build all five configs (`Release2023` must still build without Topo); run Core tests → PASS.
- [ ] **Step 3:** Commit `feat(topo-tools): the Topo Edit panel replaces the pane`.

---

### Task 10: Docs, and the in-Revit pass

**Files:**
- Modify: `docs/tools/topo-tools/README.md` (rewrite the workflow sections; replace the pane-era verification with the checklist below; Floor ⇄ Topo button), `docs/tools/topo-tools/backlog.md` (drop pane-only items: selection round trip, row focus race, status-line vanishing; keep averaging), `CLAUDE.md` (Topo Tools bullet: panel + texts, no pane; converter is the Floor ⇄ Topo button), `docs/toolkit/specs/2026-11-12-addin-split-design.md` (frozen lists: remove the Topo DockablePaneId and `RVTukTopoLineElevation`; add `RVTukTopoHeightTextLink` and the updater GUID; add the `Topo Edit` panel and button names `FloorTopoConvert`, `TopoDrawLines`, `TopoHeightLabels`, `TopoUpdate`, `TopoAccept`, `TopoCancel`, `TopoSpacing`).

- [ ] **Step 1:** Edit the docs. Commit `docs(topo-tools): the edit session replaces the pane`.
- [ ] **Step 2:** Hand Daniel this checklist for Revit 2024 (deploy `.\Deploy.ps1 2024`), and record results in the README:
  1. Nothing / a wall / two toposolids selected → the start message; a subdivision, a sloped floor → refused with its reason.
  2. Topo Edit panel appears; pressing Topo Tools again → "already open".
  3. Draw Lines → lines come out `_DP-Topo Line` with a text "0" (survey 0) at their middle; after typing 42750 in one text, the next line drawn reads 42750.
  4. A line drawn in another view is untouched.
  5. Edit a number → Update → the surface moves; Ctrl+Z undoes the Update in one step.
  6. A line half over the target → points only on the inside part, one on the edge at the line's height; neighbouring toposolid untouched.
  7. Type "42,7x" → Update → skipped and named; its earlier points are gone.
  8. Move a line off the target → Update → its points on the target are gone.
  9. Move a line, then Height Labels → its text re-centres; delete a line → its text goes.
  10. Copy a line with its text → the copy's text moves to the copied line.
  11. A floor target works the same as a toposolid.
  12. Cancel → new lines and their points gone, edited numbers restored, panel hidden.
  13. Accept → panel hidden, lines and texts stay; select the element again, Topo Tools → carries on.
  14. Close the document mid-session → the panel hides; open again → Topo Tools starts fresh.
  15. Spacing box: 50 + Enter → denser points on the next Update; survives a restart.
  16. Floor ⇄ Topo button converts as ⇄ did.
  17. Save a project after a session, open it on a PC without RVTuk → no missing-updater warning.
- [ ] **Step 3:** Fix what fails (each fix its own commit), then commit the ticked checklist.

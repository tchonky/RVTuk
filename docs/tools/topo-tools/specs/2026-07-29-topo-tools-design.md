# Topo Tools — heights drawn as lines, applied as toposolid points

**Date:** 2026-07-29
**Status:** Design, approved

## Why

Shaping a toposolid by hand means clicking points one at a time and typing an elevation into each,
in a view where nothing tells you which point you are on. A site arrives as contours — a set of
runs, each at one known height — and that shape is far easier to *draw* than to click. It is also
the thing that changes: a contour nudged 2 m, a pad dropped 300 mm, a survey revised. Every such
change today means finding the affected points again and retyping them.

Topo Tools inverts it. You draw a detail line on a floor plan, give it a height, and the tool puts
points along that line on the toposolid underneath. The lines stay in the model as the editable
record of the intent, so the second run — after you move a line, change its height, or delete it —
lands exactly where the first would have if you had drawn it that way to begin with.

v1 is that one operation. The name is deliberately an umbrella: creating toposolids, reading
contours from a survey DWG, subdivisions and split lines are later features on the same foundation.

## Scope

**In:** detail lines on a dedicated style, carrying a shared elevation, sampled into points and
applied to the toposolids under them, in the active view, re-runnably.

**Out of v1** (each a clean addition on this foundation, none of it designed here): creating
toposolids; reading contours from an imported survey DWG (reuses the sampler and router untouched);
subdivisions and split lines/creases; multi-view scope; editing points the tool did not make.

**Revit 2024/2025 only.** Toposolid does not exist in 2023, so KKarea never sees this tool.

## The API, as verified

Checked against `Nice3point.Revit.Api.RevitAPI` 2024.3.40 and 2025.4.50 before designing, because
the obvious approach does not exist.

**There is no `Toposolid.AddPoints`.** `Toposolid` derives from `CeilingAndFloor` and reaches its
points through the slab shape editor:

```
Toposolid            GetSlabShapeEditor() → SlabShapeEditor
                     SketchId, HostTopoId, GetSubDivisionIds()
SlabShapeEditor      AddPoints(IList<XYZ>) → IList<SlabShapeVertex>
                     DrawPoint(XYZ) → SlabShapeVertex
                     DeletePoint(SlabShapeVertex) → bool
                     SlabShapeVertices → SlabShapeVertexArray
                     IsEnabled, Enable()
                     ResetSlabShape()          ← wipes the shape; never called
SlabShapeVertex      Position (XYZ), VertexType     ← no ElementId
Sketch               Profile (CurveArrArray), SketchPlane
ProjectLocation      GetProjectPosition(XYZ) → ProjectPosition
ProjectPosition      Elevation (double, internal units)
```

`AddPoints` takes absolute model `XYZ`, which is what the tool wants: one shared→internal
conversion per run and no per-point bookkeeping about levels.

Two consequences shape everything below. **A point has no identity** — `SlabShapeVertex` is a
transient wrapper around a position, so tracking cannot store ids. And **`AddPoints` returns the
vertices it created**, so the tool can record the positions Revit actually produced rather than the
ones it asked for.

## Decisions

| Question | Decision |
|---|---|
| What carries the height | The tool's own Extensible Storage on the line, typed in the pane (**revised 2026-07-30** — see below) |
| What kind of line | **Detail** lines — view-specific, so site scribbles never appear in other plans, sections or 3D |
| What the number means | **Shared (survey) elevation** — absolute, as a surveyor's contour drawing quotes it |
| Which lines count | Detail lines on the dedicated `Topo_Line` style **and** carrying an elevation |
| How many points per line | One spacing figure in the pane; vertices always kept |
| Which toposolid | Whichever one lies under the point, found automatically |
| Second run | The tool tracks its own points and replaces them |
| Entry point | A dockable pane |
| Ribbon | A panel on the **Massing & Site** tab |

Two of these deserve their reasoning recorded.

**Shared elevation, not project elevation.** Site heights come off a survey as absolute values
(`42.750`), and re-typing them as project-relative is where mistakes enter. The cost is a
dependency on document state outside the line: relocating the survey point or re-acquiring
coordinates changes what a stored `42.750` resolves to. For site work that is correct — the survey
is the truth and a re-run corrects the model — but it is a real dependency, not a free one.

**Detail lines, not model lines.** A model line at the right elevation would be self-describing and
visible in 3D, which is genuinely attractive. It also puts working lines into every view that sees
that region, forever. View-specific lines keep the mess where it was made; the price is that the
height lives beside the geometry rather than in it, and that lines in a view the tool is not
scanning are invisible to it (handled below, under deletion). That price turned out to be steeper
than this section first assumed — see the revision under "The height".

## Data model

### `Topo_Line` — the line style

A subcategory of Lines named `Topo_Line`, exactly parallel to Auto Dimensions'
`Dimensions_Line`. It is the opt-in: it makes topo lines identifiable at a glance, gives them their
own colour and weight, and makes them filterable in Visibility/Graphics.

### The height — revised 2026-07-30

**The original design put the height in a shared parameter, `TOPO_Elevation`, bound to the Lines
category. That is impossible, and the tool had to change.**

`Category.AllowsBoundParameters` is false for `OST_Lines`: `BindingMap.Insert` refuses *visible*
shared or project parameters on it and returns false. Revit's own Parameter Properties dialog shows
the same limit from the other side — "Lines" appears in the category list with **no checkbox**, and
only sub-categories such as Path of Travel Lines can be ticked. Model and detail lines cannot carry
a project parameter at all. Only a non-user-visible parameter will bind, which is worthless here:
the entire purpose was typing the height in Properties.

Nor can the design be rescued by changing the element. No view-specific curve of arbitrary shape
accepts a bound parameter: line-based Detail Items are a single straight segment, Filled Regions
must close, Path of Travel is generated between two picked points.

**So the height lives in the tool's own Extensible Storage on each line, and the pane is its
editor.** One double field carrying the shared elevation, its `SetSpec(SpecTypeId.Length)` declaring
what the number means. The pane already lists every topo line in the view; each row now carries an
editable height, and the text is parsed by `UnitFormatUtils.TryParse` against `SpecTypeId.Length` —
so you type in the project's own units, and Revit's own parser decides what "42.750" means rather
than this tool guessing at millimetres.

Unset is the absence of the storage entity, never a value. An explicit **0.000 is a legitimate
shared elevation**; a forgotten one is flagged in the pane and skipped. Clearing a row's text
deletes the entity, which is how a height is un-set.

What this costs, stated plainly: the height is invisible outside our pane — no Properties palette,
no schedule, no tag. Lines could never be scheduled or tagged anyway, so the loss is the Properties
palette alone. What it buys is that the height is no longer at the mercy of a category limit, takes
any precision, and needs no shared-parameter file.

**Because the height cannot be seen by selecting a line, the pane must close that loop:** selecting
a topo line in the view highlights its row, and each row can select its line in the view. Without
that, a list of "Line 418732" is unusable in a drawing with twenty contours.

The line style remains the opt-in and is all the pane's setup action now creates.

### The ledger — Extensible Storage on each toposolid

One entity per toposolid, mapping **source line id → the points that line owns on this toposolid**,
serialised into a single string field (Extensible Storage has no 64-bit integer field and no map
key type; `IdMapCodec` in Auto Dimensions solved the same problem the same way).

**It lives on the toposolid, not on the line** — a deliberate departure from `AutoDimensionTracker`.
Storage on a line dies with the line, orphaning its points forever. On the toposolid, a run sees a
recorded line id that no longer resolves and cleans up after it.

That placement also settles the awkward case detail lines create. A line absent from the scanned
view is not the same as a deleted line, and the ledger must never confuse them: it prunes a line's
points only when `doc.GetElement(id)` returns null — genuinely deleted — never merely because this
run did not see it.

Positions are stored as recorded from `AddPoints`' return value. On re-run a recorded position is
matched to a live vertex within **0.1 mm**. A recorded point with no vertex there was moved or
deleted by hand since the last run: it is left alone and counted in the summary, rather than
guessed at.

## The pipeline

Four stages, each with one job. Only the two middle ones need to think, and neither needs Revit.

### 1. Collect — Revit

Every `CurveElement` in the active view whose line style is `Topo_Line`, with the shared elevation
from its storage entity. Each curve is tessellated (`Curve.Tessellate()`) into an XY polyline; the
detail curve's own Z is the view plane and is discarded. A line with no elevation is collected and
flagged, not dropped — the pane's job is to say why nothing happened.

**Plan views only.** A detail curve's geometry comes back in world coordinates on the view's sketch
plane, so dropping Z recovers the drawn shape only where that plane is horizontal. In a section or
elevation the same projection collapses the line to a streak across the site. The pane therefore
requires a `ViewPlan` and says so plainly in anything else, rather than producing nonsense.

The run's single elevation conversion:

```
internalZ = topoElevation − doc.ActiveProjectLocation.GetProjectPosition(XYZ.Zero).Elevation
```

Both terms are internal units, so this is subtraction and nothing else. `GetProjectPosition(XYZ.Zero).Elevation`
is the shared elevation of the internal origin; rotation and true north do not enter into Z.

### 2. Sample — Core, pure

Walk the polyline and emit points:

- **every tessellation vertex is a candidate**, so arcs and corners stay true;
- any segment longer than the spacing is divided evenly (`n = ceil(length / spacing)`, interior
  candidates at `i · length / n`), so no gap exceeds the spacing;
- candidates are emitted in order, and one is dropped when it lies within **a tenth of the spacing**
  of the previously emitted point **or of the very first emitted point**. Near-duplicate points make
  slivers, not detail.

Every point of one line gets that line's single Z.

The second half of the drop test is not decoration. `Curve.Tessellate()` on a closed loop returns
the start point again as the last point, so testing only against the previous point stacks two
coincident vertices on every closed contour. Nor can vertices be exempted from the test, which an
earlier draft of this design proposed: a tessellated tight arc emits vertices millimetres apart, and
exempting them puts that sliver straight onto the toposolid. Even division already guarantees
interior candidates sit more than half a spacing apart, so the test only ever fires on genuine
near-duplicates.

### 3. Route — pure test, Revit data

Each point goes to the toposolid whose plan footprint contains it, read from `SketchId →
Sketch.Profile`, bounding-box pre-filtered, then even-odd point-in-polygon on the tessellated loops.
Three rules follow:

- **Candidates are the toposolids visible in the active view.** Phase, design option and worksets
  therefore already decide what "the toposolid below" means, without the tool re-implementing any
  of it.
- **Overlapping in plan is resolved in Z:** the point goes to the toposolid whose bounding-box Z
  range is nearest the point's Z — zero distance when the point falls inside that range. "Below or
  above" by proximity, not by collection order. Ties go to the lower element id, so a re-run never
  shuffles a point between two toposolids.
- **Subdivisions are excluded** (`HostTopoId` is a valid id); their points go to the host. A
  driveway subdivision has a shape of its own, and writing to both would fight itself.

A point inside no footprint is skipped and counted.

### 4. Apply — Revit

One transaction for the whole run, so one undo step. Per toposolid:

1. `GetSlabShapeEditor()`; `Enable()` if not enabled. `ResetSlabShape()` is never called.
2. Delete the ledger's points for **every line this run saw** and for lines now genuinely deleted,
   matching each recorded position to a live vertex within 0.1 mm. Unmatched records are dropped
   from the ledger and reported.
3. `AddPoints` the new set.
4. Record the returned positions into the ledger and write it back.

Order matters: deleting before adding keeps a moved line from colliding with its own former points.

Two details in step 2 that are easy to get subtly wrong:

- **Every line the run saw**, not only the lines that produced points. A line whose elevation was
  cleared, or that now falls outside every footprint, is skipped for *adding* — but its points from
  the last run must still go, or clearing a height would leave the old height behind.
- **Across every toposolid that holds points for those lines**, not only the ones receiving points
  now. A line dragged from one toposolid onto its neighbour must lose its old points on the first,
  and the ledger on that first toposolid is the only record of them.

## The pane

Docked, titled "Topo Tools", opened by the ribbon button; the same shape as the Auto Dimensions
pane and driven the same way — the view model calls `Func<>`/`Action` delegates supplied by
`RVTuk.Revit`, each a `ExternalEvent.Raise()` + `WaitForCompletion()` ping-pong off the pool. Two
events: discover (read the view's topo lines) and apply (run).

- **Setup banner.** In a project missing the `Topo_Line` style, the pane shows *Set up this project*
  and nothing else works. It cannot be a side effect of the first run: you cannot draw a topo line
  before the style exists. One click, then it never returns.
- **Target** — the active view's name, refreshed when the view changes.
- **Spacing** in millimetres, remembered in `AppConfig` between sessions. (Deliberately a different
  unit convention from the heights, which follow the document: spacing is a tool setting, a height
  is model data the surveyor already quoted in the project's units.)
- **The lines found**, each with an **editable height**, its length, how many points it will make,
  and a status: *ready*, *no elevation set*, or *outside every toposolid*. This list is the answer
  to "what will this do", available before anything is pressed — and, since the height lives
  nowhere else, it is also the only place to set one.
- **Selection, both ways.** Selecting topo lines in the view highlights their rows; each row selects
  its line in the view. This is not a convenience: with the height invisible in Properties, a row
  labelled "Line 418732" would otherwise be unidentifiable.
- **Run**, and then a summary: points added per toposolid, lines skipped and why, points landing
  outside every footprint, and ledger points that had been moved by hand and were left alone.

## Ribbon — revised 2026-07-30

**Intended for the Massing & Site tab. That is not possible, and the button sits on the RVTuk
panel with the other tools.**

The API places a custom panel on the **Add-Ins** tab, on the **Analyze** tab, or on a tab the add-in
creates itself. Nothing else. `Autodesk.Revit.UI.Tab` naming only `AddIns` and `Analyze` is the same
limit seen from the enum, and the string overload does not resolve built-in tabs — verified in
Revit 2024, where it threw and the fallback ran.

The real ribbon can be reached through the undocumented `Autodesk.Windows` API, which is how other
add-ins put panels on built-in tabs. Autodesk states it is unsupported and may break at any time,
and adding a new panel that way frequently does nothing at all — the variant that works is to let
Revit create the panel and then move it. **Rejected:** a button that silently disappears on a Revit
update is a bad trade for placement, in a toolkit installed across a firm.

Two supported alternatives were considered and declined: the Analyze tab (reachable, but a
site-shaping tool under structural and energy analysis is more confusing than Add-Ins), and a
top-level `CreateRibbonTab("RVTuk")` holding all five tools (defensible, but it moves every button
in the toolkit — a decision about RVTuk as a whole, not about this tool).

The attempt-and-fall-back code is gone. It implied a placement that can never succeed.

## Layering

| Layer | Folder | Contents |
|---|---|---|
| Core | `src/RVTuk.Core/TopoTools/` | `TopoLineSampler`, `PolygonContainment`, `TopoPointLedger` + codec, `TopoLineInfo`, `TopoRunSummary` |
| UI | `src/RVTuk.UI/TopoTools/` | pane view + view models |
| Revit | `src/RVTuk.Revit/TopoTools/` | `TopoLineStyle`, `TopoElevationParameter`, `TopoLineCollector`, `ToposolidRouter`, `TopoPointApplier`, `TopoLedgerStore`, `TopoRunner`, pane command + provider, `ExternalEvents/` |
| Tests | `tests/RVTuk.Core.Tests/TopoTools/` | sampler, containment, ledger codec |

Namespace = root namespace + folder path, per CLAUDE.md.

**One piece of housekeeping:** `XyPoint` sits in `RVTuk.Core.AutoDimensions` and both tools need it,
so it moves to `RVTuk.Core.Shared` — mechanical, one file plus `using` lines in eight others. The
repo's own rule is that a file lives in a tool folder only while one tool uses it.

## Errors

The run is one transaction: any failure rolls back to where you started.

- **No toposolid visible in the view** — the run refuses, and says so.
- **Project not set up** — the setup banner; nothing else is reachable.
- **A line with no elevation** — flagged in the list, skipped, counted.
- **A point over no toposolid** — skipped, counted.
- **A ledger point no longer where it was recorded** — left alone, dropped from the ledger, counted.
- **`AddPoints` or the shape editor throwing** — the transaction rolls back and the message is
  shown; the model is untouched.

Nothing in that list stops the rest of the run.

## Tests

Core only, xunit, `tests/RVTuk.Core.Tests/TopoTools/` — the Revit and WPF layers are verified by
hand in Revit, as everywhere else in this repo.

- **Sampler:** even division of a long segment; every vertex kept; near-duplicate suppression at a
  tenth of the spacing; spacing longer than the whole line (endpoints only); a zero-length line; an
  arc's tessellation vertices surviving intact.
- **Containment:** inside, outside, on the edge, a concave footprint, and a multi-loop profile.
- **Ledger codec:** round-trip, empty ledger, many lines and many points, and a decode of malformed
  text returning empty rather than throwing.

## To verify in Revit

Carried into the implementation plan, not resolvable from the reference assemblies:

1. ~~Whether `CreateRibbonPanel("Massing & Site", …)` resolves the built-in tab, or the fallback
   runs.~~ **Answered 2026-07-30: it does not.** The fallback ran; the button is on the RVTuk
   panel. See "Ribbon" above — no supported API can reach that tab.
2. That `SlabShapeEditor.AddPoints` accepts points on a toposolid whose shape editor was never
   enabled, and what `Enable()` does to one that already has points.
3. Whether Revit merges or rejects an added point that coincides with an existing vertex, and what
   `AddPoints` then returns for it — the ledger records whatever comes back, but the summary should
   report the difference.
4. ~~That a `Length` shared parameter binds to the Lines category and appears on a detail line's
   Properties palette.~~ **Answered 2026-07-30: it does not, and cannot.** `OST_Lines` has
   `AllowsBoundParameters == false`, so `Insert` returned false and the tool reported success while
   binding nothing. The height moved to Extensible Storage — see "The height".
5. That an Extensible Storage double field with `SetSpec(SpecTypeId.Length)` round-trips through
   `Get<double>(field, UnitTypeId.Feet)` on a detail line, and that the entity survives copying the
   line.
6. That `UnitFormatUtils.TryParse` against `SpecTypeId.Length` accepts what a user types for a
   survey elevation in this office's unit setup.

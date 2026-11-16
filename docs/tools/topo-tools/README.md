# Topo Tools

**What it is:** an edit session on one toposolid or floor. Select the element in a plan view, press
**Topo Tools**, draw detail lines on the dedicated "_DP-Topo Line" style, type each line's height
(a survey elevation) into the text on the line, and the tool puts points at that height along each
line on the element. Re-runnable: move a line, change its number or delete it, run again, and the
result is what you would have got by drawing it that way to begin with. There is no pane.

## The workflow

1. **Select** one toposolid or one floor in a plan view. The Topo Tools button is on the RVTuk panel
   (Add-Ins tab), and also on the Modify tab while exactly one toposolid (not a subdivision) is
   selected.
2. **Press Topo Tools.** A temporary **Topo Edit** panel appears with **Draw Lines**, **Height
   Labels**, **Update**, **Accept**, **Cancel** and a **Spacing** box. Pressing Topo Tools again
   says a session is already open and on which element. A start with no usable selection says
   "Select one toposolid or floor in a plan view, then press Topo Tools."; a toposolid subdivision,
   a floor with a slope arrow, an element in a model group, outside the edited design option or
   borrowed by another user is refused with its reason.
3. **Draw Lines** starts Revit's Detail Line tool. Every detail line drawn (or copied, or arrayed)
   in the session's view becomes a topo line and gets a `_DP-Topo Height` text at its middle. Lines
   in other views are untouched.
4. **Edit the number** next to a line (double-click it). It is read in the project's units and is a
   survey elevation.
5. **Update** applies all the lines to the element and stays in the session; **Accept** does the
   same and closes it; **Cancel** closes it and undoes what it can. Update and Accept show a
   summary in a TaskDialog every time, with a warning icon when something was skipped. Accept
   leaves the session open if the update could not run at all, so you can fix it or Cancel.
6. Lines and texts **stay in the view** after Accept. Select the element again, press Topo Tools,
   and carry on.

**A new line's height** is the last height typed into any height text (kept in memory per document
until it closes), and survey 0 until something has been typed. A copied line keeps the text copied
with it, not the last typed height.

**Height Labels** is the tidy-up pass (Update runs it first): every topo line without a text gets
one, every text is moved back to its line's middle (by length along the curve, rotated along the
line, never upside-down), and a text whose line is gone is deleted. A text it cannot link to a line
is left alone and counted, never deleted, because it may hold a typed height. Hidden lines and
hidden texts still count: both are found by owner view, not by what is visible.

**Update** reads each text and skips a line whose text is not a height (the summary names it, e.g.
"1 line skipped: '42,7x' is not a height", and that line's earlier points are removed). It writes
only to the selected element. Points held by lines that no longer take part (deleted, moved off the
element, unreadable) are removed; other toposolids and floors are never touched. The one exception: a
vertex that was already there (an outline corner) is raised but never recorded, so it keeps the
height when its line goes away — the tool never deletes a point it did not create. A line partly over
the element counts only for the part over it, with a point on the outline at the line's height so
neighbouring toposolids meet at the same height; the drawn line is never split. One transaction per
Update, so one Ctrl+Z undoes it.

**Cancel** works by snapshot, because no transaction can stay open across user interaction. In one
transaction it deletes the lines and texts the session created, removes those lines' points from
the element through the ledger, and puts back the text of every height edited during the session.
Points that *surviving* lines got from an earlier Update stay: the surface keeps the heights from
the last Update for the lines that still exist, and the next Update reconciles them. Cancel deletes
only what is still a topo line or height text of the session's view, and the session ends only once
its transaction has committed, so a failed Cancel can be pressed again. The session also
ends by itself, keeping everything, when its document closes or another document is activated.

**Spacing** is the distance between points along a line, in centimetres (default 100). Press Enter
to keep a value; it is remembered between Revit sessions.

## Why it is shaped this way

**The height is a text note, not a parameter — forced, not chosen.** The design called for a
parameter on the line. `OST_Lines` has `Category.AllowsBoundParameters == false`, so
`BindingMap.Insert` refuses any *visible* parameter on it, and tags cannot tag detail lines. So
the height is a `TextNote` on the `_DP-Topo Height` type, linked to its line by Extensible Storage
on the text (`RVTukTopoHeightTextLink`). When two texts claim one line (a line copied with its
text), the nearer keeps it and the other relinks to the nearest line without a text.

**An updater restyles the new lines, because the Detail Line tool cannot be preset.** An `IUpdater`
(`TopoSessionUpdater`), enabled only during a session, gives each new detail line in the session's
view the `_DP-Topo Line` style and a text, and remembers heights as they are typed. It is
registered **optional**, so projects it touched open without a missing-updater warning on PCs
without RVTuk. Lines inside groups and sketch lines (region boundaries) are skipped.

**Heights are shared (survey) elevations,** absolute as a surveyor quotes them. The conversion is
one subtraction — `ProjectPosition.Elevation` at the internal origin — which does mean the tool
depends on the document's shared-coordinate setup: relocating the survey point changes what a
stored value resolves to. For site work that is the correct dependency, and a re-run corrects the
model. `AddPoints` takes an absolute Z on a toposolid (probed in Revit 2024, 2026-11-16); on a
floor that is still unverified (checklist below).

**Detail lines, and plan views only.** View-specific lines keep site working lines out of every
other view and out of 3D. A *model* line given the _DP-Topo Line style is ignored rather than
half-supported: it would appear in every plan at once.

**A point has no identity.** There is no `Toposolid.AddPoints`; points go through
`GetSlabShapeEditor()`, and `SlabShapeVertex` is a position rather than an element. So each
element carries an Extensible Storage ledger of `source line id → the points it owns there`,
recording the positions `AddPoints` returned, and a re-run matches them back within 0.1 mm. A
recorded point that has moved was moved by hand: it is left alone and reported.

**The ledger lives on the element, not the line.** Storage on a line dies with the line and
orphans its points; on the element a run sees a line id that no longer resolves and cleans up. It
also means a line merely absent from the scanned view is never mistaken for a deleted one.

**An edge point is raised relative to the unshaped top.** Probed in Revit 2024: `DrawPoint` at the
surface Z works, but `ModifySubElement` measures its offset from the element's unshaped top, not
from the vertex's current height. So an edge point's offset is target Z minus that reference, which
is read back after the first raise and corrected once if needed.

**The styles create themselves.** `_DP-Topo Line` and the `_DP-Topo Height` text type (a duplicate
of the project's default text type) are made when a session starts, if missing. Renaming either
disconnects the tool from every line or text already drawn.

### Why the Modify tab needs AdWindows

Revit's own **Modify | Toposolid** tab cannot be reached by the supported API. The probe (Revit
2024, 2026-11-16) showed it is the single shared contextual tab with Id `Modify`, whose title and
panels Revit swaps for each selection, so there is no tab of its own to find by name — and the
title is localised, so it is never matched. The button is added through the unsupported
`Autodesk.Windows` ribbon (AdWindows, compile-only; nothing is copied to the output) to a panel
shown only while exactly one non-subdivision toposolid is selected. It was decided on 2026-11-16,
reversing "don't retry" for this one button, because another add-in in the office does the same and
"select first, then press" fits it. It is **fail-safe**: any failure is caught and logged, never
shown, and the button is switched off for the rest of the Revit session. The button on the RVTuk
panel stays, so a Revit update that breaks the shortcut costs a shortcut, not the tool. It posts
the RVTuk-panel button's command rather than running anything itself. Floors' contextual tab is not
included. The Topo Edit panel stays on our own tab.

**Status:** registered on the RVTuk panel on the Add-Ins tab, gated by `RegisterTopoTools` in
`src/RVTuk.Revit/Application.cs` (on). The Massing & Site tab is impossible and verified so in
Revit 2024: the API places custom panels only on Add-Ins, on Analyze, or on a tab the add-in creates
itself. Revit 2024/2025 only — `Toposolid` does not exist in 2023, so KKarea never ships it. **The
edit session is code-complete but not yet exercised in Revit**: see the checklist below.

**Names:** code `TopoTools`; ribbon buttons "Topo Tools" and "Floor ⇄ Topo" (RVTuk panel, internal
names `TopoTools` and `FloorTopoConvert`); panel `Topo Edit` (`TopoDrawLines`, `TopoHeightLabels`,
`TopoUpdate`, `TopoAccept`, `TopoCancel`, `TopoSpacing`); line style `_DP-Topo Line`; text type
`_DP-Topo Height`; schemas `RVTukTopoHeightTextLink`, `RVTukTopoToolsPointLedger`; updater
`0aa14034-8c77-48a4-b5d6-dca41ed6da08`.

### In-Revit checklist: Topo Edit session

Tick each case only once it has actually been checked in Revit 2024 (deploy with
`.\Deploy.ps1 2024` from an elevated shell, Revit closed).

| # | Case | Expected | 2024 |
|---|------|----------|------|
| 1 | Nothing / a wall / two toposolids selected, press Topo Tools; then a subdivision, then a sloped floor | The start message for the first three; the subdivision and the sloped floor refused with their reasons | [ ] |
| 2 | Start a session, then press Topo Tools again | The Topo Edit panel appears; the second press says a session is already open | [ ] |
| 3 | Draw Lines, draw two lines; type 42750 in one text; draw a third line | Lines are `_DP-Topo Line` with a text "0" at their middle; the third line reads 42750 | [ ] |
| 4 | Draw a line in another view | It is untouched | [ ] |
| 5 | Edit a number, press Update; then Ctrl+Z | The surface moves; Ctrl+Z undoes the Update in one step | [ ] |
| 6 | A line half over the target | Points only on the inside part, one on the edge at the line's height; the neighbouring toposolid is untouched | [ ] |
| 7 | Type "42,7x", press Update | The line is skipped and named in the summary; its earlier points are gone | [ ] |
| 8 | Move a line off the target, press Update | Its points on the target are gone | [ ] |
| 9 | Move a line, press Height Labels; delete a line | The text re-centres; the deleted line's text goes | [ ] |
| 10 | Copy a line together with its text | The copy's text moves to the copied line and keeps the copied number | [ ] |
| 11 | Run the session on a floor | Works the same as a toposolid | [ ] |
| 12 | Cancel | New lines and their points gone, edited numbers restored, panel hidden | [ ] |
| 13 | Accept; select the element again, press Topo Tools | Panel hidden, lines and texts stay; the session carries on from them | [ ] |
| 14 | Close the document mid-session; open it again, press Topo Tools | The panel hides on close; Topo Tools starts fresh | [ ] |
| 15 | Spacing: type 50, press Enter, press Update; restart Revit | Denser points on that Update; 50 is still there after the restart | [ ] |
| 16 | Floor ⇄ Topo button | Converts as the old ⇄ did | [ ] |
| 17 | Save a project after a session; open it on a PC without RVTuk | No missing-updater warning | [ ] |
| 18 | Select one toposolid | A Topo Tools button appears on the Modify tab; pressing it starts the session exactly as the RVTuk-panel button does; selecting a floor shows no extra button | [ ] |
| 19 | Floor target: a flat floor at a non-zero level, Update with lines at known heights | The points land at the typed absolute Z (`AddPoints` Z is absolute on a floor) | [ ] |
| 20 | Floor target: a floor with a slope arrow | Refused with its reason, at start and on Update | [ ] |
| 21 | Two chained lines meeting on the outline; Update twice | The second summary has no "moved by hand" warning | [ ] |
| 22 | During a session, draw a filled region and a masking region | Their boundaries keep their own style and get no height texts | [ ] |
| 23 | Draw a line, Ctrl+Z it, place an ordinary text note in the view; then Cancel | The text note stays | [ ] |
| 24 | Session on a floor with no lines over it; Update | The floor is not made shape-edited | [ ] |
| 25 | Make Revit roll an Update back (press Cancel in a Revit error dialog during Update) | "Nothing was applied — Revit rolled the change back."; the session stays open | [ ] |

Items 19 and 20 are the two floor facts nothing has verified yet; if a floor wants an offset from
its top, floors need their own Z conversion.

## Floor ⇄ Topo

**The Floor ⇄ Topo button (RVTuk panel) converts the selection.** Every selected floor becomes a toposolid and every
selected toposolid a floor. A mixed selection flips each element its own way, and the new elements
are left selected, so pressing the button again flips them straight back. One Ctrl+Z undoes the whole run. The summary is a TaskDialog.

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
- **RVTuk's own storage:** the point ledger, so Update stays idempotent across a round
  trip, and the Room Floor link.
- **Pinned.**

**Lost:**
- A sloped floor's slope arrow. It comes back as a flat floor with raised corners and the same
  geometry.
- Join Geometry.
- Per-view graphic overrides.
- Excavation relationships (Revit 2025).
- Any point or split line Revit refuses. This is counted in the summary.

**Dependents are confirmed first.** Hosted families, openings, slab edges, reinforcement (rebar,
area and path reinforcement, fabric), tags, dimensions, subdivisions and parts would be deleted
with the source, so each element that has any gets a dialog — *Convert and delete them* or *Skip
this one* — with *Do the same for the rest of the selection*. Enter picks *Skip*; Esc cancels the
whole run before anything has changed.

**Edge points are drawn, not added.** `AddPoints` refuses a point on the outline or a hole's edge,
and one such point fails the whole batch. So the planner sets them apart: they are drawn onto the
new element while it is still flat, then the interior points go in as one batch, and every raise
comes last.

**Refused, with the reason in the summary:**
- toposolid subdivisions;
- elements in a model group;
- elements outside the design option being edited;
- elements borrowed by another user.

**Code:** `ConvertRunner` (the run), `SlabConverter` (one element), `SlabShape`,
`ParameterCarrier`, `ParkedParameterStore`, with `DefaultTypes` and `DependentsCounter` in
`RVTuk.Revit/Shared/`. The pure parts are in Core with tests: `ShapeReplayPlanner`,
`LoopChainer`, `PlaneHeight`, `ParkedParameterCodec`, `ConvertReport`.

### In-Revit checklist: Floor ⇄ Topo

Tick each case only once it has actually been checked in Revit.

| # | Case | Expected | 2024 | 2025 |
|---|------|----------|------|------|
| 1 | **Default toposolid type probe.** Place a toposolid with a type that is *not* first alphabetically, then convert a floor | Record which type the new toposolid gets: the placed one means `GetDefaultFamilyTypeId` answers; the first by name means the fallback ran | [ ] | [ ] |
| 2 | Flat rectangular floor → toposolid | Same outline, level and offset; top at the same height | [ ] | [ ] |
| 3 | Floor with a hole and an arc edge → toposolid → floor | Hole and arc intact both ways | [ ] | [ ] |
| 4 | Shape-edited floor (raised outer **and hole** corners, interior points) → toposolid → floor | Every vertex back at its Z within 0.1 mm | [ ] | [ ] |
| 4b | Shape-edited floor with an arc edge whose end corners are raised → toposolid → floor | Surface along the arc matches the original | [ ] | [ ] |
| 5 | Survey toposolid (hundreds of points) → floor → toposolid | Same surface; acceptable speed | [ ] | [ ] |
| 6 | Update a session, round trip the toposolid, Update again | Same counts added and replaced; nothing reported "moved by hand" | [ ] | [ ] |
| 7 | User-drawn split line on a floor → toposolid | Split line present on the toposolid | [ ] | [ ] |
| 8 | Floor with **several** points on one boundary edge, and one on a hole's edge, all raised to different heights → toposolid | Every edge point present at its height (drawn with `DrawPoint`), none counted as lost | [ ] | [ ] |
| 8b | Survey toposolid clipped to a site boundary, so hundreds of its points lie on the outline → floor | Interior points go in one batch; acceptable speed; no points lost | [ ] | [ ] |
| 9 | Sloped floor (slope arrow) → toposolid → floor | Same geometry; comes back shape-edited with no slope arrow | [ ] | [ ] |
| 10 | Structural floor with a Floors-only shared parameter set to `ריצוף A-12` → toposolid → floor | Structural ticked again; parameter reads `ריצוף A-12` | [ ] | [ ] |
| 11 | Comments, Mark, Phase Created, Workset | Copied both ways | [ ] | [ ] |
| 12 | A Room Floor floor → toposolid → floor, then click its room with Room Floor | The floor is replaced, not duplicated | [ ] | [ ] |
| 13 | Floor with a hosted family: Convert / Skip / Esc / Enter | Deleted and converted / untouched, "kept its 1 hosted element" / "Cancelled — nothing converted." / same as Skip | [ ] | [ ] |
| 13b | Structural floor with rebar; a floor divided into parts | The dialog lists "1 hosted element" / "N parts" before anything is deleted | [ ] | [ ] |
| 14 | Three floors with hosted families, "Do the same" ticked on the first | One dialog only | [ ] | [ ] |
| 15 | Floor whose type name is Hebrew with Latin and a number, e.g. `ריצוף LIQUI HARD 200`, with a hosted family | Dialog text reads correctly; the name and "(id …)" do not swap places | [ ] | [ ] |
| 16 | Mixed selection: 2 floors, 1 toposolid, 1 wall | Each flips; wall ignored; new elements selected | [ ] | [ ] |
| 17 | A toposolid subdivision selected | Skipped: "it is a subdivision" | [ ] | [ ] |
| 18 | An element in a model group; one outside the design option being edited | Both skipped with their reasons | [ ] | [ ] |
| 19 | Ctrl+Z after converting three elements | All three back in one step | [ ] | [ ] |
| 20 | Workshared: an element borrowed by another user | Skipped, naming the owner | [ ] | [ ] |
| 21 | Pinned floor → toposolid | The toposolid is pinned | [ ] | [ ] |
| 22 | Nothing relevant selected, press Floor ⇄ Topo | "Select floors or toposolids first." | [ ] | [ ] |
| 23 | The summary after any conversion | A TaskDialog lists what was converted, skipped and lost | [ ] | [ ] |

## Code

| Layer | Folder |
|-------|--------|
| Core  | `src/RVTuk.Core/TopoTools/` (sampler, line clipper, containment, height label placement, height-text linker, ledger codec, session report, converter planner / chainer / parked-parameter codec / report) |
| UI    | none: the pane is gone, the session lives on the ribbon |
| Revit | `src/RVTuk.Revit/TopoTools/` (`TopoSession`, `TopoSessionUpdater`, `TopoSessionRunner`, `TopoEditPanel`, `ContextualTopoButton`, `HeightTexts`, `HeightTextLinkStore`, `HeightTextType`, `LastTypedHeights`, line style and collector, applier, ledger store, the commands in `Commands/`, and the floor / toposolid converter) |
| Tests | `tests/RVTuk.Core.Tests/TopoTools/` |

`XyPoint` moved to `src/RVTuk.Core/Shared/Geometry/` when this tool was built — Auto Dimensions and
Topo Tools both use it, and the repo's rule is that a file lives in a tool folder only while one
tool needs it. `src/RVTuk.Revit/IsExternalInit.cs` arrived at the same time: this tool declares the
project's first records, and net48 has no such marker type in its BCL.

## Docs

- [specs/2026-11-15-topo-edit-session-design.md](specs/2026-11-15-topo-edit-session-design.md) — the edit session (current design, with the probe results)
- [plans/2026-11-15-topo-edit-session.md](plans/2026-11-15-topo-edit-session.md) — its implementation plan
- [specs/2026-07-29-topo-tools-design.md](specs/2026-07-29-topo-tools-design.md) — the original pane design; the sampling, routing, ledger and applier still stand, the pane and per-line storage are superseded
- [plans/2026-07-29-topo-tools.md](plans/2026-07-29-topo-tools.md) — its implementation plan
- [specs/2026-09-29-floor-toposolid-convert-design.md](specs/2026-09-29-floor-toposolid-convert-design.md) — Floor ⇄ Topo converter design
- [plans/2026-09-29-floor-toposolid-convert.md](plans/2026-09-29-floor-toposolid-convert.md) — its implementation plan
- [backlog.md](backlog.md) — bugs / improvements / ideas

# Topo Tools — edit session on the ribbon, heights as text in the drawing

Date: 2026-11-15. Status: draft for approval. Replaces the pane workflow of
[2026-07-29-topo-tools-design.md](2026-07-29-topo-tools-design.md); the sampling, routing, ledger and
applier from that design stay. Marks: **(yours)** = Daniel decided it; **(my call)** = proposed by
Claude, open to correction.

## Why

Working through a pane is the friction: the height of a line lives in a list, away from the line,
and finding which row is which line needed a two-way selection sync. Daniel wants to **see and
change each line's height in the drawing itself** — edit the number next to the line — and to work
the way Revit's own edit modes work: pick what you are editing, get a temporary set of tools, finish
with Accept or Cancel (yours). With the height editable in the drawing, **the pane is no longer
needed** (yours).

## What the user does

1. In a plan view, select **one toposolid or one floor** — the element to shape (yours).
2. Press **Topo Tools**. A temporary **Topo Edit** panel appears with these buttons (yours, except
   where marked):
   - **Draw Lines** — starts Revit's Detail Line tool; every line drawn becomes a topo line.
   - **Height Labels** — gives every topo line in the view a height text at its middle.
   - **Update** — applies the lines to the selected element and stays in the session (yours: "a
     button to update"; its exact behaviour is my call, below).
   - **Accept** — applies, then closes the panel.
   - **Cancel** — closes the panel, undoing the session's edits as far as possible.
3. To change a height: double-click the number next to a line, type a new one, press Update or
   Accept.
4. Lines and their height texts **stay in the view** after Accept (yours). Later: select the element
   again, press Topo Tools, and carry on where you left off.

```
 Add-Ins (today) / RVTuk tab (after the add-in split)
 ┌ RVTuk ───────────────────────────┐┌ Topo Edit ─────────────────────────────────────────┐
 │ … Topo   Floor⇄Topo  Room Floor … ││ Draw   Height   Update  Accept  Cancel  Spacing   │
 │   Tools                          ││ Lines  Labels                           [100 cm]  │
 └──────────────────────────────────┘└────────────────────────────────────────────────────┘
                                       ↑ visible only during a session

   ──────────── 42750 ────────────        (text at each line's middle, along the line)
        ─────────── 43000 ──────────
              ─────── 43000 ───────       (a new line: gets the last typed height)
```

## Approaches considered

1. **A hidden ribbon panel, shown during a session** — chosen (yours). `RibbonPanel.Visible` is
   supported API; it looks like normal Revit and cannot break on an update.
2. A small floating toolbar window over the drawing — could show the target's name and live
   warnings, but is not the ribbon and gets lost behind Revit.
3. A real contextual tab ("Modify | …") for the whole session — only reachable through the
   unsupported `Autodesk.Windows` ribbon. Ruled out for the panel.

**Amended 2026-11-16 (yours):** the **Topo Tools start button also appears on Revit's own "Modify |
Toposolid" contextual tab** whenever a toposolid is selected — another add-in in the office already
does this, and it fits "select first, then press". It needs the unsupported `Autodesk.Windows`
ribbon (AdWindows.dll), which reverses the earlier "don't retry" for this one button, so it is built
to fail safe: the button **stays on the RVTuk panel too**, and any failure finding or filling the
contextual tab is caught and logged, never shown — a Revit update that breaks it costs a shortcut,
not the tool. Floors' contextual tab is not included (not asked for; trivial to add). The Topo Edit
panel itself stays on our own tab.

**Amended 2026-11-17** (approved by Daniel that day; itself amended on 2026-11-18 after the probe
sitting): [2026-11-17-topo-edit-follows-session-design.md](2026-11-17-topo-edit-follows-session-design.md)
changes this design's panel layout (✗ above ✓ at its left end, then Draw Lines, Height Labels,
Update, Spacing) and its "the Topo Edit panel itself stays on our own tab": during a session the
panel also shows on Revit's Modify tab, through `Autodesk.Windows` under the same fail-safe, and
the ribbon switches to Add-Ins once at the start. The Modify-tab shortcut shows for one
non-subdivision toposolid only, and Floor ⇄ Topo leaves the Modify tab. It also amends "Starting a
session" (a second press asks: go to it, accept it or cancel it), "Draw Lines" (new lines in
another style are converted only while the session is armed, and Draw Lines works from another
view), "Update" (its summary shows only when something needs attention; Accept always shows it),
"The session ends by itself" (at `DocumentClosed`, so a close called off keeps it), and "Reading a
height" and "A new line's height" (heights are metres, measured like a Survey Point level, whatever
the project's units). Saving during a session now asks first. Where the two designs disagree, the
2026-11-17 one holds.

For the height text: **one text at each line's middle** (yours), over a Label-Contours-style label
line, which would leave lines no label line crosses with no visible, editable height. Revit's own
Label Contours cannot be used: its labels only report the surface's height and cannot be edited
or drive anything.

## API constraints that shape the design

- **No contextual tab, no edit mode** for add-ins → a normal panel toggled visible.
- **Contextual tabs only through `Autodesk.Windows`** → the extra start button is added there
  (identified by the tab id the Task 1 probe reports), with the RVTuk-panel button as the fallback.
- **No API to preset the line style of the Detail Line tool** → Draw Lines posts
  `PostableCommand.DetailLine`, and an **`IUpdater`** active only during a session restyles each
  detail line added in the session's view to `_DP-Topo Line`. An updater, not `DocumentChanged`:
  only an updater may modify the document, inside the user's own transaction. It must be
  registered **optional** (`isOptional: true`), or every project it touched warns about a missing
  third-party updater on PCs without the add-in.
- **The Lines category cannot carry a parameter, and tags cannot tag detail lines** → the height is
  a **`TextNote`**, linked to its line through Extensible Storage on the text.
- **A transaction (group) cannot stay open across user interaction** → Cancel cannot be one rollback;
  it undoes by snapshot (below).
- **Floors** have the same editor: `Floor.GetSlabShapeEditor()`, like `Toposolid`. Two caveats: every
  Revit-side type today (`ToposolidTarget`, the collector, the router) is typed to `Toposolid` and must
  be generalised; and that `AddPoints` takes an **absolute** Z on a floor (and `SlabShapeVertex.Position`
  reports one) is proven only on toposolids. **First step of the plan: check it in Revit 2024**; if a
  floor wants an offset from its top, floors get their own Z conversion.

## Probe results (Revit 2024, 2026-11-16)

- **Toposolid, `AddPoints`:** Z is absolute — asked 1000.0 mm, got 1000.0 mm.
- **Toposolid, edge point:** `DrawPoint` at the surface Z works (vertex created at 116.1 mm). But
  `ModifySubElement(vertex, +500 mm)` left it at **500.0 mm**, not 616.1: the offset is measured from
  the element's unshaped top (0 here), not from the vertex's current height. So an edge point is raised
  with offset = target Z − that reference; the reference is read back after the first raise
  (reference = resulting Z − offset used) and corrected once if needed, so nothing assumes where it is.
- **Contextual tab:** there is no "Modify | Toposolid" tab of its own. It is the single shared
  `Autodesk.Windows` tab **Id `Modify`**, contextual, whose title and panels Revit swaps for each
  selection (toposolid-only panels such as `…_ToposolidShaping` appear in it). So the RVTuk panel is
  added to tab `Modify` once and **shown only while the selection is exactly one toposolid**
  (`SelectionChanged`) — never by matching the title, which is localised.
- **Floors:** pending (flat floor, floor with slope arrow).



**Starting a session.**
- The selection must be exactly one `Toposolid` or `Floor`, and the active view a plan view.
  Otherwise a message: "Select one toposolid or floor in a plan view, then press Topo Tools." (my
  call). Refused with a reason, like the converter's refusals: a toposolid subdivision; a floor with
  a slope arrow (shape editing is unavailable on it); an element in a model group, outside the
  edited design option, or borrowed by another user.
- The session is bound to **that document, that view and that element** (my call). Pressing Topo
  Tools again during a session says a session is already open and on which element.
- On start, the `_DP-Topo Line` line style and a `_DP-Topo Height` text type are created if missing
  (my call: a dedicated text type, made by duplicating the project's default text type, so the
  texts are recognisable and their look is set in one place).

**Draw Lines.** Posts the Detail Line command. While the session is open, the updater restyles every
detail line created **in the session's view** to `_DP-Topo Line`, whichever way it was drawn (my
call: including copy/paste and array — anything new in that view is meant as a topo line while the
session is open), and **gives it its height text at once** (my call: with every new line getting a
real height, there is no reason to wait for a button). Lines drawn in other views are untouched. An
element-addition trigger cannot filter by view, so the updater checks each new line's `OwnerViewId`
in `Execute`. Lines inside a group (a placed or pasted detail group) are skipped — restyling group
members from an updater fails or raises group warnings.

**A new line's height** (yours) is **the last height typed**. With nothing typed yet, it is the
survey **0** (yours: texts always show survey elevations, so 0 is the zero of Survey Point levels).
"Last typed" (my call on details): the updater also watches edits to `_DP-Topo Height` texts in the
session's view and remembers the last value that parses; it is kept in memory per document for the
Revit session, so it carries from one session to the next until Revit closes. A copied line keeps
the height of the text copied with it (see the link rule below), not the last typed one.

**Height Labels** (my call on details) — the tidy-up pass, for lines from before a session or
texts that have drifted:
- Every topo line in the session's view without a height text gets one, with the last typed height
  (or survey 0).
- Every existing text is moved back to its line's middle — measured by length along the curve, not
  `Evaluate(0.5, true)`, whose normalised parameter is half the length only for lines and arcs, not
  the splines and ellipses the Detail Line tool also draws — rotated along the line and never
  upside-down. Its number is untouched.
- A text whose line has been deleted is deleted.

**The text–line link** (my call). The text carries the line's id in Extensible Storage. When two texts
claim the same line (a line and its text copied together: the copy's text still points to the
original line), the text nearest that line keeps it, and the other relinks to the nearest topo line
that has no text. A text that cannot be linked is reported and left alone, never deleted, since it
may hold a typed height.

**Reading a height.** The text is parsed in the project's length units with
`UnitFormatUtils.TryParse`, exactly as the pane did, and read as a **shared (survey) elevation** as
before. An empty text or anything unparsable (someone typed over it) → the line is skipped and
named in the summary ("1 line skipped: '42,7x' is not a height").

**Which lines take part** (yours). Only topo lines of the session's view that lie **wholly or partly
over the selected element**; every other line is ignored — not applied, not cleaned, not reported as
outside. A line partly over the element counts **only for its part over it** (yours: "consider just
the part over it"):
- The line is cut where it crosses the element's outline (holes included), and only the inside
  part is sampled.
- **A point is placed at each crossing, at the line's height** (my call). That is what makes two
  neighbouring toposolids meet at the same height along their shared edge: a line drawn across the
  seam puts the same point on both sides, one session each. Points on an outline cannot go through
  `AddPoints` (one such point fails the whole batch); they are drawn with `DrawPoint`, exactly as the
  floor ⇄ toposolid converter already does.
- The drawn line is never split or modified (my call: one line, one height text — splitting would
  double the texts to edit for no gain; "split where the topos meet" is achieved by the cut above).

**Update** (my call). Runs the Height Labels pass first, then re-applies **all** the taking-part
lines to the selected element through the sample → route → apply pipeline. The run is idempotent
(unchanged lines replace their own points with the same points), so "only what changed" and
"everything" give the same model; doing everything keeps one code path and cannot miss a moved line.
The summary is shown in a TaskDialog after every Update and Accept, with a warning heading only
when something was skipped (Revit has no supported API to write to its status bar; amended
2026-11-16 — revisit if it proves noisy).

- **Only the selected element is ever written to.** Its own ledger decides the clean-up: points it
  holds for a line that no longer takes part (deleted, moved off it, or now unreadable) are removed;
  points of taking-part lines are replaced. Other toposolids and floors are not touched, even if a
  line of this view once put points on them — they are cleaned the next time a session runs on
  them (my call, following from "ignore the other lines"; this replaces the old "clean across every
  toposolid" rule).
- One transaction per Update, so Ctrl+Z takes it back in one step.

**Accept** = Update, then hide the panel and end the session.

**Cancel** (my call on details). Restores the session's starting state as far as Revit allows:
in **one transaction**, deletes the topo lines and texts created during the session, **removes the
points those lines own** through the ledger (cheap, and otherwise they would stay with no line to own
them), and puts back the text of every height edited during the session (snapshot of text contents at
session start). Points that *surviving* lines got from an Update stay as they are: those lines still
exist, and the next Update reconciles them. Lines deleted during the session cannot be brought back.
Then hide the panel.

**The session ends by itself** (my call), keeping everything as it is, without applying, when its
document closes or when the user switches to another document. Switching view in the same document
keeps the session open; the buttons still act on the session's view, and Draw Lines asks to return
to it first.

**Spacing** (my call). The point spacing setting (`TopoPointSpacingCentimetres`, default 100 cm) moves
from the pane to a text box on the Topo Edit panel; it keeps its config key.

## What goes away, and what moves

- **The pane goes** (yours), with its view model, the selection sync, the per-row height editor and
  the height store on the line (`TopoElevationStore`, schema `RVTukTopoLineElevation`). The text is
  the height now. Nobody uses RVTuk yet, so there is no migration of stored heights (standing
  decision).
- **The floor ⇄ toposolid converter** lived in the pane (⇄). It becomes its own button **Floor ⇄
  Topo** on the RVTuk panel, next to Topo Tools, acting on the selection exactly as ⇄ did, with its
  summary in a TaskDialog (my call).
- **The add-in split** ([2026-11-12-addin-split-design.md](../../../toolkit/specs/2026-11-12-addin-split-design.md))
  freezes the Topo Tools DockablePaneId and the elevation schema. Whichever of the two lands second
  updates that spec's lists: the pane id and the elevation schema leave, the text-link schema and the
  updater id join. Once the split lands the Topo Edit panel sits on the RVTuk tab, right after the
  Topo panel (the panel is created hidden at startup, right after Topo's own, because the ribbon API
  only appends).

## Goals, non-goals, anti-goals

- **Goal:** set and change heights in the drawing; one click from "these lines" to "this surface".
- **Non-goal:** several target elements at once; multi-view sessions; reading contours from DWG;
  averaging coincident points (still in the backlog, unchanged by this).
- **Anti-goal:** a height that silently goes nowhere — a line over the target whose text cannot be read is
  always named in the summary.
- **Anti-goal:** touching any element other than the selected one.

## Left to the builder

The ribbon icons; the exact wording of messages; how the snapshot for Cancel is stored (in memory is
fine — a session does not survive closing the document); text size and offset from the line (taken
from the `_DP-Topo Height` type); the updater's trigger (`Element.GetChangeTypeElementAddition` on
`CurveElement` in the session's view).

## Verification

Core keeps its tests (sampler, router, ledger codec). New pure logic gets Core tests: the midpoint and
"never upside-down" rotation, cutting a polyline at an outline with holes (crossing points), the text–line relinking rule, height-text parsing outcomes. The rest is
an in-Revit checklist in the tool's README: session start refusals; Draw Lines restyles only in the
session's view and labels each new line with the last typed height (survey 0 at first); Height
Labels re-centres after a move; a line crossing the outline puts a point on the edge at its height
and none outside; a line moved off the target loses its points there; edit a number → Update moves the
surface; a cleared/garbage text is skipped and named; a floor target; Cancel removes new lines and
restores edited numbers; Accept leaves lines and texts in place; reopening a session picks them up;
copying a line with its text relinks the copy; a project saved with the updater opened on a PC
without RVTuk opens without a warning.

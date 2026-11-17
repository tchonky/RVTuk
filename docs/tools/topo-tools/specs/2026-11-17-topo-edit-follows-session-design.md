# Topo Tools — the Topo Edit buttons follow the session

Date: 2026-11-17. Status: draft for approval, second round (the layout, the heights and Floor ⇄ Topo
changed after the first). Amends
[2026-11-15-topo-edit-session-design.md](2026-11-15-topo-edit-session-design.md): its panel layout;
its "the Topo Edit panel itself stays on our own tab"; "Starting a session" (a second press now
asks, item 4); "Draw Lines" (new lines are converted only while armed, item 6, and Draw Lines works
from another view, item 7); "The session ends by itself" (at `DocumentClosed`, item 5); and
"Reading a height" and "A new line's height" (now metres, item 11). Marks: **(yours)** = Daniel
decided it; **(my call)** = proposed by Claude, open to correction.

## Why

- Selecting a toposolid brings up **Modify | Toposolid**, but the Topo Edit buttons live on Add-Ins,
  so people look for them in the wrong place (yours). The Revit 2024 journals of 2026-11-16 show it:
  a session started from Modify | Toposolid, Esc twice, then a search for the Add-Ins tab.
- People may forget an open session and start another task (yours). Revit gives add-ins no locked
  mode like Edit Sketch (see "Not possible"), so the session must instead be hard to lose, and
  forgetting it must cost little.
- The buttons should look like Revit's own edit modes: ✗ and ✓ (yours, 2026-11-17: first drawn as a
  Mode panel of their own, then put inside the one Topo Edit panel).
- Two more changes from the review of this design (yours): heights always in metres and always from
  the Survey Point, whatever the project's units; and Floor ⇄ Topo off the Modify tab.

What forgetting costs today, traced in the code: switching views, editing other elements, saving
and closing are safe. The one real harm is that every detail line drawn or pasted in the session's
view becomes a topo line, and `_DP-Topo Line` looks like any other line until the next Update
reshapes the element along it. A forgotten session is usually found through Topo Tools' dead end,
"A Topo Edit session is already open".

## Goals, non-goals, anti-goals

- **Goals:** during a session the buttons are in view wherever the user looks; a forgotten session
  is noticed early and costs little; the session looks and feels like a Revit edit mode; a height
  text means the same in every project.
- **Non-goals:** a real lock; anything on the Options Bar; buttons drawn in the view; keyboard
  shortcuts for the Topo Edit buttons, which most likely cannot be set today either (probe 7; my
  call).
- **Anti-goals:** anything a Revit update can turn into a broken ribbon or a lost session (every
  unsupported part fails safe, and the supported panels keep working alone); a ribbon that fights
  the user's own tab choices; losing any part of Revit's Detail Line tool (chains, arcs, offsets,
  Pick Lines, Enter to repeat).

## What the user sees

```
 Add-Ins tab during a session — and the same panel on the Modify tab, whatever it says
 ┌ RVTuk ──────┐┌ Topo Edit ──────────────────────────────────┐
 │ …           ││  ✗   Draw    Height   Update   Spacing       │
 │             ││  ✓   Lines   Labels            [100 cm]      │
 └─────────────┘└─────────────────────────────────────────────┘
```

1. **One panel, with ✗ and ✓ stacked at its left end** (yours: "you don't need to separate into two
   panels"). Cancel (red ✗) above Accept (green ✓), icons only; the tooltips keep the words. Then
   Draw Lines, Height Labels, Update, Spacing.
   - The left end rather than the right (my call; you left the side open): it is where your first
     picture had them, and where Revit puts its own Mode panel, before the drawing tools. The cost:
     a panel folded on a narrow screen shows its first icon, the ✗, so the build gives the folded
     panel the Topo Edit icon instead (through Autodesk.Windows; if that part fails, the ✗ shows).
   - The ✗ and ✓ are red and green like Revit's own, on a transparent background, in a light-theme
     and a dark-theme version as Revit's own are, not the toolkit's orange and white on dark (my
     call: they should read as Revit's finish and cancel).
   - ✗ above ✓ is also Revit's own order: its Mode panel lists Cancel Edit Mode first and Finish
     Edit Mode second, and rows are drawn top to bottom (read in Revit's ribbon definitions,
     UIFrameworkRes.dll, 2024–2027).
2. **During a session the panel also shows on the Modify tab, whatever it says** (yours: "ribbon
   follows the session") — Modify | Toposolid, Modify | Place Detail Lines while drawing,
   Modify | Text Notes, Modify | Walls, and the plain Modify tab. It comes after Revit's own panels
   (my call), though Revit may later slot one of its own after it.
   - Being last, it is the first panel to fold into a single "Topo Edit" button on a narrow screen;
     one click opens it. If the probe finds it folded at the office's usual width, that comes back
     to Daniel: accept it, or have Revit's own panels fold first.
   - Not while Revit is in an edit mode (Edit Boundary, any sketch, Edit Group): two ✗/✓ pairs side
     by side would invite clicking ours to finish Revit's sketch, and edit modes are where Revit's
     code is fragile about panels that are not its own (see "How it is built") (my call).
   - The Topo Tools shortcut leaves the Modify tab during a session (my call): the Topo Edit panel
     beside it has ✗ and ✓, which end the session and leave the selection alone, and the shortcut
     then comes back for the toposolid or floor still selected. That is as many clicks as item 4's
     dialog, with one panel fewer on the tab.
   - **Floor ⇄ Topo leaves the Modify tab for good** (yours), session or not. It stays on the RVTuk
     panel; the Modify tab keeps only the Topo Tools shortcut, for one toposolid or one floor. This
     undoes part of the finishing pass of 2026-11-16.
3. **When a session starts, the ribbon switches to Add-Ins** (yours, part of the approach). Revit then
   stays on Add-Ins when the Modify | … tab closes, and comes back to it after the next selection and
   Esc, with Revit's default "return to the previous tab" option. Once, at the start: a tab the user
   picks afterwards is theirs, and Revit returns there as usual (my call).
4. **Topo Tools pressed during a session asks instead of refusing** (my call):
   > Topo Edit is open on Toposolid 123 in Site Plan.
   > - **Go to it** — opens that view and selects that element.
   > - **Accept it** — as ✓; then, if one other toposolid or floor is selected, a session starts
   >   on it.
   > - **Cancel it** — as ✗; then the same.
   > - Closing the dialog changes nothing.
5. **Saving or syncing during a session asks** (my call): "Topo Edit is still open on Toposolid 123:
   changes to its lines and heights since the last Update are not on it yet." — **Accept, then
   save** / **Cancel the session, then save** / **Save as it is**, with a "Don't ask again until this
   Topo Edit ends" box. Esc stops the save when Revit lets it be stopped (not while Revit itself is
   closing), and otherwise saves as it is.
   - Whether Esc during a document's close keeps the document open, or lets Revit close it unsaved,
     is for probe 8. If it closes it unsaved, Esc saves as it is in every case.
   - The session now ends when its document has closed, not when closing starts (my call): a close
     called off at Revit's "Save changes?" keeps the session and its last typed height, and a save
     on the way out still brings up this prompt. Today both end as soon as closing starts.
6. **Ordinary lines drawn after leaving the session's view stay ordinary until Draw Lines is pressed
   again** (my call). The session starts "armed"; opening another view disarms it; Draw Lines arms
   it again. While disarmed, a new detail line in another style is left alone. A new line already on
   `_DP-Topo Line` (a copy, offset, mirror or paste of a topo line) still gets its height text, as
   today.
   - Rejected: converting only while Draw Lines' own drawing tool runs. Stricter, but Revit's Enter
     ("repeat last command") restarts the plain Detail Line tool, whose lines would then silently
     stay ordinary.
7. **Draw Lines pressed in another view opens the session's view and starts drawing there**
   (my call), instead of today's "Go back to Site Plan to draw topo lines": the buttons now show in
   every view, so that dead end would be met more often.
8. **In a new project, `_DP-Topo Line` and the `_DP-Topo Height` text get their own colour**, the
   toolkit's orange, RGB 255, 140, 0 (my call), so a converted line stands out at once. A style or
   type that already exists is left as it is. The colour stays after Accept, so a colour print or
   PDF of that view shows the lines and heights orange too; another colour, set once in Line Styles
   and in the text type (or in the office template), sticks.
9. **Floor ⇄ Topo skips the element a session is open on**, saying "a Topo Edit session is open on
   it; Accept or Cancel it first" (my call). Converting it deletes the element under the session.
   Found while tracing this design.
10. **Update's tooltip, and its summary when no line lies over the target, say "the toposolid or
    floor being edited"**, not "the selected element" (my call): the target is fixed for the
    session, and the panel now shows while other things are selected. The summary reads "No topo
    line in the session's view lies over the toposolid or floor being edited."
11. **Heights are always in metres and measured from the Survey Point, whatever the project's
    units** (yours).
    - "From the Survey Point" as Revit itself uses it (my call): a line labelled 42.75 lies where a
      level or a spot elevation set to Survey Point reads 42.75. Autodesk's documentation says this
      is the zero the tool already uses, so only the units change. Probe 9 checks it in Revit; if
      Revit's own readings follow the marker instead, the tool measures from the marker.
    - If the Survey Point marker itself sits off that zero (its own Elev is not 0, as after moving
      it while unclipped), heights still follow the levels and spot elevations, not the marker, and
      the first Update of every session says so in one line: "The Survey Point marker itself sits
      at +10.00 m; heights are measured like a Survey Point level."
    - A plain number is metres in every project. Today it is read in the project's units: "42.75"
      in a centimetre project means 42.75 cm.
    - New texts are written the way a surveyor writes heights, with a point and at least two
      decimals: 42.70, 42.755, 0.00, -3.20. No "+" and never "±" (my call): the tool's 0 is the
      Survey Point's, not the building's ±0.00. A new line still takes the last typed height, and
      0.00 before anything is typed.
    - Typing also accepts a comma for the point, a trailing "m" and a leading "+" (my call).
      Anything else is not a height, and Update names it as today, now as "'4275 cm' is not a
      height in metres": another unit, a formula, a space or a second separator ("4 275",
      "1,234.5"), and "±", so a "±0.00" typed out of habit is caught (other relative heights, such
      as -0.15, cannot be told apart and are read from the Survey Point). A single comma or point
      is always the decimal point, so "1,050" reads 1.05 m: heights are typed without grouping. A
      height typed with the Hebrew keyboard still on ("42ץ75") is refused too.
    - A height outside −500 to +3000 m is refused the same way (my call). This is a coarse net: it
      catches a forgotten point only on sites above 30 m (4275 for 42.75), and texts typed in
      millimetres before this change above 3 m. On a site between −5 and +30 m, a forgotten point
      or a text typed in centimetres before this change (780 for 7.80) is read as metres and
      applied without warning; retyping it and pressing Update puts the surface back. Texts typed
      in project units are not converted (my call: only test models have them).
    - The tool never rewrites a typed text.

## Not possible (looked into on 2026-11-16 and 17)

- **The Options Bar.** Removed in Revit 2027 (Autodesk's What's New for 2027; a KB article of
  2026-08-24; the 2027 binaries on this PC agree). In 2024–2026 it is a native window: add-ins that
  "use" it hide Revit's whole bar and draw their own, which would hide Chain, Offset and Radius
  while drawing lines. More fragile than the Modify tab, and for three of the four years only.
- **A locked mode like Edit Sketch.** The API's edit scopes (sketch, stairs) must close before the
  command returns; add-ins cannot run at all while Revit is in an edit mode, so our Accept and Cancel
  could not work inside one; `Document.IsInEditMode` (2025.2+) and `GetActiveEditMode` (2026+) only
  read. The only true lock is a loop of picks, which replaces Revit's Detail Line tool.

## Approaches considered

1. **Ribbon follows the session** — chosen (yours). The Modify tab needs the unsupported
   Autodesk.Windows ribbon, as the Topo Tools shortcut already does, with the same fail-safe.
2. **Supported only: Accept / Update / Cancel icons drawn in the view** (TemporaryGraphicsManager,
   all four years). Cannot break on an update, but pinned to a model point (gone when panned away),
   icons only with no tooltips before 2026, wiped by any add-in's `Clear()`, and never tried here.
3. The Options Bar, or a locked mode — not possible (above).

## How it is built

Checked on 2026-11-17 in Autodesk's API documentation and help, and in Revit's own code: AdWindows
and UIFramework decompiled for Revit 2024, 2026 and 2027 (2025 is not installed on this PC; the code
our panels go through on the Modify tab behaves the same in the three, and what changed after 2024
concerns only Revit's own panels), RevitAPIUI for all four years. The stack's layout was also
tried outside Revit with each year's own AdWindows.

**The supported base.** Topo Edit stays one real API panel on Add-Ins, hidden outside sessions,
with its panel and button names unchanged, so the buttons' command ids, and the names the tests
freeze, stay as they are: a stack adds no level to an id (read in RevitAPIUI, all four years).
Everything below may fail; this panel alone still gives a working session.
- Cancel and Accept become a two-button stack, added first so it sits at the left end, Cancel on
  top. The API builds a stack exactly as Revit builds its own Mode panel (a row panel: Cancel, a row
  break, Accept), only with small buttons that show their text. Autodesk.Windows then makes both
  Large with the text hidden, still horizontal: two 34 px rows, the height of one large button, so
  the ribbon does not grow. If that part fails, it is undone, and the stack shows small icons with
  "Cancel" and "Accept" at the same height.
- The API requires each button's text: "Cancel" and "Accept" stay, as the tooltip titles, and as
  their names in Keyboard Shortcuts if Revit lists them there (probe 7).
- The folded panel's icon is set on each panel object, through Autodesk.Windows.
- The ✗ and ✓ icons are swapped on `ThemeChanged` (supported, 2024 on). The swap reaches the
  Modify-tab copy too, since it shares the buttons.

**The Modify tab, through Autodesk.Windows.** Any failure logs one line and switches this part off
for the rest of the Revit session, leaving the Add-Ins panels, as `ContextualTopoButton` does today.
- Not the same panel object on both tabs: Autodesk.Windows lets one panel sit in two tabs, but it
  keeps a single owner, so the panel would take the other tab's colours, and leaving one tab clears
  its owner on the other. Instead **a second panel object shares the real panel's contents** (its
  `RibbonPanelSource`): the same buttons and the same Spacing box, so a click is a click on the real
  button. Whether Revit then ends a running tool first (✓ pressed while drawing ends the Detail Line
  tool and accepts) is for the probe.
- Every panel we put on the Modify tab is created as the same class Revit uses for its own panels.
  Revit's code walks that tab's panels assuming they are all of its class, and can throw when a
  plain Autodesk.Windows panel is the first one after the fixed panels and something is selected
  during an edit mode. **Today's Topo Tools shortcut panel is such a plain panel**, hidden but left
  on the tab: it gets the same fix, and our panels are removed from the tab, not just hidden,
  whenever they are not wanted. That class (UIFramework's `RvtRibbonPanel`) is made by reflection
  from its name and held as an Autodesk.Windows panel, so no UIFramework reference is added; if it
  cannot be made, nothing of ours goes on the Modify tab (never a plain panel), under the same
  fail-safe.
- Revit adds and removes only its own panels on the Modify tab, so ours stay put while the context
  changes; no timer is needed. The tab's panel list and properties are watched, to take ours off
  during edit modes and put them back afterwards. The changes are made after those notifications,
  never inside them (Autodesk.Windows throws on a list changed from its own notification), and never
  call the Revit API.
- Fallback, if clicks on the shared buttons misbehave: no Topo Edit panel on the Modify tab, and the
  switch to Add-Ins still happens. Not copies that post the real buttons' commands, as first
  planned: Revit fills its list of commands that can be posted (the Keyboard Shortcuts list) when it
  starts, skipping hidden panels, and Topo Edit is hidden then, so posting its commands would most
  likely do nothing (read in Revit's code and journals, not yet seen in Revit; probe 7 checks it).
  In this fallback the Topo Tools shortcut stays during a session and opens item 4's dialog.

**The switch to Add-Ins** makes the Add-Ins tab active through Autodesk.Windows once the start
command has returned, and reads back whether it took (a hidden tab silently refuses). Revit does not
switch back when a Modify | … tab closes while another tab is active, and remembers the active tab
when it next opens one itself: that is what makes Esc land on Add-Ins.

**All the rest is supported API.**
- The dialog's Accept and Cancel run the buttons' own code, called directly rather than posted.
- The save prompt hangs on `DocumentSaving`, `DocumentSavingAs` and
  `DocumentSynchronizingWithCentral`. In all four years their handlers may change the document, and
  may cancel when `Cancellable`; a `TaskDialog` with `VerificationText` gives the "Don't ask again"
  box. The handler works on the event's document, changes nothing in a read-only one, and never
  switches views.
- The session ends on `DocumentClosed` for its document, closed for real, instead of
  `DocumentClosing`, which may not change the document and comes too early. The last typed height
  is forgotten there too. `DocumentClosed` carries no document and is raised for a close called
  off as well, so `DocumentClosing` notes the closing document's id, and `DocumentClosed` ends the
  session and drops the height only when the id matches and the status is Succeeded.
- Arming is a flag on the session: set by Start and Draw Lines, cleared by a successful
  `ViewActivated` to another view of the session's document. The updater checks it only for lines
  not yet on `_DP-Topo Line`.
- "Go to it", and Draw Lines from another view, set the active view inside the command (allowed
  there), then select the element or start Detail Line.
- Colours are set only on `_DP-Topo Line` and `_DP-Topo Height`, by Topo Tools' own calls that
  create them (Auto Dimensions' `_DP-Dim` styles, made by the same `LineStyleCreator`, keep the
  default), and never on existing ones.
- **Heights:** one small formatter and parser in Core, in metres, with unit tests on all three
  frameworks, replace Revit's unit handling (`TopoHeightParser`, `TopoLengthFormatter`). Revit's
  parser reads the project's units, follows rules Autodesk does not document (comma or point, "+",
  formulas) and would quietly accept "4275 cm"; Revit's formatter cannot write both 42.70 and
  42.755. Metres to feet by 0.3048. Invisible direction marks that Hebrew typing can leave in a
  text are dropped before reading. The step from Survey Point heights to Revit's internal heights
  stays as it is (`ActiveProjectLocation.GetProjectPosition(XYZ.Zero).Elevation`, read again at
  every Update); the marker's own height is `BasePoint.GetSurveyPoint(doc).SharedPosition.Z`. The
  new Update sentences (not a height in metres, the marker, item 10's) go in `TopoSessionReport`,
  whose wording tests change with them.
- Floor ⇄ Topo's Modify-tab button, and the selection test that shows it, are removed from
  `ContextualTopoButton`. The Topo Tools shortcut is brought up to date when a session ends as well
  as on selection, so it comes back after ✓ or ✗. Item 9 is one more check in
  `ConvertRunner.RefusalOf`, not in `SlabRefusals.Common`, which Update runs on its own target.

## The probe — first step of the plan; needs one sitting in Revit 2024 by Daniel

A throwaway diagnostic build, not shipped. Its results go into this spec. If Revit 2026 and 2027 are
at hand, items 2, 4 and 9 are repeated there with the same build.

1. The second panel on the Modify tab: does it show on both tabs in the right colours, do its clicks
   work, does ✓ clicked while drawing end the tool and accept, is there one Spacing box for both?
2. Our panels through Revit's changes (select, start Detail Line, Esc): do they stay, and where?
3. Add-Ins made active at session start: after a selection and Esc, does Add-Ins show?
4. An edit mode with our panels on the Modify tab (Edit Boundary of a floor, then select a sketch
   line): no error, also with today's shortcut panel. How does an edit mode show on the tab, so ours
   can leave?
5. The ✗/✓ stack, on Add-Ins and on the Modify-tab copy: 32 px icons, no text, at the left end;
   tooltips; clicks; the panels' heights before and during a session (the panels' own heights: the
   ribbon control keeps its height even when panels grow); the folded panel's icon; a switch
   between the light and dark themes; the fail-safe look. At the office's usual window width and
   display scaling, on Modify | Place Detail Lines and Modify | Toposolid: is the copy whole or
   folded? With "Minimize to Panel Buttons", does one click open ✗ over ✓?
6. Does clicking into another tiled view raise `ViewActivated`?
7. Revit's list of commands for keyboard shortcuts and posting: are Topo Edit's in it, although the
   panel is hidden when Revit starts? Today's Topo Tools shortcut posts only the RVTuk panel's
   command, which is in it.
8. A close during a session: answer Yes at "Save changes?", then Esc at a test prompt hung on
   `DocumentSaving`; on a workshared model, the same after choosing Synchronize. Does the model stay
   open, with its session?
9. The heights' zero, with today's add-in (heights typed in the project's units, as today). A model
   whose ±0.00 sits at survey 42.75 (Specify Coordinates at Point, Survey Point clipped): a line at
   42.75 m lands at ±0.00 and agrees with a Survey Point spot elevation, a Survey Point level and
   the toposolid's own point readout (Elevation Base = Survey Point). Then the marker raised 10 m
   while unclipped: the level, spot elevation and readout still read 42.75, and the marker's Elev
   shows 10 m. This has never been run with the two zeros apart, and a wrong sign would show only
   here. If Revit's readings follow the marker, the tool measures from the marker and drops the
   marker line.

## Checks in Revit after the build

New rows in the README checklist, 2024 first, then 2025–2027: each item of "What the user sees"
(including "Go to it" leaving the element selected, Draw Lines from another view drawing in the
session's view, and a close called off keeping the session), the fail-safe (the Add-Ins panel keeps
working when the Modify-tab part is off), and anything the probe left open. For the heights:

- In a centimetre, a millimetre and a feet project, "42.75" puts points at 42.75 m.
- Probe 9's model with the new build, on a toposolid and on a floor: a line labelled 42.75 lands at
  ±0.00; with the marker raised, Update moves nothing, and its summary's figure equals the marker's
  Elev in Properties.
- On an office PC as the office sets it up: a "-3.20" the tool writes, and one typed with the
  Hebrew keyboard on (the point from the number pad), both show -3.20, not 3.20-, and Update reads
  the typed one. If Revit shows 3.20-, new negative texts get a leading invisible left-to-right
  mark, which the parser already drops.
- "42,75", "+42.75" and "42.75 m" are read; "4275", "4275 cm", "±0.00" and "42ץ75" are refused and
  named.
- A close during a session, Yes at "Save changes?", then Esc at the Topo Edit prompt: as probe 8
  found. A close called off at "Save changes?" keeps the session, and the next line still takes the
  last typed height.

## Docs

- **CLAUDE.md:** the "One exception" sentence covers the Topo Edit panel during a session and the
  switch to Add-Ins, and drops Floor ⇄ Topo from the Modify tab; the Topo Tools line says metres
  and lists the buttons in their new order (✗ and ✓ first).
- **README:** "Why the Modify tab needs AdWindows", as in CLAUDE.md. The workflow (steps 1–4, "A
  new line's height"), the paragraph on the updater (arming; Draw Lines from another view) and the
  "Names" line (the shortcut posts only Topo Tools; the Modify-tab copy's name). "Heights are shared
  (survey) elevations" says metres and Survey Point; names what moves a stored height (a clipped
  Survey Point move, Specify Coordinates, Acquire or Publish, Relocate Project, Reset Shared
  Coordinates, a site switch, a Forma connection or 2027 geolocation) and what does not (dragging
  the unclipped marker); and says that where a project's coordinates were never set, survey 0 is
  the internal origin (usually ±0.00), so survey heights need Specify Coordinates first. The
  Floor ⇄ Topo "Refused" list gains the open session, and the Docs list this spec. Checklist rows 2
  (the dialog), 3 ("type 42.75; the texts read 0.00 and 42.75"), 18 and 28 (no Floor ⇄ Topo on the
  Modify tab).
- **The 2026-11-15 spec** gets an "Amended 2026-11-17" line pointing here, and its "0 means the
  survey point's elevation" becomes "0 is the zero of Survey Point levels".
- **The backlog** is updated, with two new ideas: warn when a line's height is far from the
  element's surface (it would catch what the −500 to +3000 m net misses); and, if probe 7 confirms
  that the Topo Edit buttons cannot take keyboard shortcuts, hide the panel only after Revit has
  listed its commands.

## Left to the builder

How the Autodesk.Windows panels and tabs are found (by Id, never by the localised title); how an edit
mode is recognised on the tab (from the probe: from the tab itself through Autodesk.Windows, never
from UIFramework's contextual-tab events, one of which is gone after 2024); the dialogs' exact wording (short and plain, like the
toolkit's others); the icons' drawing in both themes; the Core height class's name; and whether
any other decision logic (arming, the dialogs' outcomes) is worth moving to Core for unit tests.

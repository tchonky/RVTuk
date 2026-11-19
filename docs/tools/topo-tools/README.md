# Topo Tools

**What it is:** an edit session on one toposolid or floor. Select the element in a plan view, press
**Topo Tools**, draw detail lines on the dedicated "_DP-Topo Line" style, type each line's height
(a survey elevation, in metres) into the text on the line, and the tool puts points at that height
along each line on the element. Re-runnable: move a line, change its number or delete it, run
again, and the result is what you would have got by drawing it that way to begin with. There is no
pane.

## The workflow

1. **Select** one toposolid or one floor in a plan view. The Topo Tools button is on the RVTuk panel
   (Add-Ins tab). It is also on the Modify tab, alone, while exactly one toposolid (not a
   subdivision) is selected. A floor starts its session from the RVTuk panel. Floor ⇄ Topo is on
   the RVTuk panel only.
2. **Press Topo Tools.** A temporary **Topo Edit** panel appears on the Add-Ins tab, and the ribbon
   switches to that tab once. At the panel's left end **Cancel** (✗) sits above **Accept** (✓),
   icons only (the tooltips name them). Then come **Draw Lines**, **Height Labels**, **Update** and
   a **Spacing** box. During the session the same panel also shows on the Modify tab, whatever is
   selected (see "Why the Modify tab needs AdWindows"). Pressing Topo Tools again during a session
   asks: "Topo Edit is open on Toposolid 123 in Site Plan." **Go to it** opens that view and
   selects that element. **Accept it** does what ✓ does, **Cancel it** what ✗ does; then, if one
   other toposolid or floor is selected, a session starts on it. Closing the dialog changes
   nothing. A start with no usable selection says "Select one toposolid or floor in a plan view,
   then press Topo Tools."; a toposolid subdivision, a floor with a slope arrow, an element in a
   model group, outside the edited design option or borrowed by another user is refused with its
   reason.
3. **Draw Lines** starts Revit's Detail Line tool in the session's view. Pressed in another view, it
   opens the session's view first. Every detail line drawn (or copied, or arrayed) in the session's
   view becomes a topo line and gets a `_DP-Topo Height` text at its middle, while the session is
   **armed**. A session starts armed. Opening another view disarms it, and only Draw Lines arms it
   again: coming back by the view's tab, a tiled window or "Go to it" does not. While it is
   disarmed, a new line in another style stays as drawn; a new line already on `_DP-Topo Line` (a
   copy, offset, mirror or paste of a topo line) still gets its text. Lines in other views are
   untouched.
4. **Edit the number** next to a line (double-click it). It is a height in metres, measured like a
   Survey Point level, in every project whatever its units (see "Heights are shared (survey)
   elevations" below).
5. **Update** applies all the lines to the element and stays in the session; **Accept** (✓) does
   the same and closes it; **Cancel** (✗) closes it and undoes what it can. Accept always shows a
   summary in a TaskDialog. Update shows it only when something needs attention or nothing was
   applied (see Update below). The summary has a warning icon when it names a problem. Accept
   leaves the session open if the update could not run at all, so you can fix it or Cancel.
6. Lines and texts **stay in the view** after Accept. Select the element again, press Topo Tools,
   and carry on.

**A new line's height** is the last height typed into any height text, and 0.00 until something
has been typed. It is kept in memory per document until the document has closed: a close called
off at "Save changes?" keeps it. A copied line keeps the text copied with it, not the last typed
height.

**Height Labels** is the tidy-up pass (Update runs it first): every topo line without a text gets
one, every text is moved back to its line's middle (by length along the curve, rotated along the
line, never upside-down), and a text whose line is gone is deleted. Update does the same, since it
runs this pass: deleting a line leaves its text until the next Height Labels or Update. A text it
cannot link to a line is left alone and counted, never deleted, because it may hold a typed height.
Hidden lines and hidden texts still count: both are found by owner view, not by what is visible.

**Update** reads each text and skips a line whose text is not a height (the summary names it, e.g.
"1 line skipped: '42,7x' is not a height in metres.", and that line's earlier points are removed).
It writes only to the toposolid or floor being edited. Points held by lines that no longer take
part (deleted, moved off the element, unreadable) are removed; other toposolids and floors are never
touched. The element's own outline corners take their heights from the lines too, and go back to
their own heights when no line influences them any more (see "Outline corners follow the lines"
below); the tool never deletes a point it did not create. A line partly over the element counts
only for the part over it, with a point on the outline at the line's height so neighbouring
toposolids meet at the same height; the drawn line is never split. One transaction per Update, so
one Ctrl+Z undoes it. Update shows no summary unless something needs attention (a skipped line, a
point or an outline corner moved by hand, a point not placed, a text linked to no line) or no line
was applied, since a silent Update would then hide why nothing changed; ✓ always shows it.
Refusals, a rolled-back Update and failures keep their messages.

**Where lines cross, the higher point wins.** Two points on the same plan spot (within 1 mm) — two
contours crossing, a line crossing itself, chained lines meeting on the outline — become one point
at the higher height; at equal heights the first line keeps it. This is decided before anything
reaches the element (`TopoPointDeduper`), so the rule is ours rather than whatever Revit does when
two points nearly coincide, and the ledger records one point where one point exists. The summary
(✓'s, or an Update's when it shows) says how many points took a higher line's height; a crossing
alone does not bring up Update's summary. Points near a crossing but not on the same spot all
stay, so a bank or a wall drawn as two lines comes out as a steep slope.

**Outline corners follow the lines** (since 2026-11-19; spec
[2026-11-19-outline-corner-heights-design.md](specs/2026-11-19-outline-corner-heights-design.md)).
After the line points, every Update and Accept gives each point of the outline that the tool did not
place — the corners of the outline and of its holes, and a point you put on the outline yourself —
the height the lines around it imply. It sits at the height of the nearest line, moved towards the
next line of another height it can see beyond itself, in proportion to the two distances. Where
lines of one height stop on both sides of a gap (a building, a cut-out, a path), they count as one
line drawn straight across. A corner within 1 mm of a line takes that line's height (the highest,
where several meet). A corner with no line beyond its nearest line keeps its own height, unless it
is within 5 cm of that line: then it takes the line's height, so two outlines sketched a few mm
apart do not open a crack. There is no reach limit: a corner 30 m from every line still moves if
lines lie on both sides of it. Every line of the session's view with a readable height counts, as
drawn, whether or not it crosses the element, so a corner shared with a neighbouring toposolid gets
the same height in the session of each; lines in other views do not count, and an unreadable height
on a line over no part of the element is left out silently. So an Update or Accept in another view
of the same element works out its corners from that view's lines alone: it can put back to their
own heights corners the first view's lines set (the first view's next Update raises them again).
The element keeps one corner record, whichever view wrote it last. Points inside the outline (a survey's,
or ones placed with Modify Sub Elements) and the points the ledger records for any line (at their
spot in plan, whatever their height) are never changed by the corner pass. The heights depend only on the lines, never on where the corners stand, so an Update run
twice changes nothing the second time.

- **Each corner remembers its own height,** the one it had before the tool first moved it, in the
  `RVTukTopoToolsOutlineHeights` record on the element. When no line influences it any more (its
  line deleted or moved away, or nothing left beyond its nearest line) it goes back to that height.
- **A line that ends on a corner** (within 1 mm) gives it the line's height the same way, and is
  remembered. No edge point is drawn there, so "points added" is lower than before by the number of
  line ends on corners, and a line whose only point on the element would sit on a corner reads
  "0 points added". Corners that earlier builds raised onto a line's end (raised, never recorded)
  are first recorded with the line's height as their own, so deleting that line later leaves them
  where they are.
- **A corner that cannot be brought within 0.1 mm of its height** counts among the points that
  "could not be placed", and is tried again, and warned about, at every Update.
- **Moved by hand.** A corner you move with Modify Sub Elements after the tool set it is left alone
  for good, and the next Update says so once. There is no way yet to hand such a corner back to the
  tool (backlog). A line's own point moved by hand is not frozen: the next Update in the line's view
  reports it once, "1 earlier point had been moved by hand and was left alone.", and leaves it where
  you put it; from the Update after, it follows its line again, as in earlier builds. On the
  outline, if the line still crosses there, it takes the line's height through the 1 mm snap and is
  recorded like any corner the lines set (so when the line goes, it goes back to where you had put
  it); if the line is gone, it is an ordinary outline corner. While the ledger records a line's
  point, the point is no corner, however far it was moved: an Update in another view leaves it
  alone.
- **Edit Boundary** drops the record of a corner it moved or removed, silently; a corner at a new
  spot starts afresh. **Reset Shape** on a toposolid is not a hand edit: the next Update starts the
  corners afresh, with no "moved by hand" for them (the line points' own "earlier points had been
  moved by hand" sentence still appears, since Reset Shape moved them too). The tool takes a corner
  for reset only when it stands within 0.1 mm of its recorded own height. A toposolid whose Reset
  Shape puts its corners anywhere else (one built from survey points, say) has them read as moved
  by hand, left alone for good (backlog). Changing the Height
  Offset of a shape-edited floor moves every corner off its recorded height, so the next Update
  takes them all for moved by hand (backlog); its line points are reported once and then follow
  their lines again.
- **Floors** get the same, with one difference: a plain floor (shape editing off) is switched to
  shape editing only when the lines move one of its corners by more than 0.1 mm, however far they
  are (there is no reach limit), so a floor none of whose corners takes a height from the lines
  stays a plain floor. That is judged before anything
  is switched on, from the floor's flat top (its level plus its height offset); on a plain floor
  whose top is not flat at that height the guess can be wrong (unchecked in Revit). A floor switched
  on for its corners alone, whose corners then did not move, is made plain again.
- **Arc lines:** the rule reads its own version of an arc, in chords whose ends lie on the arc and
  which stray at most 1 mm from it. A corner on the drawn arc is therefore within 1 mm of the rule's
  line and takes the line's height through the 1 mm snap. Only a corner up to 1 mm off the drawn
  arc can be up to 2 mm from the rule's line and miss the snap; it then takes practically the
  line's height, moved towards the next line by d / (d + D) of the step between them, d (at most
  2 mm) being its distance from the rule's line and D its distance to the next line.

**The summary.** Accept's summary names the corners that hold a line's height after the run,
those set by earlier Updates included: "3 outline corners took their heights from the lines." (for
one, "1 outline corner took its height from the lines."), and those put back: "2 outline corners
went back to their own heights." ("1 outline corner went back to its own height."). Neither needs
attention, so Update stays quiet about them. A corner found moved by hand does need it: "1 outline
corner moved by hand is left alone." ("2 outline corners moved by hand are left alone."), with the
warning icon. When no line was applied (none of the view's lines has a point for the element, a
point that falls on a corner included) and nothing needs attention (no line
skipped, no point or corner moved by hand, no point not placed, no unlinked text), the summary is
"No topo line in the session's view lies over the toposolid or floor being edited." followed by
the first two, since a corner shared with a neighbour moves even when every line lies over the
neighbour; Update shows it then too, because no line was applied. When something does need
attention, that opening sentence is left out and the corner sentences come before the problems.

**Where the rule is weak** (all found while testing the rule; a line drawn through or right next to
the corner settles each, since a corner within 1 mm of a line takes its height):
- Past the free end of a line that stops between a lower and a higher line, a corner pairs with
  whichever is nearer and can jump by half the step between them (0.57 m two metres past a stopped
  contour). Draw the line on to the corner.
- A retaining wall's top line drawn in the view pulls the corners along its foot up (45.65 instead
  of 41.40 beside a 5 m wall), and a corner sketched a few cm inside a wall-top line is read across
  the wall. Draw a short line at the foot's height beside those corners, or snap the outline to the
  line.
- Along the axis of a swale, a spur or a U opening onto a lower line, the height stays at the
  contour (40.50 where 40.30 is right).
- A neighbouring area's line passing beside a corner can beat the contour straight across.
- With no reach limit, far corners between distant areas move (42.50 over an existing 47.00 knoll).

**Cancel** works by snapshot, because no transaction can stay open across user interaction. In one
transaction it deletes the lines and texts the session created, removes those lines' points from
the element through the ledger, and puts back the text of every height edited during the session.
Points that *surviving* lines got from an earlier Update stay: the surface keeps the heights from
the last Update for the lines that still exist, and the next Update reconciles them. In the same
transaction Cancel puts back the outline corners the session's Updates moved, to where they stood
when the session started: a corner an earlier session set goes back to that session's height, and
one this session set first goes back to its own height. It works from the corner record taken at
the start, never from the lines, so it cannot move a corner the session left alone. A corner moved
by hand stays where it is: one moved before the session (flagged when the session starts), one an
Update of the session reported, and one moved after the session's last Update. That last one
stands more than 0.1 mm off the height that Update gave it, off its own height and off the height
Cancel would give it; whenever Cancel has corners to look at (their record changed during the
session), it flags that corner where it stands, so later Updates leave it alone without reporting
it; otherwise the next Update reports it, as after any hand move. Its message counts the corners
put back: "put back 1 changed height and 2 outline corners." ("put back 2 outline corners." when no
typed height was put back; no corner part when no corner moved). If putting the corners back fails,
Cancel still does everything else, leaves every corner as it was, and adds "Outline corners could
not be put back." Limits: Cancel puts back only corners still on the outline (one moved off it by
Edit Boundary during the session is not); corners it could not put back are not named, and the next
Update takes them from where they stand; after Reset Shape on a toposolid during the session,
Cancel raises the corners an earlier session set and this session's Updates moved back to that
earlier session's heights, and leaves every other corner where Reset Shape put it (one not put at
its recorded own height counts as moved by hand); a floor that was plain when the session started
gets its corners back at its top but stays shape-edited (backlog). Cancel deletes only what is still a
topo line or height text of the session's view, and the session ends only once
its transaction has committed, so a failed Cancel can be pressed again. The session also ends by
itself, keeping everything, once its document has closed (a close called off at "Save changes?"
keeps the session), or when a view of another document is activated.

**Saving during a session asks first.** A Save, a Save As or a Synchronize with Central of the
session's document shows a prompt: "Topo Edit is still open on Toposolid 123", "Changes to its
lines and heights since the last Update are not on it yet." The choices are **Accept, then save**,
**Cancel the session, then save** and **Save as it is**, with a "Don't ask again until this Topo
Edit ends" box. Esc, Enter and closing the prompt save as it is. The save is never called off:
Esc at such a prompt did not stop Revit from closing (probe, Revit 2024, 2026-11-18). Saving
another document asks nothing. A model opened from a read-only file asks at Save As, not at Save,
which cannot write the file back.

**Spacing** is the distance between points along a line, in centimetres (default 100), rounded to
two decimals. Press Enter to keep a value; Update and Accept also read the box, so a value typed
without Enter is used and kept too (whether Revit hands over text not yet confirmed with Enter is
unverified: checklist row 26). It is remembered between Revit sessions.

## Why it is shaped this way

**The height is a text note, not a parameter — forced, not chosen.** The design called for a
parameter on the line. `OST_Lines` has `Category.AllowsBoundParameters == false`, so
`BindingMap.Insert` refuses any *visible* parameter on it, and tags cannot tag detail lines. So
the height is a `TextNote` on the `_DP-Topo Height` type, linked to its line by Extensible Storage
on the text (`RVTukTopoHeightTextLink`). When two texts claim one line (a line copied with its
text), the nearer keeps it and the other relinks to the nearest line without a text.

**An updater restyles the new lines, because the Detail Line tool cannot be preset.** An `IUpdater`
(`TopoSessionUpdater`), enabled only during a session, gives each new detail line in the session's
view the `_DP-Topo Line` style and a text, and remembers heights as they are typed. A line in
another style is converted only while the session is armed (`TopoSession.Armed`): Start and Draw
Lines arm it, and a successful `ViewActivated` to another view of the session's document disarms
it. A new line already on `_DP-Topo Line` gets its text either way. Arming lasts until another view
opens, not only while Draw Lines' own tool runs: Revit's Enter ("repeat last command") restarts the
plain Detail Line tool, whose lines would then silently stay ordinary. Draw Lines pressed in another
view sets the active view to the session's view inside the command (allowed there), then posts
Detail Line, so the tool starts in the session's view. The updater is registered **optional**, so
projects it touched open without a missing-updater warning on PCs without RVTuk. Lines inside
groups and sketch lines (region boundaries) are skipped.

**Heights are shared (survey) elevations, in metres,** absolute as a surveyor quotes them. They
mean the same in every project, whatever its units. They are measured from the Survey Point as
Revit itself means it: a line labelled 42.75 lies where a level or a spot elevation set to Survey
Point reads 42.75. The conversion is one subtraction — `ProjectPosition.Elevation` at the
internal origin, read again at every Update — and metres to feet by 0.3048. So the tool depends
on the document's shared coordinates. A clipped Survey Point move, Specify Coordinates, Acquire
or Publish Coordinates, Relocate Project, Reset Shared Coordinates, a switch to another site, a
Forma connection or (in 2027) geolocation each change what a stored height resolves to; dragging
the unclipped Survey Point marker does not. For site work that is the correct dependency, and a
re-run corrects the model. Where a project's coordinates were never set, survey 0 is the internal
origin (usually ±0.00), so survey heights need Specify Coordinates first. If the marker itself
sits off that zero (its own Elev is not 0, as after moving it unclipped), heights still follow
the levels and spot elevations, and the first summary shown in a session says so in one line:
"The Survey Point marker itself sits at +10.00 m; heights are measured like a Survey Point level."
That the tool agrees with Revit's own Survey Point readings has not yet been seen with the two
zeros apart (checklist rows 73 and 74); if Revit's readings follow the marker, the tool will
measure from the marker instead. `AddPoints` takes an absolute Z on a toposolid (probed in Revit
2024, 2026-11-16); on a floor that is still unverified (checklist below).

**Typing a height.** The tool writes heights as a surveyor does, with a point and two to six
decimals: 42.70, 42.755, 0.00, -3.20. There is no "+" and never "±": the tool's 0 is the Survey
Point's, not the building's ±0.00. Typing also accepts a comma for the point ("42,75"), a trailing
"m" and a leading "+"; invisible direction marks that Hebrew typing can leave around a number are
dropped. A single comma or point is always the decimal point, so "1,050" reads 1.05 m: type heights
without grouping. Anything else is not a height, and Update names it ("1 line skipped: '4275 cm'
is not a height in metres."): another unit, a formula, a space or a second separator, "±" (so a
"±0.00" typed out of habit is caught), or a height typed with the Hebrew keyboard still on
("42ץ75"). A height outside −500 to +3000 m is refused the same way. That net is coarse: it catches
a forgotten point only on sites above 30 m (4275 for 42.75). The tool never rewrites a typed text.

**Retype heights typed before the switch to metres (2026-11-17).** Until then a text was read in
the project's units, and such texts are not converted. Each is now read as metres, or refused when
it falls outside −500 to +3000. So "42750" typed in millimetres is refused and named, but "780"
typed in centimetres for 7.80 m is now read as 780 m without a warning. Retype each old height in
metres, then press Update to put the surface back.

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

**The outline corners have a record of their own.** A corner belongs to no line, so it is not in the
ledger: a second schema on the element, `RVTukTopoToolsOutlineHeights`, holds for each corner the
tool moved its plan position, its own height, the height the tool last gave it, and whether it was
moved by hand. A separate schema rather than a key inside the ledger text, because an older build
rewrites the ledger and would erase it. Floor ⇄ Topo copies every KnafoKlimor entity, so it carries
this one too. Corner heights come from a rule over the lines, not from a triangulation of their
points like the one Revit builds: a corner just past a line's end falls outside every triangle,
which is the fold this pass removes.

**An edge point is raised relative to the unshaped top.** Probed in Revit 2024: `DrawPoint` at the
surface Z works, but `ModifySubElement` measures its offset from the element's unshaped top, not
from the vertex's current height. So an edge point's offset is target Z minus that reference, which
is read back after the first raise and corrected once if needed. Outline corners are raised the same
way, sharing that reference; whether `ModifySubElement` measures a corner from the same top is
unchecked in Revit (checklist row 107).

**The styles create themselves.** `_DP-Topo Line` and the `_DP-Topo Height` text type (a duplicate
of the project's default text type) are made when a session starts, if missing, in the toolkit's
orange (RGB 255, 140, 0), so a converted line stands out at once. A style or type that already
exists is left as it is, colour included. The orange stays after Accept, so a colour print shows it
too; another colour, set once in Line Styles and in the text type (or in the office template),
sticks. Auto Dimensions' `_DP-Dim` styles keep Revit's default. The two Topo Tools ones are found
by name only, so renaming either disconnects the tool from every line or text already drawn. Rename
the text type and the next session gives every line a new text at the last typed height, which the
next Update applies instead of the heights typed before; rename the line style and Update ignores
the old lines, leaving their points on the element unreported. Don't rename them.

### Why the Modify tab needs AdWindows

Revit's own **Modify | Toposolid** tab cannot be reached by the supported API. The probe (Revit
2024, 2026-11-16) showed it is the single shared contextual tab with Id `Modify`, whose title and
panels Revit swaps for each selection, so there is no tab of its own to find by name — and the
title is localised, so it is never matched. Our panels go on that tab through the unsupported
`Autodesk.Windows` ribbon (AdWindows, compile-only; nothing is copied to the output). The shortcut
was decided on 2026-11-16, reversing "don't retry" for this one tab, because another add-in in the
office does the same and "select first, then press" fits it. The session's panel followed on
2026-11-17, because people looked for the Topo Edit buttons on Modify | Toposolid.

- **Outside a session: the Topo Tools shortcut.** A panel holding the Topo Tools button alone shows
  while exactly one toposolid that is not a subdivision is selected. A floor, a wall, a door, a
  subdivision or two toposolids show nothing (checklist row 18). The selection counts only while
  the Modify tab is contextual, so the shortcut never shows on the plain Modify tab. It posts the
  RVTuk panel's Topo Tools command rather than running anything itself. Floor ⇄ Topo is no longer
  on the Modify tab (since 2026-11-17); it stays on the RVTuk panel.
- **During a session: a copy of the Topo Edit panel.** A second panel object shows the Add-Ins
  panel's contents (its `RibbonPanelSource`): the same buttons and the same Spacing box, so a click
  on the copy is a click on the real button. It shows whatever the Modify tab says:
  Modify | Toposolid, Modify | Place Detail Lines while drawing, Modify | Text Notes, the plain
  Modify tab. The shortcut leaves the tab meanwhile. The copy goes on after the panels Revit has
  on the tab at that moment. Panels Revit adds later (while drawing, after a new selection) land
  after it, so it then sits right after Revit's fixed panels. Folded on a narrow screen, it shows
  the Topo Tools icon rather than the ✗, and one click opens it. It is made at the first session
  start and kept for the Revit session.
- **Revit's own panel class.** Every panel we put on the Modify tab is made as UIFramework's
  `RvtRibbonPanel`, by reflection (there is no UIFramework reference). Revit's code walks that
  tab's panels as if all were of its class, and can throw on a plain Autodesk.Windows panel when
  something is selected during an edit mode. If that class cannot be made, nothing of ours goes
  on the Modify tab.
- **Edit modes.** While Revit is in an edit mode (Edit Boundary, any sketch, Edit Group), none of
  our panels is on the tab; the tab reads `IsContextualTab && IsInEditMode` then. Our panels are
  removed from the tab, not just hidden, whenever they are not wanted, and come back when the edit
  mode ends.
- **The switch to Add-Ins.** When a session starts, the Add-Ins tab is made the active one, once,
  at Revit's first idle moment after the start (when the probe saw it take). With Revit's default
  "return to the previous tab", a selection and Esc then land on Add-Ins. A tab picked afterwards
  is the user's.

It is **fail-safe**: any failure is caught and logged in one line in
`%LOCALAPPDATA%\RVTuk\crash.log`, never shown, and switches its part off for the rest of the Revit
session. A failure in finding the Modify tab, reading its edit mode or handling the shortcut takes
everything of ours off the Modify tab and also stops the switch to Add-Ins. A failure of the copy
or of the switch turns off only that one; while the copy is off, the shortcut stays during a
session and opens the Topo Tools dialog. The panels on the Add-Ins tab stay, so a Revit update
that breaks this costs a shortcut, not the tool.
To check the fail-safe, an empty file in `%LOCALAPPDATA%\RVTuk\`, looked for when Revit starts,
switches a part off for that Revit session: `topo-modify-tab-off` the whole part (row 69),
`topo-modify-copy-off` only the copy (row 70). Delete the file afterwards.

**Status:** registered on the RVTuk panel on the Add-Ins tab, gated by `RegisterTopoTools` in
`src/RVTuk.Revit/Application.cs` (on). The Massing & Site tab is impossible and verified so in
Revit 2024: the API places custom panels only on Add-Ins, on Analyze, or on a tab the add-in creates
itself. Revit 2024–2027 — `Toposolid` does not exist in 2023, so KKarea never ships it. **The
edit session was tried in Revit 2024 on 2026-11-16 and reported working**; the rows below are not
yet ticked one by one. The follow-the-session pass of 2026-11-17 is built but not yet checked in
Revit: its probe ran on a throwaway build (Revit 2024, 2026-11-18). The outline corner pass of
2026-11-19 is built, with Core tests on all three frameworks, but not yet checked in Revit (rows
86–108).

**Names:** code `TopoTools`; ribbon buttons "Topo Tools" and "Floor ⇄ Topo" (RVTuk panel, internal
names `TopoTools` and `FloorTopoConvert`); panel `Topo Edit` (`TopoCancel`, `TopoAccept`,
`TopoDrawLines`, `TopoHeightLabels`, `TopoUpdate`, `TopoSpacing`; Autodesk.Windows source Id
`CustomCtrl_%Add-Ins%Topo Edit`, command ids `CustomCtrl_%CustomCtrl_%Add-Ins%Topo Edit%` + the
button's name); Modify-tab shortcut panel `RVTuk_ContextualTopoTools` (AdWindows, posting only the
RVTuk panel's Topo Tools command); the Modify-tab copy of Topo Edit shares Topo Edit's source, so
it has the same source Id and the same buttons; line style `_DP-Topo Line`; text type
`_DP-Topo Height`; schemas `RVTukTopoHeightTextLink`, `RVTukTopoToolsPointLedger`,
`RVTukTopoToolsOutlineHeights` (GUID `cca5f35d-98c8-466c-9175-4d51afe00df1`, field `CornersByXy`); updater
`0aa14034-8c77-48a4-b5d6-dca41ed6da08`; fail-safe switch files `topo-modify-tab-off`,
`topo-modify-copy-off`.

**Keyboard shortcuts:** the Topo Edit buttons cannot be given keyboard shortcuts. Revit builds its
list of commands for Keyboard Shortcuts when it starts, and skips hidden panels; Topo Edit is
hidden then. Probe 7 (Revit 2024, 2026-11-18) found none of Topo Edit's buttons in that list. The
RVTuk panel's buttons, Topo Tools among them, are in it, so they can take a shortcut.

### In-Revit checklist: Topo Edit session

Tick each case only once it has actually been checked in that year's Revit: 2024 first (deploy with
`.\Deploy.ps1 2024` from an elevated shell, Revit closed), then 2025, 2026 and 2027 as Revit is at
hand. Rows 29–85 check the follow-the-session pass of 2026-11-17, and rows 2, 3, 4, 6, 9, 18, 21,
27 and 28 were reworded for it. Rows 86–109 check the outline corner pass of 2026-11-19, and row 24
was reworded for it. "Report it" marks a result the design did not expect: it goes back
to the design rather than being ticked. UIFramework 2025 could not be read for that pass, so in
2025 run at least rows 18, 62, 65, 66, 69 and 70.

| # | Case | Expected | 2024 | 2025 | 2026 | 2027 |
|---|------|----------|------|------|------|------|
| 1 | Nothing / a wall / two toposolids selected, press Topo Tools; then a subdivision, then a sloped floor | The start message for the first three; the subdivision and the sloped floor refused with their reasons | [ ] | [ ] | [ ] | [ ] |
| 2 | Start a session, then press Topo Tools again | The Topo Edit panel appears. The second press shows a dialog titled "Topo Tools": "Topo Edit is open on Toposolid N in {view}.", with Go to it, Accept it and Cancel it and their descriptions; ✓ and ✗ read clearly in them | [ ] | [ ] | [ ] | [ ] |
| 3 | Draw Lines, draw two lines; type 42.75 in one text; draw a third line | Lines are `_DP-Topo Line` with a text at their middle; the texts read 0.00 and 42.75, and the third line's reads 42.75 | [ ] | [ ] | [ ] | [ ] |
| 4 | Draw a line in another view with Revit's own Detail Line (opening that view disarms the session) | It is untouched | [ ] | [ ] | [ ] | [ ] |
| 5 | Edit a number, press Update; then Ctrl+Z | The surface moves; Ctrl+Z undoes the Update in one step | [ ] | [ ] | [ ] | [ ] |
| 6 | Press Draw Lines and draw a line half over the target | Points only on the inside part, one on the edge at the line's height; the neighbouring toposolid is untouched | [ ] | [ ] | [ ] | [ ] |
| 7 | Type "42,7x", press Update | The line is skipped and named in the summary; its earlier points are gone | [ ] | [ ] | [ ] | [ ] |
| 8 | Move a line off the target, press Update | Its points on the target are gone | [ ] | [ ] | [ ] | [ ] |
| 9 | Move a line, press Height Labels; delete a line, press Height Labels | The text re-centres; the deleted line's text goes | [ ] | [ ] | [ ] | [ ] |
| 10 | Copy a line together with its text | The copy's text moves to the copied line and keeps the copied number | [ ] | [ ] | [ ] | [ ] |
| 11 | Run the session on a floor | Works the same as a toposolid | [ ] | [ ] | [ ] | [ ] |
| 12 | Cancel | New lines and their points gone, edited numbers restored, panel hidden | [ ] | [ ] | [ ] | [ ] |
| 13 | Accept; select the element again, press Topo Tools | Panel hidden, lines and texts stay; the session carries on from them | [ ] | [ ] | [ ] | [ ] |
| 14 | Close the document mid-session; open it again, press Topo Tools | The panel hides on close; Topo Tools starts fresh | [ ] | [ ] | [ ] | [ ] |
| 15 | Spacing: type 50, press Enter, press Update; restart Revit | Denser points on that Update; 50 is still there after the restart | [ ] | [ ] | [ ] | [ ] |
| 16 | Floor ⇄ Topo button | Converts as the old ⇄ did | [ ] | [ ] | [ ] | [ ] |
| 17 | Save a project after a session; open it on a PC without RVTuk | No missing-updater warning | [ ] | [ ] | [ ] | [ ] |
| 18 | Select one toposolid; then, each straight after a toposolid, one floor, a wall, a door, a subdivision, two toposolids | One toposolid: Topo Tools alone on the Modify tab, with no Floor ⇄ Topo, doing exactly what its RVTuk-panel button does. Each of the others: nothing of ours on the Modify tab. Note where the shortcut sits when one toposolid is selected straight after another | [ ] | [ ] | [ ] | [ ] |
| 19 | Floor target: a flat floor at a non-zero level, Update with lines at known heights | The points land at the typed absolute Z (`AddPoints` Z is absolute on a floor) | [ ] | [ ] | [ ] | [ ] |
| 20 | Floor target: a floor with a slope arrow | Refused with its reason, at start and on Update | [ ] | [ ] | [ ] | [ ] |
| 21 | Two chained lines meeting on the outline; Update twice | Neither Update shows a summary: a "moved by hand" warning on the second would bring one up | [ ] | [ ] | [ ] | [ ] |
| 22 | During a session, draw a filled region and a masking region | Their boundaries keep their own style and get no height texts | [ ] | [ ] | [ ] | [ ] |
| 23 | Draw a line, Ctrl+Z it, place an ordinary text note in the view; then Cancel | The text note stays | [ ] | [ ] | [ ] | [ ] |
| 24 | Session on a plain floor with no line over it: first with no topo line in the view, then with lines on one side of it only, more than 5 cm from its corners; Update each time | The floor is not made shape-edited (Modify Sub Elements shows no points): a floor none of whose corners takes a height from the lines stays a plain floor | [ ] | [ ] | [ ] | [ ] |
| 25 | Make Revit roll an Update back (press Cancel in a Revit error dialog during Update) | "Nothing was applied — Revit rolled the change back."; the session stays open | [ ] | [ ] | [ ] | [ ] |
| 26 | Type 50 in Spacing **without** Enter, click Update; restart Revit | Denser points on that Update, and 50 after the restart. If not, Revit does not hand over text before Enter: say so in the tooltip and drop the read-back | [ ] | [ ] | [ ] | [ ] |
| 27 | Two lines at 42.75 and 43.00 sharing an end point inside the element; Update twice, then ✓ | One point there at 43.00. Neither Update shows a summary: a crossing alone needs no attention, and a "moved by hand" warning would bring one up. ✓'s summary says "1 point where lines cross took the higher line's height." | [ ] | [ ] | [ ] | [ ] |
| 28 | The Topo Edit buttons, Floor ⇄ Topo and the Modify-tab Topo Tools shortcut, in the light and the dark theme | Every button has a legible icon at both sizes. No Floor ⇄ Topo on the Modify tab | [ ] | [ ] | [ ] | [ ] |
| 29 | **The panel.** Start a session; look at Topo Edit on the Add-Ins tab; hover ✗, ✓ and Update | ✗ above ✓ at the panel's left end, 32 px icons with no text. Tooltips titled "Cancel" and "Accept"; Update's says "…to the toposolid or floor being edited, and stay in the session". The Add-Ins panels are no taller than before the session (two screenshots), and nothing on Topo Edit is cut off | [ ] | [ ] | [ ] | [ ] |
| 30 | During a session, switch Revit between the light and the dark theme | ✗ and ✓ take that theme's colours and stay legible, still icons only, on Add-Ins and on the Modify-tab copy | [ ] | [ ] | [ ] | [ ] |
| 31 | Narrow the Revit window until Topo Edit folds, on Add-Ins and on Modify \| Toposolid and Modify \| Place Detail Lines; then set the ribbon to "Minimize to Panel Buttons" | Folded, each shows the Topo Tools icon, not the ✗, and one click opens ✗ over ✓. Note the width at which the copy folds: folded at the office's usual width, it goes back to Daniel | [ ] | [ ] | [ ] | [ ] |
| 32 | ✓, then a second session in the same Revit run; ✗, then a third | Each time the ✗/✓ stack on Add-Ins is still icons only, and the copy comes back on the Modify tab with its folded icon | [ ] | [ ] | [ ] | [ ] |
| 33 | **The Topo Tools dialog.** During a session, press Topo Tools on the RVTuk panel and press Esc; again, and close the dialog; again, and press Enter | Esc and closing change nothing: the session and its panel stay. Enter does what Go to it does | [ ] | [ ] | [ ] | [ ] |
| 34 | From another view of the model, press Topo Tools, then Go to it; then draw with Revit's own Detail Line in the session's view | The session's view opens with the session's element selected, and the session stays open. The line stays ordinary: Go to it does not arm, only Draw Lines does | [ ] | [ ] | [ ] | [ ] |
| 35 | Select one other toposolid or floor, press Topo Tools, Accept it | Accept's summary shows, the session closes, and a new session starts on the other element | [ ] | [ ] | [ ] | [ ] |
| 36 | Accept it with the session's own element selected; then, in a new session, with nothing selected | The session closes; nothing new starts | [ ] | [ ] | [ ] | [ ] |
| 37 | Accept it when the update cannot run (a refusal as in row 20, or a roll-back as in row 25) | The session stays open; nothing new starts | [ ] | [ ] | [ ] | [ ] |
| 38 | Cancel it, with one other toposolid or floor selected; then, in a new session, with nothing selected | Cancel's summary shows and the session closes; then as rows 35 and 36 | [ ] | [ ] | [ ] | [ ] |
| 39 | From a 3D view, with another floor selected: Accept it; then, in a new session, Cancel it | Each is followed by "Select one toposolid or floor in a plan view, then press Topo Tools."; "Topo Update" stays in the Undo list, and Cancel's deletions stay | [ ] | [ ] | [ ] | [ ] |
| 40 | During a session in a plan view whose name mixes Hebrew and a digit, such as `קומה 1`, press Topo Tools | The dialog's sentence reads in order: the view's name whole, the full stop at the end | [ ] | [ ] | [ ] | [ ] |
| 41 | **Arming.** Open another view, come back to the session's view by its tab and draw with Revit's own Detail Line; copy a topo line without its text; press Draw Lines and draw | The plain line stays ordinary; the copied line gets a text; after Draw Lines, new lines are topo lines again | [ ] | [ ] | [ ] | [ ] |
| 42 | From another view of the model, press Draw Lines | The session's view opens and the Detail Line tool starts there; its lines are topo lines. If drawing does not start there, report it | [ ] | [ ] | [ ] | [ ] |
| 43 | Tile the session's view beside another view; click into the other, then back, and draw with Revit's own Detail Line | The line stays ordinary: clicking into a tiled view disarms the session, as opening a view does. If it becomes a topo line, report it | [ ] | [ ] | [ ] | [ ] |
| 44 | **Closing.** Type a height; close the model and answer Cancel at "Save changes?"; draw a line | No Topo Edit prompt. The model stays open with its session (Topo Edit on Add-Ins and on the Modify tab), and the new line takes the height typed before | [ ] | [ ] | [ ] | [ ] |
| 45 | Close the model during a session, answering No at "Save changes?"; open it again, start a session, draw a line | The session ended with the close (Topo Edit left Add-Ins and the Modify tab); the new line reads 0.00 | [ ] | [ ] | [ ] | [ ] |
| 46 | Two models open, the session on A: close B for real without switching to it (the × on its last open view's tab) | A's session stays as it was. If Revit makes the other model active first, the session ends there and no prompt shows: note it, not a bug | [ ] | [ ] | [ ] | [ ] |
| 47 | Two models open: in B select a wall; in A select one toposolid; switch to B. Then, with a toposolid selected, close its model | B's Modify \| Walls shows no Topo Tools shortcut, and after the close the plain Modify tab shows none. A shortcut left over is a bug to report | [ ] | [ ] | [ ] | [ ] |
| 48 | **Saving.** Ctrl+S during a session in a saved project | The "Topo Edit" prompt: "Topo Edit is still open on Toposolid N" and "Changes to its lines and heights since the last Update are not on it yet.", with "Accept, then save", "Cancel the session, then save", "Save as it is" and the box "Don't ask again until this Topo Edit ends". The view does not change | [ ] | [ ] | [ ] | [ ] |
| 49 | Choose "Accept, then save"; reopen the model | Accept's summary shows, the session ends (Topo Edit leaves Add-Ins and the Modify tab), and the model is saved. Reopened, the surface has the lines' heights | [ ] | [ ] | [ ] | [ ] |
| 50 | Choose "Cancel the session, then save" | Cancel's summary shows, the session ends, and the model is saved without the session's new lines and texts | [ ] | [ ] | [ ] | [ ] |
| 51 | Choose "Save as it is"; press Ctrl+S again | Saved, and the session stays open; the next Ctrl+S asks again | [ ] | [ ] | [ ] | [ ] |
| 52 | Tick the box and choose "Save as it is"; save again; then ✓, a new session, and save. Repeat with the box ticked and "Accept, then save" | With "Save as it is", the second save does not ask. Either way, the new session's first save asks again | [ ] | [ ] | [ ] | [ ] |
| 53 | At the prompt press Esc; at the next, close it; at the next, press Enter | Each saves as it is, and the session stays open | [ ] | [ ] | [ ] | [ ] |
| 54 | A new, never-saved project with a session: Save. Then a Save As of a saved model with a session | Both show the prompt (Revit's first save of a new project is a Save As) | [ ] | [ ] | [ ] | [ ] |
| 55 | Close the model during a session, answer Yes at "Save changes?", then press Esc at the Topo Edit prompt | The model is saved as it is and closes; the session ends. If no prompt shows during the close's save, report it | [ ] | [ ] | [ ] | [ ] |
| 56 | Type a height into one text; delete another height text in the session's view; close the model, answer Yes at "Save changes?", then choose "Accept, then save"; reopen | The line's new text reads the last typed height, not 0.00 | [ ] | [ ] | [ ] | [ ] |
| 57 | A workshared local model: Synchronize with Central during a session; then a close that answers "Synchronize with central" | The prompt shows once, not again for the local save; each choice works, and Esc syncs as it is. If it shows twice, report it | [ ] | [ ] | [ ] | [ ] |
| 58 | Two modified models, the session on one: close Revit and answer Yes at "Save changes?" for both | Only the session's model shows the prompt; saving the other asks nothing. If Revit makes the other model active first, the session ends there and no prompt shows: note it, not a bug | [ ] | [ ] | [ ] | [ ] |
| 59 | A model opened from a read-only file (read-only attribute, or in use and opened read-only), with a session: Save; then Save As | Save gives no prompt; Save As gives the prompt | [ ] | [ ] | [ ] | [ ] |
| 60 | **The Modify tab.** Edit a toposolid's sketch and select a sketch line; Finish, and select the toposolid again | No Revit error; the shortcut is back after Finish | [ ] | [ ] | [ ] | [ ] |
| 61 | One toposolid selected: press Topo Tools; then ✓ with it still selected | During the session the shortcut is off the Modify tab (the copy is there); after ✓ it comes back | [ ] | [ ] | [ ] | [ ] |
| 62 | During a session, look at Modify \| Toposolid, Modify \| Place Detail Lines (while drawing), Modify \| Text Notes, Modify \| Walls and the plain Modify tab | The copy is there, in the tab's colours. On the tab where the session started it sits after Revit's panels; after Draw Lines or a new selection, Revit's new panels come after it, so it sits right after Revit's fixed panels. Note where it sits | [ ] | [ ] | [ ] | [ ] |
| 63 | On the copy, type a spacing; then click its buttons (✗ and ✓ in separate sessions) | One Spacing box: the value shows in both. Each button acts as it does on Add-Ins | [ ] | [ ] | [ ] | [ ] |
| 64 | While drawing after Draw Lines, click ✓ on the copy | The Detail Line tool ends and the session is accepted. If not, report it | [ ] | [ ] | [ ] | [ ] |
| 65 | End a session by ✓; by ✗; by closing the model; by switching to another open model | Each time the copy leaves the Modify tab at once | [ ] | [ ] | [ ] | [ ] |
| 66 | During a session: Edit Boundary of another floor with a sketch line selected; a filled region's sketch; Edit Group with an element of the group selected. Finish some, cancel others | While each lasts, the copy is off the Modify tab and Revit shows no error. After Finish, and after Cancel, the copy is back, also with nothing selected | [ ] | [ ] | [ ] | [ ] |
| 67 | During a session, with nothing selected, start a new floor and press Cancel Edit Mode; open the Modify tab | The copy is on the plain Modify tab | [ ] | [ ] | [ ] | [ ] |
| 68 | Start a session from the RVTuk panel; another from the Modify-tab shortcut; then select something and press Esc | Each start moves the ribbon to Add-Ins once; after the selection and Esc, Revit lands on Add-Ins | [ ] | [ ] | [ ] | [ ] |
| 69 | **Fail-safe, whole part:** an empty `%LOCALAPPDATA%\RVTuk\topo-modify-tab-off`, Revit restarted | Nothing of ours on the Modify tab, and the ribbon does not move to Add-Ins at a start; a whole session (Topo Tools, Draw Lines, Update, Accept, Cancel) works from Add-Ins; crash.log has one line, `Topo Tools Modify-tab panels disabled: switched off by topo-modify-tab-off`. Delete the file afterwards | [ ] | [ ] | [ ] | [ ] |
| 70 | **Fail-safe, the copy:** an empty `%LOCALAPPDATA%\RVTuk\topo-modify-copy-off`, Revit restarted | The Add-Ins panel works. During a session the shortcut stays on the Modify tab while one toposolid is selected, and opens the Topo Tools dialog; the ribbon still moves to Add-Ins at the start. crash.log has `Topo Tools Modify-tab panels: the Topo Edit copy is switched off by topo-modify-copy-off for this Revit session.` Delete the file afterwards | [ ] | [ ] | [ ] | [ ] |
| 71 | **Heights.** In a centimetre, a millimetre and a feet project, a line labelled 42.75; Update | Points at 42.75 m in each | [ ] | [ ] | [ ] | [ ] |
| 72 | Type into the texts of separate lines "42ץ75" (into the first line drawn, so it is listed first), "42,75", "+42.75" and "42.75 m", and "4275", "4275 cm", "±0.00" and a second height typed with the Hebrew keyboard on, "43ץ10"; Update | "42,75", "+42.75" and "42.75 m" are read as 42.75 m. The summary names the other five as not heights in metres and lists them in order, "42ץ75" first, each inside its own quotes and reading as each text does in the drawing | [ ] | [ ] | [ ] | [ ] |
| 73 | Probe 9's model: Specify Coordinates at Point so that ±0.00 is survey 42.75 (Survey Point clipped). On a toposolid and on a floor, a line labelled 42.75; Update | The points land at ±0.00, where a Survey Point spot elevation, a Survey Point level and the element's point readout (Elevation Base = Survey Point) read 42.75 | [ ] | [ ] | [ ] | [ ] |
| 74 | The same model: unclip the Survey Point marker and raise it 10 m; start a new session; Update, then ✓ | Update moves nothing and shows nothing; the level, spot elevation and readout still read 42.75. ✓'s summary ends "The Survey Point marker itself sits at +10.00 m; heights are measured like a Survey Point level.", the same figure as the marker's Elev in Properties. If Revit's readings follow the marker instead, report it: the tool then measures from the marker | [ ] | [ ] | [ ] | [ ] |
| 75 | With the marker raised, a new session: type "42,7x" in one text, Update; fix it, Update; ✓ | The first Update shows the summary with the warning icon: "1 line skipped: '42,7x' is not a height in metres.", then the marker's line. The second Update shows nothing; ✓'s summary leaves the marker out | [ ] | [ ] | [ ] | [ ] |
| 76 | On an office PC as the office sets it up: type -3.20 in a text with the Hebrew keyboard on (the point from the number pad); draw a new line | Both texts show -3.20, not 3.20-, and Update reads the typed one. If Revit shows 3.20-, report it: new negative texts would then get a leading invisible left-to-right mark, which the parser already drops | [ ] | [ ] | [ ] | [ ] |
| 77 | **Update's summary.** Lines over the element, every height readable: Update; then ✓ | Update shows no dialog, and the surface changes (Ctrl+Z undoes it in one step). ✓ shows the summary ("Toposolid N: updated", "N lines applied: …") and closes the session | [ ] | [ ] | [ ] | [ ] |
| 78 | A session with no topo line over the element: Update. Then, after an applied Update, move every line off the element or delete them all: Update | Both times the summary shows "No topo line in the session's view lies over the toposolid or floor being edited."; the second time the earlier points come off | [ ] | [ ] | [ ] | [ ] |
| 79 | Delete a topo line that has points on the element, keeping its height text; Update | No "The update failed and was undone"; the text is deleted and the line's points come off; no new `Topo Update failed` line in crash.log | [ ] | [ ] | [ ] | [ ] |
| 80 | The same with Height Labels instead of Update | No error; the text is deleted (the points come off at the next Update); no new `Topo Height Labels failed` line in crash.log | [ ] | [ ] | [ ] | [ ] |
| 81 | **Styles.** In a new project, start a session and draw lines | Lines and texts are orange: `_DP-Topo Line` is RGB 255-140-000 in Line Styles, and so is the Color of `_DP-Topo Height` in its Type Properties | [ ] | [ ] | [ ] | [ ] |
| 82 | A project where `_DP-Topo Line` is blue and `_DP-Topo Height` is missing (then the other way round); start a session | The existing one stays blue; only the one Topo Tools creates is orange | [ ] | [ ] | [ ] | [ ] |
| 83 | In a new project, run Auto Dimensions | `_DP-Dim Outer`, `_DP-Dim Inner` and `_DP-Dim Ref` keep Revit's default colour | [ ] | [ ] | [ ] | [ ] |
| 84 | **Floor ⇄ Topo.** During a session on a floor, select it with another floor and press Floor ⇄ Topo | The summary skips the session's floor: "Floor N: a Topo Edit session is open on it; Accept or Cancel it first"; the other floor converts | [ ] | [ ] | [ ] | [ ] |
| 85 | After all the rows above | crash.log has no line with "keep their text", "keeps its first icon", "folds to its first icon", "Topo Edit copy is off", "did not switch", "switch to the Add-Ins tab is off" or "Modify-tab panels disabled", apart from row 69's "switched off by topo-modify-tab-off" | [ ] | [ ] | [ ] | [ ] |
| 86 | **Outline corners.** Daniel's toposolid of 2026-11-18 (lines at 41.00 and 43.00, three corners at 41.02): Update, then ✓ | The three corners read 41.20, 41.50 and 42.89 within 2 cm. Update shows no dialog; ✓'s summary includes "N outline corners took their heights from the lines." | [ ] | [ ] | [ ] | [ ] |
| 87 | Note the corner's height first (Modify Sub Elements, the point readout with Elevation Base = Survey Point). Then the view's only line drawn from inside the element to end exactly on that outline corner (snapped): Update; delete the line; Update | After the first Update the corner reads the line's height within 1 mm, and no dialog shows. The second shows "No topo line in the session's view lies over the toposolid or floor being edited. 1 outline corner went back to its own height.", the corner reads the noted height within 1 mm, and Modify Sub Elements shows the same points around it as before the line was drawn | [ ] | [ ] | [ ] | [ ] |
| 88 | Update twice with nothing changed, then ✓ | The second Update shows no dialog and moves nothing: every corner's height is as the first Update left it. ✓'s summary still names the corners ("N outline corners took their heights from the lines."), and nothing says "moved by hand" | [ ] | [ ] | [ ] | [ ] |
| 89 | After an Update that set a corner, move it up 0.5 m with Modify Sub Elements; Update; Update again; then draw a line ending on it, Update | The first Update shows the summary with the warning icon and "1 outline corner moved by hand is left alone." The corner stays where you put it through every later Update, which do not mention it again | [ ] | [ ] | [ ] | [ ] |
| 90 | Move one of a line's own points on the outline up with Modify Sub Elements; Update; Update again | The first Update shows "1 earlier point had been moved by hand and was left alone." and the point stays where you put it. The second shows no dialog and puts the point back at the line's height (Modify Sub Elements: within 1 mm of it) | [ ] | [ ] | [ ] | [ ] |
| 91 | Two toposolids sharing an outline corner, lines over only one of them: a session on each in turn, Update, ✓ | The shared corner gets the same height in both sessions, also in the one whose toposolid no line crosses ("No topo line … being edited. 1 outline corner took its height from the lines.", or more corners) | [ ] | [ ] | [ ] | [ ] |
| 92 | A toposolid with a cut-out, between lines of different heights; Update | The cut-out's corners take heights between the lines too | [ ] | [ ] | [ ] | [ ] |
| 93 | After an Update that set corners, Edit Boundary and move one corner; Update | The moved corner takes the lines' height at its new spot; nothing says "moved by hand" | [ ] | [ ] | [ ] | [ ] |
| 94 | Reset Shape on a toposolid sketched flat (not built from survey points) after an Update that set corners, then Update | The corners take the lines' heights again, with no "outline corner moved by hand". The line points' own "earlier points had been moved by hand" sentence still appears, since Reset Shape moved them too; on a second Update the line's points on the outline are back at the line's height, with no "moved by hand" | [ ] | [ ] | [ ] | [ ] |
| 95 | An arc topo line ending on, or passing through, an outline corner (snapped to it); Update | The corner is at the line's height (Modify Sub Elements: within 1 mm of it), since a corner on the arc takes the 1 mm snap | [ ] | [ ] | [ ] | [ ] |
| 96 | A toposolid whose corner the build before 2026-11-19 raised onto a line's end (Update with that build, then deploy this one): Update; delete the line; Update | The corner stays at the line's height after the delete, with no "moved by hand": it was recorded with that height as its own | [ ] | [ ] | [ ] | [ ] |
| 97 | A plain floor between two lines of different heights, no line over it; Update | Shape editing is switched on and the corners take heights between the lines; the summary shows ("No topo line … being edited. N outline corners took their heights from the lines.") | [ ] | [ ] | [ ] | [ ] |
| 98 | (Dropped in the final review of 2026-11-19: no setup could make it fail, since a floor switched on for nothing is made plain again. The dry run on a plain floor's real corners is covered by Core tests and rows 24 and 97.) | — | — | — | — | — |
| 99 | **Cancel and corners.** On an element whose corners no session has set: move a line between two Updates so a corner moves twice (no height typed); Cancel | The corner is back at its own height, and the element keeps no `RVTukTopoToolsOutlineHeights` entity (RevitLookup). Cancel's message includes "put back 1 outline corner." (more if more corners moved) | [ ] | [ ] | [ ] | [ ] |
| 100 | A corner an earlier session set to 41.20; in a new session type another height on its line so an Update moves it to 41.50; Cancel | The corner is back at 41.20. With that one corner moved, the message includes "put back 1 changed height and 1 outline corner." | [ ] | [ ] | [ ] | [ ] |
| 101 | A corner an earlier session set: in a new session delete the line that set it, Update (the corner goes back to its own height), then Cancel | The corner is back at the earlier session's height (the line stays deleted; the next Update puts the corner back again) | [ ] | [ ] | [ ] | [ ] |
| 102 | During a session, after an Update, move a corner with Modify Sub Elements; Update (it is reported); Cancel | The corner stays where you put it, and Cancel's message does not count it | [ ] | [ ] | [ ] | [ ] |
| 103 | Cancel a session whose Updates moved no corner | The message has no "outline corner" part | [ ] | [ ] | [ ] | [ ] |
| 104 | A plain floor whose corners a session's Update raised, then Cancel | The corners are back at the floor's top; the floor stays shape-edited (a known limit) | [ ] | [ ] | [ ] | [ ] |
| 105 | Reset Shape on a toposolid sketched flat (not built from survey points) whose corners an earlier session set, outside any session; then a session: Update, Cancel | The corners are back at the heights they had when the session started (their own), not at the older record's. On a toposolid built from survey points, Reset Shape may put corners off their own heights; then the Update reports them moved by hand and leaves them, and Cancel leaves them too | [ ] | [ ] | [ ] | [ ] |
| 106 | A toposolid whose corners an earlier session set: in a new session type another height on their line, Update (the corners move), Reset Shape on the toposolid, then Cancel | The corners go back up to the earlier session's heights, not where Reset Shape put them (a known limit, not a bug) | [ ] | [ ] | [ ] | [ ] |
| 107 | **Every year.** Rows 86, 87, 24 and 97 | Each passes as written. They check that `ModifySubElement` takes an outline corner of a toposolid and of a floor, measured from the same unshaped top as an edge point. Report it if a corner does not move at all (the summary then counts it among the points that "could not be placed"), or if every corner lands off its expected height by one same amount | [ ] | [ ] | [ ] | [ ] |
| 108 | A toposolid with a height offset other than 0, and a floor on a level above 0 with a height offset other than 0: on each, a line ending on an outline corner, at a known height; Update | The corner reads the line's height within 1 mm (point readout, Elevation Base = Survey Point), as the line's own points do (rows 19 and 73); the summary has no "could not be placed" | [ ] | [ ] | [ ] | [ ] |
| 109 | During a session, after an Update that set a corner, move it with Modify Sub Elements and Cancel with no Update between; once with a corner only this session set, once with one an earlier session set | The corner stays where you put it both times, and Cancel's message does not count it. A later session's Update leaves it there and does not report it | [ ] | [ ] | [ ] | [ ] |

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
  trip, the outline corner record (`RVTukTopoToolsOutlineHeights`), and the Room Floor link.
- **Pinned.**

**Lost:**
- A sloped floor's slope arrow. It comes back as a flat floor with raised corners and the same
  geometry.
- Join Geometry, and walls or columns attached to the floor (they detach).
- Per-view graphic overrides.
- Global parameter associations (the value is copied, the association is not).
- The span-direction symbol.
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
- the element a Topo Edit session is open on: "a Topo Edit session is open on it; Accept or Cancel
  it first" (converting would delete it from under the session; session checklist row 84);
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
| 6 | Accept a session (✓), round trip the toposolid, start a session on it and Accept again (Floor ⇄ Topo skips a session's own element, and only ✓ always shows the counts) | Same counts added and replaced; nothing reported "moved by hand" | [ ] | [ ] |
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
| 23 | The summary after any conversion | A TaskDialog lists what was converted, skipped and lost; no line ends in ".." | [ ] | [ ] |
| 24 | A flat toposolid (no points added) → floor | A plain floor: shape editing is off (no Modify Sub Elements points); a shape-edited toposolid still comes back shape-edited | [ ] | [ ] |
| 25 | A toposolid whose outline corners a session set → floor → toposolid; a session on it, Update, ✓ | The corners keep their heights both ways; ✓'s summary names them ("N outline corners took their heights from the lines.") and nothing says "moved by hand" | [ ] | [ ] |

## Code

| Layer | Folder |
|-------|--------|
| Core  | `src/RVTuk.Core/TopoTools/` (sampler, line clipper, containment, point deduper (crossings), height label placement, height-text linker, metre height text (`SurveyHeightText`), ledger codec, session report, outline corner rule, planner, pass and record codec (`OutlineCornerHeights`, `OutlineCornerPlanner`, `CornerPass` and `CornerPassGate` in OutlineCornerPass.cs, `OutlineCornerRecordCodec`), Modify-tab rule (`ModifyTabPanels`), `Sentence`, converter planner / chainer / parked-parameter codec / report) |
| UI    | none: the pane is gone, the session lives on the ribbon |
| Revit | `src/RVTuk.Revit/TopoTools/` (`TopoSession`, `TopoSessionUpdater`, `TopoSessionRunner`, `TopoEditPanel`, `ModifyTabHost`, `ModifyTabAccess`, `TopoDocumentEvents`, `TopoSaveGuard`, `TopoIcons`, `HeightTexts`, `HeightTextLinkStore`, `HeightTextType`, `LastTypedHeights`, line style and collector, applier, ledger store, corner store (`OutlineCornerStore`), the commands in `Commands/` (with `TopoSessionActions`: Accept and Cancel as plain calls), and the floor / toposolid converter) |
| Tests | `tests/RVTuk.Core.Tests/TopoTools/` |

`XyPoint` moved to `src/RVTuk.Core/Shared/Geometry/` when this tool was built — Auto Dimensions and
Topo Tools both use it, and the repo's rule is that a file lives in a tool folder only while one
tool needs it. `src/RVTuk.Revit/IsExternalInit.cs` arrived at the same time: this tool declares the
project's first records, and net48 has no such marker type in its BCL.

## Docs

- [specs/2026-11-19-outline-corner-heights-design.md](specs/2026-11-19-outline-corner-heights-design.md) — outline corners follow the lines: the rule, the corner record, Cancel (current design, with an "As built" note); its reference implementation [specs/2026-11-19-outline-corner-heights-reference.py](specs/2026-11-19-outline-corner-heights-reference.py)
- [plans/2026-11-19-outline-corner-heights.md](plans/2026-11-19-outline-corner-heights.md) — its implementation plan
- [specs/2026-11-17-topo-edit-follows-session-design.md](specs/2026-11-17-topo-edit-follows-session-design.md) — the Topo Edit panel follows the session onto the Modify tab; the session dialog, arming and the save prompt; heights in metres; the quiet Update (current design, with the probe results of 2026-11-18)
- [plans/2026-11-17-topo-edit-follows-session.md](plans/2026-11-17-topo-edit-follows-session.md) — its implementation plan
- [specs/2026-11-15-topo-edit-session-design.md](specs/2026-11-15-topo-edit-session-design.md) — the edit session (its base design, with the probe results of 2026-11-16; amended by the 2026-11-17 design)
- [plans/2026-11-15-topo-edit-session.md](plans/2026-11-15-topo-edit-session.md) — its implementation plan
- [specs/2026-07-29-topo-tools-design.md](specs/2026-07-29-topo-tools-design.md) — the original pane design; the sampling, routing, ledger and applier still stand, the pane and per-line storage are superseded
- [plans/2026-07-29-topo-tools.md](plans/2026-07-29-topo-tools.md) — its implementation plan
- [specs/2026-09-29-floor-toposolid-convert-design.md](specs/2026-09-29-floor-toposolid-convert-design.md) — Floor ⇄ Topo converter design
- [plans/2026-09-29-floor-toposolid-convert.md](plans/2026-09-29-floor-toposolid-convert.md) — its implementation plan
- [backlog.md](backlog.md) — bugs / improvements / ideas

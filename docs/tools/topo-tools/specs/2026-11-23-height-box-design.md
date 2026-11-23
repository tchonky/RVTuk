# Topo Edit: Height box — design

2026-11-23 · small change · branch `topo-tools`

## Why

Changing a line's height today means clicking into its `_DP-Topo Height` text note and editing
it. That works, but it is slow when many lines each need a different new number. Daniel wants a
faster way to type heights.

## Goals

- A **Height** box in the Topo Edit panel, next to Spacing, showing the height of the selected
  topo line(s); type a number, press Enter, and the selection takes it.
- Works on several lines at once (one undo step).

## Non-goals

- Relative entry (`+0.5`) and interpolation (`100..101.5`): later, if wanted.
- "Match height" picking, or any change to how labels are drawn or read.

## Anti-goals

- A second source of truth for heights. The text note stays the height; the box only writes
  text notes, so Cancel, the updater, Update and Accept keep working unchanged.

## Approaches

1. **Height box in the panel** — picked by Daniel: lines usually get *different* new numbers,
   so typing is the common case.
2. Match Height (pick a source line, then targets) — best when heights repeat; they usually don't.
3. Revit's Find/Replace on the selected labels — no code, but only swaps one identical text.

## Behaviour

- **What counts as selected:** topo lines of the session's view and their height texts (a text
  stands for its line, via `HeightTextLinkStore`). Anything else in the selection is ignored.
- **The box shows:** the height when all selected lines share one; empty when they differ or
  nothing topo-related is selected. Format and parsing are `SurveyHeightText` (metres, either
  decimal separator), as the labels use.
- **Enter:** in one transaction ("Topo height"), each selected line's text gets
  `SurveyHeightText.Format(value)`. A line without a text gets one (`HeightTexts.CreateFor`).
  The value becomes the last typed height, so the next drawn line starts at it — the updater
  already does this when it sees a text change.
- **Invalid text, or Enter with nothing selected:** the box goes back to what the selection
  shows; nothing changes.
- **Points:** Enter only changes labels, exactly like editing a label by hand; Update/Accept
  apply them. *(My call: keeps Enter instant on large toposolids. Easy to switch to
  "Enter also updates" if you prefer.)*
- **Cancel** puts the texts back already (`TextSnapshot`), so the box needs nothing for it.

## Constraints / how

- `TextBox.EnterPressed` runs in API context, so it can open the transaction directly — the
  same way Spacing reads its value.
- Refresh on `SelectionChanged` (the box has its own handler; `ModifyTabHost` keeps its own for
  the Modify tab), and on `DocumentChanged` in the session's document, so Undo and hand edits show.
- The Modify-tab copy of the panel shares the same ribbon items, Spacing included, so the
  Height box appears there too with no extra work — to be confirmed in Revit.

## Left to the builder

- Box width and prompt text ("Height (m)").
- Where the selection-reading code lives (a small helper next to `HeightTexts`).
- Unit tests for the parts in Core (parsing/format already tested); Revit behaviour checked by
  hand in 2024, then 2026/2027.

## Addendum 2026-11-23: moves

Daniel asked for relative entry after the box shipped. A signed number (`+0.5`, `-0.5`, `−0.5`)
moves each selected line from its own height; a plain number sets it, and `=` before a height sets
it too, which is how a height below zero is typed (`=-3.20`) — Daniel's choice over `+-0.5` or
`--0.5`. A line a move cannot change (no readable label, a result outside −500..3000 m) keeps its
height and a message counts them. Rule in Core: `HeightBoxInput`.

## Addendum 2026-11-23: Pick

Daniel's idea, replacing "fill between two heights": a **Pick** button after the Height box. It
does what the box holds to each topo line clicked (a height sets it, `+0.1`/`-0.1` moves it on
every click), one undo step per click, Esc ends; lines selected before pressing it get it first.
One button driven by the box, chosen over two fixed buttons (set height / raise 10 cm), so the step
can be any size and can lower. Text typed in the box and not yet applied now survives selection
changes, so it can be typed before selecting lines.

## Addendum 2026-11-23: the box keeps what you type; new layout

Tried in Revit by Daniel: the typed number vanished on Enter (the box went back to showing the
selection's height) and on Pick. Now what is typed stays in the box — after Enter, after Pick,
across selection changes — and the box shows the selection's height only while empty
(`HeightBoxText.Follows`). Typed text survives a click elsewhere through Autodesk.Windows
`AcceptTextOnLostFocus`. Layout, Daniel's: ✗/✓, a stack of Draw Lines / Pick Lines / Height (with a
"H=" label, widened to 140 px), then an Update split button (Heights, the default click, and Labels). The Spacing
box is gone: points every 100 cm.


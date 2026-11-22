# Area Calc — assign usage codes and show areas — design

> Status: design, awaiting approval (2026-11-22). Branch: `rishui-zamin`.
> Clickable mock (local, git-ignored): `.superpowers/mockups/area-calc-assign.html`.
> Builds on the window from [2026-07-05-area-submission-ui-redesign-design.md](2026-07-05-area-submission-ui-redesign-design.md).

## 1. Intent

Right before an export, the Areas tab shows a few red areas, usually ones with no usage code.
Today the user has to leave the window, find each area in Revit and pick its key in
Properties. Sometimes they also want to change the code of a few areas that are not red.
This change lets them do both from the Area Calc window, **through the same "New Usage" /
"Old Usage" key schedules** the office already uses, so the schedules, the area names and the
codes never disagree.

The same change shows the area measurements: m² per area, per-floor totals by group, and
sheet totals at the top of the window. That way the user can sanity-check the numbers before
the export.

## 2. Goals, non-goals, anti-goals

**Goals**
- Give one or several selected areas a New usage and/or an Old usage code from inside the
  window, in one undo step.
- Show why a selected area is red, in words.
- Show m² per area, per floor (by group plus a total) and for the sheet (by group plus a
  total).
- Make the four usage groups easy to tell apart by colour.

**Non-goals**
- Coding a whole fresh sheet quickly (multi-select covers it adequately; nothing is tuned
  for it).
- Editing Asset, permit area (`RZ_AREA`) or the area's number/name directly.
- Fixing boundary or zero-area errors. Those stay red and still need Revit.
- A "walk through the red areas" mode (considered, not chosen).

**Anti-goals (a failure even if it works)**
- Writing `RZ_USAGE_TYPE` / `RZ_USAGE_TYPE_OLD` directly and bypassing the key: the schedule,
  the area name and the code would drift apart.
- One click making several undo steps, or a half-applied change on failure.
- The window freezing Revit, or calling the Revit API off the main thread.

## 3. Constraints

- Layers: Core (pure logic) → UI (WPF, no Revit types) → Revit host. The UI gets the new
  Revit work as a plain delegate, like `extract` / `setupUsageKeys` today.
- Threading: Revit work goes through an `ExternalEvent` + `ManualResetEventSlim` ping-pong,
  called from a thread-pool thread by the view model (the window lives on Revit's main thread).
  See `RishuiZaminViewModel.Refresh`.
- **KKarea (Revit 2023)** hosts the same window and compiles the Area Calc handlers as linked
  source (`src/KKarea.Revit/KKarea.Revit.csproj`). Any new handler must be linked there too
  and must build against the 2023 API (`#if REVIT2023`: int-only `ElementId` ctor,
  `IntegerValue`).
- The key schedules are made by `UsageKeyScheduleBuilder`. The schedules are named "New Usage" and
  "Old Usage"; their key parameter on Areas is normally named the same, but Revit may refuse
  that name and auto-assign another (`KeyScheduleParameterName` is set in a try/catch). Key rows
  are named `"«code» - «Hebrew name»"` (read through `REF_TABLE_ELEM_NAME`). The New Usage key
  drives `RZ_USAGE_TYPE` and the area's Name; Old Usage drives only `RZ_USAGE_TYPE_OLD`.
- Old Usage never offers the process codes 300–302 (same rule as the Old Usage schedule).
- `AreaRecord.AreaValue` is already in m².
- Area names mix Hebrew and Latin. Every text output must be checked on mixed samples
  (left-to-right paragraphs, like WPF's default), never on Hebrew-only ones.

## 4. Approaches considered

| | Approach | Trade-off |
|---|---|---|
| A | Right-click a row → New usage ▸ / Old usage ▸ with the four groups as submenus | No space cost, familiar; but ~45 Hebrew entries in nested menus, no typing a code, current codes and the reason it is red are not visible |
| **B** | **Detail strip under the list for the selected area(s), with New / Old usage pickers** | Shows both codes, the reason it is red and the m²; costs ~70 px of height |
| C | Click the code pill on the row → picker in place; "←+" on hover for the old code | Fastest for red rows, no space; cramped, can't explain why a row is red |
| D | Multi-select (Ctrl/Shift) + one pick for all | Combines with any of the above |
| E | Suggest codes from the area's name | Combines with any; suggestions only |
| F | "Fix red areas" walk-through | Extra mode; not needed for "a few red areas" |

**Chosen: B + D + E** (Daniel, 2026-11-22, after clicking B and C side by side). Right-click
(A) may be added later as a shortcut into the same picker.

## 5. Design

### 5.1 Areas tab layout (top to bottom)

```
[⟳ Refresh] [⬆ Export]
┌PRIMARY─┐┌SERVICE─┐┌OTHER──┐┌PROCESS┐┌NO CODE┐┌TOTAL M²┐
│ 107.70 ││ 465.00 ││  0.00 ││ 12.50 ││108.60 ││ 572.70 │   ← sheet summary
└────────┘└────────┘└───────┘└───────┘└───────┘└────────┘
[ 6 of 12 areas flagged — check before exporting ]          ← unchanged banner
▾ קומת קרקע (4) ⚠                 22.00  12.50  84.30  Σ 22.00   ← floor totals
    001 — מבואות וחדרי מדרגות        22.00 m²  (105)
    002 — דירה 1                     84.30 m²  (⚠ no code)
    003 — הריסה ופירוק               12.50 m²  (301|←1)
──────────────────────────────────────────────────────────
002 — דירה 1                                     84.30 m²   ← detail strip
⚠ No usage code
New usage [ — none —        ▾]   Old usage [ — none —   ▾]
──────────────────────────────────────────────────────────
New usage 1 - מגורים on 2 areas · one undo step in Revit      ← status line
```

### 5.2 Usage groups and colours

Group = the area's **New usage** code's group, else its **Old usage** code's group, else
"No code". New theme brushes in `DarkTheme.xaml`:

| Group | Brush key | Colour |
|---|---|---|
| Primary | `Brush.UsagePrimary` | `#7CC67C` (green) |
| Service | `Brush.UsageService` | `#45AFAF` (dark cyan) |
| Other | `Brush.UsageOther` | `#A3A3A3` (grey) |
| Process | `Brush.UsageProcess` | `#E2C35C` (yellow) |

Used for: the code text in each row's pill (each half of `301|←1` coloured by its own code),
the group figures in floor headers and in the summary, and the group headings and entries in
the picker. A red row keeps its red name and red pill border; the code text keeps its group
colour.

### 5.3 Measurements and totals

- **Row:** m² with 2 decimals, muted, right-aligned in a fixed-width column just left of the
  code pill.
- **Floor header:** after the name, count and ⚠: the m² of each group present on the floor
  (in its colour, zero groups omitted), the "No code" m² in red if any, then **Σ floor total**.
- **Sheet summary** (under the toolbar): one box per group — always all four, a zero shown
  muted — then a red "No code" box **only while some area has no code**, then **Total m²**.
- **Total = Primary + Service** (Daniel, 2026-11-22). Other is outside the floor's area
  summary per the official usage table; Process codes mark demolition/excavation; uncoded
  areas have no group yet. Tooltip on the total: "Primary + Service".
- Totals recompute after every refresh and every assignment.
- The computation lives in Core (`RVTuk.Core.RishuiZamin`), pure and unit-tested:
  group of a record, per-group sums, total.

### 5.4 Selection

- Click selects one area; Ctrl-click toggles; Shift-click selects a range in list order.
- The selection is mirrored to Revit (all selected areas selected in the model), as single
  selection is today. `SelectAreaEventHandler` takes a list of ids.
- **Builder's call (suggested):** WPF `TreeView` has no multi-select. A `ListBox` with
  `SelectionMode="Extended"` over a `CollectionViewSource` grouped by level, with a `GroupStyle`
  using an `Expander` header that shows the floor totals, gives the same look with built-in
  multi-select. Any other control is fine if it behaves as above.

### 5.5 Detail strip

Docked under the list, always visible.
- **Nothing selected:** "Select one or more areas to set their usage." Pickers disabled.
- **One area:** "«number» — «name»", its m², and the reason it is red in words
  ("No usage code", "Boundary is not closed", "Area is zero", joined by " · ").
- **Several areas:** "N areas selected", the sum of their m², and "⚠ K without a usage code"
  when K > 0.
- **Pickers:** "New usage" and "Old usage". Each shows the shared value as "«code» - «name»"
  in the group colour, "— none —" when empty, or "(mixed)" (muted, italic) when the selected
  areas differ.

### 5.6 The picker

Opening a picker shows a popup (above the strip when there is no room below) with:
1. A search box, focused. Digits filter by code prefix ("11" → 11, 110–119); other text
   filters by substring of the Hebrew name; a group's English name ("serv") also matches.
2. When the search is empty: "— none — (clear)", then **Suggested from the name** (up to 4
   codes, from the selected areas' names), then the groups.
3. The four groups in table order, each with a coloured heading ("PRIMARY  שטחים עיקריים") and
   coloured entries (`code  name`). Old usage omits Process. The current value has a ✓.
- Keys: ↑/↓ move, Enter picks, Esc closes; clicking outside closes.
- **Suggestions** are a Core keyword table (unit-tested). Starting set (from the mock):
  מרפסת/balcony → 30, 110, 250 · חני/park → 33, 112 · מחסן/אחסנ/storage → 31, 115 ·
  ממ"ד/מרחב מוגן → 101 · מבוא/לובי/מדרגות/lobby/stair → 105 · מעלית/lift/elevator → 104 ·
  דירה/מגורים/apartment → 1 · טכני/משאבות/pump/technical → 108 · מרתף/basement → 111 ·
  מצלל/pergola → 32, 255 · בריכ/pool → 256 · משרד/office → 7. Suggestions only; never applied
  automatically.

### 5.7 Assigning (Revit side)

A new `SetUsageKeyEventHandler` (in `RVTuk.Revit/RishuiZamin/ExternalEvents/`, linked into
KKarea) receives: which key (New / Old), the code or none, and the area ids. On Revit's main
thread, in **one transaction** named e.g. "Area Calc — set New usage":
1. Find the key schedule by name ("New Usage" / "Old Usage", an Area key schedule) and its key
   parameter name from `KeyScheduleParameterName`.
2. Find the key row for the code: first the row whose driven code parameter
   (`RZ_USAGE_TYPE` / `RZ_USAGE_TYPE_OLD`) holds the code, else the row whose Key Name starts
   with `"«code»"` followed by a non-digit. This also finds rows in a schedule someone made or
   renamed by hand. (Setup Usage Keys keeps matching rows by exact Key Name; making it match the
   same way is a separate fix, not part of this change.)
3. On each area, set the key parameter to that row's `ElementId` (or `InvalidElementId` for
   none). For none, also blank the driven code parameter if Revit leaves its old value behind
   (to be checked in Revit, see §7) — the area's Name is left as it is.
4. On each area, blank the office template's matching parameters if the area has them and
   they are writable: for New, "Usage Type" and "Usage Type Name"; for Old, "Usage Type Prev"
   and "Usage Type Prev. Name". This happens on every set and every clear. Reason:
   `AreaExtractor.ReadCode` falls back to these when `RZ_*` is empty, so a cleared code would
   otherwise come back from them on the next refresh. The office no longer uses these
   parameters (Daniel, 2026-11-22: "you can just clear them"). The extractor's fallback stays
   as it is, so older projects that are never touched by the window still export.
5. Commit, or roll back everything on any failure and report it.

The view model calls it through a delegate
`Func<UsageKeySlot, int?, IReadOnlyList<long>, bool, UsageAssignResult>` (slot, code or none,
area ids, run setup first) on a thread-pool thread. `UsageAssignResult` carries one of
Done / KeysMissing / Failed plus a message, because the window must tell "the schedule or row
is missing" apart from a failure. The view model then **refreshes the whole sheet** and restores the selection by
element id (simplest way to show the new codes, names, errors and totals exactly as the export
will see them). Then the status line says what changed, e.g.
"New usage 110 - מרפסת on 2 areas · one undo step in Revit".

**When the key schedule or the key row is missing** (project never set up, or set up before a
catalog code was added): the handler returns that state without changing anything; the window
asks "This project has no "New Usage" key schedule yet. Run Setup Usage Keys now, then set the
code?" [Run setup] [Cancel]. Run setup calls the handler again with "run setup first": it runs
`UsageKeyScheduleBuilder`'s setup (which also tops up missing rows) and the assignment inside
one `TransactionGroup` that is assimilated, so it is **still one undo step**. The setup code is
reused; only its transaction becomes part of the group.

**KKarea wiring:** KKarea has its own `Application.cs` and `Commands/RishuiZaminCommand.cs`
(not linked from RVTuk). Linking the new handler into `KKarea.Revit.csproj` is not enough: its
`ExternalEvent` and the delegate must also be created in KKarea's application and command,
mirroring RVTuk's. The multi-id `SelectAreaEventHandler` change applies to both hosts.

**Renaming:** setting a New usage renames the area to the usage's Hebrew name — that is what
the key does today, and it is kept.

### 5.8 Errors

- A selected area deleted since the last refresh, a document that is read-only, or any API
  failure: nothing changes (rollback), a message box explains, and the sheet is refreshed.
- Assigning is disabled while a refresh or another assignment is running.

## 6. Testing

- **Core unit tests** (net48 / net8 / net10 via `Test-All.ps1`): group of a record (new, old-only,
  none), per-group and total sums (Total = P + S), suggestion table, search filter (code prefix,
  Hebrew substring, group name). Use mixed Hebrew/Latin names.
- **UI tests** where practical (view model): strip text for 0 / 1 / many areas, "(mixed)",
  Old usage list without Process.
- **In Revit (2024 and 2023/KKarea at least):** assign New and Old to one and to several areas;
  one Ctrl+Z undoes it; the area's Name follows the New usage; "none" clears the code and the
  area stays red after a refresh, also on an area that had "Usage Type" filled in;
  a project without key schedules offers setup; totals match a schedule's sums.

## 7. Left to the builder

- The WPF control for the grouped, multi-select list (§5.4) and the popup's exact placement.
- Whether setting a key parameter to none leaves `RZ_USAGE_TYPE` with its old value (check in
  Revit; if it does, blank it in the same transaction).
- Keeping the scroll position across the post-assignment refresh.
- **My calls, open to change:** 2-decimal m²; the four group boxes always shown, "No code" only
  when needed; Σ symbol for the floor total; refresh-everything after an assignment; the
  suggestion keyword table; finding the key row by its code as well as by its name (§5.7);
  Run setup + assign as one undo step (§5.7); keeping the "Usage Type" fallback for areas the
  window never touched (§5.7).

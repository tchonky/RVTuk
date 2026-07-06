# Project Comparator — UI/UX Spec

WPF design, consistent with the existing Family Browser. Text wireframes only (no images, no code). See the [design spec](../superpowers/specs/2026-06-23-project-comparator-design.md) and [features.md](features.md).

---

## 1. Visual language (reuse existing `DarkTheme.xaml`)

| Token | Hex | Role |
|-------|-----|------|
| `Brush.Bg` | `#1E1E1E` | window bg |
| `Brush.Panel` | `#252526` | panels / lists |
| `Brush.Control` | `#2D2D2D` | toolbars, headers |
| `Brush.Input` | `#3C3C3C` | inputs |
| `Brush.Hover` | `#3E3E42` | hover |
| `Brush.Selection` | `#264F78` | selected row |
| `Brush.Text` / `Brush.TextMuted` | `#D4D4D4` / `#858585` | text / secondary |
| `Brush.Accent` / `Brush.AccentDark` | `#FF8C00` / `#CC7000` | CTA, active tab, badges |
| `Brush.Success` / `Brush.Warning` | `#4EC94E` / `#FF6B35` | up-to-date / outdated |

**New diff-semantic colors (additive):**

| Token | bg / text | Meaning |
|-------|-----------|---------|
| `Brush.DiffAdded` | `#1A3A1A` / `#4EC94E` | only in A |
| `Brush.DiffRemoved` | `#3A1A1A` / `#FF6B35` | only in B |
| `Brush.DiffChanged` | `#2A2200` / `#FF8C00` | in both, differs |
| `Brush.DiffMatch` | transparent / `#858585` | identical (lowest weight) |

Status glyphs carry shape meaning too (not color-only): `≈` match, `△` changed, `+` only A, `−` only B. A one-row legend sits pinned above the roster.

**Density/typography:** match Family Browser — ~28px rows, ALLCAPS 10pt muted section headings, `Padding="8,4"` `CornerRadius="2"` buttons, 5px `GridSplitter`. Reuse `RelayCommand`/`RelayCommand<T>`, `ViewModelBase.SetProperty`, `BooleanToVisibilityConverter`, themed `DataGrid`/`ListBox`/`TabControl`/`ComboBox`, overlay search-hint pattern, `ToggleButton+Popup` dropdowns.

---

## 2. Entry point
Second button on the existing RVTuk ribbon panel: large, label "Comparator", tooltip "Project Comparator / Template Builder — compare two Revit projects or audit a project against the firm template". Opens a modeless window, `Topmost="True"`, centered, title "RVTuk — Project Comparator". Suggested size `1100×680`, min `800×520` (wider than Family Browser for side-by-side).

---

## 3. Main window shell

```
╔══════════════════════════════════════════════════════════════════════════════╗
║ RVTuk — Project Comparator                                    [─][□][✕]      ║
╠══════════════════════════════════════════════════════════════════════════════╣
║ SOURCE BAR  (Brush.Control, ~56px)                                           ║
║ Mode:[Build Template ▾]  A:[Project Alpha.rvt ▾]  ⇌  B:[The Standard ▾]    ║
║                                                  [Compare ▶]  [✕ Clear]      ║
╠════════════╦═════════════════════════════════════════════════════════════════╣
║ CATEGORIES ║ COMPARISON AREA                                                 ║
║ (~200px)   ║                                                                 ║
║ ▶View Tmpl ║   (roster ⟷ detail, see §5)                                    ║
║   14 △     ║                                                                 ║
║ Browser    ║                                                                 ║
║  [SOON]    ║                                                                 ║
║ Params     ║                                                                 ║
║  [SOON]    ║                                                                 ║
║ Families → ║                                                                 ║
║ Schedules  ║                                                                 ║
║  [SOON]    ║                                                                 ║
╠════════════╩═════════════════════════════════════════════════════════════════╣
║ STATUS: View Templates: 43 A / 38 B — 12 matched, 5 only A, 3 only B, 4 chg  ║
║         Standard: 9 items accepted   [Export Report ▾]                        ║
╚══════════════════════════════════════════════════════════════════════════════╝
```

Source Bar + Status Bar persist across categories. Category rail ⟷ comparison area is a resizable horizontal split.

---

## 4. Source/target selection (Source Bar)
- **Mode** combo: `Build Template` (iterative; accept into the Standard) / `Audit Project` (one-shot; differences = deficiencies). Mode relabels A/B and the report framing.
- **Slot A** ("Source / Project"): combo of open documents + "Browse file…" (background-open) + "Use active document".
- **⇌ swap** (non-destructive relabel) → **Slot B** ("Template / Standard"): open documents + "Load from disk…" + **"The Standard"** (the editable master) + "Load saved snapshot…".
- **Compare ▶** (accent CTA; disabled while running → "Comparing… (cancel)" with indeterminate progress bar across the bar). **✕ Clear** resets results, keeps selections.
- **Build-Template accumulation:** after a run, a link "➕ Compare another project against the Standard" swaps A to a new project while keeping B = Standard and preserving accepted items.

---

## 5. View Templates comparison screen (the core screen)

Two-row vertical split: roster (top) ⟷ field-diff detail (bottom).

```
┌──────────────────────────────────────────────────────────────────────────────┐
│ [🔍 Filter templates…]  [Show: Differences only ▾] [Group: None ▾] [Sort ▾]  │
│  Showing 12 of 81 templates (filter active)                                    │
│ LEGEND:  ≈ Matched   △ Changed   + Only in A   − Only in B                    │
├──┬──────────────────────────┬─────┬──────────────────────────┬─────┬──────────┤
│St│ A — Template Name        │ScA  │ B — Template Name        │ScB  │ Action   │
├──┼──────────────────────────┼─────┼──────────────────────────┼─────┼──────────┤
│△ │ Floor Plan — Working     │●●●○○│ Floor Plan — Working     │●●●●○│[Accept B▾]│ ← DiffChanged
│+ │ Ceiling Plan — As-Built  │●●●●●│ —                        │     │[Accept A▾]│ ← DiffAdded
│− │ —                        │     │ RCP – Reflected          │●●○○○│[Pending ▾]│ ← DiffRemoved
│≈ │ Section — Interior       │●●●○○│ Section — Interior       │●●●○○│[—        ]│ ← match
│...virtualised...                                              Accepted: 4/12   │
├──────────────── horizontal GridSplitter ──────────────────────────────────────┤
│ DETAIL: Floor Plan — Working  [△ 4 fields differ]      Action: [Accept B ▾]   │
│ A: Project Alpha.rvt        B: The Standard      Provenance: B ← Project Gamma │
│ [Overview ★] [V/G Overrides (12)] [Filters (2|3)] [All Fields (31)]           │
│ ┌─────────────────────┬───────────────┬───────────────┬───────┐               │
│ │ Field               │ A Value       │ B Value       │ Diff  │               │
│ │ Scale               │ 1:100         │ 1:50          │ △     │               │
│ │ Detail Level        │ Medium        │ Fine          │ △     │               │
│ │ Discipline          │ Architectural │ Architectural │ ≈     │               │
│ │ Filters Applied     │ 2             │ 3             │ △     │               │
│ │ …24 more match (see All Fields)                     │       │               │
│ └─────────────────────┴───────────────┴───────────────┴───────┘               │
│ Completeness  A:●●●○○ (3/5)   B:●●●●○ (4/5)   Recommendation: B (more complete)│
│ Dependencies to travel: filters[Structural, Grid, Hatch], params[2], patterns[1]│
│ [Accept recommendation ▶]  (records to report / Standard — no Revit changes)   │
└──────────────────────────────────────────────────────────────────────────────┘
```

**Roster columns:** Status glyph (~40px) · A name (`—` if absent) · Score A (5 completeness dots, tooltip explains) · B name · Score B · **Action** combo (`Pending / Accept A / Accept B / Merge (manual) / Ignore`). Row bg uses the diff-semantic color. Header badge "Accepted: 4/12". DataGrid virtualizes (500+ rows fine).

**Detail tabs:** Overview (differing fields first + match count) · V/G Overrides (grouped by category, diff-highlighted) · Filters (name, rule count, present-in A/B/both) · All Fields. Provenance line shows where the Standard's version came from. The **dependency manifest** is shown so the user knows what must travel.

**Action semantics (v1):** in Build-Template mode, `Accept A/B` performs **AcceptIntoStandard** (copies the item + dependency closure into the persisted Standard, records provenance, prompts on conflict). It does **not** touch Revit. `[Accept recommendation ▶]` sets the action to the recommended value and advances to the next Pending row. Tooltip: "Records into the Standard. Revit is not changed in this version."

---

## 6. Report output (report-only)
- Collapsible in-app summary (counts, accepted/pending) with `[Export Report ▾]`.
- **Export ▾** (ToggleButton+Popup): **HTML** (self-contained single file, inline CSS — open in browser, print-to-PDF) as primary; **CSV** (flat diff + field detail) Should; **plain text** Could.
- HTML structure: header (models, date, mode) → summary table → View Templates roster + per-template detail blocks → recommendations/action list → Standard changelog (accepted items + provenance).
- `SaveFileDialog`, default `…\Desktop\RVTuk-Comparator-{date}.html`; "Exported to: <path>" in status bar.

---

## 7. Interaction details
- **Search** filters by name across A and B (multi-token `All(Contains)`), independent of the `Show` status filter.
- **Group by** None / Discipline / View Type / Status with collapsible headers; Expand/Collapse all.
- **Keyboard:** ↑/↓ rows; Enter focuses detail; Tab cycles detail tabs; `Ctrl+E` search; `Ctrl+Shift+E` export; `Esc` closes popups only (not the window).
- **Large sets:** indeterminate progress + incremental `Dispatcher.BeginInvoke` batches; live "Loading n/N".
- **States:** empty (centered prompt + Compare) · running (skeleton rows) · no-differences (green check) · error (inline amber bar + Retry, non-modal — matches Family Browser) · category-not-compared (prompt to run).

---

## 8. Future-proofing
- **Write-back:** the Action model is the seam. Today `Accept` edits the Standard; the write-back release adds an "Apply to Revit" command (enabled later) with no layout redesign. No ghost/disabled Apply button in v1 (cleaner).
- **More categories:** each is a `CategoryViewModelBase` registered in a list; the rail renders from it. `[SOON]` items are pre-registered placeholders showing a "coming later" panel (no errors). **Families** links out to the Family Browser.
- **Settings:** new `ComparatorConfig` (Standard snapshot path, scoring rubric, optional report logo) persisted beside `AppConfig` in `.Setup`; surfaced via a gear in the Source Bar following the existing Settings panel pattern.

---

## 9. MVVM orientation (not a full spec)
```
RVTuk.UI/Views/        ComparatorWindow.xaml
RVTuk.UI/ViewModels/   ComparatorViewModel (mode, sources, category list)
                       CategoryViewModelBase  ViewTemplatesCategoryViewModel
                       PlaceholderCategoryViewModel  ItemDiffViewModel
                       FieldDiffViewModel  DecisionOption
```
`ComparatorViewModel` receives all Revit-API access as `Func<>`/`Action` delegates injected from `RVTuk.Revit` (same pattern as `FamilyBrowserViewModel`); the UI references no Revit type.

---

## 10. PM rulings on UI open items
| Question | Ruling |
|----------|--------|
| Ribbon keyboard shortcut | None in v1 |
| Load closed `.rvt` from disk | Yes (background-open, spec §5.3) |
| Decisions persist between sessions | Transient decisions session-only; **the Standard persists** |
| Scoring criteria | Fixed documented rubric in v1; configurable later |
| `Esc` closes window | No — popups only |
| Ghost "Apply" button | Omit in v1 |
| Report logo | Could (configurable path) |

---

## 11. Saved snapshots (save/load)

Extends §4: a *Project* snapshot (Source A or B, when not "The Standard") can now be persisted to
the same `SnapshotRepository` store and reloaded later without the source file/document present —
e.g. compare a project's state today against a snapshot saved a month ago.

**Implemented this pass (functional core):**
- **Save** (💾 button next to each Source combo): persists whatever was captured for that slot in
  the last Compare run (`SourceKind="Project"`, default name = the document's title, capture
  timestamp preserved from the original capture — no separate rename step).
- **Load saved snapshot…** (📂 button next to each Source combo): opens a modal picker
  (`SnapshotPickerWindow`) listing saved Project snapshots — Name, Captured date (local time),
  Revit year — with a per-row 🗑 delete. Picking one sets that combo's display text to
  `"{name} ({captured date})"` and resolves through it on the next Compare.
- `SnapshotRepository.DeleteSnapshot` added (cascades to `SnapshotCategory` via the existing FK).

**Deferred (noted, not built this pass — see UI-specialist consult):**
- Inline rename-before-save popup (Save currently uses the auto-generated name outright).
- Live filter/search box inside the picker (fine at current expected volumes; add if the list
  grows unwieldy).
- Non-modal "toast" for delete (current delete is immediate + un-confirmed, matching the rest of
  the picker's lightweight modal chrome rather than the app's non-modal error-bar convention).
- Status-bar/detail-header subtext distinguishing "live" vs "saved" provenance at a glance (today
  the combo's own label — `"{name} ({date})"` vs a live document title — is the only cue).

These are fast-follow polish, not required for the "store and compare stored snapshots" capability
itself, which is now real end-to-end (round-tripped through SQLite exactly like the Standard is).

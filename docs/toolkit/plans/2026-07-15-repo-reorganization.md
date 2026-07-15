# Repo Reorganization (Tool-First Structure) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reorganize the RVTuk repo so every tool (Family Browser, Rishui Zamin, Auto Dimensions, Neo Properties) has one docs folder and one code folder per layer, with one canonical name each — zero behavioural change.

**Architecture:** Mechanical, phased refactor per `docs/toolkit/specs/2026-07-15-repo-reorganization-design.md`. The four C# projects and their dependency rules stay; only internal folders, namespaces, and tool-name-bearing class names change. Every task ends with all three configs building green and a commit.

**Tech Stack:** .NET (net48 + net8.0-windows multi-target), WPF, git mv, PowerShell for bulk text replacement.

## Global Constraints

- Spec: `docs/toolkit/specs/2026-07-15-repo-reorganization-design.md` — read it first; it is authoritative.
- Work directly on `master` (mechanical reorg, phased commits). All file moves via `git mv` so history follows.
- **Never change:** assembly names, `.addin` entry classes (`RVTuk.Revit.Application`, `KKarea.Revit.Application`), ClientId GUIDs in `Deploy.ps1`/`Build-Installer.ps1`, SQLite schema, user-facing display strings (ribbon labels, window titles, tooltips), and the persisted `AppConfig` property names `AreaCalcOutputFolder` / `AreaCalcMarkerForm` (they are JSON keys in users' saved config — renaming loses their settings).
- Historical specs/plans keep their old-path *contents* untouched — they are records. Only their file locations change.
- Namespace invariant: `namespace = <project root namespace> + <folder path>` exactly, everywhere (including tests).
- Build verification command set (run from repo root, Windows):
  `dotnet build RVTuk.sln -c Release2023` / `-c Release2024` / `-c Release2025` — each must end "Build succeeded."
  `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj` — all tests pass.
- **Bulk replace helper** (referenced by several tasks as "the replace helper"; paste into PowerShell before use). It preserves each file's encoding and only rewrites files that actually contain the target:

```powershell
function Replace-InFiles([string[]]$files, [string]$old, [string]$new) {
  foreach ($f in $files) {
    $sr = New-Object System.IO.StreamReader($f, $true)
    $text = $sr.ReadToEnd(); $enc = $sr.CurrentEncoding; $sr.Close()
    if ($text.Contains($old)) {
      [System.IO.File]::WriteAllText($f, $text.Replace($old, $new), $enc)
      Write-Host "patched $f"
    }
  }
}
function Replace-InRepo([string]$old, [string]$new) {
  $files = git grep -l -F $old -- "*.cs" "*.csproj" "*.xaml" 2>$null
  if ($files) { Replace-InFiles $files $old $new }
}
```

- After each bulk replace, `git diff --stat` and skim the diff: only the expected kinds of lines (namespace/using/x:Class/clr-namespace/LogicalName/literal) should have changed.
- If a build after a namespace pass fails with CS0246/CS0234/CS0103, the compiler is pointing at a reference the mapping tables missed — fix the flagged line using the same task's mapping table, rebuild. That loop is the completeness check for this refactor.

---

### Task 1: Move existing docs into the per-tool tree

**Files:**
- Move: every `.md` under `docs/autoarea/` and `docs/superpowers/` per the map below (27 files)
- Delete (by emptying): `docs/autoarea/`, `docs/superpowers/`

**Interfaces:**
- Consumes: nothing.
- Produces: the `docs/tools/<tool>/{specs,plans}`, `docs/toolkit/specs`, `docs/future` tree that Task 2 writes new files into. Old paths referenced by `CLAUDE.md`/`VISION.md` break temporarily — fixed in Task 8 (acceptable; docs only).

- [ ] **Step 1: Create the target folders and move all docs (exact commands)**

Run from the repo root (Git Bash):

```bash
mkdir -p docs/tools/family-browser/specs docs/tools/family-browser/plans \
         docs/tools/rishui-zamin/specs docs/tools/rishui-zamin/plans \
         docs/tools/auto-dimensions/specs docs/tools/auto-dimensions/plans \
         docs/tools/neo-properties/specs docs/tools/neo-properties/plans \
         docs/future

git mv docs/autoarea/rishui-zamin-notes.md docs/tools/rishui-zamin/notes.md
git mv docs/autoarea/rishui-zamin-rules.md docs/tools/rishui-zamin/rules.md

git mv docs/superpowers/specs/family-browser-design.md                         docs/tools/family-browser/design.md
git mv docs/superpowers/specs/2026-07-07-family-browser-redesign-design.md     docs/tools/family-browser/specs/
git mv docs/superpowers/specs/2026-07-01-scan-checkboxes-design.md             docs/tools/family-browser/specs/
git mv docs/superpowers/specs/2026-07-09-instruction-image-formatting-design.md docs/tools/family-browser/specs/
git mv docs/superpowers/plans/2026-06-15-family-browser.md                     docs/tools/family-browser/plans/
git mv docs/superpowers/plans/2026-06-22-family-explorer.md                    docs/tools/family-browser/plans/
git mv docs/superpowers/plans/2026-07-01-scan-checkboxes.md                    docs/tools/family-browser/plans/
git mv docs/superpowers/plans/2026-07-09-instruction-image-formatting.md       docs/tools/family-browser/plans/

git mv docs/superpowers/specs/2026-07-01-rishui-zamin-area-submission-design.md docs/tools/rishui-zamin/specs/
git mv docs/superpowers/specs/2026-07-05-area-submission-ui-redesign-design.md  docs/tools/rishui-zamin/specs/
git mv docs/superpowers/specs/2026-07-06-kkarea-revit2023-design.md             docs/tools/rishui-zamin/specs/
git mv docs/superpowers/plans/2026-07-01-rishui-zamin-area-submission.md        docs/tools/rishui-zamin/plans/
git mv docs/superpowers/plans/2026-07-05-area-submission-ui-redesign.md         docs/tools/rishui-zamin/plans/
git mv docs/superpowers/plans/2026-07-06-kkarea-revit2023.md                    docs/tools/rishui-zamin/plans/

git mv docs/superpowers/specs/2026-07-04-auto-dimensions-design.md docs/tools/auto-dimensions/specs/
git mv docs/superpowers/plans/2026-07-04-auto-dimensions.md        docs/tools/auto-dimensions/plans/
git mv docs/superpowers/specs/2026-07-04-neo-properties-design.md  docs/tools/neo-properties/specs/
git mv docs/superpowers/plans/2026-07-04-neo-properties.md         docs/tools/neo-properties/plans/

git mv docs/superpowers/specs/2026-07-03-tool-visibility-design.md docs/toolkit/specs/

git mv docs/superpowers/specs/2026-07-01-instructions-design.md      docs/future/
git mv docs/superpowers/specs/2026-07-01-room-renumbering-design.md  docs/future/

git mv docs/superpowers/specs/2026-06-23-project-comparator-design.md    docs/archive/
git mv docs/superpowers/plans/2026-06-23-project-comparator.md           docs/archive/
git mv docs/superpowers/specs/2026-07-01-auto-dimensioning-design.md     docs/archive/

git mv docs/superpowers/specs/TEMPLATE-feature-design.md docs/TEMPLATE-feature-design.md
```

- [ ] **Step 2: Verify nothing is left behind**

Run: `ls docs/autoarea docs/superpowers/specs docs/superpowers/plans 2>&1`
Expected: "No such file or directory" for all three (git mv removes emptied dirs from tracking; if empty dirs linger on disk, `rmdir` them).

Run: `git status --short | grep -c "^R"`
Expected: 27 renames, no deletions/untracked surprises.

- [ ] **Step 3: Commit**

```bash
git commit -m "docs: move specs/plans/notes into per-tool folders"
```

---

### Task 2: New per-tool READMEs + backlogs, rewrite BACKLOG.md

**Files:**
- Create: `docs/tools/family-browser/README.md`, `docs/tools/family-browser/backlog.md`
- Create: `docs/tools/rishui-zamin/README.md`, `docs/tools/rishui-zamin/backlog.md`
- Create: `docs/tools/auto-dimensions/README.md`, `docs/tools/auto-dimensions/backlog.md`
- Create: `docs/tools/neo-properties/README.md`, `docs/tools/neo-properties/backlog.md`
- Rewrite: `docs/BACKLOG.md`

**Interfaces:**
- Consumes: the tree from Task 1.
- Produces: the per-tool "definitions page" + backlog convention every future session uses. The code paths named in the READMEs are the **post-reorg** paths (Tasks 3–7 create them) — that is intentional; the READMEs describe the end state this plan reaches.

- [ ] **Step 1: Write `docs/tools/family-browser/README.md`**

```markdown
# Family Browser

**What it is:** the family-management tool — indexes the firm's `.rfa` library
(category, parameters, thumbnails) into a shared SQLite database and gives a
searchable/filterable dark-themed window over it, with load/update into the active
project, per-family rich-text instructions, tags, favourites, and custom thumbnails.
Settings (library root, ignored subfolders/patterns, deep scan) and Help/About live
inside the browser window itself (footer toggles).

**Status:** shipped (v1 launch surface).

**Names:** code `FamilyBrowser`; ribbon button "Family Browser" (internal id
`BrowseLibrary`). Older docs say "Family Explorer" / "Library Browser" — same tool.

## Code

| Layer | Folder |
|-------|--------|
| Core  | `src/RVTuk.Core/FamilyBrowser/` (Config, Database, Extraction, Models, Util) |
| UI    | `src/RVTuk.UI/FamilyBrowser/` (Views, ViewModels) |
| Revit | `src/RVTuk.Revit/FamilyBrowser/` (Commands, ExternalEvents, Extraction) |
| Tests | `tests/RVTuk.Core.Tests/FamilyBrowser/` |

## Docs

- [design.md](design.md) — living design (window layout, filters, settings panel)
- [backlog.md](backlog.md) — bugs / improvements / ideas / done
- [specs/](specs/) and [plans/](plans/) — dated historical designs and implementation plans
```

- [ ] **Step 2: Write `docs/tools/family-browser/backlog.md`** (items lifted verbatim from the old `docs/BACKLOG.md`; check off with `[x]` + commit hash when shipped)

```markdown
# Family Browser — Backlog

Bugs, improvements, and ideas for the Family Browser tool. Toolkit-wide items live in
[`../../BACKLOG.md`](../../BACKLOG.md).

## 🐞 Bugs

- [ ] **OLE thumbnails never extract** — after a deep scan *no* family shows its embedded
  preview; `ThumbnailExtractor.ExtractFromRfa` returns null for every file. Pre-existing
  and not previously verified in Revit. Extraction reads the `\x05SummaryInformation` OLE
  stream (PIDSI_THUMBNAIL → VT_CF / CF_DIB) and converts the DIB to PNG via System.Drawing;
  every stage has a silent `catch → null`, so the failure point is unknown. **Next step:**
  add temporary per-stage diagnostic logging (stream missing? byte-order marker `0xFFFE`
  mismatch? thumbnail property not found? unexpected clipboard format? DIB→PNG throw?),
  deep-scan a few known-good `.rfa` in Revit, read the log to localise, then fix. Likely
  causes: modern Revit storing the preview outside SummaryInformation, an OpenMcdf 3.x
  stream-name/read difference, or a DIB header variant `System.Drawing` won't load.

## ✨ Improvements

- [ ] **Deep-scan re-entrancy.** The embedded Settings panel exposes one **Scan** button; a
  user can still click it twice in a row while a scan is in progress. Both runs would share
  `IndexingHandler` / `IndexingEvent` and race. Disable the Scan button (or guard
  `RunDeepScan`) while a scan is in progress.
- [ ] **Deep scan is slow** — it opens every family in Revit to read parameters (which
  upgrades older families to the running version in memory), so a first full scan takes a
  long time. ETA shown (`87fa3e0`); resumable (`57c9ceb`); thumbnail-only scans avoid
  opening families — still no chunked/background resumability across app restarts.
- [ ] **`SettingsCommand` is no longer on the ribbon** (settings folded into the browser,
  2026-07-07 redesign) but the class remains in `FamilyBrowser/Commands/`. Decide:
  keep as a dev-only entry point or delete.

## 🚀 Ideas

- [ ] **Tags follow-ups** (base `c742a9b`, clickable chips `c625502`): a tag auto-complete /
  pick-from-existing list so spelling stays consistent; a dedicated "has tag" filter
  separate from the free-text search.
- [ ] **Recently used** — track the last N families loaded into a project for quick access.
- [ ] Toolbar polish: the ⬅️/⭐/➡️ toggle buttons and Sync button use default (light) WPF
  chrome; style them to match the dark theme. Also style the category checkbox popover.

## ⏳ Deferred (decided "later" during the Family Explorer build)

- [ ] Parameter **write-back** — let the tool actually fix/reorganize parameters in the
  families (currently view/audit only).
- [ ] UI styling/layout polish for the parameter regions.

## ✅ Done

- [x] Family Explorer: network-share concurrency, parameter audit (Group/Kind + filter),
  image gallery.
- [x] Fix: WPF `Application.Current` null crash on Browse Library (`90b96d6`).
- [x] Fix: `LibraryFolderPath` read-only TwoWay binding crash (`7771823`).
- [x] Fix: scan aborting on Windows MAX_PATH; now skips over-long paths (`4b9998f`).
- [x] Window **always on top**; long family names **wrap** to 2–3 lines (`3813ecf`).
- [x] **Multi-word search** — all words match, any order/position (`0336bad`).
- [x] **Per-family Rescan** button — re-extract just the selected family (`1b3c52d`).
- [x] Fix: editor crash on open — `ContextMenu` parented in a Grid (`958f614`).
- [x] Gallery: UNC-safe image `Uri` + confirm before deleting an image (`fd60009`).
- [x] **Ignore subfolders** in deep scan + sync (configurable in Settings) (`534a6f5`).
- [x] Fix: ignored-folder list **now updates** the browser view when changed (`bd5ec13`).
- [x] Filter by **Revit version** — RevitYear column + version dropdown (`e08ce65`)
  *(filter later removed in the 2026-07-07 redesign)*.
- [x] Gallery: **reorder images** with ◀/▶ buttons in the editor (`bb87e65`).
- [x] **Skipped-family count** — deep-scan dialog + `last-scan.log` report skips (`46dada3`).
- [x] **Per-family tags** — editable, searchable, shown in detail (`c742a9b`); clickable
  tag chips that filter the list (`c625502`).
- [x] Editor gallery images **decode off the UI thread** (no stutter on slow shares) (`191c214`).
- [x] Deep-scan progress shows **estimated time remaining** (`87fa3e0`).
- [x] **Two-line toolbar** (`f04eef1`).
- [x] **Favourites** — star a family, "favourites only" filter (`f04eef1`).
- [x] **Multi-select version** filter (`f04eef1`) *(removed in the 2026-07-07 redesign)*.
- [x] **"In the project" / "Outdated" filters** in the Sync dropdown (`f04eef1`).
- [x] On deep-scan **Cancel**, keep everything already extracted (`57c9ceb`).
- [x] Scan facets: one **Scan** button + **Update thumbnails** / **Update parameters**
  checkboxes; Sync-only families get picked up by the next facet scan
  (see [specs/2026-07-01-scan-checkboxes-design.md](specs/2026-07-01-scan-checkboxes-design.md)).
- [x] Settings + Help folded into the browser window (2026-07-07 redesign, `0ec5192`).
- [x] Real version check via `_Version` parameter + model-only rows (`0bf00a8`).
```

- [ ] **Step 3: Write `docs/tools/rishui-zamin/README.md`**

```markdown
# Rishui Zamin (ribbon button: "Area Calc")

**What it is:** reads the Areas on the open sheet and exports the paired `.dxf` + `.dat`
files the רישוי זמין (Rishui Zamin) area-calculation robot expects (`RZ_FRAME` /
`RZ_FLOOR` / `RZ_AREA` layers + attribute blocks). The window's "Setup Usage Keys"
action binds the `RZ_*` shared parameters to Areas and creates/tops-up the usage-key
schedules — no separate ribbon command.

**Status:** shipped (v1 launch surface). Also shipped to Revit 2023 as **KKarea**, a
standalone one-button add-in hosting only this tool (RVTuk itself was dropped from 2023).

**Names:** code `RishuiZamin`; ribbon button displays "Area Calc" (internal id
`AreaCalc`). Older code/docs say `AreaSubmission` / `autoarea` — same tool.

## Code

| Layer | Folder |
|-------|--------|
| Core  | `src/RVTuk.Core/RishuiZamin/` (writers, validator, usage catalog, DXF templates) |
| UI    | `src/RVTuk.UI/RishuiZamin/` (Views, ViewModels) |
| Revit | `src/RVTuk.Revit/RishuiZamin/` (Commands, ExternalEvents, extractor, schedule builder, `RZ_AreaParams.txt`) |
| 2023 host | `src/KKarea.Revit/` (links the Revit-layer sources; must never reference `RVTuk.Revit` itself) |
| Tests | `tests/RVTuk.Core.Tests/RishuiZamin/` |

## Docs

- [notes.md](notes.md) — domain notes on the רישוי זמין submission format
- [rules.md](rules.md) — the robot's validation rules
- [backlog.md](backlog.md) — bugs / improvements / ideas
- [specs/](specs/) and [plans/](plans/) — dated historical designs and implementation
  plans (including the KKarea Revit-2023 host)
```

- [ ] **Step 4: Write `docs/tools/rishui-zamin/backlog.md`**

```markdown
# Rishui Zamin — Backlog

Bugs, improvements, and ideas for the Rishui Zamin (Area Calc) tool, including its
KKarea 2023 host. Toolkit-wide items live in [`../../BACKLOG.md`](../../BACKLOG.md).

## 🐞 Bugs

*(none tracked)*

## ✨ Improvements

*(none tracked)*

## 🚀 Ideas

*(none tracked)*

## ✅ Done

- [x] v1: DXF+DAT export, usage-key schedules, marker forms (branch history: `RZarea`).
- [x] KKarea standalone Revit 2023 host
  (see [specs/2026-07-06-kkarea-revit2023-design.md](specs/2026-07-06-kkarea-revit2023-design.md)).
- [x] Window UI redesign
  (see [specs/2026-07-05-area-submission-ui-redesign-design.md](specs/2026-07-05-area-submission-ui-redesign-design.md)).
```

- [ ] **Step 5: Write `docs/tools/auto-dimensions/README.md`**

```markdown
# Auto Dimensions

**What it is:** draw a detail line on the dedicated "Dimensions_Line" style as a
positional reference; a ribbon command dimensions every wall crossing it, re-runnable
after model changes without re-picking references.

**Status:** code-complete, **hidden for v1** behind the `RegisterUnreleasedTools` flag
in `src/RVTuk.Revit/Application.cs`.

**Names:** code `AutoDimensions`; ribbon button "Auto Dimensions" (when registered).
Supersedes the old separate `KKimensions` / DimensionPropagator project.

## Code

| Layer | Folder |
|-------|--------|
| Core  | `src/RVTuk.Core/AutoDimensions/` (wall-crossing finder, reference filter) |
| UI    | — (no UI; the tool is a single command) |
| Revit | `src/RVTuk.Revit/AutoDimensions/` (command, tracker, line style) |
| Tests | `tests/RVTuk.Core.Tests/AutoDimensions/` |

## Docs

- [specs/2026-07-04-auto-dimensions-design.md](specs/2026-07-04-auto-dimensions-design.md) — approved design
- [plans/2026-07-04-auto-dimensions.md](plans/2026-07-04-auto-dimensions.md) — implementation plan
- [backlog.md](backlog.md) — bugs / improvements / ideas
```

- [ ] **Step 6: Write `docs/tools/auto-dimensions/backlog.md`**

```markdown
# Auto Dimensions — Backlog

Toolkit-wide items live in [`../../BACKLOG.md`](../../BACKLOG.md).

## 🐞 Bugs

*(none tracked)*

## ✨ Improvements

*(none tracked)*

## 🚀 Ideas

*(none tracked)*

## ⏳ Release

- [ ] In-Revit verification pass, then flip `RegisterUnreleasedTools` (or promote this
  tool to the always-registered set) to ship it.
```

- [ ] **Step 7: Write `docs/tools/neo-properties/README.md`**

```markdown
# Neo Properties

**What it is:** a dockable pane mirroring the selected element's parameters like the
native Properties palette, but with pinned parameters shown first and remaining groups
in a fixed custom order. Read-only, single-element only.

**Status:** code-complete, **hidden for v1** behind the `RegisterUnreleasedTools` flag
in `src/RVTuk.Revit/Application.cs`.

**Names:** code `NeoProperties`; ribbon button "Neo Properties" (when registered).

## Code

| Layer | Folder |
|-------|--------|
| Core  | `src/RVTuk.Core/NeoProperties/` (parameter ordering/grouping) |
| UI    | `src/RVTuk.UI/NeoProperties/` (pane view + view model) |
| Revit | `src/RVTuk.Revit/NeoProperties/` (command, pane provider, selection handler) |
| Tests | `tests/RVTuk.Core.Tests/NeoProperties/` |

## Docs

- [specs/2026-07-04-neo-properties-design.md](specs/2026-07-04-neo-properties-design.md) — approved design
- [plans/2026-07-04-neo-properties.md](plans/2026-07-04-neo-properties.md) — implementation plan
- [backlog.md](backlog.md) — bugs / improvements / ideas
```

- [ ] **Step 8: Write `docs/tools/neo-properties/backlog.md`**

```markdown
# Neo Properties — Backlog

Toolkit-wide items live in [`../../BACKLOG.md`](../../BACKLOG.md).

## 🐞 Bugs

*(none tracked)*

## ✨ Improvements

*(none tracked)*

## 🚀 Ideas

*(none tracked)*

## ⏳ Release

- [ ] In-Revit verification pass, then flip `RegisterUnreleasedTools` (or promote this
  tool to the always-registered set) to ship it.
```

- [ ] **Step 9: Replace the full contents of `docs/BACKLOG.md`**

```markdown
# RVTuk — Backlog (toolkit-wide)

Toolkit-wide items only: installer, deploy, ribbon/host, cross-tool concerns.
**Per-tool backlogs** (bugs, improvements, ideas for one tool) live next to each tool:

- [Family Browser](tools/family-browser/backlog.md)
- [Rishui Zamin](tools/rishui-zamin/backlog.md)
- [Auto Dimensions](tools/auto-dimensions/backlog.md)
- [Neo Properties](tools/neo-properties/backlog.md)

For the product vision and roadmap see [`../VISION.md`](../VISION.md).
Check off with `[x]` and the commit hash when shipped.

---

## ▶ Status (read me first)

- Branch: **`master`** (the old `family-explorer` branch work is merged and shipped).
- **v1 launch surface:** Family Browser + Rishui Zamin (Area Calc) are registered;
  Auto Dimensions and Neo Properties are code-complete but hidden behind
  `RegisterUnreleasedTools` in `src/RVTuk.Revit/Application.cs`.
- Working style: replies terse; **minimal code comments**; run git for the user
  (git novice) and explain simply; move items to the right backlog's Done section
  with the commit hash as they ship.

## 🐞 Bugs / things to fix

- [~] When closed, Revit crashes (access violation `c0000005` in `siappdll.dll`).
      **Diagnosed: NOT RVTuk.** `siappdll.dll` / `3DxRevit.dll` is the 3Dconnexion
      SpaceMouse driver. The crash is on a non-main native thread during Revit
      shutdown; every RVTuk journal entry is a normal startup/command event. Fix is on
      3Dconnexion's side: update or temporarily disable the 3Dconnexion add-in /
      SpaceMouse driver. (CER dump: `…\Local\Autodesk\CER\92ed161c…\29`.)

## ✨ Improvements

*(none tracked)*

## ✅ Done

- [x] `Deploy.ps1`: per-version resilience (skip a locked/open Revit year), version
  filter, colored summary (`621f880`).
- [x] Standalone `RVTukSetup.exe` installer for other computers (`49abd6e`).
- [x] Tool-first repo reorganization
  (see [toolkit/specs/2026-07-15-repo-reorganization-design.md](toolkit/specs/2026-07-15-repo-reorganization-design.md)).
```

- [ ] **Step 10: Commit**

```bash
git add docs
git commit -m "docs: per-tool READMEs and backlogs; slim toolkit-wide BACKLOG"
```

---

### Task 3: Move the RVTuk.UI project to `src\RVTuk.UI`

**Files:**
- Move: `src/LibraryBrowser/RVTuk.UI/` → `src/RVTuk.UI/` (whole tree)
- Modify: `RVTuk.sln:8`
- Modify: `src/RVTuk.Revit/RVTuk.Revit.csproj:55`
- Modify: `src/KKarea.Revit/KKarea.Revit.csproj:34`

**Interfaces:**
- Consumes: nothing new.
- Produces: the `src/RVTuk.UI/` root that Task 5 reorganizes internally. No namespace changes in this task.

- [ ] **Step 1: Move the project**

```bash
git mv src/LibraryBrowser/RVTuk.UI src/RVTuk.UI
```

Then confirm `src/LibraryBrowser/` no longer exists (`ls src/`); remove the empty dir if it lingers on disk.

- [ ] **Step 2: Update the three path references**

In `RVTuk.sln` line 8, replace:
`"src\LibraryBrowser\RVTuk.UI\RVTuk.UI.csproj"` → `"src\RVTuk.UI\RVTuk.UI.csproj"`

In `src/RVTuk.Revit/RVTuk.Revit.csproj`, replace:
`<ProjectReference Include="..\LibraryBrowser\RVTuk.UI\RVTuk.UI.csproj" />` → `<ProjectReference Include="..\RVTuk.UI\RVTuk.UI.csproj" />`

In `src/KKarea.Revit/KKarea.Revit.csproj`, replace:
`<ProjectReference Include="..\LibraryBrowser\RVTuk.UI\RVTuk.UI.csproj" />` → `<ProjectReference Include="..\RVTuk.UI\RVTuk.UI.csproj" />`

- [ ] **Step 3: Verify no other live references remain**

Run: `git grep -n "LibraryBrowser" -- ":(exclude)docs"`
Expected: no output. (Historical docs still mention the old path — that's correct.)

- [ ] **Step 4: Build all three configs**

```powershell
dotnet build RVTuk.sln -c Release2023
dotnet build RVTuk.sln -c Release2024
dotnet build RVTuk.sln -c Release2025
```
Expected: "Build succeeded." ×3.

- [ ] **Step 5: Commit**

```bash
git commit -am "refactor: move RVTuk.UI project out of src/LibraryBrowser to src/RVTuk.UI"
```

---

### Task 4: RVTuk.Core per-tool folders + namespace pass (+ tests mirror)

**Files:**
- Move: everything under `src/RVTuk.Core/{AreaSubmission,Config,Database,Extraction,Models,Util}/` per Step 1
- Move: test files per Step 2
- Modify: namespace/using lines across `src/` and `tests/` (bulk replaces, Step 3)
- Modify: `src/RVTuk.Core/RVTuk.Core.csproj` (EmbeddedResource paths), `tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj` (Content glob), `tests/RVTuk.Core.Tests/RishuiZamin/DxfWriterTests.cs:58` (fixture path)

**Interfaces:**
- Consumes: Task 3's `src/RVTuk.UI` location (bulk replaces touch UI files' `using` lines).
- Produces: Core namespaces `RVTuk.Core.RishuiZamin`, `RVTuk.Core.FamilyBrowser.{Config,Database,Extraction,Models,Util}`, `RVTuk.Core.Shared.{Config,Util}` — Tasks 5–7 and all consumers rely on these exact names. Class names are unchanged in this task.

- [ ] **Step 1: Move Core files (exact commands, Git Bash)**

```bash
git mv src/RVTuk.Core/AreaSubmission src/RVTuk.Core/RishuiZamin

mkdir -p src/RVTuk.Core/FamilyBrowser/Config src/RVTuk.Core/FamilyBrowser/Util \
         src/RVTuk.Core/Shared/Config src/RVTuk.Core/Shared/Util

git mv src/RVTuk.Core/Config/LibraryFolderValidator.cs src/RVTuk.Core/FamilyBrowser/Config/
git mv src/RVTuk.Core/Config/AppConfig.cs      src/RVTuk.Core/Shared/Config/
git mv src/RVTuk.Core/Config/ConfigManager.cs  src/RVTuk.Core/Shared/Config/
git mv src/RVTuk.Core/Database   src/RVTuk.Core/FamilyBrowser/Database
git mv src/RVTuk.Core/Extraction src/RVTuk.Core/FamilyBrowser/Extraction
git mv src/RVTuk.Core/Models     src/RVTuk.Core/FamilyBrowser/Models
git mv src/RVTuk.Core/Util/FamilyVersionCheck.cs  src/RVTuk.Core/FamilyBrowser/Util/
git mv src/RVTuk.Core/Util/IgnoredFileMatcher.cs  src/RVTuk.Core/FamilyBrowser/Util/
git mv src/RVTuk.Core/Util/ImageCropGeometry.cs   src/RVTuk.Core/FamilyBrowser/Util/
git mv src/RVTuk.Core/Util/PathUtil.cs            src/RVTuk.Core/Shared/Util/
```

(`src/RVTuk.Core/Config` and `.../Util` are now empty — confirm they're gone.)

- [ ] **Step 2: Move test files to mirror**

```bash
git mv tests/RVTuk.Core.Tests/AreaSubmission tests/RVTuk.Core.Tests/RishuiZamin
mkdir -p tests/RVTuk.Core.Tests/FamilyBrowser tests/RVTuk.Core.Tests/Shared

git mv tests/RVTuk.Core.Tests/BrowserRepositoryVersionTests.cs tests/RVTuk.Core.Tests/FamilyBrowser/
git mv tests/RVTuk.Core.Tests/FamilyIndexerTests.cs            tests/RVTuk.Core.Tests/FamilyBrowser/
git mv tests/RVTuk.Core.Tests/FamilyVersionCheckTests.cs       tests/RVTuk.Core.Tests/FamilyBrowser/
git mv tests/RVTuk.Core.Tests/IgnoredFileMatcherTests.cs       tests/RVTuk.Core.Tests/FamilyBrowser/
git mv tests/RVTuk.Core.Tests/IndexRepositoryTests.cs          tests/RVTuk.Core.Tests/FamilyBrowser/
git mv tests/RVTuk.Core.Tests/LibraryFolderValidatorTests.cs   tests/RVTuk.Core.Tests/FamilyBrowser/
git mv tests/RVTuk.Core.Tests/ThumbnailExtractorTests.cs       tests/RVTuk.Core.Tests/FamilyBrowser/
git mv tests/RVTuk.Core.Tests/Util/ImageCropGeometryTests.cs   tests/RVTuk.Core.Tests/FamilyBrowser/
git mv tests/RVTuk.Core.Tests/AppConfigTests.cs                tests/RVTuk.Core.Tests/Shared/
```

(`tests/RVTuk.Core.Tests/Util` is now empty — confirm it's gone.)

- [ ] **Step 3: Bulk namespace replaces (PowerShell, using the replace helper from Global Constraints)**

Run in this exact order:

```powershell
Replace-InRepo "RVTuk.Core.Tests.AreaSubmission" "RVTuk.Core.Tests.RishuiZamin"
Replace-InRepo "RVTuk.Core.Tests.Util"           "RVTuk.Core.Tests.FamilyBrowser"
Replace-InRepo "RVTuk.Core.AreaSubmission" "RVTuk.Core.RishuiZamin"
Replace-InRepo "RVTuk.Core.Database"   "RVTuk.Core.FamilyBrowser.Database"
Replace-InRepo "RVTuk.Core.Extraction" "RVTuk.Core.FamilyBrowser.Extraction"
Replace-InRepo "RVTuk.Core.Models"     "RVTuk.Core.FamilyBrowser.Models"
Replace-InRepo "RVTuk.Core.Config"     "RVTuk.Core.Shared.Config"
Replace-InRepo "RVTuk.Core.Util"       "RVTuk.Core.FamilyBrowser.Util"
```

(The `Tests.*` replaces run first so the shorter `RVTuk.Core.*` patterns can't corrupt them. The `LogicalName` values in `RVTuk.Core.csproj` and the resource-name literal in `DxfWriter.cs:630` are covered by the `RVTuk.Core.AreaSubmission` replace — verify with `git grep -n "RVTuk.Core.RishuiZamin.DxfTemplates"`, expect 3 hits: two LogicalNames, one literal.)

- [ ] **Step 4: Fix the two split namespaces (per-file edits)**

The blanket replaces put every old `RVTuk.Core.Config`/`RVTuk.Core.Util` member in the Shared/FamilyBrowser bucket respectively; two files moved the *other* way:

1. `src/RVTuk.Core/FamilyBrowser/Config/LibraryFolderValidator.cs` — its namespace decl now reads `RVTuk.Core.Shared.Config`; change to:
   `namespace RVTuk.Core.FamilyBrowser.Config`
2. `src/RVTuk.Core/Shared/Util/PathUtil.cs` — its decl now reads `RVTuk.Core.FamilyBrowser.Util`; change to:
   `namespace RVTuk.Core.Shared.Util`

Then repair their consumers:

- Run `git grep -l "LibraryFolderValidator" -- "*.cs"`. In every listed file **except** `LibraryFolderValidator.cs` itself, add `using RVTuk.Core.FamilyBrowser.Config;` next to the existing usings (expected: `LibraryFolderValidatorTests.cs` and the UI ViewModel(s) that validate the library path — currently `ConfigViewModel.cs`/`SettingsViewModel.cs`).
- Run `git grep -l "PathUtil" -- "*.cs"`. In every listed file **except** `PathUtil.cs` itself, add `using RVTuk.Core.Shared.Util;` (expected: `FamilyIndexer.cs`, `FamilyBrowserViewModel.cs`, possibly repositories). In `IgnoredFileMatcher.cs:11` the hit is only an XML-doc `<see cref="PathUtil.IsUnderIgnoredFolder"/>` — qualify it instead: `<see cref="Shared.Util.PathUtil.IsUnderIgnoredFolder"/>`.

- [ ] **Step 5: Update the relative qualifier in AppConfig**

`src/RVTuk.Core/Shared/Config/AppConfig.cs` lines 22 (two occurrences on the property line):
`AreaSubmission.MarkerForm` → `RishuiZamin.MarkerForm` (both the type and the `.FormA` default). Do **not** rename the `AreaCalcMarkerForm`/`AreaCalcOutputFolder` property names (persisted JSON keys).

- [ ] **Step 6: Update item paths in the two csproj files + the fixture path literal**

`src/RVTuk.Core/RVTuk.Core.csproj` (the two EmbeddedResource items):
`Include="AreaSubmission\DxfTemplates\Preamble.dxf"` → `Include="RishuiZamin\DxfTemplates\Preamble.dxf"` (same for `Postamble.dxf`).

`tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj`:
`<Content Include="AreaSubmission\Fixtures\**\*.dxf">` → `<Content Include="RishuiZamin\Fixtures\**\*.dxf">`

`tests/RVTuk.Core.Tests/RishuiZamin/DxfWriterTests.cs:58`:
`Path.Combine(AppContext.BaseDirectory, "AreaSubmission", "Fixtures", "one_area.dxf")` → `Path.Combine(AppContext.BaseDirectory, "RishuiZamin", "Fixtures", "one_area.dxf")`

- [ ] **Step 7: Per-file namespace decls for the moved root-level tests**

All eight moved test files declare `namespace RVTuk.Core.Tests;` (file-scoped, except `ThumbnailExtractorTests.cs` which uses a block). Update each:

| File | New namespace |
|------|---------------|
| `tests/.../FamilyBrowser/BrowserRepositoryVersionTests.cs` | `RVTuk.Core.Tests.FamilyBrowser` |
| `tests/.../FamilyBrowser/FamilyIndexerTests.cs` | `RVTuk.Core.Tests.FamilyBrowser` |
| `tests/.../FamilyBrowser/FamilyVersionCheckTests.cs` | `RVTuk.Core.Tests.FamilyBrowser` |
| `tests/.../FamilyBrowser/IgnoredFileMatcherTests.cs` | `RVTuk.Core.Tests.FamilyBrowser` |
| `tests/.../FamilyBrowser/IndexRepositoryTests.cs` | `RVTuk.Core.Tests.FamilyBrowser` |
| `tests/.../FamilyBrowser/LibraryFolderValidatorTests.cs` | `RVTuk.Core.Tests.FamilyBrowser` |
| `tests/.../FamilyBrowser/ThumbnailExtractorTests.cs` | `RVTuk.Core.Tests.FamilyBrowser` |
| `tests/.../Shared/AppConfigTests.cs` | `RVTuk.Core.Tests.Shared` |

(`ImageCropGeometryTests.cs` was already fixed by the `RVTuk.Core.Tests.Util` bulk replace.)

- [ ] **Step 8: Build + test**

```powershell
dotnet build RVTuk.sln -c Release2023
dotnet build RVTuk.sln -c Release2024
dotnet build RVTuk.sln -c Release2025
dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj
```
Expected: three "Build succeeded", all tests pass (the DxfWriter tests prove the embedded-resource rename; the fixture-path test proves the Content move). If CS0246/CS0234 appear, fix per Global Constraints.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "refactor(core): per-tool folders + namespaces (FamilyBrowser/RishuiZamin/Shared), tests mirror"
```

---

### Task 5: RVTuk.UI internals — per-tool folders, namespaces, XAML plumbing

**Files:**
- Move: everything under `src/RVTuk.UI/{Views,ViewModels,Controls,Converters,Helpers,Themes}/` per Step 1
- Modify: namespace decls in all moved `.cs` files; `x:Class`/`clr-namespace`/pack-URI lines in all 7 `.xaml` files; `using` lines in `src/RVTuk.Revit/` and `src/KKarea.Revit/` consumers

**Interfaces:**
- Consumes: Core namespaces from Task 4.
- Produces: UI namespaces `RVTuk.UI.FamilyBrowser.{Views,ViewModels}`, `RVTuk.UI.RishuiZamin.{Views,ViewModels}`, `RVTuk.UI.NeoProperties.{Views,ViewModels}`, `RVTuk.UI.Shared.{ViewModels,Converters,Controls,Helpers}`; theme pack URI `/RVTuk.UI;component/Shared/Themes/DarkTheme.xaml`. Class names unchanged (renames happen in Task 7).

- [ ] **Step 1: Move UI files (exact commands, Git Bash)**

```bash
cd src/RVTuk.UI
mkdir -p FamilyBrowser/Views FamilyBrowser/ViewModels RishuiZamin/Views RishuiZamin/ViewModels \
         NeoProperties/Views NeoProperties/ViewModels Shared/ViewModels

git mv Views/FamilyBrowserWindow.xaml Views/FamilyBrowserWindow.xaml.cs \
       Views/InstructionsEditorWindow.xaml Views/InstructionsEditorWindow.xaml.cs \
       Views/ImageCropWindow.xaml Views/ImageCropWindow.xaml.cs \
       Views/IndexProgressWindow.xaml Views/IndexProgressWindow.xaml.cs \
       Views/SettingsWindow.xaml Views/SettingsWindow.xaml.cs         FamilyBrowser/Views/
git mv ViewModels/FamilyBrowserViewModel.cs ViewModels/FamilyBrowserItemViewModel.cs \
       ViewModels/CategoryFilterOption.cs ViewModels/ConfigViewModel.cs \
       ViewModels/SettingsViewModel.cs ViewModels/IndexProgressViewModel.cs \
       ViewModels/InstructionsEditorViewModel.cs                      FamilyBrowser/ViewModels/

git mv Views/AreaSubmissionWindow.xaml Views/AreaSubmissionWindow.xaml.cs RishuiZamin/Views/
git mv ViewModels/AreaSubmissionViewModel.cs ViewModels/AreaRowViewModel.cs \
       ViewModels/AreaLevelGroupViewModel.cs                          RishuiZamin/ViewModels/

git mv Views/NeoPropertiesView.xaml Views/NeoPropertiesView.xaml.cs   NeoProperties/Views/
git mv ViewModels/NeoPropertiesViewModel.cs                           NeoProperties/ViewModels/

git mv ViewModels/ViewModelBase.cs ViewModels/RelayCommand.cs ViewModels/RelayCommandT.cs Shared/ViewModels/
git mv Converters Shared/Converters
git mv Controls   Shared/Controls
git mv Helpers    Shared/Helpers
git mv Themes     Shared/Themes
cd ../..
```

(`Views/` and `ViewModels/` are now empty — confirm they're gone.)

- [ ] **Step 2: Bulk replaces — unambiguous namespaces and fully-qualified type names (PowerShell, replace helper)**

```powershell
Replace-InRepo "RVTuk.UI.Controls"   "RVTuk.UI.Shared.Controls"
Replace-InRepo "RVTuk.UI.Helpers"    "RVTuk.UI.Shared.Helpers"
Replace-InRepo "RVTuk.UI.Converters" "RVTuk.UI.Shared.Converters"
Replace-InRepo "/RVTuk.UI;component/Themes/DarkTheme.xaml" "/RVTuk.UI;component/Shared/Themes/DarkTheme.xaml"
Replace-InRepo "RVTuk.UI.Views.FamilyBrowserWindow"      "RVTuk.UI.FamilyBrowser.Views.FamilyBrowserWindow"
Replace-InRepo "RVTuk.UI.Views.InstructionsEditorWindow" "RVTuk.UI.FamilyBrowser.Views.InstructionsEditorWindow"
Replace-InRepo "RVTuk.UI.Views.ImageCropWindow"          "RVTuk.UI.FamilyBrowser.Views.ImageCropWindow"
Replace-InRepo "RVTuk.UI.Views.IndexProgressWindow"      "RVTuk.UI.FamilyBrowser.Views.IndexProgressWindow"
Replace-InRepo "RVTuk.UI.Views.SettingsWindow"           "RVTuk.UI.FamilyBrowser.Views.SettingsWindow"
Replace-InRepo "RVTuk.UI.Views.AreaSubmissionWindow"     "RVTuk.UI.RishuiZamin.Views.AreaSubmissionWindow"
Replace-InRepo "RVTuk.UI.Views.NeoPropertiesView"        "RVTuk.UI.NeoProperties.Views.NeoPropertiesView"
Replace-InRepo "RVTuk.UI.ViewModels.NeoPropertiesViewModel" "RVTuk.UI.NeoProperties.ViewModels.NeoPropertiesViewModel"
```

(These cover the `x:Class` attributes in all 7 XAML files and the fully-qualified static-property types in both hosts' `Application.cs`.)

- [ ] **Step 3: Per-file namespace decls for the moved `.cs` files**

| File(s) | New namespace decl |
|---------|--------------------|
| `FamilyBrowser/Views/*.xaml.cs` (5 files) | `namespace RVTuk.UI.FamilyBrowser.Views` |
| `FamilyBrowser/ViewModels/*.cs` (7 files) | `namespace RVTuk.UI.FamilyBrowser.ViewModels` |
| `RishuiZamin/Views/AreaSubmissionWindow.xaml.cs` | `namespace RVTuk.UI.RishuiZamin.Views` |
| `RishuiZamin/ViewModels/*.cs` (3 files) | `namespace RVTuk.UI.RishuiZamin.ViewModels` |
| `NeoProperties/Views/NeoPropertiesView.xaml.cs` | `namespace RVTuk.UI.NeoProperties.Views` |
| `NeoProperties/ViewModels/NeoPropertiesViewModel.cs` | `namespace RVTuk.UI.NeoProperties.ViewModels` |
| `Shared/ViewModels/{ViewModelBase,RelayCommand,RelayCommandT}.cs` | `namespace RVTuk.UI.Shared.ViewModels` |

(Note: some decls were already rewritten by Step 2's fully-qualified replaces where the old decl line read `namespace RVTuk.UI.Views` — the replaces don't touch decls, only qualified usages, so set every decl per this table. `Shared/Controls`, `Shared/Converters`, `Shared/Helpers` decls were fixed by Step 2's first three replaces.)

- [ ] **Step 4: Rewrite the split `using` lines (per-file table)**

Every occurrence of `using RVTuk.UI.Views;` becomes the tool-specific line(s):

| File | Replacement |
|------|-------------|
| `src/RVTuk.Revit/Commands/BrowseLibraryCommand.cs` | `using RVTuk.UI.FamilyBrowser.Views;` |
| `src/RVTuk.Revit/Commands/IndexLibraryCommand.cs` | `using RVTuk.UI.FamilyBrowser.Views;` |
| `src/RVTuk.Revit/Commands/SettingsCommand.cs` | `using RVTuk.UI.FamilyBrowser.Views;` |
| `src/RVTuk.Revit/Commands/AreaCalcCommand.cs` | `using RVTuk.UI.RishuiZamin.Views;` |
| `src/RVTuk.Revit/NeoProperties/NeoPropertiesPaneProvider.cs` | `using RVTuk.UI.NeoProperties.Views;` |
| `src/KKarea.Revit/Commands/AreaCalcCommand.cs` | `using RVTuk.UI.RishuiZamin.Views;` |

Every occurrence of `using RVTuk.UI.ViewModels;` becomes:

| File | Replacement |
|------|-------------|
| `src/RVTuk.Revit/Commands/AreaCalcCommand.cs` | `using RVTuk.UI.RishuiZamin.ViewModels;` |
| `src/RVTuk.Revit/NeoProperties/NeoPropertiesSelectionHandler.cs` | `using RVTuk.UI.NeoProperties.ViewModels;` |
| `src/KKarea.Revit/Commands/AreaCalcCommand.cs` | `using RVTuk.UI.RishuiZamin.ViewModels;` |
| `src/RVTuk.UI/FamilyBrowser/Views/FamilyBrowserWindow.xaml.cs` | `using RVTuk.UI.FamilyBrowser.ViewModels;` |
| `src/RVTuk.UI/FamilyBrowser/Views/IndexProgressWindow.xaml.cs` | `using RVTuk.UI.FamilyBrowser.ViewModels;` |
| `src/RVTuk.UI/FamilyBrowser/Views/InstructionsEditorWindow.xaml.cs` | `using RVTuk.UI.FamilyBrowser.ViewModels;` |
| `src/RVTuk.UI/FamilyBrowser/Views/SettingsWindow.xaml.cs` | `using RVTuk.UI.FamilyBrowser.ViewModels;` |
| `src/RVTuk.UI/RishuiZamin/Views/AreaSubmissionWindow.xaml.cs` | `using RVTuk.UI.RishuiZamin.ViewModels;` |

Then run `git grep -n "using RVTuk.UI.Views;\|using RVTuk.UI.ViewModels;" -- "*.cs"` — any file the tables missed gets the namespace matching the tool of the types it uses (Family Browser unless the file is Area/Neo-related).

- [ ] **Step 5: Add the Shared usings inside UI**

Types `ViewModelBase`, `RelayCommand`, `RelayCommand<T>` moved to `RVTuk.UI.Shared.ViewModels`, so every view model that previously found them in its own namespace now needs a using. Add `using RVTuk.UI.Shared.ViewModels;` to **all** `.cs` files under `src/RVTuk.UI/FamilyBrowser/ViewModels/`, `src/RVTuk.UI/RishuiZamin/ViewModels/`, and `src/RVTuk.UI/NeoProperties/ViewModels/`, plus any `.xaml.cs` the compiler flags. An unused extra using is harmless.

- [ ] **Step 6: XAML namespace mappings**

Run `git grep -n "clr-namespace:RVTuk.UI" -- "*.xaml"`. Update each hit:

- `clr-namespace:RVTuk.UI.Shared.Converters` / `.Shared.Controls` / `.Shared.Helpers` — already correct via Step 2; leave.
- `clr-namespace:RVTuk.UI.ViewModels` → the owning window's tool namespace (`RVTuk.UI.FamilyBrowser.ViewModels` in Family Browser windows, `RVTuk.UI.RishuiZamin.ViewModels` in `AreaSubmissionWindow.xaml`, `RVTuk.UI.NeoProperties.ViewModels` in `NeoPropertiesView.xaml`).
- `clr-namespace:RVTuk.UI.Views` → the owning window's tool Views namespace.

- [ ] **Step 7: Build all three configs**

```powershell
dotnet build RVTuk.sln -c Release2023
dotnet build RVTuk.sln -c Release2024
dotnet build RVTuk.sln -c Release2025
```
Expected: "Build succeeded." ×3. XAML errors (unknown x:Class / clr-namespace) point at a Step 3/6 line — fix using the same tables.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "refactor(ui): per-tool folders + namespaces inside RVTuk.UI"
```

---

### Task 6: RVTuk.Revit + KKarea — per-tool folders, namespaces, linked sources

**Files:**
- Move: files under `src/RVTuk.Revit/{AreaSubmission,Commands,ExternalEvents,Extraction,Resources}/` per Step 1
- Modify: namespace decls per Step 2–3; `using` lines in `Application.cs` (both hosts) and command files
- Modify: `src/RVTuk.Revit/RVTuk.Revit.csproj` (RZ_AreaParams path), `src/KKarea.Revit/KKarea.Revit.csproj` (five link paths)

**Interfaces:**
- Consumes: Task 4/5 namespaces.
- Produces: `RVTuk.Revit.FamilyBrowser.{Commands,ExternalEvents,Extraction}`, `RVTuk.Revit.RishuiZamin[.Commands|.ExternalEvents]`, `RVTuk.Revit.NeoProperties.Commands`. `RVTuk.Revit.Application` (namespace + class) is untouched.

- [ ] **Step 1: Move files (exact commands, Git Bash)**

```bash
cd src/RVTuk.Revit
mkdir -p FamilyBrowser/Commands FamilyBrowser/ExternalEvents FamilyBrowser/Extraction \
         RishuiZamin/Commands RishuiZamin/ExternalEvents RishuiZamin/Resources \
         NeoProperties/Commands

git mv Commands/BrowseLibraryCommand.cs Commands/IndexLibraryCommand.cs \
       Commands/SettingsCommand.cs                       FamilyBrowser/Commands/
git mv ExternalEvents/IndexingExternalEventHandler.cs \
       ExternalEvents/GetProjectFamiliesEventHandler.cs \
       ExternalEvents/LoadFamilyEventHandler.cs \
       ExternalEvents/OpenFamilyEditorEventHandler.cs    FamilyBrowser/ExternalEvents/
git mv Extraction/FamilyMetadataExtractor.cs             FamilyBrowser/Extraction/

git mv Commands/AreaCalcCommand.cs Commands/SetupRishuiZaminParamsCommand.cs RishuiZamin/Commands/
git mv ExternalEvents/AreaExtractEventHandler.cs \
       ExternalEvents/SelectAreaEventHandler.cs \
       ExternalEvents/SetupUsageKeysEventHandler.cs      RishuiZamin/ExternalEvents/
git mv AreaSubmission/AreaExtractor.cs AreaSubmission/UsageKeyScheduleBuilder.cs RishuiZamin/
git mv Resources/RZ_AreaParams.txt                       RishuiZamin/Resources/

git mv Commands/NeoPropertiesCommand.cs                  NeoProperties/Commands/
cd ../..
```

(`Commands/`, `ExternalEvents/`, `Extraction/`, `AreaSubmission/`, `Resources/` are now empty — confirm they're gone.)

- [ ] **Step 2: Bulk replaces (PowerShell, replace helper)**

```powershell
Replace-InRepo "RVTuk.Revit.AreaSubmission" "RVTuk.Revit.RishuiZamin"
Replace-InRepo "RVTuk.Revit.Extraction"     "RVTuk.Revit.FamilyBrowser.Extraction"
```

- [ ] **Step 3: Per-file namespace decls for the split folders**

| File(s) | New namespace decl |
|---------|--------------------|
| `FamilyBrowser/Commands/{BrowseLibraryCommand,IndexLibraryCommand,SettingsCommand}.cs` | `namespace RVTuk.Revit.FamilyBrowser.Commands` |
| `FamilyBrowser/ExternalEvents/*.cs` (4 files) | `namespace RVTuk.Revit.FamilyBrowser.ExternalEvents` |
| `RishuiZamin/Commands/{AreaCalcCommand,SetupRishuiZaminParamsCommand}.cs` | `namespace RVTuk.Revit.RishuiZamin.Commands` |
| `RishuiZamin/ExternalEvents/*.cs` (3 files) | `namespace RVTuk.Revit.RishuiZamin.ExternalEvents` |
| `NeoProperties/Commands/NeoPropertiesCommand.cs` | `namespace RVTuk.Revit.NeoProperties.Commands` |

(`RishuiZamin/AreaExtractor.cs` + `UsageKeyScheduleBuilder.cs` decls were fixed by Step 2's first replace. `AutoDimensions/` and `NeoProperties/{PaneProvider,SelectionHandler}` decls are already correct.)

- [ ] **Step 4: Rewrite the split `using` lines**

In `src/RVTuk.Revit/Application.cs`, replace
`using RVTuk.Revit.Commands;` with:

```csharp
using RVTuk.Revit.FamilyBrowser.Commands;
using RVTuk.Revit.NeoProperties.Commands;
using RVTuk.Revit.RishuiZamin.Commands;
```

and `using RVTuk.Revit.ExternalEvents;` with:

```csharp
using RVTuk.Revit.FamilyBrowser.ExternalEvents;
using RVTuk.Revit.RishuiZamin.ExternalEvents;
```

In `src/KKarea.Revit/Application.cs`, replace `using RVTuk.Revit.ExternalEvents;` with `using RVTuk.Revit.RishuiZamin.ExternalEvents;`.

Then run `git grep -n "using RVTuk.Revit.Commands;\|using RVTuk.Revit.ExternalEvents;" -- "*.cs"` — rewrite any remaining hit to the tool-appropriate namespace(s) (e.g. a command file referencing its own tool's handlers).

- [ ] **Step 5: csproj item paths**

`src/RVTuk.Revit/RVTuk.Revit.csproj`:
`<None Include="Resources\RZ_AreaParams.txt">` → `<None Include="RishuiZamin\Resources\RZ_AreaParams.txt">` — the child `<Link>RZ_AreaParams.txt</Link>` and `<TargetPath>RZ_AreaParams.txt</TargetPath>` stay exactly as they are (deployed flat name must not change).

`src/KKarea.Revit/KKarea.Revit.csproj` — the five linked sources:

```xml
<Compile Include="..\RVTuk.Revit\RishuiZamin\AreaExtractor.cs" Link="Shared\AreaExtractor.cs" />
<Compile Include="..\RVTuk.Revit\RishuiZamin\UsageKeyScheduleBuilder.cs" Link="Shared\UsageKeyScheduleBuilder.cs" />
<Compile Include="..\RVTuk.Revit\RishuiZamin\ExternalEvents\AreaExtractEventHandler.cs" Link="Shared\AreaExtractEventHandler.cs" />
<Compile Include="..\RVTuk.Revit\RishuiZamin\ExternalEvents\SelectAreaEventHandler.cs" Link="Shared\SelectAreaEventHandler.cs" />
<Compile Include="..\RVTuk.Revit\RishuiZamin\ExternalEvents\SetupUsageKeysEventHandler.cs" Link="Shared\SetupUsageKeysEventHandler.cs" />
```

- [ ] **Step 6: Build all three configs**

```powershell
dotnet build RVTuk.sln -c Release2023
dotnet build RVTuk.sln -c Release2024
dotnet build RVTuk.sln -c Release2025
```
Expected: "Build succeeded." ×3 (Release2023 proves the KKarea link repointing).

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "refactor(revit): per-tool folders + namespaces in both hosts; repoint KKarea linked sources"
```

---

### Task 7: Class renames — retire the old tool names

**Files:**
- Rename: 7 files (Step 1)
- Modify: every reference via bulk identifier replaces (Step 2)

**Interfaces:**
- Consumes: the namespaces from Tasks 4–6.
- Produces: classes `RishuiZaminConfig`, `RishuiZaminExporter`, `RishuiZaminViewModel`, `RishuiZaminWindow`, `RishuiZaminCommand` (in `RVTuk.Revit.RishuiZamin.Commands` and `KKarea.Revit.Commands`), and the static property `Application.RishuiZaminWindow` (both hosts). **Not renamed:** `AreaRecord`, `AreaValidator`, `AreaExtractor`, `AreaRowViewModel`, `AreaLevelGroupViewModel`, `AreaExtractEventHandler`, `SelectAreaEventHandler`, `MarkerForm`, and the persisted `AppConfig` properties `AreaCalcOutputFolder`/`AreaCalcMarkerForm`.

- [ ] **Step 1: Rename the files (Git Bash)**

```bash
git mv src/RVTuk.Core/RishuiZamin/AreaSubmissionConfig.cs   src/RVTuk.Core/RishuiZamin/RishuiZaminConfig.cs
git mv src/RVTuk.Core/RishuiZamin/AreaSubmissionExporter.cs src/RVTuk.Core/RishuiZamin/RishuiZaminExporter.cs
git mv src/RVTuk.UI/RishuiZamin/Views/AreaSubmissionWindow.xaml    src/RVTuk.UI/RishuiZamin/Views/RishuiZaminWindow.xaml
git mv src/RVTuk.UI/RishuiZamin/Views/AreaSubmissionWindow.xaml.cs src/RVTuk.UI/RishuiZamin/Views/RishuiZaminWindow.xaml.cs
git mv src/RVTuk.UI/RishuiZamin/ViewModels/AreaSubmissionViewModel.cs src/RVTuk.UI/RishuiZamin/ViewModels/RishuiZaminViewModel.cs
git mv src/RVTuk.Revit/RishuiZamin/Commands/AreaCalcCommand.cs src/RVTuk.Revit/RishuiZamin/Commands/RishuiZaminCommand.cs
git mv src/KKarea.Revit/Commands/AreaCalcCommand.cs            src/KKarea.Revit/Commands/RishuiZaminCommand.cs
git mv tests/RVTuk.Core.Tests/RishuiZamin/AreaSubmissionExporterTests.cs tests/RVTuk.Core.Tests/RishuiZamin/RishuiZaminExporterTests.cs
```

- [ ] **Step 2: Bulk identifier replaces (PowerShell, replace helper)**

First sanity-check the blast radius: `git grep -n "AreaCalcWindow" -- "*.cs"` must show only the two hosts' `Application` static property and its usages. Then:

```powershell
Replace-InRepo "AreaSubmissionConfig"    "RishuiZaminConfig"
Replace-InRepo "AreaSubmissionExporter"  "RishuiZaminExporter"
Replace-InRepo "AreaSubmissionViewModel" "RishuiZaminViewModel"
Replace-InRepo "AreaSubmissionWindow"    "RishuiZaminWindow"
Replace-InRepo "AreaCalcCommand"         "RishuiZaminCommand"
Replace-InRepo "AreaCalcWindow"          "RishuiZaminWindow"
```

Notes: the `AreaSubmissionWindow` replace also fixes the `x:Class` in `RishuiZaminWindow.xaml`; `AreaCalcCommand` fixes both hosts' `typeof(...)` ribbon wiring; `AreaSubmissionExporter` also renames the test class `AreaSubmissionExporterTests` → `RishuiZaminExporterTests`. The ribbon `PushButtonData` id string `"AreaCalc"` and the display text `"Area\nCalc"` are not matched by any of these tokens — confirm they're untouched with `git grep -n '"AreaCalc"' -- "*.cs"`.

- [ ] **Step 3: Full sweep — no old tool names left in code**

Run: `git grep -in "areasubmission" -- "*.cs" "*.xaml" "*.csproj" "*.sln" "*.ps1"`
Expected: no output.

- [ ] **Step 4: Build + test**

```powershell
dotnet build RVTuk.sln -c Release2023
dotnet build RVTuk.sln -c Release2024
dotnet build RVTuk.sln -c Release2025
dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj
```
Expected: three "Build succeeded", all tests pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor: rename AreaSubmission*/AreaCalcCommand classes to RishuiZamin*"
```

---

### Task 8: Root docs (CLAUDE.md, VISION.md, README.md) + final sweep

**Files:**
- Modify: `CLAUDE.md`, `VISION.md`, `README.md`

**Interfaces:**
- Consumes: everything above — this task documents the end state.
- Produces: the terminology + conventions every future session reads.

- [ ] **Step 1: CLAUDE.md — terminology + naming**

After the "## Project Overview" intro paragraphs (right before the `> **Product vision...**` blockquote), insert:

```markdown
## Terminology

| Term | Meaning |
|------|---------|
| **Toolkit** | RVTuk itself: one add-in, one ribbon, one install. |
| **Tool** | A user-facing feature with its own button/pane and UI: **Family Browser**, **Rishui Zamin** (ribbon label "Area Calc"), **Auto Dimensions**, **Neo Properties**. |
| **Layer** | A C# project: `RVTuk.Core` (logic) → `RVTuk.UI` (WPF) → `RVTuk.Revit` (Revit host). A tool is a vertical slice across the layers. |
| **Host** | A project that loads into Revit: `RVTuk.Revit` (2024/25) and `KKarea.Revit` (2023, ships only the Rishui Zamin tool). |

Canonical tool names in code and folders: `FamilyBrowser`, `RishuiZamin`,
`AutoDimensions`, `NeoProperties`. Inside every project (and the test project) the
structure is one folder per tool plus `Shared/`, and **namespace = root namespace +
folder path, exactly**. A file lives in a tool folder iff only that tool uses it.
```

Then update the feature bullet: `- **Area Calc** (Rishui Zamin) — reads the Areas...` → `- **Rishui Zamin** (ribbon button "Area Calc") — reads the Areas...`, and fix the three doc links:
- `docs/autoarea/rishui-zamin-notes.md` → `docs/tools/rishui-zamin/notes.md`
- `docs/superpowers/specs/2026-07-04-auto-dimensions-design.md` → `docs/tools/auto-dimensions/specs/2026-07-04-auto-dimensions-design.md`
- `docs/superpowers/specs/2026-07-04-neo-properties-design.md` → `docs/tools/neo-properties/specs/2026-07-04-neo-properties-design.md`

- [ ] **Step 2: CLAUDE.md — architecture paths + docs convention**

In the Architecture diagram, change `src\LibraryBrowser\RVTuk.UI` → `src\RVTuk.UI`.

At the end of the "## Architecture" section, add:

```markdown
### Docs layout

Everything about one tool lives in `docs/tools/<tool>/` — `README.md` (what it is,
status, entry points), `backlog.md` (its bugs/ideas/done), living design docs, and
dated `specs/` + `plans/`. **New specs and plans go there** (toolkit-wide ones go to
`docs/toolkit/specs|plans/`) — this overrides any default location a planning skill
suggests. `docs/BACKLOG.md` holds toolkit-wide items only. `docs/future/` holds specs
for tools not yet built; `docs/archive/` holds retired/superseded docs, kept verbatim
(historical docs reference pre-reorg paths — that's intentional).
```

Also update the sentence mentioning the flag: `RegisterUnreleasedTools` flag path stays `src\RVTuk.Revit\Application.cs` (unchanged — verify). Search CLAUDE.md for any remaining `AreaSubmission`, `LibraryBrowser`, `autoarea`, or `superpowers` strings and fix each to the new path/name.

- [ ] **Step 3: VISION.md updates**

- In "The three pillars", pillar 1 status: replace the "A large "Family Explorer" enhancement batch is committed and awaiting in-Revit verification and a merge decision." sentence with "Shipped in v1 together with **Rishui Zamin** (Area Calc)."
- In the "Related docs" table, replace the `docs/superpowers/specs` & `plans` row with: `| docs/tools/<tool>/ | Per-tool README, backlog, design specs and plans |` and add `| docs/toolkit/ | Toolkit-wide specs and plans |`.
- Search VISION.md for `superpowers`, `autoarea`, `Area Calc` — update paths; keep prose "Area Calc" only where it explains the ribbon label.

- [ ] **Step 4: README.md updates**

- Feature bullet: `- 📐 **Area Calc** (Rishui Zamin) — ...` → `- 📐 **Rishui Zamin** (ribbon button "Area Calc") — ...`
- Project-structure tree: replace the `LibraryBrowser/` nesting with:

```
├── src/
│   ├── RVTuk.Core/          # Business logic per tool + Shared (SQLite, config) — no Revit/WPF deps
│   ├── RVTuk.UI/            # WPF windows and view models, one folder per tool + Shared
│   ├── RVTuk.Revit/         # Revit add-in host (ribbon, external events, commands), per-tool folders
│   └── KKarea.Revit/        # Standalone Revit 2023 host for Rishui Zamin (Area Calc) only
├── tests/
│   └── RVTuk.Core.Tests/    # xunit suite for Core, mirrors the per-tool folders
├── installer/
│   └── RVTuk.Setup/         # Standalone installer exe (built by Build-Installer.ps1)
├── docs/
│   ├── tools/<tool>/        # Per-tool README, backlog, specs, plans
│   └── toolkit/             # Toolkit-wide specs and plans
```

- [ ] **Step 5: Final repo-wide sweeps**

```bash
git grep -il "areasubmission" -- ":(exclude)docs/archive" ":(exclude)docs/tools" ":(exclude)docs/toolkit"
git grep -l "LibraryBrowser"  -- ":(exclude)docs/archive" ":(exclude)docs/tools" ":(exclude)docs/toolkit"
git grep -l "autoarea"        -- ":(exclude)docs/archive" ":(exclude)docs/tools" ":(exclude)docs/toolkit"
git grep -l "docs/superpowers" -- ":(exclude)docs/archive" ":(exclude)docs/tools" ":(exclude)docs/toolkit"
```
Expected: no output from any of the four. (Historical docs under the excluded paths legitimately keep old names.)

- [ ] **Step 6: Full build matrix + tests one last time**

```powershell
dotnet build RVTuk.sln -c Release2023
dotnet build RVTuk.sln -c Release2024
dotnet build RVTuk.sln -c Release2025
dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj
```
Expected: three "Build succeeded", all tests pass.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "docs: terminology, per-tool docs convention, updated paths in CLAUDE/VISION/README"
```

---

## Post-plan (user-run)

- Elevated `.\Deploy.ps1` for an installed Revit year, restart Revit, and smoke-test:
  both ribbon buttons open, a family loads from the browser, an Area Calc export writes
  `.dxf` + `.dat`, "Setup Usage Keys" still binds parameters. (The reorg changes no
  behaviour, so this is a confidence pass — the deployed file set must be identical
  apart from DLL-internal namespace metadata.)

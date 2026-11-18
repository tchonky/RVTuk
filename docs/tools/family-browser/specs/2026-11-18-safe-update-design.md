# Family Browser — Safe Update (design)

Date: 2026-11-18. Status: reviewed (builder check 2026-11-18); awaiting approval.
Origin: the 2026-11-17 product brainstorm (code readers + competitor research) ranked
"Safe Update" first. The version check (✓ / ↑ Update / Update All) is the browser's main
promise to project teams; today it can silently damage a project, so it gets fixed before
any new feature.

## Why

Architects must be able to press Update or Update All without wondering what it did to
their project. Today it can reset values they set, load an archived copy, act on the wrong
project, report success as failure, or hang the browser. Each of those teaches people not
to press the button.

## Goals

1. **Load / Update never resets the project's values.** (Decided: always keep. No setting,
   no prompt.)
2. **Update All loads only what the list shows as outdated**, never an ignored or duplicate
   copy, after a confirmation that lists what will change, with progress and Cancel.
3. **Badges are never about a project that isn't active.** Switching projects clears the
   comparison and asks for ⟳ (decided: no automatic re-check — it would be slow in large
   projects).
4. **Loading a family that is already current is reported as success**, not an error.
5. **A parameters-only Scan keeps each family's RevitYear.**
6. **A busy Revit never freezes the browser.** Every Family Browser request to Revit that
   has not started within a timeout is withdrawn with a clear message; one that has
   started is waited for.

## Non-goals

- A keep/reset choice for values (decided against; see Approaches).
- Automatic re-sync on project switch (decided against).
- Release notes per version, Load & Place, multi-select, type-level loading — later
  brainstorm items.
- Matching project families to library rows by anything other than name.

## Anti-goals (a failure even if it works)

- Update All loads a file that is not named in its confirmation, or a copy that is
  ignored or duplicated.
- A request the browser gave up on later runs in Revit anyway and changes the model.
- A slow but working load (Desktop Connector downloading the file) is reported as failed.
- The browser blocks Revit's main thread (the stale check must be a cheap event handler).

## Constraints

- Layering: RVTuk.UI must not reference Revit; Revit events reach the view model through
  delegates/methods wired in `RVTuk.Revit` (as `getProjectFamilies` is today).
- Threading per CLAUDE.md: Revit API only inside `ExternalEvent` handlers; handlers are
  shared singletons and `Raise()` coalesces; the VM serialises loads with `_loadLock`.
- Revit 2024–2027, net48/net8/net10. Tests run with `.\build\Test-All.ps1`.
- View-only users read a local DB copy; nothing here adds schema.
- `help.md` is fetched live from master (VM ~L537), so its text change ships when merged,
  ahead of users' installed builds — word it so it is true for both.

## Approaches considered

**Project values on Load/Update**
- A. Always keep the project's values — **chosen** by the user. Safe for project teams;
  the cost is that a value fix made in the library does not reach existing projects (the
  maintainer fixes those by hand, or the project resets in Revit's Type Properties).
- B. Always reset to the library (today) — rejected: silently loses project data.
- C. Ask each time in the Update confirmation — rejected by the user as unnecessary.

**Revit busy (timeouts)**
- A. Time out only while a request waits to start; once started, wait for it — **chosen**.
- B. One request queue with per-request completion — rejected: rewrites every handler for
  the same result.
- C. Plain timeout on every wait — rejected: abandons slow-but-working loads that Revit
  then finishes after the browser reported failure.

**Project switch**
- A. Re-check automatically — rejected by the user: a full compare per switch is slow in
  large projects (model-only previews especially).
- B. Clear the comparison and ask for ⟳ — **chosen**.

## Design

### 1. Keep project values (`LoadFamilyEventHandler`)

`OverwriteLoadOptions` → `KeepValuesLoadOptions`: `OnFamilyFound` returns `true` with
`overwriteParameterValues = false`; `OnSharedFamilyFound` keeps `source = FamilySource.Family`
(nested shared families still take the library's geometry) with
`overwriteParameterValues = false`. Applies to Load, Update and Update All alike — they
share the handler.

### 2. "Already current" (`LoadFamilyEventHandler`)

`Document.LoadFamily` returns `false` both when loading fails and (expected — **verify in
Revit first**) when an identical family is already in the project, in which case the load
options are never called. Rule: if it returns `false`, `OnFamilyFound` was not called, and
the project already has a `Family` with that file's name, report success with a flag
`AlreadyCurrent`. The VM treats it as success (row → ✓). If Revit turns out to call
`OnFamilyFound` or return `true` in that case, drop this item — nothing to fix.

### 3. Update All: the right rows, confirmed, with progress and Cancel

**Which rows.** Every outdated family in the project, whatever the search, category and
source filters show *(my call: the confirmation names each one, so nothing loads unseen;
"update only what I filtered" can come later)*. A pure Core function (e.g. `UpdatePlanner`, next to `SaveToLibraryPlanner`,
unit-tested) takes the library rows with their project verdicts plus the ignored rule and
returns: the rows to update, and the names skipped as duplicates. Rules:
- ignored rows (by `IgnoredRule` — folder and file patterns) are never candidates;
- the Sync compare also skips ignored rows, so they never get a badge or count toward
  `OutdatedCount` (fix in `Sync`, VM ~L587). Ignored rows are also left out of
  `libraryNames`, so a project family whose only library copy is ignored (e.g. archived)
  shows as a model-only ⚠ row with Save to Library *(my call: an archived copy is not
  part of the library)*;
- if more than one non-ignored library row has the same file name (any folder), none of
  them is updated. The ⚠ "N copies in the library: <paths>" (tooltip) replaces the
  verdict only when that family is in the project; the confirmation's "Skipped" list
  names only duplicates where at least one copy is newer than the project's *(my call:
  keeps a large library from flooding the dialog)*. Builder's call how the ⚠ is drawn;
  reuse the model-only ⚠ style.
- The count on the **↑ Update All (N)** button uses the same function.

**Confirmation.** Pressing Update All shows a dark-themed dialog owned by the browser:

```
Update 23 families in "Tower B.rvt"?
Project values are kept.

  Door_Single_90          v3 → v5
  Window_Casement         v1 → v2
  …                                  (scrolls)

Skipped — more than one copy in the library:
  Railing_Glass  (2 copies)

                         [ Update ]  [ Cancel ]
```

The project name comes from the last Sync (see §4). Single-row **Update** does not confirm
(one family, the user just chose it).

**Progress and Cancel.** While `LoadFamiliesSequentially` runs a batch, the footer's
"Working…" becomes "Updating 4 of 23 — Door_Single_90" with a **Cancel** link. Cancel sets
a flag checked between families (the family in progress finishes). The end report says
"Updated 4 of 23. Cancelled." plus any failures, as today. A "Revit is busy" timeout (§6)
stops the whole batch rather than waiting 10 s per remaining family *(my call)*.

**Right project only.** Each load in a confirmed batch carries the document key from the
Sync the user confirmed against (§4); the handler refuses to load ("The active project
changed") when the active document's key differs. This closes the race where a project
switch reaches the VM only after the next family has started. Single-row Load passes no
key and loads into whatever is active.

### 4. Project switch clears the comparison

- **Revit side.** `Application` subscribes once (not inside the Topo Tools block) to
  `ViewActivated` and `DocumentClosed`. If a browser window is open, the handler reads a
  cheap key of the active document (none after the last one closes) and hands it to the
  VM via the dispatcher. No Revit API work beyond reading the document's identity.
  - Document key: builder's call (e.g. `doc.GetHashCode()` of the `Document` object
    plus `PathName`, so two unsaved projects differ). A family document becoming active
    counts as a change.
- **Sync records it.** `GetProjectFamiliesEventHandler` also returns the document's key
  and title; the VM keeps the title for the confirmation dialog and the footer.
- The Revit handler runs on every activation and passes the active document's key to
  the VM; the VM decides (`ActiveDocumentChanged(key)`).
- **Another document is active** (a different project, or any family document): the VM
  *hides* the comparison — every row shows no verdict, model-only rows are hidden,
  Update All is hidden — and shows a hint in the toolbar next to ⟳: **"Another project
  is active — press ⟳ to compare."** The ⟳ button gets an accent style while the hint
  shows. Single-row **Load** keeps working (it loads into whatever is active; it reads
  "Load", since there is no verdict).
- **The compared project is active again** (e.g. back from Open in Family Editor): the
  stashed comparison is restored as it was, without a new Sync *(my call: the browser's
  own Family Editor round-trips would otherwise force ⟳ after every edit)*. A family
  loaded into the project from the Family Editor meanwhile may then show a stale badge
  until the next ⟳; accepted.
- If a batch is running when the project changes, it cancels after the current family
  (the same flag as Cancel) and the report says why.
- Footer shows the compared project's title after a Sync, e.g. "Compared with Tower B.rvt".

### 5. RevitYear kept on parameters-only scans

Today a parameters-only scan neither reads nor keeps the year, which also switches off the
"too new for this Revit" guard (`IndexingExternalEventHandler` checks `FileRevitYear > 0`)
— so Revit may try to open a library file re-saved in a newer Revit. Fix both:
- `FamilyIndexer` reads the year from the file on every parameters scan, thumbnail or not
  (a cheap OLE read; expose the existing `ThumbnailExtractor.TryReadRevitYear` path as a
  public year-only method), so the guard always has a real year;
- `IndexRepository.UpdateFamilyMetadata` writes
  `RevitYear = CASE WHEN @year > 0 THEN @year ELSE RevitYear END`, so a file whose year
  can't be read keeps the stored one.

Unit tests in `IndexRepositoryTests` and `FamilyIndexerTests`.

### 6. Requests to Revit never hang the browser

A small shared helper in `RVTuk.Revit/FamilyBrowser/ExternalEvents/` (e.g.
`RevitRequest`) used by all seven Family Browser handlers (GetFamilies, LoadFamily,
OpenFamilyEditor, EditProjectFamily, GetFamilyPreviews, Indexing, and the save-to-library
path through EditProjectFamily):

- Each `Prepare` creates a new request with state `Pending`. `Execute` atomically moves it
  `Pending → Running`; if it finds `Withdrawn` (or no request), it returns at once without
  touching the document.
- The caller, after `Raise()`, waits up to **10 s** for `Running`. If the request has not
  started, it atomically moves `Pending → Withdrawn` and throws
  `"Revit is busy (a dialog is open or a command is running). Finish it and try again."` —
  every caller already reports exception messages. If the swap fails because it just
  started, it waits for completion as now.
- Once `Running`, the caller waits without a limit (a load over Desktop Connector can
  legitimately take long).
- A `Pending` result from `Raise()` stays accepted: it means an earlier, withdrawn raise is
  still queued, and that one execution will serve the current request.
- The deep scan (`IndexLibraryCommand`, one request per family) uses the same helper; a
  withdrawn request there stops the scan with the busy message, keeping what was extracted
  (as Cancel does today).

The 10 s figure is a constant; builder may tune it after trying it in Revit.

### 7. Help text

`help.md` "Checking the project": add "Update keeps the values set in the project." and
"After you switch to another project, press ⟳ again." The first line is false for builds
already installed, and help is read live from master, so the help change merges to master
only together with (or after) the deploy of the new build.

## Testing

- Core unit tests: `UpdatePlanner` (ignored, duplicates across folders, case-insensitive
  names, model-only rows never candidates), `UpdateFamilyMetadata` year rule.
- `RevitRequest` state machine: unit-testable if it lives without Revit types — builder's
  call whether it goes in Core (`RVTuk.Core.Shared`?) with tests or stays in Revit.
- Manual in Revit 2024 and 2026 (record in the plan's checklist):
  1. change a type value of a library family in a project; bump `_Version` in the library
     copy; Update → value kept, new geometry/parameters arrive;
  2. Load a family already current → ✓, no error (verifies §2's assumption first);
  3. archive copy in an ignored folder with a higher version → not counted, not loaded;
  4. two copies in non-ignored folders → ⚠, skipped, listed;
  5. Update All with ≥5 families → confirmation, progress, Cancel mid-way;
  6. switch projects → hint, Update All hidden, model-only rows gone; switch back →
     comparison restored; Open in Family Editor and back → restored; Update All, then
     switch project mid-batch → remaining loads refused, report says why;
  7. open a modal dialog in Revit (e.g. Type Properties), press Load → busy message
     within ~10 s, and nothing loads after the dialog closes;
  8. parameters-only scan → "too new" guard still works on a newer-year file.

## Left to the builder

- Exact names of `UpdatePlanner`, `RevitRequest`, `KeepValuesLoadOptions`, the VM method.
- The document key and where it is stored.
- Dialog/hint styling within the existing dark theme.
- Whether the `RevitRequest` state machine lives in Core with tests.
- Order of work; suggested: §5 and §1 (smallest), then §6, §3, §4, §2 after verifying,
  §7 last.

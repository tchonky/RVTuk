# Family Browser — Safe Update Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Load / Update / Update All safe to trust: project values kept, only the right rows updated after a confirmation, badges never about another project, and a busy Revit never freezing the browser.

**Architecture:** Pure logic goes to Core with xunit tests (`RequestGate`, `ProjectComparer`, the RevitYear rule). The Revit layer wires the gate into the six Family Browser handlers, keeps values on load, and reports document switches. The UI view model uses the comparer, stashes the comparison on a project switch, and runs Update All with confirm / progress / Cancel.

**Tech Stack:** C#, WPF (MVVM), Revit API 2024–2027 (`ExternalEvent`), SQLite (`Microsoft.Data.Sqlite`), xunit.

**Spec:** `docs/tools/family-browser/specs/2026-11-18-safe-update-design.md` (approved 2026-11-18). Read it before each task; section numbers below (§1–§7) refer to it.

## Global Constraints

- RVTuk.UI must not reference Revit types; Revit reaches the VM only through delegates and plain methods wired in RVTuk.Revit.
- Revit API calls only inside `IExternalEventHandler.Execute`; background work on the ThreadPool; WPF updates through the Dispatcher.
- Load / Update / Update All always keep project values: `overwriteParameterValues = false` in both `IFamilyLoadOptions` callbacks; `OnSharedFamilyFound` keeps `source = FamilySource.Family`.
- Start timeout for a request to Revit: **10 s** (one constant). Once a request has started, wait without a limit.
- Busy message, verbatim: `Revit is busy (a dialog is open or a command is running). Finish it and try again.`
- Stale hint, verbatim: `Another project is active — press ⟳ to compare.`
- Batch-refused message (handler, key mismatch), verbatim: `The active project changed. Press ⟳ and try again.`
- No schema change to the shared DB.
- Builds: `Release2024` (net48), `Release2025`/`Release2026` (net8), `Release2027` (net10). Before every commit that touches `src/RVTuk.Revit` or `src/RVTuk.UI`, `dotnet build RVTuk.sln -c Release2024` and `-c Release2027` must succeed. Before the final commit, `.\build\Test-All.ps1` must pass.
- New files follow namespace = root namespace + folder path. Commits end with the session's attribution lines.

## Review Focus

1. **A withdrawn request whose event Revit runs later** must do nothing (no load, no handler state change) — and must not swallow the *next* request prepared on the same handler. Pinned in Task 2 (`Withdrawn_ThenBegin_NextTryStartServesNewRequest`, `TryStart_AfterWithdraw_ReturnsFalse`).
2. **Same file name, different case or folder** (`door.rfa` in `A\` and `Door.rfa` in `B\`) is a duplicate, not two families. Pinned in Task 4 (`Duplicate_IsCaseInsensitiveAcrossFolders`).
3. **A duplicate where one copy is ignored** is not a duplicate (the ignored copy does not count). Pinned in Task 4 (`IgnoredCopy_DoesNotMakeADuplicate`).
4. **Project switch while Update All runs** — remaining families refused, report says why, no load into the new project. Pinned by the handler key check (Task 5) and the VM cancel (Task 7); manual check 6 in Task 8.
5. **Parameters-only scan of a file whose year can't be read** keeps the stored year (no reset to 0). Pinned in Task 1 (`UpdateFamilyMetadata_YearZero_KeepsStoredYear`).

---

### Task 1: RevitYear read and kept on parameters-only scans (§5)

**Files:**
- Modify: `src/RVTuk.Core/FamilyBrowser/Extraction/ThumbnailExtractor.cs` (add a public year-only reader beside `ExtractFromRfa`, L26)
- Modify: `src/RVTuk.Core/FamilyBrowser/Extraction/FamilyIndexer.cs:162-167` (parameters path)
- Modify: `src/RVTuk.Core/FamilyBrowser/Database/IndexRepository.cs:288` (`UpdateFamilyMetadata`)
- Test: `tests/RVTuk.Core.Tests/FamilyBrowser/ThumbnailExtractorTests.cs`, `FamilyIndexerTests.cs`, `IndexRepositoryTests.cs`

**Interfaces:**
- Produces: `public static int ThumbnailExtractor.ReadRevitYear(string rfaPath)` — 0 when unreadable; same FileStream/`RootStorage.Open` pattern and catch-all as `ExtractFromRfa`.

- [ ] **Step 1: Write the failing tests**
  - `ThumbnailExtractorTests.ReadRevitYear_ReadsFormatLine_ZeroWhenMissingOrNotCompound`: reuse `WriteRfa(path)` → `Assert.Equal(2024, ReadRevitYear(path))`; `WriteRfa(path, basicFileInfo: false)` → 0; a non-compound file (as in `ExtractFromRfa_NonCompoundFile_…`) → 0 and the file can be deleted right after.
  - `FamilyIndexerTests.Scan_IncludeParametersOnly_WorkItemCarriesFileYear`: library with one file written by the same compound-file helper (Format 2024), `Scan(..., includeThumbnails: false, includeParameters: true)` → the single work item has `FileRevitYear == 2024` and `ThumbnailPng == null`. (Move `WriteRfa` to a shared internal test helper if both test classes need it.)
  - `IndexRepositoryTests.UpdateFamilyMetadata_YearZero_KeepsStoredYear`: insert a family, `UpdateFamilyMetadata(id, "Doors", [], null, revitYear: 2024)`, then again with `revitYear: 0` → stored `RevitYear` is 2024; a third call with 2025 → 2025.

- [ ] **Step 2: Run to verify they fail**
  Run: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj -c Release2025 --filter "FullyQualifiedName~ReadRevitYear|FullyQualifiedName~WorkItemCarriesFileYear|FullyQualifiedName~YearZero_KeepsStoredYear"`
  Expected: compile error (`ReadRevitYear` missing), then FAIL on the year assertions.

- [ ] **Step 3: Implement**
  - `ReadRevitYear` as above (internally `TryReadRevitYear(rootStorage)`).
  - `FamilyIndexer`: when `needsParameters && !needsThumbnail`, set `revitYear = ThumbnailExtractor.ReadRevitYear(fullPath)`.
  - `UpdateFamilyMetadata` SQL: `RevitYear = CASE WHEN @year > 0 THEN @year ELSE RevitYear END`.

- [ ] **Step 4: Run the Core suite**
  Run: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj -c Release2025`
  Expected: all PASS.

- [ ] **Step 5: Commit** — `fix(family-browser): parameters-only scans read and keep each family's Revit year`

---

### Task 2: `RequestGate` — withdraw a request Revit hasn't started (§6, Core)

**Files:**
- Create: `src/RVTuk.Core/FamilyBrowser/Util/RequestGate.cs`
- Create: `src/RVTuk.Core/FamilyBrowser/Util/RevitBusyException.cs`
- Test: `tests/RVTuk.Core.Tests/FamilyBrowser/RequestGateTests.cs`

**Interfaces:**
- Produces:
  - `public sealed class RevitBusyException : InvalidOperationException` — parameterless ctor with the busy message (Global Constraints).
  - `public sealed class RequestGate`
    - `public static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(10);`
    - `public void Begin()` — caller side, from the handler's `Prepare`: new request, state Pending; resets both signals.
    - `public bool TryStart()` — handler side, first line of `Execute`: atomically Pending → Running; `false` if Withdrawn or no request (handler must then return without doing anything).
    - `public void Complete()` — handler side, in `Execute`'s `finally`, only after `TryStart()` returned true: Running → Done, signals the waiter.
    - `public void Wait(TimeSpan startTimeout)` — caller side: waits up to `startTimeout` for Running; if not started, atomically Pending → Withdrawn and throws `RevitBusyException`; if that swap loses to `TryStart`, falls through. Then waits for Done with no limit.
  - State is one `int` changed with `Interlocked.CompareExchange`; signals are two `ManualResetEventSlim` (started, done).

- [ ] **Step 1: Write the failing tests** (short timeouts, e.g. 50 ms; worker threads via `Task.Run`)
  - `Wait_ReturnsAfterComplete_WhenStartedInTime`: Begin; a task does TryStart (true) → sleep 100 ms → Complete; `Wait(1 s)` returns and the task finished first.
  - `Wait_Throws_WhenNotStartedInTime`: Begin; `Assert.Throws<RevitBusyException>(() => Wait(50 ms))`; message equals the busy text.
  - `TryStart_AfterWithdraw_ReturnsFalse`: after the throw above, `TryStart()` is false.
  - `Withdrawn_ThenBegin_NextTryStartServesNewRequest`: withdraw as above, `Begin()` again, `TryStart()` true, `Complete()`, `Wait(50 ms)` returns.
  - `Wait_DoesNotTimeOut_OnceStarted`: Begin; TryStart; a task Completes after 300 ms; `Wait(50 ms)` returns without throwing.
  - `TryStart_WithoutBegin_ReturnsFalse`.

- [ ] **Step 2: Run to verify they fail**
  Run: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj -c Release2025 --filter FullyQualifiedName~RequestGateTests`
  Expected: compile error (types missing).

- [ ] **Step 3: Implement `RequestGate` and `RevitBusyException`** as specified above.

- [ ] **Step 4: Run tests** on net8 and net48
  Run: same filter with `-c Release2025` and `-c Release2024`. Expected: PASS.

- [ ] **Step 5: Commit** — `feat(family-browser): RequestGate withdraws a Revit request that has not started`

---

### Task 3: Every Family Browser handler uses the gate (§6, Revit)

**Files:**
- Modify: `src/RVTuk.Revit/FamilyBrowser/ExternalEvents/GetProjectFamiliesEventHandler.cs`, `LoadFamilyEventHandler.cs`, `OpenFamilyEditorEventHandler.cs`, `EditProjectFamilyEventHandler.cs`, `GetFamilyPreviewsEventHandler.cs`, `IndexingExternalEventHandler.cs`
- Modify: `src/RVTuk.Revit/FamilyBrowser/Commands/BrowseLibraryCommand.cs:240-253` (`Raise` doc comment only)

**Interfaces:**
- Consumes: `RequestGate`, `RevitBusyException` (Task 2).
- Produces: unchanged public handler API — `Prepare…`/`Reset`/`PrepareAndWait` now call `_gate.Begin()`; `WaitForCompletion()` becomes `_gate.Wait(RequestGate.StartTimeout)`; `Execute` starts with `if (!_gate.TryStart()) return;` *before* its `try`, and its `finally` calls `_gate.Complete()` instead of `_done.Set()`. `_done` fields are removed. Callers (`BrowseLibraryCommand` lambdas, `IndexLibraryCommand` loop, rescan) are unchanged: a `RevitBusyException` propagates to their existing catch blocks (scan → "Scan failed" dialog, keeps what was extracted; rescan → `(false, ex.Message)`; VM loads → failure list).

- [ ] **Step 1: Apply the pattern to the six handlers.** Keep `Raise()` accepting `Pending` (an earlier withdrawn raise still queued serves the current request); update its comment to say so.

- [ ] **Step 2: Build**
  Run: `dotnet build RVTuk.sln -c Release2024` and `dotnet build RVTuk.sln -c Release2027`
  Expected: Build succeeded, 0 errors; `grep -rn "_done" src/RVTuk.Revit/FamilyBrowser` prints nothing.

- [ ] **Step 3: Commit** — `fix(family-browser): a busy Revit no longer freezes the browser`

---

### Task 4: `ProjectComparer` — which rows are compared, duplicated, skipped (§3, Core)

**Files:**
- Modify: `src/RVTuk.Core/FamilyBrowser/Models/VersionStatus.cs` — add `DuplicateInLibrary` (last).
- Create: `src/RVTuk.Core/FamilyBrowser/Models/ProjectSnapshot.cs`
- Create: `src/RVTuk.Core/FamilyBrowser/Util/ProjectComparer.cs`
- Test: `tests/RVTuk.Core.Tests/FamilyBrowser/ProjectComparerTests.cs`, `SourceToggleFilterTests.cs`

**Interfaces:**
- Produces:
  - `public sealed class ProjectSnapshot { string DocumentKey; string Title; IReadOnlyList<ProjectFamilyInfo> Families; }` (get/set properties, non-null defaults).
  - `public sealed class LibraryEntry { long Id; string FileName; string RelativePath; string? Version; bool IsIgnored; }`
  - `public sealed class SkippedDuplicate { string Name; IReadOnlyList<string> RelativePaths; }`
  - `public sealed class ProjectComparison`
    - `IReadOnlyDictionary<long, ProjectFamilyInfo> Matches` — non-ignored, non-duplicate library rows whose family is in the project (the VM calls `item.CompareWithProject(pf)` for each).
    - `IReadOnlyDictionary<long, IReadOnlyList<string>> Duplicates` — for each non-ignored row whose name has ≥ 2 non-ignored copies **and** whose family is in the project: all those copies' relative paths (ordinal-sorted).
    - `IReadOnlyList<ProjectFamilyInfo> ModelOnly` — project families with no non-ignored library row.
    - `IReadOnlyList<SkippedDuplicate> SkippedDuplicates` — one per duplicated name in the project where `FamilyVersionCheck.IsUpdateAvailable(copy.Version, pf.Version)` holds for at least one copy.
  - `public static ProjectComparison ProjectComparer.Compare(IReadOnlyList<LibraryEntry> library, IReadOnlyList<ProjectFamilyInfo> project)` — names compared through `FamilyFileName.WithoutRfaExtension(FileName)`, `OrdinalIgnoreCase`; for duplicate project names the first occurrence wins (as today).
  - `SourceToggleFilter` needs no code change: `DuplicateInLibrary` counts as "in the model" and "in the library".

- [ ] **Step 1: Write the failing tests** (`ProjectComparerTests`)
  - `Match_SingleCopyInProject_IsMatched`
  - `IgnoredRow_IsNeverMatched_AndItsFamilyIsModelOnly` (only copy ignored → appears in `ModelOnly`, not `Matches`)
  - `IgnoredCopy_DoesNotMakeADuplicate` (one ignored + one normal copy → normal copy in `Matches`)
  - `Duplicate_IsCaseInsensitiveAcrossFolders` (`A\door.rfa`, `B\Door.rfa`, project `Door` → both ids in `Duplicates`, neither in `Matches`, paths `["A\door.rfa","B\Door.rfa"]`)
  - `Duplicate_NotInProject_IsNotReported` (no `Duplicates`, no `SkippedDuplicates`)
  - `SkippedDuplicates_OnlyWhenACopyIsNewer` (project v3; copies v3 and v5 → listed; copies v3 and v2 → not listed)
  - `FamilyNotInLibrary_IsModelOnly`
  - `DuplicateProjectNames_FirstOccurrenceWins`
  - `SourceToggleFilterTests.DuplicateInLibrary_CountsAsInModelAndInLibrary`

- [ ] **Step 2: Run to verify they fail**
  Run: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj -c Release2025 --filter "FullyQualifiedName~ProjectComparerTests|FullyQualifiedName~SourceToggleFilterTests"`
  Expected: compile error, then FAIL.

- [ ] **Step 3: Implement** the models and `Compare` as specified.

- [ ] **Step 4: Run the Core suite** — `-c Release2025`, all PASS.

- [ ] **Step 5: Commit** — `feat(family-browser): ProjectComparer skips ignored rows and flags duplicate copies`

---

### Task 5: Load keeps values, reports "already current", refuses the wrong project (§1, §2, §3)

**Files:**
- Create: `src/RVTuk.Revit/FamilyBrowser/ExternalEvents/ProjectKey.cs`
- Modify: `src/RVTuk.Revit/FamilyBrowser/ExternalEvents/LoadFamilyEventHandler.cs`
- Modify: `src/RVTuk.Revit/FamilyBrowser/ExternalEvents/GetProjectFamiliesEventHandler.cs`
- Modify: `src/RVTuk.Revit/FamilyBrowser/Commands/BrowseLibraryCommand.cs:60-79` (two delegates)
- Modify: `src/RVTuk.UI/FamilyBrowser/Views/FamilyBrowserWindow.xaml.cs:37-49`, `src/RVTuk.UI/FamilyBrowser/ViewModels/FamilyBrowserViewModel.cs:36-37,362-382` (delegate types only; call sites pass `null` key for now)

**Interfaces:**
- Consumes: `ProjectSnapshot` (Task 4).
- Produces:
  - `internal static class ProjectKey { public static string Of(Document doc) }` → `(doc.IsFamilyDocument ? "F|" : "P|") + doc.PathName + "|" + doc.Title`.
  - `LoadFamilyEventHandler.Prepare(string familyPath, string? expectedDocumentKey)`; `public const string ProjectChangedMessage` = the batch-refused text. In `Execute`, after the family-document guard: if `expectedDocumentKey != null && ProjectKey.Of(doc) != expectedDocumentKey` → `ErrorMessage = ProjectChangedMessage`, return.
  - Load options class `KeepValuesLoadOptions` (Global Constraints) with `public bool FamilyFoundCalled` set in both callbacks. After `LoadFamily` returns `false`: if `!options.FamilyFoundCalled` and the document has a `Family` whose `Name` equals `FamilyFileName.WithoutRfaExtension(Path.GetFileName(FamilyPath))` (`OrdinalIgnoreCase`) → `Success = true` (already current), no error.
  - `GetProjectFamiliesEventHandler.Result` becomes `ProjectSnapshot` (`DocumentKey = ProjectKey.Of(doc)`, `Title = doc.Title`); empty snapshot when no document.
  - Delegate types: `Func<ProjectSnapshot> getProjectFamilies`; `Func<string, string?, (bool Success, string? Error)> loadFamily` (path, expected document key).

- [ ] **Step 1: Implement** the above; VM `Sync` reads `snapshot.Families` where it read the list (no behaviour change yet).

- [ ] **Step 2: Build** — `dotnet build RVTuk.sln -c Release2024` and `-c Release2027`; 0 errors.

- [ ] **Step 3: Manual check in Revit 2024** (record result in the commit message body): load a library family already current in a project → no "Revit did not load" error; change a type value, bump `_Version` in the library copy, Load → value kept, new geometry arrives.

- [ ] **Step 4: Commit** — `fix(family-browser): Load keeps project values and treats an unchanged family as current`

---

### Task 6: Sync uses the comparer; duplicates and compared project shown (§3, §4)

**Files:**
- Modify: `src/RVTuk.UI/FamilyBrowser/ViewModels/FamilyBrowserViewModel.cs:558-668` (`Sync`)
- Modify: `src/RVTuk.UI/FamilyBrowser/ViewModels/FamilyBrowserItemViewModel.cs`
- Modify: `src/RVTuk.UI/FamilyBrowser/Views/FamilyBrowserWindow.xaml` (row ⚠ for duplicates in both list and grid templates; footer text)

**Interfaces:**
- Consumes: `ProjectComparer.Compare`, `LibraryEntry`, `ProjectSnapshot` (Task 4); `IgnoredRule()` (VM L453).
- Produces:
  - Item VM: `public void MarkDuplicate(ProjectFamilyInfo loaded, IReadOnlyList<string> relativePaths)` → `VersionStatus = DuplicateInLibrary`; `public bool ShowDuplicate`; `public string? DuplicateToolTip` = `"{n} copies in the library:\n" + paths joined by "\n"`. `Refresh` keeps a duplicate row duplicate (does not call `CompareWithProject` for it).
  - VM: `private string? _comparedDocumentKey`; `public string? ComparedProjectTitle` (footer: `Compared with {Title}` when set, in the existing muted style, hidden while busy); `private ProjectComparison? _lastComparison`.
  - `Sync` builds `LibraryEntry` rows from the re-read items with `IsIgnored = ignored(item)`, calls `Compare`, applies `Matches` / `Duplicates`, adds `ModelOnly` rows as today (previews unchanged), and sets `OutdatedCount` = items with `UpdateAvailable`. `UpdateAll()` keeps selecting `UpdateAvailable` rows — duplicates and ignored rows never carry that status now.

- [ ] **Step 1: Implement.** Reuse the model-only ⚠ glyph style for `ShowDuplicate`, tooltip bound to `DuplicateToolTip`.
- [ ] **Step 2: Build** — Release2024 and Release2027, 0 errors; `dotnet test tests\RVTuk.UI.Tests\RVTuk.UI.Tests.csproj -c Release2025` PASS.
- [ ] **Step 3: Commit** — `fix(family-browser): the project check skips ignored copies and flags duplicates`

---

### Task 7: Update All — confirmation, progress, Cancel, right project (§3)

**Files:**
- Create: `src/RVTuk.UI/FamilyBrowser/Views/UpdateAllConfirmWindow.xaml` + `.xaml.cs` (dark theme, owned by the browser)
- Modify: `src/RVTuk.UI/FamilyBrowser/ViewModels/FamilyBrowserViewModel.cs` (`UpdateAll`, `LoadFamiliesSequentially`, L681-728)
- Modify: `src/RVTuk.UI/FamilyBrowser/Views/FamilyBrowserWindow.xaml` (footer progress + Cancel link)

**Interfaces:**
- Consumes: `_lastComparison.SkippedDuplicates`, `_comparedDocumentKey`, `ComparedProjectTitle` (Task 6); `loadFamily(path, key)` (Task 5); `RevitBusyException` (Task 2).
- Produces:
  - `public static bool UpdateAllConfirmWindow.Ask(Window? owner, string projectTitle, IReadOnlyList<(string Name, string? From, string? To)> updates, IReadOnlyList<SkippedDuplicate> skipped)` — layout and copy per the spec's §3 sketch: title `Update {n} families in "{title}"?`, subtitle `Project values are kept.`, scrolling list `Name   v{from} → v{to}`, a "Skipped — more than one copy in the library:" section only when `skipped` is non-empty, buttons **Update** / **Cancel**.
  - VM: `public string? BatchProgressText` (`Updating {i} of {n} — {name}`), `public ICommand CancelBatchCommand`, `private volatile bool _cancelBatch`, `private string? _batchStopReason`.
  - `LoadFamiliesSequentially(items, string? expectedDocumentKey)`: Update All passes `_comparedDocumentKey`, single-row Load passes `null`. Checks `_cancelBatch` before each family. On `RevitBusyException`: record it, stop the batch. On error text equal to the handler's project-changed message: stop the batch. End report when stopped early: `Updated {done} of {n}. {reason}` plus failures (reasons: `Cancelled.`, the busy text, `Stopped: another project became active.`).
  - The project-changed text is duplicated in UI as a constant (UI can't see the Revit const); name it `ProjectChangedMessage` in the VM and note the twin in both places.

- [ ] **Step 1: Implement** the window and VM changes. Single-row Update does not confirm.
- [ ] **Step 2: Build** — Release2024 and Release2027, 0 errors.
- [ ] **Step 3: Commit** — `feat(family-browser): Update All confirms, shows progress and can be cancelled`

---

### Task 8: Switching projects hides the comparison; returning restores it (§4)

**Files:**
- Create: `src/RVTuk.Revit/FamilyBrowser/ProjectSwitchWatcher.cs`
- Modify: `src/RVTuk.Revit/Application.cs` (subscribe next to the Family Browser handler creation, L126-137 — not inside the Topo Tools block)
- Modify: `src/RVTuk.UI/FamilyBrowser/ViewModels/FamilyBrowserViewModel.cs`
- Modify: `src/RVTuk.UI/FamilyBrowser/Views/FamilyBrowserWindow.xaml` (hint next to ⟳, accent ⟳ style)

**Interfaces:**
- Consumes: `ProjectKey.Of` (Task 5); `_comparedDocumentKey`, `_cancelBatch` (Tasks 6–7).
- Produces:
  - `internal static class ProjectSwitchWatcher { public static void Register(UIControlledApplication app) }` — subscribes `ViewActivated` (→ `vm.ActiveDocumentChanged(ProjectKey.Of(e.Document))`) and `ControlledApplication.DocumentClosing` (→ `vm.DocumentClosing(ProjectKey.Of(e.Document))`), each only when `Application.BrowserWindow != null`, posted with `Dispatcher.BeginInvoke`, wrapped in try/catch → `Application.LogDiagnostic`.
  - VM: `public void ActiveDocumentChanged(string documentKey)`, `public void DocumentClosing(string documentKey)`, `public bool IsComparisonHidden` (drives the hint and the ⟳ accent).
    - Key ≠ `_comparedDocumentKey` (and a comparison exists, not hidden): stash each row's `VersionStatus` and the model-only rows, set library rows to `None`, remove model-only rows, `OutdatedCount = 0`, `IsComparisonHidden = true`; if a batch is running set `_cancelBatch` with reason `Stopped: another project became active.`
    - Key = `_comparedDocumentKey` while hidden: restore the stash, recount, `IsComparisonHidden = false`.
    - `DocumentClosing` of the compared key: drop stash and comparison, `_comparedDocumentKey = null`, `IsComparisonHidden = true` if a comparison was shown.
    - A successful `Sync` clears the stash and sets `IsComparisonHidden = false`.

- [ ] **Step 1: Implement.**
- [ ] **Step 2: Build** — Release2024 and Release2027, 0 errors.
- [ ] **Step 3: Commit** — `feat(family-browser): switching projects hides the comparison until ⟳`

---

### Task 9: Docs, full test run, manual checklist (§7)

**Files:**
- Modify: `docs/tools/family-browser/help.md` ("Checking the project": add `Update keeps the values set in the project.` and `After you switch to another project, press ⟳ again.`; Update All confirmation and Cancel in one line each)
- Modify: `docs/tools/family-browser/design.md` (§1a Load/Update, §3 version check: keep values, duplicates, stale comparison, busy timeout — add a *Deviations* entry dated 2026-11-18)
- Modify: `docs/tools/family-browser/backlog.md` (Done entry "Safe Update" linking the spec; remove nothing else)

- [ ] **Step 1: Update the three docs.** help.md must merge to master only with or after the deploy (spec §7) — say so in the PR description.
- [ ] **Step 2: Full tests** — `.\build\Test-All.ps1`. Expected: all suites PASS on net48, net8, net10.
- [ ] **Step 3: Manual checks in Revit 2024 and 2026** (spec "Testing" list, items 1–8) after `.\Deploy.ps1 2024 2026`; tick each in the PR description.
- [ ] **Step 4: Commit** — `docs(family-browser): Safe Update in help, design and backlog`

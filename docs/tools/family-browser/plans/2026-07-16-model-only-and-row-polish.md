# Family Browser — Model-Only Row Options & Row Polish Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the first four Family Browser backlog improvements: (1) ⚠️ model-only flag, (2) model-only row actions — Open in Family Editor from the model + a Save to Library button, (3) two-line name truncation with ellipsis, (4) `_Version` value shown next to the row flags.

**Architecture:** Pure decision logic (target path for Save to Library, file-name safety) goes in `RVTuk.Core` with xunit tests. A new `EditProjectFamilyEventHandler` in `RVTuk.Revit` wraps `Document.EditFamily` + `SaveAs` (save into the library, or save to a temp file + `OpenAndActivateDocument` to open the editor). The UI receives it as two new `Func<>` delegates — the UI project never sees Revit types. XAML-only changes cover the flag glyph, name trimming, and version display.

**Tech Stack:** C# multi-target net48/net8.0-windows, WPF (MVVM), Revit API 2024/2025 (`ExternalEvent` ping-pong), xunit.

## Global Constraints

- `RVTuk.Core` must stay free of Revit API and WPF types.
- `RVTuk.UI` must not reference any Revit type — Revit interactions arrive as `Func<>`/`Action` delegates from `RVTuk.Revit`.
- Never use `System.IO.Path` on model-only family names — Revit allows `"` and `/` in family names, which throw/truncate in net48 path APIs (`FamilyFileName` doc comment).
- Namespaces = root namespace + folder path, exactly.
- Revit API calls only inside `IExternalEventHandler.Execute`; UI waits via `ManualResetEventSlim` ping-pong on a background thread.
- Build configs are `Release2024`/`Release2025` (no plain Debug/Release).
- **No git commits in this session** — the working tree already carries unrelated uncommitted work from a previous session; committing is left to the user. (Deviation from the usual commit-per-task cadence, documented here on purpose.)

---

### Task 1: `FamilyFileName.IsSafeFileName` (Core)

Model-only family names come from Revit and may contain characters Windows forbids in file names. Both the Save to Library planner and the open-from-model path must reject those up front with a clear message instead of throwing deep inside `Path.Combine`/`SaveAs`.

**Files:**
- Modify: `src\RVTuk.Core\FamilyBrowser\Util\FamilyFileName.cs`
- Test: `tests\RVTuk.Core.Tests\FamilyBrowser\FamilyFileNameTests.cs`

**Interfaces:**
- Produces: `public static bool FamilyFileName.IsSafeFileName(string? name)` — true iff `name` is non-blank and contains no character Windows forbids in file names.

- [x] **Step 1: Write the failing tests** — append to the existing `FamilyFileNameTests` class:

```csharp
[Theory]
[InlineData("Plain Door")]
[InlineData("Door 90x210 (fire rated)")]
[InlineData("Дверь-Δ £")]                    // non-ASCII is fine
public void IsSafeFileName_AcceptsOrdinaryNames(string name)
    => Assert.True(FamilyFileName.IsSafeFileName(name));

[Theory]
[InlineData("24\" Door")]    // inch mark — the CER 2026-07-16 crash character
[InlineData("A/B Door")]
[InlineData("A\\B Door")]
[InlineData("Door: wide")]
[InlineData("Door?")]
[InlineData("Door*")]
[InlineData("Door<1>")]
[InlineData("Door|x")]
[InlineData("Door\t")]       // control char
[InlineData("")]
[InlineData("   ")]
[InlineData(null)]
public void IsSafeFileName_RejectsIllegalOrBlankNames(string? name)
    => Assert.False(FamilyFileName.IsSafeFileName(name));
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj --filter IsSafeFileName`
Expected: FAIL to compile — `IsSafeFileName` not defined.

- [x] **Step 3: Implement** — add to `FamilyFileName`:

```csharp
// The invalid set is hard-coded (not Path.GetInvalidFileNameChars) so net48 and net8
// agree: net48's list is a superset and both contain all of these.
private const string InvalidFileNameChars = "\"<>|:*?\\/";

/// <summary>
/// True iff <paramref name="name"/> can be used as a Windows file name as-is:
/// non-blank and free of characters Windows forbids. Family names that fail this
/// (Revit allows " and / in them) cannot be saved to or edited via an .rfa file.
/// </summary>
public static bool IsSafeFileName(string? name)
{
    if (string.IsNullOrWhiteSpace(name)) return false;
    foreach (var c in name!)
        if (c < ' ' || InvalidFileNameChars.IndexOf(c) >= 0) return false;
    return true;
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj --filter IsSafeFileName`
Expected: PASS (15 test cases).

---

### Task 2: `SaveToLibraryPlanner` (Core)

Decides where in the library a model-only family should be saved: the folder where most same-category families already live; otherwise a (created) folder named after the category; otherwise the library root.

**Files:**
- Create: `src\RVTuk.Core\FamilyBrowser\Util\SaveToLibraryPlanner.cs`
- Test: `tests\RVTuk.Core.Tests\FamilyBrowser\SaveToLibraryPlannerTests.cs`

**Interfaces:**
- Consumes: `FamilyFileName.IsSafeFileName` (Task 1).
- Produces:
  `public static (string? RelativePath, string? Error) SaveToLibraryPlanner.Plan(string familyName, string? category, IEnumerable<(string? Category, string RelativePath)> libraryItems)`
  — `RelativePath` is library-root-relative and ends in `familyName + ".rfa"`; exactly one of the two tuple fields is non-null.

- [x] **Step 1: Write the failing tests**

```csharp
using System;
using RVTuk.Core.FamilyBrowser.Util;
using Xunit;

namespace RVTuk.Core.Tests.FamilyBrowser
{
    public class SaveToLibraryPlannerTests
    {
        private static (string? Category, string RelativePath)[] Items(params (string? c, string p)[] items)
            => Array.ConvertAll(items, i => ((string?)i.c, i.p));

        [Fact]
        public void Plan_UsesMostCommonFolderOfSameCategory()
        {
            var items = Items(("Doors", @"05_Doors\A.rfa"), ("Doors", @"05_Doors\B.rfa"),
                              ("Doors", @"Misc\C.rfa"), ("Windows", @"06_Windows\D.rfa"));
            var (path, error) = SaveToLibraryPlanner.Plan("New Door", "Doors", items);
            Assert.Null(error);
            Assert.Equal(@"05_Doors\New Door.rfa", path);
        }

        [Fact]
        public void Plan_CategoryCompareIsCaseInsensitive()
        {
            var items = Items(("doors", @"05_Doors\A.rfa"));
            var (path, _) = SaveToLibraryPlanner.Plan("New Door", "Doors", items);
            Assert.Equal(@"05_Doors\New Door.rfa", path);
        }

        [Fact]
        public void Plan_TieBreaksOnOrdinalFolderName()
        {
            var items = Items(("Doors", @"B_Doors\A.rfa"), ("Doors", @"A_Doors\B.rfa"));
            var (path, _) = SaveToLibraryPlanner.Plan("New Door", "Doors", items);
            Assert.Equal(@"A_Doors\New Door.rfa", path);
        }

        [Fact]
        public void Plan_HandlesForwardSlashSeparators()
        {
            var items = Items(("Doors", "05_Doors/Interior/A.rfa"));
            var (path, _) = SaveToLibraryPlanner.Plan("New Door", "Doors", items);
            Assert.Equal(@"05_Doors/Interior\New Door.rfa", path);
        }

        [Fact]
        public void Plan_NoSameCategoryItems_FallsBackToCategoryNamedFolder()
        {
            var items = Items(("Windows", @"06_Windows\D.rfa"));
            var (path, error) = SaveToLibraryPlanner.Plan("New Door", "Doors", items);
            Assert.Null(error);
            Assert.Equal(@"Doors\New Door.rfa", path);
        }

        [Fact]
        public void Plan_SanitizesCategoryFolderName()
        {
            var (path, _) = SaveToLibraryPlanner.Plan("Fitting", "Pipe/Duct: Special",
                Items(("Doors", @"05_Doors\A.rfa")));
            Assert.Equal(@"PipeDuct Special\Fitting.rfa", path);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("///")] // sanitises to empty
        public void Plan_NoUsableCategory_SavesAtLibraryRoot(string? category)
        {
            var (path, error) = SaveToLibraryPlanner.Plan("New Door", category, Items());
            Assert.Null(error);
            Assert.Equal("New Door.rfa", path);
        }

        [Fact]
        public void Plan_RootDwellingCategoryWinsOverSubfolder()
        {
            var items = Items(("Doors", "A.rfa"), ("Doors", "B.rfa"), ("Doors", @"Misc\C.rfa"));
            var (path, _) = SaveToLibraryPlanner.Plan("New Door", "Doors", items);
            Assert.Equal("New Door.rfa", path); // most common folder is the root
        }

        [Fact]
        public void Plan_RejectsIllegalFamilyName()
        {
            var (path, error) = SaveToLibraryPlanner.Plan("24\" Door", "Doors", Items());
            Assert.Null(path);
            Assert.NotNull(error);
            Assert.Contains("file name", error, StringComparison.OrdinalIgnoreCase);
        }
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj --filter SaveToLibraryPlanner`
Expected: FAIL to compile — `SaveToLibraryPlanner` not defined.

- [x] **Step 3: Implement `SaveToLibraryPlanner`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RVTuk.Core.FamilyBrowser.Util
{
    /// <summary>
    /// Picks the library-relative target path for saving a model-only family into the
    /// library: the folder where most same-category families already live, else a folder
    /// named after the (sanitised) category, else the library root. Pure string logic —
    /// no disk access, and no System.IO.Path on family names (see FamilyFileName).
    /// </summary>
    public static class SaveToLibraryPlanner
    {
        public static (string? RelativePath, string? Error) Plan(
            string familyName,
            string? category,
            IEnumerable<(string? Category, string RelativePath)> libraryItems)
        {
            if (!FamilyFileName.IsSafeFileName(familyName))
                return (null, $"The family name '{familyName}' contains characters that are not " +
                              "allowed in Windows file names. Rename the family in the project first.");

            var fileName = familyName + ".rfa";
            var dir = MostCommonCategoryFolder(category, libraryItems) ?? SanitizeFolderName(category);
            return (dir.Length == 0 ? fileName : dir + "\\" + fileName, null);
        }

        // The folder holding the most same-category families (ties break on ordinal name so
        // the result is deterministic). Null when the category has no indexed families.
        private static string? MostCommonCategoryFolder(
            string? category, IEnumerable<(string? Category, string RelativePath)> libraryItems)
        {
            if (string.IsNullOrWhiteSpace(category)) return null;
            var dirs = libraryItems
                .Where(i => string.Equals(i.Category, category, StringComparison.OrdinalIgnoreCase))
                .Select(i => FolderOf(i.RelativePath))
                .GroupBy(d => d, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.Ordinal)
                .FirstOrDefault();
            return dirs?.Key;
        }

        // Directory part of a library-relative path, tolerating both separators.
        private static string FolderOf(string relativePath)
        {
            int cut = Math.Max(relativePath.LastIndexOf('\\'), relativePath.LastIndexOf('/'));
            return cut < 0 ? string.Empty : relativePath.Substring(0, cut);
        }

        // A category name becomes a folder name by dropping forbidden characters entirely
        // (folder names are not identity-critical, unlike the family file name).
        private static string SanitizeFolderName(string? category)
        {
            if (string.IsNullOrWhiteSpace(category)) return string.Empty;
            var sb = new StringBuilder(category!.Length);
            foreach (var c in category)
                if (c >= ' ' && "\"<>|:*?\\/".IndexOf(c) < 0) sb.Append(c);
            return sb.ToString().Trim();
        }
    }
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj --filter SaveToLibraryPlanner`
Expected: PASS (11 test cases). Also run the full suite once: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj` — expected all green.

---

### Task 3: `EditProjectFamilyEventHandler` (Revit host)

One handler for both model-only actions. `Prepare(familyName, saveAsPath)`:
- `saveAsPath != null` → **Save to Library**: `doc.EditFamily` → `SaveAs(saveAsPath)` → close the family doc.
- `saveAsPath == null` → **Open in Family Editor from the model**: `doc.EditFamily` → `SaveAs` into `%TEMP%\RVTuk\FamilyEdit\<name>.rfa` → close → `OpenAndActivateDocument(temp)`. (An in-memory `EditFamily` document has no UI window and cannot be activated directly — the temp-file round-trip is the standard workaround. The temp file carries the family's exact name, so "Load into Project" from the editor updates the same family.)

**Files:**
- Create: `src\RVTuk.Revit\FamilyBrowser\ExternalEvents\EditProjectFamilyEventHandler.cs`
- Modify: `src\RVTuk.Revit\Application.cs` (register handler + event, next to `OpenFamilyEditorHandler`)

**Interfaces:**
- Produces (used by Task 4's delegates in `BrowseLibraryCommand`):
  - `void Prepare(string familyName, string? saveAsPath)`
  - `void WaitForCompletion()`
  - `bool Success { get; }` / `string? ErrorMessage { get; }`
  - Statics on `Application`: `EditProjectFamilyHandler`, `EditProjectFamilyEvent`.

- [x] **Step 1: Write the handler**

```csharp
using System;
using System.IO;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RVTuk.Revit.FamilyBrowser.ExternalEvents
{
    /// <summary>
    /// Edits a family that lives only in the open project (no library .rfa behind it).
    /// With a SaveAsPath: saves it there (Save to Library). Without: saves it to a temp
    /// .rfa named exactly after the family and opens that in the Family Editor — an
    /// in-memory EditFamily document has no UI window, so activating it requires the
    /// temp-file round-trip; "Load into Project" from the editor still updates the same
    /// family because Revit matches families by name.
    /// </summary>
    public class EditProjectFamilyEventHandler : IExternalEventHandler
    {
        private readonly ManualResetEventSlim _done = new(false);

        public string? FamilyName { get; private set; }
        public string? SaveAsPath { get; private set; }
        public bool Success { get; private set; }
        public string? ErrorMessage { get; private set; }

        public void Prepare(string familyName, string? saveAsPath)
        {
            FamilyName = familyName;
            SaveAsPath = saveAsPath;
            Success = false;
            ErrorMessage = null;
            _done.Reset();
        }

        public void WaitForCompletion() => _done.Wait();

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null || string.IsNullOrEmpty(FamilyName))
                {
                    ErrorMessage = "No active document.";
                    return;
                }

                var family = new FilteredElementCollector(doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .FirstOrDefault(f => string.Equals(f.Name, FamilyName, StringComparison.Ordinal));
                if (family == null)
                {
                    ErrorMessage = $"Family '{FamilyName}' was not found in the project.";
                    return;
                }
                if (family.IsInPlace)
                {
                    ErrorMessage = "In-place families cannot be edited or saved as .rfa files.";
                    return;
                }
                if (!family.IsEditable)
                {
                    ErrorMessage = "This family is not editable.";
                    return;
                }

                var target = SaveAsPath ?? Path.Combine(
                    Path.GetTempPath(), "RVTuk", "FamilyEdit", FamilyName + ".rfa");

                var famDoc = doc.EditFamily(family);
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    famDoc.SaveAs(target, new SaveAsOptions { OverwriteExistingFile = true });
                }
                finally
                {
                    // Close before OpenAndActivateDocument: after SaveAs this doc IS the
                    // file at 'target', and Revit refuses to open a path twice.
                    famDoc.Close(false);
                }

                if (SaveAsPath == null)
                    app.OpenAndActivateDocument(target);

                Success = true;
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
            finally
            {
                _done.Set();
            }
        }

        public string GetName() => "RVTuk.EditProjectFamilyEventHandler";
    }
}
```

- [x] **Step 2: Register in `Application.cs`** — next to the `OpenFamilyEditorHandler` lines:

```csharp
public static EditProjectFamilyEventHandler EditProjectFamilyHandler { get; private set; } = null!;
public static ExternalEvent EditProjectFamilyEvent { get; private set; } = null!;
```

and in `OnStartup` beside the other `ExternalEvent.Create` calls:

```csharp
EditProjectFamilyHandler = new EditProjectFamilyEventHandler();
EditProjectFamilyEvent   = ExternalEvent.Create(EditProjectFamilyHandler);
```

- [x] **Step 3: Verify it compiles**

Run: `dotnet build src\RVTuk.Revit\RVTuk.Revit.csproj -c Release2024`
Expected: Build succeeded. (Task 4 wires it to the UI; nothing calls it yet.)

---

### Task 4: UI plumbing — delegates, `SaveToLibraryCommand`, model-only editor path, version property

**Files:**
- Modify: `src\RVTuk.UI\FamilyBrowser\ViewModels\FamilyBrowserItemViewModel.cs` (add `Version`/`HasVersion`)
- Modify: `src\RVTuk.UI\FamilyBrowser\ViewModels\FamilyBrowserViewModel.cs` (two new delegates, `SaveToLibraryCommand`, model-only branch in `OpenInFamilyEditor`)
- Modify: `src\RVTuk.UI\FamilyBrowser\Views\FamilyBrowserWindow.xaml.cs` (pass-through)
- Modify: `src\RVTuk.Revit\FamilyBrowser\Commands\BrowseLibraryCommand.cs` (create the delegates)

**Interfaces:**
- Consumes: `Application.EditProjectFamilyHandler`/`Event` (Task 3), `SaveToLibraryPlanner.Plan` + `FamilyFileName.IsSafeFileName` (Tasks 1–2).
- Produces (used by Task 5's XAML):
  - `FamilyBrowserItemViewModel.Version : string?` and `HasVersion : bool`
  - `FamilyBrowserViewModel.SaveToLibraryCommand : ICommand`
  - `OpenFamilyEditorCommand` now enabled for model-only rows.
- New VM/Window constructor tail (both):
  `(..., Action<string> openInFamilyEditor, Func<string, (bool Success, string? Error)> openModelFamilyInEditor, Func<string, string, (bool Success, string? Error)> saveFamilyToLibrary, ...)` — `openModelFamilyInEditor` takes the family name; `saveFamilyToLibrary` takes (family name, full target path).

- [x] **Step 1: Item VM** — add below `Tags` in `FamilyBrowserItemViewModel`:

```csharp
// Value of the _Version shared parameter — from the library index for indexed rows,
// from the loaded family's symbols for model-only rows. Shown next to the row flags.
public string? Version => Model.Version;
public bool HasVersion => !string.IsNullOrWhiteSpace(Model.Version);
```

- [x] **Step 2: Browser VM** — in `FamilyBrowserViewModel`:

Fields + constructor params (after `_openInFamilyEditor` / `openInFamilyEditor`):

```csharp
private readonly Func<string, (bool Success, string? Error)> _openModelFamilyInEditor;
private readonly Func<string, string, (bool Success, string? Error)> _saveFamilyToLibrary;
```

```csharp
Action<string> openInFamilyEditor,
Func<string, (bool Success, string? Error)> openModelFamilyInEditor,
Func<string, string, (bool Success, string? Error)> saveFamilyToLibrary,
Action? onLibraryFolderChanged = null)
```

with assignments `_openModelFamilyInEditor = openModelFamilyInEditor;` and `_saveFamilyToLibrary = saveFamilyToLibrary;`.

Busy flag (next to `IsRescanning`):

```csharp
private bool _isSavingToLibrary;
public bool IsSavingToLibrary
{
    get => _isSavingToLibrary;
    set => SetProperty(ref _isSavingToLibrary, value);
}
```

Command declaration (next to `OpenFamilyEditorCommand`): `public ICommand SaveToLibraryCommand { get; }`

Command wiring — replace the `OpenFamilyEditorCommand` line and add `SaveToLibraryCommand`:

```csharp
// Open-in-editor works for model-only rows too (via EditFamily on the loaded family).
OpenFamilyEditorCommand= new RelayCommand(OpenInFamilyEditor, () => SelectedItem != null);
SaveToLibraryCommand   = new RelayCommand(SaveToLibrary,
    () => SelectedItem != null && SelectedItem.IsModelOnly && !IsSavingToLibrary);
```

`OpenInFamilyEditor` — insert a model-only branch at the top of the existing method, before `var fullPath = ...`:

```csharp
private void OpenInFamilyEditor()
{
    var item = SelectedItem;
    if (item == null) return;
    if (item.IsModelOnly)
    {
        // No .rfa behind this row: edit the family loaded in the model. The handler
        // detours through a temp file named exactly after the family, which requires
        // a path-legal name.
        var name = item.DisplayName;
        if (!FamilyFileName.IsSafeFileName(name))
        {
            MessageBox.Show(
                $"'{name}' can't be opened for editing from here because its name contains " +
                "characters Windows doesn't allow in file names. Rename the family in the " +
                "project first.", "RVTuk", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        ThreadPool.QueueUserWorkItem(_ =>
        {
            string? error = null;
            try
            {
                // _loadLock: EditProjectFamilyHandler is a shared singleton (also used by
                // Save to Library) and ExternalEvent.Raise() coalesces — serialize.
                lock (_loadLock)
                {
                    var (ok, err) = _openModelFamilyInEditor(name);
                    if (!ok) error = err ?? "Unknown error.";
                }
            }
            catch (Exception ex) { error = ex.Message; }
            if (error != null)
                _dispatcher.Invoke(() =>
                    MessageBox.Show($"Could not open family editor: {error}", "RVTuk"));
        });
        return;
    }
    var fullPath = Path.Combine(_config.LibraryFolderPath, item.RelativePath);
    ... // existing body unchanged
}
```

`SaveToLibrary` — new method after `OpenInFamilyEditor`:

```csharp
// Saves a model-only family's .rfa into the library folder (most common folder of its
// category, else a folder named after the category, else the root). The DB is not
// touched — reconciling disk with the index is the Scan's job, so the row stays
// "model only" until the next Scan picks the new file up.
private void SaveToLibrary()
{
    var item = SelectedItem;
    if (item == null || !item.IsModelOnly) return;

    var libraryItems = _allItems.Where(i => !i.IsModelOnly)
        .Select(i => (i.Category, i.RelativePath));
    var (relativePath, error) = SaveToLibraryPlanner.Plan(item.DisplayName, item.Category, libraryItems);
    if (error != null)
    {
        MessageBox.Show(error, "RVTuk", MessageBoxButton.OK, MessageBoxImage.Warning);
        return;
    }

    var fullPath = Path.Combine(_config.LibraryFolderPath, relativePath!);
    if (File.Exists(fullPath))
    {
        var answer = MessageBox.Show(
            $"{fullPath} already exists (not indexed yet). Overwrite it?",
            "RVTuk — Save to Library", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;
    }

    var name = item.DisplayName;
    IsSavingToLibrary = true;
    ThreadPool.QueueUserWorkItem(_ =>
    {
        bool ok;
        string? err;
        try
        {
            lock (_loadLock) // shared handler with the model-only editor path — serialize
            {
                (ok, err) = _saveFamilyToLibrary(name, fullPath);
            }
        }
        catch (Exception ex) { ok = false; err = ex.Message; }
        // Same guard as Sync: Invoke rethrows delegate exceptions on this pool thread,
        // where anything unhandled kills the whole Revit process.
        try
        {
            _dispatcher.Invoke(() =>
            {
                IsSavingToLibrary = false;
                if (ok)
                    MessageBox.Show(
                        $"Saved to:\n{fullPath}\n\nIt will appear in the library after the next Scan.",
                        "RVTuk — Save to Library", MessageBoxButton.OK, MessageBoxImage.Information);
                else
                    MessageBox.Show($"Could not save to library: {err}", "RVTuk",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
            });
        }
        catch
        {
            _dispatcher.BeginInvoke(new Action(() => IsSavingToLibrary = false));
        }
    });
}
```

- [x] **Step 3: Window pass-through** — `FamilyBrowserWindow.xaml.cs`: add the two fields, constructor parameters (after `openInFamilyEditor`), assignments, and pass them to the VM in `LoadWithConfig`:

```csharp
private readonly Func<string, (bool Success, string? Error)> _openModelFamilyInEditor;
private readonly Func<string, string, (bool Success, string? Error)> _saveFamilyToLibrary;
```

```csharp
var vm = new FamilyBrowserViewModel(
    config, repo, _getProjectFamilies, _loadFamily, _rescanFamily,
    _scan, _openInFamilyEditor, _openModelFamilyInEditor, _saveFamilyToLibrary,
    onLibraryFolderChanged: ReloadConfig);
```

- [x] **Step 4: Revit-side delegates** — `BrowseLibraryCommand.Execute`, after the `openInFamilyEditor` lambda:

```csharp
// Both wrap the same EditProjectFamilyEventHandler singleton; the VM serializes the
// two call sites with a lock, same as loads.
Func<string, (bool Success, string? Error)> openModelFamilyInEditor = familyName =>
{
    Application.EditProjectFamilyHandler.Prepare(familyName, null);
    Application.EditProjectFamilyEvent.Raise();
    Application.EditProjectFamilyHandler.WaitForCompletion();
    return (Application.EditProjectFamilyHandler.Success, Application.EditProjectFamilyHandler.ErrorMessage);
};

Func<string, string, (bool Success, string? Error)> saveFamilyToLibrary = (familyName, targetPath) =>
{
    Application.EditProjectFamilyHandler.Prepare(familyName, targetPath);
    Application.EditProjectFamilyEvent.Raise();
    Application.EditProjectFamilyHandler.WaitForCompletion();
    return (Application.EditProjectFamilyHandler.Success, Application.EditProjectFamilyHandler.ErrorMessage);
};
```

and extend the window construction:

```csharp
var window = new FamilyBrowserWindow(config, getProjectFamilies, loadFamily, rescanFamily,
    scan, openInFamilyEditor, openModelFamilyInEditor, saveFamilyToLibrary);
```

- [x] **Step 5: Verify both configs compile**

Run: `dotnet build RVTuk.sln -c Release2024` then `dotnet build RVTuk.sln -c Release2025`
Expected: Build succeeded, no warnings about missing members.

---

### Task 5: XAML — ⚠️ flag, name trimming, version text, Save to Library button, hidden model-only buttons

**Files:**
- Modify: `src\RVTuk.UI\FamilyBrowser\Views\FamilyBrowserWindow.xaml`

**Interfaces:**
- Consumes: `Version`/`HasVersion`, `SaveToLibraryCommand` (Task 4).

- [x] **Step 1: Row list template** — give the row grid five columns (version gets its own), trim the name to two lines with ellipsis + full-name tooltip, show the version, and replace the "Model only" chip with ⚠️:

```xml
<Grid Margin="6,3">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="52"/>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="Auto"/>
        <ColumnDefinition Width="Auto"/>
        <ColumnDefinition Width="Auto"/>
    </Grid.ColumnDefinitions>
    <Image Grid.Column="0" Source="{Binding Thumbnail}" Width="48" Height="48" Stretch="Uniform"/>
    <StackPanel Grid.Column="1" Margin="6,0,0,0" VerticalAlignment="Center">
        <!-- Two lines max, then ellipsis; the tooltip carries the full name. -->
        <TextBlock Text="{Binding DisplayName}" FontWeight="SemiBold"
                   TextWrapping="Wrap" TextTrimming="CharacterEllipsis" MaxHeight="32"
                   ToolTip="{Binding DisplayName}"/>
        <TextBlock Text="{Binding Category}"
                   Foreground="{StaticResource Brush.TextMuted}" FontSize="10"/>
    </StackPanel>
    <TextBlock Grid.Column="2" Text="{Binding Version}"
               Foreground="{StaticResource Brush.TextMuted}" FontSize="10"
               VerticalAlignment="Center" Margin="4,0,0,0"
               ToolTip="Family version (_Version parameter)"
               Visibility="{Binding HasVersion, Converter={StaticResource BoolVis}}"/>
    <TextBlock Grid.Column="3" Text="&#x2713;" Foreground="{StaticResource Brush.Success}"
               VerticalAlignment="Center" Margin="4,0,2,0"
               Visibility="{Binding ShowUpToDate, Converter={StaticResource BoolVis}}"/>
    <Border Grid.Column="3" Background="{StaticResource Brush.Accent}" CornerRadius="3"
            Padding="4,1" Margin="4,0,2,0"
            Visibility="{Binding ShowUpdateAvailable, Converter={StaticResource BoolVis}}">
        <TextBlock Text="&#x2191; Update" Foreground="White" FontSize="10"/>
    </Border>
    <TextBlock Grid.Column="3" Text="&#x26A0;&#xFE0F;" FontSize="12"
               VerticalAlignment="Center" Margin="4,0,2,0"
               ToolTip="Loaded in the project but not found in the library"
               Visibility="{Binding IsModelOnly, Converter={StaticResource BoolVis}}"/>
    <Button Grid.Column="4" ...favourite button unchanged... />
</Grid>
```

- [x] **Step 2: Card grid template** — in the top-right flags `StackPanel`, add the version before ✓ and replace the "Model only" chip:

```xml
<Border Background="#66000000" CornerRadius="3" Padding="4,1" Margin="0,0,2,0"
        VerticalAlignment="Center"
        ToolTip="Family version (_Version parameter)"
        Visibility="{Binding HasVersion, Converter={StaticResource BoolVis}}">
    <TextBlock Text="{Binding Version}" Foreground="{StaticResource Brush.TextMuted}" FontSize="10"/>
</Border>
```

and instead of the bordered "Model only" chip:

```xml
<Border Background="#66000000" CornerRadius="3" Padding="4,1" Margin="0,0,2,0"
        VerticalAlignment="Center"
        ToolTip="Loaded in the project but not found in the library"
        Visibility="{Binding IsModelOnly, Converter={StaticResource BoolVis}}">
    <TextBlock Text="&#x26A0;&#xFE0F;" FontSize="11"/>
</Border>
```

- [x] **Step 3: Detail pane** — prefix the model-only notice with the same glyph (`Text="&#x26A0;&#xFE0F; Loaded in the project but not found in the library"`); add the Save to Library button right after the Load/Update button:

```xml
<!-- Save to Library — the model-only counterpart of Load: writes the family from the
     model into the library folder so the library can be grown from the project. -->
<Button Grid.Column="2" VerticalAlignment="Center" Margin="14,0,0,0"
        Padding="16,7" FontWeight="SemiBold" Content="Save to Library"
        Command="{Binding SaveToLibraryCommand}"
        ToolTip="Save this family from the model into the library folder"
        Visibility="{Binding SelectedItem.IsModelOnly, Converter={StaticResource BoolVis}, FallbackValue=Collapsed}"/>
```

and hide the Rescan and Edit Info buttons for model-only rows (they act on the .rfa / DB row, which don't exist — a permanently disabled button just looks broken). Give each this style:

```xml
<Button.Style>
    <Style TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
        <Style.Triggers>
            <!-- No .rfa / DB row behind a model-only family: nothing to rescan / edit. -->
            <DataTrigger Binding="{Binding SelectedItem.IsModelOnly}" Value="True">
                <Setter Property="Visibility" Value="Collapsed"/>
            </DataTrigger>
        </Style.Triggers>
    </Style>
</Button.Style>
```

The Open in Family Editor button keeps no trigger — it now works for model-only rows too. Update its tooltip to `"Open in Family Editor (model-only families open from the model)"`.

- [x] **Step 4: Verify both configs compile**

Run: `dotnet build RVTuk.sln -c Release2024` then `dotnet build RVTuk.sln -c Release2025`
Expected: Build succeeded.

---

### Task 6: Docs + final verification

**Files:**
- Modify: `docs\tools\family-browser\backlog.md` (move the four items to ✅ Done with a one-line summary each)
- Modify: `docs\tools\family-browser\design.md` (model-only section: new actions; row template: version + trimming)

- [x] **Step 1: Backlog** — remove the four improvement bullets, append to ✅ Done:

```markdown
- [x] **Model-only row polish** (4 backlog items): flag is now ⚠️ (was a bordered chip);
  long names trim to 2 lines with ellipsis + full-name tooltip; the `_Version` value shows
  next to the row flags; model-only rows got real actions — **Open in Family Editor** edits
  the family from the model (EditFamily → temp .rfa → activate), and a **Save to Library**
  button (replacing Load) writes the .rfa into the folder where most same-category families
  live (else `<category>\`, created on demand; else the root — `SaveToLibraryPlanner`).
  The saved file is indexed by the next Scan (refresh stays read-only by design).
```

- [x] **Step 2: design.md** — update the model-only / row-template passages to match (exact edits depend on current text; keep it factual and short).

- [x] **Step 3: Full verification**

Run, expecting all green / Build succeeded:
```powershell
dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj
dotnet build RVTuk.sln -c Release2024
dotnet build RVTuk.sln -c Release2025
dotnet build RVTuk.sln -c Release2023
```
(2023 builds Core/UI/KKarea — KKarea doesn't host the Family Browser, but Core/UI changes must still compile for it.)

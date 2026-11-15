# Add-in split, phase 0 (Prep) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Get the single RVTuk add-in ready for the split: fix the two Family Browser instruction bugs, merge `topo-convert` and `round-positions`, delete the dead setup command, record the inventory in the spec, and tag `before-split`.

**Architecture:** The instruction fix is a pure string rewrite in Core (`InstructionXamlRewriter`), called by `RichTextBoxHelper` on every read and write, so the database always holds one fixed declaration whatever assembly the helper lives in. A parse failure becomes a visible notice, and the editor view model refuses to overwrite a stored row it could not read. A new WPF test project runs on net48 and net8 and makes each runtime parse markup the other one wrote.

**Tech Stack:** C# (net48 + net8.0-windows), WPF `XamlReader`/`XamlWriter`, xunit 2, PowerShell, Python 3 (one fixture script), git.

**Spec:** `docs/toolkit/specs/2026-11-12-addin-split-design.md` — phase 0 row of "Phases", "Family Browser specifics", "Frozen identifiers".

## Precondition

**`master` contains PR #12** (`kkarea-2026-2027`, commit `0f294aa`: KKarea builds and deploys for Revit 2026 and 2027). The spec was written against master as it was on 2026-11-12, before that work existed, so where the spec says "three configs" this plan uses **five** (`Release2023` … `Release2027`; `Release2027` needs the .NET 10 SDK). Merging the PR is Daniel's step; Task 1 checks it and stops if it has not happened.

## Global Constraints

- **No real instruction text or images in the repository** (it is read anonymously, treat it as public). Fixtures are the real rows' markup with the text replaced and each image replaced by a 1×1 PNG. Real rows never touch a file on disk.
- The declaration written to the database is exactly `clr-namespace:RVTuk.UI.Shared.Controls;assembly=RVTuk.UI`; the System declaration is written as `assembly=mscorlib`.
- No frozen identifier changes: no GUID, schema name, pane id, settings key, button name, `_DP-*` name or `RVTuk.db`.
- Namespace = root namespace + folder path. Core has no WPF or Revit types; UI has no Revit types.
- Run `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj` before committing Core changes.
- Everything lands on `master` in small commits. `CLAUDE.md`/`README.md` change in the same commit as what they describe. **No push without Daniel's OK.** Commit messages end with `Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>`.
- net48 tests: no .NET-Core-only string overloads. **SQLite does not load in a net48 xunit test host** (probed on 2026-11-15: `e_sqlite3.dll` stays under `runtimes\win-x64\native`), so a test that opens a database is net8-only (`#if !NETFRAMEWORK`).
- Do not touch the `AppConfig` code comments about `DataContractJsonSerializer` that arrive with the two branches; the spec settles them in phase 2.
- Steps marked **[Daniel]** need Revit or an administrator shell. An agent stops there and asks him.
- All commands run from the worktree root `D:\Coding\RVTuk-split` (Task 1) in PowerShell.

## Review Focus

Inputs the spec implies but does not spell out. Each has a test in the task named.

1. Instruction text that itself contains something looking like a declaration (`clr-namespace:RVTuk.UI.Controls;assembly=RVTuk.UI` typed into a paragraph) must be shown unchanged — Task 2.
2. A tags-only edit on a family whose stored instructions cannot be read must save the tags and leave the stored text byte-identical — Task 5.
3. `XamlWriter` failing on Save must not write `NULL` over the stored text (today it does) — Task 5.
4. An image whose stored bytes are not valid base64 must not hide the text around it — Task 4.
5. An old row carries its hover style inline; opening and saving it must drop the style and keep the text and the image — Task 4.

## Facts established while planning (2026-11-15)

- PR #12 was merged on 2026-11-15: `origin/master` = `c6d4990`. The local `master` is still `3fcbe3a` until Task 1 fast-forwards it. `family-browser-fixes` is already in master. The main checkout `D:\Coding\RVTuk` is on `kkarea-2026-2027`: `0f294aa` (the PR) plus `363f4e8` (the spec, not pushed).
- PR #12 changes no file under `tests/`, `src/RVTuk.Revit/` or the Family Browser folders, so everything this plan says about the instruction code holds on both.
- Simulated on top of `0f294aa`: `topo-convert` (14 commits) merges cleanly; `round-positions` (38 commits) then conflicts in one hunk of `src/RVTuk.Revit/Application.cs`.
- Test methods (`[Fact]`/`[Theory]`): 343 with PR #12; after both merges 426, before this plan's own tests.
- In `RVTuk.sln`, `RVTuk.Revit` and `RVTuk.Core.Tests` have an `ActiveCfg` line without `Build.0` for `Release2023`, `Release2026` and `Release2027` (they are not built in those configurations).
- The database has three instruction rows. Two have one image each and declare `xmlns:rvtuic="clr-namespace:RVTuk.UI.Controls;assembly=RVTuk.UI"` and `xmlns:s="clr-namespace:System;assembly=mscorlib"`; their `Image` elements carry `Tag` and `ToolTip` attributes. The third is an empty document.
- `InstructionsEditorViewModel.ExecuteSave` has one caller (`InstructionsEditorWindow.xaml.cs` `Save_Click`); `RichTextBoxHelper.ParseDocumentXaml` has one caller (`OnDocumentXamlChanged`).

## File structure

| File | Responsibility |
|---|---|
| `src/RVTuk.Core/FamilyBrowser/Util/InstructionXamlRewriter.cs` (new) | The two string rewrites, no WPF |
| `tests/RVTuk.Core.Tests/FamilyBrowser/InstructionXamlRewriterTests.cs` (new) | Rewrite rules |
| `src/RVTuk.UI/Shared/Controls/RichTextBoxHelper.cs` | Calls the rewriter on read/write; reports a parse failure; does not serialise the hover style |
| `src/RVTuk.UI/FamilyBrowser/ViewModels/InstructionsEditorViewModel.cs` | Knows the stored text was unreadable; refuses to overwrite it |
| `src/RVTuk.UI/FamilyBrowser/Views/InstructionsEditorWindow.xaml(.cs)` | Locks the editor and says why; reports a failed save |
| `tests/RVTuk.UI.Tests/` (new project) | `Sta.cs`, `FamilyBrowser/*Tests.cs`, `FamilyBrowser/Fixtures/*.txt` + the sanitizer script |
| `RVTuk.sln`, `CLAUDE.md` | New project, new test commands, Round Positions |
| `docs/toolkit/specs/2026-11-12-addin-split-design.md` | "Phase 0 record" section |

---

### Task 1: Workspace, baseline, backup

**Files:**
- Create: worktree `D:\Coding\RVTuk-split` on `master`
- Commit: `docs/toolkit/specs/2026-11-12-addin-split-design.md`, `docs/toolkit/plans/2026-11-15-addin-split-phase-0-prep.md`

- [ ] **Step 1: Create the worktree** (the main checkout stays on its branch for the other session)

```powershell
git -C D:\Coding\RVTuk fetch origin
git -C D:\Coding\RVTuk worktree add D:\Coding\RVTuk-split master
Set-Location D:\Coding\RVTuk-split
git merge --ff-only origin/master
git merge-base --is-ancestor 0f294aa HEAD; $LASTEXITCODE
```
Expected: `On branch master`, and the last line prints `0` (PR #12 is in master). If it prints `1`: **stop** — the precondition is not met; ask Daniel whether PR #12 can be merged now. Read any other new commit on master before going on.

- [ ] **Step 2: Bring the spec and this plan onto master**

```powershell
if (-not (Test-Path docs\toolkit\specs\2026-11-12-addin-split-design.md)) { git cherry-pick 363f4e8 }
Copy-Item D:\Coding\RVTuk\docs\toolkit\plans\2026-11-15-addin-split-phase-0-prep.md docs\toolkit\plans\
git add docs/toolkit/plans/2026-11-15-addin-split-phase-0-prep.md
git commit -m "docs(toolkit): add-in split phase 0 plan"
git diff --no-index docs\toolkit\specs\2026-11-12-addin-split-design.md D:\Coding\RVTuk\docs\toolkit\specs\2026-11-12-addin-split-design.md
```
The spec is commit `363f4e8` on `kkarea-2026-2027`; it reaches master with the PR if it was pushed first, otherwise through the cherry-pick. Expected: the last command prints nothing (the two copies are identical). If it prints a difference, the other session has edited the spec since: ask it which copy is current. From here the copy on master is the one to edit; leave a note in the shared notes folder saying so.

- [ ] **Step 3: Record the baseline**

```powershell
dotnet build RVTuk.sln -c Release2023
dotnet build RVTuk.sln -c Release2024
dotnet build RVTuk.sln -c Release2025
dotnet build RVTuk.sln -c Release2026
dotnet build RVTuk.sln -c Release2027
dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj
(git grep -h -E '^\s*\[(Fact|Theory)' -- tests | Measure-Object).Count
```
Expected: five builds with 0 errors; all tests pass; the last line prints `343`. Write down the "Passed" total of `dotnet test`.

- [ ] **Step 4: Back up the family database**

```powershell
$lib = (Get-Content "$env:APPDATA\Autodesk\Revit\Addins\RVTuk\config.json" -Raw | ConvertFrom-Json).LibraryFolderPath
New-Item -ItemType Directory -Force "$env:LOCALAPPDATA\RVTuk\backup" | Out-Null
Copy-Item "$lib\.DB\RVTuk.db" "$env:LOCALAPPDATA\RVTuk\backup\RVTuk-before-phase0.db"
```
Expected: the backup file exists and has the same length as the original.

---

### Task 2: The rewrite function (Core)

**Files:**
- Create: `src/RVTuk.Core/FamilyBrowser/Util/InstructionXamlRewriter.cs`
- Test: `tests/RVTuk.Core.Tests/FamilyBrowser/InstructionXamlRewriterTests.cs`

**Interfaces:**
- Produces (namespace `RVTuk.Core.FamilyBrowser.Util`):

```csharp
public static class InstructionXamlRewriter
{
    public const string StoredNamespace = "RVTuk.UI.Shared.Controls";
    public const string StoredAssembly  = "RVTuk.UI";
    public static string? ForRead(string? xaml, string helperNamespace, string helperAssembly);
    public static string? ForWrite(string? xaml, string helperNamespace, string helperAssembly);
}
```

Rules. Every rule matches a **whole attribute value including both double quotes**, and only **inside an element tag** (between `<` and the next `>`), never in text between tags.

| Function | From | To |
|---|---|---|
| `ForRead` | `"clr-namespace:RVTuk.UI.Controls;assembly=RVTuk.UI"` | `"clr-namespace:{helperNamespace};assembly={helperAssembly}"` |
| `ForRead` | `"clr-namespace:RVTuk.UI.Shared.Controls;assembly=RVTuk.UI"` | same |
| `ForRead`, `ForWrite` | `;assembly=System.Private.CoreLib"` (any namespace before it) | `;assembly=mscorlib"` |
| `ForWrite` | `"clr-namespace:{helperNamespace};assembly={helperAssembly}"` | `"clr-namespace:RVTuk.UI.Shared.Controls;assembly=RVTuk.UI"` |

`null` and `""` are returned as they are.

- [ ] **Step 1: Write the failing tests**

Shared test values: `const string P = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";`, `Ns = "RVTuk.UI.FamilyBrowser.Controls"`, `Asm = "RVTuk.FamilyBrowser.UI"` (the post-split home, to prove nothing is hard-wired), and a helper `Doc(string declarations, string body = "<Paragraph />")` returning `<FlowDocument xmlns="{P}" {declarations}>{body}</FlowDocument>`.

| Test | Input → assertion |
|---|---|
| `ForRead_OldControlsNamespace_BecomesTheHelpers` | `Doc("xmlns:rvtuic=\"clr-namespace:RVTuk.UI.Controls;assembly=RVTuk.UI\"")` → equals `Doc("xmlns:rvtuic=\"clr-namespace:RVTuk.UI.FamilyBrowser.Controls;assembly=RVTuk.FamilyBrowser.UI\"")` |
| `ForRead_StoredForm_BecomesTheHelpers` | same with `RVTuk.UI.Shared.Controls;assembly=RVTuk.UI` |
| `ForRead_StoredForm_IsUnchangedForTodaysHelper` | `ForRead(x, StoredNamespace, StoredAssembly)` equals `x` |
| `ForRead_CoreLib_BecomesMscorlib` | `xmlns:s="clr-namespace:System;assembly=System.Private.CoreLib"` → `xmlns:s="clr-namespace:System;assembly=mscorlib"` |
| `ForWrite_HelpersDeclaration_BecomesTheStoredForm` | helper's `Ns`/`Asm` declaration → `clr-namespace:RVTuk.UI.Shared.Controls;assembly=RVTuk.UI` |
| `ForWrite_CoreLib_BecomesMscorlib` | as for read |
| `ForWrite_ThenForRead_GivesTheOriginalBack` | `ForRead(ForWrite(x, Ns, Asm), Ns, Asm)` equals `x` |
| `LookAlikeNamesAreLeftAlone` | `"clr-namespace:RVTuk.UI.ControlsX;assembly=RVTuk.UI"` and `"clr-namespace:RVTuk.UI.Controls;assembly=RVTuk.UI.Other"` unchanged by `ForRead` |
| `TextBetweenTagsIsNeverRewritten` (Review Focus 1) | body `<Paragraph>xmlns:a="clr-namespace:RVTuk.UI.Controls;assembly=RVTuk.UI" and assembly=System.Private.CoreLib"</Paragraph>` → body unchanged by `ForRead` and by `ForWrite` |
| `NullAndEmptyComeBackAsTheyAre` | `null` → `null`, `""` → `""`, for both functions |

- [ ] **Step 2: Run them — expect a compile failure**

Run: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj --filter InstructionXamlRewriter`
Expected: FAIL, `InstructionXamlRewriter` does not exist.

- [ ] **Step 3: Implement `InstructionXamlRewriter`**

Approach: one `Regex.Replace(xaml, "<[^<>]+>", tag => …)` whose evaluator applies plain `string.Replace` calls to the tag text. No XML parser (the stored bytes outside the declarations must not change).

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj`
Expected: all pass, 10 more test methods than the baseline.

- [ ] **Step 5: Commit**

```powershell
git add src/RVTuk.Core/FamilyBrowser/Util/InstructionXamlRewriter.cs tests/RVTuk.Core.Tests/FamilyBrowser/InstructionXamlRewriterTests.cs
git commit -m "feat(family-browser): rewrite instruction XAML declarations on read and write"
```

---

### Task 3: WPF test project and fixtures

**Files:**
- Create: `tests/RVTuk.UI.Tests/RVTuk.UI.Tests.csproj`, `Sta.cs`
- Create: `tests/RVTuk.UI.Tests/FamilyBrowser/InstructionFixtures.cs`, `InstructionFixtureWriter.cs`, `InstructionFixtureTests.cs`
- Create: `tests/RVTuk.UI.Tests/FamilyBrowser/Fixtures/sanitize_instructions.py`, `legacy-net48-1.txt`, `legacy-net48-2.txt`, `raw-net48.txt`, `raw-net8.txt`
- Modify: `RVTuk.sln`, `CLAUDE.md` ("Tests" section)

Fixtures use the `.txt` extension on purpose: a `.xaml` file in a WPF project is compiled as a page.

**Interfaces:**
- Produces (namespace `RVTuk.UI.Tests` / `RVTuk.UI.Tests.FamilyBrowser`): `Sta.Run(Action test)`; `InstructionFixtures.OnePixelPng`, `.Runtime` (`"net48"` or `"net8"`), `.Read(string name)`, `.SampleDocument()`, `.HoverStyle()`, `.Images(DependencyObject root)`.

- [ ] **Step 1: Create the project** (pattern: `D:\Coding\KKompare\tests\KKompare.UI.Tests\KKompare.UI.Tests.csproj`)

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <!-- WPF tests without Revit: -c Release2024 tests net48; -c Release2025 tests net8. -->
  <PropertyGroup>
    <RootNamespace>RVTuk.UI.Tests</RootNamespace>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <PlatformTarget>x64</PlatformTarget>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <Configurations>Release2024;Release2025</Configurations>
    <TargetFramework>net8.0-windows</TargetFramework>
  </PropertyGroup>

  <PropertyGroup Condition="'$(Configuration)'=='Release2024'">
    <TargetFramework>net48</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\RVTuk.Core\RVTuk.Core.csproj" />
    <ProjectReference Include="..\..\src\RVTuk.UI\RVTuk.UI.csproj" />
  </ItemGroup>

  <ItemGroup>
    <Content Include="FamilyBrowser\Fixtures\*.txt">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Add it to the solution**

Run `dotnet sln RVTuk.sln add tests\RVTuk.UI.Tests\RVTuk.UI.Tests.csproj --solution-folder tests`, then replace the `ProjectConfigurationPlatforms` lines it generated for the new project GUID with this mapping, for each platform `P` of `Any CPU`, `x64`, `x86`:

| Solution configuration | `ActiveCfg` | `Build.0` |
|---|---|---|
| `Release2024\|P` | `Release2024\|Any CPU` | yes |
| `Release2025\|P` | `Release2025\|Any CPU` | yes |
| `Release2023\|P` | `Release2024\|Any CPU` | no line |
| `Release2026\|P`, `Release2027\|P` | `Release2025\|Any CPU` | no line |
| `Debug\|P`, `Release\|P` | `Release2025\|Any CPU` | yes |

This is the pattern `RVTuk.Revit` already uses in the same file for the years it does not build.

- [ ] **Step 3: `Sta.cs`**

`internal static class Sta { public static void Run(Action test) }` — runs `test` on a new STA thread, joins it (30 s limit, `TimeoutException`), and rethrows its exception with `ExceptionDispatchInfo`. Every test that touches a WPF object goes through it.

- [ ] **Step 4: `InstructionFixtures.cs`**

```csharp
internal static class InstructionFixtures
{
    // A valid 1x1 PNG. The sanitizer script writes the same bytes into the legacy fixtures.
    public const string OnePixelPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

#if NETFRAMEWORK
    public const string Runtime = "net48";
#else
    public const string Runtime = "net8";
#endif

    public static string Read(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "FamilyBrowser", "Fixtures", name));

    // The shape the editor produces: text, then an image inside a hover-styled border.
    public static FlowDocument SampleDocument()
    {
        var image = new Image { Width = 40, Stretch = Stretch.Uniform };
        RichTextBoxHelper.SetImageData(image, OnePixelPng);
        var paragraph = new Paragraph(new Run("Sample text"));
        paragraph.Inlines.Add(new InlineUIContainer(new Border { Style = HoverStyle(), Child = image }));
        return new FlowDocument(paragraph);
    }

    // InstructionsEditorWindow.xaml's "ImageHoverBorder", built in code.
    public static Style HoverStyle()
    {
        var style = new Style(typeof(Border));
        style.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2)));
        style.Setters.Add(new Setter(Border.BorderBrushProperty, Brushes.Transparent));
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BorderBrushProperty,
            new SolidColorBrush(Color.FromRgb(0x4A, 0x90, 0xD9))));
        style.Triggers.Add(hover);
        return style;
    }

    public static List<Image> Images(DependencyObject root);   // every Image in the logical tree
}
```

- [ ] **Step 5: `InstructionFixtureWriter.cs`** — a tool that runs as a test

`[Fact] public void WritesThisRuntimesFixtures()`: returns at once unless the environment variable `RVTUK_FIXTURE_DIR` is set. Otherwise, on an STA thread, writes `XamlWriter.Save(InstructionFixtures.SampleDocument())` to `{RVTUK_FIXTURE_DIR}\raw-{Runtime}.txt` as UTF-8 without BOM and without a trailing newline. This is exactly what today's build stores (hover style serialised).

- [ ] **Step 6: Write the raw fixtures on both runtimes**

```powershell
$env:RVTUK_FIXTURE_DIR = "$PWD\tests\RVTuk.UI.Tests\FamilyBrowser\Fixtures"
dotnet test tests\RVTuk.UI.Tests\RVTuk.UI.Tests.csproj -c Release2024 --filter InstructionFixtureWriter
dotnet test tests\RVTuk.UI.Tests\RVTuk.UI.Tests.csproj -c Release2025 --filter InstructionFixtureWriter
Remove-Item Env:RVTUK_FIXTURE_DIR
Select-String -Path tests\RVTuk.UI.Tests\FamilyBrowser\Fixtures\raw-*.txt -Pattern 'assembly=[\w.]+' -AllMatches | ForEach-Object { "$($_.Filename): $($_.Matches.Value -join ' ')" }
```
Expected: `raw-net48.txt` shows `assembly=RVTuk.UI assembly=mscorlib`; `raw-net8.txt` shows `assembly=RVTuk.UI assembly=System.Private.CoreLib`. If `raw-net8.txt` has no CoreLib declaration, stop: bug 2 is not what the spec says.

- [ ] **Step 7: The sanitizer script** `tests/RVTuk.UI.Tests/FamilyBrowser/Fixtures/sanitize_instructions.py`

```python
"""Writes sanitized copies of the stored image instructions as test fixtures.

Opens the family database read-only. Real text and images never reach a file:
only sanitized markup is written, and a row that still holds anything
unexpected stops the script. Run: python -I sanitize_instructions.py
"""
import json, os, pathlib, re, sqlite3

ONE_PIXEL_PNG = ("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA"
                 "60e6kgAAAABJRU5ErkJggg==")
OLD_DECLARATION = "clr-namespace:RVTuk.UI.Controls;assembly=RVTuk.UI"
HIDDEN = {"Tag": "sample", "ToolTip": "sample", "Text": "Sample text",
          "NavigateUri": "https://example.com/"}
MARKUP_VALUE = re.compile(r"#[0-9A-Fa-f]{6,8}|[-\d.,]+|True|False")


def sanitize(xaml):
    xaml = re.sub(r'ImageData="[^"]*"', f'ImageData="{ONE_PIXEL_PNG}"', xaml)
    for name, value in HIDDEN.items():
        xaml = re.sub(rf'(?<=\s){name}="[^"]*"', f'{name}="{value}"', xaml)

    def text(match):
        value = match.group(1)
        keep = not value.strip() or MARKUP_VALUE.fullmatch(value)
        return match.group(0) if keep else ">Sample text<"

    return re.sub(r">([^<]+)<", text, xaml)


config_path = os.path.expandvars(r"%APPDATA%\Autodesk\Revit\Addins\RVTuk\config.json")
library = json.load(open(config_path, encoding="utf-8-sig"))["LibraryFolderPath"]
database = pathlib.Path(library, ".DB", "RVTuk.db")
connection = sqlite3.connect(database.as_uri() + "?mode=ro", uri=True)
rows = connection.execute(
    "SELECT Id, FileName, InstructionsXaml FROM Families "
    "WHERE InstructionsXaml LIKE ? ORDER BY Id", (f"%{OLD_DECLARATION}%",)).fetchall()

here = pathlib.Path(__file__).parent
for number, (family_id, file_name, xaml) in enumerate(rows, 1):
    clean = sanitize(xaml)
    assert OLD_DECLARATION in clean
    assert all(ord(c) < 128 for c in clean), "non-ASCII text left: add its attribute to HIDDEN"
    assert len(clean) < 6000, "something large is left: an image or a long text"
    target = here / f"legacy-net48-{number}.txt"
    target.write_text(clean, encoding="utf-8", newline="")
    print(f"{target.name}  <- family {family_id}  {file_name}  ({len(xaml)} -> {len(clean)} chars)")
print(len(rows), "rows")
```

- [ ] **Step 8: Run it and read the result**

Run: `python -I tests\RVTuk.UI.Tests\FamilyBrowser\Fixtures\sanitize_instructions.py`
Expected: `2 rows`, and two files named with their family (the names are printed to the console only — note them for Step 12). **Open both `.txt` files and read them to the end**: the only words are `Sample text` and `sample`, every `ImageData` is the 1×1 constant. Anything else: fix the script, do not edit the file by hand.

- [ ] **Step 9: `InstructionFixtureTests.cs`** — the fixtures are what they claim to be

| Test | Assertion |
|---|---|
| `LegacyFixtures_DeclareTheOldNamespace` (`[Theory]`, `legacy-net48-1.txt`, `legacy-net48-2.txt`) | contains `xmlns:rvtuic="clr-namespace:RVTuk.UI.Controls;assembly=RVTuk.UI"` |
| `LegacyFixtures_HoldNoRealImage` (same two) | every `ImageData="…"` value equals `OnePixelPng`; file length < 6000 |
| `RawNet8Fixture_DeclaresCoreLib` | `raw-net8.txt` contains `assembly=System.Private.CoreLib` |
| `RawNet48Fixture_DeclaresMscorlib` | `raw-net48.txt` contains `assembly=mscorlib` and not `System.Private.CoreLib` |

- [ ] **Step 10: Run on both runtimes**

```powershell
dotnet test tests\RVTuk.UI.Tests\RVTuk.UI.Tests.csproj -c Release2024
dotnet test tests\RVTuk.UI.Tests\RVTuk.UI.Tests.csproj -c Release2025
dotnet build RVTuk.sln -c Release2023
dotnet build RVTuk.sln -c Release2027
```
Expected: both test runs pass (the header line names `net48`, then `net8.0-windows`); the 2023 and 2027 builds succeed and do not build `RVTuk.UI.Tests`.

- [ ] **Step 11: `CLAUDE.md`, "Tests" section** — add after the Core paragraph:

> `tests\RVTuk.UI.Tests` holds WPF tests that run without Revit (instruction XAML read/write). It follows the configuration — `-c Release2024` tests net48, `-c Release2025` tests net8 — and both must pass before committing a change to instruction reading or writing:
> ```powershell
> dotnet test tests\RVTuk.UI.Tests\RVTuk.UI.Tests.csproj -c Release2024
> dotnet test tests\RVTuk.UI.Tests\RVTuk.UI.Tests.csproj -c Release2025
> ```
> Its fixtures are sanitized (`FamilyBrowser\Fixtures\sanitize_instructions.py`): never commit a real database row. Tests that open a database are net8-only (`#if !NETFRAMEWORK`), because `e_sqlite3.dll` does not load in the net48 test host.

- [ ] **Step 12: [Daniel] See bug 1 in Revit 2024 before it is fixed**

In Revit 2024 with the build installed today, open the Family Browser and select each of the two families from Step 8. Expected: the Instructions tab is empty for both. **Look only — do not press Edit, do not save.** Write the result into the spec's bug 1 line in Task 9.

- [ ] **Step 13: Commit**

```powershell
git add tests/RVTuk.UI.Tests RVTuk.sln CLAUDE.md
git status --short
git commit -m "test(family-browser): WPF test project with cross-runtime instruction fixtures"
```
Before committing, `git status --short` must list only the files named in this task.

---

### Task 4: Read and write through the rewriter; report a parse failure

**Files:**
- Modify: `src/RVTuk.UI/Shared/Controls/RichTextBoxHelper.cs:72-151`
- Create: `tests/RVTuk.UI.Tests/FamilyBrowser/InstructionXamlCrossRuntimeTests.cs`, `RichTextBoxHelperTests.cs`
- Modify: `tests/RVTuk.UI.Tests/FamilyBrowser/InstructionFixtureWriter.cs`; create `Fixtures/saved-net48.txt`, `saved-net8.txt`

**Interfaces:**
- Consumes: `InstructionXamlRewriter.ForRead/ForWrite` (Task 2), `InstructionFixtures`, `Sta.Run` (Task 3).
- Produces, on `RichTextBoxHelper`:

```csharp
public const string UnreadableNotice =
    "These instructions cannot be shown: they were saved in a format this version cannot read. " +
    "The stored text has not been changed.";

// false only when xaml is not blank and could not be parsed; document is then one paragraph
// holding UnreadableNotice. Blank xaml gives true and an empty document.
public static bool TryParseDocumentXaml(string? xaml, out FlowDocument document);
public static FlowDocument ParseDocumentXaml(string? xaml);    // existing signature: TryParse's document
public static string? SerializeDocument(FlowDocument document); // new
public static string? SerializeDocument(RichTextBox rtb);       // existing: delegates to the overload above
```

The helper passes its own `typeof(RichTextBoxHelper).Namespace` and `typeof(RichTextBoxHelper).Assembly.GetName().Name` to the rewriter — never a literal.

- [ ] **Step 1: Write the cross-runtime test and watch the two bugs fail**

`InstructionXamlCrossRuntimeTests.cs` (compiles against today's helper):

```csharp
[Theory]
[InlineData("legacy-net48-1.txt")]
[InlineData("legacy-net48-2.txt")]
[InlineData("raw-net48.txt")]
[InlineData("raw-net8.txt")]
public void Fixture_OpensWithItsImage(string fixture) => Sta.Run(() =>
{
    FlowDocument document = RichTextBoxHelper.ParseDocumentXaml(InstructionFixtures.Read(fixture));
    Image image = Assert.Single(InstructionFixtures.Images(document));
    Assert.Equal(InstructionFixtures.OnePixelPng, RichTextBoxHelper.GetImageData(image));
    Assert.NotNull(image.Source);
});
```

Run both configurations as in Task 3 Step 10.
Expected: net48 fails `legacy-net48-1`, `legacy-net48-2` (bug 1) and `raw-net8` (bug 2); net8 fails `legacy-net48-1`, `legacy-net48-2`; `raw-net48` passes on both. Any other pattern: stop and report — the spec's description of the bugs would be wrong.

- [ ] **Step 2: Write the helper tests — expect a compile failure**

`RichTextBoxHelperTests.cs` (each body inside `Sta.Run`):

| Test | Arrange → assertion |
|---|---|
| `Serialize_WritesTheStoredDeclaration` | `SerializeDocument(SampleDocument())` contains `="clr-namespace:RVTuk.UI.Shared.Controls;assembly=RVTuk.UI"`; does not contain `System.Private.CoreLib` |
| `Serialize_LeavesTheHoverStyleOut` | result contains neither `<Style` nor `Border.Style` |
| `Serialize_PutsStyleAndSourceBack` | `RehydrateImages(doc)` so the image has a `Source`; keep that and the border's `Style` reference; after `SerializeDocument` both are the same instances as before |
| `Serialize_ThenParse_KeepsTextAndImage` | parse the result: `TryParse` true, one image with `OnePixelPng`, document text contains `Sample text` |
| `Parse_Failure_IsReported` | `TryParseDocumentXaml("<FlowDocument xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><NoSuchElement /></FlowDocument>", out var d)` is false; `d`'s text contains `UnreadableNotice` |
| `Parse_Blank_IsAReadableEmptyDocument` | `null`, `""`, `"  "` → true, `d.Blocks` empty |
| `Parse_BrokenImageBytes_StillShowsTheText` (Review Focus 4) | take `SerializeDocument(SampleDocument())`, replace `OnePixelPng` with `not-base64!`; parse → true, text contains `Sample text`, the image's `Source` is null |
| `LegacyRow_SavedAgain_IsInTheStoredForm` (Review Focus 5) | parse `legacy-net48-1.txt`, serialise → contains the stored declaration, contains neither `RVTuk.UI.Controls;` nor `<Style`; parsing that again gives one image |

Document text = `new TextRange(d.ContentStart, d.ContentEnd).Text`.

Run both configurations again.
Expected: FAIL to compile (`TryParseDocumentXaml`, `SerializeDocument(FlowDocument)`, `UnreadableNotice` missing).

- [ ] **Step 3: Implement in `RichTextBoxHelper`**

- `TryParseDocumentXaml`: `ForRead` first, then `XamlReader.Parse`, then `RehydrateImages`; the `catch` builds the notice document and returns false.
- `OnDocumentXamlChanged` keeps calling `ParseDocumentXaml`, so the viewer and the editor both show the notice.
- `SerializeDocument(FlowDocument)`: as today for `Image.Source`, and the same save–clear–restore for the **local** `Style` value of every `Border` that is the direct parent of a collected image (`ReadLocalValue(FrameworkElement.StyleProperty)`; clear and restore only when it is set). Return `ForWrite(XamlWriter.Save(document), …)`; a failure still returns `null` (Task 5 makes the caller handle it).
- Correct the comment above `ParseDocumentXaml` (it names a second caller that does not exist).

- [ ] **Step 4: Run on both runtimes** — expected: all pass.

- [ ] **Step 5: Add the "saved" fixtures**

In `InstructionFixtureWriter`, also write `RichTextBoxHelper.SerializeDocument(InstructionFixtures.SampleDocument())` to `saved-{Runtime}.txt`. Run Task 3 Step 6's four commands again, then:

```powershell
git diff --stat -- tests/RVTuk.UI.Tests/FamilyBrowser/Fixtures
Compare-Object (Get-Content tests\RVTuk.UI.Tests\FamilyBrowser\Fixtures\saved-net48.txt) (Get-Content tests\RVTuk.UI.Tests\FamilyBrowser\Fixtures\saved-net8.txt)
```
Expected: the two `raw-*` files are unchanged; `saved-net48.txt` and `saved-net8.txt` are new. Report whether the two saved files are identical (not required, but write it into the spec in Task 9).

Add `[InlineData("saved-net48.txt")]` and `[InlineData("saved-net8.txt")]` to `Fixture_OpensWithItsImage`, and a test `SavedFixtures_AreInTheStoredForm` (both files: contain the stored declaration, no `System.Private.CoreLib`, no `<Style`).

- [ ] **Step 6: Run everything**

```powershell
dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj
dotnet test tests\RVTuk.UI.Tests\RVTuk.UI.Tests.csproj -c Release2024
dotnet test tests\RVTuk.UI.Tests\RVTuk.UI.Tests.csproj -c Release2025
```
Expected: all pass.

- [ ] **Step 7: Commit**

```powershell
git add src/RVTuk.UI/Shared/Controls/RichTextBoxHelper.cs tests/RVTuk.UI.Tests
git commit -m "fix(family-browser): old and cross-runtime image instructions open again"
```

---

### Task 5: The editor never overwrites what it could not read

**Files:**
- Modify: `src/RVTuk.UI/FamilyBrowser/ViewModels/InstructionsEditorViewModel.cs` (constructor, `ExecuteSave`)
- Modify: `src/RVTuk.UI/FamilyBrowser/Views/InstructionsEditorWindow.xaml` (editor, footer), `.xaml.cs` (constructor, `InsertImageIntoEditor`, `Save_Click`)
- Test: `tests/RVTuk.UI.Tests/FamilyBrowser/InstructionsEditorViewModelTests.cs` (whole file inside `#if !NETFRAMEWORK`)
- Modify: `docs/tools/family-browser/backlog.md`

**Interfaces:**
- Consumes: `RichTextBoxHelper.TryParseDocumentXaml` (Task 4).
- Produces, on `InstructionsEditorViewModel`:

```csharp
// The stored instructions are not blank and could not be parsed.
public bool InstructionsUnreadable { get; }
// false = nothing was written (the editor could not serialise the document).
public bool ExecuteSave(string? xamlFromEditor);   // was void
```

`ExecuteSave` rules: `xamlFromEditor == null` and not `InstructionsUnreadable` → return false, write nothing at all. Otherwise write the instructions only when not `InstructionsUnreadable`; tags and thumbnail as today; return true.

- [ ] **Step 1: Write the failing tests** (net8 only; each inside `Sta.Run`)

Setup per test: a temp folder; `long id = new IndexRepository(db).InsertFamily("Doors/A.rfa", "A.rfa")` (dispose it); `new BrowserRepository(db)`; the view model is `new InstructionsEditorViewModel(id, "A.rfa", Path.Combine(folder, "A.rfa"), repo)`. `Broken` = the `<NoSuchElement />` document from Task 4; `Good` = `SerializeDocument(SampleDocument())`.

| Test | Arrange → assertion |
|---|---|
| `UnreadableRow_IsFlagged` | stored `Broken` → `InstructionsUnreadable` true; stored `Good`, `null`, `""` → false |
| `UnreadableRow_SaveKeepsTheTextAndSavesTheTags` (Review Focus 2) | stored `Broken`; `TagsText = "door"`; `ExecuteSave(Good)` → true; `repo.GetInstructionsXaml(id)` equals `Broken`; `repo.GetTags(id)` equals `"door"` |
| `ReadableRow_SaveWritesTheEditorsText` | stored `null`; `ExecuteSave(Good)` → true; stored equals `Good` |
| `EditorCouldNotSerialise_NothingIsWritten` (Review Focus 3) | stored `Good`, tags saved as `"old"`; `TagsText = "new"`; `ExecuteSave(null)` → false; stored equals `Good`; tags equal `"old"` |

- [ ] **Step 2: Run — expect a compile failure**

Run: `dotnet test tests\RVTuk.UI.Tests\RVTuk.UI.Tests.csproj -c Release2025 --filter InstructionsEditorViewModel`
Expected: FAIL, `InstructionsUnreadable` does not exist.

- [ ] **Step 3: Implement the view model**

- [ ] **Step 4: Implement the window**

- `InstructionsEditorWindow.xaml`: on `Editor` add `IsReadOnly="{Binding InstructionsUnreadable, Mode=OneTime}"`. In the footer grid, left of the buttons, add a `TextBlock` with `Visibility="{Binding InstructionsUnreadable, Converter={StaticResource BoolVis}}"`, `Foreground="OrangeRed"`, `TextWrapping="Wrap"`, `Margin="0,0,160,0"`, `VerticalAlignment="Center"` and exactly this text: `The instructions could not be read, so they are locked. Save keeps them as they are and saves only the tags and the thumbnail.`
- `.xaml.cs` constructor, after `DataContext = ViewModel;`: when `ViewModel.InstructionsUnreadable`, set `FormattingButtons.IsEnabled = false`, `RawBtn.IsEnabled = false`, `Editor.AllowDrop = false`. `InsertImageIntoEditor` returns at once in that state.
- `Save_Click`: `if (!ViewModel.ExecuteSave(xaml)) { MessageBox.Show(this, "The instructions could not be saved. Nothing was changed.", "RVTuk", MessageBoxButton.OK, MessageBoxImage.Warning); return; }` — the window stays open.

- [ ] **Step 5: Run everything** (the three commands of Task 4 Step 6, plus `dotnet build RVTuk.sln -c Release2024`)

Expected: all pass; the net48 run has 4 fewer tests than the net8 run.

- [ ] **Step 6: Backlog** — in `docs/tools/family-browser/backlog.md`, add to the done list: the two instruction bugs (old namespace; `System.Private.CoreLib`), fixed in the add-in split's phase 0, with a pointer to the spec section "Family Browser specifics".

- [ ] **Step 7: Commit**

```powershell
git add src/RVTuk.UI/FamilyBrowser tests/RVTuk.UI.Tests docs/tools/family-browser/backlog.md
git commit -m "fix(family-browser): never save over instructions that could not be read"
```

---

### Task 6: Merge `topo-convert`

- [ ] **Step 1: Merge**

Run: `git merge --no-ff topo-convert -m "Merge branch 'topo-convert'"`
Expected: no conflict (simulated on 2026-11-15). A conflict means master or the branch moved: stop and report.

- [ ] **Step 2: Build and test**

```powershell
dotnet build RVTuk.sln -c Release2023
dotnet build RVTuk.sln -c Release2024
dotnet build RVTuk.sln -c Release2025
dotnet build RVTuk.sln -c Release2026
dotnet build RVTuk.sln -c Release2027
dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj
```
Expected: 0 errors; all tests pass; 25 more test methods than before the merge.

---

### Task 7: Merge `round-positions`

**Files:**
- Conflict: `src/RVTuk.Revit/Application.cs` (one hunk, in `OnStartup`)
- Modify: `CLAUDE.md`; create `docs/tools/round-positions/README.md`

- [ ] **Step 1: Merge**

Run: `git merge --no-ff round-positions -m "Merge branch 'round-positions'"`
Expected: `CONFLICT (content)` in `src/RVTuk.Revit/Application.cs` only; `AppConfig.cs` and `AppConfigTests.cs` merge by themselves.

- [ ] **Step 2: Resolve**

The hunk has the whole `if (RegisterTopoTools) { … }` block on one side and the whole `if (RegisterRoundPositions) { … }` block on the other. Keep **both, complete**, Topo Tools first, Round Positions after it; delete only the three marker lines. Then check `git diff HEAD -- src/RVTuk.Revit/Application.cs` shows additions only (no removed line that is not re-added).

- [ ] **Step 3: Build and test** (the six commands of Task 6 Step 2, plus both UI test runs)

Expected: 0 errors; all pass; `(git grep -h -E '^\s*\[(Fact|Theory)' -- tests | Measure-Object).Count` prints 426 plus the methods Tasks 2–5 added.

- [ ] **Step 4: Docs for the tool that just arrived**

- `CLAUDE.md`: add **Round Positions** to the Terminology table's tool list, to the canonical names (`RoundPositions`), to the v1 launch surface paragraph (on the RVTuk panel, gated by `RegisterRoundPositions`, on), to the folder lists in Architecture, and one entry in Features written from `docs/tools/round-positions/specs/2026-08-06-round-positions-design.md` (ribbon "Round Positions", dockable pane, moves eligible elements onto a whole- or half-centimetre grid, never changes lengths or angles, Revit 2024/2025 only).
- `docs/tools/round-positions/README.md`: what it is, status, entry points (ribbon button `RoundPositions`, pane id `b7a1c94e-3f52-4d08-9c6b-1e84a5f2d370`), links to its spec and two plans — same shape as `docs/tools/room-floors/README.md`.

- [ ] **Step 5: Conclude the merge**

```powershell
git add src/RVTuk.Revit/Application.cs CLAUDE.md docs/tools/round-positions/README.md
git commit --no-edit
```

---

### Task 8: Delete the dead setup command

**Files:**
- Delete: `src/RVTuk.Revit/RishuiZamin/Commands/SetupRishuiZaminParamsCommand.cs`, `src/RVTuk.Revit/RishuiZamin/Resources/RZ_AreaParams.txt` (and the `Resources` folder if it is then empty)
- Modify: `src/RVTuk.Revit/RVTuk.Revit.csproj` (the comment and `ItemGroup` for `RZ_AreaParams.txt`, last block of the file)
- Modify: `Deploy.ps1` (the comment and `Copy-Item "$srcDir\*.txt"` line), `Build-Installer.ps1` (its `Copy-Item "$srcDir\*.txt"` line — keep the `version.txt` line), `docs/tools/rishui-zamin/README.md` (drop `RZ_AreaParams.txt` from the Revit row)

- [ ] **Step 1: Confirm nothing uses them**

Run: `git grep -n "SetupRishuiZaminParams\|RZ_AreaParams" -- src Deploy.ps1 Build-Installer.ps1 installer`
Expected: only the command's own file, the csproj item, and the `Deploy.ps1` comment.

- [ ] **Step 2: Delete and edit** as listed. Dated specs and plans under `docs/` keep their mentions (historical docs are kept verbatim).

- [ ] **Step 3: Verify**

```powershell
git grep -n "SetupRishuiZaminParams\|RZ_AreaParams" -- src Deploy.ps1 Build-Installer.ps1 installer CLAUDE.md README.md docs/tools/rishui-zamin/README.md
git grep -n -i "f80b9154" -- src
dotnet build RVTuk.sln -c Release2023
dotnet build RVTuk.sln -c Release2024
dotnet build RVTuk.sln -c Release2025
dotnet build RVTuk.sln -c Release2026
dotnet build RVTuk.sln -c Release2027
```
Expected: both greps print nothing; five builds with 0 errors.

- [ ] **Step 4: Commit**

```powershell
git add -A src/RVTuk.Revit Deploy.ps1 Build-Installer.ps1 docs/tools/rishui-zamin/README.md
git commit -m "chore(rishui-zamin): delete the unused shared-parameter setup command"
```

---

### Task 9: Record the inventory in the spec

**Files:**
- Modify: `docs/toolkit/specs/2026-11-12-addin-split-design.md` — new last section `## Phase 0 record`, and corrections where a number or line reference in the body is now wrong

Each step is: run the command, write the result under its own heading in `## Phase 0 record`, and correct the spec body where it disagrees. The first line of the section is the commit the record was taken at (`git rev-parse --short HEAD`) and the date.

- [ ] **Step 1: Frozen identifiers**

```powershell
$ids = '7b3c1d92-4e58-4a0f-9c21-6d5f8e3a17b4','6f2c81d4-9a7b-4e35-8c10-b4d6e27f9a51','6d21f8b4-0c93-4e57-a8d6-b45f70e29c18','3a6c50f1-8b47-4d2e-95c3-1f8a4b7e0d62','a9b2b113-115d-48a4-80a5-3063ddc3caca','309b77b4-64cd-45c7-bd73-d6a81b1f85ae','e5d47b31-2c8a-4f16-b0d9-73a5e91c46f2','a17c46e9-5b83-4d20-8f6a-9c4e2b70d135','88a9c4fa-9c6a-4b02-b169-701be8358090','b7a1c94e-3f52-4d08-9c6b-1e84a5f2d370'
$ids | ForEach-Object { git grep -n -i $_ -- src }
git grep -n 'SetVendorId\|"KnafoKlimor"' -- src
git grep -n -E 'RZ_[A-Z_]+"|new Guid\(|ScheduleName = ' -- src/RVTuk.Revit/RishuiZamin/UsageKeyScheduleBuilder.cs
git grep -n -o -E '"_DP-[^"]+"' -- src
git grep -n -A1 'new PushButtonData(' -- src/RVTuk.Revit/Application.cs src/KKarea.Revit/Application.cs
git grep -n 'LogicalName\|"RVTuk.db"' -- src
```
Expected: each of the ten GUIDs on exactly one line (six `SchemaGuid` definitions, four `DockablePaneId`s); `SetVendorId("KnafoKlimor")` in six stores plus `SlabConverter.OurVendorId`. Record as a table: identifier, value, `file:line`. Record the five `RZ_*` parameter names **with their GUIDs** (the spec body lists only the names), the two schedule names, every `_DP-*` name, every button internal name, the two embedded resource names.

- [ ] **Step 2: Views that merge the theme**

Run: `git grep -l "RVTuk.UI;component" -- src`
Expected: 11 `.xaml` files (the 10 on master plus `RoundPositions/Views/RoundPositionsPaneView.xaml`). Record the list.

- [ ] **Step 3: Files that use `Application.X`**

Run: `git grep -c -E "(^|[^.\w])Application\.[A-Z]" -- src/RVTuk.Revit src/KKarea.Revit`
Record files and counts. On the simulated merge this gave 7 files and 63 uses under `src/RVTuk.Revit`; the spec body says "67 in five files on master; a sixth arrives". Open the files, settle which count is right for the claim the spec makes (unqualified references that must bind to each host's entry class), and correct that sentence.

- [ ] **Step 4: Settings keys**

Run: `git grep -n "{ get; set; }" -- src/RVTuk.Core/Shared/Config/AppConfig.cs` and `git grep -l "ConfigManager\.\(Load\|Save\)" -- src`
Expected: 22 keys on `AppConfig` — Family Browser 3 (`LibraryFolderPath`, `IgnoredSubfolders`, `IgnoredFilePatterns`), Area Calc 2, DWG Export 15, Topo 1, Round Positions 1 — plus `ModelKey` and `Folder` on `DwgExportFolderEntry`. Record every name under its add-in, and the list of files with load/save call sites (the spec says 7 files plus one in the Revit host).

- [ ] **Step 5: Test count**

Record four numbers: test methods (`(git grep -h -E '^\s*\[(Fact|Theory)' -- tests | Measure-Object).Count`), and the "Passed" totals of the Core run, the UI net48 run and the UI net8 run. Add one line: later phases compare against these.

- [ ] **Step 6: Installed file list per Revit year**

```powershell
.\Build-Installer.ps1
$out = "$env:TEMP\rvtuk-phase0-payload"
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
dist\RVTukSetup.exe --all --no-pause --target $out
Get-ChildItem $out -Recurse -File | ForEach-Object { $_.FullName.Substring($out.Length + 1) } | Sort-Object
```
Expected: `KKarea.addin` + `KKarea\…` under `2023`, `2026` and `2027`, and `RVTuk.addin` + `RVTuk\…` under `2024` and `2025`; no `RZ_AreaParams.txt` anywhere. Record the sorted list as one code block per year (five). `dist\` and the temp folder are not committed.

- [ ] **Step 7: Re-check the spec's own references**

The spec describes master as of 2026-11-12. PR #12 and phase 0 moved lines in `RVTuk.Core.csproj`, `RVTuk.UI.csproj`, `KKarea.Revit.csproj`, `RVTuk.sln`, `Deploy.ps1`, `Build-Installer.ps1`, `installer\RVTuk.Setup\Program.cs`, `RichTextBoxHelper.cs`, `InstructionsEditorWindow.xaml(.cs)`, `Application.cs`, `AppConfig.cs` and `RVTuk.Revit.csproj`. Open **every** `file:line` reference in the spec and correct the ones that no longer point at what the sentence describes. Correct the statements PR #12 made untrue: KKarea is no longer 2023-only (Context, "Repo layout", the old-installs rule, the staging check's "on 2023" clause), master has five solution configurations, not three, and the package conditions in `RVTuk.Core.csproj` now have a net10 branch (Build section, "Framework conditions"). Also correct: the test-file count in "Critical files" (`git ls-files tests | Select-String 'Tests\.cs$' | Measure-Object`), the phase 0 row's "343 test methods today" (keep it, add the new number), and bug 1's "phase 0 reproduces it in Revit" (write what Task 3 Step 12 showed). Add the two findings from this plan: SQLite does not load in a net48 test host (belongs in "Tests on net48 need"), and whether `saved-net48.txt` and `saved-net8.txt` came out identical.

- [ ] **Step 8: Commit**

```powershell
git add docs/toolkit/specs/2026-11-12-addin-split-design.md
git commit -m "docs(toolkit): record the phase 0 inventory in the add-in split design"
```

---

### Task 10: Final verification and the `before-split` tag

This is the phase's "Done when" line from the spec, in order.

- [ ] **Step 1: master builds for all its configs** (the spec says three; with PR #12 there are five)

```powershell
dotnet build RVTuk.sln -c Release2023
dotnet build RVTuk.sln -c Release2024
dotnet build RVTuk.sln -c Release2025
dotnet build RVTuk.sln -c Release2026
dotnet build RVTuk.sln -c Release2027
```
Expected: 0 errors each.

- [ ] **Step 2: Core tests pass; UI tests pass with both configurations**

```powershell
dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj
dotnet test tests\RVTuk.UI.Tests\RVTuk.UI.Tests.csproj -c Release2024
dotnet test tests\RVTuk.UI.Tests\RVTuk.UI.Tests.csproj -c Release2025
```
Expected: all pass, with the totals recorded in Task 9 Step 5.

- [ ] **Step 3: [Daniel] The two old image instructions open in Revit 2024**

With Revit 2024 closed, in an administrator PowerShell: `Set-Location D:\Coding\RVTuk-split; .\Deploy.ps1 2024`. Then in Revit 2024:

1. Family Browser → select each of the two families from Task 3 Step 8. The Instructions tab shows the text and the image.
2. Press Edit on the **first** one: the editor shows the same text and image. Change nothing, press Save, select another family and come back: still shown.
3. Press Edit on the **second** one: text and image are shown. Press Cancel (this row stays in the old form for later phases).
4. Quick look at the merges: the ribbon has the Round Positions button and its pane opens; the Topo Tools pane opens.

If step 1 or 2 fails: stop, do not tag; the backup from Task 1 Step 4 restores the database. Use superpowers:systematic-debugging.

- [ ] **Step 4: The lists in the spec are re-checked** — Task 9 is committed and `git status` is clean.

- [ ] **Step 5: Tag**

```powershell
git tag -a before-split -m "RVTuk as one add-in, before the split into separately installable add-ins"
git log --oneline -1 before-split
```

- [ ] **Step 6: [Daniel] Push** — only after he says so: `git push origin master` and `git push origin before-split`. The local branches `topo-convert` and `round-positions` are left as they are.

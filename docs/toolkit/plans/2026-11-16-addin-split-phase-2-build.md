# Add-in split, phase 2 (Foundation), build half — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One build matrix for the five Revit years in a single root file, every test running on .NET Framework 4.8, .NET 8 and .NET 10 with one command, and the safety nets that phase 3 needs before any tool moves: pinned identifiers, a smoke test over every view, and the theme loaded through a class.

**Architecture:** A root `Directory.Build.props` derives the Revit year from the configuration name and sets framework, constant, output path and a per-configuration NuGet assets folder for every project; the project files lose their own copies. The solution maps every project to the same-named configuration, so the test projects follow the matrix. The identifier test reads the source files as text, so it keeps working when phase 3 moves them. Nothing is abstracted: no new project, no helper library.

**Tech Stack:** MSBuild / .NET SDK 10, C# (net48, net8.0-windows, net10.0-windows), WPF, xunit 2, PowerShell.

**Spec:** `docs/toolkit/specs/2026-11-12-addin-split-design.md` — "Build", "Shared source" (Theme), "Frozen identifiers", phase table row 2 (the *Build* half), ".gitignore fix", "Phase 1 record" S2 and S6.

## Not in this plan

- The install engine, `addin.props`, the staging function and the staging check, and `Deploy.ps1` on top of them: the second phase 2 plan. `Deploy.ps1` and `Build-Installer.ps1` were **not to be edited here**; Task 8 (added 2026-11-16, see the last bullet) changed their 2026 row only.
- The generic settings store: phase 3, Round Positions step (spec header note of 2026-11-16).
- Since Daniel's decision of 2026-11-16 Revit 2026 gets the full RVTuk add-in (Task 8, added after the plan was written), so the monolith in Revit 2026.5 and the 2024 ↔ 2026.5 instruction round trip are checked in Task 7.

## Global Constraints

- No frozen identifier changes (spec "Frozen identifiers"). Task 5 pins them; nothing in this plan edits one.
- Build outputs stay at `bin\{year}\Release{year}\{tfm}\` in every project — `Deploy.ps1` and `Build-Installer.ps1` read them there.
- Namespace = root namespace + folder path. Core has no WPF or Revit types; UI has no Revit types.
- No office drawings, real instruction text or real settings files in the repository.
- Everything lands on `master` in small commits. `CLAUDE.md` / `README.md` change in the same commit as what they describe. **No push without Daniel's OK.** Commit messages end with `Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>`.
- All commands run from `D:\Coding\RVTuk-split` in PowerShell. Never read or write `D:\Coding\RVTuk` (another working copy).
- After Task 3 every `dotnet build` / `dotnet test` carries `-c Release20xx`.
- Steps marked **[Daniel]** need Revit or an administrator shell. An agent stops there and asks him.

## Review Focus

Failure modes the spec implies but states no test for. Each has a check in the task named.

1. A project file that still sets `RevitYear`, `TargetFramework`, `OutputPath` or `DefineConstants` overrides the root file silently (today `RVTuk.Revit -c Release2026` builds 2025 without a word) — Task 3, the evaluation table.
2. A leftover `obj\project.assets.json` makes a build pass against the wrong year's API — Task 3 wipes `bin`/`obj` and proves it with a `--no-restore` build.
3. A test project the solution does not build makes `dotnet test` print nothing and exit 0 (today's `Release2027`) — Task 4, the "No tests ran" guard.
4. A view left on the `/RVTuk.UI;component/…` literal keeps working on the monolith and breaks only after the split — Task 6, the smoke test asserts the dictionary's type.
5. The identifier test passes when it scans nothing or when a literal was copied instead of moved — Task 5, exact counts and a mutation step.

## Facts established while planning (2026-11-16, at `f8cd1f0`)

- The matrix is copied into every project today: `RVTuk.Core.csproj:11-43`, `RVTuk.UI.csproj:13-37`, `RVTuk.Revit.csproj:12-33` (falls back to 2025), `KKarea.Revit.csproj:17-29` (falls back to 2023). `RVTuk.Core.Tests.csproj:4` is fixed net8; `RVTuk.UI.Tests.csproj:12-18` knows 2024 and 2025 only.
- `RVTuk.sln` has seven solution configurations (five plus `Debug`, `Release`) × three platforms, 213 mapping lines. `RVTuk.Core.Tests` is mapped to `Debug` in every `Release20xx` configuration. Under `Release2027` no test project is built.
- The repository has no `Directory.Build.*`, `global.json` or `NuGet.config`. SDKs installed: 9.0.321, 10.0.404. The Revit API packages 2023–2027 are in the local NuGet cache.
- `.tmp-repro/` (two scratch projects) is tracked by mistake since `69055a2`; the root file would apply to it.
- Throwaway builds in a scratch copy:
  - `RVTuk.Revit` against the 2026 API: one compile error, `DrawPoint` at `src/RVTuk.Revit/TopoTools/SlabShape.cs:155`; fixed by one `#if` line.
  - Core tests as net48: 8 compile errors (1 `System.Text.Json`, 2 `ZipArchive`, 5 `string.Contains(string, StringComparison)`), then 47 database tests fail because xunit's shadow copy runs the tests from a temp folder without `e_sqlite3.dll`, and 1 test cannot create a path of 260+ characters in its own setup.
  - With the fixes of Task 2 and the matrix: Core 569 of 569 and UI 33 of 33 on net48, net8 and net10.
  - After a `Release2026` restore, `dotnet build src\RVTuk.Revit\RVTuk.Revit.csproj -c Release2025 --no-restore` fails today (one shared assets file per project).
- Test methods (`[Fact]`/`[Theory]`): 459 (Core 436, UI 23). Cases run today: Core 569 (net8 only), UI 29 (net48) and 33 (net8).
- All 11 views merge the theme with the same line, `<ResourceDictionary Source="/RVTuk.UI;component/Shared/Themes/DarkTheme.xaml"/>`; no other file in `src` names `RVTuk.UI;component`.
- `Config_ReadsAcrossBothSerializers` (`tests/RVTuk.Core.Tests/Shared/AppConfigTests.cs:263-293`) already asserts that `DataContractJsonSerializer` runs `AppConfig`'s constructor; it has never run on .NET Framework. In the scratch copy it passed on net48.

## File structure

| File | Responsibility |
|---|---|
| `Directory.Build.props` (new) | The build matrix, the unknown-configuration error, per-configuration assets |
| `global.json` (new) | Pins SDK 10.0.x |
| `installer/Directory.Build.props` (new, `<Project />`) | Keeps the setup exe out of the matrix |
| `src/*/*.csproj`, `tests/*/*.csproj` | Lose their own matrix rows |
| `RVTuk.sln` | Five configurations, one platform, same-named mappings |
| `build/Test-All.ps1` (new) | Every test on three frameworks; refuses untracked drawings |
| `tests/*/xunit.runner.json` (new, two files) | Shadow copy off |
| `tests/RVTuk.Core.Tests/Shared/FrozenIdentifierTests.cs` (new) | Pins every frozen identifier by source text |
| `src/RVTuk.UI/Shared/Themes/DarkThemeDictionary.cs` (new) | Loads `DarkTheme.xaml` from its own assembly |
| `tests/RVTuk.UI.Tests/Shared/ViewSmokeTests.cs` (new) | Constructs all 11 views with the theme |
| the 11 view `.xaml` files | Merge `<theme:DarkThemeDictionary/>` |

---

### Task 1: Workspace and baseline

**Files:** none changed, except the commit of the documents already in the working tree.

- [ ] **Step 1: Check the workspace**

Run: `git status --short; git log --oneline -1`
Expected: branch `master` at `f8cd1f0` or later; the only changes are `CLAUDE.md`, `docs/toolkit/specs/2026-11-12-addin-split-design.md`, `docs/tools/family-browser/backlog.md` and this plan (untracked).

- [ ] **Step 2: Commit the documents**

```powershell
git add CLAUDE.md docs/toolkit/specs/2026-11-12-addin-split-design.md docs/tools/family-browser/backlog.md docs/toolkit/plans/2026-11-16-addin-split-phase-2-build.md
git commit -m "docs(toolkit): close phase 1 of the add-in split and plan phase 2's build half"
```

- [ ] **Step 3: Record what today's builds ship**

The matrix must not change a single shipped file. Build today's five deployed outputs and save their file lists:

```powershell
$base = "$env:TEMP\rvtuk-phase2"; New-Item -ItemType Directory -Force $base | Out-Null
$ship = @{ '2023' = 'KKarea.Revit','net48'; '2024' = 'RVTuk.Revit','net48'; '2025' = 'RVTuk.Revit','net8.0-windows'
           '2026' = 'KKarea.Revit','net8.0-windows'; '2027' = 'RVTuk.Revit','net10.0-windows' }
foreach ($y in $ship.Keys) {
  $p, $tfm = $ship[$y]
  Remove-Item "src\$p\bin\$y" -Recurse -Force -ErrorAction SilentlyContinue
  dotnet build "src\$p\$p.csproj" -c "Release$y" --nologo -v q; if ($LASTEXITCODE) { throw "build $y" }
  Get-ChildItem "src\$p\bin\$y\Release$y\$tfm" -Recurse -File -Name | Sort-Object | Set-Content "$base\before-$y.txt"
}
```
Expected: five builds with 0 errors, five `before-*.txt` files.

- [ ] **Step 4: Record today's test results**

Run: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj`, `dotnet test tests\RVTuk.UI.Tests\RVTuk.UI.Tests.csproj -c Release2024`, `dotnet test tests\RVTuk.UI.Tests\RVTuk.UI.Tests.csproj -c Release2025`
Expected: 569, 29 and 33 passed, 0 failed. Any other number: stop and report.

### Task 2: Tests ready for .NET Framework

Every change here is harmless on net8, so the suite stays green before the matrix exists. Core's net48 run is first possible in Task 3.

**Files:**
- Create: `tests/RVTuk.Core.Tests/xunit.runner.json`, `tests/RVTuk.UI.Tests/xunit.runner.json`
- Modify: `tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj`, `tests/RVTuk.UI.Tests/RVTuk.UI.Tests.csproj`, `tests/RVTuk.Core.Tests/RishuiZamin/AreaValidatorTests.cs:152,159,166,173,202`, `tests/RVTuk.Core.Tests/FamilyBrowser/FamilyIndexerTests.cs:322`, `tests/RVTuk.UI.Tests/FamilyBrowser/InstructionsEditorViewModelTests.cs`, `CLAUDE.md`

- [ ] **Step 1: Switch xunit's shadow copy off in both test projects**

Both `xunit.runner.json` files contain exactly `{ "shadowCopy": false }`. In each csproj, inside the existing `Content` `ItemGroup`, add:

```xml
    <Content Include="xunit.runner.json" CopyToOutputDirectory="PreserveNewest" />
```

- [ ] **Step 2: Let the editor tests run on net48**

In `InstructionsEditorViewModelTests.cs` delete line 1 (`#if !NETFRAMEWORK`) and the last line (`#endif`). Replace the comment sentences from "Net8 only: SQLite does not initialise" to the end of the comment with: `Runs on every framework: xunit.runner.json switches shadow copy off, so SQLite finds e_sqlite3.dll under the test folder's runtimes\win-x64\native.`

- [ ] **Step 3: Run the UI tests on net48**

Run: `dotnet test tests\RVTuk.UI.Tests\RVTuk.UI.Tests.csproj -c Release2024`
Expected: 33 passed (was 29). If the four editor tests fail with "Library e_sqlite3 not found", `xunit.runner.json` is not in `bin\Release2024\net48`.

- [ ] **Step 4: Remove the .NET-Core-only calls from the Core tests**

`AreaValidatorTests.cs`, five lines of this shape — replace `e.Contains(X, System.StringComparison.OrdinalIgnoreCase)` with `e.IndexOf(X, System.StringComparison.OrdinalIgnoreCase) >= 0`, `X` unchanged.

`FamilyIndexerTests.cs:322`, in `Scan_LongPathFamily_KeepsItsRow`, replace `var full = WriteRfa(rel);` with:

```csharp
        var full = Path.Combine(_root, rel);
        Directory.CreateDirectory(@"\\?\" + Path.GetDirectoryName(full));
        File.WriteAllText(@"\\?\" + full, "fake");
```

- [ ] **Step 5: Give the Core tests their net48 references**

Add to `RVTuk.Core.Tests.csproj` (inert until Task 3 makes the project net48 under `Release2024`):

```xml
  <!-- net48 only: the settings tests compare both JSON serializers, and the transmittal
       tests read zip files. Test-only: the add-in itself ships no System.Text.Json on net48. -->
  <ItemGroup Condition="'$(TargetFramework)'=='net48'">
    <PackageReference Include="System.Text.Json" Version="8.0.*" />
    <Reference Include="System.IO.Compression" />
    <Reference Include="System.IO.Compression.FileSystem" />
  </ItemGroup>
```

- [ ] **Step 6: Run all three test commands of Task 1 Step 4**

Expected: 569, **33**, 33 passed.

- [ ] **Step 7: Update `CLAUDE.md` and commit**

In the "Tests" section replace the sentence that starts "Tests that open a database are net8-only" up to the end of that paragraph with: `Database tests run on every framework: each test project carries an xunit.runner.json that switches shadow copy off, because the shadow folder has no e_sqlite3.dll.`

```powershell
git add tests CLAUDE.md
git commit -m "test: let every test run on .NET Framework (shadow copy off)"
```

### Task 3: The build matrix

One commit: the root file, the project files, the solution and the per-configuration assets folder only work together.

**Files:**
- Create: `Directory.Build.props`, `global.json`, `installer/Directory.Build.props`
- Modify: `src/RVTuk.Core/RVTuk.Core.csproj`, `src/RVTuk.UI/RVTuk.UI.csproj`, `src/RVTuk.Revit/RVTuk.Revit.csproj`, `src/KKarea.Revit/KKarea.Revit.csproj`, `tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj`, `tests/RVTuk.UI.Tests/RVTuk.UI.Tests.csproj`, `RVTuk.sln`, `src/RVTuk.Revit/TopoTools/SlabShape.cs:140-141,151`, `CLAUDE.md`
- Delete: `.tmp-repro/`

**Interfaces:**
- Produces: MSBuild properties `RevitYear` (`2023`…`2027`), `TargetFramework`, `OutputPath`, and the constant `REVIT<year>` in every project under the repository root except `installer/`. Later tasks and the engine plan rely on `dotnet build|test <x> -c Release20xx`.

- [ ] **Step 1: Create `Directory.Build.props`**

```xml
<Project>
  <!-- Build matrix: one Revit year per configuration. An unknown or empty configuration
       evaluates as 2025, so restore and an IDE keep working, and fails before compiling. -->
  <PropertyGroup>
    <Configurations>Release2023;Release2024;Release2025;Release2026;Release2027</Configurations>
    <RVTukKnownConfiguration>$([System.Text.RegularExpressions.Regex]::IsMatch('$(Configuration)', '^Release202[3-7]$'))</RVTukKnownConfiguration>
    <RevitYear>2025</RevitYear>
    <RevitYear Condition="'$(RVTukKnownConfiguration)'=='True'">$(Configuration.Substring(7))</RevitYear>
    <TargetFramework>net8.0-windows</TargetFramework>
    <TargetFramework Condition="'$(RevitYear)'=='2023' or '$(RevitYear)'=='2024'">net48</TargetFramework>
    <TargetFramework Condition="'$(RevitYear)'=='2027'">net10.0-windows</TargetFramework>
    <DefineConstants>$(DefineConstants);REVIT$(RevitYear)</DefineConstants>
    <OutputPath>bin\$(RevitYear)\Release$(RevitYear)\</OutputPath>
    <!-- One NuGet assets file per configuration: 2023/2024 share net48 and 2025/2026 share
         net8, so one shared obj\project.assets.json compiles against the other year's API. -->
    <MSBuildProjectExtensionsPath>obj\$(Configuration)\</MSBuildProjectExtensionsPath>
  </PropertyGroup>

  <Target Name="RVTukRejectConfiguration" BeforeTargets="CoreCompile"
          Condition="'$(RVTukKnownConfiguration)'!='True' and '$(DesignTimeBuild)'!='true'">
    <Error Text="Configuration '$(Configuration)' is not in the build matrix: use -c Release2023, Release2024, Release2025, Release2026 or Release2027." />
  </Target>
</Project>
```

- [ ] **Step 2: Create the two small files**

`global.json`: `{ "sdk": { "version": "10.0.100", "rollForward": "latestFeature" } }`
`installer/Directory.Build.props`: `<Project />` — MSBuild stops at the nearest `Directory.Build.props`, so the setup exe (built with `-c Release`) never sees the matrix.

- [ ] **Step 3: Remove the matrix from the project files**

A value set in a project file wins over the root file, so every line named here must go.

- `RVTuk.Core.csproj`: delete the five configuration groups and the catch-all (`:11-43`). Replace the three `Microsoft.Data.Sqlite` item groups (`:56-69`) with one unconditional group holding `<PackageReference Include="Microsoft.Data.Sqlite" Version="8.0.29" />` (the `System.Text.Json` package goes: it is in-box on net8 and net10). Replace the two `System.Drawing.Common` groups (`:103-111`) with:

  ```xml
  <!-- Everything but net48: the System.Drawing.Common of the runtime's own version
       (8.0.* on net8, 10.0.* on net10). net48 takes System.Drawing from the GAC. -->
  <ItemGroup Condition="'$(TargetFramework)'!='net48'">
    <PackageReference Include="System.Drawing.Common" Version="$([MSBuild]::GetTargetFrameworkVersion('$(TargetFramework)', 2)).*" />
  </ItemGroup>
  ```
  Keep the pin comments, OpenMCDF, the embedded resources and the net48 GAC references. Reword comments that still describe a per-framework package list.
- `RVTuk.UI.csproj`: delete `:13-37`.
- `RVTuk.Revit.csproj`: delete `:12-33`; in its place one comment: `Year, framework and REVIT<year> come from Directory.Build.props. Built for 2024-2027 (RVTuk.sln); the 2026 build is not deployed, KKarea serves that year.` Change the condition at `:35` to `'$(TargetFramework)'!='net48'`.
- `KKarea.Revit.csproj`: delete `:17-20` and `:23-29`; change the condition at `:34` to `'$(TargetFramework)'!='net48'`. Keep the linked sources.
- `RVTuk.Core.Tests.csproj`: delete `:4` (`TargetFramework`).
- `RVTuk.UI.Tests.csproj`: delete `Configurations`, `TargetFramework` and the `Release2024` group (`:12-13,16-18`); the comment at `:3` becomes `WPF tests without Revit; the framework follows the configuration (Directory.Build.props).`

- [ ] **Step 4: Rewrite the solution's configuration sections**

`SolutionConfigurationPlatforms` holds exactly five rows, `Release20xx|Any CPU = Release20xx|Any CPU`. `ProjectConfigurationPlatforms` holds exactly the 52 lines this prints (same-named configuration everywhere; `Build.0` only where the project is built):

```powershell
$built = [ordered]@{
  '{A1B2C3D4-AAAA-BBBB-CCCC-000000000001}' = 2023..2027      # RVTuk.Core
  '{A1B2C3D4-AAAA-BBBB-CCCC-000000000002}' = 2023..2027      # RVTuk.UI
  '{A1B2C3D4-AAAA-BBBB-CCCC-000000000003}' = 2024..2027      # RVTuk.Revit (2023 has no Toposolid)
  '{E7E2434F-B8BE-4F40-8D41-53FAE5A0CDE4}' = 2023, 2026      # KKarea.Revit
  '{0D0001D4-83C6-4BD3-AA2B-6781EFC839AB}' = 2024, 2025, 2027 # RVTuk.Core.Tests
  '{34A563ED-2B1E-4A6A-8C08-8563E6C48C31}' = 2024, 2025, 2027 # RVTuk.UI.Tests
}
foreach ($g in $built.Keys) { foreach ($y in 2023..2027) {
  "`t`t$g.Release$y|Any CPU.ActiveCfg = Release$y|Any CPU"
  if ($built[$g] -contains $y) { "`t`t$g.Release$y|Any CPU.Build.0 = Release$y|Any CPU" } } }
```
The `Debug`, `Release`, `x64` and `x86` rows go. The project list, `SolutionProperties` and `NestedProjects` stay.

- [ ] **Step 5: The one 2026 compile fix**

`SlabShape.cs:151`: `#if REVIT2027` → `#if REVIT2026 || REVIT2027`. In the comment at `:140-141` write "The 2026 and 2027 APIs no longer have" and "whether theirs takes edge points".

- [ ] **Step 6: Remove the stray scratch projects and the old outputs**

```powershell
git rm -r -q .tmp-repro
Get-ChildItem src, tests -Directory -Recurse -Include bin, obj | Remove-Item -Recurse -Force
```

- [ ] **Step 7: Every project evaluates as the matrix says**

```powershell
foreach ($p in Get-ChildItem src, tests -Recurse -Filter *.csproj) { foreach ($y in 2023..2027) {
  $r = (dotnet msbuild $p.FullName "-p:Configuration=Release$y" -getProperty:TargetFramework -getProperty:DefineConstants -getProperty:OutputPath -getProperty:MSBuildProjectExtensionsPath | ConvertFrom-Json).Properties
  "$($p.BaseName) $y $($r.TargetFramework) $($r.OutputPath) $($r.MSBuildProjectExtensionsPath) $($r.DefineConstants)" } }
```
Expected, for all six projects alike: 2023 and 2024 → `net48`; 2025 and 2026 → `net8.0-windows`; 2027 → `net10.0-windows`; `OutputPath` = `bin\<year>\Release<year>\<tfm>\` (the SDK appends the framework); the assets path ends in `obj\Release<year>\`; `DefineConstants` = `;REVIT<year>;TRACE;RELEASE<year>`. A row that differs means a project file still sets that property.

- [ ] **Step 8: All five configurations build; the tests pass on three frameworks**

```powershell
foreach ($y in 2023..2027) { dotnet build RVTuk.sln -c "Release$y" --nologo -v q; "Release$y exit $LASTEXITCODE" }
foreach ($y in 2024, 2025, 2027) { dotnet test RVTuk.sln -c "Release$y" --no-build --nologo }
```
Expected: five times exit 0; each test run reports Core 569 and UI 33 passed, 0 failed. Then the one test that settles the serializer question, on .NET Framework:

```powershell
dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj -c Release2024 --no-build --filter FullyQualifiedName~Config_ReadsAcrossBothSerializers
```
Expected: 1 passed, on net48. If it fails, **stop**: the spec's serializer contract is wrong and the design must be revisited.

- [ ] **Step 9: An unknown configuration fails with the matrix's error; the setup exe is untouched**

```powershell
dotnet build src\RVTuk.Core\RVTuk.Core.csproj -c Debug     # exit 1, "is not in the build matrix"
dotnet build src\RVTuk.Core\RVTuk.Core.csproj              # the same error
dotnet msbuild src\RVTuk.Core\RVTuk.Core.csproj -restore -p:Configuration=Debug -p:DesignTimeBuild=true -p:SkipCompilerExecution=true -p:ProvideCommandLineArgs=true -t:CoreCompile   # exit 0
dotnet build installer\RVTuk.Setup\RVTuk.Setup.csproj -c Release   # exit 0, as before
```

- [ ] **Step 10: A build compiles against its own year's Revit API**

```powershell
dotnet build RVTuk.sln -c Release2026 --nologo -v q
dotnet build src\RVTuk.Revit\RVTuk.Revit.csproj -c Release2025 --no-restore --nologo -v q   # exit 0 (failed before this task)
(dotnet build src\RVTuk.Revit\RVTuk.Revit.csproj -c Release2026 -t:ResolveAssemblyReferences -getItem:ReferencePath | ConvertFrom-Json).Items.ReferencePath.Identity | Select-String 'revitapi(ui)?\\'
(dotnet build src\KKarea.Revit\KKarea.Revit.csproj -c Release2023 -t:ResolveAssemblyReferences -getItem:ReferencePath | ConvertFrom-Json).Items.ReferencePath.Identity | Select-String 'revitapi(ui)?\\'
```
Expected: the 2026 paths contain `\2026.`, the 2023 paths `\2023.`.

- [ ] **Step 11: The shipped files are the same**

Step 8 built all five; list them again and compare (a new shell has none of Task 1's variables, so the block is complete):

```powershell
$base = "$env:TEMP\rvtuk-phase2"
$ship = @{ '2023' = 'KKarea.Revit','net48'; '2024' = 'RVTuk.Revit','net48'; '2025' = 'RVTuk.Revit','net8.0-windows'
           '2026' = 'KKarea.Revit','net8.0-windows'; '2027' = 'RVTuk.Revit','net10.0-windows' }
foreach ($y in $ship.Keys) {
  $p, $tfm = $ship[$y]
  Get-ChildItem "src\$p\bin\$y\Release$y\$tfm" -Recurse -File -Name | Sort-Object | Set-Content "$base\after-$y.txt"
  Compare-Object (Get-Content "$base\before-$y.txt") (Get-Content "$base\after-$y.txt")
}
```
Expected: no output. A difference in the 2025 or 2026 list that names `System.Text.Json.dll` means the package was shipping after all: put it back for `'$(TargetFramework)'=='net8.0-windows'` and note it in Task 7's record.

- [ ] **Step 12: Update `CLAUDE.md` and commit**

"Build" section: after the five build commands add `The matrix lives in the root Directory.Build.props; a configuration outside the five (or none) fails the build. NuGet assets are per configuration, in obj\Release{year}\.` Replace the paragraph that starts "`Release2023` and `Release2026` build only" up to "(KKarea does not build for 2027)." with: `Release2023 builds RVTuk.Core, RVTuk.UI and KKarea.Revit. Release2024 to Release2027 build RVTuk.Core, RVTuk.UI and RVTuk.Revit; Release2026 also builds KKarea.Revit, which is what Revit 2026 gets (the RVTuk.Revit build for 2026 is not deployed yet). The test projects build under Release2024, Release2025 and Release2027.` The sentence about the removed API members becomes: ``The 2026 API dropped `SlabShapeEditor.DrawPoint` and `DrawSplitLine`; `RVTuk.Revit\TopoTools\SlabShape.cs` uses `AddPoint` from 2026 (`#if REVIT2026 || REVIT2027`) and `AddSplitLine` after 2024, and whether that `AddPoint` accepts points on an edge is not yet checked in Revit.`` "Tests" section: add `-c Release2025` to the Core test command and to the Linux hint. "Architecture" section, three statements this task makes false: in the KKarea paragraph, the year, framework and constant come from the root `Directory.Build.props`, not from a `RevitYear` property in its csproj; in Core's dependency list, net8 is `Microsoft.Data.Sqlite`, `System.Drawing.Common` 8.x (`System.Text.Json` is in-box); in the RVTuk.Revit paragraph, the API packages are pinned to the configuration's year (`2024.*`–`2027.*`) by `RevitYear` from `Directory.Build.props`.

```powershell
git add -A
git commit -m "build: one build matrix for the five Revit years"
```

### Task 4: `Test-All.ps1` and the ignore file

**Files:**
- Create: `build/Test-All.ps1`
- Modify: `.gitignore:5-7,40`, `src/RVTuk.Core/Shared/Config/AppConfig.cs:75-78`, `tests/RVTuk.Core.Tests/Shared/AppConfigTests.cs:174-175`, `CLAUDE.md`, `README.md`

**Interfaces:**
- Produces: `.\build\Test-All.ps1` — exit code 0 only when every test passed on net48, net8 and net10 and no untracked drawing exists. The engine plan adds the installer tests to it.

- [ ] **Step 1: Create `build/Test-All.ps1`**

```powershell
# Every test on net48 (Release2024), net8 (Release2025) and net10 (Release2027). Stops at the first failure.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot)
$stray = git ls-files --others --exclude-standard -- ':(icase)*.dxf' ':(icase)*.dat' ':(icase)*.dwfx' ':(icase)*.rfa'
if ($stray) { throw "Untracked drawings - commit them as fixtures or ignore them:`n$($stray -join "`n")" }
$summary = foreach ($c in 'Release2024', 'Release2025', 'Release2027') {
    dotnet test RVTuk.sln -c $c | Tee-Object -Variable out | Out-Host
    if ($LASTEXITCODE) { throw "Tests failed: $c" }
    $lines = $out | Select-String '(Passed|Failed)!|Test summary'
    if (-not $lines) { throw "No tests ran: $c" }
    $lines | ForEach-Object { "$c  $($_.Line.Trim())" }
}
$summary
```

- [ ] **Step 2: Run it**

Run: `.\build\Test-All.ps1`
Expected: exit 0 and, per configuration, the result lines of both test projects (Core 569, UI 33). If the result lines have another wording under SDK 10, adjust the `Select-String` pattern to the lines actually printed — it must match a line that only appears when tests ran.

- [ ] **Step 3: Both guards work**

```powershell
Set-Content tests\stray.DXF 'x'
try { .\build\Test-All.ps1 } catch { $_.Exception.Message } finally { Remove-Item tests\stray.DXF }   # prints "Untracked drawings …" naming tests/stray.DXF
dotnet test RVTuk.sln -c Release2026 | Select-String '(Passed|Failed)!|Test summary'   # no output: the case "No tests ran" catches
```
If the second command prints a line although no test project is built for 2026, tighten the pattern until it prints nothing.

- [ ] **Step 4: Fix `.gitignore`**

- `:5` → `# Test results / real office drawings (never committed), at any depth`
- `:6` `tests/output/` → `**/tests/output/`
- `:7` `tests/Examples Autoarea/` → `**/tests/Examples Autoarea/`
- delete `:40` (`.gitignore` ignoring itself)
- after `installer/RVTuk.Setup/payload.zip` add `releases/`

Run: `git check-ignore -v "FamilyBrowser/tests/output/x.dxf" "AreaCalc/tests/Examples Autoarea/a.dxf" "tests/output/x.dxf"`
Expected: three lines, each naming one of the two patterns.

- [ ] **Step 5: Settle the serializer comment**

Task 3 Step 8 ran `Config_ReadsAcrossBothSerializers` on net48 and it passed: `DataContractJsonSerializer` runs the constructor of a plain class on .NET Framework too. Two comments say the opposite; reword them, change no code (the key names are frozen):

`AppConfig.cs:75-78`, the summary comment above `RoundPositionsWholeCentimetresOnly` (the property line itself, `:79`, is not touched):
```csharp
        /// <summary>Round Positions' grid step, stored inverted (false = the default, half
        /// centimetres allowed), like <see cref="DwgExportDwgOff"/>. Reading does not need the
        /// inversion: both serializers run AppConfig's constructor, on .NET Framework too
        /// (AppConfigTests.Config_ReadsAcrossBothSerializers). The key name is frozen.</summary>
```
`AppConfigTests.cs:174-175`: `// A file without these keys must read as today's behaviour: DWG on, PDF off.`

- [ ] **Step 6: Update the docs and commit**

`CLAUDE.md` "Tests": the first paragraph and its command become: `Run everything before committing: .\build\Test-All.ps1 — every test on net48 (Release2024), net8 (Release2025) and net10 (Release2027); it also refuses untracked .dxf/.dat/.dwfx/.rfa files. One project: dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj -c Release2025.` The UI paragraph: "It follows the configuration like every project" and add the `-c Release2027` command. `README.md`: add `│   └── RVTuk.UI.Tests/      # WPF tests without Revit` under `tests/`, `├── build/                         # Test-All.ps1` before `Deploy.ps1`, and `.\build\Test-All.ps1   # all tests: net48, net8, net10` in "Building".

```powershell
.\build\Test-All.ps1
git add -A
git commit -m "build: add Test-All.ps1 and unanchor the ignored test folders"
```

### Task 5: Pin the frozen identifiers

The identifiers live in `RVTuk.Revit`, which no test can load, and phase 3 moves their files. So the test counts exact source text over the whole repository: a move changes nothing, an edit or a copy does.

**Files:**
- Create: `tests/RVTuk.Core.Tests/Shared/FrozenIdentifierTests.cs`

- [ ] **Step 1: Write the test**

Class `FrozenIdentifierTests`, namespace `RVTuk.Core.Tests.Shared`, one `[Fact] public void EveryFrozenIdentifier_IsInTheSourceExactlyAsOftenAsExpected()`:
- Root: walk up from `AppContext.BaseDirectory` to the first folder holding `RVTuk.sln`; `Assert.NotNull`.
- Sources: the text of every `*.cs` and `*.csproj` under the root whose relative path has no segment equal to `bin`, `obj` or `tests` (ignore case) and none starting with `.`; a folder below the root that holds a `.git` entry (a worktree or another copy of the repository) is skipped whole.
- For each table row `count|snippet` (split at the first `|`): the number of ordinal occurrences of the snippet in all sources must equal the count. Collect `"<snippet>: expected <n>, found <m>"` for every row that differs and end with `Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures))` — `Assert.Empty` would print five cut-off items.
- The table is a raw string literal, one row per line (split it with `Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)`: the file is checked out with CRLF), exactly:

```text
1|new Guid("7b3c1d92-4e58-4a0f-9c21-6d5f8e3a17b4")
1|SchemaName = "RVTukAutoDimensionTrackingByView"
1|FieldName = "DimensionIdsByViewId"
1|new Guid("6f2c81d4-9a7b-4e35-8c10-b4d6e27f9a51")
1|SchemaName = "RVTukAutoDimensionsScopeSelectionV3"
1|CategoryMaskField = "CategoryMask"
1|ViewIdsField = "CheckedViewIds"
1|DimensionTypeIdField = "DimensionTypeId"
1|AddSimpleField(CategoryMaskField, typeof(int))
1|AddSimpleField(ViewIdsField, typeof(string))
1|AddSimpleField(DimensionTypeIdField, typeof(string))
1|new Guid("6d21f8b4-0c93-4e57-a8d6-b45f70e29c18")
1|SchemaName = "RVTukTopoLineElevation"
1|FieldName = "SharedElevation"
1|AddSimpleField(FieldName, typeof(double)).SetSpec(SpecTypeId.Length)
1|new Guid("3a6c50f1-8b47-4d2e-95c3-1f8a4b7e0d62")
1|SchemaName = "RVTukTopoToolsPointLedger"
1|FieldName = "PointsByLineId"
1|new Guid("a9b2b113-115d-48a4-80a5-3063ddc3caca")
1|SchemaName = "RVTukRoomFloorsLink"
1|FieldName = "RoomUniqueId"
1|new Guid("309b77b4-64cd-45c7-bd73-d6a81b1f85ae")
1|SchemaName = "RVTukTopoToolsParkedParameters"
1|FieldName = "Entries"
4|AddSimpleField(FieldName, typeof(string))
6|builder.SetVendorId("KnafoKlimor");
6|builder.SetReadAccessLevel(AccessLevel.Public);
6|builder.SetWriteAccessLevel(AccessLevel.Public);
1|OurVendorId = "KnafoKlimor"
1|new DockablePaneId(new Guid("e5d47b31-2c8a-4f16-b0d9-73a5e91c46f2"))
1|new DockablePaneId(new Guid("a17c46e9-5b83-4d20-8f6a-9c4e2b70d135"))
1|new DockablePaneId(new Guid("88a9c4fa-9c6a-4b02-b169-701be8358090"))
1|new DockablePaneId(new Guid("b7a1c94e-3f52-4d08-9c6b-1e84a5f2d370"))
1|UsageTypeParam = "RZ_USAGE_TYPE"
1|UsageTypeOldParam = "RZ_USAGE_TYPE_OLD"
1|PermitAreaParam = "RZ_AREA"
1|AssetParam = "RZ_ASSET"
1|BuildingNoParam = "RZ_BUILDING_NO"
1|(UsageTypeParam, new Guid("737fe9fb-a9ab-4e92-b951-bafbeb34eec0"))
1|(UsageTypeOldParam, new Guid("50640c88-869d-40cc-b9c0-d82180e83a57"))
1|(PermitAreaParam, new Guid("e59fbd50-19bb-4185-a7d6-6dc9191c7c4f"))
1|(AssetParam, new Guid("7af015fb-bc87-438a-93c8-869578f37f67"))
1|(BuildingNoParam, new Guid("e8702634-4fc1-4bb0-b0e1-2d944aa3944a"))
1|Groups.get_Item("RVTuk") ?? sharedFile.Groups.Create("RVTuk")
1|NewUsageScheduleName = "New Usage"
1|OldUsageScheduleName = "Old Usage"
1|EnsureSchedule(doc, NewUsageScheduleName, "New Usage",
1|EnsureSchedule(doc, OldUsageScheduleName, "Old Usage",
1|<LogicalName>RVTuk.Core.RishuiZamin.DxfTemplates.Preamble.dxf</LogicalName>
1|<LogicalName>RVTuk.Core.RishuiZamin.DxfTemplates.Postamble.dxf</LogicalName>
1|"RVTuk.Core.RishuiZamin.DxfTemplates." + fileName
1|OuterLineStyleName = "_DP-Dim Outer"
1|InnerLineStyleName = "_DP-Dim Inner"
1|RefLineStyleName = "_DP-Dim Ref"
1|LineStyleName = "_DP-Topo Line"
1|"BrowseLibrary"
2|"AreaCalc"
1|"DwgExport"
1|"RoundPositions"
1|"AutoDimensions"
1|"TopoTools"
1|"RoomFloors"
1|"NeoProperties"
3|"RVTuk.db"
1|DbFolderName = ".DB"
1|LegacyDbFolderName = ".Setup"
1|"Autodesk", "Revit", "Addins", "RVTuk"
1|"config.json"
1|ParameterName = "_Version"
1|LibraryFolderPath { get; set; }
1|IgnoredSubfolders { get; set; }
1|IgnoredFilePatterns { get; set; }
1|AreaCalcOutputFolder { get; set; }
1|AreaCalcMarkerForm { get; set; }
1|DwgExportFolder { get; set; }
1|DwgExportPdfSetupName { get; set; }
1|DwgExportDwgSetupName { get; set; }
1|DwgExportSheetSetName { get; set; }
1|DwgExportUseCurrentWindow { get; set; }
1|DwgExportViewNamingSetupName { get; set; }
1|DwgExportSeparatePdfFolder { get; set; }
1|DwgExportCopyMissingSetups { get; set; }
1|DwgExportCreateZip { get; set; }
1|DwgExportZipMode { get; set; }
1|DwgExportDwgOff { get; set; }
1|DwgExportPdfOn { get; set; }
1|DwgExportFolders { get; set; }
1|DwgExportPdfFolder { get; set; }
1|DwgExportPdfFolders { get; set; }
1|TopoPointSpacingCentimetres { get; set; }
1|RoundPositionsWholeCentimetresOnly { get; set; }
1|ModelKey { get; set; }
1|string Folder { get; set; }
0|RZ_UsageType
0|f80b9154-f0e7-4f01-82d0-3ec259872e5c
0|RZ Usage Key Schedule
```

Above the table, a comment: a row changes only on purpose — `"AreaCalc"` becomes 1 in phase 3's Area Calc step, which deletes `src\KKarea.Revit`; the last three rows are retired names that must never come back.

- [ ] **Step 2: Run it**

Run: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj -c Release2025 --filter FrozenIdentifierTests`
Expected: 1 passed. A failing row names its snippet and both counts; the counts above were measured at `f8cd1f0` — check the row against the code before changing either.

- [ ] **Step 3: Prove it catches an edit and a copy**

```powershell
(Get-Content src\RVTuk.Revit\RoomFloors\RoomFloorLinkStore.cs -Raw).Replace('a9b2b113', 'a9b2b114') | Set-Content src\RVTuk.Revit\RoomFloors\RoomFloorLinkStore.cs -NoNewline
dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj -c Release2025 --filter FrozenIdentifierTests   # fails: new Guid("a9b2b113-…"): expected 1, found 0
git checkout -- src\RVTuk.Revit\RoomFloors\RoomFloorLinkStore.cs
Copy-Item src\RVTuk.Revit\TopoTools\TopoToolsPaneProvider.cs src\RVTuk.Revit\TopoTools\Copy.cs
dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj -c Release2025 --filter FrozenIdentifierTests   # fails: the Topo pane id: expected 1, found 2
Remove-Item src\RVTuk.Revit\TopoTools\Copy.cs
```

- [ ] **Step 4: Run everything and commit**

```powershell
.\build\Test-All.ps1     # Core 570 on each framework
git add tests
git commit -m "test: pin the frozen identifiers by source text"
```

### Task 6: The theme class and the view smoke test

**Files:**
- Create: `src/RVTuk.UI/Shared/Themes/DarkThemeDictionary.cs`, `tests/RVTuk.UI.Tests/Shared/ViewSmokeTests.cs`
- Modify (one line plus one attribute each): `src/RVTuk.UI/AutoDimensions/Views/AutoDimensionsPaneView.xaml:8`, `DwgExporter/Views/DwgExportWindow.xaml:12`, `FamilyBrowser/Views/FamilyBrowserWindow.xaml:13`, `FamilyBrowser/Views/ImageCropWindow.xaml:10`, `FamilyBrowser/Views/IndexProgressWindow.xaml:12`, `FamilyBrowser/Views/InstructionsEditorWindow.xaml:14`, `FamilyBrowser/Views/SettingsWindow.xaml:12`, `NeoProperties/Views/NeoPropertiesView.xaml:8`, `RishuiZamin/Views/RishuiZaminWindow.xaml:13`, `RoundPositions/Views/RoundPositionsPaneView.xaml:9`, `TopoTools/Views/TopoToolsPaneView.xaml:8`

**Interfaces:**
- Produces: `RVTuk.UI.Shared.Themes.DarkThemeDictionary` — a `ResourceDictionary` with a public parameterless constructor. Phase 3 links this file and `DarkTheme.xaml` into every add-in's UI project.

- [ ] **Step 1: Create the class** (proven outside Revit on .NET Framework 4.8 and .NET 8 — spec "Phase 1 record", S2)

```csharp
using System;
using System.Windows;

namespace RVTuk.UI.Shared.Themes
{
    /// <summary>The dark theme, loaded from whichever assembly this file is compiled into.</summary>
    public class DarkThemeDictionary : ResourceDictionary
    {
        public DarkThemeDictionary()
        {
            var name = typeof(DarkThemeDictionary).Assembly.GetName().Name;
            Source = new Uri("/" + name + ";component/Shared/Themes/DarkTheme.xaml", UriKind.Relative);
        }
    }
}
```
`RVTuk.UI.csproj` does not change: `DarkTheme.xaml` is already a `Page` at `Shared\Themes\`.

- [ ] **Step 2: Write the failing smoke test**

`ViewSmokeTests`, namespace `RVTuk.UI.Tests.Shared`, one `[Fact] public void EveryView_ConstructsWithTheDarkTheme() => Sta.Run(() => { … });`:
- Views: every non-abstract type in `typeof(DarkThemeDictionary).Assembly` assignable to `Window` or `UserControl`. `Assert.Equal(11, views.Count)`.
- Construction: a public parameterless constructor through `Activator.CreateInstance`, except these five, built in a `Dictionary<Type, Func<FrameworkElement>>`:
  - `DwgExportWindow`: `new DwgExportWindow(new DwgExportViewModel(new PdfSetupItem[0], new string[0], new SheetSetItem[0], "", new ModelSetupInventory[0], "", null!, null!, null!))`
  - `RishuiZaminWindow`: `new RishuiZaminWindow(new RishuiZaminViewModel(null!, null!, null!))`
  - `ImageCropWindow`: `new ImageCropWindow(BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4))`
  - `FamilyBrowserWindow`: `new FamilyBrowserWindow(new AppConfig { LibraryFolderPath = <temp folder> }, null!, null!, null!, null!, null!, null!, null!, null!)`
  - `InstructionsEditorWindow`: `new InstructionsEditorWindow(new FamilyBrowserItemViewModel(new FamilyBrowserItem { Id = id, FileName = "A.rfa" }), <temp>\A.rfa, repo)` with `id` and `repo` made as `InstructionsEditorViewModelTests.Arrange` makes them (`IndexRepository.InsertFamily("Doors/A.rfa", "A.rfa")`, then `new BrowserRepository(db)`).
  A view that has neither is reported by name — that is what keeps the test covering every view.
- Per view, collect a failure line unless: construction did not throw, `view.Resources.MergedDictionaries.OfType<DarkThemeDictionary>().Any()`, and `((SolidColorBrush)view.FindResource("Brush.Bg")).Color == Color.FromRgb(0x2B, 0x2B, 0x2B)`. End with `Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures))`.
- **Never `Show()` or `Close()` a view**: closing an unshown `InstructionsEditorWindow` opens a message box and the test hangs. At the end dispose the editor's `repo` and call `((FamilyBrowserWindow)view).ViewModel.Dispose()` (it holds the other open database), then delete the temp folder best effort.
- The view models read the user's real `config.json` when constructed (read only); that is accepted.

Run: `dotnet test tests\RVTuk.UI.Tests\RVTuk.UI.Tests.csproj -c Release2025 --filter ViewSmokeTests`
Expected: FAIL, 11 lines saying the view does not merge `DarkThemeDictionary`. Construction errors are not expected: fix the test, not the view.

- [ ] **Step 3: Switch the 11 views**

In each file: add the attribute `xmlns:theme="clr-namespace:RVTuk.UI.Shared.Themes"` to the root element (no `;assembly=` part), and replace the line `<ResourceDictionary Source="/RVTuk.UI;component/Shared/Themes/DarkTheme.xaml"/>` with `<theme:DarkThemeDictionary/>`, same indentation. Nothing else changes.

- [ ] **Step 4: Verify**

```powershell
git grep -n "RVTuk.UI;component" -- src     # no output
.\build\Test-All.ps1                         # UI 34 on each framework, Core 570
```

- [ ] **Step 5: Commit**

```powershell
git add src tests
git commit -m "refactor(ui): load the dark theme through DarkThemeDictionary"
```

### Task 7: Checks in Revit, and the record

**Files:**
- Modify: `docs/toolkit/specs/2026-11-12-addin-split-design.md`

- [ ] **Step 1: [Daniel] Deploy** — all Revit windows closed, administrator PowerShell in `D:\Coding\RVTuk-split`: `.\Deploy.ps1`. Expected: every installed year reports a deploy, none is skipped.

- [ ] **Step 2: [Daniel] Look, in three Revit versions**

| Where | Do | Pass |
|---|---|---|
| Revit 2024 (net48) | Start. Open the Auto Dimensions, Topo Tools and Round Positions panes. Open the Family Browser, then Edit Info on a family with an image, then crop the image. Open DWG Export and Area Calc. | No error at start. Everything looks as it did before: dark, same colours and buttons. |
| Revit 2027 (the net10 build) | Start. Open the three panes and the Family Browser. Open an image instruction that was saved in Revit 2024, save it, then open it again in Revit 2024. | Panes and window are dark. The instruction shows its text and image in both versions. |
| Revit 2026.5 (the net8 build on .NET 10) | Start. Open the three panes and the Family Browser. Open an image instruction that was saved in Revit 2024, save it, then open it again in Revit 2024. | Panes and window are dark. The instruction shows its text and image in both versions. |

A view that comes up white or with default grey buttons, or an error naming `DarkTheme` or `DarkThemeDictionary`: stop and report the exact text.

- [ ] **Step 3: Record it in the spec**

Add a section "## Phase 2 record (build half)" before "## Phase 1 record", with the date and commit, and only what was measured: the test counts per framework from the last `Test-All.ps1` run; that the shipped file lists were identical before and after the matrix (or the one difference); that `DataContractJsonSerializer` runs the constructor on .NET Framework (the test and the run); that shadow copy was the cause of the SQLite failure in net48 tests; the three Revit checks with their results; and that Revit 2026 gets the full RVTuk add-in since Task 8. In "Build", put into the past tense the sentences that describe today's state: the per-project rows ("Since PR #12 and PR #14 …"), and the `Debug` catch-all that the tests built through. In "Known risks", the Topo bullet about `AddPoint` now names 2026 as well as 2027.

- [ ] **Step 4: Commit, then ask Daniel about uploading**

```powershell
git add docs
git commit -m "docs(toolkit): record the build half of phase 2"
git status --short; git log --oneline origin/master..HEAD
```
Expected: a clean tree and seven commits ahead of `origin/master`. Ask Daniel whether to upload them; push only on his yes.

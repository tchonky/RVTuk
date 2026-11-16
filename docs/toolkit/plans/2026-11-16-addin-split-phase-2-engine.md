# Add-in split, phase 2 (Foundation), install-engine half — Implementation Plan

> **Executed 2026-11-16; the code now differs from Task 3's algorithm** (three review fix rounds, see the spec's "Phase 2 record (engine half)"): before step 1 the engine creates the year folder, refuses every path it uses that is a link, and refuses a year folder without a trusted owner (Administrators group, system, current user). Decide (step 3) and "Ours" also require trusted owners of `addin.json` and the manifest. Every folder rename retries, not only step 6. `<key>.new` is checked (no link, trusted owner) before the copy. The probe skips links and clears read-only flags. The manifest's `.tmp` is created new, and a manifest that is a folder or read-only is replaced. Deletes never enter a link. `Engine.cs` is the reference.
>
> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Install, update and remove exist once, in a tested C# engine inside the setup exe; one staging function feeds both `Deploy.ps1` and `Build-Installer.ps1`; a staging check proves each staged folder holds exactly what it should — all on today's two add-ins (RVTuk and KKarea), before any tool moves.

**Architecture:** Each add-in describes itself in an `addin.props` beside its host project. `build\Staging.ps1` reads those files, copies each build output into `<staging>\<Key>\<year>\` by today's rules, and writes `addin.json` per folder and one `catalog.json`. The setup exe (`installer\RVTuk.Setup`, net48) gains three small files — catalog data, the machine it runs on (injected), and the engine — and `Program.cs` becomes argument parsing around them. The engine reads the catalog from an embedded zip or a `--source` folder, decides per add-in and year, probes for locked files, swaps folders by rename and writes the manifest. `Deploy.ps1` and `Build-Installer.ps1` shrink to "stage, then call the engine".

**Tech Stack:** C# on .NET Framework 4.8 (no NuGet packages in the exe; `DataContractJsonSerializer`), xunit 2 on net48, PowerShell (staging: 5.1 and 7; the staging check: 7 only), MSBuild.

**Spec:** `docs/toolkit/specs/2026-11-12-addin-split-design.md` — "Staging and deploy", "Installer" (Engine, Command line), "Build" (the `addin.props` bullet), phase table row 2 (the *Engine* half), and the header note of 2026-11-16 about this plan.

## Not in this plan

- The setup **window**, `Release.ps1`, the release store, `released-years.json`, `--preview`, and the change of the exe to a Windows-subsystem program: phase 4.
- Real "old installs it replaces" lines in the catalog: each is added by the phase 3 or phase 5 step that deletes the old project. The engine supports the rule and tests cover it; the catalogs this plan produces carry none.
- Per-add-in folders (`Tools/`, `FamilyBrowser/` …): phase 3. Until then `addin.props` sits beside the host project.

## Global Constraints

- No frozen identifier changes. The two ClientIds move from the scripts into `addin.props` byte-identical: RVTuk `D71D7480-4A21-474E-A47E-3E8DF8C1BDA5`, KKarea `9C97B9F2-60F9-432D-92A4-5EC2A0FDAFFC`. VendorId stays `KnafoKlimor`, VendorDescription `Knafo Klimor Architects LTD`.
- What Revit sees must not change: manifest names `RVTuk.addin` / `KKarea.addin`, install folders `RVTuk` / `KKarea`, the manifest's bytes, and the set of files in each add-in folder — plus one new file, `addin.json`.
- Machine-wide path rule: `C:\ProgramData\Autodesk\Revit\Addins\{year}` up to 2026, `C:\Program Files\Autodesk\Revit\Addins\{year}` from 2027.
- The setup exe stays a single net48 exe with **no NuGet dependencies**, outside `RVTuk.sln`, built with `-c Release`.
- The engine never touches settings, the family database or crash logs. It runs elevated, so **every name that reaches a path** (add-in key, manifest name, old-install folder) must be a plain file name: no `\`, `/`, `:` or `..`.
- **Never run `Deploy.ps1`, anything elevated, or the setup exe without `--target`** in an agent session: those write into the real Revit add-in folders. Never start or close Revit. Steps marked **[Daniel]** are his.
- All commands run from `D:\Coding\RVTuk-split` in PowerShell 7. Never read or write `D:\Coding\RVTuk`. A .NET method (`[IO.File]::…`, `ZipFile`) resolves a relative path against the folder the shell **started** in, not against `Set-Location`: give .NET methods absolute paths only. Every `dotnet build`/`dotnet test` of a solution project carries `-c Release20xx`.
- `CLAUDE.md` / `README.md` change in the same commit as what they describe. No push without Daniel's OK.

## Review Focus

1. **A running Revit that the folder rename does not notice.** On .NET 8 a loaded add-in folder can be renamed (Facts below), so a swap would succeed under an open Revit 2025–2027 and leave it running mixed DLLs. Expected: nothing changes for that year. — Task 3, the lock probe and its tests.
2. **A crash or a failed step that leaves a broken install reported as fine.** Expected: the next run repairs it; "unchanged" is only said when the folder, its `addin.json` and its manifest are all there. — Task 3, the recover and decide rules and their tests.
3. **A name from a file steering an elevated delete.** `catalog.json` and a foreign `addin.json` are input. Expected: a name with a path in it is refused, and "remove all" touches only folders that are provably ours. — Task 2 validation, Task 3 foreign-folder tests.
4. **A one-element list written by PowerShell** (`"years": "2023"` instead of `["2023"]`) makes the engine read no years for KKarea. — Task 5 writes typed arrays; Task 5 Step 5 installs from the script's catalog with the engine.
5. **A skipped year reported as success.** Today's exe exits 0 when a year was skipped or half-deleted. Expected: exit 1, and the folder untouched. — Task 3 `Engine.Failed`, Task 4 exit-code test, Task 5 Step 6.

## Facts established while planning (2026-11-16, at `35b2c47`)

- `Deploy.ps1:32-53` and `Build-Installer.ps1:25-46` hold the same five-row table (year → config, tfm, add-in, project, entry class, ClientId) and the same copy rules; both delete the add-in folder first. With one file held open the setup exe prints "skipped (locked)", **exits 0**, and leaves a half-deleted folder beside an intact manifest.
- **The folder rename is not a lock test on .NET 8.** Probe with small hosts: on .NET Framework a host that loaded a managed DLL (`Assembly.LoadFrom`) blocks `Directory.Move` of the folder; on .NET 8 (`LoadFrom` and `AssemblyLoadContext`) the move **succeeds**, the host keeps running and later loads dependencies from the new folder. Renaming a single loaded DLL succeeds on both. What fails on both runtimes while a DLL is loaded: opening it with `FileAccess.ReadWrite` + `FileShare.None` (IOException `0x80070020`) and `File.Delete`. Whether real Revit behaves like the probe hosts is unverified — Task 8 checks it.
- `Directory.Move` across volumes throws before creating anything; `Directory.Delete(recursive)` on a folder in use deletes what it can and then throws. Ordinary users have write access to `C:\ProgramData\Autodesk\Revit\Addins\<year>`.
- A net48 xunit project that references the net48 exe project and reaches its internals through `InternalsVisibleTo` runs under `dotnet test` without warnings; the embedded `payload.zip` (`Condition="Exists(...)"`) may be absent. Test packages in the repo: Microsoft.NET.Test.Sdk 17.14.2, xunit 2.9.3, xunit.runner.visualstudio 2.8.2.
- `DataContractJsonSerializer` writes and reads plain lists, ignores unknown members and leaves missing ones null; it throws on a UTF-8 BOM when given raw bytes but reads everything through `File.ReadAllText`; it writes `DateTime` as `\/Date(..)\/`, so times are stored as strings.
- Today's manifests are 11 lines with **CRLF** endings and a trailing CRLF, UTF-8 without BOM (2024: 418 bytes).
- Revit on the dev PC: 2023, 2024, 2026, 2027 installed (registry `HKLM\SOFTWARE\Autodesk\Revit\<year>\REVIT-05:0809\InstallationLocation`, 64-bit view, value ends in `\`); no 2025, though `ProgramData\…\Addins\2025` exists with RVTuk and KKompare in it. `Revit.exe` `FileMajorPart + 2000` = year for all four. Installed add-ins: KKarea (2023), RVTuk (2024–2027), KKompare (2024–2026); none has an `addin.json`.
- Applying `Deploy.ps1`'s rules to today's outputs stages 12 / 12 / 14 / 14 / 13 files for 2023–2027, the same names as installed on the dev PC. The only `*.json` in any output is `<Host>.deps.json` (net8/net10).
- Assembly metadata can be read without loading or locking (`System.Reflection.Metadata` over a byte copy) — in PowerShell 7 only; Windows PowerShell 5.1 does not have that library. RevitAPI's reference version has major = year − 2000 (minor and build float).
- A stale `src\KKarea.Revit\bin\2026` exists (KKarea built for 2026 until 2026-11-16); nothing may pick it up.
- **A dry run of Tasks 1–7** was done in a throwaway clone on 2026-11-16 from the first draft of this plan: every step ran, the counts matched (70 baseline lines; staged folders 13 / 13 / 15 / 15 / 14), and it produced 76 findings, all worked into this text. The clone and the probe scripts lived in a temporary folder that the system has since cleared: there are no drafts to copy from, and this text is complete without them.

## File structure

| File | Responsibility |
|---|---|
| `src/RVTuk.Revit/addin.props`, `src/KKarea.Revit/addin.props` (new) | What one add-in is: key, name, manifest, ClientId, entry class, version, years, assemblies |
| `build/Staging.ps1` (new) | `Get-AddIns`, `Get-Tfm`, `Copy-AddInOutput`, `New-Staging`: build outputs → `<staging>\<Key>\<year>\` + `addin.json` + `catalog.json` |
| `build/Test-Staging.ps1` (new) | The staging check |
| `installer/RVTuk.Setup/Catalog.cs` (new) | `Catalog`, `CatalogAddIn`, `OldInstall`, `AddInInfo`; JSON read; name validation |
| `installer/RVTuk.Setup/Machine.cs` (new) | `IMachine`; `RealMachine` (path rule, registry, processes); `TargetMachine` (`--target`) |
| `installer/RVTuk.Setup/Engine.cs` (new) | Install / update / remove of one add-in in one year; results |
| `installer/RVTuk.Setup/Program.cs` | Arguments, payload, elevation, log, exit code |
| `installer/RVTuk.Setup.Tests/` (new) | xunit, net48: `CatalogTests`, `EngineTests`, `ProgramTests`, `FakeMachine` |
| `Deploy.ps1`, `Build-Installer.ps1` | Stage, then call the engine |
| `build/Test-All.ps1` | Also runs the setup tests |

---

### Task 1: Spec notes and baseline

**Files:** `docs/toolkit/specs/2026-11-12-addin-split-design.md` (already edited in the working tree), this plan.

- [ ] **Step 1: Check the workspace**

Run: `git status --short; git log --oneline -1`
Expected: `master` at `35b2c47` or later; exactly two lines: the spec modified (` M`) and this plan untracked (`??`).

- [ ] **Step 2: Commit the documents**

```powershell
git add docs/toolkit/specs/2026-11-12-addin-split-design.md docs/toolkit/plans/2026-11-16-addin-split-phase-2-engine.md
git commit -m "docs(toolkit): plan the install-engine half of phase 2"
```

- [ ] **Step 3: Record what today's installer installs**

```powershell
$base = "$env:TEMP\rvtuk-phase2-engine"; Remove-Item $base -Recurse -Force -ErrorAction SilentlyContinue; New-Item -ItemType Directory $base | Out-Null
.\Build-Installer.ps1
.\dist\RVTukSetup.exe --all --target "$base\before" --no-pause
Get-ChildItem "$base\before" -Recurse -File -Name | Sort-Object | Set-Content "$base\before.txt"
(Get-Content "$base\before.txt").Count
```
Expected: `Build-Installer.ps1` ends without error; the count is 70 (12+12+14+14+13 files and 5 manifests). `$base\before\<year>\<Name>.addin` are the manifests Task 5 compares with, byte by byte.

- [ ] **Step 4: Record today's tests**

Run: `.\build\Test-All.ps1`
Expected: exit 0; Core 585 and UI 34 on net48, net8.0 and net10.0.

### Task 2: Catalog, machine, and the test project

**Files:**
- Create: `installer/RVTuk.Setup/Catalog.cs`, `installer/RVTuk.Setup/Machine.cs`, `installer/RVTuk.Setup.Tests/RVTuk.Setup.Tests.csproj`, `installer/RVTuk.Setup.Tests/xunit.runner.json`, `installer/RVTuk.Setup.Tests/CatalogTests.cs`, `installer/RVTuk.Setup.Tests/FakeMachine.cs`
- Modify: `installer/RVTuk.Setup/RVTuk.Setup.csproj`, `build/Test-All.ps1`, `CLAUDE.md`

**Interfaces — Produces** (namespace `RVTuk.Setup`, everything `internal`):

```csharp
[DataContract] sealed class Catalog {
    [DataMember(Name = "built")]  public string Built;      // "2026-11-16T10:00:00Z"
    [DataMember(Name = "commit")] public string Commit;     // "35b2c47" or "35b2c47-dirty"
    [DataMember(Name = "addIns")] public List<CatalogAddIn> AddIns;
    public static Catalog Read(string path);                // File.ReadAllText, then the serializer, then Validate; throws InvalidDataException with the reason
    public CatalogAddIn Find(string key);                   // ignore case; null when absent
    public static bool IsPlainName(string s);               // not null/empty, no '\', '/', ':' and not "." or ".." and no ".." inside
}
[DataContract] sealed class CatalogAddIn {
    [DataMember(Name = "key")]        public string Key;        // install folder and --install name: "RVTuk"
    [DataMember(Name = "name")]       public string Name;       // display name
    [DataMember(Name = "manifest")]   public string Manifest;   // "RVTuk.addin"
    [DataMember(Name = "entryClass")] public string EntryClass;
    [DataMember(Name = "assembly")]   public string Assembly;   // "RVTuk.Revit.dll"
    [DataMember(Name = "clientId")]   public string ClientId;
    [DataMember(Name = "version")]    public string Version;
    [DataMember(Name = "dev")]        public bool Dev;
    [DataMember(Name = "years")]      public List<string> Years;
    [DataMember(Name = "replaces")]   public List<OldInstall> Replaces;   // never null after Read
}
[DataContract] sealed class OldInstall {
    [DataMember(Name = "manifest")] public string Manifest;     // "KKarea.addin"
    [DataMember(Name = "folder")]   public string Folder;       // "KKarea"
}
[DataContract] sealed class AddInInfo {                         // addin.json inside an add-in folder
    [DataMember(Name = "key")]      public string Key;
    [DataMember(Name = "version")]  public string Version;
    [DataMember(Name = "commit")]   public string Commit;
    [DataMember(Name = "built")]    public string Built;
    [DataMember(Name = "manifest")] public string Manifest;
    [DataMember(Name = "dev")]      public bool Dev;
    public static AddInInfo TryRead(string folder);             // null when addin.json is missing or cannot be parsed
}
interface IMachine {
    string YearFolder(string year);      // the folder that holds <Key>\ and <Key>.addin for that year
    bool IsInstalled(string year);
    bool IsRunning(string year);
}
sealed class RealMachine : IMachine { }                         // path rule, registry, processes
sealed class TargetMachine : IMachine { public TargetMachine(string dir); }  // YearFolder = dir\<year>; installed: always; running: never
```
`Catalog.Read` validates and throws `InvalidDataException` unless: `AddIns` is not empty; every `Key`, `Manifest`, `Assembly`, and every `Replaces` entry's `Manifest` and `Folder` passes `IsPlainName`; every manifest name ends in `.addin`; every `Years` entry is four digits; no key occurs twice (ignore case); no key ends in `.new` or `.old`. A null `Years` or `Replaces` becomes an empty list.
`FakeMachine` (test project): `YearFolder` = `<root>\<year>`, with settable `HashSet<string> Installed` and `Running`.

- [ ] **Step 1: The test project**

`RVTuk.Setup.Tests.csproj`: SDK-style, `net48`, `LangVersion latest`, `Nullable disable`, `PlatformTarget x64`, `IsPackable false`; packages `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio` with the same version expressions as `tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj`; `ProjectReference` to `..\RVTuk.Setup\RVTuk.Setup.csproj`; `xunit.runner.json` (`{ "shadowCopy": false }`) copied to the output as the other test projects do. It lives under `installer\`, so the empty `installer\Directory.Build.props` keeps it out of the build matrix; it is **not** added to `RVTuk.sln`.

In `RVTuk.Setup.csproj` add `<PlatformTarget>x64</PlatformTarget>` (Revit is 64-bit only: a 32-bit process reads the wrong registry view and cannot read a Revit process's module), `<Reference Include="System.Runtime.Serialization" />`, and `<InternalsVisibleTo Include="RVTuk.Setup.Tests" />`. Reword its header comment: the exe is built by `Build-Installer.ps1` (with a payload) and by `Deploy.ps1` (which hands it a staged folder). An XML comment must not contain two hyphens in a row, so do not write a flag name there.

- [ ] **Step 2: Write the failing tests** — `CatalogTests`

```csharp
[Fact] public void Read_ReadsWhatPowerShellWrites()      // a file as ConvertTo-Json writes it (2-space indent), saved WITH a UTF-8 BOM:
                                                          // two add-ins; KKarea has years ["2023"] and "replaces": []; RVTuk has four years and one replaces entry {"manifest":"Old.addin","folder":"Old"}
    // assert: AddIns.Count == 2; Find("kkarea").Years is ["2023"]; Find("RVTuk").Replaces[0].Folder == "Old"; Dev == true; Built and Commit as written
[Fact] public void Read_WithoutReplaces_GivesAnEmptyList() // an entry with no "replaces" member → Replaces is empty, not null
[Theory] public void Read_RejectsUnsafeOrBrokenCatalogs(string json)   // each throws InvalidDataException. Five cases, each one valid entry changed in one place:
    // key "..\\X" ; manifest "sub\\A.addin" ; manifest "A.txt" ; replaces folder "C:\\Windows" ; two entries with keys "A" and "a"
[Fact] public void Find_UnknownKey_IsNull()
[Fact] public void AddInInfo_TryRead_MissingFile_IsNull()
[Fact] public void AddInInfo_TryRead_Garbage_IsNull()     // addin.json holding the text: not json
[Fact] public void AddInInfo_TryRead_ReadsAFile()         // {"key":"RVTuk","version":"1.2.0","commit":"abc","built":"2026-11-16T10:00:00Z","manifest":"RVTuk.addin","dev":false}
[Fact] public void TargetMachine_PutsEveryYearUnderTheFolder()   // YearFolder("2027") == Path.Combine(dir, "2027"); IsInstalled true; IsRunning false
[Fact] public void RealMachine_FollowsThePathRule()       // YearFolder("2026") == @"C:\ProgramData\Autodesk\Revit\Addins\2026"; YearFolder("2027") == @"C:\Program Files\Autodesk\Revit\Addins\2027"
[Fact] public void RealMachine_InstalledMatchesRevitExe() // for 2023..2027: IsInstalled(y) == File.Exists($@"C:\Program Files\Autodesk\Revit {y}\Revit.exe")
[Fact] public void TestsRunAs64Bit()                      // Assert.True(Environment.Is64BitProcess)
```

Run: `dotnet test installer\RVTuk.Setup.Tests\RVTuk.Setup.Tests.csproj -c Release`
Expected: FAIL to compile (types missing).

- [ ] **Step 3: Implement `Catalog.cs` and `Machine.cs`**

JSON through `DataContractJsonSerializer` over `Encoding.UTF8.GetBytes(File.ReadAllText(path))`. `RealMachine.YearFolder`: the two constants `Program.MachineAddinsBase` holds today, plus `\<year>`. `IsInstalled`: open `HKLM\SOFTWARE\Autodesk\Revit\<year>` in the 64-bit view, take `InstallationLocation` from any subkey, true when `Revit.exe` exists there; else true when `C:\Program Files\Autodesk\Revit <year>\Revit.exe` exists. `IsRunning(year)`: true when a process named `Revit` has `MainModule.FileVersionInfo.FileMajorPart + 2000 == year`, **and also true when a `Revit` process's `MainModule` cannot be read at all** (an unknown Revit counts as every year: skipping is the safe side).

- [ ] **Step 4: Run the tests**

Run: `dotnet test installer\RVTuk.Setup.Tests\RVTuk.Setup.Tests.csproj -c Release`
Expected: 15 passed (ten facts, one theory with five cases).

- [ ] **Step 5: Let `Test-All.ps1` run them**

After the solution loop add a second call, `dotnet test installer\RVTuk.Setup.Tests\RVTuk.Setup.Tests.csproj -c Release`, with the same handling as the loop (non-zero exit → throw `Tests failed: Setup`; no `(Passed|Failed)!` line → throw `No tests ran: Setup`), and add its result line to the summary with the prefix `Setup`.

Run: `.\build\Test-All.ps1`
Expected: exit 0; the summary ends with a `Setup  Passed! … Passed:    15` line.

- [ ] **Step 6: Docs and commit**

`CLAUDE.md` "Tests": one sentence — `The setup exe's tests (installer\RVTuk.Setup.Tests, net48, outside RVTuk.sln) run at the end of Test-All.ps1; alone: dotnet test installer\RVTuk.Setup.Tests\RVTuk.Setup.Tests.csproj -c Release.`

```powershell
git add installer build CLAUDE.md
git commit -m "feat(setup): catalog and machine model for the install engine, with tests"
```

### Task 3: The engine

**Files:**
- Create: `installer/RVTuk.Setup/Engine.cs`, `installer/RVTuk.Setup.Tests/EngineTests.cs`

**Interfaces:**
- Consumes: Task 2's types (`Catalog`, `CatalogAddIn`, `OldInstall`, `AddInInfo.TryRead`, `Catalog.IsPlainName`, `IMachine`, `FakeMachine`).
- Produces:

```csharp
enum Outcome { Installed, Updated, Removed, Unchanged, Newer, SkippedRevitOpen, SkippedInUse, Failed }
sealed class Result { public string AddIn; public string Year; public Outcome Outcome; public string Message; }
static class Engine {
    // keys: null = every add-in of the catalog. years: null = the candidate years the machine reports as installed.
    public static List<Result> Install(Catalog catalog, string sourceDir, IMachine machine,
                                       ICollection<string> keys, ICollection<string> years, Action<string> log);
    // keys: null = every add-in of the catalog AND every folder on disk that is provably ours (see "Ours").
    // A key the catalog does not hold is removed only if its folder is "ours".
    public static List<Result> Remove(Catalog catalog, IMachine machine,
                                      ICollection<string> keys, ICollection<string> years, Action<string> log);
    public static bool Failed(IEnumerable<Result> results);   // any SkippedRevitOpen, SkippedInUse or Failed
    public static string ManifestXml(CatalogAddIn a);
}
```

**The algorithm (binding; the tests below do not determine all of it).**

*Words.* For one add-in `a` in one year: `yf = machine.YearFolder(year)`, `target = yf\<a.Key>`, `fresh = yf\<a.Key>.new`, `old = yf\<a.Key>.old`, `manifest = yf\<a.Manifest>`. **Candidate years** = the union of all catalog add-ins' `Years`, sorted; with `years == null`, those of them for which `machine.IsInstalled`; with `years` given, exactly those. **Never throws:** `Install` and `Remove` return one `Result` per add-in-year they considered; any exception inside one add-in-year that the steps below do not name ends that add-in-year only, with `Failed` and the exception's message, after a best-effort delete of `fresh` and `<manifest>.tmp`. **Probe** a folder = for every file under it (recursively): open with `FileMode.Open, FileAccess.ReadWrite, FileShare.None` and close; any exception → the add-in-year ends as `SkippedInUse`, message `"<file name> is in use or cannot be written"`, and nothing has been renamed or deleted. **Selection.** With `keys` given, the selected catalog entries are those that `catalog.Find(k)` returns for some `k` in `keys`, each once, in catalog order; every comparison of a key or folder name with a catalog key ignores case. **Ours** = a folder `F` directly under `yf` such that `F\addin.json` parses, its `key` equals `F`'s name (ignore case), its `manifest` passes `Catalog.IsPlainName` and ends in `.addin`, and `yf\<manifest>` exists and contains the text `<VendorId>KnafoKlimor</VendorId>`.

*Install* of `a` — for each candidate year that is also in `a.Years` (other years produce no result):
1. `machine.IsRunning(year)` → `SkippedRevitOpen`; nothing is touched.
2. **Recover.** If `target` does not exist, `old` does and `manifest` exists: move `old` back to `target` (a crash inside an earlier install). If that move throws → `Failed`, stop.
3. **Decide.** `Unchanged` or `Newer` is answered only when all of these hold: `target\addin.json` reads; `manifest` exists; none of `a.Replaces`' folders or manifests exists in `yf`; the catalog entry is not `Dev` and the installed info is not `Dev`; both versions parse. Then, comparing as `System.Version` with a missing third part taken as 0: equal → `Unchanged`, installed greater → `Newer`; nothing is written, not even `fresh`. In every other case go on.
4. Delete `fresh` if present; delete `old` if present (after step 2 it is a leftover: either `target` exists, or a remove got half way). Create `yf`. Copy `sourceDir\<a.Key>\<year>` recursively to `fresh`, clearing the read-only attribute on each copied file. A missing source folder → `Failed`, message contains `not in the payload`.
5. **Probe** `target` (if it exists) and each existing `yf\<replaces.Folder>`. On `SkippedInUse` delete `fresh` first.
6. If `target` exists: move it to `old`, retrying on `IOException`/`UnauthorizedAccessException` every 250 ms for up to 5 s (antivirus scans fresh DLLs).
7. Move `fresh` to `target`.
8. Write the manifest: `ManifestXml(a)` as UTF-8 **without BOM** to `<manifest>.tmp`, then `File.Replace` when `manifest` exists, else `File.Move`.
9. If step 6, 7 or 8 throws → **roll back by renames only**: delete `<manifest>.tmp` if present; if step 7 had succeeded, move `target` back to `fresh`; if `old` exists, move `old` to `target`; then delete `fresh` best effort. → `Failed` with the exception's message; when a rollback move throws too, add `"; the add-in is left at <a.Key>.old"` (step 2 of the next run restores it).
10. For each of `a.Replaces`: delete `yf\<Folder>.old` if present; move `yf\<Folder>` to `yf\<Folder>.old` if the folder exists; delete `yf\<Manifest>` if present; delete `yf\<Folder>.old` best effort. An exception here → `Failed`, message `"installed, but the old install <Folder> could not be removed: …"` (step 3 of the next run sees it and tries again).
11. Delete `old` best effort.
12. → `Updated` when `target` existed at step 6 or step 10 removed something, else `Installed`.

*Remove* of `a` — for each candidate year that is also in `a.Years`: `SkippedRevitOpen` as in step 1; if neither `target`, `old`, `manifest` nor any of `a.Replaces`' folders or manifests exists → `Unchanged`; **probe** `target` and each existing replaced folder; delete `fresh` and `old` if present (leftovers; nothing is recovered in a remove); if `target` exists, move it to `old` (retry as in step 6); delete `manifest` if present; delete `old` best effort; then each replaced install exactly as in step 10; → `Removed`.

*Remove* of a folder that is **ours** but not in the catalog — when `keys == null`: in every candidate year, every folder directly under `yf` that is "ours", whose name is not a catalog key and does not end in `.new` or `.old`. When `keys` names a key the catalog does not hold: `yf\<key>` in every candidate year, if it is "ours"; if it is not there or not ours → no result for that year. Such a folder is removed like a catalog add-in with no `Replaces`, its manifest name taken from its `addin.json`; the `Result.AddIn` is the folder's name. A `yf` that does not exist holds no such folders: listing one year's folder never throws out of `Remove` (no result for that year).

`ManifestXml` returns these eleven lines joined **and ended** with `"\r\n"` (so the bytes equal today's manifests whatever line endings `Engine.cs` has on disk), `{…}` filled in:

```xml
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>{Key}</Name>
    <Assembly>{Key}\{Assembly}</Assembly>
    <FullClassName>{EntryClass}</FullClassName>
    <ClientId>{ClientId}</ClientId>
    <VendorId>KnafoKlimor</VendorId>
    <VendorDescription>Knafo Klimor Architects LTD</VendorDescription>
  </AddIn>
</RevitAddIns>
```

- [ ] **Step 1: Write the failing tests** — `EngineTests`

Each test gets its own temp folder with `src\` (the payload) and `m\` (the `FakeMachine` root), deleted in `Dispose`. Helpers and **defaults that hold unless a test says otherwise**:
- `Cat(key, version = "1.0.0", dev = false, years = {"2024"})` builds a catalog entry with `Manifest = key + ".addin"`, `Assembly = "Host.dll"`, `EntryClass = "X.App"`, `ClientId = "11111111-1111-1111-1111-111111111111"`, empty `Replaces`; tests use the key `A`.
- `Payload(key, year, version, dev)` writes `src\<key>\<year>\` with `Host.dll`, `Lib.dll` and its `addin.json` = `{"key": <key>, "version": <version>, "commit": "abc", "built": "2026-11-16T10:00:00Z", "manifest": "<key>.addin", "dev": <dev>}`; every test has a payload for each year its catalog entry names.
- `Installed(key, year, version = "0.9.0", dev = false)` writes `m\<year>\<key>\` with `Host.dll`, `Lib.dll`, **`stale.dll`** and its `addin.json` (the same members as in `Payload`), plus the manifest `m\<year>\<key>.addin` holding `ManifestXml` of an entry of that version (so it is "ours").
- `FakeMachine.Installed = {"2024"}`, `Running` empty; calls pass `keys: null, years: null`.

```csharp
[Fact] Install_Fresh_CopiesFilesAndWritesTheManifest()   // Installed; m\2024\A\Host.dll, Lib.dll, addin.json exist; File.ReadAllText(m\2024\A.addin) == Engine.ManifestXml(entry); the file's first three bytes are not EF BB BF; no A.new / A.old
[Fact] ManifestXml_IsTodaysManifest()                    // for an entry Key "RVTuk", Assembly "RVTuk.Revit.dll", EntryClass "RVTuk.Revit.Application", ClientId D71D7480-4A21-474E-A47E-3E8DF8C1BDA5:
                                                         // Encoding.UTF8.GetByteCount(xml) == 418; xml ends with "</RevitAddIns>\r\n"; xml.Replace("\r\n", "") contains no '\n'; contains "<Assembly>RVTuk\\RVTuk.Revit.dll</Assembly>" and "<VendorId>KnafoKlimor</VendorId>"
[Fact] Install_OverAnOlderVersion_ReplacesTheWholeFolder // Installed(A, 2024) → Updated; stale.dll gone; manifest == ManifestXml(entry); no A.new / A.old
[Fact] Install_SameVersion_WritesNothing                 // Installed(A, 2024, "1.0.0") → Unchanged; stale.dll still there; no A.new
[Fact] Install_OlderCatalog_LeavesTheNewerInstall        // Installed "1.2.0" → Newer; stale.dll still there
[Fact] Install_TwoPartVersion_EqualsThreePart            // Installed "1.0.0", catalog "1.0" → Unchanged
[Fact] Install_OverADevBuild_Replaces                    // Installed "1.0.0" dev → Updated
[Fact] Install_DevCatalog_AlwaysReplaces                 // Installed "1.2.0", catalog "1.0.0" dev → Updated
[Fact] Install_OverAFolderWithoutAddInJson_Replaces      // Installed(...) then delete its addin.json (today's installs have none) → Updated
[Fact] Install_SameVersionButNoManifest_Reinstalls       // Installed "1.0.0", then delete m\2024\A.addin → Updated; the manifest exists again
[Fact] Install_SameVersionButOldInstallStillThere_RemovesIt // Installed "1.0.0"; entry.Replaces = [{Old.addin, Old}]; m\2024\Old\x.dll + Old.addin exist → Updated; Old and Old.addin gone
[Fact] Install_PartialYears_OnlyWhereRevitIs             // entry years 2024..2027, machine.Installed = {2024, 2026} → results for 2024 and 2026 only; m\2025 and m\2027 do not exist
[Fact] Install_ExplicitYears_OverrideDetection           // entry years {2024, 2025}; machine.Installed empty; years {"2025"} → one result, Installed, in m\2025
[Fact] Install_YearOutsideTheAddIn_IsIgnored             // entry years {2023}; years {"2024"} → no result, m\2024 not created
[Fact] Install_RevitOpen_SkipsThatYearOnly               // entry years {2024, 2026}, Installed both, machine.Installed both, Running = {2026} → 2024 Updated, 2026 SkippedRevitOpen with stale.dll still there; Engine.Failed(results) is true
[Fact] Install_DifferingYears_EachByItsOwnState          // entry years {2024, 2026}, machine.Installed both; 2024 has "1.0.0", 2026 has "0.9.0" → 2024 Unchanged, 2026 Updated
[Fact] Install_FileInUse_ChangesNothing                  // Installed(A, 2024); hold new FileStream(m\2024\A\Lib.dll, Open, Read, FileShare.Read) → SkippedInUse, Message contains "Lib.dll"; Host.dll, Lib.dll, stale.dll and the manifest still there; no A.new / A.old; Failed is true
[Fact] Install_ManifestCannotBeWritten_RollsBack         // Installed(A, 2024); replace the file m\2024\A.addin by a DIRECTORY of that name → Failed; m\2024\A still holds stale.dll; no A.new, no A.old, no A.addin.tmp
[Fact] Install_AfterACrash_RestoresTheOldFolderFirst     // Installed "1.2.0", then rename m\2024\A to A.old (manifest stays) → Newer; m\2024\A exists again with stale.dll; A.old gone
[Fact] Install_AfterAHalfRemove_DoesNotRestore           // Installed "1.0.0", rename the folder to A.old AND delete A.addin → Installed (not Unchanged); m\2024\A holds no stale.dll; A.old gone
[Fact] Install_MissingFromThePayload_Fails               // delete src\A\2024 → Failed, "not in the payload" in the message; m\2024\A does not exist
[Fact] Install_ReplacesTheOldInstall                     // no A installed; m\2024\Old\x.dll + m\2024\Old.addin; entry.Replaces = [{Old.addin, Old}] → Updated; Old, Old.old and Old.addin gone; A installed
[Fact] Install_OldInstallInUse_ChangesNothing            // as above with a held stream on Old\x.dll → SkippedInUse; Old and Old.addin still there; m\2024\A and A.new do not exist
[Fact] Remove_DeletesFolderAndManifest                   // Installed(A, 2024) → Removed; neither folder nor manifest exists; no A.old
[Fact] Remove_NothingThere_IsUnchanged                   // m\2024 does not even exist → one result, Unchanged; Failed is false
[Fact] Remove_WithALeftoverOld_Removes                   // Installed(A, 2024) plus a stale m\2024\A.old\x.dll → Removed; no A, A.old or A.addin
[Fact] Install_KeyInOtherCase_Installs                   // keys {"a"} → one result, Installed, for A
[Fact] Remove_FileInUse_ChangesNothing                   // Installed; held stream on Lib.dll → SkippedInUse; folder (three DLLs) and manifest still there
[Fact] Remove_All_AlsoRemovesOurAddInTheCatalogDoesNotKnow // Installed("Gone", 2024) (not in the catalog) + a plain folder m\2024\Other without addin.json; keys: null → a Removed result with AddIn "Gone"; Other still there
[Fact] Remove_All_LeavesAForeignAddInJson                // m\2024\Foreign\addin.json = {"name":"x"} and m\2024\Foreign2\addin.json = {"key":"Foreign2","manifest":"Foreign2.addin"} with a Foreign2.addin that lacks our VendorId → both folders and the manifest still there; no result names them; no *.old
[Fact] Remove_ByName_OnDiskOnly                          // Installed("Gone", 2024); keys {"Gone"} → Removed; keys {"Nope"} → no result
[Fact] Remove_ByKey_LeavesOtherAddIns                    // catalog A and B, both Installed; keys {"A"} → B's folder and manifest still there
[Fact] Failed_IsFalseForInstalledUpdatedUnchangedNewerRemoved
```

Run: `dotnet test installer\RVTuk.Setup.Tests\RVTuk.Setup.Tests.csproj -c Release --filter EngineTests`
Expected: FAIL to compile.

- [ ] **Step 2: Implement `Engine.cs`** by the algorithm above. One private method per add-in-year for install and one for remove, each wrapped so that nothing escapes; `log` gets one line per result.

- [ ] **Step 3: Run the tests**

Run: `dotnet test installer\RVTuk.Setup.Tests\RVTuk.Setup.Tests.csproj -c Release`
Expected: 48 passed (15 + 33).

- [ ] **Step 4: Prove the central tests can fail**

Comment out the probe → `Install_FileInUse_ChangesNothing`, `Install_OldInstallInUse_ChangesNothing` and `Remove_FileInUse_ChangesNothing` must fail (on .NET Framework a held file also blocks the folder rename, so they fail on the outcome — `Failed` after the 5 s retry instead of `SkippedInUse` — which is the point: without the probe a .NET 8 host would not be noticed at all). Restore. Comment out step 2 → `Install_AfterACrash_RestoresTheOldFolderFirst` must fail. Restore. Make "ours" accept any parsed `addin.json` → `Remove_All_LeavesAForeignAddInJson` must fail. Restore. `git status --short` then shows only the two new files of this task.

- [ ] **Step 5: Commit**

```powershell
git add installer
git commit -m "feat(setup): the install engine - probe, swap by rename, roll back"
```

### Task 4: The command line

**Files:**
- Modify: `installer/RVTuk.Setup/Program.cs`, `CLAUDE.md`
- Create: `installer/RVTuk.Setup.Tests/ProgramTests.cs`

**Interfaces:**
- Consumes: `Engine.Install/Remove/Failed`, `Result`, `Outcome`, `Catalog.Read/Find`, `RealMachine`, `TargetMachine`.
- Produces:

```csharp
sealed class Options {
    public bool Remove;                 // --remove / --uninstall; otherwise install
    public List<string> Keys;           // null = all
    public List<string> Years;          // null = detect
    public string Source, Target;
    public bool Quiet, NoPause;
    public static Options Parse(string[] args, out string error);   // null + error text on bad input; needs no catalog
}
static class Program {
    // 0 = done; 1 = something failed or was skipped, or there is no payload; 2 = --install names an add-in the catalog does not hold.
    // Never elevates, never pauses. logFile null = no log file.
    internal static int Run(Options o, TextWriter output, string logFile);
    // Main: Parse (error → the error and the usage, exit 2); when o.Target is null and the process is not elevated → RelaunchElevated(args);
    //       Run(o, Console.Out, <log path>); pause as today unless NoPause or Quiet.
}
```

**Command line:** `--install <names|all>` (when neither `--install` nor `--remove` is given: install all), `--remove <names|all>`, `--uninstall` (= `--remove all`), `--years <y,y>`, `--source <dir>`, `--target <dir>`, `--quiet`, `--no-pause`. Names and years are comma-separated. `Parse` errors: an unknown argument (including `--all` and `--preview`); `--install` together with `--remove` or `--uninstall`; an option that needs a value and has none, **or whose next argument starts with `--`**; a year that is not four digits; a name that fails `Catalog.IsPlainName`.

**`Run`:** payload folder = `o.Source`, or the embedded `payload.zip` extracted to `%TEMP%\RVTukSetup-<pid>` (deleted at the end, also on failure); neither → print today's "built without Build-Installer.ps1" message, return 1. `Catalog.Read(<payload>\catalog.json)` (an exception → its message, return 1). For `--install`, a name the catalog does not hold → print `Unknown add-in '<name>'. This package holds: <keys>`, return 2 (for `--remove` such a name is passed on: the add-in may be on disk only). Machine = `TargetMachine(o.Target)` when set, else `RealMachine`. Output: the banner (as today's, with the catalog's `built` and `commit`), then one line per result — `  Revit <year>  <AddIn>  <words>` — with these words: `installed`, `updated`, `removed`, `unchanged`, `newer version installed`, `SKIPPED: Revit <year> is open`, `SKIPPED: <Message>`, `FAILED: <Message>`; when there is no result at all, the line `  Nothing to do: no selected add-in fits a Revit year on this PC.`; last, either `  Done.` or `  NOT everything was done - see the lines marked SKIPPED or FAILED.` With `--quiet` nothing is written to `output`. Every line is also appended to `logFile` with a timestamp, quiet or not; a log file that cannot be opened or written is ignored silently. Return 1 when `Engine.Failed`.

**`Main`'s log path:** `%ProgramData%\RVTuk\setup.log` when elevated, else `%LOCALAPPDATA%\RVTuk\setup.log`. `IsElevated` and `RelaunchElevated` stay as they are.

- [ ] **Step 1: Write the failing tests** — `ProgramTests`

Every `Run` test builds a source folder (a `catalog.json` with one add-in `A`, **`dev: true`**, years 2023 and 2024, and its two payload folders with `Host.dll` and an `addin.json`) and passes `Target = <temp>\T` — **no `Run` test may leave `Target` null** (that would be the real Revit folders) — and `logFile: null` unless stated.

```csharp
[Fact] Parse_NoArguments_InstallsEverything()             // Remove false, Keys null, Years null, error null
[Fact] Parse_NamesAndYears()                              // "--install","RVTuk,KKarea","--years","2024,2026","--source","S","--target","T","--quiet","--no-pause" → fields set
[Fact] Parse_Uninstall_IsRemoveAll()                      // Remove true, Keys null
[Fact] Parse_All_IsItsOwnWord()                           // "--remove","all" → Remove true, Keys null
[Theory] Parse_Rejects(params string[] args)              // seven cases → null, error not empty:
    // {"--all"}, {"--preview"}, {"--install","A","--remove","A"}, {"--years"}, {"--years","24"}, {"--bogus"}, {"--install","--quiet"}
[Fact] Run_InstallsFromASourceFolderIntoATarget()         // returns 0; T\2023\A\Host.dll and T\2024\A.addin exist; output contains "installed" and "Done."
[Fact] Run_UnknownName_Returns2()                         // Keys {"Nope"} → 2; output contains "Unknown add-in 'Nope'" and "A"; T does not exist
[Fact] Run_Quiet_PrintsNothing()                          // output stays empty; the files are installed
[Fact] Run_SkippedYear_Returns1()                         // install once; hold a FileStream (Open, Read, FileShare.Read) on T\2024\A\Host.dll; install again → 1; output contains "SKIPPED" and "NOT everything"; the 2023 line says "updated"
[Fact] Run_RemoveThenNothingLeft()                        // install, then Remove true → 0; T\2023 and T\2024 hold neither A nor A.addin
[Fact] Run_NothingSelected_SaysSo()                       // Years {"2030"} → 0; output contains "Nothing to do"
[Fact] Run_WritesTheLog_AndSurvivesAnUnwritableOne()      // logFile = <temp>\log.txt, Quiet true → the file contains "installed"; then logFile = an existing DIRECTORY → still returns 0
```

Run: `dotnet test installer\RVTuk.Setup.Tests\RVTuk.Setup.Tests.csproj -c Release --filter ProgramTests`
Expected: FAIL to compile.

- [ ] **Step 2: Rewrite `Program.cs`**

Delete `Install`, `Remove`, `PayloadYears`, `ManifestName`, `Normalize`, `GetRunningRevitYears`, `MachineAddinsBase`, `Summary` and the `--all` flag; keep `IsElevated`, `RelaunchElevated`, the banner's look and the "Press Enter" pause. Update the doc comment at the top of the file to the new payload layout (`catalog.json`, `<AddIn>\<year>\…`) and flags.

- [ ] **Step 3: Run all setup tests**

Run: `dotnet test installer\RVTuk.Setup.Tests\RVTuk.Setup.Tests.csproj -c Release`
Expected: 66 passed (48 + 18: eleven facts and a theory with seven cases).

- [ ] **Step 4: Docs and commit**

`CLAUDE.md` "Deployment": the sentence that says the path rule "lives once per script: `Get-AddinsBase` in Deploy.ps1 and `MachineAddinsBase` in the setup exe" becomes: the rule lives in the setup exe (`RealMachine.YearFolder`) and, until Task 7 of this plan, also in `Deploy.ps1` (`Get-AddinsBase`).

```powershell
git add installer CLAUDE.md
git commit -m "feat(setup): command line on top of the engine"
```
The exe cannot install a real payload between this commit and Task 5 (`Build-Installer.ps1` still writes the old layout); that is why the two tasks follow each other directly.

### Task 5: `addin.props`, the staging function, `Build-Installer.ps1`

**Files:**
- Create: `src/RVTuk.Revit/addin.props`, `src/KKarea.Revit/addin.props`, `build/Staging.ps1`
- Modify: `Build-Installer.ps1`, `tests/RVTuk.Core.Tests/Shared/FrozenIdentifierTests.cs`, `CLAUDE.md`, `README.md`

**Interfaces:**
- Consumes: the setup exe of Task 4 (flags `--install`, `--remove`, `--uninstall`, `--years`, `--source`, `--target`, `--no-pause`; exit codes 0 / 1 / 2). It reads `<payload>\catalog.json` with top-level members `built`, `commit`, `addIns`, and installs from `<payload>\<key>\<year>\`.
- Produces (`build\Staging.ps1`, dot-sourced; must run in Windows PowerShell 5.1 and PowerShell 7):

```powershell
Get-AddIns [-Root <repo root>]        # one object per addin.props: Key, Name, Manifest, ClientId, EntryClass, Version,
                                      # Years ([string[]]), Assemblies ([string[]], first = host), ThirdParty ([bool]), Dir (the props' folder), Project (host csproj, full path)
Get-Tfm -Year <string>                # '2023','2024' -> net48; '2025','2026' -> net8.0-windows; '2027' -> net10.0-windows
Copy-AddInOutput -OutputDir <dir> -Tfm <tfm> -TargetDir <dir>      # today's copy-and-strip rules; throws when OutputDir is missing
New-Staging -StagingDir <dir> [-AddIn <keys>] [-Years <years>] [-Build] [-Strict] [-CatalogOnly] [-Root <repo root>]
                                      # returns the staged pairs as objects (Key, Year) and NOTHING else on the output stream
```
`-Root` defaults to the parent of the script's folder. `-AddIn` / `-Years` that are `$null` or empty mean **all**. `-CatalogOnly` copies nothing and writes a `catalog.json` listing every selected add-in with all its (selected) years — what a remove needs.

- [ ] **Step 1: The two `addin.props`**

`src/RVTuk.Revit/addin.props`:

```xml
<Project>
  <!-- What this add-in is. Read by build\Staging.ps1 (not imported by the project). The ClientId is frozen. -->
  <PropertyGroup>
    <AddInKey>RVTuk</AddInKey>
    <AddInName>RVTuk</AddInName>
    <AddInManifest>RVTuk.addin</AddInManifest>
    <AddInClientId>D71D7480-4A21-474E-A47E-3E8DF8C1BDA5</AddInClientId>
    <AddInEntryClass>RVTuk.Revit.Application</AddInEntryClass>
    <AddInVersion>1.0.0</AddInVersion>
    <AddInYears>2024;2025;2026;2027</AddInYears>
    <AddInAssemblies>RVTuk.Revit;RVTuk.UI;RVTuk.Core</AddInAssemblies>
    <AddInThirdParty>true</AddInThirdParty>
  </PropertyGroup>
</Project>
```
`src/KKarea.Revit/addin.props`: the same shape with `KKarea`, `KKarea`, `KKarea.addin`, `9C97B9F2-60F9-432D-92A4-5EC2A0FDAFFC`, `KKarea.Revit.Application`, `1.0.0`, `2023`, `KKarea.Revit;RVTuk.UI;RVTuk.Core`, `true` (KKarea ships the third-party set today without using it; phase 3 turns that off).

In `FrozenIdentifierTests.cs`: scan `*.props` as well as `*.cs` and `*.csproj`, and add two rows **above the three retired rows** (the ones with count 0): `1|<AddInClientId>D71D7480-4A21-474E-A47E-3E8DF8C1BDA5</AddInClientId>` and `1|<AddInClientId>9C97B9F2-60F9-432D-92A4-5EC2A0FDAFFC</AddInClientId>`.

- [ ] **Step 2: `build\Staging.ps1`**

- **Paths first.** Every function makes its path parameters absolute before anything else — `$ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($p)` — and hands only absolute paths to .NET methods.
- `Get-AddIns`: every `addin.props` under the root, skipping folders named `bin` or `obj` and folders whose name starts with `.`; read as XML. `ThirdParty` is `($text -eq 'true')` (a `[bool]` cast of the text `false` would be true). `Project` is the one file named `<first assembly>.csproj` under the props' folder (searched recursively, skipping `bin` and `obj`); throw when there is none or more than one.
- `Copy-AddInOutput`: from `OutputDir` flat into `TargetDir`: `*.dll`, `*.pdb`, `*.deps.json` (narrower than today's `*.json`; the same files today), `x64\*.dll` when that folder exists, `runtimes\win-x64\native\e_sqlite3.dll` when it exists; then, for `net48`, delete the twelve names of today's strip list (`Deploy.ps1:190-202`, copied verbatim into one array here).
- `New-Staging`: **refuse to wipe** a `StagingDir` that exists, is not empty and holds no `catalog.json` (throw: `"<dir> does not look like a staging folder"`); otherwise remove it and create it empty. For each selected add-in and each of its years that is selected: with `-Build`, `$log = & dotnet build <Project> -c Release<year> --nologo -v q 2>&1` — **the output is captured, never returned**; output folder `<project folder>\bin\<year>\Release<year>\<tfm>`; when the build failed or `<host>.dll` is missing there: with `-Strict` throw (showing the last 12 lines of `$log`), otherwise `Write-Warning` with those lines and skip that year. `Copy-AddInOutput` into `<StagingDir>\<Key>\<year>`, then write `addin.json` there. At the end write `<StagingDir>\catalog.json` holding only the add-ins and years actually staged (with `-CatalogOnly`: all selected ones).
- **JSON.** Build ordered hashtables, `ConvertTo-Json -Depth 5`, write with `[IO.File]::WriteAllText($absolutePath, $json, (New-Object System.Text.UTF8Encoding $false))`. **Every list is a typed array** — `[string[]]` for years, `@(…)` around the add-in list, `replaces = @()` — so one element stays a list. Time: `[DateTime]::UtcNow.ToString('s') + 'Z'`. Commit: `git -C <root> rev-parse --short HEAD`, plus `-dirty` when `git -C <root> status --porcelain` prints anything.
- `catalog.json` top level: `built`, `commit`, `addIns`. Each entry: `key`, `name`, `manifest`, `entryClass`, `assembly` (`<first assembly>.dll`), `clientId`, `version`, `dev` (`$true`), `years`, `replaces` (empty). `addin.json`: `key`, `version`, `commit`, `built`, `manifest`, `dev` (`$true`). Everything this function stages is a dev build until `Release.ps1` exists (phase 4).

- [ ] **Step 3: Stage everything and look**

```powershell
. .\build\Staging.ps1
$s = "$env:TEMP\rvtuk-phase2-engine\staging"
New-Staging -StagingDir $s -Build -Strict | Format-Table
Get-Content "$s\catalog.json"
Get-ChildItem $s -Recurse -File | Group-Object { $_.Directory.FullName.Substring($s.Length) } | Select-Object Name, Count
powershell.exe -NoProfile -Command ". '$PWD\build\Staging.ps1'; New-Staging -StagingDir '$s-51' -Strict | Measure-Object | ForEach-Object Count; (Get-Content '$s-51\catalog.json' -Raw | ConvertFrom-Json).addIns | ForEach-Object { `$_.key + ' ' + (`$_.years -join ',') + ' ' + `$_.years.GetType().Name }"
New-Staging -StagingDir $PWD            # must throw "does not look like a staging folder" and delete nothing
```
Expected: a table of exactly five pairs (KKarea 2023; RVTuk 2024–2027) and no build output above it; file counts: the root 1 (`catalog.json`), `\KKarea\2023` 13, `\RVTuk\2024` 13, `\RVTuk\2025` 15, `\RVTuk\2026` 15, `\RVTuk\2027` 14 (today's 12 / 12 / 14 / 14 / 13 plus `addin.json`); no `KKarea\2026`. The Windows PowerShell 5.1 line prints `5`, then `KKarea 2023 Object[]` and `RVTuk 2024,2025,2026,2027 Object[]`. The last command throws; `git status --short` is unchanged by it.

- [ ] **Step 4: `Build-Installer.ps1` on the staging function**

Keep its banner, the wipe of each host project's `bin\<year>`, its own first wipe of the staging folder (`if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }` with `$staging = "$root\installer\payload-staging"`, placed before the `New-Staging` call: the folder is the script's own and may still hold the old layout without a `catalog.json`, which `New-Staging` refuses to wipe) and the final messages. Replace the table, the per-year build, the copy rules and the manifest writing by: dot-source `"$root\build\Staging.ps1"`; `New-Staging -StagingDir "$root\installer\payload-staging" -Build -Strict`; zip the **content** of that folder to `"$root\installer\RVTuk.Setup\payload.zip"` (`catalog.json` at the zip's root; no `version.txt`); build the setup exe as today; copy it to `dist\RVTukSetup.exe`. The wipe uses `Get-AddIns` (each add-in's project folder and `Years`).

- [ ] **Step 5: The exe installs what the old exe installed**

```powershell
$base = "$env:TEMP\rvtuk-phase2-engine"
.\Build-Installer.ps1
.\dist\RVTukSetup.exe --target "$base\after" --no-pause; "exit $LASTEXITCODE"
$after = Get-ChildItem "$base\after" -Recurse -File -Name | Sort-Object
Compare-Object (Get-Content "$base\before.txt") $after | ForEach-Object { "$($_.SideIndicator) $($_.InputObject)" }
foreach ($y in 2023..2027) { $n = if ($y -eq 2023) { 'KKarea' } else { 'RVTuk' }
  $a = [IO.File]::ReadAllBytes("$base\before\$y\$n.addin"); $b = [IO.File]::ReadAllBytes("$base\after\$y\$n.addin")
  "$y manifest bytes identical: " + ([Convert]::ToBase64String($a) -eq [Convert]::ToBase64String($b)) }
```
Expected: `exit 0`. Five difference lines, all starting with `=>`: the five `addin.json` files. Five times `manifest bytes identical: True`.

- [ ] **Step 6: Update, skip and remove through the real exe**

```powershell
.\dist\RVTukSetup.exe --target "$base\after" --no-pause; "exit $LASTEXITCODE"                       # dev build: every line "updated", exit 0
$f = [IO.File]::Open("$base\after\2024\RVTuk\RVTuk.Core.dll", 'Open', 'Read', 'Read')
.\dist\RVTukSetup.exe --target "$base\after" --no-pause; "exit $LASTEXITCODE"; $f.Dispose()          # 2024 SKIPPED (in use), the others updated, exit 1
(Get-ChildItem "$base\after\2024\RVTuk" -File).Count                                               # 13: nothing was deleted
.\dist\RVTukSetup.exe --remove KKarea --target "$base\after" --no-pause; Test-Path "$base\after\2023\KKarea"   # False
.\dist\RVTukSetup.exe --uninstall --target "$base\after" --no-pause; (Get-ChildItem "$base\after" -Recurse -File).Count   # 0
.\dist\RVTukSetup.exe --all --target "$base\after" --no-pause; "exit $LASTEXITCODE"                  # the error, the usage, exit 2
```

- [ ] **Step 7: Tests, docs, commit**

Run: `.\build\Test-All.ps1` — exit 0; Core 585, UI 34 on three frameworks, Setup 66.

`CLAUDE.md`, section "Installer for other computers", rewritten to say: the payload is `catalog.json` plus `<AddIn>\<year>\`, staged by `build\Staging.ps1`; the flags of the setup exe (`--install`, `--remove`, `--uninstall`, `--years`, `--source`, `--target`, `--quiet`, `--no-pause`; `--all` is gone — `--years` names years outright, and without it the exe installs for the Revit years whose `Revit.exe` it finds, no longer for every existing Addins folder); a year is skipped when its Revit is open **or any file of the add-in is in use**, nothing in it is changed, and the exe then exits 1; until `Release.ps1` exists (phase 4) every package is marked as a dev build and always replaces what is installed; the sentence that the two scripts' tables "must stay in sync" goes (there is one source now). Section "Deployment" or "Architecture": one paragraph on `addin.props` (what it holds; the scripts' add-in table comes from these files) and `build\Staging.ps1`. `README.md`: the same flag list and behaviour where it describes the setup exe.

```powershell
git add -A
git commit -m "build: addin.props and one staging function; the installer packs a catalog"
```

### Task 6: The staging check

**Files:**
- Create: `build/Test-Staging.ps1`
- Modify: `CLAUDE.md`

**Interfaces:**
- Consumes: `build\Staging.ps1` (`Get-AddIns` → objects with `Key`, `Manifest`, `EntryClass`, `Version`, `Years`, `Assemblies` (first = host), `ThirdParty`, `Project`; `Get-Tfm -Year`; `New-Staging -StagingDir <dir> -Build -Strict`). A staging folder holds `catalog.json` (`addIns[].key/years/entryClass/version/manifest`) and `<key>\<year>\` folders, each with an `addin.json` (`key`, `version`, `manifest`).
- Produces: `.\build\Test-Staging.ps1 [-StagingDir <dir>] [-Solution <path>]` — PowerShell 7 only (`#Requires -Version 7.0`). Without `-StagingDir` it stages everything itself (`New-Staging -Build -Strict` into a new folder under `$env:TEMP`, removed afterwards). `-Solution` defaults to `RVTuk.sln` in the repository root. It prints one line per failure, each starting with `FAIL`, and then throws `Staging check failed: <n>`; otherwise it prints `Staging check passed: <n> folders` and sets `$global:LASTEXITCODE = 0` as its last statement.

- [ ] **Step 1: Write the check.** Resolve `-StagingDir` and `-Solution` to absolute paths first. For the catalog's every add-in and year (folder `<StagingDir>\<key>\<year>`, framework `Get-Tfm -Year <year>`), with the add-in's values from `Get-AddIns`:
1. **Exact file set.** Expected names: for each of the add-in's assemblies `<name>.dll` and `<name>.pdb`; `addin.json`; for `net8.0-windows` and `net10.0-windows` also `<host>.deps.json`; and, when `ThirdParty` is true, the third-party set of the framework:
   - `net48`: `e_sqlite3.dll`, `Microsoft.Data.Sqlite.dll`, `OpenMcdf.dll`, `SQLitePCLRaw.batteries_v2.dll`, `SQLitePCLRaw.core.dll`, `SQLitePCLRaw.provider.dynamic_cdecl.dll`
   - `net8.0-windows`: `e_sqlite3.dll`, `Microsoft.Data.Sqlite.dll`, `OpenMcdf.dll`, `SQLitePCLRaw.batteries_v2.dll`, `SQLitePCLRaw.core.dll`, `SQLitePCLRaw.provider.e_sqlite3.dll`, `System.Drawing.Common.dll`
   - `net10.0-windows`: the net8 list without `System.Drawing.Common.dll`

   Report every missing and every unexpected file (`FAIL <key>\<year>: missing <name>` / `unexpected <name>`).
2. **No shared vendor DLL.** Per year: no `.dll` name other than the add-ins' own assemblies (`<assembly>.dll` of any add-in) appears in two add-in folders of that year. (Today no year has two add-ins; the rule is for phase 3.)
3. **The host assembly** (`<first assembly>.dll`), read through `System.Reflection.Metadata` over `[IO.File]::ReadAllBytes` (never `Assembly.Load`, which would lock the file): a type whose namespace + name equals the catalog's `entryClass` exists; the assembly reference named `RevitAPI` has major version `year − 2000`; the `TargetFrameworkAttribute`'s value is `.NETFramework,Version=v4.8` for `net48`, `.NETCoreApp,Version=v8.0` for `net8.0-windows`, `.NETCoreApp,Version=v10.0` for `net10.0-windows`.
4. **The theme.** The add-in's UI assembly — the one of its assemblies whose name ends in `.UI` — contains the UTF-16LE bytes of `shared/themes/darktheme.baml`.
5. **`addin.json`** parses, and its `key`, `version` and `manifest` equal the catalog entry's.
6. **The solution agrees with `Years`.** For each add-in: find the line of `-Solution` that starts with `Project(` and names `"<host assembly>"`, take the GUID at its end, collect the years `y` for which the solution holds a line `{GUID}.Release<y>|Any CPU.Build.0`; that set must equal the add-in's `Years`.
7. **No hard-wired theme path.** Run `git -C <repo root> grep --untracked -n -F "RVTuk.UI;component" -- src`: exit code 1 (no match) passes; exit code 0 fails and prints the matches; any other exit code fails with `FAIL git grep could not run`.

- [ ] **Step 2: Run it**

Run: `.\build\Test-Staging.ps1; "exit $LASTEXITCODE"`
Expected: `Staging check passed: 5 folders`, then `exit 0`.

- [ ] **Step 3: Prove each rule fires.** Stage once (`. .\build\Staging.ps1; New-Staging -StagingDir "$env:TEMP\rvtuk-phase2-engine\faults" -Strict`) and keep a pristine copy to restore from after each fault. Then, one at a time, running `.\build\Test-Staging.ps1 -StagingDir …` after each — it must throw and print the line named:
   - delete `RVTuk\2025\OpenMcdf.dll` → rule 1, `missing OpenMcdf.dll`;
   - copy any DLL to `RVTuk\2024\System.Memory.dll` → rule 1, `unexpected System.Memory.dll`;
   - copy `RVTuk\2026\RVTuk.Revit.dll` over `RVTuk\2025\RVTuk.Revit.dll` → rule 3, the Revit API year (26 in a 2025 folder), and nothing else;
   - copy `RVTuk\2024\RVTuk.Revit.dll` over `RVTuk\2027\RVTuk.Revit.dll` → rule 3, **two** lines: the Revit API year and the framework (the two cannot be separated with real outputs);
   - overwrite `KKarea\2023\RVTuk.UI.dll` with `KKarea\2023\RVTuk.Core.dll` → rule 4;
   - change `version` in `RVTuk\2024\addin.json` → rule 5;
   - with the pristine staging: `-Solution` pointing at a **copy** of `RVTuk.sln` (under `$env:TEMP`) from which the line `{A1B2C3D4-AAAA-BBBB-CCCC-000000000003}.Release2026|Any CPU.Build.0 = Release2026|Any CPU` was removed → rule 6.

   Put the seven outputs in the report. Rules 2 and 7 cannot fail on today's tree without editing tracked files: say in the report that they were exercised on the passing side only.

- [ ] **Step 4: Docs and commit**

`CLAUDE.md` "Tests": `.\build\Test-Staging.ps1 (PowerShell 7) stages all five years and checks every add-in folder: the exact file set, the entry class, the Revit API year, the framework, the theme, addin.json, and that RVTuk.sln builds each add-in for the years its addin.props names. Run it after any change to a project file, to an addin.props or to the staging function.`

```powershell
git add build CLAUDE.md
git commit -m "build: the staging check"
```

### Task 7: `Deploy.ps1` on the engine

**Files:**
- Modify: `Deploy.ps1`, `CLAUDE.md`, `README.md`

**Interfaces:**
- Consumes: `build\Staging.ps1` (`New-Staging -StagingDir <dir> [-AddIn] [-Years] [-Build] [-CatalogOnly]` → objects with `Key`, `Year`; `$null` selections mean all) and the setup exe (`--install <keys>`, `--remove <keys|all>`, `--years <y,y>`, `--source <dir>`, `--no-pause`; exit 0 / 1 / 2; it prints its own result lines and summary).
- Produces: `.\Deploy.ps1 [<years>…] [-AddIn <keys>] [-Clean]` — elevated shell required, as today (`#Requires -RunAsAdministrator` stays, so the exe never has to relaunch itself and its output stays in this window).

- [ ] **Step 1: Rewrite the script**

Parameters, exactly (so that `.\Deploy.ps1`, `.\Deploy.ps1 2024`, `.\Deploy.ps1 2024 2027`, `.\Deploy.ps1 -AddIn KKarea` and `.\Deploy.ps1 -Clean 2025` all bind):

```powershell
param(
    [Parameter(Position = 0, ValueFromRemainingArguments = $true)]
    [string[]]$Years,
    [string[]]$AddIn,
    [switch]$Clean
)
```
Keep the banner (with branch, commit and the dirty mark). `$root` = the script's folder; `$staging = "$root\installer\payload-staging"`; `$exe = "$root\installer\RVTuk.Setup\bin\Release\net48\RVTukSetup.exe"`. Dot-source `"$root\build\Staging.ps1"`. Build the setup exe (`dotnet build "$root\installer\RVTuk.Setup\RVTuk.Setup.csproj" -c Release --nologo -v q`; a failure stops the script with exit 1). Then `if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }`: the folder is the script's own and may still hold the old layout without a `catalog.json`, which `New-Staging` refuses to wipe.
- Without `-Clean`: `$staged = New-Staging -StagingDir $staging -Build -AddIn $AddIn -Years $Years` (not strict: a year that does not build is warned about and left out, as today); when nothing was staged, say so and `exit 1`; then `& $exe --install (<the distinct keys in $staged> joined by ',') [--years <y,y>] --source $staging --no-pause`.
- With `-Clean`: `New-Staging -StagingDir $staging -CatalogOnly -AddIn $AddIn -Years $Years | Out-Null` (no build), then `& $exe --remove (<$AddIn joined by ','> or all) [--years <y,y>] --source $staging --no-pause`.
- `exit $LASTEXITCODE`. `--years` is passed only when `-Years` was given: without it the engine works on the Revit years it finds on the PC.

Delete the table, `Get-AddinsBase`, the running-Revit detection, the copy rules, the manifest here-string and the summary: the engine prints all of that. Update the comment header (parameters and the five examples above).

- [ ] **Step 2: Check without deploying**

```powershell
$e = $null; [void][System.Management.Automation.Language.Parser]::ParseFile("$PWD\Deploy.ps1", [ref]$null, [ref]$e); "parse errors: $($e.Count)"
powershell.exe -NoProfile -Command "`$e=`$null; [void][System.Management.Automation.Language.Parser]::ParseFile('$PWD\Deploy.ps1',[ref]`$null,[ref]`$e); 'parse errors 5.1: ' + `$e.Count"
$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if ($admin) { 'ELEVATED SHELL: do not run Deploy.ps1 here' } else { pwsh -NoProfile -File "$PWD\Deploy.ps1" 2>&1 | Select-Object -First 3 }
```
Then run the script's own steps by hand against a scratch target, which is what it does except for the destination:

```powershell
. .\build\Staging.ps1
$staging = "$PWD\installer\payload-staging"; $exe = "$PWD\installer\RVTuk.Setup\bin\Release\net48\RVTukSetup.exe"; $t = "$env:TEMP\rvtuk-phase2-engine\deploy"
dotnet build "$PWD\installer\RVTuk.Setup\RVTuk.Setup.csproj" -c Release --nologo -v q
$staged = New-Staging -StagingDir $staging -Build -Years 2024; $staged | Format-Table
& $exe --install (($staged.Key | Select-Object -Unique) -join ',') --years 2024 --source $staging --target $t --no-pause; "exit $LASTEXITCODE"
New-Staging -StagingDir $staging -CatalogOnly | Out-Null
& $exe --remove all --source $staging --target $t --no-pause; "exit $LASTEXITCODE"; (Get-ChildItem $t -Recurse -File).Count
```
Expected: `parse errors: 0` and `parse errors 5.1: 0`; the un-elevated run refuses with the "running as Administrator" error and builds nothing; the hand run stages one pair (RVTuk 2024), installs `2024\RVTuk` (13 files) and `2024\RVTuk.addin`, `exit 0`; the remove prints `removed` for 2024, `exit 0`, and the file count is 0.

- [ ] **Step 3: Docs and commit**

`CLAUDE.md` "Deployment": the new parameters and examples; `Deploy.ps1` stages from the working tree and hands the folder to the engine; it deploys for the Revit years found on the PC (a year without Revit is no longer written to, unless named); a year is skipped, and the script exits 1, when its Revit is open or a file is in use, and a skipped year is left exactly as it was; `-Clean` removes; the path rule now lives only in the setup exe (`RealMachine.YearFolder`) — drop `Get-AddinsBase` from that sentence. In "Revit Add-in Notes": the ClientId sentence becomes `Changes to the ClientId GUID in an addin.props will break existing installations — it must stay stable (FrozenIdentifierTests pins both).`, and the `Manifests/` sentence says the setup exe's engine writes the manifests from the catalog (`Engine.ManifestXml`), no longer `Deploy.ps1`. `README.md`: the same in its deploy paragraph.

```powershell
.\build\Test-All.ps1; .\build\Test-Staging.ps1
git add -A
git commit -m "build: Deploy.ps1 stages and calls the install engine"
```

### Task 8: Checks on the real machine, and the record

**Files:**
- Modify: `docs/toolkit/specs/2026-11-12-addin-split-design.md`

- [ ] **Step 1: [Daniel] Deploy with every Revit closed.** Close all Revit windows. Open the Start menu, type `PowerShell`, right-click "PowerShell 7" and choose "Run as administrator". In that window:

```powershell
cd D:\Coding\RVTuk-split
.\Deploy.ps1
```
Expected: a line for Revit 2023 (KKarea) and for Revit 2024, 2026 and 2027 (RVTuk), each ending in `updated`; no line for 2025; the last line is `Done.` Then start Revit 2024. It may ask once whether to load the add-in — choose "Always Load". The RVTuk panel and its tools look as before.

- [ ] **Step 2: [Daniel] Deploy with one Revit open.** Start Revit 2026 and leave it open. In the same administrator window run `.\Deploy.ps1` again.
Expected: the 2026 line says `SKIPPED: Revit 2026 is open`; the other years say `updated`; the last line is `NOT everything was done …`. Revit 2026 keeps working.

- [ ] **Step 3: [Daniel] One line that answers an open question.** With Revit 2026 still open, paste this into the same administrator window:

```powershell
try { [IO.File]::Open('C:\ProgramData\Autodesk\Revit\Addins\2026\RVTuk\RVTuk.Revit.dll','Open','ReadWrite','None').Dispose(); 'NOT LOCKED' } catch [IO.IOException] { if ($_.Exception.HResult -eq -2147024864) { 'LOCKED' } else { 'OTHER: ' + $_.Exception.Message } } catch { 'OTHER: ' + $_.Exception.Message }
```
Expected: `LOCKED` — the engine's second safety net (the file probe) works against a real Revit. `NOT LOCKED` means only the "Revit is open" check protects a running Revit; `OTHER: …` means the test itself did not work (send the text). Either answer goes into the record.

- [ ] **Step 4: Record it in the spec** — a section "## Phase 2 record (engine half)" before "## Phase 2 record (build half)": date and commit; the test counts; that the new exe installs the same files and byte-identical manifests as the old one, plus `addin.json`; the lock finding and Step 3's answer; the two deploy results; what the engine does not do yet (window, releases, real old-install lines). Bring "Staging and deploy" and "Installer" to the past tense where they describe phase 2 as future.

- [ ] **Step 5: Commit, then ask Daniel about uploading**

```powershell
git add docs
git commit -m "docs(toolkit): record the install-engine half of phase 2"
git status --short; git log --oneline origin/master..HEAD
```

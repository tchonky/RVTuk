# RVTuk → separately installable add-ins — design

> "(yours)" = decided by Daniel in the brainstorm. "(my call)" = decided by Claude, open to veto. "(builder)" = left to whoever writes the implementation plans.
> Approved by Daniel on 2026-11-15. Each phase gets its own plan in `docs/toolkit/plans/` (superpowers:writing-plans).
> Updated 2026-11-15 after PR #12 (KKarea for Revit 2026 and 2027) was merged: the design is unchanged; statements about what exists today were brought up to date. Line references and counts were re-checked in phase 0 (its plan, task 9); the measured values are in "Phase 0 record" at the end.
> Changed 2026-11-15, after phase 0: `RelayCommand<T>` stays in `Shared/UI` instead of moving to Family Browser, because Round Positions (in Tools) uses it too — see "Shared source".
> Changed 2026-11-15: phase 1 was closed from evidence, without spike code (Daniel's decision) — see "Phase 1 record" at the end. The checks that need a running add-in moved into phases 2, 3 and 4, and statements about the dev PC that had gone stale (Revit 2027, Revit 2026.5) were corrected.
> Updated 2026-11-15 after PR #14: the full RVTuk add-in now builds and deploys for Revit 2027 (KKarea stays for 2023 and 2026), and the scripts install the 2027 build under Program Files. The design is unchanged; statements about what exists today were brought up to date. "Phase 1 record" keeps its 13:15 state and says so.
> Changed 2026-11-16, while planning phase 2 (my calls): the generic settings store is built in phase 3, in the Round Positions step, where a second settings file first exists — phase 2 keeps only the net48 test run that settles the serializer comment. Tool projects leave the build matrix through an empty `installer\Directory.Build.props`. Database tests run on net48 once xunit's shadow copy is off.
> Changed 2026-11-16 (yours): Revit 2026 gets the full RVTuk add-in, like 2027; KKarea serves Revit 2023 only.
> Reviewed in two rounds against the real code (three skeptic reviewers, a builder check, then a consistency check and a check of the mechanisms added in between). Statements they confirmed are marked "(checked)".

## Context

RVTuk is one Revit add-in today: `src\RVTuk.Revit\Application.cs` wires seven tools into one ribbon panel, one installer ships everything. Daniel wants (yours) **people to install only the tools they use** and **one tool to be updatable without redeploying or re-testing the others**. Giving tools to outsiders is not a driver. This deliberately reverses `VISION.md:106-108` ("One toolkit, one ribbon, one install") and supersedes the approved-but-unbuilt `docs/toolkit/specs/2026-07-03-tool-visibility-design.md`.

**Nobody uses RVTuk yet (yours).** So old versions get no special care: there is no office version to keep installable during the work, no migration logic, and no rollout safeguards. The only old installs that matter are the ones on the dev PC (RVTuk, KKarea and KKompare, in whichever years they were deployed), and one simple rule removes them.

## Goals / non-goals / anti-goals

**Goals**
1. Six add-ins, each installable, updatable and removable on its own (yours): Family Browser, Area Calc, DWG Export, Template (= KKompare), Topo, Tools (one add-in holding the small tools: Auto Dimensions, Room Floor, Round Positions, hidden Neo Properties).
2. One top-level **RVTuk** ribbon tab, one panel per add-in (yours).
3. One `RVTukSetup.exe` with a checklist showing installed vs available version per add-in; tick = install/update, untick = remove (yours). Per-machine install; users run it themselves with admin rights (yours).
4. Revit 2023 (Area Calc only) and 2024–2027 for every add-in (yours).
5. No behaviour change inside any tool; existing project data, settings and the family database keep working. Known visible changes: the ribbon location (RVTuk tab), the Area Calc dialog title on the year KKarea serves today — 2023 ("KKarea – Area Calc" → "RVTuk – Area Calc"), crash-log file names, and the instruction bug fixes below.

**Non-goals** (my call): shared runtime DLL or host/plug-in architecture; namespace renames; code signing; per-user install scope; per-year choice in the setup window; downgrades; an Apps & Features entry; auto-update; Inno Setup/MSI; migration logic or safeguards for old versions (see Context); unifying KKompare's theme or view-model base with RVTuk's; KKcheck and DowngRev; fixing the latent OpenMcdf/System.Memory reference issue (backlog only); the PDF print-path spec's installer duties.

**Anti-goals** — a failure even if everything builds: project data orphaned (any frozen identifier below changes); losing settings or family instructions; a same-named vendor DLL in two add-in folders; having to release all add-ins together; office sample drawings or real instruction text committed to the repository (the help page is fetched from it anonymously, so treat it as public).

## Why shared code is source, not a binary (answers "is a shared add-in possible?")

Possible only from Revit 2027. On 2023–2026 one copy of a same-named DLL serves the whole session: .NET Framework returns the first-loaded same-identity assembly (every vendor DLL is `1.0.0.0`, unsigned); .NET 8/10 holds one assembly per simple name per load context, binds by-name references silently to the first copy and throws `FileLoadException` on path loads of a different build. WPF resource lookup is by short assembly name, last-loaded wins, and ignores load contexts, so Revit 2026 manifest isolation does not help either. Independent updates + a shared DLL therefore fail silently (`MissingMethodException` at first use). The genuinely common code is ~740 lines (measured after phase 0, see "Other findings" in the Phase 0 record), so the cost of compiling it into each add-in is negligible. Consequence: shared source means no shared statics between add-ins.

## Architecture

### Add-in table (my call unless marked)

| Add-in (display, yours) | Repo folder | Assemblies | Namespaces kept | Manifest = load/panel order | ClientId |
|---|---|---|---|---|---|
| Family Browser | `FamilyBrowser/` | `RVTuk.FamilyBrowser.{Core,UI,Revit}.dll` | `RVTuk.*.FamilyBrowser` | `RVTuk.1.FamilyBrowser.addin` | `D71D7480-4A21-474E-A47E-3E8DF8C1BDA5` (RVTuk's) |
| Area Calc | `AreaCalc/` | `RVTuk.AreaCalc.{Core,UI,Revit}.dll` | `RVTuk.*.RishuiZamin` | `RVTuk.2.AreaCalc.addin` | `9C97B9F2-60F9-432D-92A4-5EC2A0FDAFFC` (KKarea's) |
| DWG Export | `DwgExport/` | `RVTuk.DwgExport.{Core,UI,Revit}.dll` | `RVTuk.*.DwgExporter` | `RVTuk.3.DwgExport.addin` | new, generated once |
| Template | `Template/` | `KKompare.{Core,UI,Revit}.dll` — unchanged (yours) | `KKompare.*` | `RVTuk.4.Template.addin` | `496F971A-DD0B-44CC-9430-D249796BBB9F` — unchanged (yours) |
| Topo | `Topo/` | `RVTuk.Topo.{Core,UI,Revit}.dll` | `RVTuk.*.TopoTools` | `RVTuk.5.Topo.addin` | new, generated once |
| Tools | `Tools/` | `RVTuk.Tools.{Core,UI,Revit}.dll` | `RVTuk.*` + one folder per tool | `RVTuk.6.Tools.addin` | new, generated once |

- Years: Area Calc 2023–2027, the others 2024–2027. Template is offered for 2024–2026 from the day it joins (the years KKompare serves today). Area Calc's code already builds for 2026 and 2027 (in the full RVTuk add-in), but Area Calc has not yet been used inside Revit 2026 or 2027 (the add-in itself starts there — Phase 2 record), so the setup offers them only after their manual pass, like any other year.
- Install folder per add-in under `Addins\{year}\`: `RVTuk.FamilyBrowser\`, `RVTuk.AreaCalc\`, `RVTuk.DwgExport\`, `RVTuk.Template\` (yours — the folder name changes, the DLL names do not), `RVTuk.Topo\`, `RVTuk.Tools\`.
- ClientIds are frozen from first release. Revit stores "Always Load" per ClientId, per Revit year, per Windows user (checked in the registry: no path or hash), so every user gets at least one prompt per add-in per Revit year. An update may prompt again: Revit 2024 showed the prompt in the first session after each of two redeploys although the ClientId was already stored; the journal does not say for which add-in (Phase 1 record, S4).
- Revit loads manifests alphabetically by file name, the machine-wide and per-user folders together (Autodesk's Developer's Guide and the dev PC's journals: Phase 1 record, S1), and the ribbon API only appends, so the numeric key in the manifest name fixes panel order. A seventh add-in takes `RVTuk.7.*`.
- Folder names have no spaces (my call); display names do.

### Repo layout

```
RVTuk/
  FamilyBrowser/  AreaCalc/  DwgExport/  Topo/  Tools/     each: src/<3 projects>, tests/<Core tests, UI tests>, addin.props
  Template/                                                KKompare imported with history, own CLAUDE.md
  Shared/Core  Shared/UI  Shared/Revit                     source only, linked file by file into the projects that use it
  build/                                                   staging function, staging check, Test-All.ps1, released-years list
  installer/RVTuk.Setup/ (+ tests)                         the one installer: engine, command line, window (stays outside RVTuk.sln)
  releases/                                                release store (git-ignored)
  docs/                                                    unchanged location (the help URL in FamilyBrowserViewModel.cs:26 reads master's docs at runtime)
```

Three layers per add-in stay exactly as today (Core: no Revit/WPF; UI: no Revit types; Revit: host). `src\KKarea.Revit` (today the Area Calc host for 2023) is deleted — Area Calc's build for every year comes from the same three projects; one `RishuiZaminCommand.cs` serves all years (KKarea's copy differs only in its namespace and two display strings — checked).

### Namespaces do not change (my call)

Each project sets `RootNamespace` so every existing namespace survives the move, e.g. `RVTuk.FamilyBrowser.Core.csproj` → `RootNamespace=RVTuk.Core.FamilyBrowser`; the multi-tool Tools projects keep `RVTuk.Core` / `RVTuk.UI` / `RVTuk.Revit` with one folder per tool. Each host's entry class is named `Application` in its tool's Revit namespace (Tools keeps `RVTuk.Revit.Application`), so the unqualified `Application.X` references in the command files (68 in six files since phase 0 merged `round-positions`; listed in the Phase 0 record) bind without edits. Checked: enclosing-namespace lookup finds the entry class first, no file imports a competing `Application`, and the only namespace-sensitive artefact (the embedded DXF templates) has an explicit `LogicalName`. Result: the split is file moves + new project files + six entry classes, not a rename. Files are moved unedited in their own commits so git follows the renames.

Two exceptions to "namespace = folder path": linked shared files keep their `RVTuk.*.Shared.*` namespaces, and the four single-tool files that leave `Shared/` (below) take their new folder's namespace — the only namespace changes in the split.

### Shared source

| Folder | Contents | Notes |
|---|---|---|
| `Shared/Core` | `XyPoint`, the generic settings store, `IsExternalInit` polyfill | Today Core's polyfill sits inside `RishuiZamin/UsageCatalog.cs:3-13`; cut it out there when the shared copy is linked (else duplicate type) |
| `Shared/UI` | `DarkTheme.xaml`, `DarkThemeDictionary`, `ViewModelBase`, `RelayCommand`, `RelayCommand<T>` | see "Theme". `RelayCommand<T>` is linked only by Family Browser and Tools (Round Positions uses it) |
| `Shared/Revit` | `LineStyleCreator`, `DefaultTypes`, `DependentsCounter`, `LoopOrder` (the three came with `topo-convert`), host helpers (below), `IsExternalInit` | `DefaultTypes` and `DependentsCounter` use `Toposolid`, which does not exist in the 2023 API |

- **Linked per file, explicitly, by each project that uses the file** (the KKarea pattern: the linked `Compile` items in `KKarea.Revit.csproj`) — never by folder glob or `.projitems`. A folder link would pull the `Toposolid` files into Area Calc's 2023 build.
- Single-tool code now sitting in `Shared/` moves to its tool: `RichTextBoxHelper`, `MarkdownConverter`, `PathUtil` → Family Browser; `CountToVisibilityConverter` → Area Calc. Rule stays: a file is shared iff two add-ins use it. By that rule `RelayCommand<T>` stays shared: since `round-positions` was merged, Round Positions (Tools) uses it besides Family Browser.

**Theme** (my call): a path-relative `Source` breaks silently when a view's folder depth differs between add-ins, and a wrong path only fails when the view is constructed — at Revit start for the panes. Instead:
- `DarkTheme.xaml` is linked with an explicit `Link="Shared\Themes\DarkTheme.xaml"`, so its resource id is always `shared/themes/darktheme.baml`.
- A small class `DarkThemeDictionary : ResourceDictionary` (namespace `RVTuk.UI.Shared.Themes`) sets its own `Source` to `/<its own assembly name>;component/Shared/Themes/DarkTheme.xaml`. The 11 views (listed in the Phase 0 record) merge `<theme:DarkThemeDictionary/>` instead of the `/RVTuk.UI;component/…` literal. Depth-independent; a missing class is a compile error. Checked on paper (local `clr-namespace` without an assembly part already works in these views; a `ResourceDictionary` subclass that sets `Source` in its constructor is found by `StaticResource`) and proved outside Revit on .NET Framework 4.8 and .NET 8 (Phase 1 record, S2); phase 2's switch of the 11 views was its first run inside Revit (passed in 2024, 2026 and 2027 — Phase 2 record).

**Host helpers** every entry class uses instead of today's copied boilerplate:
- **RVTuk tab**: `CreateRibbonTab("RVTuk")`, catching only `Autodesk.Revit.Exceptions.ArgumentException` (another add-in created the tab first — documented; it is Revit's own class and does not derive from `System.ArgumentException`, so a catch of the .NET class misses it), then `CreateRibbonPanel("RVTuk", "<display name>")`. Any other failure (e.g. Revit's 20-custom-tab limit) falls back to a panel on Add-Ins so the tool stays reachable. Button internal names stay (`BrowseLibrary`, `AreaCalc`, …).
- **Assembly resolve, name-scoped**: answers only when the requested simple name is a DLL in the add-in's own folder; returns null for `*.resources`; never looks at the requester (on .NET Framework `RequestingAssembly` is null for `LoadFrom` assemblies; on .NET 8/10 XAML lookups arrive from `System.Xaml`). Probes outside Revit found nothing that needs the hook at all, so each phase 3 step first runs its add-in with no hook, and an add-in that works without one registers none (expected: only Family Browser keeps it).
- **Startup guard**: each host's whole `OnStartup` body — including pane-view construction and `RegisterDockablePane`, which sit outside the try/catch today (`Application.cs:166-370`) — is wrapped; a failure is logged and returns `Result.Failed` for that add-in only.
- **Crash log**: named handler, unsubscribed at shutdown, own file `%LOCALAPPDATA%\RVTuk\<AddIn>-crash.log`. Ownership is decided by **assembly identity**, never by namespace text (namespaces and shared types are identical across add-ins): walk the exception's stack frames (and inner exceptions) and log only if a frame's assembly is one of the add-in's own three. The two commands that write `crash.log` directly (`BrowseLibraryCommand.cs:175`, `RishuiZaminCommand.cs:96`) use the helper too.
- **Family Browser's `DispatcherUnhandledException` handler** (`BrowseLibraryCommand.cs:177-220`, today marks every exception handled while its window is open): handles an exception only if a frame is its own, or — for layout/rendering exceptions with framework frames only, the case it was written for — while a Family Browser window is the active window. Everything else is left unhandled.

### Settings

One file per add-in in the existing folder `%APPDATA%\Autodesk\Revit\Addins\RVTuk\`: `FamilyBrowser.json`, `AreaCalc.json`, `DwgExport.json`, `Topo.json`, `Tools.json`. Each add-in owns a settings class with only its keys, **JSON key names unchanged** (22 keys after phase 0: Family Browser 3, Area Calc 2, DWG Export 15, Topo 1, Round Positions 1).

- **No migration step.** Load = own file if present, else the old `config.json` read-only, else defaults. Save = own file only. Nothing is written until a setting changes; an add-in's settings fork from `config.json` at its first save and are not synced back. Safe because every save site re-reads the file just before saving (checked).
- **The store never overwrites what it could not read** (today's loader returns defaults on any read error, `ConfigManager.cs:37-40`): it reports missing / ok / unreadable; a save is skipped after an unreadable old `config.json`; an unreadable own file is kept aside as `.bad` (replacing an earlier `.bad`) before a new one is written. Writes go to a temp file in the same folder, then `File.Replace` when the target exists, else `File.Move`; an `IOException` is retried briefly and then the save is skipped.
- **Serializer contract** (the same file is read by `DataContractJsonSerializer` on net48 and `System.Text.Json` on net8/10): settings classes are plain classes with a public parameterless constructor and public get/set properties named exactly as the JSON keys; **no serializer attributes** (with `[DataContract]` the net48 serializer skips the constructor and every default becomes null/0); members are only string, bool, number, enum-as-int, or `List` of such plain classes — no `Dictionary`, no `DateTime`. A code comment from the two branches merged in phase 0 contradicted this; the net48 test run of phase 2 confirmed the contract and the comments were corrected (Phase 2 record).
- Tests: the settings tests run on net48 and net8; golden per-add-in files are produced from a copy of a real `config.json` through both serializers.

### Family Browser specifics

- Third-party packages (Microsoft.Data.Sqlite, SQLitePCLRaw + `e_sqlite3.dll`, OpenMCDF 3.1.2, System.Drawing.Common) move into Family Browser's Core project only; no other add-in ships them (checked: only Family Browser code uses them). `SqliteNative.EnsureLoaded` keeps pre-loading from its own assembly's folder. `RVTuk.db` keeps its name.
- **Instruction XAML — two bugs in the build before phase 0, fixed in phase 0.** Instructions with an inline image are stored in the database with assembly-qualified declarations. A row that fails to parse opens as an **empty** document (`RichTextBoxHelper.cs:94-103` at `c6d4990` swallows the error) and Save writes that back (`InstructionsEditorWindow.xaml.cs:331-337` at `c6d4990`) — also on a tags-only edit.
  - *Bug 1*: rows saved before the 15 July 2026 namespace move declare `clr-namespace:RVTuk.UI.Controls;assembly=RVTuk.UI`; builds since then read only `RVTuk.UI.Shared.Controls`. The database has 3 instruction rows; the 2 with images are in the old form, so they open empty in the build before phase 0 (seen in a read-only survey and a parse probe outside Revit; reproduced outside Revit by the UI tests on sanitized copies of the two rows: before the fix both legacy fixtures failed on net48 and on net8). Checked in Revit 2024 on 2026-11-15 with the phase 0 build: both rows open with their text and image.
  - *Bug 2*: every image's hover style serialises a `System.Boolean`; .NET 8/10 writes `assembly=System.Private.CoreLib`, which Revit 2024 (.NET Framework) cannot parse. An image instruction saved from Revit 2025+ opens empty in 2024 (reproduced by the UI tests: before the fix the `raw-net8` fixture failed on net48).
  - **Fix = a string rewrite on read and on write**, as a pure function in Family Browser's Core that takes the target namespace and assembly name as parameters (the UI caller passes the helper's own). The split makes this necessary anyway: the declaration carries the assembly name, which changes.
    - Read: map `RVTuk.UI.Controls;assembly=RVTuk.UI` and `RVTuk.UI.Shared.Controls;assembly=RVTuk.UI` to the helper's real namespace and assembly, and `assembly=System.Private.CoreLib` to `assembly=mscorlib`.
    - Write: always emit exactly `clr-namespace:RVTuk.UI.Shared.Controls;assembly=RVTuk.UI` (one fixed form, wherever the helper lives) and `assembly=mscorlib` for the System namespace (parses on both runtimes); the hover style is cleared before `XamlWriter.Save` and restored after, like `Image.Source` already is (`RichTextBoxHelper.cs:153-190`).
    - A parse failure is reported, not swallowed: the viewer says the text cannot be shown, and the editor refuses to save over a stored row that was non-empty but did not parse.
  - Tests: the rewrite function in the Core tests; a UI test project targeting net48 and net8 in which each runtime parses checked-in strings that the *other* runtime's `XamlWriter` produced, including one with the CoreLib declaration. Fixtures are the real rows' markup with the text replaced and each image replaced by a 1×1 PNG — real rows are never committed.

### Frozen identifiers (move byte-identical; a test asserts each)

- Extensible Storage schemas — GUID, schema name, fields, `SetVendorId("KnafoKlimor")`, Public read/write: `7b3c1d92-4e58-4a0f-9c21-6d5f8e3a17b4` RVTukAutoDimensionTrackingByView · `6f2c81d4-9a7b-4e35-8c10-b4d6e27f9a51` RVTukAutoDimensionsScopeSelectionV3 · `6d21f8b4-0c93-4e57-a8d6-b45f70e29c18` RVTukTopoLineElevation · `3a6c50f1-8b47-4d2e-95c3-1f8a4b7e0d62` RVTukTopoToolsPointLedger · `a9b2b113-115d-48a4-80a5-3063ddc3caca` RVTukRoomFloorsLink · `309b77b4-64cd-45c7-bd73-d6a81b1f85ae` RVTukTopoToolsParkedParameters (came with `topo-convert`). Checked: changing ClientIds orphans none of them — every schema is Public with VendorId only; nothing uses `ActiveAddInId`, `UpdaterId` or `SetApplicationGUID`.
- **Cross-add-in contract**: Topo's slab converter (`topo-convert`, `TopoTools/SlabConverter.cs:107-120`) copies every storage entity whose schema VendorId is `KnafoKlimor` — so it carries Tools' Room Floor link and any future schema of any RVTuk add-in. The VendorId is therefore frozen too.
- DockablePaneIds: `e5d47b31-2c8a-4f16-b0d9-73a5e91c46f2` Auto Dimensions · `a17c46e9-5b83-4d20-8f6a-9c4e2b70d135` Topo Tools · `88a9c4fa-9c6a-4b02-b169-701be8358090` Neo Properties · `b7a1c94e-3f52-4d08-9c6b-1e84a5f2d370` Round Positions.
- Area Calc: the five shared-parameter GUIDs **and names** (`RZ_USAGE_TYPE`, `RZ_USAGE_TYPE_OLD`, `RZ_AREA`, `RZ_ASSET`, `RZ_BUILDING_NO` — binding is detected by name), parameter group `RVTuk`, key-schedule names `New Usage` and `Old Usage` (`UsageKeyScheduleBuilder.cs:37-54,132`); embedded resource names `RVTuk.Core.RishuiZamin.DxfTemplates.*`.
- Line-style names `_DP-*`; ribbon button names; JSON settings key names; the database file name `RVTuk.db`.
- Retired, never reused: `RZ_UsageType` (`f80b9154-f0e7-4f01-82d0-3ec259872e5c`) and schedule `RZ Usage Key Schedule`. They belonged to `SetupRishuiZaminParamsCommand`, which nothing referenced (checked); it and `RZ_AreaParams.txt` were deleted in phase 0 (my call; `3c56866`).

### Build (my call)

| Config | TFM | Revit API package | Constant |
|---|---|---|---|
| Release2023 | net48 | 2023.* | REVIT2023 |
| Release2024 | net48 | 2024.* | REVIT2024 |
| Release2025 | net8.0-windows | 2025.* | REVIT2025 |
| Release2026 | net8.0-windows | 2026.* | REVIT2026 |
| Release2027 | net10.0-windows | 2027.* | REVIT2027 |

Defined once in a root `Directory.Build.props`, on the monolith, before any tool moves (checked: SDK 10.0.404 and the 2027 API package are already on the dev PC). Before phase 2 the five configurations existed only per project (PR #12, PR #14): `RVTuk.Core` and `RVTuk.UI` each carried their own 2026 and 2027 rows, `KKarea.Revit` built for 2023 and 2026, `RVTuk.Revit` for 2024, 2025 and 2027 (not 2026), and the tests for neither new year. Phase 2's build half (2026-11-16) moved the rows into the root props and extended them to every project: `RVTuk.Revit` builds for 2024–2027, `KKarea.Revit` for 2023 only, the tests for 2024, 2025 and 2027 — see "Phase 2 record (build half)".

- **Three project classes.** *Add-in projects* take TFM, API package and constant from the matrix. *Test projects* follow the same matrix (as KKompare's do), so tests run on net48 (Release2024), net8 (Release2025) and net10 (Release2027). *Tool projects* (the installer and its tests) opt out through an empty `installer\Directory.Build.props` (a property in the project file is read after the root props, too late) and stay outside `RVTuk.sln`.
- **A configuration outside the table fails the build.** For restore and the IDE to keep working, such a configuration (including none — inside `Directory.Build.props` an unset configuration is the empty string) still evaluates as net8.0-windows with the 2025 API package; a target that runs before `CoreCompile`, skipped in design-time builds, raises the error. Before phase 2 `RVTuk.Core`, `RVTuk.UI` and `RVTuk.Revit` silently mapped anything unknown to 2025 (`KKarea.Revit` fell back to 2023), and that catch-all was the only reason the Core tests built: the Core test project was mapped to `Debug` in every `Release20xx` solution configuration. The UI test project added in phase 0 picked its framework itself — net48 under `Release2024`, net8 otherwise. One case the error does not cover: `RVTuk.sln` built or tested without `-c` uses the solution's first configuration, `Release2023`, which builds no test project — so every command carries `-c`, and `Test-All.ps1` demands a result line from every test project.
- **Solution**: the `Debug`/`Release` configurations are deleted; every project is mapped to the same-named project configuration (solution restore uses the solution configuration's name); a year an add-in does not support is an `ActiveCfg` line without `Build.0`, as the solution does today, and the staging check compares those lines with each add-in's `Years`. Every documented command carries `-c`.
- **One NuGet assets folder per configuration**: the root props sets `MSBuildProjectExtensionsPath` to `obj\$(Configuration)\` and leaves `BaseIntermediateOutputPath` at `obj\` (checked by an evaluation probe — moving `BaseIntermediateOutputPath` instead makes each build compile its siblings' generated files). Reason: 2023/2024 share net48 and 2025/2026 share net8, and with one shared assets file a build after switching configuration compiles against the other year's Revit API without any error.
- **Framework conditions never fall through silently**: before phase 2 `RVTuk.Core.csproj` tested for exact frameworks — its own blocks for net48, net8.0-windows and, since PR #12, net10.0-windows (which took System.Drawing.Common 10.0.* where net8 took 8.0.*). A framework without a block silently got no Microsoft.Data.Sqlite and no System.Drawing.Common. Phase 2 made the conditions net48 versus everything else instead of a table of versions (my call, 2026-11-16): Microsoft.Data.Sqlite is unconditional and System.Drawing.Common takes its runtime's own version everywhere but net48. The root props can only produce the three frameworks of the table, so none can fall through.
- **Tests on net48** need: System.Text.Json as a test-only package, the two `System.IO.Compression` references, and no Core-only string overloads (the existing tests use all three — checked). They also need a way to open SQLite: in a net48 xunit host, opening a `SqliteConnection` (Microsoft.Data.Sqlite 8.0.29) throws from `SQLitePCL.Batteries_V2.DoDynamic_cdecl` — a `FileLoadException` for `System.Memory` in the 2026-11-15 probe; `e_sqlite3.dll` lands only under `runtimes\<rid>\native`, and copying it flat does not help — while a net48 console exe opens it. The UI test project therefore kept its database tests net8-only (`#if !NETFRAMEWORK`) in phase 0. A probe in a scratch copy on 2026-11-16 found the cause: xunit's shadow copy runs the test DLLs from a temp folder that has no `e_sqlite3.dll`. With `xunit.runner.json` holding `{ "shadowCopy": false }` every database test passed on net48 (Core 569 of 569, UI 33 of 33) and the `System.Memory` error did not appear; phase 2 adds that file to both test projects and removes the `#if`.
- `global.json` pins the SDK (10.0.x); SDK 9 is also installed.
- Each add-in's `addin.props` is the single source for its display name, ClientId, `Version` and `Years` (the configurations it builds for); the add-in table the scripts use is generated from those files plus the fixed order key. Every add-in compiles for every year it will support from the start.
- **`build\Test-All.ps1`** is the one command that runs everything: `dotnet test RVTuk.sln` with `-c Release2024`, `-c Release2025` and `-c Release2027`; the installer tests join it with the install-engine plan. Linux/cloud runs can only run the Core test projects on net8/net10, project by project.
- Unique assembly names make Revit 2026/2027 manifest isolation unnecessary; no `ManifestSettings` is emitted.

### Releases — how "update one, leave the others alone" is made true (my call)

Rebuilding everything from the current source for every installer would put fresh, untested builds of five add-ins under their old version numbers (shared code is source, and package versions float). So released builds are kept, and the installer is assembled from them:

- Each add-in carries its own `Version` in `addin.props` (start 1.0.0), bumped by hand.
- **`Release.ps1 -AddIn <name>`** releases one add-in: refuses a dirty working tree; builds it for all its `Years` from clean `bin`/`obj`; runs its tests; stages it; writes `addin.json` (name, version, commit, build time, manifest file name) into each staged folder; copies the result to the **release store** as `<AddIn>\<version>\<year>\…`; tags the commit `<Folder>-v<version>` (folder name, no spaces). It **refuses a version that already exists in the store**, so a forgotten bump cannot ship different files under an old number.
- **`Release.ps1 -Status`** lists the add-ins whose inputs changed since their last release tag: own folder, the `Shared/` files they link (taken from the evaluated project items), `build/`, root build props, `global.json`, and untracked files in those paths. An edit under `Shared/` reaches an add-in only when that add-in is next released.
- **`Build-Installer.ps1`** compiles nothing but the setup exe: it packs the newest released version of every add-in from the store, prints the catalog and the `-Status` list. An add-in that was not released again ships byte-identical files.
- **Which years the installer offers** is a packing decision, not a build input: `build\released-years.json` lists them per add-in; other built years are packed as *preview* and offered only with `--preview`. A year is added to the list after its manual pass in Revit, and the installer is repacked — the files that were tested are the files that ship.
- **Release store** = a plain folder, default `releases\` in the repo root (git-ignored), path overridable so it can live on the office server; no script ever cleans it. A lost store can be rebuilt from the tags (equivalent, not byte-identical, while package versions float).
- `Deploy.ps1` (dev) builds from the working tree and marks its `addin.json` as a dev build.

### Staging and deploy

- **One staging function** in `build/` replaces the duplicated tables and copy rules in `Deploy.ps1` and `Build-Installer.ps1` (and KKompare's copies). It keeps today's copy-and-strip rules from `Deploy.ps1`: flat DLLs/PDBs (and `.deps.json` on net8/net10), `e_sqlite3.dll` flat, the net48 polyfill strip list. It writes `addin.json`; it writes **no manifest** — the install engine does, from the catalog.
- **Machine-wide path rule** (checked): `C:\ProgramData\Autodesk\Revit\Addins\{year}` up to 2026; `C:\Program Files\Autodesk\Revit\Addins\{year}` from 2027. Autodesk states it for 2027: "Revit no longer looks for machine-wide add-ins under ProgramData" (Phase 1 record). `Deploy.ps1` (`Get-AddinsBase`) and the setup exe (`MachineAddinsBase`) follow the rule since PR #14; the engine takes it over in phase 2.
- **`Deploy.ps1 [-AddIn <names>] [years]`** stages from the working tree and hands the folder to the install engine (`--source`), so install and remove exist once, in C#. `-Clean` = `--remove all` for the given years.
- **Staging check** (`build\`, script): each staged folder holds only its own three assemblies with their `.pdb` (and `.deps.json` on net8/net10) and `addin.json` — plus the third-party set for Family Browser (and for the remaining monolith during phase 3); no vendor DLL name appears in two add-in folders; each `*.Revit.dll` is read by metadata — the catalog's entry class exists in it, its RevitAPI reference matches the year, its target framework matches the matrix; each UI assembly contains `shared/themes/darktheme.baml`; no source file names `RVTuk.UI;component`; per year, the union of the staged folders equals the file list recorded from today's payload except for renamed project files (and, on the year KKarea serves — 2023 — minus the third-party set that KKarea ships today without using it).

### Installer

**Engine** — a tested library with a command line, inside `installer\RVTuk.Setup`, built before the split:
- Works on an injected *environment*: machine-wide Addins root per year (path rule above), Revit years installed, Revit years running. Tests inject temp folders; `--target <dir>` maps every root under one folder; `--years` overrides detection.
  - *Installed* = `Revit.exe` found through `InstallationLocation`, a value of a subkey of `HKLM\SOFTWARE\Autodesk\Revit\<year>` (64-bit view; on the dev PC the subkey is `REVIT-05:0809`, and the year key itself holds no values), falling back to the default folder. The Addins folder is not a sign: the dev PC has one for 2025 without Revit.
  - *Running* = a Revit process whose file version's major part + 2000 is the year (checked on the `Revit.exe` files of 2023, 2024, 2026 and 2027, and on a running Revit 2024), falling back to the install path.
- Payload: `catalog.json` + `{addin}/{year}/…`, embedded in the single exe, or a staged folder given with `--source`. The catalog carries, per add-in: manifest name, entry class, ClientId, version, years (released/preview) and the old installs it replaces. The engine writes the manifest (relative `Assembly` path, as today).
- **Install/update of one add-in in one year**: stage beside the target, rename the existing folder away (this is the lock test; renames retry for a few seconds because antivirus scans fresh DLLs), swap the new folder in, write the manifest, delete the renamed folder; any failure rolls the renames back. **Remove** uses the same rename-first step, then manifest, then folder. A locked year changes nothing.
- **State** comes from `addin.json` in each add-in folder. An add-in found on disk but missing from the catalog is listed with Remove as its only action.
- **Versions**: catalog newer than installed → update; equal → untouched (no file is written); installed newer than the catalog (an old setup exe) → left alone and shown as "newer"; dev build → replaced.
- **Old installs — one rule.** The catalog names, per add-in, the old manifest it replaces (with its folder): Family Browser, Area Calc, DWG Export, Topo and Tools ← `RVTuk.addin`, `ReviTchucky.addin`; Area Calc ← `KKarea.addin`; Template ← `KKompare.addin`. Installing an add-in in a year removes the old installs it replaces in that year, in the same rename-first step; `--remove all` removes them too. Each of these lines is added in the step that deletes the old project (so the dev-only "remaining monolith" of phase 3, which still uses `RVTuk.addin`, is never touched). Nothing else: no migration checks, no warnings — on a PC with old RVTuk, installing only some of the five simply drops the others, which is acceptable because nobody uses the old version yet.
- Never touches settings, the family database or crash logs. The exit code is non-zero when a requested year failed or was skipped. Logs: the elevated copy writes `%ProgramData%\RVTuk\setup.log`, un-elevated runs write `%LOCALAPPDATA%\RVTuk\setup.log`; a log that cannot be written never fails the run.

**Command line**: `--install <names|all>`, `--remove <names|all>`, `--years`, `--preview`, `--quiet`, `--source`, `--target`, `--no-pause`; `--uninstall` stays as an alias of `--remove all`. Without `--target`, a non-elevated run relaunches itself elevated (UAC prompt), also with `--quiet`.

**Window** — WinForms, the layout Daniel approved (yours):

```
 RVTuk Setup                                  built 2026-11-12
 ---------------------------------------------------------------
      Add-in           Installed   Available    Revit
 [x]  Family Browser   1.4.0       1.5.0  (new) 2024 2025
 [x]  Area Calc        1.2.1       1.2.1        2023 2024 2025
 [ ]  DWG Export       -           1.1.0        2024 2025
 [x]  Template         1.0.3       1.0.3        2024 2025 2026
 [ ]  Topo             -           1.0.0        2024 2025
 [x]  Tools            1.3.0       1.3.2  (new) 2024 2025
 ---------------------------------------------------------------
 Revit 2024 is open - close it to update.    [ Apply ] [ Close ]
```

Rules behind it (my call):
- State is per add-in **and** per Revit year, so the **Revit column** shows each year this add-in supports on this PC with a mark: up to date, will update, will install, or Revit open (skipped).
- **A row starts ticked** when the add-in is installed in any year, or an old install it replaces is found. Tick = install where missing and update where older, in every such year; untick = remove from every year. A row with no usable year is unticked, disabled, and says why (e.g. only Revit 2023 on this PC).
- **Installed column**: the version from `addin.json`; `mixed` when years differ; `old` for an old install; `dev`; `newer`.
- **Revit open in a year**: that year is skipped for install and remove and named in the status line; other years proceed. After Apply a per-year result list is shown, with "close Revit 20xx and apply again" for skipped years. Apply is disabled when nothing would change.
- The window runs **un-elevated** and does all detection there; it elevates only on Apply, passing the elevated copy the choices (add-in, year, action) — never paths; that copy recomputes every path from its own embedded catalog and writes its per-year results to a file under `%ProgramData%\RVTuk` that the window reads when it exits. The exe becomes a Windows-subsystem program that attaches to the parent console in command-line mode (today it is a console program).

One installer codebase replaces `installer\RVTuk.Setup` as it is today and `Template\installer\KKompare.Setup`.

## Phases (in order; each gets its own implementation plan, except phase 1, which was closed from evidence)

**Everything lands on master in small steps** (my call). Nobody uses RVTuk yet, so nothing has to stay installable for the office while the split is half-done; what must keep working is the dev PC's own Revit, and phase 3 is ordered for that. `CLAUDE.md` and `README.md` change in the same step that changes what they describe.

| # | Phase | Done when |
|---|---|---|
| 0 | **Prep.** (a) Fix the two instruction bugs, with the Core rewrite tests and a UI test project that builds for net48 under Release2024 and net8 under Release2025 (KKompare's test-project pattern). (b) Merge `topo-convert` and `round-positions` (yours). (c) Delete the dead setup command and its txt file. (d) Record: frozen identifiers, views, command files, settings keys, test count (343 test methods before phase 0, 459 after it), the installed file list per year. (e) Tag `before-split` | master builds for all five configurations; the Core tests pass and the UI tests pass with `-c Release2024` and `-c Release2025`; the two old image instructions open in Revit 2024; the lists in this spec are re-checked |
| 1 | **Spikes** — closed on 2026-11-15 from evidence, without spike code (Daniel's decision); the answers are in "Phase 1 record". What each spike asked: S1 two add-ins share the RVTuk tab, panels in manifest order, with no assembly-resolve hook (2024 + 2026.5); Revit's real manifest load order. S2 `DarkThemeDictionary` in two differently named UI assemblies with views at both folder depths; the theme's resource id. S3 monolith + one split add-in in one session: what fails; pane layout after a ClientId change and with a missing pane provider. S4 unsigned-prompt count and whether an updated DLL re-prompts. S5 Family Browser alone on a PC without pyRevit/ProSheets. S6 (build) the root-props matrix on the monolith: all five configs restore and build with per-configuration assets, `Release2023` with projects excluded, the error target; size of the 2026/2027 API differences in the tools other than Area Calc (whose code already builds for both since PR #12). The checks that need a running add-in moved to the first step that runs one: the theme class inside Revit (phase 2); the shared tab with panels in manifest order, loading without the resolve hook, a pane's saved place, and the monolith beside a split add-in, including a pane whose add-in is not installed (phase 3); thumbnails on a clean PC (phase 4). The build-matrix run comes first among phase 2's build changes | done: every spike has a written result or a named later step. A check that fails there changes this design at that point |
| 2 | **Foundation** (two plans: build, install engine). *Build*: root build props holding the five-config matrix (the per-project 2026/2027 rows of PR #12 and PR #14 move into it), extended to `RVTuk.Revit` for 2026 and to the tests — which answers what spike S6 was for: all five configurations restore and build with per-configuration assets, `Release2023` with projects excluded, and the error target — with the 2026 compile fixes for the tools other than Area Calc made here (PR #14 made the 2027 ones); solution clean-up; tests follow the matrix and compile on net48; `.gitignore` fix (below); `Test-All.ps1`; on the monolith, before anything moves: the frozen-identifier tests, a WPF smoke test that constructs every Window/UserControl on an STA thread, the 11 views switched to `DarkThemeDictionary` (the theme class's first run inside Revit, which spike S2 left open), and the net48 test run that settles the serializer comment (see "Settings"; the generic settings store itself is built in phase 3). *Engine*: `addin.props`, staging function, staging check, the install engine, `Deploy.ps1` on top of it | all five configs build, a configuration outside the table fails with the error, and a build straight after another year's build references its own year's Revit API; `Test-All.ps1` passes on net48, net8 and net10; the staging check passes on the monolith payload; engine tests pass (fresh, update, remove, partial years, skipped year, differing years, locked file, old install replaced); `Deploy.ps1` deploys the unchanged monolith through the engine and Revit 2024 looks the same; the monolith loads in Revit 2026.5 with its panes in the dark theme (the theme class on .NET 10), and an image instruction saved in 2024 opens in 2026.5 and back |
| 3 | **The split** — a shrinking monolith: each step moves one tool out with its settings keys, deletes its registration from `src\RVTuk.Revit\Application.cs` in the same step, and the add-in list keeps a dev-only "RVTuk (remaining)" entry under the old manifest and ClientId (and "KKarea" for the years it serves, until the Area Calc step). Order: Tools (Room Floor first, then Auto Dimensions, then Round Positions — the first step that moves a settings key, so it builds the generic settings store under `ConfigManager` — then Neo Properties), DWG Export, Area Calc (deletes `src\KKarea.Revit`), Topo, Family Browser last (deletes the monolith and takes its ClientId) | per step: `Test-All.ps1` and the staging check pass; in Revit 2024 the remaining monolith and the split add-ins load together with every tool present exactly once; the add-in runs without the resolve hook in Revit 2024 and in Revit 2026.5, or keeps it with the reason written down; its manual checklist passes (2023 too for Area Calc); the test count is not lower than the phase 0 record. Two steps also record what spikes S1 and S3 left open. The Auto Dimensions step: its pane keeps its saved place under the new ClientId; then Tools is removed and Revit must start without an error dialog or an empty pane frame. The DWG Export step, the first with two add-ins on the RVTuk tab: the second add-in to start gets the existing tab, and DWG Export's panel (key 3) stands left of Tools' (key 6). Until the monolith is gone, these manual checks run with the Family Browser window closed, because its handler marks every exception handled |
| 4 | **Releases and setup window** — `Release.ps1`, release store, `released-years.json`, the new `Build-Installer.ps1`, the window, `--preview` | automated installer tests pass; manual: a clean PC with Revit 2024 and without ProSheets and pyRevit (scanning a few families there and seeing their thumbnails settles the OpenMcdf question — Phase 1 record, S5), the dev PC with its old installs, partial selection, update, remove, Revit open, started from the office share (UNC and mapped drive) as a non-admin — if elevation loses the share, the window copies itself to a local folder first; Revit 2025 on an office PC through `--preview`. **First setup exe: five add-ins, Revit 2023–2025** (Area Calc also for 2026/2027 if its Revit pass is done by then) |
| 5 | **Template** — import KKompare with its history (below); it joins the solution, the build matrix (builds for 2024–2027, excluded from Release2023), staging, the RVTuk tab and the catalog, offered for 2024–2026. Until then installed KKompare keeps working untouched | KKompare's 184 tests pass from the RVTuk solution on net48, net8 and net10; Template loads in the RVTuk tab on 2024 and 2026.5; the setup replaces the dev PC's old KKompare |
| 6 | **Revit 2026 and 2027** — add-in by add-in (the whole monolith ships for 2026 since phase 2 and for 2027 since PR #14; in 2027 it loads with every ribbon button, its tools not yet checked one by one): manual pass in Revit 2026.5 and in Revit 2027 (installed on the dev PC since 2026-11-15); fix what the pass finds; add the year to `released-years.json`; repack | each add-in offered for both years after its pass |
| 7 | **Docs sweep** — rewrite the VISION.md principle, per-tool README code paths, move the tool-visibility spec to `docs/archive/` as superseded | `CLAUDE.md` commands all run as written |

**`.gitignore` fix (phase 2, before any test folder moves)**: `tests/output/` and `tests/Examples Autoarea/` (28 MB of real office drawings, untracked) are ignored by root-anchored patterns that stop matching once tests move under an add-in folder. Make them unanchored, remove the line that ignores `.gitignore` itself, add `releases/` (the staging folder's line comes with the engine plan, which names the folder), and add a check that no untracked `.dxf`, `.dat`, `.dwfx` or `.rfa` sits outside known fixture folders.

**Template import (phase 5)** — (my call) "move, then merge" instead of `git subtree add`: in a clone of KKompare, one commit moves everything under `Template/`; RVTuk then merges that history (`--allow-unrelated-histories`, a merge commit, no squash). Same result as chosen — the `Template/` folder with all 51 commits and their hashes — and `git log --follow` keeps working through the move, which it does not across a subtree import (checked: no path conflicts, ignore and attribute files stay correct). Scope of the phase: commit or discard KKompare's pending `Deploy.ps1` edit first; strip the per-project `TargetFramework`/`Configurations`/catch-all from its five csproj files (they would override the root matrix and silently build 2027 as net8); add `Template/addin.props`; delete `Template/KKompare.sln`, `Deploy.ps1`, `Build-Installer.ps1` and `installer/`; rewrite `Template/CLAUDE.md`; switch its panel to the RVTuk tab helper; afterwards archive the KKompare GitHub repo read-only with a pointer (yours to do).

## Critical files

- `src\RVTuk.Revit\Application.cs` — dissolves into six entry classes + `Shared/Revit` host helpers
- `src\RVTuk.Core\Shared\Config\AppConfig.cs`, `ConfigManager.cs` — split into per-add-in settings + the generic store (load/save call sites in 7 files, plus one in the Revit host after phase 0)
- `src\RVTuk.UI\Shared\Themes\DarkTheme.xaml` + the 11 views that reference `/RVTuk.UI;component/…`
- `src\RVTuk.UI\Shared\Controls\RichTextBoxHelper.cs:87-190`, `src\RVTuk.UI\FamilyBrowser\Views\InstructionsEditorWindow.xaml(.cs)` (`:36-44` hover style, `:340-354` Save) — instruction XAML
- `src\RVTuk.Revit\FamilyBrowser\Commands\BrowseLibraryCommand.cs:175-220` — crash-log writer and the dispatcher handler
- `src\RVTuk.Core\RVTuk.Core.csproj`, `src\RVTuk.UI\RVTuk.UI.csproj`, `src\RVTuk.Revit\RVTuk.Revit.csproj`, `src\KKarea.Revit\*`, `RVTuk.sln`, `tests\RVTuk.Core.Tests\*` (55 test files; `tests\RVTuk.Core.Tests\Shared\AppConfigTests.cs` mixes five tools: Family Browser, Area Calc, DWG Export, Topo, Round Positions), `tests\RVTuk.UI.Tests\*` (4 test files, since phase 0)
- `Deploy.ps1`, `Build-Installer.ps1`, `installer\RVTuk.Setup\Program.cs` + `RVTuk.Setup.csproj`, `.gitignore`
- `D:\Coding\KKompare` (whole repo → `Template/`; its five csproj files, `CLAUDE.md`, `KKompare.sln`)
- `VISION.md`, `CLAUDE.md`, `README.md`, `docs\toolkit\specs\2026-07-03-tool-visibility-design.md`

Reuse, don't rewrite (line references checked): KKarea's linked-source host (the linked `Compile` items in `src\KKarea.Revit\KKarea.Revit.csproj`, and its `Application.cs`) as the pattern for per-file links and a standalone host; the copy-and-strip block in `Deploy.ps1`; elevation and `--target` in `installer\RVTuk.Setup\Program.cs` (`Main`'s elevation check, `IsElevated`, `RelaunchElevated`); KKompare's named, unsubscribed crash handler (`Application.cs:76-91`), its test projects' Release2024→net48 switch and its WPF test project (`tests\KKompare.UI.Tests`).

## Verification

1. **Build + tests** — the finish line of every step in phases 2, 3, 5 and 6 (suits a `/goal` loop): all five `dotnet build RVTuk.sln -c Release20xx` succeed and `build\Test-All.ps1` passes; the number of tests is not lower than the phase 0 record.
2. **Staging check** passes (rules above).
3. **Frozen-identifier tests** assert every GUID, schema name, pane id, parameter GUID and name, schedule name, resource name and JSON key listed above.
4. **No behaviour change**, each compared on the same model and the same Revit year, the build at tag `before-split` versus the split build (KKarea is the old build on 2023 and 2026): the Area Calc `.dxf` is byte-identical (covers Hebrew/bidi output; the `.dat` is a constant and proves nothing); DWG Export file names, the transmittal zip's file list and its report text are identical; "Setup Usage Keys" on a project set up by the old build reports every parameter present and creates no second schedule; settings are read from a copy of a real `config.json` on net48 and net8; the instruction round trip passes in both directions between runtimes. For 2026 only Area Calc has a "before" (KKarea); for 2027 the "before" is the monolith as of PR #14, which is later than the tag. Where there is no "before", compare against the 2024 output and expect identity apart from documented differences.
5. **Manual in Revit**, per add-in and year: RVTuk tab and panel order; panes open, dock and restore; existing project data still read (Auto Dimensions tracking, Topo elevations/ledger, Room Floor links, usage-key schedules); installing a subset shows only those panels.
6. **Installer**: the automated cases listed in phase 2 and the manual cases in phase 4.

## Known risks carried forward

- Revit 2025 is not installed on the dev machine: its builds are verified by compile, then on an office PC through `--preview` before the year is offered by default. Revit 2026.5 runs on .NET 10, as Revit 2027 does, so the dev PC has no Revit on .NET 8 at all: a net8 build is seen on .NET 8 only on an office PC with Revit 2025 or a Revit 2026 older than 2026.5.
- Unsigned prompts: one per add-in per Revit year per user at first start; the rollout note must say so, because each prompt offers "Do Not Load". An update may prompt again (Phase 1 record, S4). Whether signing is worth it is decided at rollout.
- OpenMcdf 3.1.2 collides with pyRevit's 3.1.4 and references a stripped `System.Memory 4.0.2.0`; unchanged by the split. It is now a Family Browser backlog item: on a PC without ProSheets or pyRevit nothing provides that `System.Memory`, so thumbnails are expected to fail silently (Phase 1 record, S5).
- If RVTuk is rolled out to the office before phase 4 is done, the "nobody uses it yet" simplifications (no migration checks, master not kept installable) stop being safe and this design must be revisited first.
- Topo on Revit 2026 and 2027: those APIs no longer have `DrawPoint`, so `AddPoint` is used there (`src/RVTuk.Revit/TopoTools/SlabShape.cs`; PR #14 for 2027, phase 2 for 2026); whether it accepts points on an edge is not checked in Revit → phase 6's pass.

## Phase 0 record

Taken at `3c56866` on 2026-11-15 (phase 0 plan, task 9); the test rows of "Tests and builds" were re-measured at `642d43a`, after the final review added four UI test methods. The values come from the commands under each heading, run at those commits; where a value needed more than a command's raw output (uses rather than matching lines, counts at earlier commits), the text says how it was taken, and "Other findings" are probe results. Later phases compare against these values.

### Frozen identifiers

```powershell
$ids | ForEach-Object { git grep -n -i $_ -- src }   # the ten GUIDs below
git grep -n 'SetVendorId\|"KnafoKlimor"' -- src
git grep -n -E 'RZ_[A-Z_]+"|new Guid\(|ScheduleName = ' -- src/RVTuk.Revit/RishuiZamin/UsageKeyScheduleBuilder.cs
git grep -n -o -E '"_DP-[^"]+"' -- src
git grep -n -A1 'new PushButtonData(' -- src/RVTuk.Revit/Application.cs src/KKarea.Revit/Application.cs
git grep -n 'LogicalName\|"RVTuk.db"' -- src
git grep -n -E 'SetSchemaName|SchemaName\s*=|Field\s*=\s*"|FieldName\s*=|AddSimpleField|SetReadAccessLevel|SetWriteAccessLevel' -- src
```

The code agrees with this spec on every frozen identifier. Each of the ten GUIDs is on exactly one line under `src`.

Extensible Storage schemas (paths under `src/RVTuk.Revit/`). Each calls `SetVendorId("KnafoKlimor")` and sets Public read and write access:

| Schema name | GUID | Fields | `file:line` — GUID, name, vendor, access, fields |
|---|---|---|---|
| `RVTukAutoDimensionTrackingByView` | `7b3c1d92-4e58-4a0f-9c21-6d5f8e3a17b4` | `DimensionIdsByViewId` (string) | `AutoDimensions/AutoDimensionTracker.cs:26`, `:27`, `:77`, `:78-79`, `:28,80` |
| `RVTukAutoDimensionsScopeSelectionV3` | `6f2c81d4-9a7b-4e35-8c10-b4d6e27f9a51` | `CategoryMask` (int), `CheckedViewIds` (string), `DimensionTypeId` (string) | `AutoDimensions/ScopeSelectionStore.cs:26`, `:27`, `:84`, `:85-86`, `:28-30,87-89` |
| `RVTukTopoLineElevation` | `6d21f8b4-0c93-4e57-a8d6-b45f70e29c18` | `SharedElevation` (double, spec `Length`) | `TopoTools/TopoElevationStore.cs:28`, `:29`, `:74`, `:75-76`, `:30,77` |
| `RVTukTopoToolsPointLedger` | `3a6c50f1-8b47-4d2e-95c3-1f8a4b7e0d62` | `PointsByLineId` (string) | `TopoTools/TopoLedgerStore.cs:19`, `:20`, `:51`, `:52-53`, `:21,54` |
| `RVTukRoomFloorsLink` | `a9b2b113-115d-48a4-80a5-3063ddc3caca` | `RoomUniqueId` (string) | `RoomFloors/RoomFloorLinkStore.cs:19`, `:20`, `:57`, `:58-59`, `:21,60` |
| `RVTukTopoToolsParkedParameters` | `309b77b4-64cd-45c7-bd73-d6a81b1f85ae` | `Entries` (string) | `TopoTools/ParkedParameterStore.cs:16`, `:17`, `:53`, `:54-55`, `:18,56` |

The slab converter's VendorId is `OurVendorId = "KnafoKlimor"` (`TopoTools/SlabConverter.cs:24`), compared at `:115`. It skips the parked-parameter schema (`:111`, it writes a new one) and schemas it cannot read and write (`:116`).

| DockablePaneId | GUID | `file:line` |
|---|---|---|
| Auto Dimensions | `e5d47b31-2c8a-4f16-b0d9-73a5e91c46f2` | `src/RVTuk.Revit/AutoDimensions/AutoDimensionsPaneProvider.cs:18` |
| Topo Tools | `a17c46e9-5b83-4d20-8f6a-9c4e2b70d135` | `src/RVTuk.Revit/TopoTools/TopoToolsPaneProvider.cs:17` |
| Neo Properties | `88a9c4fa-9c6a-4b02-b169-701be8358090` | `src/RVTuk.Revit/NeoProperties/NeoPropertiesPaneProvider.cs:10` |
| Round Positions | `b7a1c94e-3f52-4d08-9c6b-1e84a5f2d370` | `src/RVTuk.Revit/RoundPositions/RoundPositionsPaneProvider.cs:17` |

Area Calc, in `src/RVTuk.Revit/RishuiZamin/UsageKeyScheduleBuilder.cs` (linked into KKarea):

| Shared parameter | GUID | name line | GUID line |
|---|---|---|---|
| `RZ_USAGE_TYPE` | `737fe9fb-a9ab-4e92-b951-bafbeb34eec0` | `:37` | `:50` |
| `RZ_USAGE_TYPE_OLD` | `50640c88-869d-40cc-b9c0-d82180e83a57` | `:38` | `:51` |
| `RZ_AREA` | `e59fbd50-19bb-4185-a7d6-6dc9191c7c4f` | `:39` | `:52` |
| `RZ_ASSET` | `7af015fb-bc87-438a-93c8-869578f37f67` | `:40` | `:53` |
| `RZ_BUILDING_NO` | `e8702634-4fc1-4bb0-b0e1-2d944aa3944a` | `:41` | `:54` |

- Parameter group `RVTuk` (`:132`); key schedules `New Usage` (`:43`) and `Old Usage` (`:44`).
- Embedded resources, the only ones in `src`: `RVTuk.Core.RishuiZamin.DxfTemplates.Preamble.dxf` (`src/RVTuk.Core/RVTuk.Core.csproj:80`) and `RVTuk.Core.RishuiZamin.DxfTemplates.Postamble.dxf` (`:83`).
- Retired names: `RZ_UsageType`, `f80b9154-f0e7-4f01-82d0-3ec259872e5c`, `RZ Usage Key Schedule`, `SetupRishuiZaminParamsCommand` and `RZ_AreaParams.txt` appear nowhere in `src`, `installer`, `Deploy.ps1` or `Build-Installer.ps1`.

Line styles: `_DP-Dim Outer`, `_DP-Dim Inner`, `_DP-Dim Ref` (`src/RVTuk.Revit/AutoDimensions/DimensionLineStyle.cs:15-17`); `_DP-Topo Line` (`src/RVTuk.Revit/TopoTools/TopoLineStyle.cs:12`; line 6 is its doc comment).

Ribbon button internal names (`PushButtonData`): in `src/RVTuk.Revit/Application.cs` `BrowseLibrary` `:408`, `AreaCalc` `:421`, `DwgExport` `:434`, `RoundPositions` `:447`, `AutoDimensions` `:462`, `TopoTools` `:484`, `RoomFloors` `:500`, `NeoProperties` `:517`; in `src/KKarea.Revit/Application.cs` `AreaCalc` `:99`. The panels today: `RVTuk` (`Application.cs:405`) and `KKarea` (`src/KKarea.Revit/Application.cs:96`). The `Neo Properties` panel (`:515`) and its `NeoProperties` button exist only when `RegisterNeoProperties` is on; it is off (`:105`), so `CreateRibbon` returns at `:513` before creating either.

Database file name `RVTuk.db`: `src/RVTuk.Core/Shared/Config/AppConfig.cs:151` (also read by `src/RVTuk.Core/FamilyBrowser/Config/LibraryFolderValidator.cs:32-33`).

Manifest identity in the scripts: ClientId `D71D7480-4A21-474E-A47E-3E8DF8C1BDA5` for RVTuk (`Deploy.ps1:39,43`, `Build-Installer.ps1:32,36`), `9C97B9F2-60F9-432D-92A4-5EC2A0FDAFFC` for KKarea (`Deploy.ps1:35,47,51`, `Build-Installer.ps1:28,40,44`), VendorId `KnafoKlimor` (`Deploy.ps1:26`, `Build-Installer.ps1:19`). KKompare's `496F971A-DD0B-44CC-9430-D249796BBB9F` sits in its own repo (`Deploy.ps1:22`, `Build-Installer.ps1:18`).

### Views that merge the theme

`git grep -l "RVTuk.UI;component" -- src` — 11 files, all under `src/RVTuk.UI/`:

- `AutoDimensions/Views/AutoDimensionsPaneView.xaml`
- `DwgExporter/Views/DwgExportWindow.xaml`
- `FamilyBrowser/Views/FamilyBrowserWindow.xaml`
- `FamilyBrowser/Views/ImageCropWindow.xaml`
- `FamilyBrowser/Views/IndexProgressWindow.xaml`
- `FamilyBrowser/Views/InstructionsEditorWindow.xaml`
- `FamilyBrowser/Views/SettingsWindow.xaml`
- `NeoProperties/Views/NeoPropertiesView.xaml`
- `RishuiZamin/Views/RishuiZaminWindow.xaml`
- `RoundPositions/Views/RoundPositionsPaneView.xaml`
- `TopoTools/Views/TopoToolsPaneView.xaml`

### Files that use `Application.X`

`git grep -c -E "(^|[^.\w])Application\.[A-Z]" -- src/RVTuk.Revit src/KKarea.Revit` reports matching *lines*: 63 in 7 files under `src/RVTuk.Revit` plus 17 in KKarea's `RishuiZaminCommand.cs`. That is not the number the claim in "Namespaces do not change" needs: `-c` counts lines, not uses (some lines hold two), and git's `-E` reads `\w` inside brackets as two literal characters, so `UIApplication.` in a comment matches too. Counting uses instead, `git grep -P -o '(?<![.\w])Application\.[A-Z]' -- src/RVTuk.Revit | Group-Object { $_.Split(':')[0] } -NoElement` gives 69 in 7 files; dropping `FamilyBrowser/ExternalEvents/OpenFamilyEditorEventHandler.cs`, whose only hit is a comment (`:31`), leaves:

| File (under `src/RVTuk.Revit/`) | Uses |
|---|---|
| `AutoDimensions/AutoDimensionsPaneCommand.cs` | 2 |
| `FamilyBrowser/Commands/BrowseLibraryCommand.cs` | 42 |
| `FamilyBrowser/Commands/IndexLibraryCommand.cs` | 3 |
| `RishuiZamin/Commands/RishuiZaminCommand.cs` | 18 |
| `RoundPositions/Commands/RoundPositionsCommand.cs` | 1 |
| `TopoTools/TopoToolsPaneCommand.cs` | 2 |
| **Total** | **68 in 6 files** |

Left out: `FamilyBrowser/ExternalEvents/OpenFamilyEditorEventHandler.cs:31,33` (comments naming Revit's own `Application.OpenDocumentFile` and `UIApplication`), and `src/KKarea.Revit/Commands/RishuiZaminCommand.cs` (18 uses that bind to `KKarea.Revit.Application`; the file goes with KKarea). The same count at `c6d4990` gives 67 in the first five files. None of the six files has a `using System.Windows;` or `using Autodesk.Revit.ApplicationServices;`, and nothing names `RVTuk.Revit.Application` qualified.

### Settings keys

`git grep -n "{ get; set; }" -- src/RVTuk.Core/Shared/Config/AppConfig.cs` — 22 keys on `AppConfig` (line numbers in that file):

- **Family Browser** (3): `LibraryFolderPath` `:22`, `IgnoredSubfolders` `:158`, `IgnoredFilePatterns` `:167`
- **Area Calc** (2): `AreaCalcOutputFolder` `:26`, `AreaCalcMarkerForm` `:30`
- **DWG Export** (15): `DwgExportFolder` `:37`, `DwgExportPdfSetupName` `:38`, `DwgExportDwgSetupName` `:39`, `DwgExportSheetSetName` `:40`, `DwgExportUseCurrentWindow` `:41`, `DwgExportViewNamingSetupName` `:46`, `DwgExportSeparatePdfFolder` `:49`, `DwgExportCopyMissingSetups` `:53`, `DwgExportCreateZip` `:57`, `DwgExportZipMode` `:60`, `DwgExportDwgOff` `:72`, `DwgExportPdfOn` `:73`, `DwgExportFolders` `:83`, `DwgExportPdfFolder` `:92`, `DwgExportPdfFolders` `:94`; plus `ModelKey` `:10` and `Folder` `:11` on `DwgExportFolderEntry`
- **Topo** (1): `TopoPointSpacingCentimetres` `:67`
- **Tools** — Round Positions (1): `RoundPositionsWholeCentimetresOnly` `:79`

Not keys: `DatabasePath` `:151` (derived, get-only) and the constants `DbFolderName`, `LegacyDbFolderName`, `DwgExportFolderCap`.

`git grep -l "ConfigManager\.\(Load\|Save\)" -- src` — 8 files: the 7 of master before phase 0 plus `Application.cs` for Round Positions:

- `src/RVTuk.Revit/Application.cs:349,352,354`
- `src/RVTuk.Revit/FamilyBrowser/Commands/BrowseLibraryCommand.cs:41,47,123,136`
- `src/RVTuk.UI/DwgExporter/ViewModels/DwgExportViewModel.cs:87,412,428`
- `src/RVTuk.UI/FamilyBrowser/ViewModels/ConfigViewModel.cs:130,132`
- `src/RVTuk.UI/FamilyBrowser/ViewModels/SettingsViewModel.cs:46,88,90`
- `src/RVTuk.UI/FamilyBrowser/Views/FamilyBrowserWindow.xaml.cs:71`
- `src/RVTuk.UI/RishuiZamin/ViewModels/RishuiZaminViewModel.cs:60,113,115,231,233`
- `src/RVTuk.UI/TopoTools/ViewModels/TopoToolsPaneViewModel.cs:51,328,330`

### Tests and builds

| Measure | Command | Value |
|---|---|---|
| Test methods | `(git grep -h -E '^\s*\[(Fact\|Theory)' -- tests \| Measure-Object).Count` | **459** (Core 436, UI 23) |
| Core tests passed | `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj` | **569** (net8) |
| UI tests passed, net48 | `dotnet test tests\RVTuk.UI.Tests\RVTuk.UI.Tests.csproj -c Release2024` | **29** |
| UI tests passed, net8 | `dotnet test tests\RVTuk.UI.Tests\RVTuk.UI.Tests.csproj -c Release2025` | **33** |
| Test files | `git ls-files tests \| Select-String 'Tests\.cs$'` | 59 (Core 55, UI 4) |
| Builds | `dotnet build RVTuk.sln -c Release2023` … `Release2027` | all 0 errors (warnings 5, 9, 8, 4, 21) |

Test methods: 343 before phase 0 (`b3a0d51`) + 29 from phase 0's own tests (372 at `fadba36`) + 34 from `topo-convert` (406 at `4f13767`) + 49 from `round-positions` (455 at `5417840`) + 4 from the final review's fixes (459 at `642d43a`). The UI tests run 4 more cases on net8 than on net48: `InstructionsEditorViewModelTests` (4 methods) is `#if !NETFRAMEWORK` (see "Tests on net48"). Later phases compare against these numbers.

### Installed file list per Revit year

```powershell
.\Build-Installer.ps1
$out = "$env:TEMP\rvtuk-phase0-payload"
dist\RVTukSetup.exe --all --no-pause --target $out
Get-ChildItem $out -Recurse -File | ForEach-Object { $_.FullName.Substring($out.Length + 1) } | Sort-Object
```

The setup exe reported every year installed (exit code 0). No `RZ_AreaParams.txt` in any year. Each manifest carries the ClientId listed under "Frozen identifiers", VendorId `KnafoKlimor` and the entry class `RVTuk.Revit.Application` (2024, 2025) or `KKarea.Revit.Application` (2023, 2026, 2027). 2027 has no `System.Drawing.Common.dll`, and its `KKarea.Revit.deps.json` does not list the package (2025 and 2026 list 8.0.34).

```
2023\KKarea.addin
2023\KKarea\e_sqlite3.dll
2023\KKarea\KKarea.Revit.dll
2023\KKarea\KKarea.Revit.pdb
2023\KKarea\Microsoft.Data.Sqlite.dll
2023\KKarea\OpenMcdf.dll
2023\KKarea\RVTuk.Core.dll
2023\KKarea\RVTuk.Core.pdb
2023\KKarea\RVTuk.UI.dll
2023\KKarea\RVTuk.UI.pdb
2023\KKarea\SQLitePCLRaw.batteries_v2.dll
2023\KKarea\SQLitePCLRaw.core.dll
2023\KKarea\SQLitePCLRaw.provider.dynamic_cdecl.dll
```

```
2024\RVTuk.addin
2024\RVTuk\e_sqlite3.dll
2024\RVTuk\Microsoft.Data.Sqlite.dll
2024\RVTuk\OpenMcdf.dll
2024\RVTuk\RVTuk.Core.dll
2024\RVTuk\RVTuk.Core.pdb
2024\RVTuk\RVTuk.Revit.dll
2024\RVTuk\RVTuk.Revit.pdb
2024\RVTuk\RVTuk.UI.dll
2024\RVTuk\RVTuk.UI.pdb
2024\RVTuk\SQLitePCLRaw.batteries_v2.dll
2024\RVTuk\SQLitePCLRaw.core.dll
2024\RVTuk\SQLitePCLRaw.provider.dynamic_cdecl.dll
```

```
2025\RVTuk.addin
2025\RVTuk\e_sqlite3.dll
2025\RVTuk\Microsoft.Data.Sqlite.dll
2025\RVTuk\OpenMcdf.dll
2025\RVTuk\RVTuk.Core.dll
2025\RVTuk\RVTuk.Core.pdb
2025\RVTuk\RVTuk.Revit.deps.json
2025\RVTuk\RVTuk.Revit.dll
2025\RVTuk\RVTuk.Revit.pdb
2025\RVTuk\RVTuk.UI.dll
2025\RVTuk\RVTuk.UI.pdb
2025\RVTuk\SQLitePCLRaw.batteries_v2.dll
2025\RVTuk\SQLitePCLRaw.core.dll
2025\RVTuk\SQLitePCLRaw.provider.e_sqlite3.dll
2025\RVTuk\System.Drawing.Common.dll
```

```
2026\KKarea.addin
2026\KKarea\e_sqlite3.dll
2026\KKarea\KKarea.Revit.deps.json
2026\KKarea\KKarea.Revit.dll
2026\KKarea\KKarea.Revit.pdb
2026\KKarea\Microsoft.Data.Sqlite.dll
2026\KKarea\OpenMcdf.dll
2026\KKarea\RVTuk.Core.dll
2026\KKarea\RVTuk.Core.pdb
2026\KKarea\RVTuk.UI.dll
2026\KKarea\RVTuk.UI.pdb
2026\KKarea\SQLitePCLRaw.batteries_v2.dll
2026\KKarea\SQLitePCLRaw.core.dll
2026\KKarea\SQLitePCLRaw.provider.e_sqlite3.dll
2026\KKarea\System.Drawing.Common.dll
```

```
2027\KKarea.addin
2027\KKarea\e_sqlite3.dll
2027\KKarea\KKarea.Revit.deps.json
2027\KKarea\KKarea.Revit.dll
2027\KKarea\KKarea.Revit.pdb
2027\KKarea\Microsoft.Data.Sqlite.dll
2027\KKarea\OpenMcdf.dll
2027\KKarea\RVTuk.Core.dll
2027\KKarea\RVTuk.Core.pdb
2027\KKarea\RVTuk.UI.dll
2027\KKarea\RVTuk.UI.pdb
2027\KKarea\SQLitePCLRaw.batteries_v2.dll
2027\KKarea\SQLitePCLRaw.core.dll
2027\KKarea\SQLitePCLRaw.provider.e_sqlite3.dll
```

### Other findings

- **Instruction fixtures.** `saved-net48.txt` and `saved-net8.txt` (`tests/RVTuk.UI.Tests/FamilyBrowser/Fixtures/`, the fixed `SerializeDocument` output on each runtime) are byte-identical: 465 bytes each, SHA-256 `A0FC128C3EB16F0346DA838FCF1B7D2B6E33A05A5DFAD37D17F4BFA65CE676CB`. Both declare only `clr-namespace:RVTuk.UI.Shared.Controls;assembly=RVTuk.UI`. The two legacy fixtures declare `clr-namespace:RVTuk.UI.Controls;assembly=RVTuk.UI`; `raw-net8.txt` declares `assembly=System.Private.CoreLib`.
- **Bug 1 in Revit.** Checked in Revit 2024 on 2026-11-15 after deploying the phase 0 build: the two image instructions open with their text and image. The "before" look in Revit was not recorded; the UI tests reproduce both bugs (see "Family Browser specifics").
- **SQLite in a net48 test host.** Probed again on 2026-11-15 with a scratch net48 xunit project on Microsoft.Data.Sqlite 8.0.29: `SqliteConnection`'s type initializer throws; the inner exception is a `FileLoadException` for `System.Memory` (versions 4.0.5.0 and 4.0.1.1), thrown in `SQLitePCL.Batteries_V2.DoDynamic_cdecl`. `e_sqlite3.dll` sits only under `runtimes\<rid>\native` (also in `tests\RVTuk.UI.Tests\bin\Release2024\net48`), but copying it flat changes nothing. A net48 console exe on the same package gets the DLL flat plus an `.exe.config` and opens SQLite 3.41.2.
- **`DatabasePath` in `config.json`.** `AppConfig.DatabasePath` (`src/RVTuk.Core/Shared/Config/AppConfig.cs:151`) is a get-only property. `System.Text.Json` (net8/net10, `ConfigManager.cs:51`) writes it into `config.json`; `DataContractJsonSerializer` (net48, `:48`) does not. Both ignore it on load (scratch probe on net48 and net8, 2026-11-15). Whoever builds the golden per-add-in settings files needs to know.
- **Round Positions as merged** registers one category pass, Walls (`src/RVTuk.Revit/RoundPositions/PassRegistry.cs:17-18`). The other categories in its design are not built.
- **`RelayCommand<T>` has a second user.** Round Positions (`src/RVTuk.UI/RoundPositions/ViewModels/RoundPositionsPaneViewModel.cs:48-54`, from `round-positions`) uses it besides Family Browser. By the rule "a file is shared iff two add-ins use it" it stays in `Shared/UI`, to be linked by Family Browser and Tools. Settled on 2026-11-15, after this record was taken: "Shared source" had moved it to Family Browser and was corrected, and with it the number of files that leave `Shared/` (four, not five). The other four single-tool files still have one add-in each (`MarkdownConverter`, `RichTextBoxHelper`, `PathUtil`: Family Browser; `CountToVisibilityConverter`: Area Calc).
- **Size and users of the shared code.** Measured at `df1e428` on 2026-11-15 with `wc -l`: 739 lines in the nine files of "Shared source" that exist today — `XyPoint.cs` 5, `DarkTheme.xaml` 450, `ViewModelBase.cs` 20, `RelayCommand.cs` 26, `RelayCommandT.cs` 28, `LineStyleCreator.cs` 79, `DefaultTypes.cs` 55, `DependentsCounter.cs` 50, `LoopOrder.cs` 26. Not counted: the two `IsExternalInit` polyfills, and the settings store, `DarkThemeDictionary` and the host helpers, which are not written yet. Each of the nine has users in at least two add-ins: `XyPoint` and `LineStyleCreator` in Auto Dimensions (Tools) and Topo; `DefaultTypes`, `DependentsCounter` and `LoopOrder` in Room Floor (Tools) and Topo; `RelayCommand<T>` in Family Browser and Round Positions (Tools); the theme, `ViewModelBase` and `RelayCommand` in all five.

## Phase 2 record (build half)

Taken on 2026-11-16 at `080ec6e`. Plan: `docs/toolkit/plans/2026-11-16-addin-split-phase-2-build.md`; commits `01e9d42` to `080ec6e` on `master`. Only what was measured or seen is listed.

- **Tests.** `build\Test-All.ps1` at `080ec6e`: Core 570 and UI 34 passed, 0 failed, on each of net48 (`Release2024`), net8.0 (`Release2025`) and net10.0 (`Release2027`). Before the phase: Core 569 on net8 only, UI 29 on net48 and 33 on net8.
- **The matrix ships the same set of files.** The file lists of the five deployed outputs were recorded before (`8a14f4b`) and after (`8dbbdb8`) and were identical: 19, 19, 33, 33 and 32 files for 2023–2027 (2026 was still KKarea then). All five `dotnet build RVTuk.sln -c Release20xx` exit 0; `-c Debug` on a project fails with the matrix error; after a `Release2026` build, `RVTuk.Revit -c Release2025 --no-restore` builds (it failed before).
- **The serializer comment is settled.** `Config_ReadsAcrossBothSerializers` passed on net48: `DataContractJsonSerializer` runs the constructor of a plain class on .NET Framework too. The two comments that said otherwise were corrected (`AppConfig.cs`, `AppConfigTests.cs`); the "Settings" contract stands.
- **SQLite in net48 tests.** The cause was xunit's shadow copy (the shadow folder has no `e_sqlite3.dll`), not `System.Memory`. Both test projects carry `xunit.runner.json` with `{ "shadowCopy": false }`; no test is net8-only any more.
- **The 2026 compile fix** was one code line: `SlabShape.cs` uses `AddPoint` under `#if REVIT2026 || REVIT2027`.
- **The safety nets fail when they should.** Frozen-identifier test: a GUID edit gave "expected 1, found 0", a copied pane provider "expected 1, found 2". View smoke test: 11 failure lines before the views were switched, none after. `Test-All.ps1`: with one test project's `Build.0` line removed it throws "Not every test project ran".
- **Revit 2026 gets the full add-in (yours, 2026-11-16).** `Deploy.ps1` and `Build-Installer.ps1` map 2026 to `RVTuk`; `KKarea.Revit` builds for 2023 only. `Build-Installer.ps1` ran against the new layout, and the setup exe with `--all --target <scratch folder>` installed KKarea for 2023 and RVTuk for 2024–2027; the 2025 and 2026 folders held the same file names.
- **In Revit (Daniel, 2026-11-16, after `.\Deploy.ps1` at `080ec6e`): "All passed"** for the plan's Task 7 checklist — Revit 2024 (net48): start, the three panes, Family Browser, Edit Info and image crop, DWG Export, Area Calc, all dark as before; Revit 2026 (the net8 build) and Revit 2027 (the net10 build): start, the three panes and the Family Browser dark, and an image instruction saved in 2024 opens, saves and opens again in 2024. This is the theme class's first run inside Revit (left open by S2) and the phase table's "monolith loads in Revit 2026.5 … and back" item. Reported by Daniel; not otherwise recorded.
- **Not checked:** the tools in use on Revit 2026 and 2027, including Topo's `AddPoint` on edges (phase 6); Revit 2025 (not installed on the dev PC); anything of the install engine (second phase 2 plan), so `Deploy.ps1` still deploys by itself.
- **Left as they are (small):** `Test-All.ps1` leaves the shell at the repository root and reads English result lines only; the `xmlns:theme` attribute shares a line with `xmlns:x` in the 11 views; restore reports NuGet advisories for OpenMcdf 3.1.2 and SQLitePCLRaw.lib.e_sqlite3 2.1.6 (packages unchanged here — `docs/BACKLOG.md`).

## Phase 1 record

Taken on 2026-11-15 at about 13:15, at `df83f7c`. Read-only: no spike code was written and Revit was not started for it. The answers come from Autodesk's documentation, from what Revit had already written on the dev PC (journals, registry, layout file), from the code, and from probes outside Revit. "Seen in Revit" is said only where a journal shows it. Each spike ends with what is still open and, where there is one, the step that will show it. The dev PC changed later the same day; the last section says how.

### S1 — shared tab, manifest order, no resolve hook

- **Manifest order: answered.** Autodesk's Developer's Guide, "Add-in Registration" (2026 edition): "All of the files in both the user-specific location and the all users location are considered together and loaded in alphabetical order." and "Within each manifest file, the external commands and external applications are loaded in the order in which they are listed." The journals agree. `Select-String -Path <newest journal> -Pattern 'Starting External Application: ([^,]+),'` lists the applications in start order. Revit 2024 (`journal.0953.txt`), after Revit's own add-ins: … BIMobject Revit Add-In → ProSheets → DowngRev.Runner → Views For A360 → FormIt Conversion → KKompare → PyRevitLoader → RVTuk → Magic Join. Their manifests are `bimobjectapp`, `DiRoots.ProSheets`, `DowngRev.Runner` (per-user folder), `ExportViewSelectorApp`, `FormItConverter`, `KKompare`, `pyRevit` and `RVTuk`; Magic Join comes from an ApplicationPlugins bundle and starts last. Revit 2026.5 (`journal.0016.txt`): … BIMobject Revit Add-In → ProSheets → KKcheck (per-user folder) → KKompare → PyRevitLoader. So it is one sort by manifest file name, case-insensitive, with the two folders merged.
- **Shared tab: documented, not yet seen in Revit.** API reference, `UIControlledApplication.CreateRibbonTab`: `ArgumentException` — "tabName or panelName is Empty or the tab name duplicates the name of another tab in the Revit UI."; `InvalidOperationException` — "Too many custom tabs have been created in this session. (Maximum is 20)."; remarks: "If multiple tabs are added, they will be shown in the order added." Both are Revit's classes (`Autodesk.Revit.Exceptions`), not the .NET ones. `CreateRibbonPanel(tabName, panelName)`: "This method will create a custom panel appending to the specified tab." (at most 100 panels per tab). Neither host creates a tab today: both call the one-argument `CreateRibbonPanel` (`src/RVTuk.Revit/Application.cs:405`, `src/KKarea.Revit/Application.cs:96`), which puts the panel on Add-Ins. Open: two add-ins on one tab and their panel order → phase 3, the Tools and DWG Export steps.
- **No resolve hook: probed outside Revit only.** The design review's probes of 2026-11-12: an assembly loaded with `LoadFrom` gets its siblings from the same folder without any handler (.NET Framework 4.8 and .NET 10), and XAML finds an already loaded assembly by name. Today's hook (`src/RVTuk.Revit/Application.cs:128,390-400`; KKarea and KKompare carry the same code) answers every requester for any simple name that exists in its own folder. Open: a run in Revit without the hook, on 2024 and on 2026.5 → each phase 3 step.

### S2 — the theme class

- **Answered outside Revit.** A probe (2026-11-15; .NET Framework 4.8 and .NET 8.0.34; no `System.Windows.Application` object, as in Revit) linked `DarkTheme.xaml` with `Link="Shared\Themes\DarkTheme.xaml"` and the class below into two assemblies, `Probe.Alpha.UI` and `Probe.Beta.UI`, and constructed a UserControl one folder deep in the first and a Window two folders deep in the second. In both, the merged dictionary's `Source` names the view's own assembly; the brushes, a keyed style and the implicit `Button` style resolve; and nothing changes when the monolith's `RVTuk.UI.dll` is loaded into the same process. The linked theme's resource id is `shared/themes/darktheme.baml` in both assemblies, on both frameworks. `DarkTheme.xaml` itself names no assembly, namespace or pack URI.

  ```csharp
  public class DarkThemeDictionary : ResourceDictionary
  {
      public DarkThemeDictionary()
      {
          var name = typeof(DarkThemeDictionary).Assembly.GetName().Name;
          Source = new Uri("/" + name + ";component/Shared/Themes/DarkTheme.xaml", UriKind.Relative);
      }
  }
  ```

- **What a view that was not switched does.** The same probe compiled a view that keeps today's `/RVTuk.UI;component/…` line into `Probe.Alpha.UI`. Without `RVTuk.UI` loaded, constructing it throws `XamlParseException` (inner `FileNotFoundException` for `RVTuk.UI`); with the monolith's `RVTuk.UI` loaded it takes the monolith's theme and nothing shows. So during phase 3 such a view works until the monolith is gone; the staging check's ban on `RVTuk.UI;component` in source is what catches it.
- Open: a run inside Revit → phase 2 (the 11 views are switched on the monolith; Revit 2024 must look the same, and the panes in Revit 2026.5 show it on .NET 10). Two differently named assemblies inside Revit are first seen at phase 3's Tools step.

### S3 — the monolith beside a split add-in, pane layout

- **A pane keeps its place under a new ClientId: expected from the layout file, not yet seen in Revit.** Revit saves the layout in `%APPDATA%\Autodesk\Revit\Autodesk Revit <year>\RevitUILayout.xml`. Each pane is one `<LayoutAnchorable Title="…" ContentId="<pane GUID>" …>` element. The 2024 file holds, among others, Auto Dimensions (`E5D47B31-…`) and Topo Tools (`A17C46E9-…`), and nowhere a ClientId, an add-in name or a DLL path. The pane GUIDs are frozen, so the saved place should follow a pane to its new add-in → phase 3, the Auto Dimensions step.
- **A pane whose add-in is not installed: not answered.** No source describes it, and no layout file on the dev PC holds such a pane. It cannot change the design (untick = remove is the requirement), so it is watched in phase 3: remove Tools after its panes were saved, then start Revit. A pass is no error dialog and no empty pane frame; a fail does not change the design and goes into "Known risks" and the rollout note.
- **The monolith beside a split add-in: not run.** From the code: the monolith's resolve hook answers every requester (S1), and neither its `UnhandledException` logger (`src/RVTuk.Revit/Application.cs:132`) nor the Family Browser dispatcher handler asks whose exception it is. "Host helpers" replaces all three in the split add-ins, but the remaining monolith keeps them until its last step, so phase 3's manual checks run with the Family Browser window closed. Phase 3's first step is this run.

### S4 — unsigned add-in prompts

- **Where the choice is kept.** `reg query "HKCU\SOFTWARE\Autodesk\Revit\Autodesk Revit 2024\CodeSigning"` (the same shape for 2023 and 2026): one `REG_DWORD` value of 1 per add-in, named by its ClientId in lower case — `d71d7480-…` (RVTuk) under 2023 and 2024, `9c97b9f2-…` (KKarea) under 2023, `496f971a-…` (KKompare) under 2024 and 2026. No path, hash or date. So the stored choice is per ClientId, per Revit year, per Windows user; a user sees at least one prompt per add-in and year, and more after updates (next point).
- **An updated or moved add-in may prompt again.** The ten Revit 2024 sessions before the redeploy of 2026-11-15 (`journal.0943`–`0952`) show no prompt (`TaskDialog_Security_Unsigned_File_Loading`). `journal.0953`, the first session after it, shows one, and `journal.0954`, the first session after a second redeploy that afternoon, shows one again, although RVTuk's ClientId was stored all along. The journal names neither the add-in nor the answer, so the cause is not proven. Revit's help ("Security: Unsigned File or Add-In") ties Always Load to more than the id: "when you load this file (from the same location and with the same add-in ID), it will be loaded without displaying this warning" — so the move into new folders may prompt once as well.
- Not checked, and no step is planned: whether "Do Not Load" is stored, and how a user undoes it. The rollout note in "Known risks" covers the prompts.

### S5 — OpenMcdf and System.Memory

- **Decided: a Family Browser backlog item** (`docs/tools/family-browser/backlog.md`), confirmed or closed on phase 4's clean PC.
- The deployed `OpenMcdf.dll` 3.1.2 references `System.Memory 4.0.2.0` (`[Reflection.Assembly]::ReflectionOnlyLoadFrom(…).GetReferencedAssemblies()` in Windows PowerShell). Revit 2024 brings `System.Memory` 4.0.1.1, its `Revit.exe.config` has no redirect for it, and `Deploy.ps1:185` strips the add-in's own copy. On the dev PC two add-ins that start before RVTuk bring newer copies (ProSheets 4.0.2.0, pyRevit 4.0.5.0), and the Revit 2024 journal notes "Addin's module OpenMcdf of version 3.1.2.0 conflicts with same preloaded module of version 3.1.4.0" (pyRevit's). So the pin comment at `src/RVTuk.Core/RVTuk.Core.csproj:70` ("to match the version preloaded by other Revit addins") does not hold today.
- Expected in Revit 2024 on a PC without those add-ins, not seen yet (the fault is .NET Framework only, and thumbnails are made when families are scanned): nothing provides `System.Memory 4.0.2.0`, OpenMcdf fails at first use, and because `ThumbnailExtractor` returns null on every exception, families show no thumbnail and nothing is reported.

### S6 — the build matrix

- **Mechanics: probed by evaluation only** (design review, 2026-11-12; nothing was built). With `MSBuildProjectExtensionsPath` set to `obj\Release2025\`, the NuGet assets file is `obj\Release2025\project.assets.json` and no generated file of another configuration is compiled; with `BaseIntermediateOutputPath` moved instead, they are. Open: a real restore and build of all five configurations, the solution level, the error target → phase 2's first task.
- **Packages.** The NuGet cache on the dev PC holds the Revit API package for every year: 2023.1.90, 2024.3.60, 2025.4.60, 2026.4.10 and 2027.3.0.
- **API differences outside Area Calc: one file, by text search.** Of the members Autodesk lists as removed in 2026 and 2027, a search of `src` finds `DrawSplitLine` (`src/RVTuk.Revit/TopoTools/SlabShape.cs:123`, in the branch every year but 2025 compiles) and `DrawPoint` (`:150`), both removed in 2026. The two `ElementId` integer members appear only in Area Calc's shared sources, inside `#if REVIT2023`. A search is not a compile: building `RVTuk.Revit` against the 2026 and 2027 API belongs to phase 2, where the compile fixes are made. (Later the same day PR #14 built it for 2027 and fixed this file for that year; the 2026 build is still phase 2's.)

### The dev PC, corrected

- **Revit 2027 is installed** (2026-11-15, `Revit.exe` 27.3.0.28; first started that afternoon). Its machine-wide add-ins are in `C:\Program Files\Autodesk\Revit\Addins\2027`. Seen in Revit 2027 (`journal.0003.txt`): "Add-in manifest file from: C:\ProgramData\Autodesk\Revit\Addins\2027\RevitMCP.addin, won't be loaded. All-users Add-in manifest files must be installed to: C:\Program Files\Autodesk\Revit\Addins\2027". Autodesk's developer blog on the 2027 SDK (2026-04-10): "For improved security, Revit no longer looks for machine-wide add-ins under ProgramData."; the new location is `C:\Program Files\Autodesk\Revit\Addins\20XX`, and "Per-user add-in locations are still supported". At `df83f7c`, `Deploy.ps1:54` and `installer/RVTuk.Setup/Program.cs:34` still use ProgramData for every year (fixed later that day — last point).
- **Revit 2026.5 runs on .NET 10.** `C:\Program Files\Autodesk\Revit 2026\RevitAPI.runtimeconfig.json` says `net10.0` (`Revit.exe` 26.5.10.38), the same as 2027. The journals show the change: Revit 2026.4 and 2026.4.1 sessions had `System.Runtime` 8.0.0.0 loaded, 2026.5 and 2026.5.1 sessions 10.0.0.0. KKompare's net8 build started in 2026.5: `journal.0016.txt` notes "Addin's module System.Runtime of version 8.0.0.0 conflicts with same preloaded module of version 10.0.0.0" and lists `KKompare.addin` with `AddInLoadFailureMessage: NoError`. So `Release2026` stays net8.0-windows — it must also load in the Revit 2026 versions that still run .NET 8 — but nothing on the dev PC runs .NET 8.
- **`InstallationLocation`** is a value of `HKLM\SOFTWARE\Autodesk\Revit\<year>\REVIT-05:0809`, not of the year key (checked for 2024 and 2027).
- **Later the same day** an uncommitted build from another working copy (branch `family-browser-acc`, which does not contain phase 0) was deployed and loaded in Revit 2026.5.1 and 2027: the whole toolkit for 2024–2027, with the 2027 build under Program Files. A version of that fix built on phase 0 reached `master` the same day as PR #14 (`f8cd1f0`): the 2027 build installs under Program Files, and Revit 2027 gets the full RVTuk add-in while KKarea stays for 2023 and 2026. Checked in Revit 2027 that day: the add-in loads and every ribbon button shows; the tools themselves are not yet checked there. The sections above this record were brought up to date; the record keeps the 13:15 state.

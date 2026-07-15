# Area Submission Window Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `AreaSubmissionWindow`'s toolbar-driven pane switching with a Settings/Areas `TabControl`, group the Settings fields with inline red-highlight validation, relocate "Setup Usage Keys" into Settings, and re-theme the whole add-in from the current VSCode-style orange/black palette to a Revit-native charcoal/blue palette.

**Architecture:** Presentation-only change across three files in `RVTuk.UI` (WPF/MVVM, no Revit dependency). `DarkTheme.xaml` brush values change globally; `AreaSubmissionViewModel` gains a validation surface (per-field invalid flags + a settings-wide flag) and a flagged/total area count; `AreaSubmissionWindow.xaml` is restructured around a `TabControl`. No Core or Revit project changes — the underlying `AreaRecord`/`AreaValidator`/DXF export pipeline is untouched.

**Tech Stack:** WPF (net48 + net8.0-windows dual target), MVVM with hand-rolled `RelayCommand`/`ViewModelBase` (no external MVVM framework), `dotnet build` via the `Release2024`/`Release2025` solution configurations.

## Global Constraints

- Only touch `RVTuk.UI` (`src/LibraryBrowser/RVTuk.UI`). Do not add a Revit or WPF dependency to `RVTuk.Core`, and do not add a Revit API reference to `RVTuk.UI` — see `RVTuk/CLAUDE.md` Architecture section.
- No change to `AreaRecord`, `AreaValidator`, `DxfWriter`, `DatWriter`, `AreaSubmissionConfig`'s fields/meaning, or any Revit-side extraction/selection/export code (`RVTuk.Revit`) — this is a presentation-only redesign per the design spec's Non-goals.
- Follow existing patterns: `ViewModelBase.SetProperty`/`OnPropertyChanged` for notification, `RelayCommand` for commands, `StaticResource Brush.*` (never hardcoded hex) in XAML.
- There is no WPF UI test project in this repo (only `RVTuk.Core.Tests`, which covers `RVTuk.Core` only) — verification for every task in this plan is `dotnet build` plus, at the end, a manual in-Revit pass. Do not invent a new UI test framework.
- Build verification command (per task): `dotnet build src\LibraryBrowser\RVTuk.UI\RVTuk.UI.csproj -c Release2024` from the `RVTuk` folder. Run from Windows PowerShell/cmd (not the sandboxed bash), since this is a WPF/net-framework build.
- Design reference: [`docs/superpowers/specs/2026-07-05-area-submission-ui-redesign-design.md`](../specs/2026-07-05-area-submission-ui-redesign-design.md).

---

## Task 1: Recolor the shared theme palette

**Files:**
- Modify: `src/LibraryBrowser/RVTuk.UI/Themes/DarkTheme.xaml:4-20`

**Interfaces:**
- Consumes: nothing new.
- Produces: updated `Brush.*` `StaticResource` values (`Brush.Bg`, `Brush.Panel`, `Brush.Control`, `Brush.Input`, `Brush.Hover`, `Brush.Text`, `Brush.TextMuted`, `Brush.TextDisabled`, `Brush.Border`, `Brush.BorderFocus`, `Brush.Accent`, `Brush.AccentDark`, `Brush.Warning`, `Brush.Splitter`) and a new `Brush.ErrorFill` key — every later task in this plan (and every other existing window, since they all merge this same dictionary) picks these up automatically because every existing `Style` already references brushes by key rather than a literal color.

This is a values-only edit — no `Style`/`ControlTemplate` changes, so nothing can regress structurally.

- [ ] **Step 1: Replace the Brushes block**

In `src/LibraryBrowser/RVTuk.UI/Themes/DarkTheme.xaml`, replace:

```xml
    <!-- ── Brushes ────────────────────────────────────────────────────────── -->
    <SolidColorBrush x:Key="Brush.Bg"           Color="#1E1E1E"/>
    <SolidColorBrush x:Key="Brush.Panel"        Color="#252526"/>
    <SolidColorBrush x:Key="Brush.Control"      Color="#2D2D2D"/>
    <SolidColorBrush x:Key="Brush.Input"        Color="#3C3C3C"/>
    <SolidColorBrush x:Key="Brush.Hover"        Color="#3E3E42"/>
    <SolidColorBrush x:Key="Brush.Selection"    Color="#264F78"/>
    <SolidColorBrush x:Key="Brush.Text"         Color="#D4D4D4"/>
    <SolidColorBrush x:Key="Brush.TextMuted"    Color="#858585"/>
    <SolidColorBrush x:Key="Brush.TextDisabled" Color="#656565"/>
    <SolidColorBrush x:Key="Brush.Border"       Color="#3F3F46"/>
    <SolidColorBrush x:Key="Brush.BorderFocus"  Color="#007ACC"/>
    <SolidColorBrush x:Key="Brush.Accent"       Color="#FF8C00"/>
    <SolidColorBrush x:Key="Brush.AccentDark"   Color="#CC7000"/>
    <SolidColorBrush x:Key="Brush.Success"      Color="#4EC94E"/>
    <SolidColorBrush x:Key="Brush.Warning"      Color="#FF6B35"/>
    <SolidColorBrush x:Key="Brush.Splitter"     Color="#3F3F46"/>
```

with:

```xml
    <!-- ── Brushes (Revit-native dark palette) ───────────────────────────── -->
    <SolidColorBrush x:Key="Brush.Bg"           Color="#2B2B2B"/>
    <SolidColorBrush x:Key="Brush.Panel"        Color="#333333"/>
    <SolidColorBrush x:Key="Brush.Control"      Color="#3C3C3C"/>
    <SolidColorBrush x:Key="Brush.Input"        Color="#232323"/>
    <SolidColorBrush x:Key="Brush.Hover"        Color="#454545"/>
    <SolidColorBrush x:Key="Brush.Selection"    Color="#264F78"/>
    <SolidColorBrush x:Key="Brush.Text"         Color="#E3E3E3"/>
    <SolidColorBrush x:Key="Brush.TextMuted"    Color="#9A9A9A"/>
    <SolidColorBrush x:Key="Brush.TextDisabled" Color="#6E6E6E"/>
    <SolidColorBrush x:Key="Brush.Border"       Color="#4A4A4A"/>
    <SolidColorBrush x:Key="Brush.BorderFocus"  Color="#4A90D9"/>
    <SolidColorBrush x:Key="Brush.Accent"       Color="#4A90D9"/>
    <SolidColorBrush x:Key="Brush.AccentDark"   Color="#2B6CB0"/>
    <SolidColorBrush x:Key="Brush.Success"      Color="#4EC94E"/>
    <SolidColorBrush x:Key="Brush.Warning"      Color="#D64545"/>
    <SolidColorBrush x:Key="Brush.ErrorFill"    Color="#3A2424"/>
    <SolidColorBrush x:Key="Brush.Splitter"     Color="#4A4A4A"/>
```

Note: `Brush.Warning` is repurposed as the app's single error/invalid-state red (was an unused orange-red, `#FF6B35`, referenced nowhere in any `Views/*.xaml` today) — this is what later tasks use for invalid-field borders/text and flagged-row styling. `Brush.ErrorFill` is new, used for the matching background tint.

- [ ] **Step 2: Build to verify no XAML/XAML-compile regression**

Run (from the `RVTuk` folder, in Windows PowerShell — not this session's bash):
```powershell
dotnet build src\LibraryBrowser\RVTuk.UI\RVTuk.UI.csproj -c Release2024
```
Expected: `Build succeeded.` (a values-only resource dictionary edit cannot introduce a compile error, but this catches typos like a malformed hex value or unclosed tag).

- [ ] **Step 3: Commit**

```bash
git add src/LibraryBrowser/RVTuk.UI/Themes/DarkTheme.xaml
git commit -m "Recolor RVTuk theme to a Revit-native charcoal/blue palette"
```

---

## Task 2: Add the grouped-panel header style

**Files:**
- Modify: `src/LibraryBrowser/RVTuk.UI/Themes/DarkTheme.xaml` (append before the closing `</ResourceDictionary>`)

**Interfaces:**
- Consumes: `Brush.Border`, `Brush.Text`, `Brush.TextMuted`, `Brush.Bg` (Task 1).
- Produces: `Brush.GroupHeader` brush key and a `GroupHeaderPanel` `Style` keyed for `HeaderedContentControl` — Task 4 wraps each Settings-tab field group in a `<HeaderedContentControl Style="{StaticResource GroupHeaderPanel}">`.

This mimics the reference screenshot's Properties-palette section bars (header bar + decorative chevron, body below). The chevron is static — groups are always expanded, this is not a functional `Expander`.

- [ ] **Step 1: Append the new brush + style**

In `src/LibraryBrowser/RVTuk.UI/Themes/DarkTheme.xaml`, find the end of the file:

```xml
    <Style TargetType="DataGridCell">
        <Setter Property="Foreground"      Value="{StaticResource Brush.Text}"/>
        <Setter Property="BorderThickness" Value="0"/>
        <Setter Property="Padding"         Value="4,2"/>
        <Style.Triggers>
            <Trigger Property="IsSelected" Value="True">
                <Setter Property="Foreground"  Value="{StaticResource Brush.Text}"/>
                <Setter Property="Background"  Value="Transparent"/>
            </Trigger>
        </Style.Triggers>
    </Style>

</ResourceDictionary>
```

Replace it with:

```xml
    <Style TargetType="DataGridCell">
        <Setter Property="Foreground"      Value="{StaticResource Brush.Text}"/>
        <Setter Property="BorderThickness" Value="0"/>
        <Setter Property="Padding"         Value="4,2"/>
        <Style.Triggers>
            <Trigger Property="IsSelected" Value="True">
                <Setter Property="Foreground"  Value="{StaticResource Brush.Text}"/>
                <Setter Property="Background"  Value="Transparent"/>
            </Trigger>
        </Style.Triggers>
    </Style>

    <!-- ── Group header panel (Settings-tab field grouping) ──────────────── -->
    <SolidColorBrush x:Key="Brush.GroupHeader" Color="#3F3F41"/>

    <Style x:Key="GroupHeaderPanel" TargetType="HeaderedContentControl">
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="HeaderedContentControl">
                    <Border BorderBrush="{StaticResource Brush.Border}" BorderThickness="1"
                            CornerRadius="2" Margin="0,0,0,9">
                        <DockPanel LastChildFill="True">
                            <Border DockPanel.Dock="Top"
                                    Background="{StaticResource Brush.GroupHeader}"
                                    BorderBrush="{StaticResource Brush.Border}"
                                    BorderThickness="0,0,0,1"
                                    Padding="8,5">
                                <Grid>
                                    <TextBlock Text="{TemplateBinding Header}"
                                               FontSize="11" FontWeight="SemiBold"
                                               Foreground="{StaticResource Brush.Text}"/>
                                    <TextBlock Text="▾" HorizontalAlignment="Right"
                                               Foreground="{StaticResource Brush.TextMuted}"
                                               FontSize="9"/>
                                </Grid>
                            </Border>
                            <Border Background="{StaticResource Brush.Bg}" Padding="9,8">
                                <ContentPresenter/>
                            </Border>
                        </DockPanel>
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

</ResourceDictionary>
```

- [ ] **Step 2: Build to verify**

```powershell
dotnet build src\LibraryBrowser\RVTuk.UI\RVTuk.UI.csproj -c Release2024
```
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add src/LibraryBrowser/RVTuk.UI/Themes/DarkTheme.xaml
git commit -m "Add a Revit-Properties-style group header panel style"
```

---

## Task 3: Add Settings validation + tab-badge properties to the view model

**Files:**
- Modify: `src/LibraryBrowser/RVTuk.UI/ViewModels/AreaSubmissionViewModel.cs` (full-file replacement below)

**Interfaces:**
- Consumes: `AreaSubmissionConfig` (`RVTuk.Core.AreaSubmission`, unchanged), `AreaLevelGroupViewModel.Rows` / `AreaRowViewModel.HasError` (unchanged in this task).
- Produces (new members later tasks bind to):
  - `string BuildingNoText { get; set; }`, `bool BuildingNoInvalid { get; }`
  - `string ScaleText { get; set; }`, `bool ScaleInvalid { get; }`
  - `string FileBaseNameText { get; set; }`, `bool FileBaseNameInvalid { get; }`
  - `bool OutputFolderInvalid { get; }` (alongside the existing `OutputFolder` property)
  - `bool HasInvalidSettings { get; }`
  - `int FlaggedCount { get; }`, `int TotalAreaCount { get; }`
  - `int CurrentPaneIndex { get; set; }` (mirrors the existing `CurrentPane`/`SubmissionPane`)
- This task does **not** remove `ShowConfigCommand`, `IsConfigPane`, or `IsAreasPane` — the current window XAML still binds to them until Task 4 replaces it, so removing them here would silently break pane-switching before Task 4 lands (WPF `{Binding}` failures are runtime-silent, not build errors — the old XAML would compile fine but stop working).

This task only **adds** members and only **extends** existing setters (`CurrentPane`, `OutputFolder`) with extra `OnPropertyChanged` calls — it does not change any existing binding path, so the current window keeps working exactly as before until Task 4.

- [ ] **Step 1: Replace the whole file**

Replace `src/LibraryBrowser/RVTuk.UI/ViewModels/AreaSubmissionViewModel.cs` with:

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Threading;
using RVTuk.Core.AreaSubmission;
using RVTuk.Core.Config;

namespace RVTuk.UI.ViewModels
{
    public enum SubmissionPane { Config, Areas }

    /// <summary>
    /// View model for the Area Calc (Rishui Zamin) window. Top toolbar switches the bottom pane
    /// between the Config fields and the area tree; Revit behaviour is injected as delegates so
    /// this project takes no Revit dependency.
    /// </summary>
    public class AreaSubmissionViewModel : ViewModelBase
    {
        private readonly Func<IReadOnlyList<(long Id, AreaRecord Rec)>> _extract;
        private readonly Action<long> _selectInModel;
        private readonly Func<IReadOnlyList<AreaRecord>, AreaSubmissionConfig, (bool ok, string msg)> _export;
        private readonly Func<(bool ok, string msg)>? _setupUsageKeys;
        private readonly Dispatcher _dispatcher;

        private SubmissionPane _currentPane = SubmissionPane.Config;
        private AreaRowViewModel? _selectedRow;
        private string _buildingNoText = "";
        private string _scaleText = "";
        private string _fileBaseNameText = "";

        /// <param name="extract">Reads the areas on the open sheet (id + record).</param>
        /// <param name="selectInModel">Selects an Area in the model by element id.</param>
        /// <param name="export">Validates + writes the DXF/DAT for the given records; returns (ok, message).</param>
        /// <param name="setupUsageKeys">Binds the robot's area text parameters and creates/tops-up
        /// the usage key schedules in the project; returns (ok, message). Blocking — run off the
        /// UI thread.</param>
        public AreaSubmissionViewModel(
            Func<IReadOnlyList<(long Id, AreaRecord Rec)>> extract,
            Action<long> selectInModel,
            Func<IReadOnlyList<AreaRecord>, AreaSubmissionConfig, (bool ok, string msg)> export,
            Func<(bool ok, string msg)>? setupUsageKeys = null)
        {
            _extract = extract;
            _selectInModel = selectInModel;
            _export = export;
            _setupUsageKeys = setupUsageKeys;
            _dispatcher = Dispatcher.CurrentDispatcher;

            ShowConfigCommand = new RelayCommand(() => CurrentPane = SubmissionPane.Config);
            RefreshCommand    = new RelayCommand(Refresh);
            ExportCommand     = new RelayCommand(Export);
            BrowseOutputCommand = new RelayCommand(BrowseOutput);
            SetupUsageKeysCommand = new RelayCommand(SetupUsageKeys);

            var savedOutputFolder = ConfigManager.LoadConfig().AreaCalcOutputFolder;
            if (!string.IsNullOrWhiteSpace(savedOutputFolder))
                Config.OutputFolder = savedOutputFolder;

            _buildingNoText = Config.BuildingNo.ToString();
            _scaleText = Config.Scale.ToString();
            _fileBaseNameText = Config.FileBaseName;
        }

        public AreaSubmissionConfig Config { get; } = new AreaSubmissionConfig();
        public ObservableCollection<AreaLevelGroupViewModel> Levels { get; } = new();

        public ICommand ShowConfigCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand BrowseOutputCommand { get; }
        public ICommand SetupUsageKeysCommand { get; }

        /// <summary>Raised after an export attempt so the window can show a result dialog.</summary>
        public event Action<bool, string>? ExportCompleted;

        public SubmissionPane CurrentPane
        {
            get => _currentPane;
            set
            {
                SetProperty(ref _currentPane, value);
                OnPropertyChanged(nameof(IsConfigPane));
                OnPropertyChanged(nameof(IsAreasPane));
                OnPropertyChanged(nameof(CurrentPaneIndex));
            }
        }

        public bool IsConfigPane => _currentPane == SubmissionPane.Config;
        public bool IsAreasPane  => _currentPane == SubmissionPane.Areas;

        /// <summary>Zero-based index mirror of <see cref="CurrentPane"/> for two-way binding to
        /// the Settings/Areas <c>TabControl.SelectedIndex</c> (so clicking a tab keeps
        /// <see cref="CurrentPane"/> in sync, and code that sets <see cref="CurrentPane"/>
        /// programmatically — e.g. <see cref="Refresh"/> — still switches the visible tab).</summary>
        public int CurrentPaneIndex
        {
            get => (int)_currentPane;
            set => CurrentPane = (SubmissionPane)value;
        }

        /// <summary>Notifying wrapper over <see cref="AreaSubmissionConfig.OutputFolder"/> so the
        /// Browse button can update the bound textbox.</summary>
        public string OutputFolder
        {
            get => Config.OutputFolder;
            set
            {
                if (Config.OutputFolder == value) return;
                Config.OutputFolder = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(OutputFolderInvalid));
                OnPropertyChanged(nameof(HasInvalidSettings));

                var appConfig = ConfigManager.LoadConfig();
                appConfig.AreaCalcOutputFolder = value;
                ConfigManager.SaveConfig(appConfig);
            }
        }

        /// <summary>True when no output folder is set — blocks export (mirrors
        /// <c>AreaValidator.CheckConfig</c>'s "Output folder is not set" rule).</summary>
        public bool OutputFolderInvalid => string.IsNullOrWhiteSpace(OutputFolder);

        /// <summary>Notifying wrapper over <see cref="AreaSubmissionConfig.BuildingNo"/> so the
        /// Settings tab can highlight it red when blank or non-positive. Kept as a string (rather
        /// than binding <c>Config.BuildingNo</c> directly) so an in-progress edit — e.g. the field
        /// briefly empty while retyping — doesn't fight WPF's built-in int type-conversion.</summary>
        public string BuildingNoText
        {
            get => _buildingNoText;
            set
            {
                SetProperty(ref _buildingNoText, value);
                Config.BuildingNo = int.TryParse(value, out var n) ? n : 0;
                OnPropertyChanged(nameof(BuildingNoInvalid));
                OnPropertyChanged(nameof(HasInvalidSettings));
            }
        }

        /// <summary>Mirrors <c>AreaValidator.CheckConfig</c>'s "Building number must be at least
        /// 1" rule.</summary>
        public bool BuildingNoInvalid => !int.TryParse(_buildingNoText, out var n) || n < 1;

        /// <summary>Notifying wrapper over <see cref="AreaSubmissionConfig.Scale"/> (the
        /// DWFX_SCALE value) so the Settings tab can highlight it red when blank or non-positive.</summary>
        public string ScaleText
        {
            get => _scaleText;
            set
            {
                SetProperty(ref _scaleText, value);
                Config.Scale = int.TryParse(value, out var n) ? n : 0;
                OnPropertyChanged(nameof(ScaleInvalid));
                OnPropertyChanged(nameof(HasInvalidSettings));
            }
        }

        /// <summary>Mirrors <c>AreaValidator.CheckConfig</c>'s "Scale must be greater than zero"
        /// rule.</summary>
        public bool ScaleInvalid => !int.TryParse(_scaleText, out var n) || n <= 0;

        /// <summary>Notifying wrapper over <see cref="AreaSubmissionConfig.FileBaseName"/> so the
        /// Settings tab can highlight it red when blank.</summary>
        public string FileBaseNameText
        {
            get => _fileBaseNameText;
            set
            {
                SetProperty(ref _fileBaseNameText, value);
                Config.FileBaseName = value;
                OnPropertyChanged(nameof(FileBaseNameInvalid));
                OnPropertyChanged(nameof(HasInvalidSettings));
            }
        }

        /// <summary>Mirrors <c>AreaValidator.CheckConfig</c>'s "File base name is not set" rule.</summary>
        public bool FileBaseNameInvalid => string.IsNullOrWhiteSpace(_fileBaseNameText);

        /// <summary>True when any Settings field required for export is missing or invalid —
        /// drives the Settings tab's warning badge. Deliberately mirrors the same four checks as
        /// <c>AreaValidator.CheckConfig</c> so the UI and the export-time check never disagree
        /// about what counts as incomplete.</summary>
        public bool HasInvalidSettings =>
            BuildingNoInvalid || ScaleInvalid || FileBaseNameInvalid || OutputFolderInvalid;

        /// <summary>Count of area rows currently flagged with an error — drives the Areas tab's
        /// count badge and its summary banner. Recomputed (via <see cref="OnPropertyChanged"/>)
        /// whenever <see cref="Refresh"/> repopulates <see cref="Levels"/>.</summary>
        public int FlaggedCount => Levels.SelectMany(g => g.Rows).Count(r => r.HasError);

        /// <summary>Total area rows currently loaded — used by the Areas tab's summary banner
        /// text ("N of M areas flagged").</summary>
        public int TotalAreaCount => Levels.SelectMany(g => g.Rows).Count();

        /// <summary>"Official" marker radio — Form A, the spec's block/ATTRIB encoding
        /// (docs/autoarea/rishui-zamin-rules.md §5). Mutually exclusive with
        /// <see cref="UseOldMarkers"/>.</summary>
        public bool UseOfficialMarkers
        {
            get => Config.MarkerForm == MarkerForm.FormA;
            set
            {
                if (value && Config.MarkerForm != MarkerForm.FormA)
                {
                    Config.MarkerForm = MarkerForm.FormA;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(UseOldMarkers));
                }
            }
        }

        /// <summary>"Old" marker radio — Form B, the tekenplus plain-TEXT encoding RVTuk emitted
        /// before the Official option existed (kept for comparison/fallback).</summary>
        public bool UseOldMarkers
        {
            get => Config.MarkerForm == MarkerForm.FormB;
            set
            {
                if (value && Config.MarkerForm != MarkerForm.FormB)
                {
                    Config.MarkerForm = MarkerForm.FormB;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(UseOfficialMarkers));
                }
            }
        }

        public AreaRowViewModel? SelectedRow
        {
            get => _selectedRow;
            set
            {
                SetProperty(ref _selectedRow, value);
                if (value != null)
                {
                    try { _selectInModel(value.ElementId); }
                    catch { /* selection is best-effort; never crash the window */ }
                }
            }
        }

        // Extraction goes through a Revit ExternalEvent ping-pong that blocks the calling thread
        // until Revit's main thread services it. This window lives ON the main thread, so we must
        // run the extract off-thread (else the main thread blocks waiting for itself) and marshal
        // the results back to the Dispatcher — same pattern as the Family Browser's Sync.
        private void Refresh()
        {
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                IReadOnlyList<(long Id, AreaRecord Rec)> extracted;
                try { extracted = _extract(); }
                catch (Exception ex)
                {
                    _dispatcher.Invoke(() => ExportCompleted?.Invoke(false, "Could not read the open sheet: " + ex.Message));
                    return;
                }

                var groups = extracted
                    .GroupBy(e => e.Rec.Floor ?? string.Empty)
                    .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(g =>
                    {
                        var vm = new AreaLevelGroupViewModel(g.Key);
                        foreach (var (id, rec) in g.OrderBy(e => e.Rec.Number, StringComparer.OrdinalIgnoreCase))
                            vm.Rows.Add(new AreaRowViewModel(id, rec));
                        return vm;
                    })
                    .ToList();

                _dispatcher.Invoke(() =>
                {
                    Levels.Clear();
                    foreach (var g in groups) Levels.Add(g);
                    OnPropertyChanged(nameof(FlaggedCount));
                    OnPropertyChanged(nameof(TotalAreaCount));
                    CurrentPane = SubmissionPane.Areas;
                    if (extracted.Count == 0)
                        ExportCompleted?.Invoke(false, "No areas found on the open sheet's area plan(s).");
                });
            });
        }

        private void Export()
        {
            var records = Levels.SelectMany(g => g.Rows).Select(r => r.Record).ToList();
            if (records.Count == 0)
            {
                ExportCompleted?.Invoke(false, "No areas loaded — click Refresh first.");
                return;
            }
            var (ok, msg) = _export(records, Config);
            ExportCompleted?.Invoke(ok, msg);
        }

        /// <summary>Creates the usage key schedules / area parameters in the project. Blocks on
        /// Revit's main thread, so it runs on the thread pool like <see cref="Refresh"/>.</summary>
        private void SetupUsageKeys()
        {
            if (_setupUsageKeys == null)
            {
                ExportCompleted?.Invoke(false, "Usage key setup is not available in this context.");
                return;
            }

            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                (bool ok, string msg) result;
                try { result = _setupUsageKeys(); }
                catch (Exception ex) { result = (false, "Usage key setup failed: " + ex.Message); }

                _dispatcher.Invoke(() => ExportCompleted?.Invoke(result.ok, result.msg));
            });
        }

        private void BrowseOutput()
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select the output folder for the .dxf / .dat files",
                SelectedPath = System.IO.Directory.Exists(OutputFolder) ? OutputFolder : string.Empty
            };
            if (dialog.ShowDialog() == DialogResult.OK)
                OutputFolder = dialog.SelectedPath;
        }
    }
}
```

- [ ] **Step 2: Build to verify**

```powershell
dotnet build src\LibraryBrowser\RVTuk.UI\RVTuk.UI.csproj -c Release2024
```
Expected: `Build succeeded.` The window's current behavior is unchanged (old XAML still binds to `Config.BuildingNo`/`Config.Scale`/`Config.FileBaseName`/`IsConfigPane`/`IsAreasPane`/`ShowConfigCommand`, all of which still exist).

- [ ] **Step 3: Commit**

```bash
git add src/LibraryBrowser/RVTuk.UI/ViewModels/AreaSubmissionViewModel.cs
git commit -m "Add Settings validation and area-count properties to AreaSubmissionViewModel"
```

---

## Task 4: Rebuild the window around a Settings/Areas TabControl

**Files:**
- Modify: `src/LibraryBrowser/RVTuk.UI/ViewModels/AreaSubmissionViewModel.cs` (remove `ShowConfigCommand`/`IsConfigPane`/`IsAreasPane` — their only consumer, the old toolbar/pane-visibility XAML, is replaced in this task)
- Modify: `src/LibraryBrowser/RVTuk.UI/Views/AreaSubmissionWindow.xaml` (full-file replacement below)

**Interfaces:**
- Consumes: `GroupHeaderPanel` style + `Brush.Warning`/`Brush.ErrorFill` (Task 2/1), `BuildingNoText`/`BuildingNoInvalid`, `ScaleText`/`ScaleInvalid`, `FileBaseNameText`/`FileBaseNameInvalid`, `OutputFolderInvalid`, `HasInvalidSettings`, `CurrentPaneIndex` (Task 3).
- Produces: the Settings tab's final layout (this does not change again). The Areas tab in this task is a deliberate unstyled placeholder — same tree/template as today, just moved inside a `TabItem` — restyled in Task 5.

This is the biggest task: it removes the old toolbar (Config/Refresh/Usage Keys/Export) and the `Visibility`-toggled Config/Areas pane pair, replacing them with a persistent 2-button toolbar (Refresh/Export) above a `TabControl`.

- [ ] **Step 1: Remove the now-dead ShowConfigCommand / IsConfigPane / IsAreasPane from the view model**

In `src/LibraryBrowser/RVTuk.UI/ViewModels/AreaSubmissionViewModel.cs`, remove the `ShowConfigCommand` wiring. Replace:

```csharp
            ShowConfigCommand = new RelayCommand(() => CurrentPane = SubmissionPane.Config);
            RefreshCommand    = new RelayCommand(Refresh);
```

with:

```csharp
            RefreshCommand    = new RelayCommand(Refresh);
```

Replace:

```csharp
        public ICommand ShowConfigCommand { get; }
        public ICommand RefreshCommand { get; }
```

with:

```csharp
        public ICommand RefreshCommand { get; }
```

Replace:

```csharp
        public SubmissionPane CurrentPane
        {
            get => _currentPane;
            set
            {
                SetProperty(ref _currentPane, value);
                OnPropertyChanged(nameof(IsConfigPane));
                OnPropertyChanged(nameof(IsAreasPane));
                OnPropertyChanged(nameof(CurrentPaneIndex));
            }
        }

        public bool IsConfigPane => _currentPane == SubmissionPane.Config;
        public bool IsAreasPane  => _currentPane == SubmissionPane.Areas;

        /// <summary>Zero-based index mirror of <see cref="CurrentPane"/> for two-way binding to
        /// the Settings/Areas <c>TabControl.SelectedIndex</c> (so clicking a tab keeps
        /// <see cref="CurrentPane"/> in sync, and code that sets <see cref="CurrentPane"/>
        /// programmatically — e.g. <see cref="Refresh"/> — still switches the visible tab).</summary>
        public int CurrentPaneIndex
        {
            get => (int)_currentPane;
            set => CurrentPane = (SubmissionPane)value;
        }
```

with:

```csharp
        public SubmissionPane CurrentPane
        {
            get => _currentPane;
            set
            {
                SetProperty(ref _currentPane, value);
                OnPropertyChanged(nameof(CurrentPaneIndex));
            }
        }

        /// <summary>Zero-based index mirror of <see cref="CurrentPane"/> for two-way binding to
        /// the Settings/Areas <c>TabControl.SelectedIndex</c> (so clicking a tab keeps
        /// <see cref="CurrentPane"/> in sync, and code that sets <see cref="CurrentPane"/>
        /// programmatically — e.g. <see cref="Refresh"/> — still switches the visible tab).</summary>
        public int CurrentPaneIndex
        {
            get => (int)_currentPane;
            set => CurrentPane = (SubmissionPane)value;
        }
```

- [ ] **Step 2: Replace the whole window XAML**

Replace `src/LibraryBrowser/RVTuk.UI/Views/AreaSubmissionWindow.xaml` with:

```xml
<Window x:Class="RVTuk.UI.Views.AreaSubmissionWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:RVTuk.UI.ViewModels"
        Title="RVTuk — Area Calc (Rishui Zamin)"
        Width="480" Height="600" MinWidth="380" MinHeight="420"
        WindowStartupLocation="CenterScreen" Topmost="True">

    <Window.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="/RVTuk.UI;component/Themes/DarkTheme.xaml"/>
            </ResourceDictionary.MergedDictionaries>
            <BooleanToVisibilityConverter x:Key="BoolVis"/>
        </ResourceDictionary>
    </Window.Resources>

    <Window.Background><StaticResource ResourceKey="Brush.Bg"/></Window.Background>
    <Window.Foreground><StaticResource ResourceKey="Brush.Text"/></Window.Foreground>

    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>

        <!-- Persistent toolbar — visible regardless of the active tab -->
        <StackPanel Grid.Row="0" Orientation="Horizontal" Margin="8"
                    Background="{StaticResource Brush.Panel}">
            <Button Content="⟳ Refresh" Command="{Binding RefreshCommand}" Margin="0,0,6,0" Padding="10,5"/>
            <Button Content="⬆ Export"  Command="{Binding ExportCommand}"  Padding="10,5"
                    Background="{StaticResource Brush.AccentDark}" BorderBrush="{StaticResource Brush.Accent}"/>
        </StackPanel>

        <TabControl Grid.Row="1" SelectedIndex="{Binding CurrentPaneIndex, Mode=TwoWay}">

            <!-- ── SETTINGS TAB ─────────────────────────────────────────── -->
            <TabItem>
                <TabItem.Header>
                    <StackPanel Orientation="Horizontal">
                        <TextBlock Text="Settings"/>
                        <Border Background="{StaticResource Brush.ErrorFill}" CornerRadius="8" Padding="5,0" Margin="5,0,0,0"
                                Visibility="{Binding HasInvalidSettings, Converter={StaticResource BoolVis}}">
                            <TextBlock Text="!" FontSize="9" Foreground="{StaticResource Brush.Warning}"/>
                        </Border>
                    </StackPanel>
                </TabItem.Header>

                <ScrollViewer VerticalScrollBarVisibility="Auto">
                    <StackPanel Margin="12,12,12,14">

                        <HeaderedContentControl Header="Submission" Style="{StaticResource GroupHeaderPanel}">
                            <StackPanel>
                                <TextBlock Margin="0,0,0,2">
                                    <TextBlock.Style>
                                        <Style TargetType="TextBlock" BasedOn="{StaticResource {x:Type TextBlock}}">
                                            <Setter Property="Text" Value="Building number"/>
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding BuildingNoInvalid}" Value="True">
                                                    <Setter Property="Foreground" Value="{StaticResource Brush.Warning}"/>
                                                </DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </TextBlock.Style>
                                </TextBlock>
                                <TextBox Text="{Binding BuildingNoText, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,0,10">
                                    <TextBox.Style>
                                        <Style TargetType="TextBox" BasedOn="{StaticResource {x:Type TextBox}}">
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding BuildingNoInvalid}" Value="True">
                                                    <Setter Property="BorderBrush" Value="{StaticResource Brush.Warning}"/>
                                                    <Setter Property="Background" Value="{StaticResource Brush.ErrorFill}"/>
                                                </DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </TextBox.Style>
                                </TextBox>

                                <TextBlock Text="Asset (optional)" Margin="0,0,0,2"/>
                                <TextBox Text="{Binding Config.Asset}" Margin="0,0,0,10"/>

                                <TextBlock Margin="0,0,0,2">
                                    <TextBlock.Style>
                                        <Style TargetType="TextBlock" BasedOn="{StaticResource {x:Type TextBlock}}">
                                            <Setter Property="Text" Value="Scale value (DWFX_SCALE — 10 for 1:100)"/>
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding ScaleInvalid}" Value="True">
                                                    <Setter Property="Foreground" Value="{StaticResource Brush.Warning}"/>
                                                </DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </TextBlock.Style>
                                </TextBlock>
                                <TextBox Text="{Binding ScaleText, UpdateSourceTrigger=PropertyChanged}">
                                    <TextBox.Style>
                                        <Style TargetType="TextBox" BasedOn="{StaticResource {x:Type TextBox}}">
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding ScaleInvalid}" Value="True">
                                                    <Setter Property="BorderBrush" Value="{StaticResource Brush.Warning}"/>
                                                    <Setter Property="Background" Value="{StaticResource Brush.ErrorFill}"/>
                                                </DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </TextBox.Style>
                                </TextBox>
                            </StackPanel>
                        </HeaderedContentControl>

                        <HeaderedContentControl Header="Markers &amp; Output" Style="{StaticResource GroupHeaderPanel}">
                            <StackPanel>
                                <TextBlock Text="Marker format" Margin="0,0,0,2"/>
                                <RadioButton GroupName="MarkerForm" Margin="0,2,0,2"
                                             IsChecked="{Binding UseOfficialMarkers}"
                                             Content="Official (Block Attributes)"
                                             ToolTip="RZ_*_SYM block inserts with one attribute per tag — the encoding the official spec describes (Garmoshka sample)"/>
                                <RadioButton GroupName="MarkerForm" Margin="0,0,0,10"
                                             IsChecked="{Binding UseOldMarkers}"
                                             Content="Plain text"
                                             ToolTip="One TEXT per polygon with KEY=VALUE&amp;&amp;&amp;… content — the tekenplus encoding RVTuk emitted before"/>

                                <TextBlock Margin="0,0,0,2">
                                    <TextBlock.Style>
                                        <Style TargetType="TextBlock" BasedOn="{StaticResource {x:Type TextBlock}}">
                                            <Setter Property="Text" Value="Output file base name (no extension)"/>
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding FileBaseNameInvalid}" Value="True">
                                                    <Setter Property="Foreground" Value="{StaticResource Brush.Warning}"/>
                                                </DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </TextBlock.Style>
                                </TextBlock>
                                <TextBox Text="{Binding FileBaseNameText, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,0,10">
                                    <TextBox.Style>
                                        <Style TargetType="TextBox" BasedOn="{StaticResource {x:Type TextBox}}">
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding FileBaseNameInvalid}" Value="True">
                                                    <Setter Property="BorderBrush" Value="{StaticResource Brush.Warning}"/>
                                                    <Setter Property="Background" Value="{StaticResource Brush.ErrorFill}"/>
                                                </DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </TextBox.Style>
                                </TextBox>

                                <TextBlock Margin="0,0,0,2">
                                    <TextBlock.Style>
                                        <Style TargetType="TextBlock" BasedOn="{StaticResource {x:Type TextBlock}}">
                                            <Setter Property="Text" Value="Output folder"/>
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding OutputFolderInvalid}" Value="True">
                                                    <Setter Property="Foreground" Value="{StaticResource Brush.Warning}"/>
                                                </DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </TextBlock.Style>
                                </TextBlock>
                                <Grid>
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="*"/>
                                        <ColumnDefinition Width="Auto"/>
                                    </Grid.ColumnDefinitions>
                                    <TextBox Grid.Column="0" Margin="0,0,6,0"
                                             Text="{Binding OutputFolder, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}">
                                        <TextBox.Style>
                                            <Style TargetType="TextBox" BasedOn="{StaticResource {x:Type TextBox}}">
                                                <Style.Triggers>
                                                    <DataTrigger Binding="{Binding OutputFolderInvalid}" Value="True">
                                                        <Setter Property="BorderBrush" Value="{StaticResource Brush.Warning}"/>
                                                        <Setter Property="Background" Value="{StaticResource Brush.ErrorFill}"/>
                                                    </DataTrigger>
                                                </Style.Triggers>
                                            </Style>
                                        </TextBox.Style>
                                    </TextBox>
                                    <Button Grid.Column="1" Content="Browse…" Width="72" Command="{Binding BrowseOutputCommand}"/>
                                </Grid>
                            </StackPanel>
                        </HeaderedContentControl>

                        <HeaderedContentControl Header="Project setup" Style="{StaticResource GroupHeaderPanel}">
                            <StackPanel>
                                <Button Content="🔑 Setup Usage Keys" HorizontalAlignment="Left" Padding="10,5"
                                        Command="{Binding SetupUsageKeysCommand}"/>
                                <TextBlock Foreground="{StaticResource Brush.TextMuted}" FontSize="10" Margin="0,8,0,0"
                                           TextWrapping="Wrap">
                                    Creates the RZ usage key schedules and area text parameters in this project.
                                    Idempotent — safe to re-run.
                                </TextBlock>
                            </StackPanel>
                        </HeaderedContentControl>

                        <TextBlock Foreground="{StaticResource Brush.TextMuted}" FontSize="10" Margin="0,4,0,0"
                                   TextWrapping="Wrap">
                            Writes «base».dxf + «base».dat. Export the DWFX sheets from Revit separately.
                            Press Refresh to list the open sheet's areas and check for errors before exporting.
                        </TextBlock>
                    </StackPanel>
                </ScrollViewer>
            </TabItem>

            <!-- ── AREAS TAB (unstyled placeholder — restyled in the next task) ── -->
            <TabItem Header="Areas">
                <TreeView x:Name="AreaTree"
                          ItemsSource="{Binding Levels}"
                          SelectedItemChanged="AreaTree_SelectedItemChanged"
                          BorderThickness="0" Background="{StaticResource Brush.Bg}">
                    <TreeView.Resources>
                        <HierarchicalDataTemplate DataType="{x:Type vm:AreaLevelGroupViewModel}" ItemsSource="{Binding Rows}">
                            <StackPanel Orientation="Horizontal">
                                <TextBlock Text="{Binding Header}" FontWeight="SemiBold"/>
                                <TextBlock Text="  ⚠" Foreground="{StaticResource Brush.Accent}"
                                           Visibility="{Binding HasError, Converter={StaticResource BoolVis}}"/>
                            </StackPanel>
                        </HierarchicalDataTemplate>
                        <DataTemplate DataType="{x:Type vm:AreaRowViewModel}">
                            <TextBlock Text="{Binding Display}" ToolTip="{Binding ErrorSummary}">
                                <TextBlock.Style>
                                    <Style TargetType="TextBlock">
                                        <Setter Property="Foreground" Value="{StaticResource Brush.Text}"/>
                                        <Style.Triggers>
                                            <DataTrigger Binding="{Binding HasError}" Value="True">
                                                <Setter Property="Foreground" Value="#FF5555"/>
                                                <Setter Property="FontWeight" Value="SemiBold"/>
                                            </DataTrigger>
                                        </Style.Triggers>
                                    </Style>
                                </TextBlock.Style>
                            </TextBlock>
                        </DataTemplate>
                    </TreeView.Resources>
                </TreeView>
            </TabItem>

        </TabControl>
    </Grid>
</Window>
```

Note: the Areas `TabItem` here still binds to `Display` on `AreaRowViewModel` (today's property) — that's intentional, it keeps this task's Areas tab fully functional (just unstyled) without depending on Task 5's `AreaRowViewModel` changes.

- [ ] **Step 3: Build to verify**

```powershell
dotnet build src\LibraryBrowser\RVTuk.UI\RVTuk.UI.csproj -c Release2024
```
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add src/LibraryBrowser/RVTuk.UI/ViewModels/AreaSubmissionViewModel.cs src/LibraryBrowser/RVTuk.UI/Views/AreaSubmissionWindow.xaml
git commit -m "Rebuild Area Submission window around a Settings/Areas TabControl"
```

---

## Task 5: Restyle the Areas tab (summary banner, tag rows, count badge)

**Files:**
- Modify: `src/LibraryBrowser/RVTuk.UI/ViewModels/AreaRowViewModel.cs` (full-file replacement below)
- Create: `src/LibraryBrowser/RVTuk.UI/Converters/CountToVisibilityConverter.cs`
- Modify: `src/LibraryBrowser/RVTuk.UI/Views/AreaSubmissionWindow.xaml` (Areas `TabItem` only — full-file replacement below, Settings tab unchanged from Task 4)

**Interfaces:**
- Consumes: `FlaggedCount`/`TotalAreaCount` (Task 3), `Brush.Warning`/`Brush.ErrorFill`/`Brush.Control`/`Brush.Border`/`Brush.TextMuted` (Task 1).
- Produces: `AreaRowViewModel.NumberAndName` / `AreaRowViewModel.Tag` (replacing `Display`), `RVTuk.UI.Converters.CountToVisibilityConverter`.
- `AreaRowViewModel.Display` is removed — its only consumer was the placeholder `DataTemplate` from Task 4, replaced in this task.

- [ ] **Step 1: Replace AreaRowViewModel.cs**

Replace `src/LibraryBrowser/RVTuk.UI/ViewModels/AreaRowViewModel.cs` with:

```csharp
using RVTuk.Core.AreaSubmission;

namespace RVTuk.UI.ViewModels
{
    /// <summary>One Area row in the submission tree. Wraps the Core <see cref="AreaRecord"/>
    /// plus the Revit element id used to select it in the model.</summary>
    public class AreaRowViewModel
    {
        public long ElementId { get; }
        public AreaRecord Record { get; }

        public AreaRowViewModel(long elementId, AreaRecord record)
        {
            ElementId = elementId;
            Record = record;
        }

        public string? Number => Record.Number;
        public string? Name => Record.Name;
        public int? UsageCode => Record.UsageCode;
        public int? UsageCodePrev => Record.UsageCodePrev;
        public bool HasError => Record.Errors != AreaError.None;

        /// <summary>Left-hand label: "«number» — «name»" (or "(no #)" when the area has no
        /// Number set).</summary>
        public string NumberAndName
        {
            get
            {
                var head = string.IsNullOrWhiteSpace(Number) ? "(no #)" : Number!;
                var name = string.IsNullOrWhiteSpace(Name) ? "" : " — " + Name;
                return head + name;
            }
        }

        /// <summary>Right-hand tag: the usage code, with a permit-history code
        /// (USAGE_TYPE_OLD) shown as "301←1" so unexpected template data is visible, or
        /// "⚠ no code" when neither is set.</summary>
        public string Tag => UsageCode.HasValue || UsageCodePrev.HasValue
            ? (UsageCode?.ToString() ?? "–") + (UsageCodePrev.HasValue ? "←" + UsageCodePrev : "")
            : "⚠ no code";

        /// <summary>Comma-joined reason(s) this row is flagged, for a tooltip.</summary>
        public string? ErrorSummary => HasError ? Record.Errors.ToString() : null;
    }
}
```

- [ ] **Step 2: Add the count-to-visibility converter**

Create `src/LibraryBrowser/RVTuk.UI/Converters/CountToVisibilityConverter.cs`:

```csharp
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RVTuk.UI.Converters
{
    /// <summary>Collapses when the bound count is zero (or not an int); visible otherwise. Used
    /// for count badges (e.g. the Areas tab's flagged-row badge) that should only show up when
    /// there's something to report.</summary>
    public class CountToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is int count && count > 0 ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
```

(No `.csproj` edit needed — this is an SDK-style project, so new `.cs` files under the project folder are picked up automatically.)

- [ ] **Step 3: Replace the whole window XAML with the final version**

Replace `src/LibraryBrowser/RVTuk.UI/Views/AreaSubmissionWindow.xaml` with:

```xml
<Window x:Class="RVTuk.UI.Views.AreaSubmissionWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:RVTuk.UI.ViewModels"
        xmlns:conv="clr-namespace:RVTuk.UI.Converters"
        Title="RVTuk — Area Calc (Rishui Zamin)"
        Width="480" Height="600" MinWidth="380" MinHeight="420"
        WindowStartupLocation="CenterScreen" Topmost="True">

    <Window.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="/RVTuk.UI;component/Themes/DarkTheme.xaml"/>
            </ResourceDictionary.MergedDictionaries>
            <BooleanToVisibilityConverter x:Key="BoolVis"/>
            <conv:CountToVisibilityConverter x:Key="CountVis"/>
        </ResourceDictionary>
    </Window.Resources>

    <Window.Background><StaticResource ResourceKey="Brush.Bg"/></Window.Background>
    <Window.Foreground><StaticResource ResourceKey="Brush.Text"/></Window.Foreground>

    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>

        <!-- Persistent toolbar — visible regardless of the active tab -->
        <StackPanel Grid.Row="0" Orientation="Horizontal" Margin="8"
                    Background="{StaticResource Brush.Panel}">
            <Button Content="⟳ Refresh" Command="{Binding RefreshCommand}" Margin="0,0,6,0" Padding="10,5"/>
            <Button Content="⬆ Export"  Command="{Binding ExportCommand}"  Padding="10,5"
                    Background="{StaticResource Brush.AccentDark}" BorderBrush="{StaticResource Brush.Accent}"/>
        </StackPanel>

        <TabControl Grid.Row="1" SelectedIndex="{Binding CurrentPaneIndex, Mode=TwoWay}">

            <!-- ── SETTINGS TAB ─────────────────────────────────────────── -->
            <TabItem>
                <TabItem.Header>
                    <StackPanel Orientation="Horizontal">
                        <TextBlock Text="Settings"/>
                        <Border Background="{StaticResource Brush.ErrorFill}" CornerRadius="8" Padding="5,0" Margin="5,0,0,0"
                                Visibility="{Binding HasInvalidSettings, Converter={StaticResource BoolVis}}">
                            <TextBlock Text="!" FontSize="9" Foreground="{StaticResource Brush.Warning}"/>
                        </Border>
                    </StackPanel>
                </TabItem.Header>

                <ScrollViewer VerticalScrollBarVisibility="Auto">
                    <StackPanel Margin="12,12,12,14">

                        <HeaderedContentControl Header="Submission" Style="{StaticResource GroupHeaderPanel}">
                            <StackPanel>
                                <TextBlock Margin="0,0,0,2">
                                    <TextBlock.Style>
                                        <Style TargetType="TextBlock" BasedOn="{StaticResource {x:Type TextBlock}}">
                                            <Setter Property="Text" Value="Building number"/>
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding BuildingNoInvalid}" Value="True">
                                                    <Setter Property="Foreground" Value="{StaticResource Brush.Warning}"/>
                                                </DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </TextBlock.Style>
                                </TextBlock>
                                <TextBox Text="{Binding BuildingNoText, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,0,10">
                                    <TextBox.Style>
                                        <Style TargetType="TextBox" BasedOn="{StaticResource {x:Type TextBox}}">
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding BuildingNoInvalid}" Value="True">
                                                    <Setter Property="BorderBrush" Value="{StaticResource Brush.Warning}"/>
                                                    <Setter Property="Background" Value="{StaticResource Brush.ErrorFill}"/>
                                                </DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </TextBox.Style>
                                </TextBox>

                                <TextBlock Text="Asset (optional)" Margin="0,0,0,2"/>
                                <TextBox Text="{Binding Config.Asset}" Margin="0,0,0,10"/>

                                <TextBlock Margin="0,0,0,2">
                                    <TextBlock.Style>
                                        <Style TargetType="TextBlock" BasedOn="{StaticResource {x:Type TextBlock}}">
                                            <Setter Property="Text" Value="Scale value (DWFX_SCALE — 10 for 1:100)"/>
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding ScaleInvalid}" Value="True">
                                                    <Setter Property="Foreground" Value="{StaticResource Brush.Warning}"/>
                                                </DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </TextBlock.Style>
                                </TextBlock>
                                <TextBox Text="{Binding ScaleText, UpdateSourceTrigger=PropertyChanged}">
                                    <TextBox.Style>
                                        <Style TargetType="TextBox" BasedOn="{StaticResource {x:Type TextBox}}">
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding ScaleInvalid}" Value="True">
                                                    <Setter Property="BorderBrush" Value="{StaticResource Brush.Warning}"/>
                                                    <Setter Property="Background" Value="{StaticResource Brush.ErrorFill}"/>
                                                </DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </TextBox.Style>
                                </TextBox>
                            </StackPanel>
                        </HeaderedContentControl>

                        <HeaderedContentControl Header="Markers &amp; Output" Style="{StaticResource GroupHeaderPanel}">
                            <StackPanel>
                                <TextBlock Text="Marker format" Margin="0,0,0,2"/>
                                <RadioButton GroupName="MarkerForm" Margin="0,2,0,2"
                                             IsChecked="{Binding UseOfficialMarkers}"
                                             Content="Official (Block Attributes)"
                                             ToolTip="RZ_*_SYM block inserts with one attribute per tag — the encoding the official spec describes (Garmoshka sample)"/>
                                <RadioButton GroupName="MarkerForm" Margin="0,0,0,10"
                                             IsChecked="{Binding UseOldMarkers}"
                                             Content="Plain text"
                                             ToolTip="One TEXT per polygon with KEY=VALUE&amp;&amp;&amp;… content — the tekenplus encoding RVTuk emitted before"/>

                                <TextBlock Margin="0,0,0,2">
                                    <TextBlock.Style>
                                        <Style TargetType="TextBlock" BasedOn="{StaticResource {x:Type TextBlock}}">
                                            <Setter Property="Text" Value="Output file base name (no extension)"/>
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding FileBaseNameInvalid}" Value="True">
                                                    <Setter Property="Foreground" Value="{StaticResource Brush.Warning}"/>
                                                </DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </TextBlock.Style>
                                </TextBlock>
                                <TextBox Text="{Binding FileBaseNameText, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,0,10">
                                    <TextBox.Style>
                                        <Style TargetType="TextBox" BasedOn="{StaticResource {x:Type TextBox}}">
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding FileBaseNameInvalid}" Value="True">
                                                    <Setter Property="BorderBrush" Value="{StaticResource Brush.Warning}"/>
                                                    <Setter Property="Background" Value="{StaticResource Brush.ErrorFill}"/>
                                                </DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </TextBox.Style>
                                </TextBox>

                                <TextBlock Margin="0,0,0,2">
                                    <TextBlock.Style>
                                        <Style TargetType="TextBlock" BasedOn="{StaticResource {x:Type TextBlock}}">
                                            <Setter Property="Text" Value="Output folder"/>
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding OutputFolderInvalid}" Value="True">
                                                    <Setter Property="Foreground" Value="{StaticResource Brush.Warning}"/>
                                                </DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </TextBlock.Style>
                                </TextBlock>
                                <Grid>
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="*"/>
                                        <ColumnDefinition Width="Auto"/>
                                    </Grid.ColumnDefinitions>
                                    <TextBox Grid.Column="0" Margin="0,0,6,0"
                                             Text="{Binding OutputFolder, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}">
                                        <TextBox.Style>
                                            <Style TargetType="TextBox" BasedOn="{StaticResource {x:Type TextBox}}">
                                                <Style.Triggers>
                                                    <DataTrigger Binding="{Binding OutputFolderInvalid}" Value="True">
                                                        <Setter Property="BorderBrush" Value="{StaticResource Brush.Warning}"/>
                                                        <Setter Property="Background" Value="{StaticResource Brush.ErrorFill}"/>
                                                    </DataTrigger>
                                                </Style.Triggers>
                                            </Style>
                                        </TextBox.Style>
                                    </TextBox>
                                    <Button Grid.Column="1" Content="Browse…" Width="72" Command="{Binding BrowseOutputCommand}"/>
                                </Grid>
                            </StackPanel>
                        </HeaderedContentControl>

                        <HeaderedContentControl Header="Project setup" Style="{StaticResource GroupHeaderPanel}">
                            <StackPanel>
                                <Button Content="🔑 Setup Usage Keys" HorizontalAlignment="Left" Padding="10,5"
                                        Command="{Binding SetupUsageKeysCommand}"/>
                                <TextBlock Foreground="{StaticResource Brush.TextMuted}" FontSize="10" Margin="0,8,0,0"
                                           TextWrapping="Wrap">
                                    Creates the RZ usage key schedules and area text parameters in this project.
                                    Idempotent — safe to re-run.
                                </TextBlock>
                            </StackPanel>
                        </HeaderedContentControl>

                        <TextBlock Foreground="{StaticResource Brush.TextMuted}" FontSize="10" Margin="0,4,0,0"
                                   TextWrapping="Wrap">
                            Writes «base».dxf + «base».dat. Export the DWFX sheets from Revit separately.
                            Press Refresh to list the open sheet's areas and check for errors before exporting.
                        </TextBlock>
                    </StackPanel>
                </ScrollViewer>
            </TabItem>

            <!-- ── AREAS TAB ────────────────────────────────────────────── -->
            <TabItem>
                <TabItem.Header>
                    <StackPanel Orientation="Horizontal">
                        <TextBlock Text="Areas"/>
                        <Border Background="{StaticResource Brush.ErrorFill}" CornerRadius="8" Padding="5,0" Margin="5,0,0,0"
                                Visibility="{Binding FlaggedCount, Converter={StaticResource CountVis}}">
                            <TextBlock Text="{Binding FlaggedCount}" FontSize="9" Foreground="{StaticResource Brush.Warning}"/>
                        </Border>
                    </StackPanel>
                </TabItem.Header>

                <DockPanel>
                    <Border DockPanel.Dock="Top" Background="{StaticResource Brush.ErrorFill}"
                            BorderBrush="{StaticResource Brush.Warning}" BorderThickness="1" CornerRadius="2"
                            Margin="10,10,10,0" Padding="8,6"
                            Visibility="{Binding FlaggedCount, Converter={StaticResource CountVis}}">
                        <TextBlock Foreground="{StaticResource Brush.Text}" FontSize="11" TextWrapping="Wrap">
                            <Run Text="{Binding FlaggedCount, Mode=OneWay}"/><Run Text=" of "/><Run Text="{Binding TotalAreaCount, Mode=OneWay}"/><Run Text=" areas flagged — check before exporting"/>
                        </TextBlock>
                    </Border>

                    <TreeView x:Name="AreaTree"
                              ItemsSource="{Binding Levels}"
                              SelectedItemChanged="AreaTree_SelectedItemChanged"
                              BorderThickness="0" Background="{StaticResource Brush.Bg}" Margin="4,6,4,4">
                        <TreeView.Resources>
                            <HierarchicalDataTemplate DataType="{x:Type vm:AreaLevelGroupViewModel}" ItemsSource="{Binding Rows}">
                                <StackPanel Orientation="Horizontal" Margin="0,4,0,2">
                                    <TextBlock Text="{Binding Header}" FontWeight="SemiBold"/>
                                    <TextBlock Text="  ⚠" Foreground="{StaticResource Brush.Warning}"
                                               Visibility="{Binding HasError, Converter={StaticResource BoolVis}}"/>
                                </StackPanel>
                            </HierarchicalDataTemplate>
                            <DataTemplate DataType="{x:Type vm:AreaRowViewModel}">
                                <Grid Margin="10,2,4,2" ToolTip="{Binding ErrorSummary}">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="*"/>
                                        <ColumnDefinition Width="Auto"/>
                                    </Grid.ColumnDefinitions>
                                    <Grid.Style>
                                        <Style TargetType="Grid">
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding HasError}" Value="True">
                                                    <Setter Property="Background" Value="{StaticResource Brush.ErrorFill}"/>
                                                </DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </Grid.Style>
                                    <TextBlock Grid.Column="0" Text="{Binding NumberAndName}" Margin="0,2">
                                        <TextBlock.Style>
                                            <Style TargetType="TextBlock">
                                                <Setter Property="Foreground" Value="{StaticResource Brush.Text}"/>
                                                <Style.Triggers>
                                                    <DataTrigger Binding="{Binding HasError}" Value="True">
                                                        <Setter Property="Foreground" Value="{StaticResource Brush.Warning}"/>
                                                        <Setter Property="FontWeight" Value="SemiBold"/>
                                                    </DataTrigger>
                                                </Style.Triggers>
                                            </Style>
                                        </TextBlock.Style>
                                    </TextBlock>
                                    <Border Grid.Column="1" Background="{StaticResource Brush.Control}"
                                            BorderBrush="{StaticResource Brush.Border}" BorderThickness="1"
                                            CornerRadius="8" Padding="6,1" VerticalAlignment="Center">
                                        <Border.Style>
                                            <Style TargetType="Border">
                                                <Style.Triggers>
                                                    <DataTrigger Binding="{Binding HasError}" Value="True">
                                                        <Setter Property="BorderBrush" Value="{StaticResource Brush.Warning}"/>
                                                    </DataTrigger>
                                                </Style.Triggers>
                                            </Style>
                                        </Border.Style>
                                        <TextBlock Text="{Binding Tag}" FontSize="9">
                                            <TextBlock.Style>
                                                <Style TargetType="TextBlock">
                                                    <Setter Property="Foreground" Value="{StaticResource Brush.TextMuted}"/>
                                                    <Style.Triggers>
                                                        <DataTrigger Binding="{Binding HasError}" Value="True">
                                                            <Setter Property="Foreground" Value="{StaticResource Brush.Warning}"/>
                                                        </DataTrigger>
                                                    </Style.Triggers>
                                                </Style>
                                            </TextBlock.Style>
                                        </TextBlock>
                                    </Border>
                                </Grid>
                            </DataTemplate>
                        </TreeView.Resources>
                    </TreeView>
                </DockPanel>
            </TabItem>

        </TabControl>
    </Grid>
</Window>
```

- [ ] **Step 4: Build to verify**

```powershell
dotnet build src\LibraryBrowser\RVTuk.UI\RVTuk.UI.csproj -c Release2024
```
Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add src/LibraryBrowser/RVTuk.UI/ViewModels/AreaRowViewModel.cs src/LibraryBrowser/RVTuk.UI/Converters/CountToVisibilityConverter.cs src/LibraryBrowser/RVTuk.UI/Views/AreaSubmissionWindow.xaml
git commit -m "Restyle the Areas tab with tag rows, a flagged-count badge, and a summary banner"
```

---

## Task 6: Full-solution build and manual in-Revit verification

**Files:** none (verification only).

**Interfaces:** none — this task only exercises what Tasks 1–5 produced.

- [ ] **Step 1: Build both solution configurations**

From the `RVTuk` folder, in Windows PowerShell:
```powershell
dotnet build RVTuk.sln -c Release2024
dotnet build RVTuk.sln -c Release2025
```
Expected: `Build succeeded.` for both — this also rebuilds `RVTuk.Revit`, confirming nothing in the Revit-facing project broke from the `RVTuk.UI` changes.

- [ ] **Step 2: Deploy and open Revit**

From an elevated (Administrator) PowerShell, from the `RVTuk` folder:
```powershell
.\Deploy.ps1
```
Then start Revit (2024 or 2025) and open a project with at least one sheet hosting Area Plan view(s) with placed Areas (per `docs/superpowers/specs/2026-07-01-rishui-zamin-area-submission-design.md`).

- [ ] **Step 3: Manually verify the redesigned window**

Open the Area Calc window from the RVTuk ribbon and check each item:

- Window opens on the **Settings** tab by default; toolbar shows only **Refresh** and **Export** (no more separate Config/Usage Keys buttons in the toolbar).
- Clear the **Output folder** field: its border/background turn red, its label turns red, and the **Settings** tab header grows a red "!" badge. Clear the **File base name** field too: same treatment, badge stays (doesn't double up oddly).
- Set **Building number** to `0` or blank, and **Scale** to `0` or blank: same red field treatment; restoring a valid value clears that field's red state (and clears the tab badge once every field is valid again).
- Click **🔑 Setup Usage Keys** inside the **Project setup** group (not the toolbar) — it still runs the existing setup flow and reports success/failure the same way as before.
- Switch to the **Areas** tab, click **Refresh**: areas load grouped by level with counts; if any are flagged, an amber-red summary banner reads "N of M areas flagged — check before exporting", the **Areas** tab header shows a matching count badge, and flagged rows show a tinted red background with their tag (usage code, or "⚠ no code") in red — matching the reference mockup at `RVTuk/.superpowers/brainstorm/2032-1783240487/content/interactive-prototype-v2.html`.
- Click a flagged and an unflagged row: both select the corresponding Area element in the Revit model (unchanged behavior).
- With Settings incomplete, click **Export**: still blocked with the existing message (behavior unchanged, only the pre-check visibility is new).
- Fill in Settings correctly and Export with clean areas loaded: still succeeds and writes the `.dxf`/`.dat` files as before.
- Open every other RVTuk window (Comparator, Family Browser, Settings, Config, Instructions Editor, Index Progress) from the ribbon and confirm each renders correctly with the new charcoal/blue palette — no leftover orange, no unreadable text, no window that still looks like the old VSCode theme.

- [ ] **Step 4: Report results**

If every check in Step 3 passes, the redesign is complete. If anything fails, note exactly which check and what was observed (screenshot if possible) before filing a follow-up fix — do not mark this task done on a partial pass.

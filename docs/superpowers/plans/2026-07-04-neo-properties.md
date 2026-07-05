# Neo Properties Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a read-only dockable pane, "Neo Properties," that mirrors the selected element's
parameters like the native Properties palette, but with pinned parameters shown first and
remaining groups in a fixed custom order.

**Architecture:** A pure, unit-tested ordering function lives in `RVTuk.Core` (plain strings in,
plain strings out — no Revit types, so it's testable in `tests\RVTuk.Core.Tests` without a live
Revit session). `RVTuk.Revit` extracts `(Group, Name, Value)` triples from the selected
`Element`'s parameters via `Element.GetOrderedParameters()`, calls the Core orderer, and pushes
the result into a `RVTuk.UI` view-model bound by a `UserControl` hosted in a Revit
`IDockablePaneProvider` pane. Everything runs synchronously on the Revit UI thread — reading an
element's parameters is cheap enough that no `ExternalEvent`/background-thread hop is needed for
this read-only pane (see `docs/superpowers/specs/2026-07-04-neo-properties-design.md`).

**Tech Stack:** C#, WPF (net48 / net8.0-windows depending on config), Revit API
(`Autodesk.Revit.UI.IDockablePaneProvider`, `UIControlledApplication.SelectionChanged`), xunit.

## Global Constraints

- Read-only. No parameter writes anywhere in this feature.
- Single-element selection only for v1 — 0 or 2+ selected shows a placeholder message, per the
  approved design.
- Pinned parameter names and group order are hardcoded placeholders for this test, not
  user-configurable.
- `RVTuk.Core` must stay free of Revit API and WPF types (existing architecture rule in
  `CLAUDE.md`); only `RVTuk.Revit` may reference `Autodesk.Revit.*`.
- Build against both `Release2024` and `Release2025` configs before considering a task done,
  since `RVTuk.Core`/`RVTuk.UI` multi-target and `RVTuk.Revit` differs per config.
- Branch: work happens on `neo-properties` (already checked out and pushed with tracking to
  `origin/neo-properties`).

---

### Task 1: Core — ParameterEntry/ParameterGroupView types + ParameterOrderer

**Files:**
- Create: `src/RVTuk.Core/NeoProperties/ParameterEntry.cs`
- Create: `src/RVTuk.Core/NeoProperties/ParameterGroupView.cs`
- Create: `src/RVTuk.Core/NeoProperties/ParameterOrderer.cs`
- Test: `tests/RVTuk.Core.Tests/NeoProperties/ParameterOrdererTests.cs`

**Interfaces:**
- Produces: `ParameterEntry(string Group, string Name, string Value)` (record), used by Task 5
  (Revit extraction) as the input shape and by Task 2 (UI view-model) as the display shape.
- Produces: `ParameterGroupView(string GroupName, IReadOnlyList<ParameterEntry> Parameters)`
  (record) — the ordered output shape, one per rendered group header, consumed by Task 2's
  view-model and Task 3's XAML `ItemsControl` binding.
- Produces: `ParameterOrderer.PinnedParameterNames` (`IReadOnlyList<string>`),
  `ParameterOrderer.GroupOrder` (`IReadOnlyList<string>`), and
  `ParameterOrderer.Order(IReadOnlyList<ParameterEntry> parameters) : IReadOnlyList<ParameterGroupView>`
  — consumed by Task 5.

**Ordering behavior (exact, so the test in Step 1 is unambiguous):**
1. `PinnedParameterNames = ["Mark", "Comments", "Family and Type"]`, in this priority order.
2. `GroupOrder = ["Identity Data", "Constraints", "Dimensions"]`, in this priority order.
3. Walk `PinnedParameterNames` in order; for each name, find the **first** matching entry in
   the input (by `Name`, case-sensitive exact match) that hasn't already been consumed. If
   found, remove it from further consideration and collect it. If none of the pinned names are
   present, skip the "Pinned" group entirely (don't emit an empty group).
4. If any pinned entries were collected, emit them as the first `ParameterGroupView` with
   `GroupName == "Pinned"`, in `PinnedParameterNames` order.
5. Of the remaining (non-pinned) entries, group them by `Group`, preserving each entry's
   original relative order within its group.
6. Emit groups in `GroupOrder` order first (only groups that actually have remaining entries),
   then any other groups in the order their first entry originally appeared in the input list.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Linq;
using RVTuk.Core.NeoProperties;
using Xunit;

namespace RVTuk.Core.Tests.NeoProperties;

public class ParameterOrdererTests
{
    [Fact]
    public void PinnedParametersAppearFirstInPinnedOrder()
    {
        var input = new[]
        {
            new ParameterEntry("Identity Data", "Comments", "hello"),
            new ParameterEntry("Constraints", "Mark", "A-101"),
            new ParameterEntry("Identity Data", "Family and Type", "Wall: Basic"),
        };

        var result = ParameterOrderer.Order(input);

        Assert.Equal("Pinned", result[0].GroupName);
        Assert.Equal(new[] { "Mark", "Comments", "Family and Type" },
            result[0].Parameters.Select(p => p.Name));
    }

    [Fact]
    public void MissingPinnedParametersAreSkippedNotBlank()
    {
        var input = new[]
        {
            new ParameterEntry("Identity Data", "Comments", "hello"),
            new ParameterEntry("Other", "Length", "10 ft"),
        };

        var result = ParameterOrderer.Order(input);

        Assert.Equal(new[] { "Comments" }, result[0].Parameters.Select(p => p.Name));
    }

    [Fact]
    public void NoPinnedParametersPresentSkipsPinnedGroupEntirely()
    {
        var input = new[]
        {
            new ParameterEntry("Other", "Length", "10 ft"),
        };

        var result = ParameterOrderer.Order(input);

        Assert.DoesNotContain(result, g => g.GroupName == "Pinned");
    }

    [Fact]
    public void RemainingGroupsFollowFixedGroupOrder()
    {
        var input = new[]
        {
            new ParameterEntry("Dimensions", "Length", "10 ft"),
            new ParameterEntry("Constraints", "Level", "Level 1"),
            new ParameterEntry("Identity Data", "Description", "desc"),
        };

        var result = ParameterOrderer.Order(input);

        Assert.Equal(new[] { "Identity Data", "Constraints", "Dimensions" },
            result.Select(g => g.GroupName));
    }

    [Fact]
    public void GroupsOutsideFixedOrderAppendInFirstSeenOrder()
    {
        var input = new[]
        {
            new ParameterEntry("Structural", "Rebar Cover", "25 mm"),
            new ParameterEntry("Identity Data", "Description", "desc"),
            new ParameterEntry("Electrical", "Voltage", "230V"),
        };

        var result = ParameterOrderer.Order(input);

        Assert.Equal(new[] { "Identity Data", "Structural", "Electrical" },
            result.Select(g => g.GroupName));
    }

    [Fact]
    public void IntraGroupOrderIsPreserved()
    {
        var input = new[]
        {
            new ParameterEntry("Identity Data", "Description", "desc"),
            new ParameterEntry("Identity Data", "Assembly Code", "A1010"),
        };

        var result = ParameterOrderer.Order(input);

        var idGroup = result.Single(g => g.GroupName == "Identity Data");
        Assert.Equal(new[] { "Description", "Assembly Code" },
            idGroup.Parameters.Select(p => p.Name));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj --filter "FullyQualifiedName~ParameterOrdererTests"`
Expected: FAIL to compile — `RVTuk.Core.NeoProperties` namespace/types don't exist yet.

- [ ] **Step 3: Create the record types**

`src/RVTuk.Core/NeoProperties/ParameterEntry.cs`:
```csharp
namespace RVTuk.Core.NeoProperties
{
    /// <summary>One parameter's display data, as read from the Revit element (or a test double).</summary>
    public record ParameterEntry(string Group, string Name, string Value);
}
```

`src/RVTuk.Core/NeoProperties/ParameterGroupView.cs`:
```csharp
using System.Collections.Generic;

namespace RVTuk.Core.NeoProperties
{
    /// <summary>One rendered group header plus its parameters, in display order.</summary>
    public record ParameterGroupView(string GroupName, IReadOnlyList<ParameterEntry> Parameters);
}
```

- [ ] **Step 4: Implement ParameterOrderer**

`src/RVTuk.Core/NeoProperties/ParameterOrderer.cs`:
```csharp
using System.Collections.Generic;
using System.Linq;

namespace RVTuk.Core.NeoProperties
{
    /// <summary>
    /// Reorders a flat parameter list into pinned-first, then a fixed group order — a
    /// placeholder ordering used to validate the Neo Properties pane mechanism. Swap
    /// <see cref="PinnedParameterNames"/> / <see cref="GroupOrder"/> for real values later.
    /// </summary>
    public static class ParameterOrderer
    {
        public static readonly IReadOnlyList<string> PinnedParameterNames =
            new[] { "Mark", "Comments", "Family and Type" };

        public static readonly IReadOnlyList<string> GroupOrder =
            new[] { "Identity Data", "Constraints", "Dimensions" };

        public static IReadOnlyList<ParameterGroupView> Order(IReadOnlyList<ParameterEntry> parameters)
        {
            var remaining = new List<ParameterEntry>(parameters);
            var result = new List<ParameterGroupView>();

            var pinned = new List<ParameterEntry>();
            foreach (var pinnedName in PinnedParameterNames)
            {
                var index = remaining.FindIndex(p => p.Name == pinnedName);
                if (index < 0) continue;
                pinned.Add(remaining[index]);
                remaining.RemoveAt(index);
            }
            if (pinned.Count > 0)
                result.Add(new ParameterGroupView("Pinned", pinned));

            var groupsInFirstSeenOrder = remaining
                .Select(p => p.Group)
                .Distinct()
                .ToList();

            var orderedGroupNames = GroupOrder
                .Where(g => groupsInFirstSeenOrder.Contains(g))
                .Concat(groupsInFirstSeenOrder.Where(g => !GroupOrder.Contains(g)))
                .ToList();

            foreach (var groupName in orderedGroupNames)
            {
                var entries = remaining.Where(p => p.Group == groupName).ToList();
                result.Add(new ParameterGroupView(groupName, entries));
            }

            return result;
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj --filter "FullyQualifiedName~ParameterOrdererTests"`
Expected: PASS (6 tests).

- [ ] **Step 6: Commit**

```bash
git add src/RVTuk.Core/NeoProperties tests/RVTuk.Core.Tests/NeoProperties
git commit -m "feat(neo-properties): add pinned/group parameter ordering (Core, unit-tested)"
```

---

### Task 2: UI — NeoPropertiesViewModel

**Files:**
- Create: `src/LibraryBrowser/RVTuk.UI/ViewModels/NeoPropertiesViewModel.cs`

**Interfaces:**
- Consumes: `RVTuk.Core.NeoProperties.ParameterGroupView` (Task 1).
- Produces: `NeoPropertiesViewModel : ViewModelBase` with public properties
  `IReadOnlyList<ParameterGroupView> Groups`, `string StatusMessage`, `bool HasGroups`,
  `bool HasNoGroups`, and methods `ShowNoSelection()`, `ShowMultipleSelection()`,
  `ShowParameters(IReadOnlyList<ParameterGroupView> groups)` — consumed by Task 3 (XAML binding)
  and Task 5 (Revit selection handler calls these methods).

No unit test here: this view-model only has trivial property assignment (no branching logic
worth a xunit test), and `RVTuk.UI` isn't part of the automated test suite (WPF projects only
build on Windows per `CLAUDE.md`) — it's covered by the manual in-Revit test in Task 7.

- [ ] **Step 1: Implement the view-model**

```csharp
using System.Collections.Generic;
using RVTuk.Core.NeoProperties;

namespace RVTuk.UI.ViewModels
{
    public class NeoPropertiesViewModel : ViewModelBase
    {
        private IReadOnlyList<ParameterGroupView> _groups = new List<ParameterGroupView>();
        public IReadOnlyList<ParameterGroupView> Groups
        {
            get => _groups;
            private set
            {
                SetProperty(ref _groups, value);
                OnPropertyChanged(nameof(HasGroups));
                OnPropertyChanged(nameof(HasNoGroups));
            }
        }

        private string _statusMessage = "No element selected";
        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        public bool HasGroups => Groups.Count > 0;
        public bool HasNoGroups => !HasGroups;

        public void ShowNoSelection()
        {
            Groups = new List<ParameterGroupView>();
            StatusMessage = "No element selected";
        }

        public void ShowMultipleSelection()
        {
            Groups = new List<ParameterGroupView>();
            StatusMessage = "Select a single element";
        }

        public void ShowParameters(IReadOnlyList<ParameterGroupView> groups)
        {
            Groups = groups;
            StatusMessage = groups.Count == 0 ? "No parameters" : "";
        }
    }
}
```

- [ ] **Step 2: Build RVTuk.UI to verify it compiles**

Run: `dotnet build src/LibraryBrowser/RVTuk.UI/RVTuk.UI.csproj -c Release2025`
Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```bash
git add src/LibraryBrowser/RVTuk.UI/ViewModels/NeoPropertiesViewModel.cs
git commit -m "feat(neo-properties): add NeoPropertiesViewModel"
```

---

### Task 3: UI — NeoPropertiesView (UserControl)

**Files:**
- Create: `src/LibraryBrowser/RVTuk.UI/Views/NeoPropertiesView.xaml`
- Create: `src/LibraryBrowser/RVTuk.UI/Views/NeoPropertiesView.xaml.cs`

**Interfaces:**
- Consumes: `NeoPropertiesViewModel` (Task 2) as its `DataContext` — set by the caller (Task 6),
  not by this control itself.
- Produces: `NeoPropertiesView : UserControl` (parameterless constructor) — consumed by Task 4
  (`NeoPropertiesPaneProvider` hosts an instance as `DockablePaneProviderData.FrameworkElement`).

- [ ] **Step 1: Create the XAML**

`src/LibraryBrowser/RVTuk.UI/Views/NeoPropertiesView.xaml`:
```xml
<UserControl x:Class="RVTuk.UI.Views.NeoPropertiesView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <UserControl.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="/RVTuk.UI;component/Themes/DarkTheme.xaml"/>
            </ResourceDictionary.MergedDictionaries>
            <BooleanToVisibilityConverter x:Key="BoolVis"/>
        </ResourceDictionary>
    </UserControl.Resources>

    <Grid Background="{StaticResource Brush.Bg}">
        <TextBlock Text="{Binding StatusMessage}"
                   Foreground="{StaticResource Brush.TextMuted}"
                   FontSize="12" Margin="12"
                   Visibility="{Binding HasNoGroups, Converter={StaticResource BoolVis}}"
                   TextWrapping="Wrap"/>

        <ScrollViewer VerticalScrollBarVisibility="Auto"
                      Visibility="{Binding HasGroups, Converter={StaticResource BoolVis}}">
            <ItemsControl ItemsSource="{Binding Groups}" Margin="8">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <StackPanel Margin="0,0,0,10">
                            <TextBlock Text="{Binding GroupName}"
                                       Foreground="{StaticResource Brush.TextMuted}"
                                       FontSize="10" FontWeight="SemiBold"
                                       Margin="4,8,4,4"/>
                            <ItemsControl ItemsSource="{Binding Parameters}">
                                <ItemsControl.ItemTemplate>
                                    <DataTemplate>
                                        <Grid Margin="4,2">
                                            <Grid.ColumnDefinitions>
                                                <ColumnDefinition Width="*"/>
                                                <ColumnDefinition Width="*"/>
                                            </Grid.ColumnDefinitions>
                                            <TextBlock Grid.Column="0" Text="{Binding Name}"
                                                       Foreground="{StaticResource Brush.Text}"
                                                       FontSize="12" TextWrapping="Wrap"/>
                                            <TextBlock Grid.Column="1" Text="{Binding Value}"
                                                       Foreground="{StaticResource Brush.TextMuted}"
                                                       FontSize="12" TextWrapping="Wrap"/>
                                        </Grid>
                                    </DataTemplate>
                                </ItemsControl.ItemTemplate>
                            </ItemsControl>
                        </StackPanel>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </ScrollViewer>
    </Grid>
</UserControl>
```

The placeholder `TextBlock` binds to `HasNoGroups` and the `ScrollViewer` binds to `HasGroups` —
both use the same stock `BooleanToVisibilityConverter`, no inversion logic needed since Task 2
already exposes both properties directly.

- [ ] **Step 2: Create the code-behind**

`src/LibraryBrowser/RVTuk.UI/Views/NeoPropertiesView.xaml.cs`:
```csharp
using System.Windows.Controls;

namespace RVTuk.UI.Views
{
    public partial class NeoPropertiesView : UserControl
    {
        public NeoPropertiesView()
        {
            InitializeComponent();
        }
    }
}
```

- [ ] **Step 4: Build RVTuk.UI to verify the XAML compiles**

Run: `dotnet build src/LibraryBrowser/RVTuk.UI/RVTuk.UI.csproj -c Release2025`
Expected: `Build succeeded`.

- [ ] **Step 5: Commit**

```bash
git add src/LibraryBrowser/RVTuk.UI/Views/NeoPropertiesView.xaml src/LibraryBrowser/RVTuk.UI/Views/NeoPropertiesView.xaml.cs src/LibraryBrowser/RVTuk.UI/ViewModels/NeoPropertiesViewModel.cs
git commit -m "feat(neo-properties): add NeoPropertiesView UserControl"
```

---

### Task 4: Revit — NeoPropertiesPaneProvider

**Files:**
- Create: `src/RVTuk.Revit/NeoProperties/NeoPropertiesPaneProvider.cs`

**Interfaces:**
- Consumes: `RVTuk.UI.Views.NeoPropertiesView` (Task 3) — an instance is passed into the
  constructor by the caller (Task 6).
- Produces: `NeoPropertiesPaneProvider : IDockablePaneProvider` and the static
  `NeoPropertiesPaneProvider.PaneId : DockablePaneId` — consumed by Task 5's ribbon command
  (`GetDockablePane(PaneId).Show()`) and Task 6's `RegisterDockablePane` call.

- [ ] **Step 1: Implement the provider**

```csharp
using System;
using Autodesk.Revit.UI;
using RVTuk.UI.Views;

namespace RVTuk.Revit.NeoProperties
{
    public class NeoPropertiesPaneProvider : IDockablePaneProvider
    {
        public static readonly DockablePaneId PaneId =
            new DockablePaneId(new Guid("88a9c4fa-9c6a-4b02-b169-701be8358090"));

        private readonly NeoPropertiesView _view;

        public NeoPropertiesPaneProvider(NeoPropertiesView view)
        {
            _view = view;
        }

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            data.FrameworkElement = _view;
            data.InitialState = new DockablePaneState
            {
                DockPosition = DockPosition.Tabbed,
                TabBehind = DockablePanes.BuiltInDockablePanes.PropertiesPalette
            };
        }
    }
}
```

- [ ] **Step 2: Build RVTuk.Revit to verify it compiles**

Run: `dotnet build src/RVTuk.Revit/RVTuk.Revit.csproj -c Release2025`
Expected: `Build succeeded`. (This links against the Nice3point Revit API reference assemblies,
which don't require Revit installed — see `nice3point.revit.api.revitapiui` in the NuGet cache.)

- [ ] **Step 3: Commit**

```bash
git add src/RVTuk.Revit/NeoProperties/NeoPropertiesPaneProvider.cs
git commit -m "feat(neo-properties): add NeoPropertiesPaneProvider"
```

---

### Task 5: Revit — NeoPropertiesSelectionHandler

**Files:**
- Create: `src/RVTuk.Revit/NeoProperties/NeoPropertiesSelectionHandler.cs`

**Interfaces:**
- Consumes: `RVTuk.Core.NeoProperties.ParameterEntry` / `ParameterOrderer.Order` (Task 1),
  `RVTuk.UI.ViewModels.NeoPropertiesViewModel` (Task 2) via a static settable
  `NeoPropertiesSelectionHandler.ViewModel` property.
- Produces: `NeoPropertiesSelectionHandler.OnSelectionChanged(object? sender, SelectionChangedEventArgs e)`
  — a `static` method matching `EventHandler<SelectionChangedEventArgs>`, consumed by Task 6's
  `application.SelectionChanged += ...` wiring in `Application.OnStartup`.

- [ ] **Step 1: Implement the handler**

```csharp
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Events;
using RVTuk.Core.NeoProperties;
using RVTuk.UI.ViewModels;

namespace RVTuk.Revit.NeoProperties
{
    public static class NeoPropertiesSelectionHandler
    {
        public static NeoPropertiesViewModel? ViewModel { get; set; }

        public static void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            var vm = ViewModel;
            if (vm == null) return;

            var ids = e.GetSelectedElements();
            if (ids.Count == 0)
            {
                vm.ShowNoSelection();
                return;
            }
            if (ids.Count > 1)
            {
                vm.ShowMultipleSelection();
                return;
            }

            var document = e.GetDocument();
            var element = document.GetElement(ids.First());
            if (element == null)
            {
                vm.ShowNoSelection();
                return;
            }

            var entries = ExtractParameterEntries(element);
            vm.ShowParameters(ParameterOrderer.Order(entries));
        }

        private static IReadOnlyList<ParameterEntry> ExtractParameterEntries(Element element)
        {
            var entries = new List<ParameterEntry>();
            foreach (Parameter parameter in element.GetOrderedParameters())
            {
                try
                {
                    var groupName = LabelUtils.GetLabelForGroup(parameter.Definition.GetGroupTypeId());
                    var name = parameter.Definition.Name;
                    var value = parameter.AsValueString() ?? parameter.AsString() ?? "";
                    entries.Add(new ParameterEntry(groupName, name, value));
                }
                catch
                {
                    // A malformed/inaccessible parameter shouldn't blank the whole pane.
                }
            }
            return entries;
        }
    }
}
```

- [ ] **Step 2: Build RVTuk.Revit to verify it compiles**

Run: `dotnet build src/RVTuk.Revit/RVTuk.Revit.csproj -c Release2025`
Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```bash
git add src/RVTuk.Revit/NeoProperties/NeoPropertiesSelectionHandler.cs
git commit -m "feat(neo-properties): add selection-changed handler with parameter extraction"
```

---

### Task 6: Revit — NeoPropertiesCommand + wire into Application.cs + ribbon button

**Files:**
- Create: `src/RVTuk.Revit/Commands/NeoPropertiesCommand.cs`
- Modify: `src/RVTuk.Revit/Application.cs`

**Interfaces:**
- Consumes: `NeoPropertiesPaneProvider.PaneId` (Task 4), `NeoPropertiesSelectionHandler` (Task 5),
  `NeoPropertiesViewModel` (Task 2), `NeoPropertiesView` (Task 3).
- Produces: registered dockable pane + ribbon button; nothing further consumes this — it's the
  wiring task.

- [ ] **Step 1: Create the ribbon command**

`src/RVTuk.Revit/Commands/NeoPropertiesCommand.cs`:
```csharp
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RVTuk.Revit.NeoProperties;

namespace RVTuk.Revit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class NeoPropertiesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var pane = commandData.Application.GetDockablePane(NeoPropertiesPaneProvider.PaneId);
            pane.Show();
            return Result.Succeeded;
        }
    }
}
```

- [ ] **Step 2: Add the static view-model holder and register the pane in OnStartup**

In `src/RVTuk.Revit/Application.cs`, add near the other static properties (after line 37,
`public static RVTuk.UI.Views.AreaSubmissionWindow? AreaCalcWindow { get; set; }`):
```csharp
        public static RVTuk.UI.ViewModels.NeoPropertiesViewModel NeoPropertiesViewModel { get; private set; } = null!;
```

Add `using RVTuk.Revit.NeoProperties;` to the top of the file, alongside the existing
`using RVTuk.Revit.ExternalEvents;`.

In `OnStartup`, right after the existing handler-creation block (after
`SetupUsageKeysEvent   = ExternalEvent.Create(SetupUsageKeysHandler);` and before the
`try { CreateRibbon(application); }` block), add:
```csharp
            NeoPropertiesViewModel = new RVTuk.UI.ViewModels.NeoPropertiesViewModel();
            NeoPropertiesSelectionHandler.ViewModel = NeoPropertiesViewModel;
            application.SelectionChanged += NeoPropertiesSelectionHandler.OnSelectionChanged;

            var neoView = new RVTuk.UI.Views.NeoPropertiesView { DataContext = NeoPropertiesViewModel };
            application.RegisterDockablePane(
                NeoPropertiesPaneProvider.PaneId,
                "Neo Properties",
                new NeoPropertiesPaneProvider(neoView));
```

- [ ] **Step 3: Add the ribbon button**

In `CreateRibbon`, after the existing `panel.AddItem(areaBtn);` line, add a new panel (same tab,
new panel, per the approved design) and its button:
```csharp
            RibbonPanel neoPanel = app.CreateRibbonPanel("Neo Properties");
            var neoBtn = new PushButtonData(
                "NeoProperties",
                "Neo\nProperties",
                assemblyPath,
                typeof(NeoPropertiesCommand).FullName!)
            {
                ToolTip = "Open the Neo Properties pane: same parameters as Properties, pinned/reordered"
            };
            neoBtn.LargeImage = CreateNeoPropertiesIcon(32);
            neoBtn.Image      = CreateNeoPropertiesIcon(16);

            neoPanel.AddItem(neoBtn);
```

- [ ] **Step 4: Add the icon method**

Add this method alongside the other `Create...Icon` methods (e.g. after `CreateAreaCalcIcon`):
```csharp
        private static BitmapSource CreateNeoPropertiesIcon(int size)
        {
            var dv = new DrawingVisual();
            using (var ctx = dv.RenderOpen())
            {
                double s = size;
                ctx.DrawRectangle(new SolidColorBrush(WpfColor.FromRgb(0x25, 0x25, 0x26)), null,
                    new Rect(0, 0, s, s));

                // Three horizontal rows (parameter list) with the top row highlighted (pinned).
                var pinned = new SolidColorBrush(WpfColor.FromRgb(0xFF, 0x8C, 0x00));
                var row = new SolidColorBrush(WpfColor.FromRgb(0xD4, 0xD4, 0xD4));
                double rowHeight = s * 0.14;
                double rowGap = s * 0.10;
                double x = s * 0.16;
                double width = s * 0.68;
                double y = s * 0.22;

                ctx.DrawRectangle(pinned, null, new Rect(x, y, width, rowHeight));
                y += rowHeight + rowGap;
                ctx.DrawRectangle(row, null, new Rect(x, y, width, rowHeight));
                y += rowHeight + rowGap;
                ctx.DrawRectangle(row, null, new Rect(x, y, width, rowHeight));
            }
            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            return bmp;
        }
```

- [ ] **Step 5: Build both configs to verify everything compiles**

Run: `dotnet build src/RVTuk.Revit/RVTuk.Revit.csproj -c Release2024`
Expected: `Build succeeded`.

Run: `dotnet build src/RVTuk.Revit/RVTuk.Revit.csproj -c Release2025`
Expected: `Build succeeded`.

- [ ] **Step 6: Commit**

```bash
git add src/RVTuk.Revit/Commands/NeoPropertiesCommand.cs src/RVTuk.Revit/Application.cs
git commit -m "feat(neo-properties): wire ribbon button, pane registration, and selection sync"
```

---

### Task 7: Full-solution build + manual in-Revit verification

**Files:** none created/modified — verification only.

- [ ] **Step 1: Run the full Core test suite**

Run: `dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj`
Expected: all tests pass, including the 6 new `ParameterOrdererTests`.

- [ ] **Step 2: Build the whole solution, both configs**

Run: `dotnet build RVTuk.sln -c Release2024`
Expected: `Build succeeded`.

Run: `dotnet build RVTuk.sln -c Release2025`
Expected: `Build succeeded`.

- [ ] **Step 3: Deploy and manually verify in Revit**

Run (elevated shell, from repo root): `.\Deploy.ps1`

Then in Revit:
1. Confirm the "Neo Properties" button appears in its own panel on the RVTuk ribbon tab.
2. Click it — confirm the pane opens, tabbed with (or near) the native Properties palette, and
   can be docked/floated/tabbed like Properties.
3. With nothing selected, confirm the pane shows "No element selected".
4. Select a single element that has `Mark` and/or `Comments` set — confirm those appear first,
   under a "Pinned" header, in `Mark`, `Comments`, `Family and Type` order (whichever are
   present), followed by "Identity Data" / "Constraints" / "Dimensions" groups (whichever are
   present) in that order, then any other groups.
5. Select an element missing some pinned parameters (e.g. no `Mark`) — confirm it's skipped
   cleanly, no blank row, no crash.
6. Select multiple elements — confirm the pane shows "Select a single element" and does not show
   stale data from the prior single selection.
7. Rapidly click through several different elements — confirm no visible lag or stutter.

- [ ] **Step 4: Update CLAUDE.md's feature list**

In `CLAUDE.md`, under "Current and planned features," add a line after the existing **Area
Calc** bullet:
```markdown
- **Neo Properties** — a dockable pane mirroring the selected element's parameters like the
  native Properties palette, but with pinned parameters shown first and remaining groups in a
  fixed custom order. Read-only, single-element only. See
  [`docs/superpowers/specs/2026-07-04-neo-properties-design.md`](docs/superpowers/specs/2026-07-04-neo-properties-design.md).
```

- [ ] **Step 5: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: add Neo Properties to CLAUDE.md feature list"
```

---

## Deferred / explicitly out of scope

Per the design spec's non-goals — do not implement in this plan:
- Editing parameter values.
- Multi-select aggregation.
- Per-category custom ordering.
- Config-file-driven or user-editable ordering.

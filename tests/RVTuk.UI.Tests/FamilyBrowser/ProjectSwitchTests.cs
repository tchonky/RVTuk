using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Threading;
using RVTuk.Core.FamilyBrowser.Database;
using RVTuk.Core.FamilyBrowser.Models;
using RVTuk.Core.Shared.Config;
using RVTuk.UI.FamilyBrowser.ViewModels;
using Xunit;

namespace RVTuk.UI.Tests.FamilyBrowser;

// The browser's comparison belongs to the project the last ⟳ looked at (Safe Update §4).
// Library: Doors\Door.rfa at _Version 5; project "A" holds Door at _Version 3 (outdated).
public class ProjectSwitchTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rvtuk_sw_" + Guid.NewGuid().ToString("N"));
    private readonly AppConfig _config;

    public ProjectSwitchTests()
    {
        _config = new AppConfig { LibraryFolderPath = _root };
        Directory.CreateDirectory(Path.GetDirectoryName(_config.DatabasePath)!);
        using var index = new IndexRepository(_config.DatabasePath);
        long id = index.InsertFamily(@"Doors\Door.rfa", "Door.rfa");
        index.UpdateFamilyMetadata(id, "Doors", new List<ParameterModel>(), null, familyVersion: "5");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* best effort */ }
    }

    private static ProjectSnapshot ProjectA() => new()
    {
        DocumentKey = "A", Title = "A.rvt",
        Families = new[] { new ProjectFamilyInfo { Name = "Door", Version = "3" } },
    };

    private FamilyBrowserViewModel NewVm(
        Func<ProjectSnapshot>? getProject = null,
        Func<string, string?, (bool, string?)>? load = null)
        => new(_config, new BrowserRepository(_config.DatabasePath),
            getProject ?? ProjectA,
            load ?? ((_, _) => (true, null)),
            (_, _) => (true, null),
            (_, _) => { },
            _ => (true, null), _ => (true, null), (_, _) => (true, null),
            _ => new Dictionary<string, byte[]>());

    private static void PumpUntil(Func<bool> done)
    {
        var sw = Stopwatch.StartNew();
        while (!done())
        {
            if (sw.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("condition not reached");
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(5);
        }
    }

    private static void Sync(FamilyBrowserViewModel vm)
    {
        vm.SyncCommand.Execute(null);
        PumpUntil(() => !vm.IsSyncing);
    }

    private static FamilyBrowserItemViewModel Door(FamilyBrowserViewModel vm)
        => vm.FilteredItems.Single(i => i.DisplayName == "Door");

    [Fact]
    public void SyncFinishingAfterASwitch_KeepsTheComparisonHidden() => Sta.Run(() =>
    {
        FamilyBrowserViewModel vm = null!;
        var ui = Dispatcher.CurrentDispatcher;
        vm = NewVm(getProject: () =>
        {
            var a = ProjectA();
            ui.Invoke(() => vm.ActiveDocumentChanged("B")); // user switches while ⟳ runs
            return a;
        });

        Sync(vm);

        Assert.True(vm.IsComparisonHidden);
        Assert.Equal(0, vm.OutdatedCount);
        Assert.Equal(VersionStatus.None, Door(vm).VersionStatus);
    });

    [Fact]
    public void UpdateAll_IsOff_WhileTheComparisonIsHidden() => Sta.Run(() =>
    {
        var vm = NewVm();
        Sync(vm);
        Assert.True(vm.UpdateAllCommand.CanExecute(null));

        vm.ActiveDocumentChanged("B");

        Assert.False(vm.UpdateAllCommand.CanExecute(null));
    });

    [Fact]
    public void RescanWhileHidden_DoesNotBringBackAVerdict() => Sta.Run(() =>
    {
        var vm = NewVm();
        Sync(vm);
        vm.SelectedItem = Door(vm);
        vm.ActiveDocumentChanged("B");

        vm.RescanFamilyCommand.Execute(null);
        PumpUntil(() => !vm.IsRescanning);

        Assert.Equal(VersionStatus.None, Door(vm).VersionStatus);
        Assert.Equal(0, vm.OutdatedCount);
    });

    [Fact]
    public void SingleLoadWhileHidden_ShowsNoVerdict_AndComingBackRestoresA() => Sta.Run(() =>
    {
        var vm = NewVm();
        Sync(vm);
        vm.SelectedItem = Door(vm);
        vm.ActiveDocumentChanged("B");

        vm.LoadFamilyCommand.Execute(null); // loads into B, not A
        PumpUntil(() => !vm.IsLoading);
        Assert.Equal(VersionStatus.None, Door(vm).VersionStatus);

        vm.ActiveDocumentChanged("A");
        Assert.Equal(VersionStatus.UpdateAvailable, Door(vm).VersionStatus);
    });

    [Fact]
    public void IgnoringAFolderAfterSync_DropsItsRowFromUpdateAll() => Sta.Run(() =>
    {
        var vm = NewVm();
        Sync(vm);
        Assert.Equal(1, vm.OutdatedCount);

        _config.IgnoredSubfolders.Add("Doors");
        vm.ActiveDocumentChanged("B");
        vm.ActiveDocumentChanged("A"); // restore recounts

        Assert.Equal(0, vm.OutdatedCount);
    });
}

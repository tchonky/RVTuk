using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Threading;
using RVTuk.Core.RishuiZamin;
using RVTuk.UI.RishuiZamin.ViewModels;
using Xunit;

namespace RVTuk.UI.Tests.RishuiZamin;

public class RishuiZaminViewModelTests
{
    private static (long Id, AreaRecord Rec) A(long id, int? code, int? old, double m2, string floor = "קומת קרקע Ground",
        AreaError errors = AreaError.None) =>
        (id, new AreaRecord { Number = id.ToString(), Name = "דירה " + id + " - Apt", UsageCode = code, UsageCodePrev = old,
            AreaValue = m2, Floor = floor, Errors = errors, Scale = 100 });

    private static readonly (long Id, AreaRecord Rec)[] Five =
    {
        A(1, 1, null, 84.30), A(2, 105, null, 22.00), A(3, 250, null, 10.00, "קומה 1 First"),
        A(4, 301, 1, 12.50, "קומה 1 First"), A(5, null, null, 5.25, errors: AreaError.NoUsageCode),
    };

    private sealed class Stubs
    {
        public int ExtractCalls;
        public IReadOnlyList<(long, AreaRecord)> Areas = Five;
        public readonly List<IReadOnlyList<long>> Selected = new();
        public readonly List<bool> AssignSetupFlags = new();
        public Func<bool, UsageAssignResult> AssignResult = _ => new UsageAssignResult(UsageAssignStatus.Done, "");
        public string DocKey = "A.rvt";
        public PlotLimits Stored = PlotLimits.Empty;
        public (bool ok, string msg) SaveResult = (true, "");
        public readonly List<PlotLimits> Saved = new();

        public RishuiZaminViewModel Vm(bool withAssign = true) => new RishuiZaminViewModel(
            () => { Interlocked.Increment(ref ExtractCalls); return Areas; },
            ids => Selected.Add(ids),
            (_, _) => (true, ""),
            null,
            withAssign
                ? (slot, code, ids, setup) => { lock (AssignSetupFlags) AssignSetupFlags.Add(setup); return AssignResult(setup); }
                : null,
            () => (DocKey, Stored),
            limits => { lock (Saved) Saved.Add(limits); return SaveResult; });
    }

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

    private static List<AreaRowViewModel> Rows(RishuiZaminViewModel vm) => vm.Levels.SelectMany(l => l.Rows).ToList();

    private static void Select(RishuiZaminViewModel vm, params long[] ids)
    {
        foreach (var r in Rows(vm)) r.IsSelected = ids.Contains(r.ElementId);
        vm.OnSelectionChanged();
    }

    [Fact]
    public void Populate_ComputesSheetTotals() => Sta.Run(() =>
    {
        var vm = new Stubs().Vm();
        vm.Populate(Five);
        Assert.Equal(106.30, vm.SheetTotals.Total, 9);
        Assert.True(vm.HasUncoded);
        Assert.Equal(2, vm.Levels.Count);
    });

    [Fact]
    public void OnSelectionChanged_MirrorsIdsToModel_AndSummarises() => Sta.Run(() =>
    {
        var s = new Stubs();
        var vm = s.Vm();
        vm.Populate(Five);
        Select(vm, 1, 3);
        Assert.Equal(new long[] { 1, 3 }, s.Selected.Last().OrderBy(i => i));
        Assert.Equal("2 areas selected", vm.Selection.Title);
        Assert.Equal(2, vm.SelectedRows.Count);
    });

    [Fact]
    public void Populate_RestoresSelectionById_DropsMissingIds() => Sta.Run(() =>
    {
        var vm = new Stubs().Vm();
        vm.Populate(Five);
        Select(vm, 1, 2);
        vm.Populate(new[] { Five[1], Five[2] });
        Assert.Equal(new long[] { 2 }, Rows(vm).Where(r => r.IsSelected).Select(r => r.ElementId));
        Assert.Single(vm.SelectedRows);
        Assert.Equal("2 — דירה 2 - Apt", vm.Selection.Title);
    });

    [Fact]
    public void Populate_FillsFlatRowsInListOrder_WithTheirLevel() => Sta.Run(() =>
    {
        var vm = new Stubs().Vm();
        vm.Populate(Five);
        Assert.Equal(Rows(vm).Select(r => r.ElementId), vm.AllRows.Select(r => r.ElementId));
        Assert.All(vm.AllRows, r => Assert.Contains(r, r.Level!.Rows));
    });

    [Fact]
    public void UsageDisplays_FollowSelection() => Sta.Run(() =>
    {
        var vm = new Stubs().Vm();
        vm.Populate(Five);
        Select(vm, 1);
        Assert.Equal("1 - מגורים", vm.NewUsageDisplay.Text);
        Assert.Equal("— none —", vm.OldUsageDisplay.Text);
        Select(vm, 1, 2);
        Assert.Equal("(mixed)", vm.NewUsageDisplay.Text);
    });

    [Fact]
    public void CanAssign_FalseWithoutDelegate_OrSelection() => Sta.Run(() =>
    {
        var vm = new Stubs().Vm();
        vm.Populate(Five);
        Assert.False(vm.CanAssign);
        Select(vm, 1);
        Assert.True(vm.CanAssign);

        var noDelegate = new Stubs().Vm(withAssign: false);
        noDelegate.Populate(Five);
        Select(noDelegate, 1);
        Assert.False(noDelegate.CanAssign);
    });

    [Fact]
    public void StatusText_Wording()
    {
        Assert.Equal("New usage 110 - מרפסת on 2 areas · one undo step in Revit",
            RishuiZaminViewModel.FormatAssignStatus(UsageKeySlot.New, 110, 2));
        Assert.Equal("Old usage 1 - מגורים on 1 area · one undo step in Revit",
            RishuiZaminViewModel.FormatAssignStatus(UsageKeySlot.Old, 1, 1));
        Assert.Equal("New usage cleared on 3 areas · one undo step in Revit",
            RishuiZaminViewModel.FormatAssignStatus(UsageKeySlot.New, null, 3));
    }

    [Fact]
    public void Assign_Done_SetsStatus_RefreshesAndKeepsSelection() => Sta.Run(() =>
    {
        var s = new Stubs();
        var vm = s.Vm();
        vm.Populate(Five);
        Select(vm, 1, 2);
        vm.Assign(UsageKeySlot.New, 110);
        PumpUntil(() => s.ExtractCalls == 1 && !vm.IsBusy);
        Assert.Equal("New usage 110 - מרפסת on 2 areas · one undo step in Revit", vm.StatusText);
        Assert.Equal(new long[] { 1, 2 }, Rows(vm).Where(r => r.IsSelected).Select(r => r.ElementId));
        Assert.Equal(new[] { false }, s.AssignSetupFlags);
    });

    [Fact]
    public void Assign_KeysMissing_AsksThenRunsSetup() => Sta.Run(() =>
    {
        var s = new Stubs
        {
            AssignResult = setup => setup
                ? new UsageAssignResult(UsageAssignStatus.Done, "")
                : new UsageAssignResult(UsageAssignStatus.KeysMissing, "no schedule"),
        };
        var vm = s.Vm();
        string? asked = null;
        vm.ConfirmRunSetup = msg => { asked = msg; return true; };
        vm.Populate(Five);
        Select(vm, 5);
        vm.Assign(UsageKeySlot.New, 1);
        PumpUntil(() => s.ExtractCalls == 1 && !vm.IsBusy);
        Assert.Equal("no schedule", asked);
        Assert.Equal(new[] { false, true }, s.AssignSetupFlags);
        Assert.Equal("New usage 1 - מגורים on 1 area · one undo step in Revit", vm.StatusText);
    });

    [Fact]
    public void Assign_KeysMissing_Declined_ChangesNothing() => Sta.Run(() =>
    {
        var s = new Stubs { AssignResult = _ => new UsageAssignResult(UsageAssignStatus.KeysMissing, "no schedule") };
        var vm = s.Vm();
        vm.ConfirmRunSetup = _ => false;
        vm.Populate(Five);
        Select(vm, 5);
        vm.Assign(UsageKeySlot.New, 1);
        PumpUntil(() => !vm.IsBusy);
        Assert.Equal(new[] { false }, s.AssignSetupFlags);
        Assert.Equal(0, s.ExtractCalls);
        Assert.Null(vm.StatusText);
    });

    [Fact]
    public void Assign_Failed_ClearsAnEarlierSuccessLine() => Sta.Run(() =>
    {
        var s = new Stubs();
        var vm = s.Vm();
        vm.Populate(Five);
        Select(vm, 1);
        vm.Assign(UsageKeySlot.New, 110);
        PumpUntil(() => s.ExtractCalls == 1 && !vm.IsBusy);
        Assert.NotNull(vm.StatusText);

        s.AssignResult = _ => new UsageAssignResult(UsageAssignStatus.Failed, "rolled back");
        vm.Assign(UsageKeySlot.New, 1);
        PumpUntil(() => s.ExtractCalls == 2 && !vm.IsBusy);
        Assert.Null(vm.StatusText);
    });

    [Fact]
    public void Populate_KeepsCollapsedFloorsCollapsed() => Sta.Run(() =>
    {
        var vm = new Stubs().Vm();
        vm.Populate(Five);
        vm.Levels.Single(l => l.LevelName == "קומה 1 First").IsExpanded = false;
        vm.Populate(Five);
        Assert.False(vm.Levels.Single(l => l.LevelName == "קומה 1 First").IsExpanded);
        Assert.True(vm.Levels.Single(l => l.LevelName == "קומת קרקע Ground").IsExpanded);
    });

    [Fact]
    public void Assign_Failed_ShowsMessageAndRefreshes() => Sta.Run(() =>
    {
        var s = new Stubs { AssignResult = _ => new UsageAssignResult(UsageAssignStatus.Failed, "Area 2 no longer exists") };
        var vm = s.Vm();
        string? failure = null;
        vm.AssignFailed += msg => failure = msg;
        vm.Populate(Five);
        Select(vm, 2);
        vm.Assign(UsageKeySlot.Old, 1);
        PumpUntil(() => s.ExtractCalls == 1 && !vm.IsBusy);
        Assert.Equal("Area 2 no longer exists", failure);
    });
    // ── Table tab ────────────────────────────────────────────────────────

    private static void RefreshAndWait(Stubs s, RishuiZaminViewModel vm)
    {
        var before = s.ExtractCalls;
        vm.RefreshCommand.Execute(null);
        PumpUntil(() => s.ExtractCalls == before + 1 && !vm.IsBusy);
    }

    private static void Commit(RishuiZaminViewModel vm)
    {
        vm.CommitPlotLimits();
        PumpUntil(() => !vm.IsBusy);
    }

    [Fact]
    public void Populate_BuildsTable() => Sta.Run(() =>
    {
        var vm = new Stubs().Vm();
        vm.Populate(Five);
        Assert.Equal(5, vm.Table.Rows.Count);
        Assert.Equal(2, vm.Table.Rows.Count(r => r.Kind == AreaTableRowKind.Floor));
    });

    [Fact]
    public void Refresh_LoadsStoredLimitsIntoBoxes() => Sta.Run(() =>
    {
        var s = new Stubs { Stored = new PlotLimits(2370, 35, 20, 55) };
        var vm = s.Vm();
        RefreshAndWait(s, vm);
        Assert.Equal(new[] { "2370", "35", "20", "55" },
            new[] { vm.PlotAreaBox.Text, vm.PrimaryLimitBox.Text, vm.ServiceLimitBox.Text, vm.TotalLimitBox.Text });
        Assert.Equal(3, vm.LimitChips.Count);
        Assert.Equal(2370, vm.PlotAreaM2);
        Assert.Null(vm.PlotHint);
    });

    [Fact]
    public void CommitLimits_Changed_SavesOnce_AndSaysSo() => Sta.Run(() =>
    {
        var s = new Stubs();
        var vm = s.Vm();
        RefreshAndWait(s, vm);
        Assert.Equal(PlotLimitCheck.NoPlotHint, vm.PlotHint);
        vm.PlotAreaBox.Text = "2370";
        Commit(vm);
        Assert.Single(s.Saved);
        Assert.Equal(2370, s.Saved[0].PlotAreaM2);
        Assert.Equal("Plot area and limits saved in the project · one undo step in Revit", vm.TableStatusText);
        Assert.False(vm.LimitsUnsaved);
    });

    [Fact]
    public void CommitLimits_Unchanged_DoesNotSave() => Sta.Run(() =>
    {
        var s = new Stubs { Stored = new PlotLimits(2370, 35, null, null) };
        var vm = s.Vm();
        RefreshAndWait(s, vm);
        Commit(vm);
        Assert.Empty(s.Saved);

        vm.ServiceLimitBox.Text = "20";
        Commit(vm);
        Commit(vm);
        Assert.Single(s.Saved);
    });

    [Fact]
    public void CommitLimits_Invalid_MarksBox_DoesNotSave() => Sta.Run(() =>
    {
        var s = new Stubs();
        var vm = s.Vm();
        RefreshAndWait(s, vm);
        vm.PrimaryLimitBox.Text = "35,5";
        Assert.True(vm.PrimaryLimitBox.IsInvalid);
        Commit(vm);
        Assert.Empty(s.Saved);
    });

    [Fact]
    public void CommitLimits_RecomputesChips() => Sta.Run(() =>
    {
        var s = new Stubs();
        var vm = s.Vm();
        RefreshAndWait(s, vm);
        vm.PlotAreaBox.Text = "500";
        vm.PrimaryLimitBox.Text = "35";
        Commit(vm);
        Assert.Equal(PlotLimitCheck.Chips(vm.Table, new PlotLimits(500, 35, null, null)), vm.LimitChips);
    });

    [Fact]
    public void FailedSave_SurvivesRefreshOfSameDocument() => Sta.Run(() =>
    {
        var s = new Stubs { SaveResult = (false, "borrowed") };
        var vm = s.Vm();
        RefreshAndWait(s, vm);
        vm.PlotAreaBox.Text = "2370";
        Commit(vm);
        Assert.True(vm.LimitsUnsaved);
        Assert.Equal("Not saved to the project: borrowed", vm.TableStatusText);

        s.Stored = new PlotLimits(1000, null, null, null);
        RefreshAndWait(s, vm);
        Assert.Equal("2370", vm.PlotAreaBox.Text);
        Assert.True(vm.LimitsUnsaved);
    });

    [Fact]
    public void Refresh_OtherDocument_ReplacesUnsavedValues() => Sta.Run(() =>
    {
        var s = new Stubs { SaveResult = (false, "borrowed") };
        var vm = s.Vm();
        RefreshAndWait(s, vm);
        vm.PlotAreaBox.Text = "2370";
        Commit(vm);

        s.DocKey = "B.rvt";
        s.Stored = new PlotLimits(800, 40, null, null);
        RefreshAndWait(s, vm);
        Assert.Equal("800", vm.PlotAreaBox.Text);
        Assert.Equal("40", vm.PrimaryLimitBox.Text);
        Assert.False(vm.LimitsUnsaved);
    });

    [Fact]
    public void Refresh_SameDocument_Saved_TakesStoredValues() => Sta.Run(() =>
    {
        var s = new Stubs();
        var vm = s.Vm();
        RefreshAndWait(s, vm);
        vm.PlotAreaBox.Text = "2370";
        Commit(vm);

        s.Stored = new PlotLimits(1999.5, null, null, null);   // as after an Undo in Revit
        RefreshAndWait(s, vm);
        Assert.Equal("1999.5", vm.PlotAreaBox.Text);
    });

    [Fact]
    public void Refresh_FromTableTab_StaysOnTable() => Sta.Run(() =>
    {
        var s = new Stubs();
        var vm = s.Vm();
        vm.CurrentPane = SubmissionPane.Table;
        RefreshAndWait(s, vm);
        Assert.Equal(SubmissionPane.Table, vm.CurrentPane);

        vm.CurrentPane = SubmissionPane.Config;
        RefreshAndWait(s, vm);
        Assert.Equal(SubmissionPane.Areas, vm.CurrentPane);
    });
}

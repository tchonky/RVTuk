using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using RVTuk.Core.RoundPositions;
using RVTuk.UI.Shared.ViewModels;

namespace RVTuk.UI.RoundPositions.ViewModels
{
    public class RoundPositionsPaneViewModel : ViewModelBase
    {
        private readonly Func<bool, CancellationToken, ScanResult> _scan;
        private readonly Func<bool, IReadOnlyList<long>, CancellationToken, RoundingReport> _fix;
        private readonly Action<IReadOnlyList<long>> _selectInModel;
        private readonly Action<bool> _persistSetting;

        /// <summary>
        /// Captured here rather than read from Application.Current later: Revit never creates a
        /// WPF Application, so Application.Current is null and the completion callback would run
        /// on the pool thread — touching an ObservableCollection from there throws inside a
        /// continuation nobody observes, and the pane sits on "Working…" for ever.
        /// </summary>
        private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

        private CancellationTokenSource? _cancellation;
        private bool _allowHalfCentimetres;
        private bool _isBusy;
        private bool _hasScanned;
        private string _status = "Not scanned yet.";

        public RoundPositionsPaneViewModel(
            Func<bool, CancellationToken, ScanResult> scan,
            Func<bool, IReadOnlyList<long>, CancellationToken, RoundingReport> fix,
            Action<IReadOnlyList<long>> selectInModel,
            bool allowHalfCentimetres,
            Action<bool> persistSetting)
        {
            _scan = scan ?? throw new ArgumentNullException(nameof(scan));
            _fix = fix ?? throw new ArgumentNullException(nameof(fix));
            _selectInModel = selectInModel ?? throw new ArgumentNullException(nameof(selectInModel));
            _persistSetting = persistSetting ?? throw new ArgumentNullException(nameof(persistSetting));
            _allowHalfCentimetres = allowHalfCentimetres;

            ScanCommand = new RelayCommand<object>(_ => RunScan(), _ => !IsBusy);
            FixAllCommand = new RelayCommand<object>(_ => RunFix(AllDirtyIds()), _ => !IsBusy && HasScanned);
            FixNodeCommand = new RelayCommand<TreeRow>(row => RunFix(row!.ElementIds), _ => !IsBusy);
            SelectNodeCommand = new RelayCommand<TreeRow>(row => _selectInModel(row!.ElementIds));
            SelectSkipGroupCommand = new RelayCommand<SkipGroup>(
                group => _selectInModel(group!.Elements.Select(e => e.ElementId).ToList()));
            CancelCommand = new RelayCommand<object>(_ => _cancellation?.Cancel(), _ => IsBusy);
        }

        public ObservableCollection<CategoryRow> Categories { get; } = new ObservableCollection<CategoryRow>();

        public ObservableCollection<SkipGroup> SkipGroups { get; } = new ObservableCollection<SkipGroup>();

        /// <summary>A bool, not the collection's Count: binding an int through a
        /// BooleanToVisibilityConverter silently does nothing.</summary>
        public bool HasSkips => SkipGroups.Count > 0;

        public bool AllowHalfCentimetres
        {
            get => _allowHalfCentimetres;
            set
            {
                if (_allowHalfCentimetres == value) return;
                SetProperty(ref _allowHalfCentimetres, value);
                _persistSetting(value);

                // The grid changed, so every count in the tree is now about a different question.
                Categories.Clear();
                SkipGroups.Clear();
                OnPropertyChanged(nameof(HasSkips));
                HasScanned = false;
                Status = "Grid changed — scan again.";
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set { SetProperty(ref _isBusy, value); RaiseCanExecuteChanged(); }
        }

        public bool HasScanned
        {
            get => _hasScanned;
            private set { SetProperty(ref _hasScanned, value); RaiseCanExecuteChanged(); }
        }

        public string Status
        {
            get => _status;
            private set => SetProperty(ref _status, value);
        }

        public ICommand ScanCommand { get; }
        public ICommand FixAllCommand { get; }
        public ICommand FixNodeCommand { get; }
        public ICommand SelectNodeCommand { get; }
        public ICommand SelectSkipGroupCommand { get; }
        public ICommand CancelCommand { get; }

        private IReadOnlyList<long> AllDirtyIds() => Categories.SelectMany(c => c.ElementIds).ToList();

        private void RunScan() => Run(() =>
        {
            var result = _scan(AllowHalfCentimetres, _cancellation!.Token);
            return () =>
            {
                ShowScan(result);
                Status = result.Dirty.Count == 0
                    ? $"Nothing off grid. {result.Examined} examined, {result.AlreadyClean} already clean."
                    : $"{result.Dirty.Count} off grid, {result.AlreadyClean} already clean, " +
                      $"{result.Skipped.Count} cannot be fixed.";
            };
        });

        private void RunFix(IReadOnlyList<long> ids)
        {
            if (ids.Count == 0) return;

            Run(() =>
            {
                var report = _fix(AllowHalfCentimetres, ids, _cancellation!.Token);
                var stopped = _cancellation.IsCancellationRequested;

                // Always re-scan the whole model, not just the node fixed: a fix moves things
                // outside its own node — a level carries its floor, a wall drags its openings.
                var scan = _scan(AllowHalfCentimetres, CancellationToken.None);
                return () =>
                {
                    ShowScan(scan);
                    Status = (stopped ? "Stopped. " : string.Empty) +
                        $"Moved {report.Moved} of {ids.Count} in {report.ElapsedMilliseconds} ms. " +
                        $"{scan.Dirty.Count} still off grid.";
                };
            });
        }

        private void ShowScan(ScanResult result)
        {
            Categories.Clear();
            foreach (var node in result.GroupedByCategoryAndLevel()) Categories.Add(new CategoryRow(node));

            SkipGroups.Clear();
            foreach (var group in result.GroupedSkips()) SkipGroups.Add(group);
            OnPropertyChanged(nameof(HasSkips));

            HasScanned = true;
        }

        private void Run(Func<Action> work)
        {
            _cancellation?.Dispose();
            _cancellation = new CancellationTokenSource();
            IsBusy = true;
            Status = "Working…";

            // The delegates block until Revit's main thread has done the work, so calling them on
            // the UI thread would freeze the pane along with Revit.
            Task.Run(work).ContinueWith(t =>
            {
                // IsBusy must come back down on every path. If it does not, the pane sits on
                // "Working…" and every button stays disabled with no way back.
                _dispatcher.Invoke(new Action(() =>
                {
                    try
                    {
                        if (t.IsFaulted) Status = $"Failed: {t.Exception?.GetBaseException().Message}";
                        else t.Result();
                    }
                    catch (Exception ex)
                    {
                        Status = $"Failed while showing the result: {ex.Message}";
                    }
                    finally
                    {
                        IsBusy = false;
                    }
                }));
            });
        }

        private void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
    }
}

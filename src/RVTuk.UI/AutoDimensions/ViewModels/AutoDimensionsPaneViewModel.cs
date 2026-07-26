using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using RVTuk.Core.AutoDimensions;
using RVTuk.UI.Shared.ViewModels;

namespace RVTuk.UI.AutoDimensions.ViewModels
{
    /// <summary>
    /// The Auto Dimensions scope pane. Both Revit interactions arrive as delegates that block on
    /// an ExternalEvent ping-pong, so both are invoked from the thread pool and their results
    /// marshalled back through the dispatcher — never called on the WPF UI thread, which is
    /// Revit's main thread and would deadlock waiting for its own event.
    /// </summary>
    public class AutoDimensionsPaneViewModel : ViewModelBase
    {
        private readonly Func<AutoDimensionsScope> _discover;
        private readonly Func<int, IReadOnlyList<long>, string> _createDimensions;
        private readonly Dispatcher _dispatcher;

        public AutoDimensionsPaneViewModel(
            Func<AutoDimensionsScope> discover,
            Func<int, IReadOnlyList<long>, string> createDimensions)
        {
            _discover = discover;
            _createDimensions = createDimensions;
            _dispatcher = Dispatcher.CurrentDispatcher;

            Categories = new ObservableCollection<CategoryOptionViewModel>
            {
                new CategoryOptionViewModel("Walls", DimensionCategories.Walls, true, null),
                new CategoryOptionViewModel("Doors", DimensionCategories.Doors, true, null),
                new CategoryOptionViewModel("Windows", DimensionCategories.Windows, true, null),
                new CategoryOptionViewModel("Ceilings", DimensionCategories.Ceilings, false, "Coming in v2"),
                new CategoryOptionViewModel("Floors", DimensionCategories.Floors, false, "Coming in v2"),
            };
            Levels = new ObservableCollection<LevelNodeViewModel>();
            CreateDimensionsCommand = new RelayCommand(RunCreateDimensions, CanCreateDimensions);
        }

        public ObservableCollection<CategoryOptionViewModel> Categories { get; }
        public ObservableCollection<LevelNodeViewModel> Levels { get; }
        public RelayCommand CreateDimensionsCommand { get; }

        private string _statusMessage = "Open the pane to read this project's levels.";
        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            private set => SetProperty(ref _isBusy, value);
        }

        /// <summary>Re-reads levels, views and the persisted selection. Returns immediately.</summary>
        public void Refresh()
        {
            if (IsBusy) return;

            IsBusy = true;
            StatusMessage = "Reading levels and views…";

            Task.Run(() =>
            {
                AutoDimensionsScope scope;
                try
                {
                    scope = _discover();
                }
                catch (Exception ex)
                {
                    _dispatcher.Invoke(() =>
                    {
                        StatusMessage = "Could not read the project: " + ex.Message;
                        IsBusy = false;
                    });
                    return;
                }

                _dispatcher.Invoke(() =>
                {
                    Populate(scope);
                    IsBusy = false;
                });
            });
        }

        private void Populate(AutoDimensionsScope scope)
        {
            var categories = scope.Selection == null
                ? CategoryMask.Default
                : CategoryMask.FromMask(scope.Selection.CategoryMask);

            foreach (var option in Categories)
                option.IsChecked = option.IsEnabled && categories.HasFlag(option.Category);

            var persisted = scope.Selection == null
                ? null
                : new HashSet<long>(scope.Selection.CheckedViewIds);

            Levels.Clear();
            foreach (var levelScope in scope.Levels)
            {
                var level = new LevelNodeViewModel(levelScope);
                foreach (var view in level.Views)
                {
                    // No persisted selection yet: start with each level's own reference view.
                    view.IsChecked = persisted == null
                        ? view.IsReferenceView
                        : persisted.Contains(view.ViewId);
                }
                Levels.Add(level);
            }

            StatusMessage = Levels.Count == 0
                ? "No plan views with a level in this project."
                : $"{Levels.Count} level(s); {Levels.Count(l => l.HasNoReferenceView)} without a reference view.";
        }

        private int SelectedCategoryMask => Categories
            .Where(c => c.IsEnabled && c.IsChecked)
            .Aggregate(0, (mask, c) => mask | CategoryMask.ToMask(c.Category));

        private List<long> CheckedViewIds => Levels
            .SelectMany(l => l.Views)
            .Where(v => v.IsChecked)
            .Select(v => v.ViewId)
            .ToList();

        private bool CanCreateDimensions() =>
            !IsBusy && SelectedCategoryMask != 0 && CheckedViewIds.Count > 0;

        private void RunCreateDimensions()
        {
            var mask = SelectedCategoryMask;
            var viewIds = CheckedViewIds;

            IsBusy = true;
            StatusMessage = "Creating dimensions…";

            Task.Run(() =>
            {
                string summary;
                try
                {
                    summary = _createDimensions(mask, viewIds);
                }
                catch (Exception ex)
                {
                    summary = "Auto Dimensions failed: " + ex.Message;
                }

                _dispatcher.Invoke(() =>
                {
                    StatusMessage = summary;
                    IsBusy = false;
                });
            });
        }
    }
}

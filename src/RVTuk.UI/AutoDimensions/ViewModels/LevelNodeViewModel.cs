using System.Collections.ObjectModel;
using System.Linq;
using RVTuk.Core.AutoDimensions;
using RVTuk.UI.Shared.ViewModels;

namespace RVTuk.UI.AutoDimensions.ViewModels
{
    /// <summary>
    /// One level row. A level with no reference view has nothing to fan out, so it carries no
    /// views at all and renders as an inert red line instead of a checkbox.
    /// </summary>
    public class LevelNodeViewModel : ViewModelBase
    {
        private bool _cascading;

        public LevelNodeViewModel(LevelScope scope)
        {
            LevelName = scope.LevelName;
            HasReferenceView = scope.HasReferenceView;
            Views = new ObservableCollection<ViewNodeViewModel>();

            if (!scope.HasReferenceView) return;

            foreach (var view in scope.Views)
            {
                Views.Add(new ViewNodeViewModel(
                    view.ViewId,
                    view.ViewName,
                    view.ViewId == scope.ReferenceViewId,
                    this));
            }
        }

        public string LevelName { get; }
        public bool HasReferenceView { get; }
        public bool HasNoReferenceView => !HasReferenceView;
        public string DisplayName => HasReferenceView ? LevelName : LevelName + "   — no reference view";
        public ObservableCollection<ViewNodeViewModel> Views { get; }

        private bool _isExpanded = true;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                SetProperty(ref _isExpanded, value);
                OnPropertyChanged(nameof(ShowViews));
            }
        }

        /// <summary>
        /// A level with no reference view has no views to show at all, so it never expands —
        /// collapsing is only about taming the length of a project with many levels.
        /// </summary>
        public bool ShowViews => HasReferenceView && IsExpanded;

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                SetProperty(ref _isChecked, value);
                if (_cascading) return;

                foreach (var view in Views) view.SetCheckedFromLevel(value);
            }
        }

        /// <summary>The level is checked exactly when every one of its views is.</summary>
        internal void RecomputeFromViews()
        {
            _cascading = true;
            IsChecked = Views.Count > 0 && Views.All(v => v.IsChecked);
            _cascading = false;
        }
    }
}

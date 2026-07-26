using RVTuk.UI.Shared.ViewModels;

namespace RVTuk.UI.AutoDimensions.ViewModels
{
    /// <summary>One checkable target view under a level.</summary>
    public class ViewNodeViewModel : ViewModelBase
    {
        private readonly LevelNodeViewModel _level;

        public ViewNodeViewModel(long viewId, string viewName, bool isReferenceView, LevelNodeViewModel level)
        {
            ViewId = viewId;
            ViewName = viewName;
            IsReferenceView = isReferenceView;
            _level = level;
        }

        public long ViewId { get; }
        public string ViewName { get; }
        public bool IsReferenceView { get; }
        public string DisplayName => IsReferenceView ? ViewName + "   (reference view)" : ViewName;

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                SetProperty(ref _isChecked, value);
                _level.RecomputeFromViews();
            }
        }

        /// <summary>Used by the level's cascade, which recomputes its own state itself.</summary>
        internal void SetCheckedFromLevel(bool value) =>
            SetProperty(ref _isChecked, value, nameof(IsChecked));
    }
}

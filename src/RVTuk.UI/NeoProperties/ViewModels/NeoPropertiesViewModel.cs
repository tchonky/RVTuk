using System.Collections.Generic;
using RVTuk.Core.NeoProperties;

using RVTuk.UI.Shared.ViewModels;

namespace RVTuk.UI.NeoProperties.ViewModels
{
    /// <summary>View model for the Neo Properties dockable pane — read-only, single-element.</summary>
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

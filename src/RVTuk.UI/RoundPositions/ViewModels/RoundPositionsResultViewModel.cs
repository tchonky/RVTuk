using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using RVTuk.Core.RoundPositions;
using RVTuk.UI.Shared.ViewModels;

namespace RVTuk.UI.RoundPositions.ViewModels
{
    public class RoundPositionsResultViewModel : ViewModelBase
    {
        private readonly Action<IReadOnlyList<long>> _selectInModel;

        public RoundPositionsResultViewModel(
            RoundingReport report, Action<IReadOnlyList<long>> selectInModel)
        {
            _selectInModel = selectInModel;

            Summary =
                $"Examined {report.Examined}   ·   Moved {report.Moved}   ·   " +
                $"Already clean {report.AlreadyClean}   ·   Skipped {report.Skipped.Count}";

            // GroupedByReason re-groups on every call — hold the one result, don't bind the method.
            Groups = report.GroupedByReason();
            ReleasedConstraints = report.ReleasedConstraints;
            SelectGroupCommand = new RelayCommand<SkipGroup>(SelectGroup);
        }

        public string Summary { get; }

        public IReadOnlyList<SkipGroup> Groups { get; }

        public IReadOnlyList<ReleasedConstraint> ReleasedConstraints { get; }

        public bool HasReleasedConstraints => ReleasedConstraints.Count > 0;

        public ICommand SelectGroupCommand { get; }

        private void SelectGroup(SkipGroup? group)
        {
            if (group == null) return;
            _selectInModel(group.Elements.Select(e => e.ElementId).ToList());
        }
    }
}

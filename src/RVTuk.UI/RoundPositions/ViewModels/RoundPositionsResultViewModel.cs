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
            _selectInModel = selectInModel
                ?? throw new ArgumentNullException(nameof(selectInModel));

            Summary =
                $"Examined {report.Examined}   ·   Moved {report.Moved}   ·   " +
                $"Already clean {report.AlreadyClean}   ·   Skipped {report.Skipped.Count}";

            // GroupedByReason re-groups on every call — hold the one result, don't bind the method.
            Groups = report.GroupedByReason();
            ReleasedConstraints = report.ReleasedConstraints;
            SelectGroupCommand = new RelayCommand<SkipGroup>(SelectGroup);
            SelectReleasedCommand = new RelayCommand<object>(_ => SelectReleased());
        }

        public string Summary { get; }

        public IReadOnlyList<SkipGroup> Groups { get; }

        public IReadOnlyList<ReleasedConstraint> ReleasedConstraints { get; }

        public bool HasReleasedConstraints => ReleasedConstraints.Count > 0;

        /// <summary>The fully clean run: no groups to show, so say so rather than show a blank panel.</summary>
        public bool NothingSkipped => Groups.Count == 0;

        public ICommand SelectGroupCommand { get; }

        public ICommand SelectReleasedCommand { get; }

        private void SelectGroup(SkipGroup? group)
        {
            if (group == null) return;
            _selectInModel(group.Elements.Select(e => e.ElementId).ToList());
        }

        /// <summary>
        /// Selects the elements the constraints were taken off — not the constraints themselves.
        /// A deleted alignment lock's own id is a tombstone that resolves to nothing, and this
        /// list is the only record in existence that the removal happened, so it has to point at
        /// something the user can still find.
        /// </summary>
        private void SelectReleased() =>
            _selectInModel(ReleasedConstraints.Select(c => c.ElementId).Distinct().ToList());
    }
}

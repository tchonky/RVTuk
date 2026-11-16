using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RVTuk.Core.TopoTools;
using RVTuk.Revit.Shared;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// The ⇄ button. It works in three steps:
    /// <list type="number">
    /// <item><b>Refuse</b> what cannot be converted: subdivisions, group members, elements in
    /// another design option, elements borrowed by someone else.</item>
    /// <item><b>Ask</b> about anything whose deletion would take other elements with it, all
    /// before any change. Cancel therefore really does mean nothing happened, and no dialog is
    /// ever up while a transaction is open.</item>
    /// <item><b>Convert</b> in one transaction group, so one Ctrl+Z undoes the run. Each element
    /// gets its own transaction inside it, so one Revit refuses rolls back alone.</item>
    /// </list>
    /// </summary>
    public static class ConvertRunner
    {
        private const string Title = "Topo Tools";
        private const string TransactionName = "Convert Floor ⇄ Toposolid";

        private sealed class Candidate
        {
            public ElementId Id = ElementId.InvalidElementId;
            public bool IsFloor;
            public string Label = "";
            public string TypeName = "";
            public string Dependents = "";
        }

        public static string Run(UIDocument uiDoc)
        {
            var doc = uiDoc.Document;

            var selected = uiDoc.Selection.GetElementIds()
                .Select(doc.GetElement)
                .Where(e => e is Floor || e is Toposolid)
                .ToList();
            if (selected.Count == 0) return ConvertReport.NothingSelected;

            var report = new ConvertReport();
            var floorTypeId = DefaultTypes.FloorTypeId(doc);
            var toposolidTypeId = DefaultTypes.ToposolidTypeId(doc);
            var candidates = new List<Candidate>();
            foreach (var element in selected)
            {
                var label = Label(element);
                var refusal = RefusalOf(doc, element, floorTypeId, toposolidTypeId);
                if (refusal != null)
                {
                    report.Skipped.Add($"{label}: {refusal}");
                    continue;
                }

                candidates.Add(new Candidate
                {
                    Id = element.Id,
                    IsFloor = element is Floor,
                    Label = label,
                    TypeName = doc.GetElement(element.GetTypeId())?.Name ?? "",
                    Dependents = ConvertReport.DescribeDependents(
                        DependentsCounter.Hosted(element),
                        DependentsCounter.Annotations(element),
                        DependentsCounter.Subdivisions(element),
                        DependentsCounter.Parts(element)),
                });
            }

            var approved = Ask(candidates, report);
            if (approved == null) return ConvertReport.Cancelled;

            var newIds = ConvertAll(doc, approved, floorTypeId, toposolidTypeId, report);
            if (newIds.Count > 0) uiDoc.Selection.SetElementIds(newIds);

            return report.Summarise();
        }

        /// <summary>
        /// The refusals shared with the edit session (<see cref="SlabRefusals.Common"/> — not
        /// <see cref="SlabRefusals.Of"/>, whose shape-point refusal would turn away the sloped
        /// floors this converter handles on purpose), then the converter's own type checks.
        /// </summary>
        private static string? RefusalOf(
            Document doc, Element element, ElementId floorTypeId, ElementId toposolidTypeId)
        {
            var common = SlabRefusals.Common(doc, element);
            if (common != null) return common;

            if (element is Floor && toposolidTypeId == ElementId.InvalidElementId)
                return "the project has no toposolid types";
            if (element is Toposolid && floorTypeId == ElementId.InvalidElementId)
                return "the project has no floor types";

            return null;
        }

        /// <returns>The candidates to convert, or null when the user cancelled.</returns>
        private static List<Candidate>? Ask(List<Candidate> candidates, ConvertReport report)
        {
            var approved = new List<Candidate>();
            int stillToAsk = candidates.Count(c => c.Dependents.Length > 0);
            bool? standingAnswer = null;

            foreach (var candidate in candidates)
            {
                if (candidate.Dependents.Length == 0)
                {
                    approved.Add(candidate);
                    continue;
                }
                stillToAsk--;

                bool convert;
                if (standingAnswer != null)
                {
                    convert = standingAnswer.Value;
                }
                else
                {
                    var dialog = new TaskDialog(Title)
                    {
                        MainInstruction = candidate.IsFloor ? "Convert floor → toposolid?" : "Convert toposolid → floor?",
                        MainContent =
                            $"{(candidate.IsFloor ? "Floor" : "Toposolid")} {Isolate(candidate.TypeName)} " +
                            $"(id {candidate.Id.Value}) carries {candidate.Dependents} that converting deletes.",
                        AllowCancellation = true,
                        // A reflexive Enter keeps the element's work rather than deleting it.
                        DefaultButton = TaskDialogResult.CommandLink2,
                    };
                    dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Convert and delete them");
                    dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Skip this one");
                    if (stillToAsk > 0) dialog.VerificationText = "Do the same for the rest of the selection";

                    var result = dialog.Show();
                    if (result == TaskDialogResult.CommandLink1) convert = true;
                    else if (result == TaskDialogResult.CommandLink2) convert = false;
                    else return null;

                    if (stillToAsk > 0 && dialog.WasVerificationChecked()) standingAnswer = convert;
                }

                if (convert) approved.Add(candidate);
                else report.Skipped.Add($"{candidate.Label}: kept its {candidate.Dependents}");
            }

            return approved;
        }

        private static List<ElementId> ConvertAll(
            Document doc, List<Candidate> approved, ElementId floorTypeId, ElementId toposolidTypeId, ConvertReport report)
        {
            var newIds = new List<ElementId>();
            if (approved.Count == 0) return newIds;

            using (var group = new TransactionGroup(doc, TransactionName))
            {
                group.Start();

                foreach (var candidate in approved)
                {
                    var source = doc.GetElement(candidate.Id);
                    if (source == null)
                    {
                        report.Skipped.Add($"{candidate.Label}: deleted by an earlier conversion in this run");
                        continue;
                    }

                    var failures = new FailureCollector();
                    using (var tx = new Transaction(doc, TransactionName))
                    {
                        tx.Start();
                        var options = tx.GetFailureHandlingOptions();
                        options.SetFailuresPreprocessor(failures);
                        options.SetClearAfterRollback(true);
                        tx.SetFailureHandlingOptions(options);

                        ConvertOutcome outcome;
                        try
                        {
                            outcome = SlabConverter.Convert(doc, source, candidate.IsFloor ? toposolidTypeId : floorTypeId);
                        }
                        catch (Exception ex)
                        {
                            tx.RollBack();
                            report.Skipped.Add($"{candidate.Label}: {ex.Message}");
                            continue;
                        }

                        if (tx.Commit() != TransactionStatus.Committed)
                        {
                            report.Skipped.Add($"{candidate.Label}: {failures.Error ?? "Revit rejected the new element"}");
                            continue;
                        }

                        if (outcome.BecameToposolid) report.FloorsToToposolids++;
                        else report.ToposolidsToFloors++;
                        report.PointsLost += outcome.PointsLost;
                        report.CreasesLost += outcome.CreasesLost;
                        report.ParametersNotCarried += outcome.ParametersNotCarried;
                        report.WarningsDismissed += failures.Warnings;
                        newIds.Add(outcome.NewId);
                    }
                }

                // Nothing converted: leave no empty entry on the undo list.
                if (newIds.Count > 0) group.Assimilate();
                else group.RollBack();
            }

            return newIds;
        }

        private static string Label(Element element) =>
            $"{(element is Floor ? "Floor" : "Toposolid")} {element.Id.Value}";

        /// <summary>First-strong isolates (U+2068…U+2069), as Room Floor uses: a Hebrew name ending
        /// in a number would otherwise drag the text after it into its right-to-left run.</summary>
        private static string Isolate(string text) => "⁨" + text + "⁩";

        /// <summary>
        /// Twenty elements must not mean twenty warning popups: warnings are dismissed and counted.
        /// An error rolls back that element alone; its first description becomes the skip reason.
        /// </summary>
        private sealed class FailureCollector : IFailuresPreprocessor
        {
            public int Warnings { get; private set; }
            public string? Error { get; private set; }

            public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
            {
                bool hasError = false;
                foreach (var failure in accessor.GetFailureMessages())
                {
                    if (failure.GetSeverity() == FailureSeverity.Warning)
                    {
                        accessor.DeleteWarning(failure);
                        Warnings++;
                    }
                    else
                    {
                        hasError = true;
                        Error ??= failure.GetDescriptionText();
                    }
                }
                return hasError ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
            }
        }
    }
}

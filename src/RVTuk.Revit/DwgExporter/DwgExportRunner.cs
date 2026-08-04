using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RVTuk.Core.DwgExporter;

namespace RVTuk.Revit.DwgExporter
{
    /// <summary>
    /// Turns one dialog request into work across one or more open models: resolve each ticked
    /// model's setups by name, create any the user approved copying, plan every model's files
    /// into one list (so duplicates are caught across models before anything is written), then
    /// export model by model. Runs inside the command's API context on the UI thread.
    /// </summary>
    internal class DwgExportRunner
    {
        private readonly UIDocument _uidoc;
        private readonly Document _active;

        public DwgExportRunner(UIDocument uidoc)
        {
            _uidoc = uidoc;
            _active = uidoc.Document;
        }

        /// <summary>One model's share of a run: the document, its files, and its rules.</summary>
        private class Job
        {
            public Document Doc { get; set; } = null!;
            public List<(ElementId Id, PlannedExportFile File)> Files { get; set; } =
                new List<(ElementId, PlannedExportFile)>();
            public NamingRules Rules { get; set; } = new NamingRules();
        }

        /// <summary>A worked filename per kind — each rule previewed against something it
        /// actually applies to, rather than whichever file happens to sort first.</summary>
        public DwgExportExamples EvaluateExample(DwgExportRequest request)
        {
            var rules = SheetDwgExporter.ReadNamingRules(_active, request);
            var files = request.CurrentWindow
                ? SheetDwgExporter.PlanCurrentWindow(_uidoc, rules)
                : SheetDwgExporter.PlanFiles(_active, request, rules, "", out _);

            return new DwgExportExamples
            {
                Sheet = Example(files, wantSheet: true, "(no sheets in the selected range)"),
                View = Example(files, wantSheet: false, "(no non-sheet views in the selected range)"),
            };
        }

        private static string Example(
            List<(ElementId Id, PlannedExportFile File)> files, bool wantSheet, string none)
        {
            var match = files.FirstOrDefault(f => f.File.IsSheet == wantSheet).File;
            return match == null ? none : match.FileName + ".dwg";
        }

        public DwgExportPlan Plan(DwgExportRequest request)
        {
            Directory.CreateDirectory(request.OutputFolder);
            if (request.ExportPdf) Directory.CreateDirectory(request.PdfFolder);

            var jobs = BuildJobs(request, out _, out _);
            var files = jobs.SelectMany(j => j.Files).Select(f => f.File).ToList();
            return DwgExportPlanner.Check(files, name => SheetDwgExporter.OutputFileExists(request, name));
        }

        public DwgExportResult Run(DwgExportRequest request, Action<int, int, string> progress)
        {
            var jobs = BuildJobs(request, out var skippedModels, out var skippedViews);
            var total = jobs.Sum(j => j.Files.Count);
            var done = 0;
            var result = new DwgExportResult();

            foreach (var job in jobs)
            {
                var dwgOptions = request.ExportDwg
                    ? SheetDwgExporter.GetDwgOptions(job.Doc, request.DwgSetupName)
                    : null;
                var sheetPdfOptions = request.ExportPdf
                    ? SheetDwgExporter.GetPdfOptions(job.Doc, request.SheetNamingSetupName, job.Rules, forViews: false)
                    : null;
                var viewPdfOptions = request.ExportPdf
                    ? SheetDwgExporter.GetPdfOptions(
                        job.Doc,
                        job.Rules.UseViewName ? request.SheetNamingSetupName : request.ViewNamingSetupName,
                        job.Rules,
                        forViews: true)
                    : null;

                var offset = done;
                var one = SheetDwgExporter.Export(
                    job.Doc, request, job.Files, dwgOptions, sheetPdfOptions, viewPdfOptions,
                    (i, _, label) => progress(offset + i, total, label));

                done += job.Files.Count;
                result.ExportedCount += one.ExportedCount;
                result.Errors.AddRange(one.Errors);
                result.Notes.AddRange(one.Notes);
            }

            result.Notes.AddRange(skippedModels);
            result.Notes.AddRange(skippedViews.Select(name => name + ": skipped (not an exportable view)"));
            return result;
        }

        /// <summary>
        /// The active model always runs. Each extra model is resolved by name; models the
        /// resolver rejects become notes rather than failures, and any approved copies are made
        /// before that model's files are planned (the rules must be readable from it afterwards).
        /// </summary>
        private List<Job> BuildJobs(
            DwgExportRequest request, out List<string> skippedModels, out List<string> skippedViews)
        {
            skippedModels = new List<string>();
            skippedViews = new List<string>();
            var jobs = new List<Job>();

            var activeRules = SheetDwgExporter.ReadNamingRules(_active, request);
            if (request.CurrentWindow)
            {
                jobs.Add(new Job
                {
                    Doc = _active,
                    Rules = activeRules,
                    Files = SheetDwgExporter.PlanCurrentWindow(_uidoc, activeRules),
                });
                return jobs; // the active window is only ever the active model
            }

            var extras = request.ExtraModelKeys ?? new List<string>();
            var multi = extras.Count > 0;
            jobs.Add(new Job
            {
                Doc = _active,
                Rules = activeRules,
                Files = SheetDwgExporter.PlanFiles(
                    _active, request, activeRules, multi ? _active.Title : "", out var activeSkipped),
            });
            skippedViews.AddRange(activeSkipped);

            if (!multi) return jobs;

            var byKey = OpenModels.Enumerate(_active.Application)
                .Where(d => !ReferenceEquals(d, _active))
                .ToDictionary(OpenModels.KeyOf, d => d, StringComparer.OrdinalIgnoreCase);

            foreach (var key in extras)
            {
                if (!byKey.TryGetValue(key, out var doc))
                {
                    skippedModels.Add(key + ": skipped (no longer open)");
                    continue;
                }

                var plan = ModelSetupResolver.Resolve(OpenModels.ReadInventory(doc), request);
                if (!plan.CanRun)
                {
                    skippedModels.Add(doc.Title + ": skipped — " + plan.SkipReason);
                    continue;
                }

                try
                {
                    foreach (var name in plan.PdfSetupsToCopy)
                        SetupTransfer.CopyPdfSetup(_active, doc, name);
                    if (plan.CopyDwgSetup)
                        SetupTransfer.CopyDwgSetup(_active, doc, request.DwgSetupName);
                }
                catch (Exception ex)
                {
                    skippedModels.Add(doc.Title + ": skipped — " + ex.Message);
                    continue;
                }

                try
                {
                    var rules = SheetDwgExporter.ReadNamingRules(doc, request);
                    jobs.Add(new Job
                    {
                        Doc = doc,
                        Rules = rules,
                        Files = SheetDwgExporter.PlanFiles(doc, request, rules, doc.Title, out var skipped),
                    });
                    skippedViews.AddRange(skipped.Select(n => doc.Title + " — " + n));
                }
                catch (Exception ex)
                {
                    skippedModels.Add(doc.Title + ": skipped — " + ex.Message);
                }
            }

            return jobs;
        }
    }
}

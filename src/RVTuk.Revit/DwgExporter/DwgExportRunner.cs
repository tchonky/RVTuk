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
            var mode = request.ExportDwg ? request.ZipMode : TransmittalMode.None;
            var bundling = mode != TransmittalMode.None;
            // Taken before anything is written: the diff against the after-shot is how we
            // learn what Revit produced, images and xref'd view drawings included.
            var before = bundling
                ? FolderSnapshot.Take(request.OutputFolder)
                : new List<FileStamp>();

            // Per-drawing bundling needs to know which view produced what, so it re-snapshots
            // after every view instead of once at the end.
            var perDrawing = mode == TransmittalMode.PerDrawing;
            var productions = new List<DrawingProduction>();
            var running = before;

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
                    (i, _, label) => progress(offset + i, total, label),
                    perDrawing
                        ? file =>
                        {
                            var now = FolderSnapshot.Take(request.OutputFolder);
                            productions.Add(new DrawingProduction
                            {
                                File = file,
                                ProducedFiles = FolderSnapshot.Diff(running, now),
                            });
                            running = now;
                        }
                        : (Action<PlannedExportFile>?)null);

                done += job.Files.Count;
                result.ExportedCount += one.ExportedCount;
                result.Errors.AddRange(one.Errors);
                result.Notes.AddRange(one.Notes);
            }

            result.Notes.AddRange(skippedModels);
            result.Notes.AddRange(skippedViews.Select(name => name + ": skipped (not an exportable view)"));

            if (bundling)
            {
                if (perDrawing) WritePerDrawingTransmittals(request, jobs, productions, result);
                else WriteTransmittal(request, jobs, before, result);
            }
            return result;
        }

        /// <summary>
        /// One archive per drawing, named after it. Same failure rule as the run-level
        /// bundle: the drawings are already on disk, so a zip problem is reported and no more.
        /// </summary>
        private void WritePerDrawingTransmittals(
            DwgExportRequest request, List<Job> jobs, List<DrawingProduction> productions, DwgExportResult result)
        {
            try
            {
                var bundles = PerDrawingTransmittal.Plan(request.OutputFolder, productions);
                if (bundles.Count == 0)
                {
                    result.Notes.Add("No transmittals written — the export produced no drawings.");
                    return;
                }

                var fonts = FontCollector.Collect(_active, request.DwgSetupName, out var fontWarnings);

                foreach (var bundle in bundles)
                {
                    var contents = TransmittalBuilder.Build(bundle.Files, fonts, bundle.ArchivePath);
                    contents.Warnings.AddRange(fontWarnings);

                    var report = TransmittalReport.Render(
                        DescribeRun(request, jobs, Path.GetFileNameWithoutExtension(bundle.ArchivePath)),
                        contents);

                    result.Notes.AddRange(TransmittalWriter.Write(bundle.ArchivePath, contents, report));
                }

                result.Notes.Add("Transmittals: " + bundles.Count + " zip(s) in " + request.OutputFolder);
            }
            catch (Exception ex)
            {
                result.Errors.Add("Transmittal zips failed (the exported files are unaffected): " + ex.Message);
            }
        }

        private TransmittalInfo DescribeRun(DwgExportRequest request, List<Job> jobs, string? drawingName = null)
            => new TransmittalInfo
            {
                CreatedUtc = DateTime.UtcNow,
                SheetSetName = request.CurrentWindow ? "" : request.SheetSetName,
                ModelTitles = jobs.Select(j => j.Doc.Title).Distinct().ToList(),
                DwgSetupName = request.DwgSetupName,
                SheetNamingSetupName = request.SheetNamingSetupName,
                ViewNamingSetupName = request.ViewNamingSetupName,
                DrawingName = drawingName ?? "",
            };

        /// <summary>
        /// Bundles what the run just produced. Runs after a successful export and never fails
        /// it — the drawings are already on disk, so a zip problem is reported and no more.
        /// </summary>
        private void WriteTransmittal(
            DwgExportRequest request, List<Job> jobs, IReadOnlyList<FileStamp> before, DwgExportResult result)
        {
            try
            {
                var produced = FolderSnapshot.Diff(before, FolderSnapshot.Take(request.OutputFolder));
                if (produced.Count == 0)
                {
                    result.Notes.Add("No transmittal written — the export produced no files.");
                    return;
                }

                var archivePath = Path.Combine(request.OutputFolder, ArchiveName(request));
                var fonts = FontCollector.Collect(_active, request.DwgSetupName, out var fontWarnings);

                var contents = TransmittalBuilder.Build(produced, fonts, archivePath);
                contents.Warnings.AddRange(fontWarnings);

                var report = TransmittalReport.Render(DescribeRun(request, jobs), contents);

                // Skipped-file warnings surface here rather than in the report: the report is
                // already rendered by the time the writer discovers a lock.
                result.Notes.AddRange(TransmittalWriter.Write(archivePath, contents, report));
                result.Notes.Add("Transmittal: " + archivePath);
            }
            catch (Exception ex)
            {
                result.Errors.Add("Transmittal zip failed (the exported files are unaffected): " + ex.Message);
            }
        }

        private string ArchiveName(DwgExportRequest request)
        {
            var stem = request.CurrentWindow || string.IsNullOrWhiteSpace(request.SheetSetName)
                ? _active.Title
                : request.SheetSetName;
            return FileNameComposer.Sanitize(stem) + "_" + DateTime.Now.ToString("yyyy-MM-dd_HHmm") + ".zip";
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

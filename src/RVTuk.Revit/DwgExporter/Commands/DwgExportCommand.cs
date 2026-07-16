using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RVTuk.Core.DwgExporter;
using RVTuk.UI.DwgExporter.ViewModels;
using RVTuk.UI.DwgExporter.Views;

namespace RVTuk.Revit.DwgExporter.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class DwgExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            // Revit never creates a WPF Application; make one we own (never auto-shutdown).
            if (System.Windows.Application.Current == null)
            {
                new System.Windows.Application
                {
                    ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown
                };
            }

            var uidoc = commandData.Application.ActiveUIDocument;
            var doc = uidoc.Document;

            try
            {
                // ── Dropdown contents ────────────────────────────────────────────────
                var pdfSettings = new FilteredElementCollector(doc)
                    .OfClass(typeof(ExportPDFSettings))
                    .Cast<ExportPDFSettings>()
                    .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var pdfItems = pdfSettings.Count > 0
                    ? pdfSettings.Select(s => new PdfSetupItem
                    {
                        Name = s.Name,
                        Pattern = NamingRuleEvaluator.DescribePattern(doc, s.GetOptions().GetNamingRule()),
                    }).ToList()
                    : new List<PdfSetupItem>
                    {
                        new PdfSetupItem
                        {
                            Name = DwgExportDefaults.FallbackPdfSetupName,
                            Pattern = DwgExportDefaults.FallbackPdfSetupName,
                        },
                    };

                var dwgNames = new FilteredElementCollector(doc)
                    .OfClass(typeof(ExportDWGSettings))
                    .Cast<ExportDWGSettings>()
                    .Select(s => s.Name)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (dwgNames.Count == 0) dwgNames.Add(DwgExportDefaults.DefaultDwgSetupName);

                var sheetSets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheetSet))
                    .Cast<ViewSheetSet>()
                    .Select(s => new SheetSetItem
                    {
                        Name = s.Name,
                        SheetCount = s.Views.OfType<ViewSheet>().Count(),
                    })
                    .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var activeView = uidoc.ActiveGraphicalView;
                var currentViewLabel = activeView is ViewSheet vs
                    ? vs.SheetNumber + " - " + vs.Name
                    : activeView?.Name ?? "(no graphical view)";

                // Per-model key for remembering the output folder; unsaved docs fall back to title.
                var modelKey = string.IsNullOrWhiteSpace(doc.PathName) ? doc.Title : doc.PathName;

                // ── Delegates (run on the UI thread inside this command's API context) ──
                Func<DwgExportRequest, string> evaluateExample = request =>
                {
                    var files = SheetDwgExporter.PlanFiles(uidoc, request, out _);
                    return files.Count == 0 ? "(no sheets in the selected set)" : files[0].File.FileName + ".dwg";
                };

                Func<DwgExportRequest, DwgExportPlan> planExport = request =>
                {
                    if (!Directory.Exists(request.OutputFolder))
                        Directory.CreateDirectory(request.OutputFolder);
                    var files = SheetDwgExporter.PlanFiles(uidoc, request, out _);
                    return DwgExportPlanner.Check(
                        files.Select(f => f.File).ToList(),
                        name => SheetDwgExporter.OutputFileExists(request.OutputFolder, name, request));
                };

                Func<DwgExportRequest, Action<int, int, string>, DwgExportResult> runExport =
                    (request, progress) =>
                    {
                        var files = SheetDwgExporter.PlanFiles(uidoc, request, out var skipped);
                        var result = SheetDwgExporter.Export(doc, request, files, progress);
                        result.Errors.AddRange(
                            skipped.Select(name => name + ": skipped (not an exportable view)"));
                        return result;
                    };

                var vm = new DwgExportViewModel(
                    pdfItems, dwgNames, sheetSets, currentViewLabel, modelKey,
                    evaluateExample, planExport, runExport);
                vm.OpenNativeDialog = kind =>
                {
                    // "sets" prefers Publish Settings (a dedicated view/sheet-set manager);
                    // Revit greys it out for some model contexts, so the PDF Export dialog —
                    // whose pencil also edits sets — is the fallback.
                    var candidates = kind switch
                    {
                        "sets" => new[] { PostableCommand.PublishSettings, PostableCommand.ExportPDF },
                        "dwgsetups" => new[] { PostableCommand.ExportOptionsExportSetupsDWGOrDXF },
                        _ => new[] { PostableCommand.ExportPDF },
                    };
                    foreach (var postable in candidates)
                    {
                        try
                        {
                            var id = RevitCommandId.LookupPostableCommandId(postable);
                            if (id != null && commandData.Application.CanPostCommand(id))
                            {
                                commandData.Application.PostCommand(id);
                                return true;
                            }
                        }
                        catch
                        {
                            // another command already posted, or id unavailable — try next
                        }
                    }
                    return false;
                };

                var window = new DwgExportWindow(vm);
                new System.Windows.Interop.WindowInteropHelper(window)
                {
                    Owner = commandData.Application.MainWindowHandle,
                };
                window.ShowDialog();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("RVTuk – DWG Export",
                    "Failed to open DWG Export:\n\n" + ex.GetType().Name + ": " + ex.Message);
                return Result.Failed;
            }
        }
    }
}

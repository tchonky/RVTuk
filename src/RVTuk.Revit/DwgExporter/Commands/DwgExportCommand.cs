using System;
using System.Collections.Generic;
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
                // ── Dropdown contents (all from the active model) ────────────────────
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

                var dwgNames = ExportDWGSettings.ListNames(doc)
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

                // Active model first, so the dialog can lock its checkbox on.
                var models = OpenModels.Enumerate(commandData.Application.Application)
                    .OrderByDescending(d => ReferenceEquals(d, doc))
                    .ThenBy(d => d.Title, StringComparer.OrdinalIgnoreCase)
                    .Select(OpenModels.ReadInventory)
                    .ToList();

                // ── Delegates (run on the UI thread inside this command's API context) ──
                var runner = new DwgExportRunner(uidoc);

                var vm = new DwgExportViewModel(
                    pdfItems, dwgNames, sheetSets, currentViewLabel,
                    models, OpenModels.KeyOf(doc),
                    runner.EvaluateExample, runner.Plan, runner.Run);

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

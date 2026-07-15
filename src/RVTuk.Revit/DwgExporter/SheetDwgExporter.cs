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
    /// Resolves the requested export range to concrete views + filenames, and runs the export
    /// one view per Document.Export call — the multi-view overload invents its own filenames,
    /// and per-view calls give us exact names, progress, and per-sheet error capture.
    /// </summary>
    internal static class SheetDwgExporter
    {
        /// <summary>The views/sheets the request resolves to, with evaluated filenames (no extension).</summary>
        public static List<(ElementId Id, PlannedExportFile File)> PlanFiles(
            UIDocument uidoc, DwgExportRequest request, out List<string> skipped)
        {
            var doc = uidoc.Document;
            var rule = GetNamingRule(doc, request.PdfSetupName);
            var result = new List<(ElementId, PlannedExportFile)>();
            skipped = new List<string>();

            if (request.CurrentWindow)
            {
                var view = uidoc.ActiveGraphicalView
                    ?? throw new InvalidOperationException("The active window is not an exportable graphical view.");
                result.Add((view.Id, new PlannedExportFile
                {
                    ViewLabel = Label(view),
                    FileName = FileNameFor(doc, view, rule),
                }));
                return result;
            }

            var set = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheetSet))
                .Cast<ViewSheetSet>()
                .FirstOrDefault(s => s.Name == request.SheetSetName)
                ?? throw new InvalidOperationException(
                    "View/sheet set '" + request.SheetSetName + "' no longer exists in this document.");

            foreach (View view in set.Views)
            {
                if (view is ViewSheet || view.CanBePrinted)
                {
                    result.Add((view.Id, new PlannedExportFile
                    {
                        ViewLabel = Label(view),
                        FileName = FileNameFor(doc, view, rule),
                    }));
                }
                else
                {
                    skipped.Add(view.Name);
                }
            }

            return result
                .OrderBy(x => x.Item2.FileName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static DwgExportResult Export(
            Document doc,
            DwgExportRequest request,
            List<(ElementId Id, PlannedExportFile File)> files,
            Action<int, int, string> progress)
        {
            var options = GetDwgOptions(doc, request.DwgSetupName);
            var result = new DwgExportResult();

            for (int i = 0; i < files.Count; i++)
            {
                var (id, file) = files[i];
                try
                {
                    var ok = doc.Export(request.OutputFolder, file.FileName,
                        new List<ElementId> { id }, options);
                    if (ok) result.ExportedCount++;
                    else result.Errors.Add(file.ViewLabel + ": Revit reported the export failed.");
                }
                catch (Exception ex)
                {
                    result.Errors.Add(file.ViewLabel + ": " + ex.Message);
                }
                progress(i + 1, files.Count, file.ViewLabel);
            }

            return result;
        }

        /// <summary>Null when the fallback pseudo-setup is selected (document has no PDF setups).</summary>
        public static IList<TableCellCombinedParameterData>? GetNamingRule(Document doc, string pdfSetupName)
        {
            if (pdfSetupName == DwgExportDefaults.FallbackPdfSetupName) return null;

            var settings = new FilteredElementCollector(doc)
                .OfClass(typeof(ExportPDFSettings))
                .Cast<ExportPDFSettings>()
                .FirstOrDefault(s => s.Name == pdfSetupName)
                ?? throw new InvalidOperationException(
                    "PDF export setup '" + pdfSetupName + "' no longer exists in this document.");
            return settings.GetOptions().GetNamingRule();
        }

        private static DWGExportOptions GetDwgOptions(Document doc, string dwgSetupName)
        {
            if (dwgSetupName == DwgExportDefaults.DefaultDwgSetupName) return new DWGExportOptions();

            var settings = new FilteredElementCollector(doc)
                .OfClass(typeof(ExportDWGSettings))
                .Cast<ExportDWGSettings>()
                .FirstOrDefault(s => s.Name == dwgSetupName)
                ?? throw new InvalidOperationException(
                    "DWG export setup '" + dwgSetupName + "' no longer exists in this document.");
            return settings.GetDWGExportOptions();
        }

        /// <summary>Sheets get the naming rule; non-sheet views (rule params don't apply) get their view name.</summary>
        private static string FileNameFor(Document doc, View view, IList<TableCellCombinedParameterData>? rule)
        {
            var raw = view is ViewSheet sheet
                ? FileNameComposer.Compose(NamingRuleEvaluator.ResolveForSheet(doc, sheet, rule))
                : view.Name;
            return FileNameComposer.Sanitize(raw);
        }

        private static string Label(View view)
            => view is ViewSheet sheet ? sheet.SheetNumber + " - " + sheet.Name : view.Name;

        public static bool DwgFileExists(string folder, string fileName)
            => File.Exists(Path.Combine(folder, fileName + ".dwg"));
    }
}

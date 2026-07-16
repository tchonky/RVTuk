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
            var dwgOptions = request.ExportDwg ? GetDwgOptions(doc, request.DwgSetupName) : null;
            var pdfOptions = request.ExportPdf ? GetPdfOptions(doc, request.PdfSetupName) : null;
            var tag = request.ExportDwg && request.ExportPdf ? " (DWG+PDF)"
                : request.ExportPdf ? " (PDF)" : " (DWG)";
            var result = new DwgExportResult();

            for (int i = 0; i < files.Count; i++)
            {
                var (id, file) = files[i];
                if (dwgOptions != null)
                {
                    try
                    {
                        var ok = doc.Export(request.OutputFolder, file.FileName,
                            new List<ElementId> { id }, dwgOptions);
                        if (ok) result.ExportedCount++;
                        else result.Errors.Add(file.ViewLabel + " (DWG): Revit reported the export failed.");
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add(file.ViewLabel + " (DWG): " + ex.Message);
                    }
                }
                if (pdfOptions != null)
                {
                    try
                    {
                        // Revit evaluates the setup's naming rule itself, so the .pdf
                        // basename matches the native PDF export byte for byte.
                        var ok = doc.Export(request.OutputFolder,
                            new List<ElementId> { id }, pdfOptions);
                        if (ok) result.ExportedCount++;
                        else result.Errors.Add(file.ViewLabel + " (PDF): Revit reported the export failed.");
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add(file.ViewLabel + " (PDF): " + ex.Message);
                    }
                }
                progress(i + 1, files.Count, file.ViewLabel + tag);
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

        /// <summary>Options for the per-sheet PDF calls: the chosen setup's own options with
        /// Combine forced off (one PDF per sheet, names mirror the DWGs 1:1). The fallback
        /// pseudo-setup gets an explicit "Sheet Number - Sheet Name" rule so the pairing
        /// holds in documents without saved PDF setups.</summary>
        private static PDFExportOptions GetPdfOptions(Document doc, string pdfSetupName)
        {
            if (pdfSetupName == DwgExportDefaults.FallbackPdfSetupName)
            {
                var number = TableCellCombinedParameterData.Create();
                number.ParamId = new ElementId(BuiltInParameter.SHEET_NUMBER);
                number.Separator = " - ";
                var name = TableCellCombinedParameterData.Create();
                name.ParamId = new ElementId(BuiltInParameter.SHEET_NAME);

                var fallback = new PDFExportOptions { Combine = false };
                fallback.SetNamingRule(new List<TableCellCombinedParameterData> { number, name });
                return fallback;
            }

            var settings = new FilteredElementCollector(doc)
                .OfClass(typeof(ExportPDFSettings))
                .Cast<ExportPDFSettings>()
                .FirstOrDefault(s => s.Name == pdfSetupName)
                ?? throw new InvalidOperationException(
                    "PDF export setup '" + pdfSetupName + "' no longer exists in this document.");
            var options = settings.GetOptions();
            options.Combine = false;
            return options;
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

        /// <summary>Whether any output of the requested formats already exists for this name.</summary>
        public static bool OutputFileExists(string folder, string fileName, DwgExportRequest request)
            => (request.ExportDwg && File.Exists(Path.Combine(folder, fileName + ".dwg")))
            || (request.ExportPdf && File.Exists(Path.Combine(folder, fileName + ".pdf")));
    }
}

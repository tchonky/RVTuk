using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RVTuk.Core.DwgExporter;

namespace RVTuk.Revit.DwgExporter
{
    /// <summary>The two naming rules a run uses, already read from the chosen setups. A null
    /// rule means the built-in fallback; UseViewName means non-sheet views take their own name.</summary>
    internal class NamingRules
    {
        public IList<TableCellCombinedParameterData>? Sheet { get; set; }
        public IList<TableCellCombinedParameterData>? View { get; set; }
        public bool UseViewName { get; set; } = true;
    }

    /// <summary>
    /// Resolves the requested export range to concrete views + filenames, and runs the export
    /// one view per Document.Export call — the multi-view overload invents its own filenames,
    /// and per-view calls give us exact names, progress, and per-view error capture. Every
    /// method takes the Document explicitly so any open model can be planned and exported,
    /// not only the active one.
    /// </summary>
    internal static class SheetDwgExporter
    {
        /// <summary>Reads both chosen setups' naming rules out of one document.</summary>
        public static NamingRules ReadNamingRules(Document doc, DwgExportRequest request) => new NamingRules
        {
            Sheet = GetNamingRule(doc, request.SheetNamingSetupName),
            View = request.ViewNamingSetupName == DwgExportDefaults.ViewNameNamingName
                ? null
                : GetNamingRule(doc, request.ViewNamingSetupName),
            UseViewName = request.ViewNamingSetupName == DwgExportDefaults.ViewNameNamingName,
        };

        /// <summary>The views/sheets the named set resolves to, with evaluated filenames (no extension).</summary>
        public static List<(ElementId Id, PlannedExportFile File)> PlanFiles(
            Document doc, DwgExportRequest request, NamingRules rules, string modelTitle,
            out List<string> skipped)
        {
            var result = new List<(ElementId, PlannedExportFile)>();
            skipped = new List<string>();

            var set = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheetSet))
                .Cast<ViewSheetSet>()
                .FirstOrDefault(s => s.Name == request.SheetSetName)
                ?? throw new InvalidOperationException(
                    "View/sheet set '" + request.SheetSetName + "' no longer exists in this document.");

            foreach (View view in set.Views)
            {
                if (view is ViewSheet || view.CanBePrinted)
                    result.Add((view.Id, Plan(doc, view, rules, modelTitle)));
                else
                    skipped.Add(view.Name);
            }

            return result
                .OrderBy(x => x.Item2.FileName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>The active window's view — only ever the active document, so no model title.</summary>
        public static List<(ElementId Id, PlannedExportFile File)> PlanCurrentWindow(
            UIDocument uidoc, NamingRules rules)
        {
            var view = uidoc.ActiveGraphicalView
                ?? throw new InvalidOperationException("The active window is not an exportable graphical view.");
            return new List<(ElementId, PlannedExportFile)> { (view.Id, Plan(uidoc.Document, view, rules, "")) };
        }

        private static PlannedExportFile Plan(Document doc, View view, NamingRules rules, string modelTitle)
        {
            var isSheet = view is ViewSheet;
            var usedFallback = false;
            string name;

            if (isSheet)
            {
                name = FileNameComposer.Compose(NamingRuleEvaluator.ResolveForView(doc, view, rules.Sheet));
            }
            else if (rules.UseViewName)
            {
                name = view.Name;
            }
            else
            {
                name = FileNameComposer.Compose(NamingRuleEvaluator.ResolveForView(doc, view, rules.View));
                // A rule written for sheets can resolve to nothing on a view. Falling back to
                // the view's own name keeps the files apart; Sanitize's generic "Sheet" would
                // collapse every such view onto one name and abort the run as a duplicate.
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = view.Name;
                    usedFallback = true;
                }
            }

            return new PlannedExportFile
            {
                ModelTitle = modelTitle,
                ViewLabel = Label(view),
                FileName = FileNameComposer.Sanitize(name),
                IsSheet = isSheet,
                UsedViewNameFallback = usedFallback,
            };
        }

        public static DwgExportResult Export(
            Document doc,
            DwgExportRequest request,
            List<(ElementId Id, PlannedExportFile File)> files,
            DWGExportOptions? dwgOptions,
            PDFExportOptions? sheetPdfOptions,
            PDFExportOptions? viewPdfOptions,
            Action<int, int, string> progress)
        {
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
                        else result.Errors.Add(file.Source + " (DWG): Revit reported the export failed.");
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add(file.Source + " (DWG): " + ex.Message);
                    }
                }

                var pdfOptions = file.IsSheet ? sheetPdfOptions : viewPdfOptions;
                if (pdfOptions != null)
                {
                    try
                    {
                        // Revit evaluates the setup's naming rule itself, so the .pdf
                        // basename matches the native PDF export byte for byte.
                        var ok = doc.Export(request.PdfFolder, new List<ElementId> { id }, pdfOptions);
                        if (ok) result.ExportedCount++;
                        else result.Errors.Add(file.Source + " (PDF): Revit reported the export failed.");
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add(file.Source + " (PDF): " + ex.Message);
                    }
                }

                if (file.UsedViewNameFallback)
                {
                    result.Notes.Add(file.Source +
                        ": the views naming rule produced nothing here, so the view name was used.");
                }

                progress(i + 1, files.Count, file.Source + tag);
            }

            return result;
        }

        /// <summary>Null when the fallback pseudo-setup is selected (document has no PDF setups).</summary>
        public static IList<TableCellCombinedParameterData>? GetNamingRule(Document doc, string pdfSetupName)
        {
            if (pdfSetupName == DwgExportDefaults.FallbackPdfSetupName) return null;

            var settings = ExportPDFSettings.FindByName(doc, pdfSetupName)
                ?? throw new InvalidOperationException(
                    "PDF export setup '" + pdfSetupName + "' no longer exists in this document.");
            return settings.GetOptions().GetNamingRule();
        }

        /// <summary>
        /// Options for the per-view PDF calls: the chosen setup's own options with Combine
        /// forced off (one PDF per view, names mirror the DWGs 1:1).
        /// <paramref name="forViews"/> with the "&lt;View Name&gt;" entry chosen borrows the
        /// sheets setup's page settings and swaps in the view-name rule — the user's paper and
        /// quality choices shouldn't change just because an item is a view.
        /// </summary>
        public static PDFExportOptions GetPdfOptions(
            Document doc, string namingSetupName, NamingRules rules, bool forViews)
        {
            if (forViews && rules.UseViewName)
            {
                var borrowed = BaseOptions(doc, namingSetupName);
                borrowed.SetNamingRule(NamingRuleEvaluator.ViewNameRule());
                return borrowed;
            }
            return BaseOptions(doc, namingSetupName);
        }

        private static PDFExportOptions BaseOptions(Document doc, string pdfSetupName)
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

            var settings = ExportPDFSettings.FindByName(doc, pdfSetupName)
                ?? throw new InvalidOperationException(
                    "PDF export setup '" + pdfSetupName + "' no longer exists in this document.");
            var options = settings.GetOptions();
            options.Combine = false;
            return options;
        }

        public static DWGExportOptions GetDwgOptions(Document doc, string dwgSetupName)
        {
            if (dwgSetupName == DwgExportDefaults.DefaultDwgSetupName) return new DWGExportOptions();

            var settings = ExportDWGSettings.FindByName(doc, dwgSetupName)
                ?? throw new InvalidOperationException(
                    "DWG export setup '" + dwgSetupName + "' no longer exists in this document.");
            return settings.GetDWGExportOptions();
        }

        private static string Label(View view)
            => view is ViewSheet sheet ? sheet.SheetNumber + " - " + sheet.Name : view.Name;

        /// <summary>Whether any output of the requested formats already exists — each format
        /// checked in its own folder, which may or may not be the same one.</summary>
        public static bool OutputFileExists(DwgExportRequest request, string fileName)
            => (request.ExportDwg && File.Exists(Path.Combine(request.OutputFolder, fileName + ".dwg")))
            || (request.ExportPdf && File.Exists(Path.Combine(request.PdfFolder, fileName + ".pdf")));
    }
}

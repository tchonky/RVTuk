using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RVTuk.Core.Config;
using RVTuk.Core.Database;
using RVTuk.Core.Models.Comparison;
using RVTuk.Core.Serialization;
using RVTuk.Revit.Extraction;
using RVTuk.UI.ViewModels;
using RVTuk.UI.Views;

namespace RVTuk.Revit.Commands
{
    /// <summary>Opens the Project Comparator. Builds the delegates that wrap the snapshot
    /// ExternalEvents + the SQLite store, so the UI never references the Revit API.</summary>
    [Transaction(TransactionMode.Manual)]
    public class CompareProjectsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            if (System.Windows.Application.Current == null)
            {
                new System.Windows.Application
                {
                    ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown
                };
            }

            var config = ConfigManager.LoadConfig();
            if (!ConfigManager.IsConfigured(config))
            {
                var settings = new SettingsWindow();
                settings.ShowDialog();
                config = ConfigManager.LoadConfig();
                if (!ConfigManager.IsConfigured(config))
                    return Result.Cancelled;
            }

            if (Application.ComparatorWindow != null && Application.ComparatorWindow.IsLoaded)
            {
                Application.ComparatorWindow.Activate();
                return Result.Succeeded;
            }

            var uiapp = commandData.Application;
            var extractors = new List<ICategoryExtractor> { new ViewTemplateExtractor() };
            var stdDbPath = config.StandardsDatabasePath;

            // --- list open project documents (called on the UI/main thread) ---
            Func<IReadOnlyList<string>> getOpenDocuments = () =>
            {
                var names = new List<string>();
                try
                {
                    foreach (Document d in uiapp.Application.Documents)
                        if (!d.IsLinked && !d.IsFamilyDocument) names.Add(d.Title);
                }
                catch { /* best effort */ }
                return names;
            };

            // --- capture an open document (active when title is null) via ExternalEvent ---
            Func<string?, CapturedSnapshot> captureOpenDoc = title =>
            {
                Application.CaptureSnapshotHandler.Prepare(title, extractors);
                Application.CaptureSnapshotEvent.Raise();
                Application.CaptureSnapshotHandler.WaitForCompletion();
                return new CapturedSnapshot
                {
                    Meta = Application.CaptureSnapshotHandler.ResultMeta ?? new SnapshotMeta(),
                    Categories = Application.CaptureSnapshotHandler.ResultCategories,
                    Error = Application.CaptureSnapshotHandler.Error,
                };
            };

            // --- capture a closed file (background-open) via ExternalEvent ---
            Func<string, CapturedSnapshot> captureFile = path =>
            {
                Application.OpenModelSnapshotHandler.Prepare(path, extractors);
                Application.OpenModelSnapshotEvent.Raise();
                Application.OpenModelSnapshotHandler.WaitForCompletion();
                return new CapturedSnapshot
                {
                    Meta = Application.OpenModelSnapshotHandler.ResultMeta ?? new SnapshotMeta(),
                    Categories = Application.OpenModelSnapshotHandler.ResultCategories,
                    Error = Application.OpenModelSnapshotHandler.Error,
                };
            };

            Func<string?> pickFile = () =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "Revit models (*.rvt;*.rte)|*.rvt;*.rte|All files (*.*)|*.*",
                    Title = "Select a Revit model to compare",
                };
                return dlg.ShowDialog() == true ? dlg.FileName : null;
            };

            Action<string> saveReportHtml = html =>
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "HTML report (*.html)|*.html",
                    FileName = "RVTuk-Comparator-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".html",
                };
                if (dlg.ShowDialog() == true)
                    File.WriteAllText(dlg.FileName, html);
            };

            Func<StandardSnapshot> loadStandard = () =>
            {
                try
                {
                    using var repo = new SnapshotRepository(stdDbPath);
                    var stdMeta = repo.ListSnapshots().FirstOrDefault(m => m.SourceKind == "Standard");
                    if (stdMeta == null) return new StandardSnapshot();
                    var cats = repo.LoadCategories(stdMeta.Id, DeserializeCategory);
                    var std = new StandardSnapshot { Meta = stdMeta };
                    std.Categories.AddRange(cats);
                    return std;
                }
                catch { return new StandardSnapshot(); }
            };

            Action<StandardSnapshot> saveStandard = std =>
            {
                using var repo = new SnapshotRepository(stdDbPath);
                std.Meta.SourceKind = "Standard";
                std.Meta.IsMutable = true;
                if (string.IsNullOrEmpty(std.Meta.SourceName)) std.Meta.SourceName = "The Standard";
                std.Meta.CapturedUtc = DateTime.UtcNow.ToString("o");
                var payloads = std.Categories.Select(ToPayload).ToList();
                repo.SaveSnapshot(std.Meta, payloads);
            };

            try
            {
                var vm = new ComparatorViewModel(
                    getOpenDocuments, captureOpenDoc, captureFile, pickFile,
                    saveReportHtml, loadStandard, saveStandard);
                var window = new ComparatorWindow(vm);
                Application.ComparatorWindow = window;
                window.Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("RVTuk – Project Comparator",
                    $"Failed to open the comparator:\n\n{ex.GetType().Name}: {ex.Message}");
                return Result.Failed;
            }
        }

        private static CategorySnapshot DeserializeCategory(string categoryId, string json)
        {
            if (categoryId == ViewTemplatesSnapshot.Category)
                return SnapshotJson.Deserialize<ViewTemplatesSnapshot>(json);
            // Unknown category in this build — return an empty placeholder so load doesn't fail.
            return new ViewTemplatesSnapshot();
        }

        private static CategoryPayload ToPayload(CategorySnapshot c)
        {
            if (c is ViewTemplatesSnapshot vt)
                return new CategoryPayload { CategoryId = c.CategoryId, PayloadJson = SnapshotJson.Serialize(vt), ItemCount = vt.Templates.Count };
            return new CategoryPayload { CategoryId = c.CategoryId, PayloadJson = "{}", ItemCount = 0 };
        }
    }
}

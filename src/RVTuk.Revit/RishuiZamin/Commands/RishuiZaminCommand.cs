using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RVTuk.Core.RishuiZamin;
using RVTuk.UI.RishuiZamin.ViewModels;
using RVTuk.UI.RishuiZamin.Views;

namespace RVTuk.Revit.RishuiZamin.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class RishuiZaminCommand : IExternalCommand
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

            // Single-instance: bring an open window to front instead of opening another.
            if (Application.RishuiZaminWindow != null && Application.RishuiZaminWindow.IsLoaded)
            {
                Application.RishuiZaminWindow.Activate();
                return Result.Succeeded;
            }

            // Extract areas from the open sheet via the ExternalEvent ping-pong. This BLOCKS until
            // Revit's main thread services it, so the view model calls it on a background thread.
            Func<IReadOnlyList<(long Id, AreaRecord Rec)>> extract = () =>
            {
                Application.AreaExtractHandler.Reset();
                Application.AreaExtractEvent.Raise();
                Application.AreaExtractHandler.WaitForCompletion();
                if (Application.AreaExtractHandler.Error is { } error)
                {
                    throw new InvalidOperationException("Area extraction failed: " + error.Message, error);
                }
                var extracted = Application.AreaExtractHandler.Result
                    .Select(e => (e.ElementId, e.Record))
                    .ToList();

                if (extracted.Count == 0)
                {
                    var diag = Application.AreaExtractHandler.Diagnostics;
                    var reason = diag.AreaPlanCount == 0
                        ? "the sheet has no Area Plan viewports on it"
                        : $"found {diag.RawAreaCount} Area element(s) across {diag.AreaPlanCount} area plan(s), but none are placed (Area > 0 with a valid boundary)";
                    throw new InvalidOperationException("No areas found — " + reason + ".");
                }

                return extracted;
            };

            // Select an area in the model — fire-and-forget (called on the UI thread; the event
            // runs on Revit's main thread once the click returns, so no WaitForCompletion here).
            Action<long> selectInModel = id =>
            {
                Application.SelectAreaHandler.Prepare(id);
                Application.SelectAreaEvent.Raise();
            };

            // Bind the robot's text parameters to Areas and create/top-up the usage key
            // schedules. Blocks until Revit's main thread services it — the view model calls
            // it on a background thread, like extract.
            Func<(bool ok, string msg)> setupUsageKeys = () =>
            {
                Application.SetupUsageKeysHandler.Reset();
                Application.SetupUsageKeysEvent.Raise();
                Application.SetupUsageKeysHandler.WaitForCompletion();
                return Application.SetupUsageKeysHandler.Result;
            };

            // Export is pure (validate + write files); safe to run on the UI thread. The
            // RZ_FRAME must be the real sheet outline, so inject the title-block paper size
            // (captured during the last extract) scaled to drawing units here, where the
            // user-chosen Scale is final.
            Func<IReadOnlyList<AreaRecord>, RishuiZaminConfig, (bool ok, string msg)> export =
                (records, cfg) =>
                {
                    var diag = Application.AreaExtractHandler.Diagnostics;
                    cfg.SheetWidthCm = diag.SheetPaperWidthCm * cfg.Scale;
                    cfg.SheetHeightCm = diag.SheetPaperHeightCm * cfg.Scale;
                    return RishuiZaminExporter.Export(records, cfg);
                };

            var crashLogPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RVTuk", "crash.log");

            try
            {
                var vm = new RishuiZaminViewModel(extract, selectInModel, export, setupUsageKeys);
                if (string.IsNullOrWhiteSpace(vm.FileBaseNameText))
                {
                    vm.FileBaseNameText = commandData.Application.ActiveUIDocument.Document.Title;
                }
                var window = new RishuiZaminWindow(vm);
                window.Closed += (s, e) => Application.RishuiZaminWindow = null;
                Application.RishuiZaminWindow = window;
                window.Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(crashLogPath)!);
                    File.AppendAllText(crashLogPath,
                        $"\n[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] AreaCalc: {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}\n");
                }
                catch { /* logging must never itself crash */ }

                TaskDialog.Show("RVTuk – Area Calc",
                    $"Failed to open Area Calc:\n\n{ex.GetType().Name}: {ex.Message}");
                return Result.Failed;
            }
        }
    }
}

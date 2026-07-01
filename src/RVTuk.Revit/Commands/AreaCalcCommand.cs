using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RVTuk.Core.AreaSubmission;
using RVTuk.UI.ViewModels;
using RVTuk.UI.Views;

namespace RVTuk.Revit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class AreaCalcCommand : IExternalCommand
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
            if (Application.AreaCalcWindow != null && Application.AreaCalcWindow.IsLoaded)
            {
                Application.AreaCalcWindow.Activate();
                return Result.Succeeded;
            }

            // Extract areas from the open sheet via the ExternalEvent ping-pong. This BLOCKS until
            // Revit's main thread services it, so the view model calls it on a background thread.
            Func<IReadOnlyList<(long Id, AreaRecord Rec)>> extract = () =>
            {
                Application.AreaExtractHandler.Reset();
                Application.AreaExtractEvent.Raise();
                Application.AreaExtractHandler.WaitForCompletion();
                return Application.AreaExtractHandler.Result
                    .Select(e => (e.ElementId, e.Record))
                    .ToList();
            };

            // Select an area in the model — fire-and-forget (called on the UI thread; the event
            // runs on Revit's main thread once the click returns, so no WaitForCompletion here).
            Action<long> selectInModel = id =>
            {
                Application.SelectAreaHandler.Prepare(id);
                Application.SelectAreaEvent.Raise();
            };

            // Export is pure (validate + write files); safe to run on the UI thread.
            Func<IReadOnlyList<AreaRecord>, AreaSubmissionConfig, (bool ok, string msg)> export =
                (records, cfg) => AreaSubmissionExporter.Export(records, cfg);

            var crashLogPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RVTuk", "crash.log");

            try
            {
                var vm = new AreaSubmissionViewModel(extract, selectInModel, export);
                var window = new AreaSubmissionWindow(vm);
                window.Closed += (s, e) => Application.AreaCalcWindow = null;
                Application.AreaCalcWindow = window;
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

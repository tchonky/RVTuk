using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using RVTuk.Core.Models.Comparison;
using RVTuk.Revit.Extraction;

namespace RVTuk.Revit.ExternalEvents
{
    /// <summary>
    /// Background-opens a .rvt/.rte detached + worksets-closed, extracts standards, then closes it.
    /// Safeguards: version guard (skip newer-than-running files), dialog suppression so a modal can't
    /// hang the headless open, and a guaranteed Close in finally.
    ///
    /// Runtime-verifiable only inside Revit. Never background-open two large models at once.
    /// </summary>
    public class OpenModelSnapshotEventHandler : IExternalEventHandler
    {
        private readonly ManualResetEventSlim _done = new ManualResetEventSlim(false);

        public string FilePath { get; set; } = string.Empty;
        public List<ICategoryExtractor> Extractors { get; set; } = new List<ICategoryExtractor>();

        public SnapshotMeta? ResultMeta { get; private set; }
        public List<CategorySnapshot> ResultCategories { get; private set; } = new List<CategorySnapshot>();
        public string? Error { get; private set; }

        public void Prepare(string filePath, List<ICategoryExtractor> extractors)
        {
            FilePath = filePath;
            Extractors = extractors;
            ResultMeta = null;
            ResultCategories = new List<CategorySnapshot>();
            Error = null;
            _done.Reset();
        }

        public void WaitForCompletion() => _done.Wait();
        public bool WaitForCompletion(int timeoutMs) => _done.Wait(timeoutMs);

        public void Execute(UIApplication app)
        {
            Document? doc = null;
            EventHandler<DialogBoxShowingEventArgs>? dialogSuppressor = null;
            try
            {
                if (IsNewerThanRunning(app, FilePath, out var msg))
                {
                    Error = msg;
                    return;
                }

                // Suppress modal dialogs (e.g. "model upgraded", audit prompts) so the open
                // can't block forever on the background thread waiting for this handler.
                dialogSuppressor = (s, e) => { try { e.OverrideResult(1); } catch { /* best effort */ } };
                app.DialogBoxShowing += dialogSuppressor;

                var modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(FilePath);
                var options = new OpenOptions
                {
                    DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets,
                };
                // We only need authoring standards, not geometry — close worksets to slash load/memory.
                options.SetOpenWorksetsConfiguration(new WorksetConfiguration(WorksetConfigurationOption.CloseAllWorksets));

                doc = app.Application.OpenDocumentFile(modelPath, options);

                ResultMeta = SnapshotMetaFactory.Build(app, doc, "Project", FilePath);
                foreach (var extractor in Extractors)
                {
                    try { ResultCategories.Add(extractor.Extract(doc)); }
                    catch { /* skip a category whose extraction fails */ }
                }
            }
            catch (Exception e)
            {
                Error = e.Message;
            }
            finally
            {
                try { doc?.Close(false); } catch { /* best effort */ }
                if (dialogSuppressor != null)
                {
                    try { app.DialogBoxShowing -= dialogSuppressor; } catch { /* best effort */ }
                }
                _done.Set();
            }
        }

        /// <summary>Best-effort version guard. Opening a file newer than the running Revit can crash
        /// or silently upgrade it. Parses BasicFileInfo.SavedInVersion (format varies across releases).</summary>
        private static bool IsNewerThanRunning(UIApplication app, string path, out string message)
        {
            message = string.Empty;
            try
            {
                var info = BasicFileInfo.Extract(path);
                // Format is a version string such as "Autodesk Revit 2024 (...)" or "2024".
                var saved = info?.Format;
                if (string.IsNullOrEmpty(saved)) return false;

                var digits = new string(saved!.Where(char.IsDigit).ToArray());
                if (digits.Length >= 4) digits = digits.Substring(0, 4);
                if (int.TryParse(digits, out var fileYear)
                    && int.TryParse(app.Application.VersionNumber, out var running)
                    && fileYear > running)
                {
                    message = $"\"{System.IO.Path.GetFileName(path)}\" was saved in Revit {fileYear}, " +
                              $"newer than the running Revit {running}. Skipped to avoid an unsafe upgrade.";
                    return true;
                }
            }
            catch { /* if we can't read the version, let the open attempt proceed */ }
            return false;
        }

        public string GetName() => "RVTuk.OpenModelSnapshotEventHandler";
    }
}

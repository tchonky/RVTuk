using System;
using System.IO;
using System.Text;
using System.Threading;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RVTuk.Core.Config;
using RVTuk.Core.Database;
using RVTuk.Core.Extraction;
using RVTuk.Revit.Extraction;
using RVTuk.UI.Views;

namespace RVTuk.Revit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class IndexLibraryCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var config = ConfigManager.LoadConfig();
            if (!ConfigManager.IsConfigured(config))
            {
                new SettingsWindow().ShowDialog();
                config = ConfigManager.LoadConfig();
                if (!ConfigManager.IsConfigured(config))
                    return Result.Cancelled;
            }
            RunScan(commandData.Application, config, includeThumbnails: true, includeParameters: true);
            return Result.Succeeded;
        }

        /// <param name="includeThumbnails">
        /// Re-extract thumbnails for families that are new/changed or simply missing one. A plain
        /// file read — never touches Revit's main thread.
        /// </param>
        /// <param name="includeParameters">
        /// Re-extract category/parameters (via the Revit engine — the slow path) for families
        /// that are new/changed or simply missing them.
        /// </param>
        /// <remarks>
        /// Both false is a filenames-only sync: add new families, prune deleted ones, no
        /// extraction. Non-destructive either way — curated data (instructions, tags, favourites,
        /// custom thumbnails, gallery) is always preserved.
        /// </remarks>
        public static void RunScan(UIApplication uiApp, AppConfig config, bool includeThumbnails, bool includeParameters)
        {
            var progressWindow = new IndexProgressWindow();
            var vm = progressWindow.ViewModel;
            var handler = Application.IndexingHandler;
            var externalEvent = Application.IndexingEvent;
            var extractor = new FamilyMetadataExtractor(uiApp.Application);

            progressWindow.Show();
            var cancellationToken = vm.Start();

            ThreadPool.QueueUserWorkItem(_ =>
            {
                int updated = 0;
                int thumbnailOnly = 0;
                int skippedLong = 0;
                int skippedIgnored = 0;
                try
                {
                    using var repo = new IndexRepository(config.DatabasePath);
                    var indexer = new FamilyIndexer(repo, config.LibraryFolderPath, config.IgnoredSubfolders);

                    var workItems = indexer.Scan(
                        (fileName, current, total) => vm.UpdateProgress(fileName, current, total),
                        cancellationToken,
                        includeThumbnails,
                        includeParameters);

                    updated = workItems.Count;
                    thumbnailOnly = indexer.ThumbnailOnlyCount;
                    skippedLong = indexer.SkippedLongPath;
                    skippedIgnored = indexer.SkippedIgnored;

                    // Phase 2 — pull category/parameters from each family via the Revit engine.
                    // Only families needing parameters ever reach here; thumbnails-only and
                    // filenames-only families were already fully handled in Phase 1 above, so a
                    // scan with Update parameters unchecked never touches Revit's main thread.
                    for (int i = 0; i < workItems.Count; i++)
                    {
                        if (cancellationToken.IsCancellationRequested) break;
                        var item = workItems[i];
                        vm.UpdateProgress(Path.GetFileName(item.FullPath), i + 1, workItems.Count);
                        // IndexingGate: the browser's one-family rescan shares this handler.
                        lock (Application.IndexingGate)
                        {
                            handler.PrepareAndWait(item, repo, extractor);
                            externalEvent.Raise();
                            handler.WaitForCompletion();
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                        TaskDialog.Show("RVTuk – Error", ex.Message));
                }
                finally
                {
                    vm.Finish();
                    int finalUpdated = updated;
                    int finalThumbnailOnly = thumbnailOnly;
                    int finalSkippedLong = skippedLong;
                    int finalSkippedIgnored = skippedIgnored;

                    WriteScanLog(config, finalUpdated, finalThumbnailOnly, finalSkippedLong, finalSkippedIgnored);

                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        progressWindow.Close();

                        var msg = new StringBuilder();
                        msg.Append($"Indexed: {finalUpdated} families.");
                        if (finalThumbnailOnly > 0)
                            msg.Append($"\nThumbnails updated: {finalThumbnailOnly}");
                        if (finalSkippedLong > 0)
                            msg.Append($"\nSkipped (path too long): {finalSkippedLong}");
                        if (finalSkippedIgnored > 0)
                            msg.Append($"\nSkipped (ignored folder): {finalSkippedIgnored}");

                        TaskDialog.Show("RVTuk – Scan Complete", msg.ToString());
                    });
                }
            });
        }

        /// <summary>
        /// Writes a small last-scan.log next to the database so the admin can see why some
        /// families were skipped. Best-effort: any failure (e.g. read-only share) is swallowed.
        /// </summary>
        private static void WriteScanLog(AppConfig config, int indexed, int thumbnailOnly, int skippedLong, int skippedIgnored)
        {
            try
            {
                string dir = Path.GetDirectoryName(config.DatabasePath);
                if (string.IsNullOrEmpty(dir)) return;

                string logPath = Path.Combine(dir, "last-scan.log");
                var sb = new StringBuilder();
                sb.AppendLine($"Scan finished: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"Indexed families:          {indexed}");
                sb.AppendLine($"Thumbnails updated:        {thumbnailOnly}");
                sb.AppendLine($"Skipped (path too long):   {skippedLong}");
                sb.AppendLine($"Skipped (ignored folder):  {skippedIgnored}");
                File.WriteAllText(logPath, sb.ToString());
            }
            catch
            {
                // Logging is best-effort; never let it break the scan result dialog.
            }
        }
    }
}

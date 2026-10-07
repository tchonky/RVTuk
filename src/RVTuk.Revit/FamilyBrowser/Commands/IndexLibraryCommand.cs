using System;
using System.IO;
using System.Text;
using System.Threading;
using Autodesk.Revit.UI;
using RVTuk.Core.Shared.Config;
using RVTuk.Core.FamilyBrowser.Database;
using RVTuk.Core.FamilyBrowser.Extraction;
using RVTuk.Revit.FamilyBrowser.Extraction;
using RVTuk.UI.FamilyBrowser.Views;

namespace RVTuk.Revit.FamilyBrowser.Commands
{
    /// <summary>The library scan, started from the browser's embedded Settings panel.</summary>
    internal static class IndexLibraryCommand
    {
        /// <param name="owner">
        /// The browser: owning the progress window keeps it above the browser, and closing the
        /// browser closes it — which cancels the scan.
        /// </param>
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
        /// custom thumbnails) is always preserved.
        /// </remarks>
        public static void RunScan(UIApplication uiApp, System.Windows.Window? owner, AppConfig config,
            bool includeThumbnails, bool includeParameters, Action? onDone = null)
        {
            var progressWindow = new IndexProgressWindow { Owner = owner };
            bool progressClosed = false;
            progressWindow.Closed += (_, __) => progressClosed = true;
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
                bool failed = false;
                try
                {
                    AppConfig.MigrateLegacyDbFolder(config.LibraryFolderPath);
                    using var repo = new IndexRepository(config.DatabasePath);
                    var indexer = new FamilyIndexer(repo, config.LibraryFolderPath,
                        config.IgnoredSubfolders, config.IgnoredFilePatterns);

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
                            BrowseLibraryCommand.Raise(externalEvent);
                            handler.WaitForCompletion();
                            // A failed DB write (locked, disk full, share gone) fails every
                            // family after it the same way — stop and say so.
                            if (handler.Error != null) throw new InvalidOperationException(handler.Error);
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    failed = true;
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                        TaskDialog.Show("RVTuk – Scan failed", ex.Message));
                }
                finally
                {
                    vm.Finish();
                    int finalUpdated = updated;
                    int finalThumbnailOnly = thumbnailOnly;
                    int finalSkippedLong = skippedLong;
                    int finalSkippedIgnored = skippedIgnored;

                    WriteScanLog(config, finalUpdated, finalThumbnailOnly, finalSkippedLong, finalSkippedIgnored);

                    bool cancelled = cancellationToken.IsCancellationRequested;
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        if (!progressClosed) progressWindow.Close();
                        onDone?.Invoke();
                        if (failed) return; // the error dialog already said what happened

                        var msg = new StringBuilder();
                        msg.Append($"Indexed: {finalUpdated} families.");
                        if (finalThumbnailOnly > 0)
                            msg.Append($"\nThumbnails updated: {finalThumbnailOnly}");
                        if (finalSkippedLong > 0)
                            msg.Append($"\nSkipped (path too long): {finalSkippedLong}");
                        if (finalSkippedIgnored > 0)
                            msg.Append($"\nSkipped (ignored folder): {finalSkippedIgnored}");

                        TaskDialog.Show(cancelled ? "RVTuk – Scan Cancelled" : "RVTuk – Scan Complete", msg.ToString());
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

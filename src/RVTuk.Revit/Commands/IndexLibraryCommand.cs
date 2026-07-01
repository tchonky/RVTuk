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
            RunDeepScan(commandData.Application, config, forceReextractAll: false);
            return Result.Succeeded;
        }

        /// <param name="forceReextractAll">
        /// false = "Scan New &amp; Changed": re-extract only new/modified families (fast).
        /// true  = "Re-scan All Families": re-extract every family's parameters and original
        /// thumbnail, pruning families whose files are gone. Non-destructive — curated data
        /// (instructions, tags, favourites, custom thumbnails, gallery) is preserved.
        /// </param>
        public static void RunDeepScan(UIApplication uiApp, AppConfig config, bool forceReextractAll)
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
                int skippedLong = 0;
                int skippedIgnored = 0;
                try
                {
                    using var repo = new IndexRepository(config.DatabasePath);
                    var indexer = new FamilyIndexer(repo, config.LibraryFolderPath, config.IgnoredSubfolders);

                    var workItems = indexer.Scan(
                        (fileName, current, total) => vm.UpdateProgress(fileName, current, total),
                        cancellationToken,
                        forceReextractAll);

                    updated = workItems.Count;
                    skippedLong = indexer.SkippedLongPath;
                    skippedIgnored = indexer.SkippedIgnored;

                    // Phase 2 — pull category/parameters from each family via the Revit engine.
                    // This is the slow part, so drive the progress bar over the work items here
                    // (phase 1 above only covered the fast file walk + thumbnail read).
                    for (int i = 0; i < workItems.Count; i++)
                    {
                        if (cancellationToken.IsCancellationRequested) break;
                        var item = workItems[i];
                        vm.UpdateProgress(Path.GetFileName(item.FullPath), i + 1, workItems.Count);
                        handler.PrepareAndWait(item, repo, extractor);
                        externalEvent.Raise();
                        handler.WaitForCompletion();
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
                    int finalSkippedLong = skippedLong;
                    int finalSkippedIgnored = skippedIgnored;

                    WriteScanLog(config, finalUpdated, finalSkippedLong, finalSkippedIgnored);

                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        progressWindow.Close();

                        var msg = new StringBuilder();
                        msg.Append($"Indexed: {finalUpdated} families.");
                        if (finalSkippedLong > 0)
                            msg.Append($"\nSkipped (path too long): {finalSkippedLong}");
                        if (finalSkippedIgnored > 0)
                            msg.Append($"\nSkipped (ignored folder): {finalSkippedIgnored}");

                        TaskDialog.Show("RVTuk – Deep Scan Complete", msg.ToString());
                    });
                }
            });
        }

        /// <summary>
        /// Writes a small last-scan.log next to the database so the admin can see why some
        /// families were skipped. Best-effort: any failure (e.g. read-only share) is swallowed.
        /// </summary>
        private static void WriteScanLog(AppConfig config, int indexed, int skippedLong, int skippedIgnored)
        {
            try
            {
                string dir = Path.GetDirectoryName(config.DatabasePath);
                if (string.IsNullOrEmpty(dir)) return;

                string logPath = Path.Combine(dir, "last-scan.log");
                var sb = new StringBuilder();
                sb.AppendLine($"Deep scan finished: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"Indexed families:          {indexed}");
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

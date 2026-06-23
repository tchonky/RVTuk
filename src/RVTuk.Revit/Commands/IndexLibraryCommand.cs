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
            RunDeepScan(commandData.Application, config);
            return Result.Succeeded;
        }

        public static void RunDeepScan(UIApplication uiApp, AppConfig config)
        {
            var progressWindow = new IndexProgressWindow();
            var vm = progressWindow.ViewModel;
            var handler = Application.IndexingHandler;
            var externalEvent = Application.IndexingEvent;
            var extractor = new FamilyMetadataExtractor(uiApp.Application);

            vm.RebuildRequested += () =>
            {
                using var repo = new IndexRepository(config.DatabasePath);
                repo.ClearAll();
            };

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
                        cancellationToken);

                    updated = workItems.Count;
                    skippedLong = indexer.SkippedLongPath;
                    skippedIgnored = indexer.SkippedIgnored;

                    foreach (var item in workItems)
                    {
                        if (cancellationToken.IsCancellationRequested) break;
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

using System;
using System.Threading;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ReviTchucky.Core.Config;
using ReviTchucky.Core.Database;
using ReviTchucky.Core.Extraction;
using ReviTchucky.Revit.Extraction;
using ReviTchucky.UI.Views;

namespace ReviTchucky.Revit.Commands
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
                try
                {
                    using var repo = new IndexRepository(config.DatabasePath);
                    var indexer = new FamilyIndexer(repo, config.LibraryFolderPath);

                    var workItems = indexer.Scan(
                        (fileName, current, total) => vm.UpdateProgress(fileName, current, total),
                        cancellationToken);

                    updated = workItems.Count;

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
                        TaskDialog.Show("ReviTchucky – Error", ex.Message));
                }
                finally
                {
                    vm.Finish();
                    int finalUpdated = updated;
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        progressWindow.Close();
                        TaskDialog.Show("ReviTchucky – Deep Scan Complete",
                            $"Indexed: {finalUpdated} families.");
                    });
                }
            });
        }
    }
}

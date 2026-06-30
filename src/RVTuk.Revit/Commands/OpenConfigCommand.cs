using System;
using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RVTuk.Core.Config;
using RVTuk.UI.ViewModels;
using RVTuk.UI.Views;

namespace RVTuk.Revit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class OpenConfigCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            // Revit hosts the CLR but never creates a WPF Application; create one we own that
            // never auto-shuts-down, so closing a window can't take Revit down. (Same reasoning
            // as BrowseLibraryCommand.)
            if (System.Windows.Application.Current == null)
            {
                new System.Windows.Application
                {
                    ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown
                };
            }

            // Single-instance: bring an open Config window to front instead of opening another.
            if (Application.ConfigWindow != null && Application.ConfigWindow.IsLoaded)
            {
                Application.ConfigWindow.Activate();
                return Result.Succeeded;
            }

            var config = ConfigManager.LoadConfig();
            var uiApp = commandData.Application;

            // Deep-scan actions need the Revit UIApplication for metadata extraction. Reload config
            // at click time so a folder / ignored-list change made in the window is honoured.
            Action scanNewAndChanged = () =>
                IndexLibraryCommand.RunDeepScan(uiApp, ConfigManager.LoadConfig(), forceReextractAll: false);
            Action rescanAll = () =>
                IndexLibraryCommand.RunDeepScan(uiApp, ConfigManager.LoadConfig(), forceReextractAll: true);

            // If the Family Browser is open, refresh it after a library-folder change so it does
            // not keep showing the old library.
            Action onLibraryFolderChanged = () =>
            {
                try { Application.BrowserWindow?.ReloadConfig(); } catch { /* refresh is best-effort */ }
            };

            var crashLogPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RVTuk", "crash.log");

            try
            {
                var vm = new ConfigViewModel(config, scanNewAndChanged, rescanAll, onLibraryFolderChanged);
                var window = new ConfigWindow(vm);
                window.Closed += (s, e) => Application.ConfigWindow = null;
                Application.ConfigWindow = window; // set before Show() so re-entry finds it
                window.Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(crashLogPath)!);
                    File.AppendAllText(crashLogPath,
                        $"\n[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] OpenConfig: {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}\n");
                }
                catch { /* logging must never itself crash */ }

                TaskDialog.Show("RVTuk – Config",
                    $"Failed to open Config:\n\n{ex.GetType().Name}: {ex.Message}");
                return Result.Failed;
            }
        }
    }
}

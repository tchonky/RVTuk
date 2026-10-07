using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RVTuk.Core.Shared.Config;
using RVTuk.Core.FamilyBrowser.Database;
using RVTuk.Core.FamilyBrowser.Extraction;
using RVTuk.Core.FamilyBrowser.Models;
using RVTuk.Revit.FamilyBrowser.Extraction;
using RVTuk.UI.FamilyBrowser.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Interop;

namespace RVTuk.Revit.FamilyBrowser.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class BrowseLibraryCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            // Revit hosts the CLR but never creates a WPF Application, so
            // System.Windows.Application.Current is null in-process. Multiple paths here
            // (the DispatcherUnhandledException wiring below) and the deep-scan progress UI
            // (IndexProgressViewModel) dereference Current and would NullReferenceException.
            // Create one we own, set to never auto-shutdown so closing a window can't take
            // Revit down with it. Must run on Revit's main (STA) thread — which this is.
            if (System.Windows.Application.Current == null)
            {
                new System.Windows.Application
                {
                    ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown
                };
            }

            // Owning our windows by Revit's main window keeps them above Revit without Topmost,
            // which would also float them over Revit's own modal dialogs.
            var revitWindow = commandData.Application.MainWindowHandle;

            var config = ConfigManager.LoadConfig();
            if (!ConfigManager.IsConfigured(config))
            {
                var settings = new SettingsWindow();
                new WindowInteropHelper(settings).Owner = revitWindow;
                settings.ShowDialog();
                config = ConfigManager.LoadConfig();
                if (!ConfigManager.IsConfigured(config))
                    return Result.Cancelled;
            }

            // Bring existing window to front, or open a new one
            if (Application.BrowserWindow != null && Application.BrowserWindow.IsLoaded)
            {
                Application.BrowserWindow.Activate();
                return Result.Succeeded;
            }

            // Create delegates that wrap ExternalEvent ping-pong.
            // These lambdas live in the Revit project (which CAN reference ExternalEvent).
            // FamilyBrowserWindow (UI project) only sees Func<> delegates — no Revit types.
            Func<IReadOnlyList<ProjectFamilyInfo>> getProjectFamilies = () =>
            {
                Application.GetFamiliesHandler.Reset();
                Raise(Application.GetFamiliesEvent);
                Application.GetFamiliesHandler.WaitForCompletion();
                if (Application.GetFamiliesHandler.ErrorMessage is string error)
                    throw new InvalidOperationException(error);
                return Application.GetFamiliesHandler.Result;
            };

            Func<string, (bool Success, string? Error)> loadFamily = path =>
            {
                Application.LoadFamilyHandler.Prepare(path);
                Raise(Application.LoadFamilyEvent);
                Application.LoadFamilyHandler.WaitForCompletion();
                return (Application.LoadFamilyHandler.Success, Application.LoadFamilyHandler.ErrorMessage);
            };

            Func<string, (bool Success, string? Error)> openInFamilyEditor = path =>
            {
                Application.OpenFamilyEditorHandler.Prepare(path);
                Raise(Application.OpenFamilyEditorEvent);
                Application.OpenFamilyEditorHandler.WaitForCompletion();
                var error = Application.OpenFamilyEditorHandler.ErrorMessage;
                return (error == null, error);
            };

            // Both wrap the same EditProjectFamilyEventHandler singleton; the VM serializes
            // the two call sites with a lock, same as loads.
            Func<string, (bool Success, string? Error)> openModelFamilyInEditor = familyName =>
            {
                Application.EditProjectFamilyHandler.Prepare(familyName, null);
                Raise(Application.EditProjectFamilyEvent);
                Application.EditProjectFamilyHandler.WaitForCompletion();
                return (Application.EditProjectFamilyHandler.Success, Application.EditProjectFamilyHandler.ErrorMessage);
            };

            Func<string, string, (bool Success, string? Error)> saveFamilyToLibrary = (familyName, targetPath) =>
            {
                Application.EditProjectFamilyHandler.Prepare(familyName, targetPath);
                Raise(Application.EditProjectFamilyEvent);
                Application.EditProjectFamilyHandler.WaitForCompletion();
                return (Application.EditProjectFamilyHandler.Success, Application.EditProjectFamilyHandler.ErrorMessage);
            };

            // Type-preview images for model-only rows (no .rfa on disk to extract from).
            Func<IReadOnlyList<string>, IReadOnlyDictionary<string, byte[]>> getFamilyPreviews = familyNames =>
            {
                Application.GetFamilyPreviewsHandler.Prepare(familyNames);
                Raise(Application.GetFamilyPreviewsEvent);
                Application.GetFamilyPreviewsHandler.WaitForCompletion();
                return Application.GetFamilyPreviewsHandler.Result;
            };

            Application.CurrentUIApp = commandData.Application;
            var capturedUIApp = commandData.Application;

            // Runs a scan (used by the browser's embedded Settings panel — the ribbon Config
            // window was removed in favour of settings embedded directly in the browser), then
            // re-reads the browser's list so new and pruned families show without a manual ⟳.
            Action<bool, bool> scan = (includeThumbnails, includeParameters) =>
                IndexLibraryCommand.RunScan(capturedUIApp, Application.BrowserWindow, ConfigManager.LoadConfig(),
                    includeThumbnails, includeParameters,
                    onDone: () => Application.BrowserWindow?.ViewModel.SyncCommand.Execute(null));

            // Re-extract metadata for ONE family (selected in the browser), reusing the same
            // indexing ExternalEvent ping-pong. Called from a background thread by the VM, so
            // WaitForCompletion blocks that thread (not Revit's main thread) while Execute runs.
            // Returns the failure reason — a read-only/locked shared DB is an expected state
            // (cloud-sync clients flag it), and the user needs to see why, not a generic "no".
            Func<long, string, (bool Success, string? Error)> rescanFamily = (familyId, fullPath) =>
            {
                try
                {
                    using var repo = new IndexRepository(ConfigManager.LoadConfig().DatabasePath);
                    var (thumb, year) = ThumbnailExtractor.ExtractFromRfa(fullPath);
                    // Carry the file's real size/date: the handler writes them into the row on
                    // success, and leaving them at their defaults would store the sentinel values
                    // (MinValue/0) — making the family look stale so every deep scan after a
                    // one-off rescan re-extracted it for nothing.
                    var fi = new FileInfo(fullPath);
                    var workItem = new ExtractionWorkItem
                    {
                        FamilyId = familyId,
                        FullPath = fullPath,
                        RelativePath = string.Empty,
                        ThumbnailPng = thumb,
                        FileRevitYear = year,
                        ModifiedDate = fi.LastWriteTimeUtc,
                        FileSize = fi.Length
                    };
                    var extractor = new FamilyMetadataExtractor(capturedUIApp.Application);
                    // IndexingGate: the deep scan (Config window) shares this handler.
                    lock (Application.IndexingGate)
                    {
                        var handler = Application.IndexingHandler;
                        handler.PrepareAndWait(workItem, repo, extractor);
                        Raise(Application.IndexingEvent);
                        handler.WaitForCompletion();
                        if (handler.Error != null) return (false, handler.Error);
                        if (!handler.Extracted)
                            return (false, "Revit could not open this family to read its parameters. It may be " +
                                           "saved in a newer Revit version, open in another session, or damaged.");
                    }
                    return (true, null);
                }
                catch (Exception ex) { return (false, ex.Message); }
            };

            // Crash log lives under %LOCALAPPDATA% — writing to C:\ root fails without elevation
            // (and the failure was being swallowed, so the file the dialog pointed to never existed).
            var crashLogPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RVTuk", "crash.log");

            // DispatcherUnhandledException fires INSIDE WPF's managed layer, before the
            // Win32 callback exception filter that prevents AppDomain.UnhandledException
            // from running in Revit's CLR-hosted environment. This is our only chance to
            // catch exceptions thrown during WPF layout/rendering after Show() returns.
            System.Windows.Threading.DispatcherUnhandledExceptionEventHandler? dispatcherHandler = null;
            dispatcherHandler = (s, ev) =>
            {
                // Always log — exception may fire during Show() before BrowserWindow is assigned
                try
                {
                    var sb = new StringBuilder();
                    sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]");
                    sb.AppendLine($"Type:    {ev.Exception?.GetType().FullName}");
                    sb.AppendLine($"Message: {ev.Exception?.Message}");
                    sb.AppendLine($"Stack:");
                    sb.AppendLine(ev.Exception?.StackTrace);
                    if (ev.Exception?.InnerException != null)
                    {
                        sb.AppendLine($"Inner: {ev.Exception.InnerException.GetType().FullName}: {ev.Exception.InnerException.Message}");
                        sb.AppendLine(ev.Exception.InnerException.StackTrace);
                    }
                    sb.AppendLine();
                    Directory.CreateDirectory(Path.GetDirectoryName(crashLogPath)!);
                    File.AppendAllText(crashLogPath, sb.ToString());
                }
                catch { }

                ev.Handled = true;
                System.Windows.Application.Current.DispatcherUnhandledException -= dispatcherHandler;

                try { Application.BrowserWindow?.Close(); Application.BrowserWindow = null; } catch { }

                System.Windows.MessageBox.Show(
                    $"RVTuk Family Browser encountered an error:\n" +
                    $"{ev.Exception?.GetType().Name}: {ev.Exception?.Message}\n\n" +
                    $"Details written to {crashLogPath}",
                    "RVTuk Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            };

            try
            {
                System.Windows.Application.Current.DispatcherUnhandledException += dispatcherHandler;
                var window = new FamilyBrowserWindow(config, getProjectFamilies, loadFamily, rescanFamily,
                    scan, openInFamilyEditor, openModelFamilyInEditor, saveFamilyToLibrary, getFamilyPreviews);
                window.Closed += (s, e) =>
                {
                    System.Windows.Application.Current.DispatcherUnhandledException -= dispatcherHandler;
                    if (ReferenceEquals(Application.BrowserWindow, window)) Application.BrowserWindow = null;
                };
                Application.BrowserWindow = window; // set before Show() so handler can close it if layout throws
                new WindowInteropHelper(window).Owner = revitWindow;
                window.Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                System.Windows.Application.Current.DispatcherUnhandledException -= dispatcherHandler;
                TaskDialog.Show("RVTuk – Family Browser",
                    $"Failed to open the browser:\n\n{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}");
                return Result.Failed;
            }
        }

        /// <summary>
        /// Raises an ExternalEvent for a caller that then waits on its handler. A raise Revit
        /// refuses never reaches the handler, so the wait would block forever (holding whatever
        /// lock the caller is in) — throw instead; every caller reports the message.
        /// </summary>
        internal static void Raise(ExternalEvent externalEvent)
        {
            var request = externalEvent.Raise();
            if (request == ExternalEventRequest.Denied || request == ExternalEventRequest.TimedOut)
                throw new InvalidOperationException(
                    $"Revit did not accept the request ({request}). Finish or cancel what Revit is doing and try again.");
        }
    }
}

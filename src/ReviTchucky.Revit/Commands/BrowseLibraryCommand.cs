using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ReviTchucky.Core.Config;
using ReviTchucky.Core.Database;
using ReviTchucky.Core.Extraction;
using ReviTchucky.Core.Models;
using ReviTchucky.Revit.Extraction;
using ReviTchucky.UI.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ReviTchucky.Revit.Commands
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

            var config = ConfigManager.LoadConfig();
            if (!ConfigManager.IsConfigured(config))
            {
                var settings = new SettingsWindow();
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
            Func<IReadOnlyList<string>> getProjectFamilies = () =>
            {
                Application.GetFamiliesHandler.Reset();
                Application.GetFamiliesEvent.Raise();
                Application.GetFamiliesHandler.WaitForCompletion();
                return Application.GetFamiliesHandler.Result;
            };

            Func<string, (bool Success, string? Error)> loadFamily = path =>
            {
                Application.LoadFamilyHandler.Prepare(path);
                Application.LoadFamilyEvent.Raise();
                Application.LoadFamilyHandler.WaitForCompletion();
                return (Application.LoadFamilyHandler.Success, Application.LoadFamilyHandler.ErrorMessage);
            };

            Application.CurrentUIApp = commandData.Application;
            var capturedUIApp = commandData.Application;
            Action deepScan = () => IndexLibraryCommand.RunDeepScan(capturedUIApp, ConfigManager.LoadConfig());

            // Re-extract metadata for ONE family (selected in the browser), reusing the same
            // indexing ExternalEvent ping-pong. Called from a background thread by the VM, so
            // WaitForCompletion blocks that thread (not Revit's main thread) while Execute runs.
            Func<long, string, bool> rescanFamily = (familyId, fullPath) =>
            {
                try
                {
                    using var repo = new IndexRepository(ConfigManager.LoadConfig().DatabasePath);
                    var (thumb, year) = ThumbnailExtractor.ExtractFromRfa(fullPath);
                    var workItem = new ExtractionWorkItem
                    {
                        FamilyId = familyId,
                        FullPath = fullPath,
                        RelativePath = string.Empty,
                        ThumbnailPng = thumb,
                        FileRevitYear = year
                    };
                    var extractor = new FamilyMetadataExtractor(capturedUIApp.Application);
                    Application.IndexingHandler.PrepareAndWait(workItem, repo, extractor);
                    Application.IndexingEvent.Raise();
                    Application.IndexingHandler.WaitForCompletion();
                    return true;
                }
                catch { return false; }
            };

            // Crash log lives under %LOCALAPPDATA% — writing to C:\ root fails without elevation
            // (and the failure was being swallowed, so the file the dialog pointed to never existed).
            var crashLogPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ReviTchucky", "crash.log");

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
                    $"ReviTchucky Family Browser encountered an error:\n" +
                    $"{ev.Exception?.GetType().Name}: {ev.Exception?.Message}\n\n" +
                    $"Details written to {crashLogPath}",
                    "ReviTchucky Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            };

            try
            {
                System.Windows.Application.Current.DispatcherUnhandledException += dispatcherHandler;
                var window = new FamilyBrowserWindow(config, getProjectFamilies, loadFamily, deepScan, rescanFamily);
                window.Closed += (s, e) =>
                    System.Windows.Application.Current.DispatcherUnhandledException -= dispatcherHandler;
                Application.BrowserWindow = window; // set before Show() so handler can close it if layout throws
                window.Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                System.Windows.Application.Current.DispatcherUnhandledException -= dispatcherHandler;
                TaskDialog.Show("ReviTchucky – Family Browser",
                    $"Failed to open the browser:\n\n{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}");
                return Result.Failed;
            }
        }
    }
}

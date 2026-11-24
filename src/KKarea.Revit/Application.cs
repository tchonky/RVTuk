using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using RVTuk.Revit.RishuiZamin.ExternalEvents;
using WpfColor = System.Windows.Media.Color;

namespace KKarea.Revit
{
    /// <summary>
    /// KKarea: standalone Revit 2023 host for the Area Calc (Rishui Zamin) tool. The area
    /// logic is shared source with RVTuk (linked compile items in the csproj); this class is
    /// only the thin host: external events, assembly-resolve hook, crash logging, and a
    /// one-button ribbon.
    /// </summary>
    public class Application : IExternalApplication
    {
        public static AreaExtractEventHandler AreaExtractHandler { get; private set; } = null!;
        public static ExternalEvent AreaExtractEvent { get; private set; } = null!;
        public static SelectAreaEventHandler SelectAreaHandler { get; private set; } = null!;
        public static ExternalEvent SelectAreaEvent { get; private set; } = null!;
        public static SetupUsageKeysEventHandler SetupUsageKeysHandler { get; private set; } = null!;
        public static ExternalEvent SetupUsageKeysEvent { get; private set; } = null!;
        public static SetUsageKeyEventHandler SetUsageKeyHandler { get; private set; } = null!;
        public static ExternalEvent SetUsageKeyEvent { get; private set; } = null!;
        public static SavePlotLimitsEventHandler SavePlotLimitsHandler { get; private set; } = null!;
        public static ExternalEvent SavePlotLimitsEvent { get; private set; } = null!;
        public static RVTuk.UI.RishuiZamin.Views.RishuiZaminWindow? RishuiZaminWindow { get; set; }

        private static string? _addinDir;

        public Result OnStartup(UIControlledApplication application)
        {
            _addinDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            // Register early so all our assemblies resolve from our own folder,
            // preventing conflicts with other add-ins or Revit's bundled versions.
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAddinAssembly;

            // Log unhandled exceptions (including render-thread crashes) to the shared
            // RVTuk crash log so field diagnostics stay in one place.
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                try
                {
                    var logPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "RVTuk", "crash.log");
                    Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                    var ex = e.ExceptionObject as Exception;
                    File.AppendAllText(logPath,
                        $"\n[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] KKarea: {ex?.GetType().FullName}: {ex?.Message}\n{ex?.StackTrace}\n");
                }
                catch { /* logging must never itself crash */ }
            };

            AreaExtractHandler = new AreaExtractEventHandler();
            AreaExtractEvent   = ExternalEvent.Create(AreaExtractHandler);
            SelectAreaHandler  = new SelectAreaEventHandler();
            SelectAreaEvent    = ExternalEvent.Create(SelectAreaHandler);
            SetupUsageKeysHandler = new SetupUsageKeysEventHandler();
            SetupUsageKeysEvent   = ExternalEvent.Create(SetupUsageKeysHandler);
            SetUsageKeyHandler    = new SetUsageKeyEventHandler();
            SetUsageKeyEvent      = ExternalEvent.Create(SetUsageKeyHandler);
            SavePlotLimitsHandler = new SavePlotLimitsEventHandler();
            SavePlotLimitsEvent   = ExternalEvent.Create(SavePlotLimitsHandler);

            try
            {
                CreateRibbon(application);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("KKarea", $"Failed to create ribbon: {ex.Message}");
            }

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            AppDomain.CurrentDomain.AssemblyResolve -= ResolveAddinAssembly;
            return Result.Succeeded;
        }

        private static Assembly? ResolveAddinAssembly(object? sender, ResolveEventArgs args)
        {
            if (_addinDir == null) return null;

            // Only handle assemblies we ship — don't interfere with Revit's own.
            var name = new AssemblyName(args.Name).Name;
            if (name == null) return null;

            var candidate = Path.Combine(_addinDir, name + ".dll");
            return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
        }

        private static void CreateRibbon(UIControlledApplication app)
        {
            string assemblyPath = Assembly.GetExecutingAssembly().Location;
            RibbonPanel panel = app.CreateRibbonPanel("KKarea");

            var areaBtn = new PushButtonData(
                "AreaCalc",
                "Rishui\nZamin",
                assemblyPath,
                typeof(Commands.RishuiZaminCommand).FullName!)
            {
                ToolTip = "Rishui Zamin area calculation: generate the .dxf + .dat from the open sheet's Areas"
            };
            areaBtn.LargeImage = RVTuk.UI.RishuiZamin.Views.RishuiZaminIcon.Create(32);
            areaBtn.Image      = RVTuk.UI.RishuiZamin.Views.RishuiZaminIcon.Create(16);

            panel.AddItem(areaBtn);
        }
    }
}

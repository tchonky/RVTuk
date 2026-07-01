using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using WpfColor = System.Windows.Media.Color;
using WpfPoint = System.Windows.Point;
using RVTuk.Revit.Commands;
using RVTuk.Revit.ExternalEvents;

namespace RVTuk.Revit
{
    public class Application : IExternalApplication
    {
        public static IndexingExternalEventHandler IndexingHandler { get; private set; } = null!;
        public static ExternalEvent IndexingEvent { get; private set; } = null!;
        public static GetProjectFamiliesEventHandler GetFamiliesHandler { get; private set; } = null!;
        public static ExternalEvent GetFamiliesEvent { get; private set; } = null!;
        public static LoadFamilyEventHandler LoadFamilyHandler { get; private set; } = null!;
        public static ExternalEvent LoadFamilyEvent { get; private set; } = null!;
        public static CaptureSnapshotEventHandler CaptureSnapshotHandler { get; private set; } = null!;
        public static ExternalEvent CaptureSnapshotEvent { get; private set; } = null!;
        public static OpenModelSnapshotEventHandler OpenModelSnapshotHandler { get; private set; } = null!;
        public static ExternalEvent OpenModelSnapshotEvent { get; private set; } = null!;
        public static AreaExtractEventHandler AreaExtractHandler { get; private set; } = null!;
        public static ExternalEvent AreaExtractEvent { get; private set; } = null!;
        public static SelectAreaEventHandler SelectAreaHandler { get; private set; } = null!;
        public static ExternalEvent SelectAreaEvent { get; private set; } = null!;
        /// <summary>Serialises every IndexingHandler/IndexingEvent ping-pong: the deep scan
        /// (Config window) and the browser's one-family rescan share the same handler singleton
        /// and can run from different background threads at the same time.</summary>
        public static readonly object IndexingGate = new object();

        public static RVTuk.UI.Views.FamilyBrowserWindow? BrowserWindow { get; set; }
        public static RVTuk.UI.Views.ComparatorWindow? ComparatorWindow { get; set; }
        public static RVTuk.UI.Views.ConfigWindow? ConfigWindow { get; set; }
        public static RVTuk.UI.Views.AreaSubmissionWindow? AreaCalcWindow { get; set; }
        public static UIApplication? CurrentUIApp { get; set; }

        private static string? _addinDir;

        public Result OnStartup(UIControlledApplication application)
        {
            _addinDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            // Register early so all our assemblies resolve from our own folder,
            // preventing conflicts with other add-ins or Revit's bundled versions.
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAddinAssembly;

            // Log unhandled exceptions (including render-thread crashes) to a file
            // so we can diagnose crashes that escape Revit's own error handler.
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
                        $"\n[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex?.GetType().FullName}: {ex?.Message}\n{ex?.StackTrace}\n");
                }
                catch { /* logging must never itself crash */ }
            };

            IndexingHandler = new IndexingExternalEventHandler();
            IndexingEvent = ExternalEvent.Create(IndexingHandler);
            GetFamiliesHandler = new GetProjectFamiliesEventHandler();
            GetFamiliesEvent   = ExternalEvent.Create(GetFamiliesHandler);
            LoadFamilyHandler  = new LoadFamilyEventHandler();
            LoadFamilyEvent    = ExternalEvent.Create(LoadFamilyHandler);
            CaptureSnapshotHandler = new CaptureSnapshotEventHandler();
            CaptureSnapshotEvent   = ExternalEvent.Create(CaptureSnapshotHandler);
            OpenModelSnapshotHandler = new OpenModelSnapshotEventHandler();
            OpenModelSnapshotEvent   = ExternalEvent.Create(OpenModelSnapshotHandler);
            AreaExtractHandler = new AreaExtractEventHandler();
            AreaExtractEvent   = ExternalEvent.Create(AreaExtractHandler);
            SelectAreaHandler  = new SelectAreaEventHandler();
            SelectAreaEvent    = ExternalEvent.Create(SelectAreaHandler);

            try
            {
                CreateRibbon(application);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("RVTuk", $"Failed to create ribbon: {ex.Message}");
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
            RibbonPanel panel = app.CreateRibbonPanel("RVTuk");

            var browseBtn = new PushButtonData(
                "BrowseLibrary",
                "Family\nBrowser",
                assemblyPath,
                typeof(BrowseLibraryCommand).FullName!)
            {
                ToolTip = "Open the family browser to search, load, sync, and deep-scan library families"
            };
            browseBtn.LargeImage = CreateBrowseLibraryIcon(32);
            browseBtn.Image      = CreateBrowseLibraryIcon(16);

            panel.AddItem(browseBtn);

            var compareBtn = new PushButtonData(
                "CompareProjects",
                "Project\nComparator",
                assemblyPath,
                typeof(CompareProjectsCommand).FullName!)
            {
                ToolTip = "Compare two Revit projects (or a project against the firm template) and build a better Standard"
            };
            compareBtn.LargeImage = CreateComparatorIcon(32);
            compareBtn.Image      = CreateComparatorIcon(16);

            panel.AddItem(compareBtn);

            var configBtn = new PushButtonData(
                "Config",
                "Config",
                assemblyPath,
                typeof(OpenConfigCommand).FullName!)
            {
                ToolTip = "Library folder, deep scan, and ignored subfolders (settings for RVTuk tools)"
            };
            configBtn.LargeImage = CreateConfigIcon(32);
            configBtn.Image      = CreateConfigIcon(16);

            panel.AddItem(configBtn);

            var areaBtn = new PushButtonData(
                "AreaCalc",
                "Area\nCalc",
                assemblyPath,
                typeof(AreaCalcCommand).FullName!)
            {
                ToolTip = "Rishui Zamin area calculation: generate the .dxf + .dat from the open sheet's Areas"
            };
            areaBtn.LargeImage = CreateAreaCalcIcon(32);
            areaBtn.Image      = CreateAreaCalcIcon(16);

            var setupBtn = new PushButtonData(
                "SetupRishuiZamin",
                "Setup RZ\nParameters",
                assemblyPath,
                typeof(SetupRishuiZaminParamsCommand).FullName!)
            {
                ToolTip = "One-time project setup for Area Calc: bind the RZ_UsageType parameter to Areas and create the usage key schedule"
            };
            setupBtn.LargeImage = CreateConfigIcon(32);
            setupBtn.Image      = CreateConfigIcon(16);

            // Split button: main face opens Area Calc; the dropdown carries the one-time setup.
            var areaSplit = (SplitButton)panel.AddItem(new SplitButtonData("AreaCalcSplit", "Area Calc"));
            areaSplit.IsSynchronizedWithCurrentItem = false; // keep Area Calc as the face after Setup runs
            areaSplit.AddPushButton(areaBtn);
            areaSplit.AddPushButton(setupBtn);
        }

        private static BitmapSource CreateAreaCalcIcon(int size)
        {
            var dv = new DrawingVisual();
            using (var ctx = dv.RenderOpen())
            {
                double s = size;
                ctx.DrawRectangle(new SolidColorBrush(WpfColor.FromRgb(0x25, 0x25, 0x26)), null,
                    new Rect(0, 0, s, s));

                // A rectangle "area" split into two zones (primary/service) with a dimension tick.
                var primary = new SolidColorBrush(WpfColor.FromRgb(0xFF, 0x8C, 0x00));
                var service = new SolidColorBrush(WpfColor.FromRgb(0x4C, 0x9A, 0xFF));
                ctx.DrawRectangle(primary, null, new Rect(s * 0.16, s * 0.24, s * 0.42, s * 0.52));
                ctx.DrawRectangle(service, null, new Rect(s * 0.58, s * 0.40, s * 0.26, s * 0.36));

                var pen = new Pen(new SolidColorBrush(Colors.White), Math.Max(1, s * 0.05));
                pen.Freeze();
                // outline
                ctx.DrawRectangle(null, pen, new Rect(s * 0.16, s * 0.24, s * 0.68, s * 0.52));
            }
            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            return bmp;
        }

        private static BitmapSource CreateConfigIcon(int size)
        {
            // Reuse the standard gear glyph (24×24 path coords) rendered onto the dark tile,
            // so Config matches the other ribbon icons.
            const string gearPath =
                "M19.14 12.94c.04-.3.06-.61.06-.94 0-.32-.02-.64-.07-.94l2.03-1.58c.18-.14.23-.41.12-.61" +
                "l-1.92-3.32c-.12-.22-.37-.29-.59-.22l-2.39.96c-.5-.38-1.03-.7-1.62-.94l-.36-2.54c-.04-.24-.24-.41-.48-.41" +
                "h-3.84c-.24 0-.43.17-.47.41l-.36 2.54c-.59.24-1.13.57-1.62.94l-2.39-.96c-.22-.08-.47 0-.59.22" +
                "L2.74 8.87c-.12.21-.08.47.12.61l2.03 1.58c-.05.3-.09.63-.09.94s.02.64.07.94l-2.03 1.58c-.18.14-.23.41-.12.61" +
                "l1.92 3.32c.12.22.37.29.59.22l2.39-.96c.5.38 1.03.7 1.62.94l.36 2.54c.05.24.24.41.48.41h3.84" +
                "c.24 0 .44-.17.47-.41l.36-2.54c.59-.24 1.13-.57 1.62-.94l2.39.96c.22.08.47 0 .59-.22l1.92-3.32" +
                "c.12-.22.07-.47-.12-.61l-2.01-1.58zM12 15.6c-1.98 0-3.6-1.62-3.6-3.6s1.62-3.6 3.6-3.6 3.6 1.62 3.6 3.6-1.62 3.6-3.6 3.6z";

            var dv = new DrawingVisual();
            using (var ctx = dv.RenderOpen())
            {
                double s = size;
                ctx.DrawRectangle(new SolidColorBrush(WpfColor.FromRgb(0x25, 0x25, 0x26)), null,
                    new Rect(0, 0, s, s));

                var geo = Geometry.Parse(gearPath);
                ctx.PushTransform(new ScaleTransform(s / 24.0, s / 24.0));
                ctx.DrawGeometry(new SolidColorBrush(WpfColor.FromRgb(0xFF, 0x8C, 0x00)), null, geo);
                ctx.Pop();
            }
            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            return bmp;
        }

        private static BitmapSource CreateComparatorIcon(int size)
        {
            var dv = new DrawingVisual();
            using (var ctx = dv.RenderOpen())
            {
                double s = size;
                ctx.DrawRectangle(new SolidColorBrush(WpfColor.FromRgb(0x25, 0x25, 0x26)), null,
                    new Rect(0, 0, s, s));

                // Two overlapping document sheets.
                var sheetA = new SolidColorBrush(WpfColor.FromRgb(0xFF, 0x8C, 0x00));
                var sheetB = new SolidColorBrush(WpfColor.FromRgb(0xFF, 0xC0, 0x40));
                ctx.DrawRectangle(sheetA, null, new Rect(s * 0.12, s * 0.18, s * 0.42, s * 0.58));
                ctx.DrawRectangle(sheetB, null, new Rect(s * 0.46, s * 0.30, s * 0.42, s * 0.58));

                // Double-headed diff arrow between them.
                var pen = new Pen(new SolidColorBrush(Colors.White), Math.Max(1, s * 0.05));
                pen.Freeze();
                double y = s * 0.50;
                ctx.DrawLine(pen, new WpfPoint(s * 0.40, y), new WpfPoint(s * 0.60, y));
            }
            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            return bmp;
        }

        private static BitmapSource CreateBrowseLibraryIcon(int size)
        {
            var dv = new DrawingVisual();
            using (var ctx = dv.RenderOpen())
            {
                double s = size;

                // Dark background
                ctx.DrawRectangle(new SolidColorBrush(WpfColor.FromRgb(0x25, 0x25, 0x26)), null,
                    new Rect(0, 0, s, s));

                // Four book spines (orange family)
                var bookColors = new[]
                {
                    WpfColor.FromRgb(0xFF, 0x8C, 0x00),
                    WpfColor.FromRgb(0xFF, 0xA5, 0x20),
                    WpfColor.FromRgb(0xE6, 0x72, 0x00),
                    WpfColor.FromRgb(0xFF, 0xC0, 0x40),
                };
                double bw = s * 0.14;
                double gap = s * 0.035;
                double bx = s * 0.07;
                double by = s * 0.18;
                double bh = s * 0.60;
                for (int i = 0; i < 4; i++)
                    ctx.DrawRectangle(new SolidColorBrush(bookColors[i]), null,
                        new Rect(bx + i * (bw + gap), by, bw, bh));

                // Shelf line
                ctx.DrawRectangle(new SolidColorBrush(WpfColor.FromRgb(0x55, 0x44, 0x33)), null,
                    new Rect(s * 0.05, by + bh, s * 0.88, s * 0.07));

                // Magnifying glass (circle + handle) in white
                var pen = new Pen(new SolidColorBrush(Colors.White), Math.Max(1, s * 0.06));
                pen.Freeze();
                double cx = s * 0.76;
                double cy = s * 0.34;
                double r  = s * 0.145;
                ctx.DrawEllipse(null, pen, new WpfPoint(cx, cy), r, r);
                ctx.DrawLine(pen,
                    new WpfPoint(cx + r * 0.72, cy + r * 0.72),
                    new WpfPoint(cx + r * 1.55, cy + r * 1.55));
            }
            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            return bmp;
        }
    }
}

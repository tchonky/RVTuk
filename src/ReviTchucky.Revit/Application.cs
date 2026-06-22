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
using ReviTchucky.Revit.Commands;
using ReviTchucky.Revit.ExternalEvents;

namespace ReviTchucky.Revit
{
    public class Application : IExternalApplication
    {
        public static IndexingExternalEventHandler IndexingHandler { get; private set; } = null!;
        public static ExternalEvent IndexingEvent { get; private set; } = null!;
        public static GetProjectFamiliesEventHandler GetFamiliesHandler { get; private set; } = null!;
        public static ExternalEvent GetFamiliesEvent { get; private set; } = null!;
        public static LoadFamilyEventHandler LoadFamilyHandler { get; private set; } = null!;
        public static ExternalEvent LoadFamilyEvent { get; private set; } = null!;
        public static ReviTchucky.UI.Views.FamilyBrowserWindow? BrowserWindow { get; set; }
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
                        "ReviTchucky", "crash.log");
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

            try
            {
                CreateRibbon(application);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("ReviTchucky", $"Failed to create ribbon: {ex.Message}");
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
            RibbonPanel panel = app.CreateRibbonPanel("ReviTchucky");

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

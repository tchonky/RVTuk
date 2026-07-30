using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using WpfColor = System.Windows.Media.Color;
using WpfPoint = System.Windows.Point;
using RVTuk.Core.AutoDimensions;
using RVTuk.Revit.AutoDimensions;
using RVTuk.Revit.AutoDimensions.ExternalEvents;
using RVTuk.Revit.DwgExporter.Commands;
using RVTuk.Revit.FamilyBrowser.Commands;
using RVTuk.Revit.NeoProperties.Commands;
using RVTuk.Revit.RishuiZamin.Commands;
using RVTuk.Revit.FamilyBrowser.ExternalEvents;
using RVTuk.Revit.RishuiZamin.ExternalEvents;
using RVTuk.Revit.NeoProperties;
using RVTuk.Core.TopoTools;
using RVTuk.Revit.TopoTools;
using RVTuk.Revit.TopoTools.ExternalEvents;

namespace RVTuk.Revit
{
    public class Application : IExternalApplication
    {
        public static IndexingExternalEventHandler IndexingHandler { get; private set; } = null!;
        public static ExternalEvent IndexingEvent { get; private set; } = null!;
        /// <summary>Serialises every IndexingHandler/IndexingEvent ping-pong: the deep scan
        /// (Config window) and the browser's one-family rescan share the same handler singleton
        /// and can run from different background threads at the same time.</summary>
        public static readonly object IndexingGate = new object();
        public static GetProjectFamiliesEventHandler GetFamiliesHandler { get; private set; } = null!;
        public static ExternalEvent GetFamiliesEvent { get; private set; } = null!;
        public static LoadFamilyEventHandler LoadFamilyHandler { get; private set; } = null!;
        public static ExternalEvent LoadFamilyEvent { get; private set; } = null!;
        public static OpenFamilyEditorEventHandler OpenFamilyEditorHandler { get; private set; } = null!;
        public static ExternalEvent OpenFamilyEditorEvent { get; private set; } = null!;
        public static EditProjectFamilyEventHandler EditProjectFamilyHandler { get; private set; } = null!;
        public static ExternalEvent EditProjectFamilyEvent { get; private set; } = null!;
        public static GetFamilyPreviewsEventHandler GetFamilyPreviewsHandler { get; private set; } = null!;
        public static ExternalEvent GetFamilyPreviewsEvent { get; private set; } = null!;
        public static AreaExtractEventHandler AreaExtractHandler { get; private set; } = null!;
        public static ExternalEvent AreaExtractEvent { get; private set; } = null!;
        public static SelectAreaEventHandler SelectAreaHandler { get; private set; } = null!;
        public static ExternalEvent SelectAreaEvent { get; private set; } = null!;
        public static SetupUsageKeysEventHandler SetupUsageKeysHandler { get; private set; } = null!;
        public static ExternalEvent SetupUsageKeysEvent { get; private set; } = null!;
        public static RVTuk.UI.FamilyBrowser.Views.FamilyBrowserWindow? BrowserWindow { get; set; }
        public static RVTuk.UI.RishuiZamin.Views.RishuiZaminWindow? RishuiZaminWindow { get; set; }
        public static UIApplication? CurrentUIApp { get; set; }
        public static RVTuk.UI.NeoProperties.ViewModels.NeoPropertiesViewModel NeoPropertiesViewModel { get; private set; } = null!;
        public static LevelDiscoveryEventHandler LevelDiscoveryHandler { get; private set; } = null!;
        public static ExternalEvent LevelDiscoveryEvent { get; private set; } = null!;
        public static CreateDimensionsEventHandler CreateDimensionsHandler { get; private set; } = null!;
        public static ExternalEvent CreateDimensionsEvent { get; private set; } = null!;
        public static RVTuk.UI.AutoDimensions.ViewModels.AutoDimensionsPaneViewModel AutoDimensionsPaneViewModel { get; private set; } = null!;
        public static TopoSetupEventHandler TopoSetupHandler { get; private set; } = null!;
        public static ExternalEvent TopoSetupEvent { get; private set; } = null!;
        public static TopoDiscoveryEventHandler TopoDiscoveryHandler { get; private set; } = null!;
        public static ExternalEvent TopoDiscoveryEvent { get; private set; } = null!;
        public static TopoApplyEventHandler TopoApplyHandler { get; private set; } = null!;
        public static ExternalEvent TopoApplyEvent { get; private set; } = null!;
        public static RVTuk.UI.TopoTools.ViewModels.TopoToolsPaneViewModel TopoToolsPaneViewModel { get; private set; } = null!;

        /// <summary>
        /// Auto Dimensions ships on the RVTuk panel alongside the Family Browser, Area Calc and
        /// DWG Export. Its single entry point is the dockable pane — the old active-view-only
        /// ribbon command was folded into it (a one-view run is that view alone, ticked).
        /// </summary>
        private static readonly bool RegisterAutoDimensions = true;

        /// <summary>
        /// Topo Tools ships on its own panel on Revit's Massing &amp; Site tab, not on the RVTuk
        /// Add-Ins panel — it belongs with the site tools it works alongside. Its single entry
        /// point is the dockable pane.
        /// </summary>
        private static readonly bool RegisterTopoTools = true;

        /// <summary>
        /// Neo Properties is still unreleased: no ribbon panel, no dockable pane, no selection
        /// tracking. Flip to true in a dev build to work on it.
        /// (The Project Comparator was removed from this repo entirely — it lives on as its own
        /// separate project; recover the code from git history if ever needed.)
        /// </summary>
        private static readonly bool RegisterNeoProperties = false;

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
            OpenFamilyEditorHandler = new OpenFamilyEditorEventHandler();
            OpenFamilyEditorEvent   = ExternalEvent.Create(OpenFamilyEditorHandler);
            EditProjectFamilyHandler = new EditProjectFamilyEventHandler();
            EditProjectFamilyEvent   = ExternalEvent.Create(EditProjectFamilyHandler);
            GetFamilyPreviewsHandler = new GetFamilyPreviewsEventHandler();
            GetFamilyPreviewsEvent   = ExternalEvent.Create(GetFamilyPreviewsHandler);
            AreaExtractHandler = new AreaExtractEventHandler();
            AreaExtractEvent   = ExternalEvent.Create(AreaExtractHandler);
            SelectAreaHandler  = new SelectAreaEventHandler();
            SelectAreaEvent    = ExternalEvent.Create(SelectAreaHandler);
            SetupUsageKeysHandler = new SetupUsageKeysEventHandler();
            SetupUsageKeysEvent   = ExternalEvent.Create(SetupUsageKeysHandler);

            if (RegisterNeoProperties)
            {
                NeoPropertiesViewModel = new RVTuk.UI.NeoProperties.ViewModels.NeoPropertiesViewModel();
                NeoPropertiesSelectionHandler.ViewModel = NeoPropertiesViewModel;
                application.SelectionChanged += NeoPropertiesSelectionHandler.OnSelectionChanged;

                var neoView = new RVTuk.UI.NeoProperties.Views.NeoPropertiesView { DataContext = NeoPropertiesViewModel };
                application.RegisterDockablePane(
                    NeoPropertiesPaneProvider.PaneId,
                    "Neo Properties",
                    new NeoPropertiesPaneProvider(neoView));
            }

            if (RegisterAutoDimensions)
            {
                LevelDiscoveryHandler   = new LevelDiscoveryEventHandler();
                LevelDiscoveryEvent     = ExternalEvent.Create(LevelDiscoveryHandler);
                CreateDimensionsHandler = new CreateDimensionsEventHandler();
                CreateDimensionsEvent   = ExternalEvent.Create(CreateDimensionsHandler);

                // The pane (UI project) only ever sees these delegates — no Revit types cross over.
                // Both block on WaitForCompletion, so the view model calls them from the pool.
                Func<AutoDimensionsScope> discoverScope = () =>
                {
                    LevelDiscoveryHandler.Reset();
                    LevelDiscoveryEvent.Raise();
                    LevelDiscoveryHandler.WaitForCompletion();
                    return LevelDiscoveryHandler.Result;
                };

                Func<int, IReadOnlyList<long>, long, string> createDimensions = (mask, viewIds, dimensionTypeId) =>
                {
                    CreateDimensionsHandler.Prepare(mask, viewIds, dimensionTypeId);
                    CreateDimensionsEvent.Raise();
                    CreateDimensionsHandler.WaitForCompletion();
                    return CreateDimensionsHandler.Summary;
                };

                AutoDimensionsPaneViewModel =
                    new RVTuk.UI.AutoDimensions.ViewModels.AutoDimensionsPaneViewModel(
                        discoverScope, createDimensions);

                var autoDimView = new RVTuk.UI.AutoDimensions.Views.AutoDimensionsPaneView
                {
                    DataContext = AutoDimensionsPaneViewModel
                };
                application.RegisterDockablePane(
                    AutoDimensionsPaneProvider.PaneId,
                    "Auto Dimensions",
                    new AutoDimensionsPaneProvider(autoDimView));
            }

            if (RegisterTopoTools)
            {
                TopoSetupHandler     = new TopoSetupEventHandler();
                TopoSetupEvent       = ExternalEvent.Create(TopoSetupHandler);
                TopoDiscoveryHandler = new TopoDiscoveryEventHandler();
                TopoDiscoveryEvent   = ExternalEvent.Create(TopoDiscoveryHandler);
                TopoApplyHandler     = new TopoApplyEventHandler();
                TopoApplyEvent       = ExternalEvent.Create(TopoApplyHandler);

                // The pane (UI project) only ever sees these delegates — no Revit types cross over.
                // All three block on WaitForCompletion, so the view model calls them from the pool.
                Func<double, TopoScope> discoverTopo = spacingMillimetres =>
                {
                    TopoDiscoveryHandler.Reset();
                    TopoDiscoveryHandler.Prepare(spacingMillimetres);
                    TopoDiscoveryEvent.Raise();
                    TopoDiscoveryHandler.WaitForCompletion();
                    return TopoDiscoveryHandler.Result;
                };

                Func<double, string> applyTopo = spacingMillimetres =>
                {
                    TopoApplyHandler.Reset();
                    TopoApplyHandler.Prepare(spacingMillimetres);
                    TopoApplyEvent.Raise();
                    TopoApplyHandler.WaitForCompletion();
                    return TopoApplyHandler.Summary;
                };

                Func<string> setUpTopoProject = () =>
                {
                    TopoSetupHandler.Reset();
                    TopoSetupEvent.Raise();
                    TopoSetupHandler.WaitForCompletion();
                    return TopoSetupHandler.Summary;
                };

                TopoToolsPaneViewModel =
                    new RVTuk.UI.TopoTools.ViewModels.TopoToolsPaneViewModel(
                        discoverTopo, applyTopo, setUpTopoProject);

                var topoView = new RVTuk.UI.TopoTools.Views.TopoToolsPaneView
                {
                    DataContext = TopoToolsPaneViewModel
                };
                application.RegisterDockablePane(
                    TopoToolsPaneProvider.PaneId,
                    "Topo Tools",
                    new TopoToolsPaneProvider(topoView));
            }

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

            var areaBtn = new PushButtonData(
                "AreaCalc",
                "Area\nCalc",
                assemblyPath,
                typeof(RishuiZaminCommand).FullName!)
            {
                ToolTip = "Rishui Zamin area calculation: generate the .dxf + .dat from the open sheet's Areas"
            };
            areaBtn.LargeImage = CreateAreaCalcIcon(32);
            areaBtn.Image      = CreateAreaCalcIcon(16);

            panel.AddItem(areaBtn);

            var dwgBtn = new PushButtonData(
                "DwgExport",
                "DWG\nExport",
                assemblyPath,
                typeof(DwgExportCommand).FullName!)
            {
                ToolTip = "Batch-export a sheet set to DWG, named by the PDF export setups' naming rules"
            };
            dwgBtn.LargeImage = CreateDwgExportIcon(32);
            dwgBtn.Image      = CreateDwgExportIcon(16);

            panel.AddItem(dwgBtn);

            if (RegisterAutoDimensions)
            {
                var autoDimBtn = new PushButtonData(
                    "AutoDimensions",
                    "Auto\nDimensions",
                    assemblyPath,
                    typeof(AutoDimensionsPaneCommand).FullName!)
                {
                    ToolTip = "Open the Auto Dimensions pane: pick the reference categories and which views of each level get dimensions"
                };
                autoDimBtn.LargeImage = CreateAutoDimensionsIcon(32);
                autoDimBtn.Image      = CreateAutoDimensionsIcon(16);

                panel.AddItem(autoDimBtn);
            }

            if (RegisterTopoTools)
            {
                // Autodesk.Revit.UI.Tab offers only AddIns and Analyze, so the built-in tab can
                // only be named through the string overload — and whether Revit resolves built-in
                // tabs that way is not guaranteed. Falling back to the RVTuk panel keeps the tool
                // reachable either way rather than losing the button to an exception.
                RibbonPanel topoPanel;
                try
                {
                    topoPanel = app.CreateRibbonPanel("Massing & Site", "RVTuk");
                }
                catch (Exception)
                {
                    topoPanel = panel;
                }

                var topoBtn = new PushButtonData(
                    "TopoTools",
                    "Topo\nTools",
                    assemblyPath,
                    typeof(TopoToolsPaneCommand).FullName!)
                {
                    ToolTip = "Open the Topo Tools pane: turn topo lines into points on the toposolid below"
                };
                topoBtn.LargeImage = CreateTopoToolsIcon(32);
                topoBtn.Image      = CreateTopoToolsIcon(16);

                topoPanel.AddItem(topoBtn);
            }

            if (!RegisterNeoProperties) return;

            RibbonPanel neoPanel = app.CreateRibbonPanel("Neo Properties");
            var neoBtn = new PushButtonData(
                "NeoProperties",
                "Neo\nProperties",
                assemblyPath,
                typeof(NeoPropertiesCommand).FullName!)
            {
                ToolTip = "Open the Neo Properties pane: same parameters as Properties, pinned/reordered"
            };
            neoBtn.LargeImage = CreateNeoPropertiesIcon(32);
            neoBtn.Image      = CreateNeoPropertiesIcon(16);

            neoPanel.AddItem(neoBtn);
        }

        private static BitmapSource CreateTopoToolsIcon(int size)
        {
            var dv = new DrawingVisual();
            using (var ctx = dv.RenderOpen())
            {
                double s = size;
                ctx.DrawRectangle(new SolidColorBrush(WpfColor.FromRgb(0x25, 0x25, 0x26)), null,
                    new Rect(0, 0, s, s));

                // Three nested contour lines, with a point sitting on the middle one.
                var pen = new Pen(new SolidColorBrush(WpfColor.FromRgb(0xFF, 0x8C, 0x00)),
                    Math.Max(1, s * 0.06));
                pen.Freeze();

                ctx.DrawEllipse(null, pen, new WpfPoint(s * 0.5, s * 0.55), s * 0.36, s * 0.24);
                ctx.DrawEllipse(null, pen, new WpfPoint(s * 0.5, s * 0.55), s * 0.23, s * 0.15);
                ctx.DrawEllipse(null, pen, new WpfPoint(s * 0.5, s * 0.55), s * 0.10, s * 0.07);

                ctx.DrawEllipse(new SolidColorBrush(Colors.White), null,
                    new WpfPoint(s * 0.5, s * 0.31), s * 0.07, s * 0.07);
            }
            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            return bmp;
        }

        private static BitmapSource CreateAutoDimensionsIcon(int size)
        {
            var dv = new DrawingVisual();
            using (var ctx = dv.RenderOpen())
            {
                double s = size;
                ctx.DrawRectangle(new SolidColorBrush(WpfColor.FromRgb(0x25, 0x25, 0x26)), null,
                    new Rect(0, 0, s, s));

                var pen = new Pen(new SolidColorBrush(WpfColor.FromRgb(0xFF, 0x8C, 0x00)), Math.Max(1, s * 0.06));
                pen.Freeze();
                double y = s * 0.5;
                // Dimension line with tick marks at each end and one in the middle.
                ctx.DrawLine(pen, new WpfPoint(s * 0.14, y), new WpfPoint(s * 0.86, y));
                foreach (var x in new[] { s * 0.14, s * 0.5, s * 0.86 })
                {
                    ctx.DrawLine(pen, new WpfPoint(x, y - s * 0.16), new WpfPoint(x, y + s * 0.16));
                }
            }
            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            return bmp;
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

        private static BitmapSource CreateDwgExportIcon(int size)
        {
            var dv = new DrawingVisual();
            using (var ctx = dv.RenderOpen())
            {
                double s = size;
                ctx.DrawRectangle(new SolidColorBrush(WpfColor.FromRgb(0x25, 0x25, 0x26)), null,
                    new Rect(0, 0, s, s));

                // Document sheet with a folded corner.
                var sheetBrush = new SolidColorBrush(WpfColor.FromRgb(0xD4, 0xD4, 0xD4));
                var foldBrush  = new SolidColorBrush(WpfColor.FromRgb(0x9A, 0x9A, 0x9A));
                var sheetGeo = new StreamGeometry();
                using (var g = sheetGeo.Open())
                {
                    g.BeginFigure(new WpfPoint(s * 0.20, s * 0.10), true, true);
                    g.LineTo(new WpfPoint(s * 0.62, s * 0.10), true, false);
                    g.LineTo(new WpfPoint(s * 0.76, s * 0.24), true, false);
                    g.LineTo(new WpfPoint(s * 0.76, s * 0.62), true, false);
                    g.LineTo(new WpfPoint(s * 0.20, s * 0.62), true, false);
                }
                sheetGeo.Freeze();
                ctx.DrawGeometry(sheetBrush, null, sheetGeo);

                var foldGeo = new StreamGeometry();
                using (var g = foldGeo.Open())
                {
                    g.BeginFigure(new WpfPoint(s * 0.62, s * 0.10), true, true);
                    g.LineTo(new WpfPoint(s * 0.76, s * 0.24), true, false);
                    g.LineTo(new WpfPoint(s * 0.62, s * 0.24), true, false);
                }
                foldGeo.Freeze();
                ctx.DrawGeometry(foldBrush, null, foldGeo);

                // Orange export arrow under the sheet.
                var pen = new Pen(new SolidColorBrush(WpfColor.FromRgb(0xFF, 0x8C, 0x00)),
                    Math.Max(1, s * 0.09));
                pen.Freeze();
                ctx.DrawLine(pen, new WpfPoint(s * 0.24, s * 0.78), new WpfPoint(s * 0.72, s * 0.78));
                ctx.DrawLine(pen, new WpfPoint(s * 0.60, s * 0.67), new WpfPoint(s * 0.72, s * 0.78));
                ctx.DrawLine(pen, new WpfPoint(s * 0.60, s * 0.89), new WpfPoint(s * 0.72, s * 0.78));
            }
            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            return bmp;
        }

        private static BitmapSource CreateNeoPropertiesIcon(int size)
        {
            var dv = new DrawingVisual();
            using (var ctx = dv.RenderOpen())
            {
                double s = size;
                ctx.DrawRectangle(new SolidColorBrush(WpfColor.FromRgb(0x25, 0x25, 0x26)), null,
                    new Rect(0, 0, s, s));

                // Three horizontal rows (parameter list) with the top row highlighted (pinned).
                var pinned = new SolidColorBrush(WpfColor.FromRgb(0xFF, 0x8C, 0x00));
                var row = new SolidColorBrush(WpfColor.FromRgb(0xD4, 0xD4, 0xD4));
                double rowHeight = s * 0.14;
                double rowGap = s * 0.10;
                double x = s * 0.16;
                double width = s * 0.68;
                double y = s * 0.22;

                ctx.DrawRectangle(pinned, null, new Rect(x, y, width, rowHeight));
                y += rowHeight + rowGap;
                ctx.DrawRectangle(row, null, new Rect(x, y, width, rowHeight));
                y += rowHeight + rowGap;
                ctx.DrawRectangle(row, null, new Rect(x, y, width, rowHeight));
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

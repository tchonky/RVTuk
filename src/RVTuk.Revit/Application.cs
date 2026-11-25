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
using RVTuk.Revit.RoundPositions;
using RVTuk.Revit.RoundPositions.Commands;
using RVTuk.Revit.RoundPositions.ExternalEvents;
using RVTuk.Core.RoundPositions;
using RVTuk.Core.Shared.Config;
using RVTuk.Revit.FamilyBrowser.ExternalEvents;
using RVTuk.Revit.RishuiZamin.ExternalEvents;
using RVTuk.Revit.NeoProperties;
using RVTuk.Revit.TopoTools;
using RVTuk.Revit.TopoTools.Commands;
using RVTuk.Revit.RoomFloors.Commands;

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
        public static SetUsageKeyEventHandler SetUsageKeyHandler { get; private set; } = null!;
        public static ExternalEvent SetUsageKeyEvent { get; private set; } = null!;
        public static SavePlotLimitsEventHandler SavePlotLimitsHandler { get; private set; } = null!;
        public static ExternalEvent SavePlotLimitsEvent { get; private set; } = null!;
        public static RVTuk.UI.FamilyBrowser.Views.FamilyBrowserWindow? BrowserWindow { get; set; }
        public static RVTuk.UI.RishuiZamin.Views.RishuiZaminWindow? RishuiZaminWindow { get; set; }
        public static UIApplication? CurrentUIApp { get; set; }
        public static RVTuk.UI.NeoProperties.ViewModels.NeoPropertiesViewModel NeoPropertiesViewModel { get; private set; } = null!;
        public static LevelDiscoveryEventHandler LevelDiscoveryHandler { get; private set; } = null!;
        public static ExternalEvent LevelDiscoveryEvent { get; private set; } = null!;
        public static CreateDimensionsEventHandler CreateDimensionsHandler { get; private set; } = null!;
        public static ExternalEvent CreateDimensionsEvent { get; private set; } = null!;
        public static RVTuk.UI.AutoDimensions.ViewModels.AutoDimensionsPaneViewModel AutoDimensionsPaneViewModel { get; private set; } = null!;

        /// <summary>
        /// Auto Dimensions ships on the RVTuk panel alongside the Family Browser, Area Calc and
        /// DWG Export. Its single entry point is the dockable pane — the old active-view-only
        /// ribbon command was folded into it (a one-view run is that view alone, ticked).
        /// </summary>
        private static readonly bool RegisterAutoDimensions = true;

        /// <summary>
        /// Topo Tools ships on the RVTuk panel alongside the other tools. It was intended for
        /// Revit's Massing &amp; Site tab, which the API cannot reach — see CreateRibbon. Its
        /// button opens a Topo Edit session; Floor ⇄ Topo sits next to it.
        /// </summary>
        private static readonly bool RegisterTopoTools = true;

        /// <summary>
        /// Room Floor ships on the RVTuk panel: a one-shot command (no pane, no dialog) that makes
        /// a finish floor per clicked room, like Automatic Ceiling does for ceilings.
        /// </summary>
        private static readonly bool RegisterRoomFloors = true;

        /// <summary>
        /// Neo Properties is still unreleased: no ribbon panel, no dockable pane, no selection
        /// tracking. Flip to true in a dev build to work on it.
        /// (The Project Comparator was removed from this repo entirely — it lives on as its own
        /// separate project; recover the code from git history if ever needed.)
        /// </summary>
        private static readonly bool RegisterNeoProperties = false;

        /// <summary>Round Positions ships on the RVTuk panel. Its single entry point is the
        /// dockable pane — the one-click button it replaced could not be stopped once started.</summary>
        private static readonly bool RegisterRoundPositions = true;

        public static ScanEventHandler RoundScanHandler { get; private set; } = null!;
        public static ExternalEvent RoundScanEvent { get; private set; } = null!;
        public static FixEventHandler RoundFixHandler { get; private set; } = null!;
        public static ExternalEvent RoundFixEvent { get; private set; } = null!;
        public static SelectEventHandler RoundSelectHandler { get; private set; } = null!;
        public static ExternalEvent RoundSelectEvent { get; private set; } = null!;
        public static RVTuk.UI.RoundPositions.ViewModels.RoundPositionsPaneViewModel? RoundPositionsPaneViewModel
        { get; private set; }

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
                var ex = e.ExceptionObject as Exception;
                LogDiagnostic($"{ex?.GetType().FullName}: {ex?.Message}\n{ex?.StackTrace}");
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

            // The browser's ✓/↑ badges describe the project the last ⟳ compared: tell it when
            // another document becomes active or that one closes (cheap: no API work here).
            // The key is read here, inside the event: the document (and the event args) may be
            // gone by the time the dispatcher runs the callback.
            application.ViewActivated += (_, e) =>
            {
                var key = KeyOf(e.Document);
                if (key != null) TellBrowser(vm => vm.ActiveDocumentChanged(key));
            };
            application.ControlledApplication.DocumentClosing += (_, e) =>
            {
                var key = KeyOf(e.Document);
                if (key != null) TellBrowser(vm => vm.DocumentClosing(key));
            };
            // Every family load notes which library file it came from, for ⟳'s comparison.
            RVTuk.Revit.FamilyBrowser.FamilyLoadWatch.Start(application);
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

            if (RegisterRoundPositions)
            {
                RoundScanHandler   = new ScanEventHandler();
                RoundScanEvent     = ExternalEvent.Create(RoundScanHandler);
                RoundFixHandler    = new FixEventHandler();
                RoundFixEvent      = ExternalEvent.Create(RoundFixHandler);
                RoundSelectHandler = new SelectEventHandler();
                RoundSelectEvent   = ExternalEvent.Create(RoundSelectHandler);

                // The pane (UI project) only ever sees these delegates — no Revit type crosses
                // over. All three block on WaitForCompletion, so the view model calls them from
                // the pool.
                // Bounded waits: an external event Revit never runs would otherwise leave the pane
                // blocked with nothing on screen to say so. A fix gets longer than a scan because
                // it legitimately takes longer.
                var scanTimeout = TimeSpan.FromMinutes(5);
                var fixTimeout = TimeSpan.FromMinutes(30);
                const string NoResponse =
                    "Revit did not run the request. It may be busy with another command, or " +
                    "waiting on a dialog — close anything open and try again.";

                Func<bool, System.Threading.CancellationToken, ScanResult> scan = (allowHalf, cancellation) =>
                {
                    RoundScanHandler.Prepare(allowHalf, cancellation);
                    RoundScanEvent.Raise();
                    if (!RoundScanHandler.WaitForCompletion(scanTimeout))
                        throw new TimeoutException(NoResponse);
                    if (RoundScanHandler.Error != null) throw RoundScanHandler.Error;
                    return RoundScanHandler.Result;
                };

                Func<bool, IReadOnlyList<long>, System.Threading.CancellationToken, RoundingReport> fix =
                    (allowHalf, ids, cancellation) =>
                {
                    RoundFixHandler.Prepare(allowHalf, new HashSet<long>(ids), cancellation);
                    RoundFixEvent.Raise();
                    if (!RoundFixHandler.WaitForCompletion(fixTimeout))
                        throw new TimeoutException(NoResponse);
                    if (RoundFixHandler.Error != null) throw RoundFixHandler.Error;
                    return RoundFixHandler.Report;
                };

                // Raise and return — deliberately no wait. The pane's WPF thread IS Revit's main
                // thread, so blocking here would block the very thread that has to run the event:
                // it would sit until the timeout and only then select. Nothing needs the result,
                // and Revit runs the event the moment it is idle, which is immediately.
                Action<IReadOnlyList<long>> selectInModel = ids =>
                {
                    RoundSelectHandler.Prepare(ids);
                    RoundSelectEvent.Raise();
                };

                // Stored inverted: the config default (false) is "half centimetres allowed".
                var roundConfig = ConfigManager.LoadConfig();
                Action<bool> persistRoundSetting = allowHalf =>
                {
                    var current = ConfigManager.LoadConfig();
                    current.RoundPositionsWholeCentimetresOnly = !allowHalf;
                    ConfigManager.SaveConfig(current);
                };

                RoundPositionsPaneViewModel =
                    new RVTuk.UI.RoundPositions.ViewModels.RoundPositionsPaneViewModel(
                        scan, fix, selectInModel,
                        !roundConfig.RoundPositionsWholeCentimetresOnly, persistRoundSetting);

                var roundView = new RVTuk.UI.RoundPositions.Views.RoundPositionsPaneView
                {
                    DataContext = RoundPositionsPaneViewModel
                };
                application.RegisterDockablePane(
                    RoundPositionsPaneProvider.PaneId,
                    "Round Positions",
                    new RoundPositionsPaneProvider(roundView));
            }

            try
            {
                CreateRibbon(application);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("RVTuk", $"Failed to create ribbon: {ex.Message}");
            }

            // ModifyTabHost puts our panels on the Modify tab through the unsupported
            // Autodesk.Windows ribbon — the Topo Tools shortcut while one non-subdivision toposolid
            // is selected, and the Topo Edit copy during a session (never Floor ⇄ Topo); it also
            // switches the ribbon to Add-Ins once at each session start; it never throws: a failure
            // of the copy or of the switch turns off only that part, any other failure logs one
            // line and switches the whole host off.
            if (RegisterTopoTools) ModifyTabHost.Install(application);

            if (RegisterTopoTools)
            {
                // The Topo Edit session's updater: registered optional, disabled until a session.
                try
                {
                    TopoSessionUpdater.Register(application.ActiveAddInId);
                }
                catch (Exception ex)
                {
                    LogDiagnostic($"TopoSessionUpdater.Register failed: {ex}");
                }

                // A document closed for real ends its session and forgets its last typed topo
                // height; another document's view ends the session, another view of its own
                // document disarms it. Never throws.
                TopoDocumentEvents.Install(application);

                // Saving or syncing the session's document asks first: Accept, then save; Cancel
                // the session, then save; or save as it is. Never throws.
                TopoSaveGuard.Install(application);
            }

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            AppDomain.CurrentDomain.AssemblyResolve -= ResolveAddinAssembly;
            return Result.Succeeded;
        }

        /// <summary>Appends one entry to %LOCALAPPDATA%\RVTuk\crash.log. Never throws.</summary>
        private static string? KeyOf(Document doc)
        {
            if (BrowserWindow == null) return null;
            try { return ProjectKey.Of(doc); }
            catch (Exception ex) { LogDiagnostic($"Family Browser project key failed: {ex}"); return null; }
        }

        private static void TellBrowser(Action<RVTuk.UI.FamilyBrowser.ViewModels.FamilyBrowserViewModel> tell)
        {
            try
            {
                var window = BrowserWindow;
                if (window == null) return;
                window.Dispatcher.BeginInvoke(new Action(() => { try { tell(window.ViewModel); } catch (Exception ex) { LogDiagnostic($"Family Browser project switch failed: {ex}"); } }));
            }
            catch (Exception ex)
            {
                LogDiagnostic($"Family Browser project switch failed: {ex}");
            }
        }

        internal static void LogDiagnostic(string message)
        {
            try
            {
                var logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RVTuk", "crash.log");
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, $"\n[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
            }
            catch { /* logging must never itself crash */ }
        }

        /// <summary>Name of the toolkit's ribbon panel (on the Add-Ins tab).</summary>
        internal const string RibbonPanelName = "RVTuk";

        /// <summary>Internal name of the Topo Tools push button. It is part of the button's
        /// command id, which the contextual Topo Tools button posts (see
        /// <see cref="ModifyTabHost"/>).</summary>
        internal const string TopoToolsButtonName = "TopoTools";

        /// <summary>Internal name of the Floor ⇄ Topo push button, part of its command id like
        /// <see cref="TopoToolsButtonName"/>.</summary>
        internal const string FloorTopoConvertButtonName = "FloorTopoConvert";

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
            RibbonPanel panel = app.CreateRibbonPanel(RibbonPanelName);

            var browseBtn = new PushButtonData(
                "BrowseLibrary",
                "Family\nBrowser",
                assemblyPath,
                typeof(BrowseLibraryCommand).FullName!)
            {
                ToolTip = "Search the office family library and load or update families in the open project"
            };
            browseBtn.LargeImage = CreateBrowseLibraryIcon(32);
            browseBtn.Image      = CreateBrowseLibraryIcon(16);

            panel.AddItem(browseBtn);

            var areaBtn = new PushButtonData(
                "AreaCalc",
                "Rishui\nZamin",
                assemblyPath,
                typeof(RishuiZaminCommand).FullName!)
            {
                ToolTip = "Rishui Zamin area calculation: generate the .dxf + .dat from the open sheet's Areas"
            };
            areaBtn.LargeImage = RVTuk.UI.RishuiZamin.Views.RishuiZaminIcon.Create(32);
            areaBtn.Image      = RVTuk.UI.RishuiZamin.Views.RishuiZaminIcon.Create(16);

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

            var roundBtn = new PushButtonData(
                "RoundPositions",
                "Round\nPositions",
                assemblyPath,
                typeof(RoundPositionsCommand).FullName!)
            {
                ToolTip = "Move every eligible element onto a whole-centimetre grid (lengths and angles are never changed)"
            };
            roundBtn.LargeImage = CreateRoundPositionsIcon(32);
            roundBtn.Image      = CreateRoundPositionsIcon(16);

            panel.AddItem(roundBtn);

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
                // Topo Tools was meant for Revit's Massing & Site tab. It cannot go there: the API
                // places custom panels on Add-Ins, on Analyze, or on a tab the add-in creates
                // itself — nothing else. Autodesk.Revit.UI.Tab naming only AddIns and Analyze is
                // that same limit seen from the enum. Reaching the real tab needs the unsupported
                // Autodesk.Windows ribbon, which Autodesk warns may break at any time; not worth a
                // silently vanishing button in a firm-wide toolkit, so it sits with its siblings.
                var topoBtn = new PushButtonData(
                    TopoToolsButtonName,
                    "Topo\nTools",
                    assemblyPath,
                    typeof(TopoStartCommand).FullName!)
                {
                    ToolTip = "Select a toposolid or floor, then press: draw topo lines and set their heights on the lines"
                };
                topoBtn.LargeImage = TopoIcons.TopoTools(32);
                topoBtn.Image      = TopoIcons.TopoTools(16);

                panel.AddItem(topoBtn);

                // The converter that used to be the pane's ⇄ button; acts on the selection.
                var convertBtn = new PushButtonData(
                    FloorTopoConvertButtonName,
                    "Floor ⇄\nTopo",
                    assemblyPath,
                    typeof(FloorTopoConvertCommand).FullName!)
                {
                    ToolTip = "Convert the selected floors to toposolids and the selected toposolids to floors, keeping their shape"
                };
                convertBtn.LargeImage = TopoIcons.FloorTopo(32);
                convertBtn.Image      = TopoIcons.FloorTopo(16);

                panel.AddItem(convertBtn);
            }

            if (RegisterRoomFloors)
            {
                var roomFloorBtn = new PushButtonData(
                    "RoomFloors",
                    "Room\nFloor",
                    assemblyPath,
                    typeof(RoomFloorCommand).FullName!)
                {
                    ToolTip = "Click rooms in a plan view to create a finish floor that follows each room's outline. Click a room again to update its floor."
                };
                roomFloorBtn.LargeImage = CreateRoomFloorsIcon(32);
                roomFloorBtn.Image      = CreateRoomFloorsIcon(16);

                panel.AddItem(roomFloorBtn);
            }

            // The Topo Edit session's panel: created hidden right after the RVTuk panel (the
            // ribbon API only appends), shown while a session is open.
            if (RegisterTopoTools) TopoEditPanel.Create(app, assemblyPath);

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

        private static BitmapSource CreateRoomFloorsIcon(int size)
        {
            var dv = new DrawingVisual();
            using (var ctx = dv.RenderOpen())
            {
                double s = size;
                ctx.DrawRectangle(new SolidColorBrush(WpfColor.FromRgb(0x25, 0x25, 0x26)), null,
                    new Rect(0, 0, s, s));

                // An L-shaped room: white walls around an orange floor.
                var outline = new StreamGeometry();
                using (var g = outline.Open())
                {
                    g.BeginFigure(new WpfPoint(s * 0.14, s * 0.14), true, true);
                    g.PolyLineTo(new[]
                    {
                        new WpfPoint(s * 0.86, s * 0.14),
                        new WpfPoint(s * 0.86, s * 0.86),
                        new WpfPoint(s * 0.50, s * 0.86),
                        new WpfPoint(s * 0.50, s * 0.50),
                        new WpfPoint(s * 0.14, s * 0.50),
                    }, true, true);
                }
                outline.Freeze();

                var wall = new Pen(new SolidColorBrush(Colors.White), Math.Max(1, s * 0.06));
                wall.Freeze();
                ctx.DrawGeometry(new SolidColorBrush(WpfColor.FromRgb(0xFF, 0x8C, 0x00)), wall, outline);
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

        private static BitmapSource CreateRoundPositionsIcon(int size)
        {
            var dv = new DrawingVisual();
            using (var ctx = dv.RenderOpen())
            {
                double s = size;
                ctx.DrawRectangle(new SolidColorBrush(WpfColor.FromRgb(0x25, 0x25, 0x26)), null,
                    new Rect(0, 0, s, s));

                // A faint grid with one element snapped onto an intersection.
                var gridPen = new Pen(new SolidColorBrush(WpfColor.FromRgb(0x5A, 0x5A, 0x5A)),
                    Math.Max(1, s * 0.04));
                gridPen.Freeze();
                foreach (var f in new[] { 0.30, 0.55, 0.80 })
                {
                    ctx.DrawLine(gridPen, new WpfPoint(s * 0.12, s * f), new WpfPoint(s * 0.88, s * f));
                    ctx.DrawLine(gridPen, new WpfPoint(s * f, s * 0.12), new WpfPoint(s * f, s * 0.88));
                }

                var arrowPen = new Pen(new SolidColorBrush(WpfColor.FromRgb(0xFF, 0x8C, 0x00)),
                    Math.Max(1, s * 0.07));
                arrowPen.Freeze();
                ctx.DrawLine(arrowPen, new WpfPoint(s * 0.72, s * 0.24), new WpfPoint(s * 0.58, s * 0.38));
                ctx.DrawLine(arrowPen, new WpfPoint(s * 0.58, s * 0.38), new WpfPoint(s * 0.58, s * 0.28));
                ctx.DrawLine(arrowPen, new WpfPoint(s * 0.58, s * 0.38), new WpfPoint(s * 0.68, s * 0.38));

                ctx.DrawEllipse(new SolidColorBrush(WpfColor.FromRgb(0xFF, 0x8C, 0x00)), null,
                    new WpfPoint(s * 0.55, s * 0.55), s * 0.09, s * 0.09);
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

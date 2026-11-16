using System;
using System.Linq;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using AwRibbonButton = Autodesk.Windows.RibbonButton;
using AwRibbonItemSize = Autodesk.Windows.RibbonItemSize;
using AwRibbonPanel = Autodesk.Windows.RibbonPanel;
using AwRibbonPanelSource = Autodesk.Windows.RibbonPanelSource;
using AwRibbonTab = Autodesk.Windows.RibbonTab;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// Puts the Topo Tools and Floor ⇄ Topo buttons on Revit's contextual Modify tab — what the
    /// user sees as "Modify | Toposolid" or "Modify | Floors":
    /// <list type="bullet">
    /// <item>Topo Tools while exactly one element is selected and it is a floor or a toposolid
    /// (not a subdivision). A floor the session cannot take (sloped, say) still shows it; the
    /// command refuses with the reason.</item>
    /// <item>Floor ⇄ Topo while the selection is not empty and every selected element is a floor
    /// or a toposolid (not a subdivision).</item>
    /// </list>
    ///
    /// The supported API cannot reach that tab, so this goes through the unsupported
    /// Autodesk.Windows ribbon. Per the probe (topo edit-session spec, "Probe results"), that
    /// "tab" is the one shared contextual tab with Id <c>Modify</c>, whose title and panels Revit
    /// swaps per selection: our panel is added to it once and shown/hidden on SelectionChanged,
    /// and each button shown/hidden by its own rule. The title is localised, so it is never
    /// matched. The probe was on a toposolid; a floor's tab is assumed to be the same one.
    ///
    /// The buttons do not run anything themselves: each posts its RVTuk-panel button's command,
    /// so it always does exactly what that button does.
    ///
    /// Fail-safe: any failure writes one line to the crash log and switches this off for the rest
    /// of the session; the add-in otherwise behaves exactly as without it.
    /// </summary>
    public static class ContextualTopoButton
    {
        /// <summary>Autodesk.Windows Id of the Add-Ins tab, which hosts the RVTuk panel.</summary>
        private const string AddInsTabId = "Add-Ins";

        /// <summary>Autodesk.Windows Id of the shared contextual Modify tab (probe result).</summary>
        private const string ModifyTabId = "Modify";

        private const string PanelId = "RVTuk_ContextualTopoTools";

        private const string LogPrefix = "Contextual Topo Tools buttons";

        private static UIControlledApplication? _app;
        private static ExternalEvent? _postEvent;
        private static PostCommandEventHandler? _postHandler;
        private static AwRibbonPanel? _panel;
        private static AwRibbonButton? _topoToolsButton;
        private static AwRibbonButton? _convertButton;
        private static bool _disabled;
        private static bool _topoToolsWanted;
        private static bool _convertWanted;
        private static bool _idleRetryPending;

        /// <summary>The command id Revit gives a push button on the RVTuk panel.</summary>
        private static string CommandNameOf(string buttonName) =>
            $"CustomCtrl_%CustomCtrl_%{AddInsTabId}%{Application.RibbonPanelName}%{buttonName}";

        /// <summary>Call from OnStartup after the ribbon is built. Never throws.</summary>
        public static void Install(UIControlledApplication app)
        {
            try
            {
                _app = app;
                // PostCommand goes through an external event: its handler runs in a valid API
                // context and is handed the UIApplication, so none has to be captured and kept.
                _postHandler = new PostCommandEventHandler();
                _postEvent = ExternalEvent.Create(_postHandler);
                app.SelectionChanged += OnSelectionChanged;
            }
            catch (Exception ex)
            {
                Fail("install", ex);
            }
        }

        private static void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_disabled) return;
            try
            {
                ReadSelection(e, out _topoToolsWanted, out _convertWanted);
            }
            catch (Exception ex)
            {
                Fail("reading the selection", ex);
                return;
            }
            Apply();
        }

        /// <summary>
        /// Brings the panel and its buttons in line with what is wanted. The panel is created
        /// lazily, the first time a button applies; the tab's presence is checked every time,
        /// since Revit may rebuild it.
        /// </summary>
        private static void Apply()
        {
            if (_disabled) return;
            try
            {
                // Hiding needs no tab: the panel object is enough, whether or not the tab is in
                // the ribbon right now (otherwise a stale panel could show on the next element's
                // Modify tab).
                if (!_topoToolsWanted && !_convertWanted)
                {
                    if (_panel != null) _panel.IsVisible = false;
                    return;
                }

                var tab = FindModifyTab();
                if (tab == null)
                {
                    // Revit may build the contextual tab after raising SelectionChanged: look
                    // again once Revit is idle.
                    RetryWhenIdle();
                    return;
                }

                if (_panel == null)
                {
                    _panel = CreatePanel();
                    if (_panel == null) return; // disabled inside
                }

                if (!tab.Panels.Contains(_panel))
                    tab.Panels.Add(_panel);

                _topoToolsButton!.IsVisible = _topoToolsWanted;
                _convertButton!.IsVisible = _convertWanted;
                _panel.IsVisible = true;
            }
            catch (Exception ex)
            {
                Fail("updating the Modify tab", ex);
            }
        }

        private static void RetryWhenIdle()
        {
            if (_idleRetryPending || _app == null) return;
            _idleRetryPending = true;
            _app.Idling += OnIdling;
        }

        private static bool _missingTabLogged;

        private static void OnIdling(object? sender, IdlingEventArgs e)
        {
            try
            {
                _app!.Idling -= OnIdling;
                _idleRetryPending = false;
            }
            catch (Exception ex)
            {
                Fail("idle retry", ex);
                return;
            }
            // One retry only: if the tab is still missing, the next selection change tries again.
            // Logged once per session, so a Revit year (or a floor) whose contextual tab is not
            // "Modify" (the Id was probed only on a toposolid in 2024) leaves a trace instead of
            // a silently absent button.
            if (FindModifyTabSafe() != null) Apply();
            else if (!_missingTabLogged)
            {
                _missingTabLogged = true;
                Application.LogDiagnostic(
                    $"{LogPrefix}: no contextual tab with Id \"{ModifyTabId}\" found after a toposolid or floor was selected.");
            }
        }

        /// <summary>
        /// Topo Tools: exactly one element, a floor or a toposolid. Floor ⇄ Topo: at least one,
        /// all floors or toposolids. Stops at the first element that is neither.
        /// </summary>
        private static void ReadSelection(SelectionChangedEventArgs e, out bool topoTools, out bool convert)
        {
            topoTools = convert = false;
            var ids = e.GetSelectedElements();
            if (ids == null || ids.Count == 0) return;
            var doc = e.GetDocument();
            if (doc == null) return;
            foreach (var id in ids)
                if (!IsFloorOrToposolid(doc.GetElement(id))) return;
            convert = true;
            topoTools = ids.Count == 1;
        }

        private static bool IsFloorOrToposolid(Element? element) =>
            element is Floor
            || element is Toposolid topo && topo.HostTopoId == ElementId.InvalidElementId;

        private static AwRibbonTab? FindModifyTab()
        {
            var ribbon = Autodesk.Windows.ComponentManager.Ribbon;
            if (ribbon == null) return null;
            return ribbon.Tabs.FirstOrDefault(t => t.IsContextualTab && t.Id == ModifyTabId);
        }

        private static AwRibbonTab? FindModifyTabSafe()
        {
            try { return FindModifyTab(); }
            catch (Exception ex) { Fail("finding the Modify tab", ex); return null; }
        }

        private static AwRibbonPanel? CreatePanel()
        {
            var topoToolsId = LookUp(Application.TopoToolsButtonName);
            if (topoToolsId == null) return null; // disabled inside
            var convertId = LookUp(Application.FloorTopoConvertButtonName);
            if (convertId == null) return null; // disabled inside

            _topoToolsButton = Button("_TopoTools", "Topo\nTools", topoToolsId,
                TopoIcons.TopoTools(32), TopoIcons.TopoTools(16),
                "Open Topo Tools — the same as the Topo Tools button on the RVTuk panel");
            _convertButton = Button("_FloorTopoConvert", "Floor ⇄\nTopo", convertId,
                TopoIcons.FloorTopo(32), TopoIcons.FloorTopo(16),
                "Convert the selected floors and toposolids — the same as the Floor ⇄ Topo button on the RVTuk panel");

            var source = new AwRibbonPanelSource { Id = PanelId, Title = Application.RibbonPanelName };
            source.Items.Add(_topoToolsButton);
            source.Items.Add(_convertButton);
            return new AwRibbonPanel { Source = source };
        }

        private static RevitCommandId? LookUp(string buttonName)
        {
            string commandName = CommandNameOf(buttonName);
            var commandId = RevitCommandId.LookupCommandId(commandName);
            if (commandId == null) Fail($"command '{commandName}' not found", null);
            return commandId;
        }

        private static AwRibbonButton Button(string idSuffix, string text, RevitCommandId commandId,
            System.Windows.Media.ImageSource largeImage, System.Windows.Media.ImageSource image, string toolTip) =>
            new AwRibbonButton
            {
                Id = PanelId + idSuffix,
                Text = text,
                ShowText = true,
                ShowImage = true,
                Size = AwRibbonItemSize.Large,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                LargeImage = largeImage,
                Image = image,
                ToolTip = toolTip,
                CommandHandler = new PostCommandHandler(commandId),
            };

        /// <summary>Logs one line and switches the contextual buttons off for the session.</summary>
        private static void Fail(string step, Exception? ex)
        {
            _disabled = true;
            Application.LogDiagnostic(ex == null
                ? $"{LogPrefix} disabled: {step}"
                : $"{LogPrefix} disabled ({step}): {ex.GetType().FullName}: {ex.Message}");

            // Best effort: do not leave a half-working button on screen.
            try
            {
                if (_panel != null) _panel.IsVisible = false;
                _app!.SelectionChanged -= OnSelectionChanged;
                if (_idleRetryPending) _app.Idling -= OnIdling;
            }
            catch { /* already disabled; nothing more to do */ }
        }

        /// <summary>
        /// Ribbon click: hand the event this button's command, then raise it. Clicks are serial on
        /// Revit's main thread, so a coalesced raise still posts the last command clicked.
        /// </summary>
        private sealed class PostCommandHandler : ICommand
        {
            private readonly RevitCommandId _commandId;

            public PostCommandHandler(RevitCommandId commandId) => _commandId = commandId;

#pragma warning disable CS0067 // never raised: the button is always executable
            public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067

            public bool CanExecute(object? parameter) => true;

            public void Execute(object? parameter)
            {
                if (_disabled || _postHandler == null) return;
                try
                {
                    _postHandler.CommandId = _commandId;
                    _postEvent?.Raise();
                }
                catch (Exception ex)
                {
                    Fail("raising the post event", ex);
                }
            }
        }

        private sealed class PostCommandEventHandler : IExternalEventHandler
        {
            /// <summary>The command to post next; set by the button clicked.</summary>
            public RevitCommandId? CommandId { get; set; }

            private bool _refusalLogged;

            public void Execute(UIApplication app)
            {
                try
                {
                    var commandId = CommandId;
                    CommandId = null;
                    if (commandId == null) return;

                    // Revit refuses a post now and then (another command still running, say): that
                    // is not a broken button, so log it once and ignore the click instead of Fail.
                    if (!app.CanPostCommand(commandId))
                    {
                        if (!_refusalLogged)
                        {
                            _refusalLogged = true;
                            Application.LogDiagnostic($"{LogPrefix}: Revit refused to post the command; click ignored.");
                        }
                        return;
                    }

                    app.PostCommand(commandId);
                }
                catch (Exception ex)
                {
                    Fail("posting the command", ex);
                }
            }

            public string GetName() => "RVTuk: post a contextual Topo Tools command";
        }
    }
}

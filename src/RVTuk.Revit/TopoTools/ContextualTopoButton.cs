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
    /// Puts the Topo Tools button on Revit's contextual Modify tab while exactly one toposolid
    /// (not a subdivision) is selected — what the user sees as "Modify | Toposolid".
    ///
    /// The supported API cannot reach that tab, so this goes through the unsupported
    /// Autodesk.Windows ribbon. Per the probe (topo edit-session spec, "Probe results"), that
    /// "tab" is the one shared contextual tab with Id <c>Modify</c>, whose title and panels Revit
    /// swaps per selection: our panel is added to it once and shown/hidden on SelectionChanged.
    /// The title is localised, so it is never matched.
    ///
    /// The button does not run anything itself: it posts the RVTuk-panel Topo Tools button's
    /// command, so it always does exactly what that button does.
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

        private static UIControlledApplication? _app;
        private static ExternalEvent? _postEvent;
        private static PostTopoToolsHandler? _postHandler;
        private static AwRibbonPanel? _panel;
        private static bool _disabled;
        private static bool _wanted;
        private static bool _idleRetryPending;

        /// <summary>The command id Revit gives the RVTuk-panel Topo Tools push button.</summary>
        private static string TopoToolsCommandName =>
            $"CustomCtrl_%CustomCtrl_%{AddInsTabId}%{Application.RibbonPanelName}%{Application.TopoToolsButtonName}";

        /// <summary>Call from OnStartup after the ribbon is built. Never throws.</summary>
        public static void Install(UIControlledApplication app)
        {
            try
            {
                _app = app;
                // PostCommand goes through an external event: its handler runs in a valid API
                // context and is handed the UIApplication, so none has to be captured and kept.
                _postHandler = new PostTopoToolsHandler();
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
                _wanted = IsSingleToposolid(e);
            }
            catch (Exception ex)
            {
                Fail("reading the selection", ex);
                return;
            }
            Apply();
        }

        /// <summary>
        /// Brings the panel in line with <see cref="_wanted"/>. The panel is created lazily, the
        /// first time a toposolid is selected; the tab's presence is checked every time, since
        /// Revit may rebuild it.
        /// </summary>
        private static void Apply()
        {
            if (_disabled) return;
            try
            {
                // Hiding needs no tab: the panel object is enough, whether or not the tab is in
                // the ribbon right now (otherwise a stale panel could show on the next element's
                // Modify tab).
                if (!_wanted)
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
            if (FindModifyTabSafe() != null) Apply();
        }

        private static bool IsSingleToposolid(SelectionChangedEventArgs e)
        {
            var ids = e.GetSelectedElements();
            if (ids == null || ids.Count != 1) return false;
            var doc = e.GetDocument();
            if (doc == null) return false;
            return doc.GetElement(ids.First()) is Toposolid topo
                   && topo.HostTopoId == ElementId.InvalidElementId;
        }

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
            string commandName = TopoToolsCommandName;
            var commandId = RevitCommandId.LookupCommandId(commandName);
            if (commandId == null)
            {
                Fail($"command '{commandName}' not found", null);
                return null;
            }
            _postHandler!.CommandId = commandId;

            var button = new AwRibbonButton
            {
                Id = PanelId + "_TopoTools",
                Text = "Topo\nTools",
                ShowText = true,
                ShowImage = true,
                Size = AwRibbonItemSize.Large,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                LargeImage = Application.CreateTopoToolsIcon(32),
                Image = Application.CreateTopoToolsIcon(16),
                ToolTip = "Open Topo Tools — the same as the Topo Tools button on the RVTuk panel",
                CommandHandler = new PostCommandHandler(),
            };

            var source = new AwRibbonPanelSource { Id = PanelId, Title = Application.RibbonPanelName };
            source.Items.Add(button);
            return new AwRibbonPanel { Source = source };
        }

        /// <summary>Logs one line and switches the contextual button off for the session.</summary>
        private static void Fail(string step, Exception? ex)
        {
            _disabled = true;
            Application.LogDiagnostic(ex == null
                ? $"Contextual Topo Tools button disabled: {step}"
                : $"Contextual Topo Tools button disabled ({step}): {ex.GetType().FullName}: {ex.Message}");

            // Best effort: do not leave a half-working button on screen.
            try
            {
                if (_panel != null) _panel.IsVisible = false;
                _app!.SelectionChanged -= OnSelectionChanged;
                if (_idleRetryPending) _app.Idling -= OnIdling;
            }
            catch { /* already disabled; nothing more to do */ }
        }

        /// <summary>Ribbon click: raise the event that posts the RVTuk-panel command.</summary>
        private sealed class PostCommandHandler : ICommand
        {
#pragma warning disable CS0067 // never raised: the button is always executable
            public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067

            public bool CanExecute(object? parameter) => true;

            public void Execute(object? parameter)
            {
                if (_disabled) return;
                try
                {
                    _postEvent?.Raise();
                }
                catch (Exception ex)
                {
                    Fail("raising the post event", ex);
                }
            }
        }

        private sealed class PostTopoToolsHandler : IExternalEventHandler
        {
            public RevitCommandId? CommandId { get; set; }

            private bool _refusalLogged;

            public void Execute(UIApplication app)
            {
                try
                {
                    if (CommandId == null) return;

                    // Revit refuses a post now and then (another command still running, say): that
                    // is not a broken button, so log it once and ignore the click instead of Fail.
                    if (!app.CanPostCommand(CommandId))
                    {
                        if (!_refusalLogged)
                        {
                            _refusalLogged = true;
                            Application.LogDiagnostic("Contextual Topo Tools button: Revit refused to post the command; click ignored.");
                        }
                        return;
                    }

                    app.PostCommand(CommandId);
                }
                catch (Exception ex)
                {
                    Fail("posting the Topo Tools command", ex);
                }
            }

            public string GetName() => "RVTuk: post Topo Tools command";
        }
    }
}

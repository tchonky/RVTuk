using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using RVTuk.Core.TopoTools;
using AwRibbonButton = Autodesk.Windows.RibbonButton;
using AwRibbonItemSize = Autodesk.Windows.RibbonItemSize;
using AwRibbonPanel = Autodesk.Windows.RibbonPanel;
using AwRibbonPanelSource = Autodesk.Windows.RibbonPanelSource;
using AwRibbonTab = Autodesk.Windows.RibbonTab;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// Puts our panels on Revit's Modify tab (what the user sees as "Modify | Toposolid" and the
    /// like) and takes them off again. Outside a Topo Edit session that is the Topo Tools
    /// shortcut, a panel with that one button, while exactly one toposolid that is not a
    /// subdivision is selected. Floor ⇄ Topo is not on the tab, and a floor starts its session from
    /// the RVTuk panel. The shortcut posts the RVTuk panel's Topo Tools command, so it does exactly
    /// what that button does.
    ///
    /// During a session the copy of the Topo Edit panel is there instead, whatever is selected: a
    /// second panel object showing the source of the Topo Edit panel on the Add-Ins tab, so it
    /// holds the same buttons and the same Spacing box, and a click on it is a click on the real
    /// button. It is made at the first session start, with the Topo Tools icon for when it is
    /// folded, and kept for the Revit session. When a session starts, the ribbon also switches to
    /// the Add-Ins tab, once, at Revit's first idle moment after the start.
    ///
    /// Which of our panels belong there is <see cref="ModifyTabPanels.Decide"/>'s answer, and one
    /// idempotent <see cref="Apply"/> makes the tab match it: a panel is added at the end of the
    /// tab, or removed, never only hidden. Revit sets the visibility of every panel on a contextual
    /// tab each time it builds the tab (read in UIFramework 2024, 2026 and 2027), so a hidden
    /// panel came back on the next selection. In Revit's edit modes none of ours is on the tab.
    /// Each panel of ours is of Revit's own panel class
    /// (<see cref="ModifyTabAccess.NewRevitPanel"/>): Revit's code walks the tab's panels as if
    /// all were.
    ///
    /// Apply runs after SelectionChanged, after a Topo Edit session starts or ends, and after the
    /// Modify tab's panels or properties change, which is how an edit mode starting or ending
    /// shows. It always runs later, through the ribbon's Dispatcher, one call pending at a time:
    /// never inside a notification (Autodesk.Windows throws on a panel list changed from its own
    /// notification), and it never calls the Revit API.
    ///
    /// The Modify tab is reachable only through the unsupported Autodesk.Windows ribbon. Fail-safe:
    /// any failure writes one line to the crash log, takes our panels off the tab, unhooks, and
    /// switches this off for the rest of the Revit session; the Add-Ins panels work on as before.
    /// Two parts fail on their own instead, each with one line, and stay off for the rest of the
    /// Revit session while everything else goes on: the copy (<see cref="CopyFailed"/>; during a
    /// session the shortcut then stays as it is outside one), and the switch to Add-Ins. For
    /// checking that in Revit, an empty file named <see cref="TabOffSwitch"/> or
    /// <see cref="CopyOffSwitch"/> in %LOCALAPPDATA%\RVTuk\, beside crash.log, switches a part off
    /// when Revit starts.
    /// </summary>
    internal static class ModifyTabHost
    {
        /// <summary>With a file of this name in %LOCALAPPDATA%\RVTuk\ when Revit starts, the
        /// fail-safe runs at once: nothing of ours goes on the Modify tab that Revit session.</summary>
        internal const string TabOffSwitch = "topo-modify-tab-off";

        /// <summary>With a file of this name in %LOCALAPPDATA%\RVTuk\ when Revit starts,
        /// <see cref="CopyAvailable"/> stays false that Revit session.</summary>
        internal const string CopyOffSwitch = "topo-modify-copy-off";

        /// <summary>Autodesk.Windows source Id of the shortcut panel.</summary>
        private const string ShortcutPanelId = "RVTuk_ContextualTopoTools";

        private const string LogPrefix = "Topo Tools Modify-tab panels";

        private static UIControlledApplication? _app;
        private static ExternalEvent? _postEvent;
        private static PostCommandEventHandler? _postHandler;

        /// <summary>The RVTuk panel's Topo Tools command, looked up at the first selection of one
        /// toposolid: Apply, which makes the shortcut, must not call the Revit API.</summary>
        private static RevitCommandId? _topoToolsCommand;

        /// <summary>The shortcut panel: made the first time it is wanted, then kept for the Revit
        /// session and added or removed as wanted.</summary>
        private static AwRibbonPanel? _shortcut;

        /// <summary>The copy of the Topo Edit panel: made at the first session start
        /// (<see cref="MakeCopyOnce"/>), then kept for the Revit session and added or removed as
        /// wanted.</summary>
        private static AwRibbonPanel? _copy;

        /// <summary>The Modify tab whose panel list and properties are hooked. Panels of ours go
        /// on this tab only.</summary>
        private static AwRibbonTab? _watchedTab;

        /// <summary>What the last SelectionChanged said: exactly one toposolid, not a subdivision,
        /// is selected.</summary>
        private static bool _oneToposolidSelected;

        private static bool _applyPending;
        private static bool _applying;
        private static bool _disabled;

        /// <summary>A session started since the last Apply: the next one is preceded by making the
        /// copy (<see cref="RunPendingApply"/>).</summary>
        private static bool _sessionStartPending;

        /// <summary>Set by a copy failure during Apply, which then asks for one more Apply as it
        /// ends: that one takes the copy off the tab, if it is on it, and lets the shortcut stand
        /// in for it. Requests made while Apply runs are otherwise dropped.</summary>
        private static bool _applyAgain;

        /// <summary>The switch to Add-Ins threw once: it is not tried again this Revit session.</summary>
        private static bool _switchOff;

        /// <summary>A switch to Add-Ins that did not take is logged once per Revit session.</summary>
        private static bool _switchRefusalLogged;

        /// <summary>The application whose Idling is hooked for the switch to Add-Ins: set when a
        /// session start hooks it, cleared when the handler unhooks it at Revit's first idle moment.
        /// One hook serves every start before that moment.</summary>
        private static UIControlledApplication? _switchIdlingApp;

        /// <summary>
        /// Whether the copy of the Topo Edit panel can go on the Modify tab during a session. True
        /// from <see cref="Install"/> unless <see cref="CopyOffSwitch"/> is there; false for the
        /// rest of the Revit session after a copy failure (<see cref="CopyFailed"/>). While false,
        /// during a session the shortcut follows the selection as it does outside one, and opens
        /// the session dialog.
        /// </summary>
        internal static bool CopyAvailable { get; private set; }

        /// <summary>Call once from OnStartup, after the ribbon is built. Reads the two switch files,
        /// once. Never throws.</summary>
        internal static void Install(UIControlledApplication app)
        {
            try
            {
                CopyAvailable = !SwitchFileExists(CopyOffSwitch);
                if (!CopyAvailable)
                    Application.LogDiagnostic(
                        $"{LogPrefix}: the Topo Edit copy is switched off by {CopyOffSwitch} for this Revit session.");
                if (SwitchFileExists(TabOffSwitch))
                {
                    Fail("switched off by " + TabOffSwitch, null);
                    return;
                }

                _app = app;
                // PostCommand goes through an external event: its handler runs in a valid API
                // context and is handed the UIApplication, so none has to be captured and kept.
                _postHandler = new PostCommandEventHandler();
                _postEvent = ExternalEvent.Create(_postHandler);
                app.SelectionChanged += OnSelectionChanged;
                TopoSession.Started += OnSessionStarted;
                TopoSession.Ended += OnSessionEnded;
            }
            catch (Exception ex)
            {
                Fail("install", ex);
            }
        }

        /// <summary>Whether %LOCALAPPDATA%\RVTuk\<paramref name="name"/> exists. Never throws: an
        /// error counts as no file.</summary>
        private static bool SwitchFileExists(string name)
        {
            try
            {
                return File.Exists(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RVTuk", name));
            }
            catch (Exception)
            {
                return false; // the switches' rule: an error counts as no file
            }
        }

        // ------------------------------------------------------------------ what asks for an Apply

        /// <summary>Notes whether exactly one toposolid, not a subdivision, is selected, and asks
        /// for an Apply. Once switched off, here or anywhere, it unhooks itself: a Revit API call,
        /// made here because the fail-safe may run where the Revit API must not be called.</summary>
        private static void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (!_disabled)
            {
                try
                {
                    ReadSelection(e);
                }
                catch (Exception ex)
                {
                    Fail("reading the selection", ex);
                }
            }

            if (_disabled) UnhookSelectionChanged();
            else RequestApply();
        }

        /// <summary>Reads the selection; the first time it is one toposolid, also looks up the
        /// command the shortcut posts (switching off if Revit does not know it).</summary>
        private static void ReadSelection(SelectionChangedEventArgs e)
        {
            bool oneToposolid = IsOneToposolid(e);
            if (oneToposolid && _topoToolsCommand == null)
            {
                string commandName = ModifyTabAccess.AddInsButtonId(Application.RibbonPanelName, Application.TopoToolsButtonName);
                _topoToolsCommand = RevitCommandId.LookupCommandId(commandName);
                if (_topoToolsCommand == null)
                {
                    Fail($"command '{commandName}' not found", null);
                    return;
                }
            }
            _oneToposolidSelected = oneToposolid;
        }

        /// <summary>Exactly one element is selected, and it is a toposolid that is not a
        /// subdivision.</summary>
        private static bool IsOneToposolid(SelectionChangedEventArgs e)
        {
            var ids = e.GetSelectedElements();
            if (ids == null || ids.Count != 1) return false;
            var doc = e.GetDocument();
            return doc?.GetElement(ids.First()) is Toposolid topo && topo.HostTopoId == ElementId.InvalidElementId;
        }

        private static void UnhookSelectionChanged()
        {
            var app = _app;
            _app = null;
            if (app == null) return;
            try
            {
                app.SelectionChanged -= OnSelectionChanged;
            }
            catch (Exception)
            {
                // Already switched off and logged; the handler does nothing from now on anyway.
            }
        }

        /// <summary>A session started, inside its start command. The Apply asked for here is
        /// preceded by making the copy, if it is not made yet; the switch to Add-Ins waits for
        /// Revit's first idle moment after the start (<see cref="OnFirstIdleAfterStart"/>). Both
        /// run once the command has returned.</summary>
        private static void OnSessionStarted()
        {
            _sessionStartPending = true;
            HookIdlingForSwitch();
            RequestApply();
        }

        private static void OnSessionEnded() => RequestApply();

        private static void OnModifyPanelsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RequestApply();

        private static void OnModifyPropertyChanged(object? sender, PropertyChangedEventArgs e) => RequestApply();

        /// <summary>
        /// Asks for one <see cref="Apply"/> later, through the ribbon's Dispatcher. Nothing more is
        /// asked while one is pending, or while Apply runs: its own changes to the tab notify too.
        /// Never throws, since it runs inside notifications.
        /// </summary>
        private static void RequestApply()
        {
            if (_disabled || _applyPending || _applying) return;
            try
            {
                _applyPending = Defer(RunPendingApply);
            }
            catch (Exception ex)
            {
                _applyPending = false;
                Fail("deferring the update", ex);
            }
        }

        /// <summary>The Dispatcher callback <see cref="RequestApply"/> queues. After a session
        /// start the copy is made first, outside Apply's fail-safe.</summary>
        private static void RunPendingApply()
        {
            _applyPending = false;
            if (_sessionStartPending)
            {
                _sessionStartPending = false;
                MakeCopyOnce();
            }
            Apply();
        }

        // ------------------------------------------------------------------ Apply

        /// <summary>
        /// Makes the Modify tab hold exactly the panels of ours that
        /// <see cref="ModifyTabPanels.Decide"/> asks for. Idempotent. Runs only from the ribbon's
        /// Dispatcher (<see cref="RequestApply"/>) and never calls the Revit API. Any failure is the
        /// fail-safe's: if the shortcut cannot be made, nothing of ours goes on the tab. The copy is
        /// the exception: adding or removing it fails under its own rule
        /// (<see cref="ApplyCopy"/>).
        /// </summary>
        private static void Apply()
        {
            if (_disabled) return;
            _applying = true;
            string step = "finding the Modify tab";
            try
            {
                var tab = ModifyTabAccess.FindTab(ModifyTabAccess.ModifyTabId);
                step = "watching the Modify tab";
                Watch(tab);
                if (tab == null)
                {
                    // Revit has its Modify tab from start-up on (probe): without one, nothing of
                    // ours can show where it is wanted.
                    if (_oneToposolidSelected)
                        Fail($"no tab with Id \"{ModifyTabAccess.ModifyTabId}\" while a toposolid is selected", null);
                    return;
                }

                step = "reading the edit mode";
                bool inEditMode = ModifyTabAccess.IsInEditMode(tab);

                // The last SelectionChanged's answer, counted only while the tab is contextual, as
                // it is whenever something is selected: the shortcut cannot stay on the plain
                // Modify tab should Revit clear the tab without a SelectionChanged. It also goes on
                // only after Revit's own panels for the selection, and so lands after them: Revit
                // puts the panels it adds behind one of ours already on the tab.
                bool oneToposolidSelected = _oneToposolidSelected && IsContextual(tab);

                var wanted = ModifyTabPanels.Decide(
                    sessionOpen: TopoSession.Current != null,
                    inEditMode: inEditMode,
                    oneToposolidSelected: oneToposolidSelected,
                    copyAvailable: CopyAvailable);

                ApplyCopy(tab, wanted.TopoEditCopy);

                if (wanted.TopoToolsShortcut)
                {
                    step = "making the shortcut";
                    _shortcut ??= MakeShortcutPanel(_topoToolsCommand
                        ?? throw new InvalidOperationException("The Topo Tools command was not looked up."));
                    step = "adding the shortcut";
                    PutAtEnd(tab, _shortcut);
                }
                else if (_shortcut != null)
                {
                    step = "removing the shortcut";
                    TakeOff(tab, _shortcut);
                }
            }
            catch (Exception ex)
            {
                Fail(step, ex);
            }
            finally
            {
                _applying = false;
                if (_applyAgain)
                {
                    _applyAgain = false;
                    RequestApply();
                }
            }
        }

        /// <summary>
        /// The copy's part of <see cref="Apply"/>: on <paramref name="tab"/>, at its end, when
        /// <paramref name="wanted"/>, and off it otherwise, which also takes it off after a copy
        /// failure. A failure here is the copy's (<see cref="CopyFailed"/>), never the fail-safe's.
        /// Nothing to do before the copy is made.
        /// </summary>
        private static void ApplyCopy(AwRibbonTab tab, bool wanted)
        {
            var copy = _copy;
            if (copy == null) return;
            try
            {
                if (wanted) PutAtEnd(tab, copy);
                else TakeOff(tab, copy);
            }
            catch (Exception ex)
            {
                CopyFailed(wanted ? "adding the copy" : "removing the copy", ex);
            }
        }

        /// <summary>The shortcut: the Topo Tools button alone, posting <paramref name="command"/>,
        /// on a panel of Revit's own class.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static AwRibbonPanel MakeShortcutPanel(RevitCommandId command)
        {
            var button = new AwRibbonButton
            {
                Id = ShortcutPanelId + "_TopoTools",
                Text = "Topo\nTools",
                ShowText = true,
                ShowImage = true,
                Size = AwRibbonItemSize.Large,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                LargeImage = TopoIcons.TopoTools(32),
                Image = TopoIcons.TopoTools(16),
                ToolTip = "Open Topo Tools — the same as the Topo Tools button on the RVTuk panel",
                CommandHandler = new PostCommandHandler(command),
            };
            var source = new AwRibbonPanelSource { Id = ShortcutPanelId, Title = Application.RibbonPanelName };
            source.Items.Add(button);
            return ModifyTabAccess.NewRevitPanel(source);
        }

        // ------------------------------------------------------------------ the copy and the switch

        /// <summary>
        /// Makes the copy at a session start, once per Revit session: a panel of Revit's own class
        /// showing the source of the Topo Edit panel on the Add-Ins tab. Then, in a try of its own,
        /// gives it the Topo Tools icon for when it is folded; if that fails, the copy is kept and
        /// folds to its first icon, the ✗. Runs before Apply, outside its fail-safe: a failure is
        /// the copy's (<see cref="CopyFailed"/>).
        /// </summary>
        private static void MakeCopyOnce()
        {
            if (_disabled || !CopyAvailable || _copy != null) return;
            try
            {
                _copy = NewCopy();
            }
            catch (Exception ex)
            {
                CopyFailed("making the copy", ex);
                return;
            }
            if (_copy == null)
            {
                CopyFailed("Topo Edit panel not found on the Add-Ins tab", null);
                return;
            }

            try
            {
                SetFoldedIcon(_copy);
            }
            catch (Exception ex)
            {
                Application.LogDiagnostic(
                    $"{LogPrefix}: the Topo Edit copy folds to its first icon: {ex.GetType().FullName}: {ex.Message}");
            }
        }

        /// <summary>
        /// The copy's fail-safe: logs one line (the step, then the exception's type and message, if
        /// any) and switches the copy off for the rest of the Revit session. All else goes on:
        /// during a session the shortcut then stands in for the copy, and the Add-Ins panel works
        /// as before. It never changes the tab's panel list itself, wherever it runs: a copy that
        /// may be on the tab comes off in one more Apply, run later through the Dispatcher. Once
        /// the copy is off, a later failure (taking it off, say) adds nothing.
        /// </summary>
        private static void CopyFailed(string step, Exception? ex)
        {
            if (!CopyAvailable) return;
            CopyAvailable = false;
            Application.LogDiagnostic(ex == null
                ? $"{LogPrefix}: the Topo Edit copy is off for this Revit session: {step}"
                : $"{LogPrefix}: the Topo Edit copy is off for this Revit session ({step}): {ex.GetType().FullName}: {ex.Message}");

            if (_copy == null) return; // never made, so on no tab
            if (_applying) _applyAgain = true;
            else RequestApply();
        }

        /// <summary>
        /// At a session start, hooks Revit's Idling once for the switch to Add-Ins: the switch
        /// waits for Revit's first idle moment after the start, which comes once Revit is done with
        /// the start command. That is the moment the probe saw the switch take (Revit 2024,
        /// 2026-11-18); an earlier switch could be undone by Revit rebuilding Modify | Toposolid
        /// after the command. A start while the hook is pending adds nothing. Runs in the start
        /// command, where the Revit API may be called. Never throws: a failure turns off only the
        /// switch.
        /// </summary>
        private static void HookIdlingForSwitch()
        {
            if (_disabled || _switchOff || _switchIdlingApp != null) return;
            var app = _app;
            if (app == null) return;
            try
            {
                app.Idling += OnFirstIdleAfterStart;
                _switchIdlingApp = app;
            }
            catch (Exception ex)
            {
                SwitchFailed("hooking Idling", ex);
            }
        }

        /// <summary>
        /// Revit's first idle moment after a session start: unhooks itself first, whatever
        /// follows, then queues the switch on the ribbon's Dispatcher, as the probe did. The copy
        /// and its Apply were queued at the start, so the switch runs after them. Never throws: a
        /// failure turns off only the switch.
        /// </summary>
        private static void OnFirstIdleAfterStart(object? sender, IdlingEventArgs e)
        {
            try
            {
                var app = _switchIdlingApp;
                _switchIdlingApp = null;
                if (app != null) app.Idling -= OnFirstIdleAfterStart;

                if (_disabled || _switchOff) return;
                if (!Defer(SwitchToAddIns)) NoteSwitchRefused("there is no ribbon");
            }
            catch (Exception ex)
            {
                SwitchFailed("at Revit's first idle moment", ex);
            }
        }

        /// <summary>
        /// The Dispatcher callback queued at Revit's first idle moment after a session start:
        /// makes the Add-Ins tab the active one and reads it back. A hidden tab refuses without a
        /// word (read in AdWindows 2024 to 2027). Revit notes the tab that is active when it opens a
        /// Modify | … tab and goes back to it when that tab closes (read in UIFramework 2024, 2026
        /// and 2027), which is how a selection and Esc then land on Add-Ins. Only at a start: a tab
        /// picked afterwards is the user's. Nothing when the session has ended meanwhile.
        /// </summary>
        private static void SwitchToAddIns()
        {
            if (_disabled || _switchOff || TopoSession.Current == null) return;
            try
            {
                var refusal = ActivateAddInsTab();
                if (refusal != null) NoteSwitchRefused(refusal);
            }
            catch (Exception ex)
            {
                SwitchFailed("switching to the Add-Ins tab", ex);
            }
        }

        /// <summary>A switch to Add-Ins that did not take: one line, the first time in the Revit
        /// session. It is tried again at the next start.</summary>
        private static void NoteSwitchRefused(string why)
        {
            if (_switchRefusalLogged) return;
            _switchRefusalLogged = true;
            Application.LogDiagnostic($"{LogPrefix}: the ribbon did not switch to the Add-Ins tab: {why}.");
        }

        /// <summary>
        /// The switch's fail-safe: one line (the step, then the exception's type and message), and
        /// the switch is off for the rest of the Revit session. Nothing else is switched off. A hook
        /// on Idling still pending unhooks itself at the next idle moment: this may run from the
        /// Dispatcher, where the Revit API must not be called.
        /// </summary>
        private static void SwitchFailed(string step, Exception ex)
        {
            if (_switchOff) return;
            _switchOff = true;
            Application.LogDiagnostic(
                $"{LogPrefix}: the switch to the Add-Ins tab is off for this Revit session ({step}): {ex.GetType().FullName}: {ex.Message}");
        }

        // ------------------------------------------------------------------ Autodesk.Windows, NoInlining

        /// <summary>Queues <paramref name="action"/> on the ribbon's Dispatcher, at Normal priority:
        /// after the notification that asked for it, and before the ribbon is next drawn. False when
        /// there is no ribbon, and so no Modify tab.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool Defer(Action action)
        {
            var ribbon = Autodesk.Windows.ComponentManager.Ribbon;
            if (ribbon == null) return false;
            ribbon.Dispatcher.BeginInvoke(DispatcherPriority.Normal, action);
            return true;
        }

        /// <summary>Hooks the panel list and properties of <paramref name="tab"/>, the Modify tab
        /// Revit has now. If that is another object than before, our panels first leave the old
        /// one, the copy under its own failure rule, and it is unhooked. Null unhooks only.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Watch(AwRibbonTab? tab)
        {
            var old = _watchedTab;
            if (ReferenceEquals(tab, old)) return;
            if (old != null)
            {
                ApplyCopy(old, wanted: false);
                if (_shortcut != null) old.Panels.Remove(_shortcut);
                Unhook(old);
            }

            _watchedTab = tab; // before hooking: if that throws, the fail-safe unhooks this tab
            if (tab == null) return;
            tab.Panels.CollectionChanged += OnModifyPanelsChanged;
            tab.PropertyChanged += OnModifyPropertyChanged;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Unhook(AwRibbonTab tab)
        {
            tab.Panels.CollectionChanged -= OnModifyPanelsChanged;
            tab.PropertyChanged -= OnModifyPropertyChanged;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool IsContextual(AwRibbonTab tab) => tab.IsContextualTab;

        /// <summary>Adds <paramref name="panel"/> at the end of the tab, unless it is on it.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void PutAtEnd(AwRibbonTab tab, AwRibbonPanel panel)
        {
            if (!tab.Panels.Contains(panel)) tab.Panels.Add(panel);
        }

        /// <summary>Removes <paramref name="panel"/> from the tab, if it is on it.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void TakeOff(AwRibbonTab tab, AwRibbonPanel panel) => tab.Panels.Remove(panel);

        /// <summary>A new copy of the Topo Edit panel on the Add-Ins tab, of Revit's own panel class
        /// and showing that panel's source; null when the panel is not there. Throws when the copy
        /// cannot be made.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static AwRibbonPanel? NewCopy()
        {
            var addIns = TopoEditPanel.FindOnAddIns();
            if (addIns == null) return null;
            // Never a copy without a source: Revit's code reads the source Id of every panel on
            // the Modify tab.
            var source = addIns.Source
                ?? throw new InvalidOperationException("The Topo Edit panel on the Add-Ins tab has no source.");
            return ModifyTabAccess.NewRevitPanel(source);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void SetFoldedIcon(AwRibbonPanel panel) => panel.CollapsedPanelImage = TopoIcons.TopoTools(32);

        /// <summary>Makes the Add-Ins tab the active one. Null when it then is; otherwise why
        /// not.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string? ActivateAddInsTab()
        {
            var tab = ModifyTabAccess.FindTab(ModifyTabAccess.AddInsTabId);
            if (tab == null) return $"no tab with Id \"{ModifyTabAccess.AddInsTabId}\"";
            tab.IsActive = true;
            return tab.IsActive ? null : "the tab did not become active (a hidden tab refuses)";
        }

        // ------------------------------------------------------------------ the fail-safe

        /// <summary>
        /// Logs one line (the step, then the exception's type and message), takes our panels off
        /// the Modify tab, unhooks, and switches this off for the rest of the Revit session.
        /// SelectionChanged is unhooked by its own handler (<see cref="OnSelectionChanged"/>), and
        /// so is a pending Idling hook of the switch (<see cref="OnFirstIdleAfterStart"/>): this
        /// may run from the Dispatcher, where the Revit API must not be called.
        /// </summary>
        private static void Fail(string step, Exception? ex)
        {
            if (_disabled) return;
            _disabled = true;
            Application.LogDiagnostic(ex == null
                ? $"{LogPrefix} disabled: {step}"
                : $"{LogPrefix} disabled ({step}): {ex.GetType().FullName}: {ex.Message}");

            TopoSession.Started -= OnSessionStarted;
            TopoSession.Ended -= OnSessionEnded;

            // Best effort, each part on its own: the line above already says what broke.
            var tab = _watchedTab;
            if (tab == null) return;
            if (_copy != null)
            {
                // The copy comes off later, through the Dispatcher, never from here: this may run
                // inside a notification of the tab's panel list (from RequestApply), where a
                // change to the list throws.
                try
                {
                    Defer(TakeCopyOffAfterFail);
                }
                catch (Exception)
                {
                    // The copy stays where it is: switched off, and logged above.
                }
            }
            if (_shortcut != null)
            {
                try
                {
                    TakeOff(tab, _shortcut);
                }
                catch (Exception)
                {
                    // Nothing more to do: switched off, and logged above.
                }
            }
            try
            {
                Unhook(tab);
            }
            catch (Exception)
            {
                // The same: the handlers do nothing once switched off.
            }
        }

        /// <summary>Dispatcher callback queued by <see cref="Fail"/>: the copy leaves the watched
        /// tab. Best effort; never throws.</summary>
        private static void TakeCopyOffAfterFail()
        {
            var tab = _watchedTab;
            var copy = _copy;
            if (tab == null || copy == null) return;
            try
            {
                TakeOff(tab, copy);
            }
            catch (Exception)
            {
                // Nothing more to do: switched off, and logged by Fail.
            }
        }

        // ------------------------------------------------------------------ the shortcut's click

        /// <summary>
        /// Ribbon click: hands the event the command, then raises it. Clicks are serial on Revit's
        /// main thread, so a coalesced raise still posts the last command clicked.
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

            public string GetName() => "RVTuk: post the Topo Tools command from the Modify tab";
        }
    }
}

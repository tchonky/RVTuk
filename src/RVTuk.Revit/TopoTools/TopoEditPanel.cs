using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using RVTuk.Core.Shared.Config;
using RVTuk.Core.TopoTools;
using RVTuk.Revit.TopoTools.Commands;
using AwRibbonItem = Autodesk.Windows.RibbonItem;
using AwRibbonItemSize = Autodesk.Windows.RibbonItemSize;
using AwRibbonPanel = Autodesk.Windows.RibbonPanel;
using AwRibbonRowBreak = Autodesk.Windows.RibbonRowBreak;
using AwRibbonRowPanel = Autodesk.Windows.RibbonRowPanel;
using AwRibbonTab = Autodesk.Windows.RibbonTab;
using AwRibbonTextBox = Autodesk.Windows.RibbonTextBox;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// The temporary "Topo Edit" ribbon panel: created hidden at startup, right after the RVTuk
    /// panel (the ribbon API only appends), shown while a Topo Edit session is open. It hides
    /// itself on <see cref="TopoSession.Ended"/>, so every way a session ends — Accept, Cancel,
    /// the document closing, switching document — takes the panel with it.
    ///
    /// Cancel (✗) above Accept (✓) at its left end, as Revit's own Mode panel has them, then a
    /// stack of Draw Lines, Pick Lines and the Height box, then the Update split button (Heights,
    /// the default, and Labels). The API makes ✗ and ✓ as small buttons that show their text; the
    /// first <see cref="Show"/> makes them icons only through the unsupported Autodesk.Windows
    /// ribbon, gives the folded panel the Topo Tools icon, and gives the Height box its "H="
    /// label and keeps text typed in it when the box loses focus. If that fails, the API's look
    /// stays and the panel works the same, except that typed text needs Enter before Pick. As in <see cref="ModifyTabAccess"/>, each
    /// Autodesk.Windows member is reached in a NoInlining method called inside a try: a member
    /// that a Revit update removed then throws there and is logged in one line, not while a
    /// caller is being compiled, outside its try.
    /// </summary>
    public static class TopoEditPanel
    {
        public const string PanelName = "Topo Edit";

        /// <summary>Internal name of the Cancel button: part of its command id and of its
        /// Autodesk.Windows Id (<see cref="ItemIdOf"/>).</summary>
        internal const string CancelName = "TopoCancel";

        /// <summary>Internal name of the Accept button, used as <see cref="CancelName"/> is.</summary>
        internal const string AcceptName = "TopoAccept";

        /// <summary>The panel's Autodesk.Windows source Id: CustomCtrl_%Add-Ins%Topo Edit.</summary>
        private static readonly string PanelSourceId = ModifyTabAccess.AddInsPanelId(PanelName);

        private static RibbonPanel? _panel;
        private static PushButton? _cancelButton;
        private static PushButton? _acceptButton;
        private static TextBox? _heightBox;

        /// <summary>The last selection SelectionChanged reported, read again when the document
        /// changes.</summary>
        private static ICollection<ElementId> _lastSelection = new List<ElementId>();

        /// <summary>What the add-in last put in the Height box; other text there was typed.</summary>
        private static string _lastShown = "";

        /// <summary>Set by the first <see cref="Show"/>: the icons-only look is tried once per
        /// Revit session, whatever comes of it.</summary>
        private static bool _iconsOnlyTried;

        /// <summary>The distance between points along a topo line, in Revit's internal feet: a
        /// fixed 100 cm (the Spacing box was taken out on 2026-11-23).</summary>
        public const double SpacingFeet = 100 / 30.48;

        /// <summary>Internal name of the Height box: part of its Autodesk.Windows Id.</summary>
        private const string HeightName = "TopoHeight";

        public static void Create(UIControlledApplication app, string assemblyPath)
        {
            var panel = app.CreateRibbonPanel(PanelName);

            // Added first, so the stack sits at the panel's left end; its first item is the top row.
            bool dark = IsDarkTheme();
            var stack = panel.AddStackedItems(
                Button<TopoCancelCommand>(CancelName, "Cancel", assemblyPath,
                    "Close the session: delete the lines and texts it made and put changed heights back",
                    size => TopoIcons.Cancel(size, dark)),
                Button<TopoAcceptCommand>(AcceptName, "Accept", assemblyPath,
                    "Apply the topo lines and close the session; lines and heights stay in the view",
                    size => TopoIcons.Accept(size, dark)));
            _cancelButton = (PushButton)stack[0];
            _acceptButton = (PushButton)stack[1];

            // A stack of three small rows: Draw Lines, Pick Lines, the Height box.
            var height = new TextBoxData(HeightName)
            {
                ToolTip = "Height for the selected topo lines and for Pick, in metres. Type a height and press Enter " +
                          "to give it to the selected lines, or +0.5 / -0.5 to raise or lower each from its own " +
                          "height (=-3.20 sets a height below zero). What you type stays, for Pick or the next Enter; " +
                          "clear the box to see the selected line's height. Update moves the points.",
            };
            var rows = panel.AddStackedItems(
                Button<DrawTopoLinesCommand>("TopoDrawLines", "Draw Lines", assemblyPath,
                    "Start Revit's Detail Line tool in the session's view, opening it first if needed: every line drawn there becomes a topo line with a height text",
                    TopoIcons.DrawLines),
                Button<PickHeightCommand>("TopoPick", "Pick Lines", assemblyPath,
                    "Click topo lines one by one to do what the Height box holds: 43 gives each the height 43.00, " +
                    "+0.1 raises it 10 cm on every click, -0.1 lowers it. Lines selected first get it straight away. " +
                    "Esc to finish.",
                    TopoIcons.Pick),
                height);
            var heightBox = (TextBox)rows[2];
            heightBox.PromptText = "Height (m)";
            heightBox.ShowImageAsButton = false;
            heightBox.SelectTextOnFocus = true;
            heightBox.Width = 140;
            heightBox.Value = "";
            heightBox.EnterPressed += OnHeightEntered;

            // Update: Heights is the button's own click (it never switches to the last used one);
            // Labels is in its menu.
            var update = (SplitButton)panel.AddItem(new SplitButtonData("TopoUpdateMenu", "Update"));
            update.AddPushButton(Button<TopoUpdateCommand>("TopoUpdate", "Heights", assemblyPath,
                "Apply the topo lines and their heights to the toposolid or floor being edited, and stay in the session",
                TopoIcons.Update));
            update.AddPushButton(Button<HeightLabelsCommand>("TopoHeightLabels", "Labels", assemblyPath,
                "Give every topo line in the session's view a height text at its middle, and re-centre the existing ones",
                TopoIcons.HeightLabels));
            update.IsSynchronizedWithCurrentItem = false;

            panel.Visible = false;

            _panel = panel;
            _heightBox = heightBox;
            TopoSession.Started += ClearHeightBox;
            TopoSession.Ended += ClearHeightBox;
            TopoSession.Ended += Hide;
            app.SelectionChanged += OnSelectionForHeight;
            app.ControlledApplication.DocumentChanged += OnDocumentChangedForHeight;
            app.ThemeChanged += OnThemeChanged;
        }

        public static void Show()
        {
            if (_panel == null) return;
            _panel.Visible = true;

            // After Visible, so the panel is surely in the tab. Nothing shows in between: the
            // ribbon is redrawn only once Revit's thread is free again. In a try: what a Revit
            // update took from Autodesk.Windows throws as MakeStackIconsOnly is compiled, before
            // its own tries.
            if (!_iconsOnlyTried)
            {
                _iconsOnlyTried = true;
                try
                {
                    MakeStackIconsOnly();
                }
                catch (Exception ex)
                {
                    Application.LogDiagnostic(
                        "Topo Edit: Cancel and Accept keep their text, and the folded panel keeps its first icon: " +
                        $"{ex.GetType().FullName}: {ex.Message}");
                }

                try
                {
                    LabelHeightBox();
                }
                catch (Exception ex)
                {
                    Application.LogDiagnostic(
                        "Topo Edit: the Height box keeps the API's look, and typed text needs Enter before Pick: " +
                        $"{ex.GetType().FullName}: {ex.Message}");
                }
            }
        }

        public static void Hide()
        {
            if (_panel != null) _panel.Visible = false;
        }

        /// <summary>
        /// This panel's Autodesk.Windows object on the Add-Ins tab, hidden or not; null when it is
        /// not there. Unsupported API, so it may throw: every caller catches.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static AwRibbonPanel? FindOnAddIns() => FindAddInsTab()?.FindPanel(PanelSourceId);

        private static AwRibbonTab? FindAddInsTab() => ModifyTabAccess.FindTab(ModifyTabAccess.AddInsTabId);

        /// <summary>A button's Autodesk.Windows Id, the same as its command id:
        /// CustomCtrl_%CustomCtrl_%Add-Ins%Topo Edit% and its name. A stack adds no level.</summary>
        private static string ItemIdOf(string buttonName) => ModifyTabAccess.AddInsButtonId(PanelName, buttonName);

        /// <summary>
        /// Gives the Height box its "H=" label at its left, and makes it keep text typed in it
        /// when it loses focus (a click on Pick Lines, say), so Pick reads it without Enter. Through
        /// the unsupported Autodesk.Windows ribbon, so a failure is logged in one line and the box
        /// works as the API makes it.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void LabelHeightBox()
        {
            var item = FindAddInsTab()?.FindItem(ItemIdOf(HeightName), out _);
            if (item is not AwRibbonTextBox box)
            {
                Application.LogDiagnostic(
                    $"Topo Edit: the Height box keeps the API's look: no text box \"{ItemIdOf(HeightName)}\" on the Add-Ins tab.");
                return;
            }
            SetAcceptOnLostFocus(box);
            SetLabel(box, "H=");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void SetAcceptOnLostFocus(AwRibbonTextBox box) => box.AcceptTextOnLostFocus = true;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void SetLabel(AwRibbonTextBox box, string label)
        {
            box.Text = label;
            box.ShowText = true;
        }

        /// <summary>
        /// Makes ✗ and ✓ icons only, as Revit's own Mode panel has them: Large with the text hidden,
        /// still horizontal (never vertical), so the two rows show the 32 px icons in the height of
        /// one large button. Then, in a try of its own, gives the folded panel the Topo Tools icon
        /// instead of its first one, the ✗. Both go through the unsupported Autodesk.Windows
        /// ribbon, so this never throws: nothing is changed unless the panel is laid out as the API
        /// builds it, and a failure puts the text back and logs one line. A failure of the folded
        /// icon logs its own line and leaves the stack's look alone. It reaches no Autodesk.Windows
        /// member itself: each goes through a NoInlining method called inside one of its tries.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void MakeStackIconsOnly()
        {
            var changed = new List<AwRibbonItem>();
            try
            {
                var mismatch = FindStack(out var cancel, out var accept);
                if (mismatch != null)
                {
                    Application.LogDiagnostic($"Topo Edit: Cancel and Accept keep their text: {mismatch}.");
                }
                else
                {
                    foreach (var button in new[] { cancel!, accept! })
                    {
                        changed.Add(button); // first: a setter that throws may still have stored its value
                        SetSize(button, AwRibbonItemSize.Large);
                        SetShowText(button, false);
                    }
                }
            }
            catch (Exception ex)
            {
                foreach (var button in changed) PutTextBack(button);
                Application.LogDiagnostic(
                    $"Topo Edit: Cancel and Accept keep their text: {ex.GetType().FullName}: {ex.Message}");
            }

            try
            {
                var panel = FindOnAddIns();
                if (panel != null)
                    SetFoldedIcon(panel);
                else
                    Application.LogDiagnostic(
                        $"Topo Edit: the folded panel keeps its first icon: no panel \"{PanelSourceId}\" on the Add-Ins tab.");
            }
            catch (Exception ex)
            {
                Application.LogDiagnostic(
                    $"Topo Edit: the folded panel keeps its first icon: {ex.GetType().FullName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Finds ✗ and ✓ on the Add-Ins tab by Id and checks that their panel is laid out as the API
        /// builds it: its source Id is ours, and its first item is the stack [Cancel, row break,
        /// Accept], both with a large image. Null when all of that holds; otherwise what does not,
        /// with both buttons null.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string? FindStack(out AwRibbonItem? cancel, out AwRibbonItem? accept)
        {
            cancel = accept = null;
            var tab = FindAddInsTab();
            if (tab == null) return $"no tab with Id \"{ModifyTabAccess.AddInsTabId}\"";

            // RibbonTab.FindItem walks every panel, hidden ones too, and goes into stacks (its
            // enumerator descends into RibbonRowPanel): read in AdWindows 2024, 2026 and 2027.
            var foundCancel = tab.FindItem(ItemIdOf(CancelName), out var cancelPanel);
            var foundAccept = tab.FindItem(ItemIdOf(AcceptName), out var acceptPanel);
            if (foundCancel == null || foundAccept == null) return "a button is not on the Add-Ins tab";
            if (!ReferenceEquals(cancelPanel, acceptPanel) || cancelPanel.Source?.Id != PanelSourceId)
                return $"the buttons are not both on the panel \"{PanelSourceId}\"";

            var items = cancelPanel.Source.Items;
            var rows = items.Count > 0 && items[0] is AwRibbonRowPanel stack ? stack.Items : null;
            if (rows == null || rows.Count != 3
                || !ReferenceEquals(rows[0], foundCancel)
                || rows[1] is not AwRibbonRowBreak
                || !ReferenceEquals(rows[2], foundAccept))
                return "the panel does not start with the stack [Cancel, row break, Accept]";

            if (foundCancel.LargeImage == null || foundAccept.LargeImage == null)
                return "a button has no large image";

            cancel = foundCancel;
            accept = foundAccept;
            return null;
        }

        /// <summary>The API's look back on one button: small, with its text. Best effort, and never
        /// throws: the caller logs the failure that called for it.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void PutTextBack(AwRibbonItem button)
        {
            try { SetSize(button, AwRibbonItemSize.Standard); }
            catch { /* the caller logs the failure */ }
            try { SetShowText(button, true); }
            catch { /* the caller logs the failure */ }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void SetSize(AwRibbonItem button, AwRibbonItemSize size) => button.Size = size;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void SetShowText(AwRibbonItem button, bool showText) => button.ShowText = showText;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void SetFoldedIcon(AwRibbonPanel panel) => panel.CollapsedPanelImage = TopoIcons.TopoTools(32);

        private static bool IsDarkTheme() => UIThemeManager.CurrentTheme == UITheme.Dark;

        /// <summary>Revit's theme changed: ✗ and ✓ take the colours of the theme now current.</summary>
        private static void OnThemeChanged(object? sender, ThemeChangedEventArgs e)
        {
            try
            {
                if (_cancelButton == null || _acceptButton == null) return;
                bool dark = IsDarkTheme();
                _cancelButton.Image = TopoIcons.Cancel(16, dark);
                _cancelButton.LargeImage = TopoIcons.Cancel(32, dark);
                _acceptButton.Image = TopoIcons.Accept(16, dark);
                _acceptButton.LargeImage = TopoIcons.Accept(32, dark);
            }
            catch (Exception ex)
            {
                Application.LogDiagnostic($"Topo Edit icons for the new theme failed: {ex}");
            }
        }

        private static void ClearHeightBox()
        {
            try
            {
                _lastSelection = new List<ElementId>();
                ShowInBox("");
            }
            catch (Exception ex)
            {
                Application.LogDiagnostic($"Topo Edit height box failed: {ex}");
            }
        }

        /// <summary>Puts <paramref name="value"/> in the Height box, whatever it holds.</summary>
        private static void ShowInBox(string value)
        {
            if (_heightBox == null) return;
            _heightBox.Value = value;
            _lastShown = value;
        }

        /// <summary>
        /// Puts <paramref name="value"/> in the Height box unless it holds something typed and not
        /// yet applied: text other than what the box last showed. So "+0.1" typed before selecting
        /// lines, or before Pick, is still there when Enter or Pick reads it. Clearing the box hands
        /// it back to the selection.
        /// </summary>
        private static void FollowInBox(string value)
        {
            if (_heightBox == null) return;
            if (!HeightBoxText.Follows(Convert.ToString(_heightBox.Value, CultureInfo.InvariantCulture), _lastShown)) return;
            ShowInBox(value);
        }

        /// <summary>The selection changed: the Height box shows the selected topo lines' height.</summary>
        private static void OnSelectionForHeight(object? sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (_heightBox == null) return;
                var doc = e.GetDocument();
                _lastSelection = e.GetSelectedElements()?.ToList() ?? new List<ElementId>();
                FollowInBox(doc == null ? "" : ShownHeight(doc, _lastSelection));
            }
            catch (Exception ex)
            {
                Application.LogDiagnostic($"Topo Edit height box failed: {ex}");
            }
        }

        /// <summary>
        /// The document changed (an Undo, a label edited by hand, a line deleted): during a session,
        /// the Height box shows the last selection's height again, so it is never left stale.
        /// </summary>
        // ponytail: re-reads the view's labels on every change in the session's document; cache per session if it lags on big sites.
        private static void OnDocumentChangedForHeight(object? sender, DocumentChangedEventArgs e)
        {
            try
            {
                if (_heightBox == null || TopoSession.Current == null) return;
                var doc = e.GetDocument();
                if (doc == null || !TopoSession.Current.IsFor(doc)) return;
                FollowInBox(ShownHeight(doc, _lastSelection));
            }
            catch (Exception ex)
            {
                Application.LogDiagnostic($"Topo Edit height box failed: {ex}");
            }
        }

        /// <summary>What the Height box shows for <paramref name="selected"/>: empty outside the
        /// session's document or when no topo line of its view is selected.</summary>
        private static string ShownHeight(Document doc, ICollection<ElementId> selected)
        {
            if (!TrySessionView(doc, out var view)) return "";
            var notes = HeightTexts.ReadNotes(doc, view, out var topoLines);
            var lineIds = SelectedLineIds(selected, notes, topoLines);
            if (lineIds.Count == 0) return "";
            return HeightBoxText.For(lineIds.Select(id => notes.TryGetValue(id, out var note) ? note.Text : null));
        }

        private static bool TrySessionView(Document doc, out View view)
        {
            view = null!;
            var session = TopoSession.Current;
            if (session == null || !session.IsFor(doc)) return false;
            if (doc.GetElement(session.ViewId) is not View found) return false;
            view = found;
            return true;
        }

        /// <summary>
        /// The topo lines (<paramref name="topoLines"/>) that are selected, each once: a line selected
        /// itself, or through its height text. A text goes to the line <paramref name="notes"/> (the
        /// linker's plan) gives it, not to its stored link: a copied text still names the original
        /// line until Height Labels or Update relinks it.
        /// </summary>
        private static List<long> SelectedLineIds(ICollection<ElementId>? selected,
            IReadOnlyDictionary<long, TextNote> notes, ISet<long> topoLines)
        {
            var result = new List<long>();
            if (selected == null || selected.Count == 0) return result;

            var lineByText = notes.ToDictionary(pair => pair.Value.Id.Value, pair => pair.Key);
            foreach (var id in selected)
            {
                long lineId;
                if (topoLines.Contains(id.Value)) lineId = id.Value;
                else if (!lineByText.TryGetValue(id.Value, out lineId)) continue;

                if (!result.Contains(lineId)) result.Add(lineId);
            }
            return result;
        }

        /// <summary>Why <see cref="ApplyHeight"/> left a line alone, for a message.</summary>
        internal static readonly string LeftAloneReason =
            "its label does not read as a height, or the move would take it past " +
            $"{SurveyHeightText.LowestMetres:0} to {SurveyHeightText.HighestMetres:0} m";

        /// <summary>
        /// What the Height box holds, read as <see cref="HeightBoxInput"/>; false when there is no
        /// box or its text does not read.
        /// </summary>
        internal static bool TryReadInput(out HeightBoxInput input)
        {
            input = null!;
            return _heightBox != null
                && HeightBoxInput.TryParse(Convert.ToString(_heightBox.Value, CultureInfo.InvariantCulture), out input);
        }

        /// <summary>
        /// The topo lines of <paramref name="view"/> that <paramref name="ids"/> name, each once
        /// (a line or its height text), with the view's height texts by line.
        /// </summary>
        internal static List<long> LinesOf(Document doc, View view, ICollection<ElementId> ids,
            out IReadOnlyDictionary<long, TextNote> notes)
        {
            notes = HeightTexts.ReadNotes(doc, view, out var topoLines);
            return SelectedLineIds(ids, notes, topoLines);
        }

        /// <summary>
        /// Gives <paramref name="lineIds"/> what <paramref name="input"/> says, in one transaction
        /// "Topo height": each line's label is rewritten, and a line without one gets one when a
        /// height was typed. A line the input cannot change is left alone and counted in
        /// <paramref name="leftAlone"/>. False when Revit did not commit (worksharing, a failure it
        /// rolled back): then no label changed. A typed height becomes where the next drawn line
        /// starts. (The session updater also notes every label it sees change, so after a move
        /// the next line starts at a moved line's height, as after editing labels by hand.)
        /// </summary>
        internal static bool ApplyHeight(Document doc, View view, TopoSession session, HeightBoxInput input,
            IReadOnlyCollection<long> lineIds, IReadOnlyDictionary<long, TextNote> notes, out int leftAlone)
        {
            leftAlone = 0;
            using var tx = new Transaction(doc, "Topo height");
            tx.Start();
            foreach (var lineId in lineIds)
            {
                notes.TryGetValue(lineId, out var note);
                var text = input.NewLabel(note?.Text);
                if (text == null)
                {
                    leftAlone++;
                    continue;
                }

                if (note != null)
                {
                    if ((note.Text ?? "").Trim() != text) note.Text = text;
                }
                else if (doc.GetElement(new ElementId(lineId)) is CurveElement line
                         && SurveyHeightText.TryParse(text, out double feet))
                {
                    var created = HeightTexts.CreateFor(doc, view, line, feet);
                    session.Created.Add(created.Id);
                    session.LastSeenTexts[created.Id] = (created.Text ?? "").Trim();
                }
            }
            if (tx.Commit() != TransactionStatus.Committed) return false;

            if (input.IsAbsolute && SurveyHeightText.TryParse(input.NewLabel(null), out double typedFeet))
                LastTypedHeights.Set(doc, typedFeet);
            return true;
        }

        /// <summary>
        /// Enter in the Height box: every selected topo line's height text gets the typed height
        /// (<see cref="HeightBoxInput"/>: a height, or a move such as "+0.5" from each line's own),
        /// in one transaction; a line without one gets a new one when a height was typed. A move
        /// leaves alone the lines it cannot move (no readable label, a result out of range) and
        /// says how many. Points move on Update, as with a text edited by hand. What was typed stays
        /// in the box, for Pick or the next Enter; with no topo line selected, Enter only keeps it
        /// there. Input that does not read changes nothing and says so.
        /// </summary>
        private static void OnHeightEntered(object? sender, TextBoxEnterPressedEventArgs e)
        {
            try
            {
                if (_heightBox == null) return;
                var uiDoc = e.Application?.ActiveUIDocument;
                if (uiDoc == null) return;
                var doc = uiDoc.Document;
                var selected = uiDoc.Selection.GetElementIds();

                // What was typed stays in the box whatever happens here, for Pick or the next Enter.
                var typed = (Convert.ToString(_heightBox.Value, CultureInfo.InvariantCulture) ?? "").Trim();
                var session = TopoSession.Current;
                if (session == null || typed.Length == 0 || !TrySessionView(doc, out var view)) return;
                if (!HeightBoxInput.TryParse(typed, out var input))
                {
                    TaskDialog.Show("RVTuk – Topo Tools",
                        $"\"{typed}\" is not a height. Type a height (43), a move (+0.1, -0.1), " +
                        "or = before a height below zero (=-3.20).");
                    return;
                }

                // No topo line selected: the height just waits in the box, for Pick.
                var notes = HeightTexts.ReadNotes(doc, view, out var topoLines);
                var lineIds = SelectedLineIds(selected, notes, topoLines);
                if (lineIds.Count == 0) return;

                ApplyHeight(doc, view, session, input, lineIds, notes, out int leftAlone);

                if (leftAlone > 0)
                    TaskDialog.Show("RVTuk – Topo Tools",
                        (leftAlone == 1 ? "1 line keeps its height" : $"{leftAlone} lines keep their height") +
                        ": " + LeftAloneReason + ".");
            }
            catch (Exception ex)
            {
                Application.LogDiagnostic($"Topo Edit height failed: {ex}");
                TaskDialog.Show("RVTuk – Topo Tools", $"The height could not be set: {ex.Message}");
            }
        }

        private static PushButtonData Button<TCommand>(string name, string text, string assemblyPath, string toolTip,
            Func<int, BitmapSource> icon) =>
            new PushButtonData(name, text, assemblyPath, typeof(TCommand).FullName!)
            {
                ToolTip = toolTip,
                LargeImage = icon(32),
                Image = icon(16),
            };
    }
}

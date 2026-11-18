using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using RVTuk.Core.Shared.Config;
using RVTuk.Revit.TopoTools.Commands;
using AwRibbonItem = Autodesk.Windows.RibbonItem;
using AwRibbonItemSize = Autodesk.Windows.RibbonItemSize;
using AwRibbonPanel = Autodesk.Windows.RibbonPanel;
using AwRibbonRowBreak = Autodesk.Windows.RibbonRowBreak;
using AwRibbonRowPanel = Autodesk.Windows.RibbonRowPanel;
using AwRibbonTab = Autodesk.Windows.RibbonTab;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// The temporary "Topo Edit" ribbon panel: created hidden at startup, right after the RVTuk
    /// panel (the ribbon API only appends), shown while a Topo Edit session is open. It hides
    /// itself on <see cref="TopoSession.Ended"/>, so every way a session ends — Accept, Cancel,
    /// the document closing, switching document — takes the panel with it.
    ///
    /// Cancel (✗) above Accept (✓) at its left end, as Revit's own Mode panel has them, then Draw
    /// Lines, Height Labels, Update and Spacing. The API makes the two as small buttons that show
    /// their text; the first <see cref="Show"/> makes them icons only through the unsupported
    /// Autodesk.Windows ribbon, and gives the folded panel the Topo Tools icon. If that fails, the
    /// API's look stays and the panel works the same. As in <see cref="ModifyTabAccess"/>, each
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

        private const double CentimetresPerFoot = 30.48;

        private static RibbonPanel? _panel;
        private static PushButton? _cancelButton;
        private static PushButton? _acceptButton;
        private static TextBox? _spacingBox;
        private static double _spacingCentimetres = 100;

        /// <summary>Set by the first <see cref="Show"/>: the icons-only look is tried once per
        /// Revit session, whatever comes of it.</summary>
        private static bool _iconsOnlyTried;

        /// <summary>
        /// The point spacing set in the panel, in Revit's internal feet. Revit's TextBox has no
        /// focus-lost event, so a value typed without Enter is read here, when Update or Accept
        /// asks: a valid new value is kept (and saved) as Enter would; an invalid one is replaced
        /// in the box by the stored value.
        /// </summary>
        public static double SpacingFeet
        {
            get
            {
                try
                {
                    if (TryReadBox(out double centimetres) && centimetres != _spacingCentimetres)
                        Adopt(centimetres);
                }
                catch (Exception ex)
                {
                    Application.LogDiagnostic($"Topo Edit spacing failed: {ex}");
                }
                return _spacingCentimetres / CentimetresPerFoot;
            }
        }

        public static void Create(UIControlledApplication app, string assemblyPath)
        {
            _spacingCentimetres = ValidOrDefault(ConfigManager.LoadConfig().TopoPointSpacingCentimetres);

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

            panel.AddItem(Button<DrawTopoLinesCommand>("TopoDrawLines", "Draw\nLines", assemblyPath,
                "Start Revit's Detail Line tool in the session's view, opening it first if needed: every line drawn there becomes a topo line with a height text",
                TopoIcons.DrawLines));
            panel.AddItem(Button<HeightLabelsCommand>("TopoHeightLabels", "Height\nLabels", assemblyPath,
                "Give every topo line in the session's view a height text at its middle, and re-centre the existing ones",
                TopoIcons.HeightLabels));
            panel.AddItem(Button<TopoUpdateCommand>("TopoUpdate", "Update", assemblyPath,
                "Apply the topo lines and their heights to the toposolid or floor being edited, and stay in the session",
                TopoIcons.Update));

            var spacing = (TextBox)panel.AddItem(new TextBoxData("TopoSpacing")
            {
                ToolTip = "Distance between points along each topo line, in centimetres. " +
                          "Press Enter, or Update or Accept, to keep it.",
            });
            spacing.PromptText = "Spacing (cm)";
            spacing.ShowImageAsButton = false;
            spacing.SelectTextOnFocus = true;
            spacing.Width = 80;
            spacing.Value = Format(_spacingCentimetres);
            spacing.EnterPressed += OnSpacingEntered;

            panel.Visible = false;

            _panel = panel;
            _spacingBox = spacing;
            TopoSession.Ended += Hide;
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

        private static void OnSpacingEntered(object? sender, TextBoxEnterPressedEventArgs e)
        {
            try
            {
                if (TryReadBox(out double centimetres)) Adopt(centimetres);
            }
            catch (Exception ex)
            {
                Application.LogDiagnostic($"Topo Edit spacing failed: {ex}");
            }
        }

        /// <summary>
        /// Reads the spacing box. False when there is no box or its text is not a positive number;
        /// in the second case the stored value is put back in the box.
        /// </summary>
        private static bool TryReadBox(out double centimetres)
        {
            centimetres = 0;
            if (_spacingBox == null) return false;

            var typed = Convert.ToString(_spacingBox.Value, CultureInfo.InvariantCulture)?.Trim() ?? "";
            if (TryParsePositive(typed, out centimetres)) return true;

            _spacingBox.Value = Format(_spacingCentimetres);
            return false;
        }

        /// <summary>Makes <paramref name="centimetres"/> (already rounded) the spacing, shows it in
        /// the box, and saves it to the config.</summary>
        private static void Adopt(double centimetres)
        {
            _spacingCentimetres = centimetres;
            if (_spacingBox != null) _spacingBox.Value = Format(centimetres);

            var config = ConfigManager.LoadConfig();
            config.TopoPointSpacingCentimetres = centimetres;
            ConfigManager.SaveConfig(config);
        }

        /// <summary>Parses a typed spacing, rounded to what <see cref="Format"/> shows, so the box
        /// read back by <see cref="SpacingFeet"/> never differs from the stored value.</summary>
        private static bool TryParsePositive(string text, out double value)
        {
            // Either decimal separator: the office types both.
            var normalised = text.Replace(',', '.');
            if (!double.TryParse(normalised, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return false;
            value = Round(value);
            return value > 0 && !double.IsInfinity(value);
        }

        /// <summary>The configured spacing, rounded as the box shows it; 100 when it is not usable.</summary>
        private static double ValidOrDefault(double centimetres)
        {
            centimetres = Round(centimetres);
            return centimetres > 0 && !double.IsNaN(centimetres) && !double.IsInfinity(centimetres) ? centimetres : 100;
        }

        /// <summary>Rounds to the two decimals <see cref="Format"/> shows.</summary>
        private static double Round(double centimetres) => Math.Round(centimetres, 2, MidpointRounding.AwayFromZero);

        private static string Format(double centimetres) =>
            centimetres.ToString("0.##", CultureInfo.InvariantCulture);

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

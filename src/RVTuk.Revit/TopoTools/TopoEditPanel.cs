using System;
using System.Globalization;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using RVTuk.Core.Shared.Config;
using RVTuk.Revit.TopoTools.Commands;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// The temporary "Topo Edit" ribbon panel: created hidden at startup, right after the RVTuk
    /// panel (the ribbon API only appends), shown while a Topo Edit session is open. It hides
    /// itself on <see cref="TopoSession.Ended"/>, so every way a session ends — Accept, Cancel,
    /// the document closing, switching document — takes the panel with it.
    /// </summary>
    public static class TopoEditPanel
    {
        public const string PanelName = "Topo Edit";

        private const double CentimetresPerFoot = 30.48;

        private static RibbonPanel? _panel;
        private static TextBox? _spacingBox;
        private static double _spacingCentimetres = 100;

        /// <summary>The point spacing set in the panel, in Revit's internal feet.</summary>
        public static double SpacingFeet => _spacingCentimetres / CentimetresPerFoot;

        public static void Create(UIControlledApplication app, string assemblyPath)
        {
            _spacingCentimetres = ValidOrDefault(ConfigManager.LoadConfig().TopoPointSpacingCentimetres);

            var panel = app.CreateRibbonPanel(PanelName);

            panel.AddItem(Button<DrawTopoLinesCommand>("TopoDrawLines", "Draw\nLines", assemblyPath,
                "Start Revit's Detail Line tool: every line drawn in this view becomes a topo line with a height text"));
            panel.AddItem(Button<HeightLabelsCommand>("TopoHeightLabels", "Height\nLabels", assemblyPath,
                "Give every topo line in the view a height text at its middle, and re-centre the existing ones"));
            panel.AddItem(Button<TopoUpdateCommand>("TopoUpdate", "Update", assemblyPath,
                "Apply the topo lines and their heights to the selected element, and stay in the session"));
            panel.AddItem(Button<TopoAcceptCommand>("TopoAccept", "Accept", assemblyPath,
                "Apply the topo lines and close the session; lines and heights stay in the view"));
            panel.AddItem(Button<TopoCancelCommand>("TopoCancel", "Cancel", assemblyPath,
                "Close the session: delete the lines and texts it made and put changed heights back"));

            var spacing = (TextBox)panel.AddItem(new TextBoxData("TopoSpacing")
            {
                ToolTip = "Distance between points along each topo line, in centimetres. Press Enter to keep it.",
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
        }

        public static void Show()
        {
            if (_panel != null) _panel.Visible = true;
        }

        public static void Hide()
        {
            if (_panel != null) _panel.Visible = false;
        }

        private static void OnSpacingEntered(object? sender, TextBoxEnterPressedEventArgs e)
        {
            try
            {
                if (_spacingBox == null) return;

                var typed = Convert.ToString(_spacingBox.Value, CultureInfo.InvariantCulture)?.Trim() ?? "";
                if (!TryParsePositive(typed, out double centimetres))
                {
                    _spacingBox.Value = Format(_spacingCentimetres);
                    return;
                }

                _spacingCentimetres = centimetres;
                _spacingBox.Value = Format(centimetres);

                var config = ConfigManager.LoadConfig();
                config.TopoPointSpacingCentimetres = centimetres;
                ConfigManager.SaveConfig(config);
            }
            catch (Exception ex)
            {
                Application.LogDiagnostic($"Topo Edit spacing failed: {ex}");
            }
        }

        private static bool TryParsePositive(string text, out double value)
        {
            // Either decimal separator: the office types both.
            var normalised = text.Replace(',', '.');
            return double.TryParse(normalised, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && value > 0 && !double.IsInfinity(value);
        }

        private static double ValidOrDefault(double centimetres) =>
            centimetres > 0 && !double.IsNaN(centimetres) && !double.IsInfinity(centimetres) ? centimetres : 100;

        private static string Format(double centimetres) =>
            centimetres.ToString("0.##", CultureInfo.InvariantCulture);

        private static PushButtonData Button<TCommand>(string name, string text, string assemblyPath, string toolTip) =>
            new PushButtonData(name, text, assemblyPath, typeof(TCommand).FullName!) { ToolTip = toolTip };
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RVTuk.Core.Shared.Config;
using RVTuk.UI.RoundPositions.ViewModels;
using RVTuk.UI.RoundPositions.Views;

namespace RVTuk.Revit.RoundPositions.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class RoundPositionsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            // Revit never creates a WPF Application; make one we own (never auto-shutdown).
            if (System.Windows.Application.Current == null)
            {
                new System.Windows.Application
                {
                    ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown
                };
            }

            var uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null) return Result.Cancelled;
            var doc = uidoc.Document;

            try
            {
                var config = ConfigManager.LoadConfig();
                var viewModel = new RoundPositionsViewModel
                {
                    // Stored inverted: the config default (false) is "half centimetres allowed".
                    AllowHalfCentimetres = !config.RoundPositionsWholeCentimetresOnly,
                };

                var dialog = new RoundPositionsWindow(viewModel);
                new System.Windows.Interop.WindowInteropHelper(dialog)
                {
                    Owner = commandData.Application.MainWindowHandle,
                };
                if (dialog.ShowDialog() != true) return Result.Cancelled;

                config.RoundPositionsWholeCentimetresOnly = !viewModel.AllowHalfCentimetres;
                ConfigManager.SaveConfig(config);

                var report = RoundPositionsRunner.Run(doc, viewModel.AllowHalfCentimetres);

                Action<IReadOnlyList<long>> selectInModel = ids =>
                    uidoc.Selection.SetElementIds(ids.Select(id => new ElementId(id)).ToList());

                var resultWindow = new RoundPositionsResultWindow(
                    new RoundPositionsResultViewModel(report, selectInModel));
                new System.Windows.Interop.WindowInteropHelper(resultWindow)
                {
                    Owner = commandData.Application.MainWindowHandle,
                };
                resultWindow.ShowDialog();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                // A rolled-back run reaches here with a message written for the architect, and no
                // report window follows it — the model is unchanged, so there is nothing to report.
                // `message` stays empty on purpose: Revit shows it in a second dialog of its own.
                TaskDialog.Show("RVTuk — Round Positions", $"The run failed:\n\n{ex.Message}");
                return Result.Failed;
            }
        }
    }
}

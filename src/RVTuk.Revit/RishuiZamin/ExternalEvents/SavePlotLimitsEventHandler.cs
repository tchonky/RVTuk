using System;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RVTuk.Core.RishuiZamin;
using RVTuk.Revit.RishuiZamin;

namespace RVTuk.Revit.RishuiZamin.ExternalEvents
{
    /// <summary>
    /// Writes the Table tab's plot area and limits with <see cref="PlotLimitsStore.Write"/> on
    /// Revit's main thread, as one undo step. The background thread calls <see cref="Prepare"/>,
    /// raises the event and blocks on <see cref="WaitForCompletion"/>, like
    /// <see cref="SetUsageKeyEventHandler"/>.
    /// </summary>
    public class SavePlotLimitsEventHandler : IExternalEventHandler
    {
        public const string TransactionName = "Area Calc — plot area and limits";

        private readonly ManualResetEventSlim _done = new(false);
        private PlotLimits _limits = PlotLimits.Empty;
        private Document? _readFrom;

        public (bool ok, string msg) Result { get; private set; } = (false, "Not run.");

        /// <param name="readFrom">The document the window last read
        /// (<see cref="AreaExtractEventHandler.Document"/>).</param>
        public void Prepare(PlotLimits limits, Document? readFrom)
        {
            _limits = limits;
            _readFrom = readFrom;
            Result = (false, "Not run.");
            _done.Reset();
        }

        public void WaitForCompletion() => _done.Wait();

        public void Execute(UIApplication app)
        {
            try
            {
                Result = Save(app.ActiveUIDocument?.Document);
            }
            catch (Exception ex)
            {
                Result = (false, ex.Message);
            }
            finally
            {
                _done.Set();
            }
        }

        private (bool ok, string msg) Save(Document? doc)
        {
            if (doc == null || _readFrom == null || !doc.Equals(_readFrom))
                return (false, "The active project is not the one the window last read. Click Refresh and try again.");
            if (doc.IsReadOnly)
                return (false, "The project is read-only.");

            var info = doc.ProjectInformation;
            if (doc.IsWorkshared &&
                WorksharingUtils.GetCheckoutStatus(doc, info.Id) == CheckoutStatus.OwnedByOtherUser)
            {
                var owner = WorksharingUtils.GetWorksharingTooltipInfo(doc, info.Id).Owner;
                return (false, "Project Information is being edited by " + owner + ". Ask them to sync, then try again.");
            }

            using var t = new Transaction(doc, TransactionName);
            t.Start();
            PlotLimitsStore.Write(doc, _limits);
            return t.Commit() == TransactionStatus.Committed
                ? (true, "")
                : (false, "Revit did not accept the change.");
        }

        public string GetName() => "RVTuk.SavePlotLimits";
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using Autodesk.Revit.UI;
using RVTuk.Core.RishuiZamin;
using RVTuk.Revit.RishuiZamin;

namespace RVTuk.Revit.RishuiZamin.ExternalEvents
{
    /// <summary>
    /// Runs <see cref="UsageKeyAssigner.Assign"/> on Revit's main thread. The background thread
    /// calls <see cref="Prepare"/>, raises the event and blocks on <see cref="WaitForCompletion"/>,
    /// mirroring <see cref="SetupUsageKeysEventHandler"/>.
    /// </summary>
    public class SetUsageKeyEventHandler : IExternalEventHandler
    {
        private readonly ManualResetEventSlim _done = new(false);

        private UsageKeySlot _slot;
        private int? _code;
        private IReadOnlyList<long> _areaIds = Array.Empty<long>();
        private bool _runSetupFirst;
        private Autodesk.Revit.DB.Document? _readFrom;

        public UsageAssignResult Result { get; private set; } =
            new UsageAssignResult(UsageAssignStatus.Failed, "Not run.");

        /// <param name="readFrom">The document the areas were read from
        /// (<see cref="AreaExtractEventHandler.Document"/>).</param>
        public void Prepare(UsageKeySlot slot, int? code, IReadOnlyList<long> areaIds, bool runSetupFirst,
            Autodesk.Revit.DB.Document? readFrom)
        {
            _readFrom = readFrom;
            _slot = slot;
            _code = code;
            _areaIds = areaIds;
            _runSetupFirst = runSetupFirst;
            Result = new UsageAssignResult(UsageAssignStatus.Failed, "Not run.");
            _done.Reset();
        }

        public void WaitForCompletion() => _done.Wait();

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument?.Document;
                Result = doc == null
                    ? new UsageAssignResult(UsageAssignStatus.Failed, "No active document.")
                    : UsageKeyAssigner.Assign(doc, _slot, _code, _areaIds, _runSetupFirst, _readFrom);
            }
            catch (Exception ex)
            {
                Result = new UsageAssignResult(UsageAssignStatus.Failed, "Setting the usage failed: " + ex.Message);
            }
            finally
            {
                _done.Set();
            }
        }

        public string GetName() => "RVTuk.SetUsageKey";
    }
}

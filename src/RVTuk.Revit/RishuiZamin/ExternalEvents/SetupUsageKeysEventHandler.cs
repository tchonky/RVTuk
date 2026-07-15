using System;
using System.Threading;
using Autodesk.Revit.UI;
using RVTuk.Revit.RishuiZamin;

namespace RVTuk.Revit.RishuiZamin.ExternalEvents
{
    /// <summary>
    /// Runs <see cref="UsageKeyScheduleBuilder.EnsureUsageKeySchedules"/> against the active
    /// document on Revit's main thread (it opens a transaction). The background thread blocks
    /// on <see cref="WaitForCompletion"/>, mirroring <see cref="AreaExtractEventHandler"/>.
    /// </summary>
    public class SetupUsageKeysEventHandler : IExternalEventHandler
    {
        private readonly ManualResetEventSlim _done = new(false);

        public (bool ok, string message) Result { get; private set; }

        public void Reset()
        {
            Result = default;
            _done.Reset();
        }

        public void WaitForCompletion() => _done.Wait();

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument?.Document;
                Result = doc == null
                    ? (false, "No active document.")
                    : UsageKeyScheduleBuilder.EnsureUsageKeySchedules(doc);
            }
            catch (Exception ex)
            {
                Result = (false, "Usage key schedule setup failed: " + ex.Message);
            }
            finally
            {
                _done.Set();
            }
        }

        public string GetName() => "RVTuk.SetupUsageKeys";
    }
}

using System;
using System.Threading;
using Autodesk.Revit.UI;
using RVTuk.Core.AutoDimensions;

namespace RVTuk.Revit.AutoDimensions.ExternalEvents
{
    /// <summary>
    /// Marshals a scope discovery pass onto Revit's main thread for the pane, which triggers it
    /// from a background thread (raise + WaitForCompletion would deadlock on the UI thread).
    /// Follows the GetProjectFamiliesEventHandler ping-pong pattern.
    /// </summary>
    public class LevelDiscoveryEventHandler : IExternalEventHandler
    {
        private static readonly AutoDimensionsScope Empty =
            new AutoDimensionsScope(Array.Empty<LevelScope>(), null);

        private readonly ManualResetEventSlim _done = new(false);

        public AutoDimensionsScope Result { get; private set; } = Empty;

        public void Reset() => _done.Reset();
        public void WaitForCompletion() => _done.Wait();

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null) { Result = Empty; return; }

                Result = new AutoDimensionsScope(
                    LevelScopeFinder.Find(doc),
                    ScopeSelectionStore.Read(doc));
            }
            catch
            {
                Result = Empty;
            }
            finally
            {
                _done.Set();
            }
        }

        public string GetName() => "RVTuk.AutoDimensionsLevelDiscoveryEventHandler";
    }
}

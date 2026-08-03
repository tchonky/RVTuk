using System;
using System.Threading;
using Autodesk.Revit.UI;
using RVTuk.Core.AutoDimensions;
using RVTuk.Revit.Shared;

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
            new AutoDimensionsScope(
                Array.Empty<LevelScope>(), null, Array.Empty<DimensionTypeInfo>());

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

                // First run in this project creates the styles. Without this you could not draw
                // a reference line before pressing Create Dimensions once, which necessarily did
                // nothing — there was no style to have drawn on.
                LineStyleCreator.TryEnsureInOwnTransaction(doc, DimensionLineStyle.AllStyleNames);

                Result = new AutoDimensionsScope(
                    LevelScopeFinder.Find(doc),
                    ScopeSelectionStore.Read(doc),
                    DimensionTypeFinder.Find(doc));
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace ReviTchucky.Revit.ExternalEvents
{
    public class GetProjectFamiliesEventHandler : IExternalEventHandler
    {
        private readonly ManualResetEventSlim _done = new(false);

        public IReadOnlyList<string> Result { get; private set; } = Array.Empty<string>();

        public void Reset() => _done.Reset();
        public void WaitForCompletion() => _done.Wait();

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null) { Result = Array.Empty<string>(); return; }

                Result = new FilteredElementCollector(doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .Select(f => f.Name)
                    .ToList();
            }
            catch
            {
                Result = Array.Empty<string>();
            }
            finally
            {
                _done.Set();
            }
        }

        public string GetName() => "ReviTchucky.GetProjectFamiliesEventHandler";
    }
}

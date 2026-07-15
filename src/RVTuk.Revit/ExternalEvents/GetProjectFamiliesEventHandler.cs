using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RVTuk.Core.FamilyBrowser.Models;
using RVTuk.Core.FamilyBrowser.Util;

namespace RVTuk.Revit.ExternalEvents
{
    public class GetProjectFamiliesEventHandler : IExternalEventHandler
    {
        private readonly ManualResetEventSlim _done = new(false);

        public IReadOnlyList<ProjectFamilyInfo> Result { get; private set; } = Array.Empty<ProjectFamilyInfo>();

        public void Reset() => _done.Reset();
        public void WaitForCompletion() => _done.Wait();

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null) { Result = Array.Empty<ProjectFamilyInfo>(); return; }

                Result = new FilteredElementCollector(doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .Select(f => new ProjectFamilyInfo
                    {
                        Name = f.Name,
                        Version = ReadVersion(doc, f),
                        Category = f.FamilyCategory?.Name,
                    })
                    .ToList();
            }
            catch
            {
                Result = Array.Empty<ProjectFamilyInfo>();
            }
            finally
            {
                _done.Set();
            }
        }

        // The _Version shared parameter surfaces on the family's symbols (types) in the project.
        // Take the first symbol that has a value; null means the loaded copy doesn't carry the
        // parameter, which makes the browser skip the version check for this family.
        private static string? ReadVersion(Document doc, Family family)
        {
            try
            {
                foreach (var id in family.GetFamilySymbolIds())
                {
                    var p = doc.GetElement(id)?.LookupParameter(FamilyVersionCheck.ParameterName);
                    if (p == null || !p.HasValue) continue;

                    string? raw = p.StorageType switch
                    {
                        StorageType.String  => p.AsString(),
                        StorageType.Integer => p.AsInteger().ToString(System.Globalization.CultureInfo.InvariantCulture),
                        StorageType.Double  => p.AsDouble().ToString(System.Globalization.CultureInfo.InvariantCulture),
                        _                   => p.AsValueString(),
                    };
                    var version = FamilyVersionCheck.Normalize(raw);
                    if (version != null) return version;
                }
            }
            catch { /* a family with unreadable symbols simply has no version */ }
            return null;
        }

        public string GetName() => "RVTuk.GetProjectFamiliesEventHandler";
    }
}

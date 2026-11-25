using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RVTuk.Core.FamilyBrowser.Models;
using RVTuk.Core.FamilyBrowser.Util;

namespace RVTuk.Revit.FamilyBrowser.ExternalEvents
{
    public class GetProjectFamiliesEventHandler : IExternalEventHandler
    {
        private readonly RequestGate _gate = new();

        public ProjectSnapshot Result { get; private set; } = new();

        /// <summary>Set when there is no project to compare against (a family is active).</summary>
        public string? ErrorMessage { get; private set; }

        public void Reset() { ErrorMessage = null; _gate.Begin(); }
        public void WaitForCompletion() => _gate.Wait(RequestGate.StartTimeout);

        public void Execute(UIApplication app)
        {
            if (!_gate.TryStart()) return; // withdrawn: the caller already reported "Revit is busy"
            try
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null) { Result = new ProjectSnapshot(); return; }
                // A family document lists its nested families — comparing those against the
                // library would be wrong; say so instead of showing a misleading list.
                if (doc.IsFamilyDocument)
                {
                    Result = new ProjectSnapshot();
                    ErrorMessage = LoadFamilyEventHandler.FamilyDocumentActive;
                    return;
                }

                var infos = new List<ProjectFamilyInfo>();
                var unresolved = new Dictionary<ElementId, ProjectFamilyInfo>();
                foreach (Family f in new FilteredElementCollector(doc).OfClass(typeof(Family)))
                {
                    var info = new ProjectFamilyInfo
                    {
                        Name = f.Name,
                        Version = ReadVersionFromSymbols(doc, f),
                        Category = f.FamilyCategory?.Name,
                    };
                    // Which library file it was loaded from, if RVTuk noted it (FamilySourceStamp).
                    if (FamilySourceStamp.TryRead(f, out var size, out var modified))
                    {
                        info.LoadedFileSize = size;
                        info.LoadedFileModifiedUtc = modified;
                    }
                    if (info.Version == null) unresolved[f.Id] = info;
                    infos.Add(info);
                }
                ReadVersionsFromInstances(doc, unresolved);
                Result = new ProjectSnapshot { DocumentKey = ProjectKey.Of(doc), Title = doc.Title, Families = infos };
            }
            catch
            {
                Result = new ProjectSnapshot();
            }
            finally
            {
                _gate.Complete();
            }
        }

        // As a type parameter (the office standard), _Version surfaces on the family's symbols
        // in the project. Take the first symbol that has a value; null means the loaded copy
        // doesn't carry it there — the instance fallback below gets a chance next.
        private static string? ReadVersionFromSymbols(Document doc, Family family)
        {
            try
            {
                foreach (var id in family.GetFamilySymbolIds())
                {
                    var p = doc.GetElement(id)?.LookupParameter(FamilyVersionCheck.ParameterName);
                    if (p == null || !p.HasValue) continue;

                    var version = FamilyVersionCheck.Normalize(ReadRaw(p));
                    if (version != null) return version;
                }
            }
            catch { /* a family with unreadable symbols simply has no version */ }
            return null;
        }

        // Instance-parameter fallback: an instance _Version never surfaces on the symbols — the
        // value lives on each placed FamilyInstance. One pass over the project's instances
        // resolves every family the symbol read missed; the first instance encountered is taken
        // as authoritative (the office locks the value with a formula, so instances agree).
        // Families with no placed instance stay version-less — nothing in the project carries a
        // value for them. Finding the parameter here also means the loaded copy has it at
        // instance level, which the browser flags (VersionIsInstance) as off-standard.
        private static void ReadVersionsFromInstances(Document doc, Dictionary<ElementId, ProjectFamilyInfo> unresolved)
        {
            if (unresolved.Count == 0) return;
            try
            {
                foreach (FamilyInstance inst in new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)))
                {
                    var familyId = inst.Symbol?.Family?.Id;
                    if (familyId == null || !unresolved.TryGetValue(familyId, out var info)) continue;

                    var p = inst.LookupParameter(FamilyVersionCheck.ParameterName);
                    if (p != null)
                    {
                        info.VersionIsInstance = true;
                        if (p.HasValue) info.Version = FamilyVersionCheck.Normalize(ReadRaw(p));
                    }
                    // Resolved either way: no parameter on an instance means the family has no
                    // instance _Version at all — don't re-check its every other instance.
                    unresolved.Remove(familyId);
                    if (unresolved.Count == 0) return;
                }
            }
            catch { /* unreadable instances simply leave families version-less */ }
        }

        private static string? ReadRaw(Parameter p) => p.StorageType switch
        {
            StorageType.String  => p.AsString(),
            StorageType.Integer => p.AsInteger().ToString(System.Globalization.CultureInfo.InvariantCulture),
            StorageType.Double  => p.AsDouble().ToString(System.Globalization.CultureInfo.InvariantCulture),
            _                   => p.AsValueString(),
        };

        public string GetName() => "RVTuk.GetProjectFamiliesEventHandler";
    }
}

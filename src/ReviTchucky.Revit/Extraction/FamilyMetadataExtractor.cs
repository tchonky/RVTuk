using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using ReviTchucky.Core.Models;
using RevitApplication = Autodesk.Revit.ApplicationServices.Application;

namespace ReviTchucky.Revit.Extraction
{
    /// <summary>
    /// Must be called only from Revit's main thread (inside an ExternalEvent handler).
    /// Opens the family document to read FamilyManager parameters (group + kind), which the
    /// lightweight PartAtom XML cannot provide. Heavier than ExtractPartAtomFromFamilyFile,
    /// but only runs during the admin deep scan.
    /// </summary>
    public class FamilyMetadataExtractor
    {
        private readonly RevitApplication _app;

        public FamilyMetadataExtractor(RevitApplication app)
        {
            _app = app;
        }

        public (string? Category, IReadOnlyList<ParameterModel> Parameters) ExtractMetadata(string rfaPath)
        {
            // Revit's OpenDocumentFile throws / shows a modal "path too long" dialog for paths
            // over Windows MAX_PATH on .NET Framework. Such families are already skipped during the
            // scan; guard here too so extraction can never surface that dialog.
            if (string.IsNullOrEmpty(rfaPath) || rfaPath.Length >= 260)
                return (null, Array.Empty<ParameterModel>());

            Document? doc = null;
            try
            {
                doc = _app.OpenDocumentFile(rfaPath);
                if (doc == null || !doc.IsFamilyDocument)
                    return (null, Array.Empty<ParameterModel>());

                string? category = null;
                try { category = doc.OwnerFamily?.FamilyCategory?.Name; } catch { }

                var parameters = new List<ParameterModel>();
                foreach (FamilyParameter fp in doc.FamilyManager.Parameters)
                {
                    try { parameters.Add(ReadParameter(fp)); }
                    catch { /* skip a single unreadable parameter */ }
                }
                return (category, parameters);
            }
            catch
            {
                return (null, Array.Empty<ParameterModel>());
            }
            finally
            {
                try { doc?.Close(false); } catch { }
            }
        }

        private static ParameterModel ReadParameter(FamilyParameter fp)
        {
            var def = fp.Definition;

            string kind;
            string? guid = null;
            if (fp.IsShared)
            {
                kind = "Shared";
                try { guid = fp.GUID.ToString(); } catch { }
            }
            else if (def is InternalDefinition idef && idef.BuiltInParameter != BuiltInParameter.INVALID)
            {
                kind = "System";
            }
            else
            {
                kind = "Family";
            }

            string? group = null;
            try { group = LabelUtils.GetLabelForGroup(def.GetGroupTypeId()); }
            catch
            {
#if NET48
                // Revit 2023/2024 (net48): ParameterGroup is deprecated but present; use as fallback.
#pragma warning disable CS0618
                try { group = LabelUtils.GetLabelFor(def.ParameterGroup); } catch { }
#pragma warning restore CS0618
#endif
                // Revit 2025 (net8): ParameterGroup was removed; GetGroupTypeId() is the only path.
                // If that also threw, group stays null.
            }

            string dataType;
            try { dataType = LabelUtils.GetLabelForSpec(def.GetDataType()); }
            catch { dataType = fp.StorageType.ToString(); }

            string? formula = null;
            try { formula = fp.Formula; } catch { }

            return new ParameterModel
            {
                ParameterName = def.Name,
                DataType      = string.IsNullOrEmpty(dataType) ? "Unknown" : dataType,
                IsInstance    = fp.IsInstance,
                ParamGroup    = group,
                Kind          = kind,
                Guid          = guid,
                Formula       = formula
            };
        }
    }
}

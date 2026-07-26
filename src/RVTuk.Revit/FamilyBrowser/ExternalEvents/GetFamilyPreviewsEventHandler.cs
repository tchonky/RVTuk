using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RVTuk.Revit.FamilyBrowser.ExternalEvents
{
    /// <summary>
    /// Renders type-preview images for families loaded in the project. The browser's
    /// "model only" rows have no library .rfa to read an OLE thumbnail from, so their
    /// preview comes from the loaded family symbol instead (ElementType.GetPreviewImage).
    /// Called with just the model-only names — preview rendering is not free, and a
    /// project can hold hundreds of loaded families that don't need one.
    /// </summary>
    public class GetFamilyPreviewsEventHandler : IExternalEventHandler
    {
        private readonly ManualResetEventSlim _done = new(false);
        private static readonly System.Drawing.Size PreviewSize = new(96, 96);

        private IReadOnlyList<string> _familyNames = Array.Empty<string>();
        public IReadOnlyDictionary<string, byte[]> Result { get; private set; }
            = new Dictionary<string, byte[]>();

        public void Prepare(IReadOnlyList<string> familyNames)
        {
            _familyNames = familyNames;
            Result = new Dictionary<string, byte[]>();
            _done.Reset();
        }

        public void WaitForCompletion() => _done.Wait();

        public void Execute(UIApplication app)
        {
            var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null || _familyNames.Count == 0) return;

                var wanted = new HashSet<string>(_familyNames, StringComparer.OrdinalIgnoreCase);
                foreach (var family in new FilteredElementCollector(doc)
                             .OfClass(typeof(Family)).Cast<Family>())
                {
                    if (!wanted.Contains(family.Name) || result.ContainsKey(family.Name)) continue;
                    var png = RenderPreviewPng(doc, family);
                    if (png != null) result[family.Name] = png;
                }
            }
            catch { /* previews are cosmetic — a failure must never break the sync */ }
            finally
            {
                Result = result;
                _done.Set();
            }
        }

        private static byte[]? RenderPreviewPng(Document doc, Family family)
        {
            try
            {
                foreach (var id in family.GetFamilySymbolIds())
                {
                    if (doc.GetElement(id) is not ElementType type) continue;
                    using var bmp = type.GetPreviewImage(PreviewSize);
                    if (bmp == null) continue;
                    using var ms = new System.IO.MemoryStream();
                    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    return ms.ToArray();
                }
            }
            catch { /* a symbol without a renderable preview simply yields no image */ }
            return null;
        }

        public string GetName() => "RVTuk.GetFamilyPreviewsEventHandler";
    }
}

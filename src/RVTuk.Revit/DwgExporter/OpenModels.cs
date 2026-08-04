using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RVTuk.Core.DwgExporter;

namespace RVTuk.Revit.DwgExporter
{
    /// <summary>The other models a run can reach, and what each of them offers by name.</summary>
    internal static class OpenModels
    {
        /// <summary>Project documents open in this Revit session. Links and families are
        /// excluded — neither has view/sheet sets to export — as are handles Revit has already
        /// invalidated.</summary>
        public static List<Document> Enumerate(Autodesk.Revit.ApplicationServices.Application app)
        {
            var docs = new List<Document>();
            foreach (Document doc in app.Documents)
            {
                if (doc == null || !doc.IsValidObject) continue;
                if (doc.IsLinked || doc.IsFamilyDocument) continue;
                docs.Add(doc);
            }
            return docs;
        }

        /// <summary>The same key the config uses to remember folders: the document path, or the
        /// title while the document is unsaved.</summary>
        public static string KeyOf(Document doc)
            => string.IsNullOrWhiteSpace(doc.PathName) ? doc.Title : doc.PathName;

        public static ModelSetupInventory ReadInventory(Document doc) => new ModelSetupInventory
        {
            Key = KeyOf(doc),
            Title = doc.Title,
            IsReadOnly = doc.IsReadOnly,
            SheetSetNames = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheetSet))
                .Cast<ViewSheetSet>()
                .Select(s => s.Name)
                .ToList(),
            PdfSetupNames = ExportPDFSettings.ListNames(doc).ToList(),
            DwgSetupNames = ExportDWGSettings.ListNames(doc).ToList(),
        };
    }
}

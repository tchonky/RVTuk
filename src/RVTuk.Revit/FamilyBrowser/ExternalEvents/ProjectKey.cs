using Autodesk.Revit.DB;

namespace RVTuk.Revit.FamilyBrowser.ExternalEvents
{
    /// <summary>
    /// A document's identity for the browser: which project the last ⟳ compared. Title keeps
    /// two unsaved projects (empty PathName) apart; the prefix keeps a family document apart.
    /// </summary>
    internal static class ProjectKey
    {
        public static string Of(Document doc) =>
            (doc.IsFamilyDocument ? "F|" : "P|") + doc.PathName + "|" + doc.Title;
    }
}

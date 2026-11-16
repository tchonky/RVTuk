using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// The last height typed into a height text, per open document, in shared (survey) feet. A new
    /// topo line takes it; with nothing typed yet it is survey 0. Kept in memory only, so it carries
    /// from one session to the next until the document closes (Application forgets it on
    /// DocumentClosing).
    ///
    /// Keyed through Document's own Equals/GetHashCode, which compare the underlying document, so
    /// two API wrappers of the same document share one entry.
    /// </summary>
    public static class LastTypedHeights
    {
        private static readonly Dictionary<Document, double> Heights = new Dictionary<Document, double>();

        public static double Get(Document doc)
        {
            lock (Heights) return Heights.TryGetValue(doc, out var feet) ? feet : 0;
        }

        public static void Set(Document doc, double sharedFeet)
        {
            lock (Heights) Heights[doc] = sharedFeet;
        }

        public static void Forget(Document doc)
        {
            lock (Heights) Heights.Remove(doc);
        }
    }
}

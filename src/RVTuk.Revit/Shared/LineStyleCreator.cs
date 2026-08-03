using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace RVTuk.Revit.Shared
{
    /// <summary>
    /// Creates the dedicated line subcategories the tools drive their work off. Shared because
    /// Auto Dimensions and Topo Tools both need it, and both create theirs on first run rather
    /// than behind a setup button — you cannot draw a line on a style that does not exist yet.
    /// </summary>
    public static class LineStyleCreator
    {
        public static bool Exists(Document doc, string name) =>
            doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines)
                .SubCategories.Contains(name);

        /// <summary>Must be called inside an open transaction.</summary>
        public static void EnsureExists(Document doc, string name)
        {
            var linesCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            if (linesCategory.SubCategories.Contains(name)) return;
            doc.Settings.Categories.NewSubcategory(linesCategory, name);
        }

        /// <summary>
        /// Creates whichever of <paramref name="names"/> are missing, in a transaction of its own.
        ///
        /// Opens that transaction ONLY when something is actually missing, so the ordinary
        /// refresh — every refresh after the first — stays read-only and does not mark the
        /// document modified. Returns false rather than throwing when the document cannot take a
        /// transaction: a read-only model must still refresh, it simply refreshes without the
        /// styles, which reads the same as a project nobody has drawn in yet.
        /// </summary>
        public static bool TryEnsureInOwnTransaction(Document doc, params string[] names)
        {
            try
            {
                var linesCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);

                var missing = new List<string>();
                foreach (var name in names)
                {
                    if (!linesCategory.SubCategories.Contains(name)) missing.Add(name);
                }

                if (missing.Count == 0) return true;
                if (doc.IsReadOnly) return false;

                using (var tx = new Transaction(doc, "RVTuk — create line styles"))
                {
                    tx.Start();
                    try
                    {
                        foreach (var name in missing) EnsureExists(doc, name);
                        tx.Commit();
                        return true;
                    }
                    catch
                    {
                        tx.RollBack();
                        return false;
                    }
                }
            }
            catch
            {
                return false;
            }
        }
    }
}

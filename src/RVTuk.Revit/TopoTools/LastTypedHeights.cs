using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// The last height typed into a height text, per open document, in shared (survey) feet. A new
    /// topo line takes it; with nothing typed yet it is survey 0. Kept in memory only, so it carries
    /// from one session to the next until the document has closed (TopoDocumentEvents forgets it on
    /// DocumentClosed, and keeps it when the close is called off).
    ///
    /// <see cref="Get"/>, <see cref="Set"/> and <see cref="KeyFor"/> find a document's entry through
    /// Document's own Equals, which compares the underlying document, so two API wrappers of the
    /// same document share one entry. The entries are a list, not a dictionary, so that
    /// <see cref="ForgetKey"/> can remove one after its document has closed without calling Equals
    /// or GetHashCode on it.
    /// </summary>
    public static class LastTypedHeights
    {
        private sealed class Entry
        {
            public Entry(Document key, double sharedFeet)
            {
                Key = key;
                SharedFeet = sharedFeet;
            }

            public Document Key { get; }
            public double SharedFeet { get; set; }
        }

        private static readonly List<Entry> Entries = new List<Entry>();

        public static double Get(Document doc)
        {
            lock (Entries) return Find(doc)?.SharedFeet ?? 0;
        }

        public static void Set(Document doc, double sharedFeet)
        {
            lock (Entries)
            {
                var entry = Find(doc);
                if (entry != null) entry.SharedFeet = sharedFeet;
                else Entries.Add(new Entry(doc, sharedFeet));
            }
        }

        /// <summary>
        /// The stored key object that equals <paramref name="doc"/>, or null when no height is stored
        /// for it. Call it only while <paramref name="doc"/> is open: it calls Equals.
        /// </summary>
        public static Document? KeyFor(Document doc)
        {
            lock (Entries) return Find(doc)?.Key;
        }

        /// <summary>
        /// Removes the entry whose key is this very object (a key <see cref="KeyFor"/> returned),
        /// calling neither Equals nor GetHashCode on it, so it may be a closed document.
        /// </summary>
        public static void ForgetKey(Document key)
        {
            lock (Entries) Entries.RemoveAll(entry => ReferenceEquals(entry.Key, key));
        }

        /// <summary>The entry for <paramref name="doc"/>, or null. Call inside the lock.</summary>
        private static Entry? Find(Document doc)
        {
            foreach (var entry in Entries)
                if (doc.Equals(entry.Key)) return entry;
            return null;
        }
    }
}

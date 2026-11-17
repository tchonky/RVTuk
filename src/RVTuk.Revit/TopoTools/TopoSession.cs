using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// The one open Topo Edit session: the plan view it works in, the toposolid or floor it shapes,
    /// the elements it created (so Cancel can delete them) and the text of every height text in the
    /// view when it started (so Cancel can put typed heights back). While a session is open the
    /// TopoSessionUpdater restyles and labels each new detail line drawn in the view.
    ///
    /// No transaction stays open across the session: Revit cannot keep one open while the user
    /// works, so Cancel undoes by these records, not by a rollback.
    /// </summary>
    public sealed class TopoSession
    {
        /// <summary>The open session, or null. At most one at a time.</summary>
        public static TopoSession? Current { get; private set; }

        /// <summary>Raised once by <see cref="End"/>, after <see cref="Current"/> is cleared.
        /// The Topo Edit panel hides itself on it.</summary>
        public static event Action? Ended;

        public Document Document { get; }
        public ElementId ViewId { get; }
        public ElementId TargetId { get; }

        /// <summary>Ids of the lines and texts made during the session.</summary>
        public HashSet<ElementId> Created { get; } = new HashSet<ElementId>();

        /// <summary>Each height text in the view at the start, by id, with its text as it was.</summary>
        public IReadOnlyDictionary<ElementId, string> TextSnapshot { get; }

        /// <summary>
        /// The last text the updater saw on each height text in the view, trimmed. A text change
        /// counts as typed (and becomes the last typed height) only when it differs from this, so
        /// moves, re-centring and relinking never pass for typing. Seeded from the snapshot.
        /// </summary>
        internal Dictionary<ElementId, string> LastSeenTexts { get; }

        /// <summary>
        /// Set by the session's first committed Update once it has read the Survey Point marker's
        /// height for its summary, a failed read included, so at most one summary names the marker.
        /// </summary>
        public bool MarkerNoted { get; set; }

        private TopoSession(Document document, ElementId viewId, ElementId targetId,
            IReadOnlyDictionary<ElementId, string> textSnapshot)
        {
            Document = document;
            ViewId = viewId;
            TargetId = targetId;
            TextSnapshot = textSnapshot;
            LastSeenTexts = new Dictionary<ElementId, string>();
            foreach (var pair in textSnapshot) LastSeenTexts[pair.Key] = pair.Value.Trim();
        }

        /// <summary>
        /// Opens a session on <paramref name="target"/> in <paramref name="view"/>, snapshots the
        /// view's height texts and switches the updater on. Throws if a session is already open.
        /// Needs no transaction.
        /// </summary>
        public static TopoSession Start(Document document, ViewPlan view, Element target)
        {
            if (Current != null)
                throw new InvalidOperationException("A Topo Edit session is already open.");

            // Document-wide, narrowed by owner view, like HeightTexts: the collector's view
            // constructor would miss texts hidden in the view. Grouped texts are left out, as
            // HeightTexts leaves them.
            var snapshot = new FilteredElementCollector(document)
                .OfClass(typeof(TextNote))
                .WherePasses(new ElementOwnerViewFilter(view.Id))
                .Cast<TextNote>()
                .Where(note => note.GroupId == ElementId.InvalidElementId)
                .Where(HeightTextType.IsHeightText)
                .ToDictionary(note => note.Id, note => note.Text ?? "");

            var session = new TopoSession(document, view.Id, target.Id, snapshot);
            Current = session;
            TopoSessionUpdater.Enable();
            return session;
        }

        /// <summary>
        /// Closes the session, if one is open: clears <see cref="Current"/>, switches the updater
        /// off and raises <see cref="Ended"/>. Applies nothing and undoes nothing. A second call
        /// does nothing.
        /// </summary>
        public static void End()
        {
            if (Current == null) return;
            Current = null;
            TopoSessionUpdater.Disable();

            try
            {
                Ended?.Invoke();
            }
            catch (Exception ex)
            {
                Application.LogDiagnostic($"TopoSession.Ended handler failed: {ex}");
            }
        }

        /// <summary>True when <paramref name="document"/> is this session's document.</summary>
        public bool IsFor(Document document) => document != null && Document.Equals(document);
    }
}

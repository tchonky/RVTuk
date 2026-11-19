using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RVTuk.Core.TopoTools;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// The one open Topo Edit session: the plan view it works in, the toposolid or floor it shapes,
    /// the elements it created (so Cancel can delete them) and the text of every height text in the
    /// view when it started (so Cancel can put typed heights back). While a session is open the
    /// TopoSessionUpdater labels each new line on "_DP-Topo Line" drawn in the view, and while it
    /// is <see cref="Armed"/> it also turns new lines in other styles into topo lines and labels
    /// them.
    ///
    /// No transaction stays open across the session: Revit cannot keep one open while the user
    /// works, so Cancel undoes by these records, not by a rollback.
    /// </summary>
    public sealed class TopoSession
    {
        /// <summary>The open session, or null. At most one at a time.</summary>
        public static TopoSession? Current { get; private set; }

        /// <summary>Raised once by <see cref="Start"/>, after <see cref="Current"/> is set and the
        /// updater switched on. A handler that throws is logged; the session stays open.</summary>
        public static event Action? Started;

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
        /// The target's outline corner record text as stored at the start
        /// (<see cref="OutlineCornerStore.Raw"/>, "" for none). Cancel compares it with the record
        /// it finds to see whether there is anything to put back.
        /// </summary>
        public string CornerSnapshot { get; }

        /// <summary>
        /// The corner record the session starts from, checked against where the corners stood at
        /// the start (<see cref="OutlineCornerPlanner.EffectiveStart"/>): a record already stale
        /// then — Reset Shape, a hand move not yet flagged, a plain element — never makes Cancel
        /// raise a corner. When the vertices cannot be read it is the stored record as it is.
        /// </summary>
        public IReadOnlyList<CornerRecord> CornerStart { get; }

        /// <summary>
        /// The last text the updater saw on each height text in the view, trimmed. A text change
        /// counts as typed (and becomes the last typed height) only when it differs from this, so
        /// moves, re-centring and relinking never pass for typing. Seeded from the snapshot.
        /// </summary>
        internal Dictionary<ElementId, string> LastSeenTexts { get; }

        /// <summary>
        /// Set by the session's first Update that shows its summary (Accept's always does), once it
        /// has read the Survey Point marker's height for it, a failed read included, so at most one
        /// summary names the marker.
        /// </summary>
        public bool MarkerNoted { get; set; }

        /// <summary>
        /// True while new detail lines drawn in the session's view in another style become topo
        /// lines. A session starts armed; another view of its document becoming active disarms it
        /// (<see cref="TopoDocumentEvents"/>), and only Draw Lines arms it again: coming back to the
        /// session's view by its tab, a tiled window or "Go to it" does not. Disarmed, the updater
        /// leaves such lines as drawn, and still labels a new line already on "_DP-Topo Line" (a
        /// copy, offset, mirror or paste of a topo line).
        /// </summary>
        public bool Armed { get; set; } = true;

        /// <summary>
        /// True when saving the session's document should no longer ask about this session ("Don't
        /// ask again until this Topo Edit ends"). False when the session starts; it lasts as long as
        /// the session.
        /// </summary>
        public bool DontAskOnSave { get; set; }

        private TopoSession(Document document, ElementId viewId, ElementId targetId,
            IReadOnlyDictionary<ElementId, string> textSnapshot, string cornerSnapshot,
            IReadOnlyList<CornerRecord> cornerStart)
        {
            Document = document;
            ViewId = viewId;
            TargetId = targetId;
            TextSnapshot = textSnapshot;
            CornerSnapshot = cornerSnapshot;
            CornerStart = cornerStart;
            LastSeenTexts = new Dictionary<ElementId, string>();
            foreach (var pair in textSnapshot) LastSeenTexts[pair.Key] = pair.Value.Trim();
        }

        /// <summary>
        /// Opens a session on <paramref name="target"/> in <paramref name="view"/>, armed, snapshots
        /// the view's height texts and the target's corner record, switches the updater on and
        /// raises <see cref="Started"/>. Throws if a session is already open. Needs no transaction.
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

            string cornerSnapshot = OutlineCornerStore.Raw(target);
            var session = new TopoSession(
                document, view.Id, target.Id, snapshot, cornerSnapshot, CornerStartOf(target, cornerSnapshot));
            Current = session;
            TopoSessionUpdater.Enable();

            try
            {
                Started?.Invoke();
            }
            catch (Exception ex)
            {
                Application.LogDiagnostic($"TopoSession.Started handler failed: {ex}");
            }

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

        /// <summary>
        /// <see cref="CornerStart"/>: the stored record checked against the live vertices. Reading
        /// needs no transaction; if it fails, the stored record as it is, with one log line.
        /// </summary>
        private static IReadOnlyList<CornerRecord> CornerStartOf(Element target, string cornerSnapshot)
        {
            var stored = OutlineCornerRecordCodec.Decode(cornerSnapshot);
            if (stored.Count == 0) return stored;

            try
            {
                var editor = SlabShape.EditorOf(target);
                bool enabled = editor != null && editor.IsEnabled;
                var vertices = new List<XyzPoint>();
                if (enabled)
                {
                    foreach (SlabShapeVertex vertex in editor!.SlabShapeVertices)
                        vertices.Add(SlabShape.ToPoint(vertex.Position));
                }
                return OutlineCornerPlanner.EffectiveStart(stored, enabled, vertices);
            }
            catch (Exception ex)
            {
                Application.LogDiagnostic($"TopoSession: reading the corners at the start failed, the stored record is used: {ex.Message}");
                return stored;
            }
        }

        /// <summary>True when <paramref name="document"/> is this session's document.</summary>
        public bool IsFor(Document document) => document != null && Document.Equals(document);
    }
}

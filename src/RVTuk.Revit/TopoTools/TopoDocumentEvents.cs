using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// The Topo Edit session and the last typed heights follow documents and views:
    /// <list type="bullet">
    /// <item>A document closed for real ends the session open on it and forgets its last typed
    /// height (<see cref="LastTypedHeights"/>). A close called off, at Revit's "Save changes?" say,
    /// keeps both. DocumentClosing only notes what the closing document has: until DocumentClosed
    /// the close may still be called off, and the document is still open, with its session.
    /// DocumentClosed carries no document and is raised for a close called off too, so it acts on
    /// that note, found by DocumentId, only when its status is Succeeded.</item>
    /// <item>Activating a view of another document ends the session. Activating another view of
    /// the session's document disarms it (<see cref="TopoSession.Armed"/>).</item>
    /// </list>
    /// Every handler catches everything and logs one line.
    /// </summary>
    internal static class TopoDocumentEvents
    {
        /// <summary>What DocumentClosing found on a closing document: the session open on it, and
        /// the key its last typed height is stored under. Either is null when it had none.</summary>
        private sealed class Closing
        {
            public Closing(TopoSession? session, Document? heightKey)
            {
                Session = session;
                HeightKey = heightKey;
            }

            public TopoSession? Session { get; }
            public Document? HeightKey { get; }
        }

        /// <summary>Each closing document's note, by the DocumentId that pairs its DocumentClosing
        /// with the DocumentClosed after it. Removed at DocumentClosed.</summary>
        private static readonly Dictionary<int, Closing> ClosingDocuments = new Dictionary<int, Closing>();

        /// <summary>Hooks DocumentClosing, DocumentClosed and ViewActivated. Call once from
        /// OnStartup. Never throws.</summary>
        internal static void Install(UIControlledApplication app)
        {
            try
            {
                app.ControlledApplication.DocumentClosing += OnDocumentClosing;
                app.ControlledApplication.DocumentClosed += OnDocumentClosed;
                app.ViewActivated += OnViewActivated;
            }
            catch (Exception ex)
            {
                Application.LogDiagnostic($"Topo Tools document events: install failed: {ex}");
            }
        }

        /// <summary>Notes the session open on the closing document and its height's key. Ends
        /// nothing and forgets nothing.</summary>
        private static void OnDocumentClosing(object? sender, DocumentClosingEventArgs e)
        {
            try
            {
                var session = TopoSession.Current;
                var sessionOnIt = session != null && session.IsFor(e.Document) ? session : null;
                ClosingDocuments[e.DocumentId] = new Closing(sessionOnIt, LastTypedHeights.KeyFor(e.Document));
            }
            catch (Exception ex)
            {
                Application.LogDiagnostic($"Topo Tools DocumentClosing failed: {ex}");
            }
        }

        /// <summary>Closed for real: ends the session that was on the document, if it is still the
        /// open one, and forgets the document's height. Called off or failed: nothing changed, so
        /// nothing is put back.</summary>
        private static void OnDocumentClosed(object? sender, DocumentClosedEventArgs e)
        {
            try
            {
                if (!ClosingDocuments.TryGetValue(e.DocumentId, out var closing)) return;
                ClosingDocuments.Remove(e.DocumentId);
                if (e.Status != RevitAPIEventStatus.Succeeded) return;

                // The document is gone: nothing here calls the API, Equals or GetHashCode on it.
                // The session and the stored key are matched by reference instead.
                if (closing.Session != null && ReferenceEquals(TopoSession.Current, closing.Session))
                    TopoSession.End();
                if (closing.HeightKey != null) LastTypedHeights.ForgetKey(closing.HeightKey);
            }
            catch (Exception ex)
            {
                Application.LogDiagnostic($"Topo Tools DocumentClosed failed: {ex}");
            }
        }

        /// <summary>A view of another document ends the session; another view of the session's
        /// document disarms it. Coming back to the session's view does not arm it: only Draw Lines
        /// does.</summary>
        private static void OnViewActivated(object? sender, ViewActivatedEventArgs e)
        {
            try
            {
                var session = TopoSession.Current;
                if (session == null) return;

                if (!session.IsFor(e.Document))
                {
                    TopoSession.End();
                    return;
                }

                if (e.Status == RevitAPIEventStatus.Succeeded && e.CurrentActiveView?.Id != session.ViewId)
                    session.Armed = false;
            }
            catch (Exception ex)
            {
                Application.LogDiagnostic($"Topo Tools ViewActivated failed: {ex}");
            }
        }
    }
}

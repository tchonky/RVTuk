using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RVTuk.Revit.TopoTools.Commands
{
    /// <summary>What the Topo Edit panel's commands share: finding the open session and its view.</summary>
    internal static class TopoSessionCommands
    {
        public const string Title = "Topo Edit";

        /// <summary>Said when <c>Transaction.Commit</c> did not commit: failure processing rolled it back.</summary>
        public const string RolledBack = "Nothing was applied — Revit rolled the change back.";

        /// <summary>
        /// The open session and its plan view. False — after telling the user, and hiding the panel
        /// or ending the session as fits — when there is no session or its view is gone.
        /// </summary>
        public static bool TryGet(out TopoSession session, out ViewPlan view)
        {
            session = TopoSession.Current!;
            view = null!;

            if (session == null)
            {
                TopoEditPanel.Hide();
                TaskDialog.Show(Title, "No Topo Edit session is open.");
                return false;
            }

            if (session.Document.GetElement(session.ViewId) is not ViewPlan plan)
            {
                TopoSession.End();
                TaskDialog.Show(Title, "The session's view no longer exists. The Topo Edit session is closed.");
                return false;
            }

            view = plan;
            return true;
        }

        /// <summary>"Toposolid 123" / "Floor 123", as <see cref="SlabTarget.Label"/> names them;
        /// "Element 123" once it is gone.</summary>
        public static string LabelOf(Element? element, ElementId id) =>
            $"{(element is Floor ? "Floor" : element is Toposolid ? "Toposolid" : "Element")} {id.Value}";
    }
}

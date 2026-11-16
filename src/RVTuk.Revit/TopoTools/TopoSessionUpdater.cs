using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// Active only during a Topo Edit session. Inside the user's own transaction it gives each new
    /// detail line drawn in the session's view the "_DP-Topo Line" style and a height text at the
    /// last typed height, and remembers each height the user types into a height text as the next
    /// line's height. There is no API to preset the Detail Line tool's style, hence an updater; and
    /// only an updater may change the document inside the user's transaction.
    ///
    /// Registered once at startup as optional, so documents it touched open without a
    /// missing-updater warning on PCs without the add-in, then kept disabled outside sessions.
    /// </summary>
    public sealed class TopoSessionUpdater : IUpdater
    {
        private static readonly Guid UpdaterGuid = new Guid("0aa14034-8c77-48a4-b5d6-dca41ed6da08");

        private static TopoSessionUpdater? _instance;

        private readonly UpdaterId _id;

        private TopoSessionUpdater(AddInId addInId)
        {
            _id = new UpdaterId(addInId, UpdaterGuid);
        }

        /// <summary>Registers the updater and its triggers, then disables it. Call once at startup.</summary>
        public static void Register(AddInId addInId)
        {
            var updater = new TopoSessionUpdater(addInId);
            UpdaterRegistry.RegisterUpdater(updater, true);
            UpdaterRegistry.AddTrigger(updater._id,
                new ElementClassFilter(typeof(CurveElement)), Element.GetChangeTypeElementAddition());
            UpdaterRegistry.AddTrigger(updater._id,
                new ElementClassFilter(typeof(TextNote)), Element.GetChangeTypeAny());
            UpdaterRegistry.DisableUpdater(updater._id);
            _instance = updater;
        }

        public static void Enable()
        {
            if (_instance == null) return;
            try
            {
                if (!UpdaterRegistry.IsUpdaterEnabled(_instance._id))
                    UpdaterRegistry.EnableUpdater(_instance._id);
            }
            catch (Exception ex)
            {
                Application.LogDiagnostic($"TopoSessionUpdater.Enable failed: {ex}");
            }
        }

        public static void Disable()
        {
            if (_instance == null) return;
            try
            {
                if (UpdaterRegistry.IsUpdaterEnabled(_instance._id))
                    UpdaterRegistry.DisableUpdater(_instance._id);
            }
            catch (Exception ex)
            {
                Application.LogDiagnostic($"TopoSessionUpdater.Disable failed: {ex}");
            }
        }

        public void Execute(UpdaterData data)
        {
            try
            {
                var session = TopoSession.Current;
                var doc = data.GetDocument();
                if (session == null || !session.IsFor(doc)) return;
                if (doc.GetElement(session.ViewId) is not View view) return;

                // Texts made here show up as added, not modified, but are skipped explicitly too:
                // their text is the last typed height already, never something the user typed.
                var createdHere = new HashSet<ElementId>();

                GraphicsStyle? topoStyle = null;
                bool styleLookedUp = false;
                foreach (var id in data.GetAddedElementIds())
                {
                    try
                    {
                        if (doc.GetElement(id) is not CurveElement line) continue;
                        if (line.CurveElementType != CurveElementType.DetailCurve) continue;
                        if (line.OwnerViewId != session.ViewId) continue;
                        if (line.GroupId != ElementId.InvalidElementId) continue;

                        if (!styleLookedUp)
                        {
                            topoStyle = FindTopoLineStyle(doc);
                            styleLookedUp = true;
                        }
                        if (topoStyle == null) break;

                        line.LineStyle = topoStyle;
                        var text = HeightTexts.CreateFor(doc, view, line, LastTypedHeights.Get(doc));
                        createdHere.Add(text.Id);
                        session.Created.Add(line.Id);
                        session.Created.Add(text.Id);
                    }
                    catch (Exception ex)
                    {
                        Application.LogDiagnostic($"TopoSessionUpdater: new line {id} failed: {ex}");
                    }
                }

                foreach (var id in data.GetModifiedElementIds())
                {
                    try
                    {
                        if (createdHere.Contains(id)) continue;
                        if (doc.GetElement(id) is not TextNote note) continue;
                        if (note.OwnerViewId != session.ViewId) continue;
                        if (!HeightTextType.IsHeightText(note)) continue;

                        if (TopoHeightParser.TryParse(doc, note.Text, out double sharedFeet))
                            LastTypedHeights.Set(doc, sharedFeet);
                    }
                    catch (Exception ex)
                    {
                        Application.LogDiagnostic($"TopoSessionUpdater: text {id} failed: {ex}");
                    }
                }
            }
            catch (Exception ex)
            {
                Application.LogDiagnostic($"TopoSessionUpdater.Execute failed: {ex}");
            }
        }

        private static GraphicsStyle? FindTopoLineStyle(Document doc)
        {
            var lines = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            if (lines == null || !lines.SubCategories.Contains(TopoLineStyle.LineStyleName)) return null;
            return lines.SubCategories.get_Item(TopoLineStyle.LineStyleName)
                ?.GetGraphicsStyle(GraphicsStyleType.Projection);
        }

        public UpdaterId GetUpdaterId() => _id;

        public ChangePriority GetChangePriority() => ChangePriority.Annotations;

        public string GetUpdaterName() => "RVTuk Topo Edit session";

        public string GetAdditionalInformation() =>
            "Gives detail lines drawn during a Topo Edit session the _DP-Topo Line style and a height text.";
    }
}

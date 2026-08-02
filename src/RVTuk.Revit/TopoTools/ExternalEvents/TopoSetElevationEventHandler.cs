using System;
using System.Collections.Generic;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RVTuk.Revit.TopoTools.ExternalEvents
{
    /// <summary>
    /// Writes a shared elevation from the text typed in the pane, onto every line the edit applies
    /// to — one row on its own, or the whole selection when several lines are selected. One
    /// transaction covers the lot, so a bulk change is a single undo.
    ///
    /// The text is parsed by <c>UnitFormatUtils.TryParse</c> against <c>SpecTypeId.Length</c>, so the
    /// document's own units decide what "42.750" means — this tool never assumes millimetres, and a
    /// user may type any form Revit itself accepts. Empty text clears the height rather than
    /// storing zero, because zero is a legitimate shared elevation.
    /// </summary>
    public class TopoSetElevationEventHandler : IExternalEventHandler
    {
        private readonly ManualResetEventSlim _done = new(false);
        private IReadOnlyList<long> _lineIds = Array.Empty<long>();
        private string _text = "";

        public string Summary { get; private set; } = "";

        public void Prepare(IReadOnlyList<long> lineIds, string text)
        {
            _lineIds = lineIds ?? Array.Empty<long>();
            _text = text ?? "";
        }

        public void Reset() => _done.Reset();
        public void WaitForCompletion() => _done.Wait();

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    Summary = "No document is open.";
                    return;
                }
                if (_lineIds.Count == 0)
                {
                    Summary = "";
                    return;
                }

                bool clearing = string.IsNullOrWhiteSpace(_text);
                double elevationFeet = 0;

                if (!clearing &&
                    !UnitFormatUtils.TryParse(doc.GetUnits(), SpecTypeId.Length, _text, out elevationFeet))
                {
                    Summary = $"Could not read \"{_text}\" as a height.";
                    return;
                }

                using (var tx = new Transaction(doc, "Topo Tools — set line height"))
                {
                    tx.Start();
                    try
                    {
                        int changed = 0;
                        foreach (var lineId in _lineIds)
                        {
                            var line = doc.GetElement(new ElementId(lineId));
                            if (line == null) continue;

                            if (clearing) TopoElevationStore.Clear(line);
                            else TopoElevationStore.Set(line, elevationFeet);
                            changed++;
                        }

                        if (changed == 0)
                        {
                            tx.RollBack();
                            Summary = "Those lines no longer exist.";
                            return;
                        }

                        tx.Commit();

                        string where = changed == 1 ? "" : $" on {changed} lines";
                        Summary = clearing
                            ? $"Height cleared{where}."
                            : "Height set to " +
                              TopoLengthFormatter.Format(doc, elevationFeet) +
                              where + ".";
                    }
                    catch (Exception ex)
                    {
                        tx.RollBack();
                        Summary = "Could not set that height: " + ex.Message;
                    }
                }
            }
            catch (Exception ex)
            {
                Summary = "Could not set that height: " + ex.Message;
            }
            finally
            {
                _done.Set();
            }
        }

        public string GetName() => "RVTuk.TopoToolsSetElevationEventHandler";
    }
}

using System;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RVTuk.Revit.TopoTools.ExternalEvents
{
    /// <summary>
    /// Writes one line's shared elevation from the text typed in the pane.
    ///
    /// The text is parsed by <c>UnitFormatUtils.TryParse</c> against <c>SpecTypeId.Length</c>, so the
    /// document's own units decide what "42.750" means — this tool never assumes millimetres, and a
    /// user may type any form Revit itself accepts. Empty text clears the height rather than
    /// storing zero, because zero is a legitimate shared elevation.
    /// </summary>
    public class TopoSetElevationEventHandler : IExternalEventHandler
    {
        private readonly ManualResetEventSlim _done = new(false);
        private long _lineId;
        private string _text = "";

        public string Summary { get; private set; } = "";

        public void Prepare(long lineId, string text)
        {
            _lineId = lineId;
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

                var line = doc.GetElement(new ElementId(_lineId));
                if (line == null)
                {
                    Summary = "That line no longer exists.";
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
                        if (clearing) TopoElevationStore.Clear(line);
                        else TopoElevationStore.Set(line, elevationFeet);

                        tx.Commit();
                        Summary = clearing
                            ? "Height cleared."
                            : "Height set to " +
                              UnitFormatUtils.Format(doc.GetUnits(), SpecTypeId.Length, elevationFeet, false) + ".";
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

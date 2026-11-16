using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>THROWAWAY probe: three unverified slab-shape API facts. Delete after the results are recorded.</summary>
    [Transaction(TransactionMode.Manual)]
    public class TopoProbeCommand : IExternalCommand
    {
        private const double Mm = 304.8;
        private static string F(double feet) => (Math.Round(feet * Mm * 10) / 10).ToString("0.0") + " mm";

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var uidoc = data.Application.ActiveUIDocument;
            var doc = uidoc.Document;
            var report = new StringBuilder();

            var ids = uidoc.Selection.GetElementIds();
            Element? element = ids.Count == 1 ? doc.GetElement(ids.First()) : null;
            if (element == null || !(element is Floor || element is Toposolid))
            {
                TaskDialog.Show("Topo probe", "Select exactly one Floor or Toposolid first.");
                return Result.Cancelled;
            }

            report.AppendLine($"Element: {element.GetType().Name} id {element.Id.ToString()}, category {element.Category?.Name}");
            report.AppendLine($"Revit {data.Application.Application.VersionNumber}");

            using (var t = new Transaction(doc, "Topo probe (rolled back)"))
            {
                t.Start();
                try
                {
                    ProbeA(element, report);
                    ProbeB(doc, element, report);
                    ProbeC(doc, element, report);
                }
                finally
                {
                    t.RollBack();
                }
            }

            ProbeD(report);

            string path = Path.Combine(Path.GetTempPath(), "RVTuk-topo-probe.txt");
            try { File.WriteAllText(path, report.ToString()); } catch (Exception) { }
            TaskDialog.Show("Topo probe", report + "\nSame text saved to:\n" + path);
            return Result.Succeeded;
        }

        // (a) AddPoints at box centre, Z = box top + 1000 mm
        private static void ProbeA(Element element, StringBuilder r)
        {
            r.AppendLine("\n(a) AddPoints interior, absolute Z");
            try
            {
                var box = element.get_BoundingBox(null);
                var editor = SlabShape.EditorOf(element)!;
                r.AppendLine($"  editor enabled before: {editor.IsEnabled}");
                if (!editor.IsEnabled) editor.Enable();
                element.Document.Regenerate();
                var c = (box.Min + box.Max) / 2;
                double asked = box.Max.Z + 1000 / Mm;
                var added = editor.AddPoints(new List<XYZ> { new XYZ(c.X, c.Y, asked) });
                element.Document.Regenerate();
                r.AppendLine($"  box top Z {F(box.Max.Z)}, asked Z {F(asked)}");
                if (added == null || added.Count == 0) { r.AppendLine("  AddPoints returned no vertices"); return; }
                foreach (SlabShapeVertex v in added)
                    r.AppendLine($"  returned vertex Z {F(v.Position.Z)}  (returned - asked = {F(v.Position.Z - asked)})");
            }
            catch (Exception ex) { r.AppendLine("  FAILED: " + ex.GetType().Name + ": " + ex.Message); }
        }

        // (b) DrawPoint at first outline edge midpoint, then ModifySubElement +500 mm
        private static void ProbeB(Document doc, Element element, StringBuilder r)
        {
            r.AppendLine("\n(b) DrawPoint on edge + ModifySubElement");
            try
            {
                var editor = SlabShape.EditorOf(element)!;
                if (!editor.IsEnabled || editor.SlabShapeVertices.Size == 0)
                {
                    r.AppendLine("  not shape-edited - raise a corner first");
                    return;
                }
                var faces = TopFaces(element, out string how);
                if (faces.Count == 0) { r.AppendLine("  no top face found"); return; }

                var loop = faces.OrderByDescending(f => f.Area).First().GetEdgesAsCurveLoops().FirstOrDefault();
                var edge = loop?.FirstOrDefault();
                if (edge == null) { r.AppendLine("  no outline edge found"); return; }
                var mid = edge.Evaluate(0.5, true);

                // Surface Z at the midpoint XY: nearest point on the top faces to a point straight above it.
                var probe = new XYZ(mid.X, mid.Y, element.get_BoundingBox(null).Max.Z + 1);
                double best = double.MaxValue, surfaceZ = mid.Z;
                foreach (var f in faces)
                {
                    var ir = f.Project(probe);
                    if (ir != null && ir.Distance < best) { best = ir.Distance; surfaceZ = ir.XYZPoint.Z; }
                }
                r.AppendLine($"  surface Z via {how} + Face.Project; edge midpoint Z {F(mid.Z)}, surface Z {F(surfaceZ)}");

                SlabShapeVertex? vertex = null;
                try
                {
#pragma warning disable CS0618
                    vertex = editor.DrawPoint(new XYZ(mid.X, mid.Y, surfaceZ));
#pragma warning restore CS0618
                }
                catch (Exception ex) { r.AppendLine("  DrawPoint threw: " + ex.GetType().Name + ": " + ex.Message); }
                r.AppendLine($"  DrawPoint returned {(vertex == null ? "null" : "vertex at Z " + F(vertex.Position.Z))}");
                if (vertex == null) return;

                try
                {
                    editor.ModifySubElement(vertex, 500 / Mm);
                    r.AppendLine("  ModifySubElement returned void, did not throw");
                }
                catch (Exception ex) { r.AppendLine("  ModifySubElement threw: " + ex.GetType().Name + ": " + ex.Message); }
                doc.Regenerate();
                SlabShapeVertex? found = null;
                foreach (SlabShapeVertex v in editor.SlabShapeVertices)
                    if (Math.Abs(v.Position.X - mid.X) < 1e-3 && Math.Abs(v.Position.Y - mid.Y) < 1e-3) { found = v; break; }
                r.AppendLine(found == null
                    ? "  final vertex not found at that XY"
                    : $"  final vertex Z {F(found.Position.Z)} (expected {F(surfaceZ + 500 / Mm)})");
            }
            catch (Exception ex) { r.AppendLine("  FAILED: " + ex.GetType().Name + ": " + ex.Message); }
        }

        // (c) Enable() on a floor, sketch contents
        private static void ProbeC(Document doc, Element element, StringBuilder r)
        {
            r.AppendLine("\n(c) Floor Enable() and sketch contents");
            if (!(element is Floor floor)) { r.AppendLine("  not a floor, skipped"); return; }
            try
            {
                var editor = floor.GetSlabShapeEditor();
                r.AppendLine($"  editor already enabled = {editor.IsEnabled}");
                try { editor.Enable(); r.AppendLine("  Enable() did not throw"); }
                catch (Exception ex) { r.AppendLine("  Enable() threw: " + ex.GetType().Name + ": " + ex.Message); }
            }
            catch (Exception ex) { r.AppendLine("  GetSlabShapeEditor FAILED: " + ex.Message); }

            try
            {
                var sketch = doc.GetElement(floor.SketchId) as Sketch;
                if (sketch == null) { r.AppendLine("  no sketch"); return; }
                var all = sketch.GetAllElements();
                r.AppendLine($"  sketch.GetAllElements(): {all.Count} item(s)");
                foreach (var id in all)
                {
                    var e = doc.GetElement(id);
                    r.AppendLine($"   - id {id.ToString()}: {e?.GetType().Name ?? "null"} / category {e?.Category?.Name ?? "none"}");
                }
            }
            catch (Exception ex) { r.AppendLine("  sketch FAILED: " + ex.GetType().Name + ": " + ex.Message); }
        }

        // (d) ribbon tabs via the unsupported Autodesk.Windows API; does not touch the document
        private static void ProbeD(StringBuilder r)
        {
            r.AppendLine("\n(d) Ribbon tabs (Autodesk.Windows)  << = contextual and visible");
            try
            {
                foreach (var tab in Autodesk.Windows.ComponentManager.Ribbon.Tabs)
                {
                    bool hot = tab.IsContextualTab && tab.IsVisible;
                    r.AppendLine($"  tab Id='{tab.Id}' Title='{tab.Title}' contextual={tab.IsContextualTab} visible={tab.IsVisible} active={tab.IsActive}{(hot ? " <<" : "")}");
                    if (!tab.IsContextualTab) continue;
                    foreach (var panel in tab.Panels)
                        r.AppendLine($"     panel Source.Id='{panel.Source?.Id}' Source.Title='{panel.Source?.Title}'");
                }
            }
            catch (Exception ex) { r.AppendLine("  FAILED: " + ex.GetType().Name + ": " + ex.Message); }
        }

        private static List<Face> TopFaces(Element element, out string how)
        {
            var faces = new List<Face>();
            try
            {
                foreach (var reference in HostObjectUtils.GetTopFaces((HostObject)element))
                    if (element.GetGeometryObjectFromReference(reference) is Face f) faces.Add(f);
                if (faces.Count > 0) { how = "HostObjectUtils.GetTopFaces"; return faces; }
            }
            catch (Exception) { }

            how = "geometry faces with normal Z > 0";
            foreach (var go in element.get_Geometry(new Options()))
            {
                if (go is Solid s)
                    foreach (Face f in s.Faces)
                        if (f is PlanarFace pf && pf.FaceNormal.Z > 0) faces.Add(f);
            }
            return faces;
        }
    }
}

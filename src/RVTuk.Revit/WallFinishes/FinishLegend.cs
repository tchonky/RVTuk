using System.Linq;
using Autodesk.Revit.DB;
using RVTuk.Core.WallFinishes;

namespace RVTuk.Revit.WallFinishes
{
    /// <summary>
    /// The "RVTuk Wall Finishes" drafting view: one sample line per finish style that has a line
    /// somewhere in the project, with its material's name. A drafting view because the API cannot
    /// create a Legend view from scratch, only duplicate one.
    /// </summary>
    public static class FinishLegend
    {
        public const string ViewName = "RVTuk Wall Finishes";
        private const double Metre = 1 / 0.3048;

        /// <summary>Call inside an open transaction. Redraws the whole view each time.</summary>
        public static void Rebuild(Document doc)
        {
            var legend = FindOrCreate(doc);
            if (legend == null) return;

            var old = new FilteredElementCollector(doc, legend.Id)
                .WhereElementIsNotElementType()
                .Where(e => e.OwnerViewId == legend.Id)
                .Select(e => e.Id)
                .ToList();
            if (old.Count > 0) doc.Delete(old);

            var styles = new FilteredElementCollector(doc)
                .OfClass(typeof(CurveElement))
                .Cast<CurveElement>()
                .Where(c => c.OwnerViewId != legend.Id)
                .Select(c => c.LineStyle)
                .OfType<GraphicsStyle>()
                .Where(s => FinishStyleName.IsFinishStyle(s.Name))
                .GroupBy(s => s.Id)
                .Select(g => g.First())
                .OrderBy(s => s.Name)
                .ToList();

            var textType = doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
            for (int i = 0; i < styles.Count; i++)
            {
                double y = -i * Metre;
                var line = doc.Create.NewDetailCurve(legend, Line.CreateBound(new XYZ(0, y, 0), new XYZ(Metre, y, 0)));
                line.LineStyle = styles[i];
                if (textType != ElementId.InvalidElementId)
                {
                    TextNote.Create(doc, legend.Id, new XYZ(1.3 * Metre, y, 0),
                        FinishStyleName.MaterialOf(styles[i].Name),
                        new TextNoteOptions(textType) { VerticalAlignment = VerticalTextAlignment.Middle });
                }
            }
        }

        private static ViewDrafting? FindOrCreate(Document doc)
        {
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewDrafting))
                .Cast<ViewDrafting>()
                .FirstOrDefault(v => !v.IsTemplate && v.Name == ViewName);
            if (existing != null) return existing;

            var type = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(t => t.ViewFamily == ViewFamily.Drafting);
            if (type == null) return null;

            var view = ViewDrafting.Create(doc, type.Id);
            view.Name = ViewName;
            view.Scale = 50;
            return view;
        }
    }
}

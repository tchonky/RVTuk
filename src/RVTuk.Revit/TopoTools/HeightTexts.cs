using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RVTuk.Core.Shared.Geometry;
using RVTuk.Core.TopoTools;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// The height texts: one "_DP-Topo Height" text note per topo line, centred on the line's
    /// middle and rotated along it, holding the line's shared (survey) elevation. Which text belongs
    /// to which line is stored on the text (HeightTextLinkStore) and settled by HeightTextLinker.
    ///
    /// Texts inside a group are left out, matching TopoLineCollector, which skips grouped lines:
    /// otherwise a grouped text would look orphaned and be deleted out of its group.
    /// </summary>
    public static class HeightTexts
    {
        /// <summary>A move shorter than this (0.1 mm) is skipped, so a settled text is not touched.</summary>
        private const double MoveToleranceFeet = 0.1 / 304.8;

        /// <summary>A turn smaller than this is skipped, for the same reason.</summary>
        private const double RotateToleranceRadians = 0.001;

        /// <summary>
        /// The Height Labels pass over the view: deletes texts whose line is gone, links stray and
        /// copied texts to the nearest free line, gives every line without a text a new one at the
        /// last typed height, and moves every linked text back to its line's middle. Texts it cannot
        /// link are left alone; their number is returned. Ids of the texts it creates are added to
        /// <paramref name="created"/> when given.
        ///
        /// Idempotent: a second run in a row finds every text linked and in place, and modifies
        /// nothing. Caller must already be inside a transaction.
        /// </summary>
        public static int Label(Document doc, View view, ICollection<ElementId>? created)
        {
            var spotByLine = TopoLineCollector.Collect(doc, view)
                .ToDictionary(line => line.LineId, line => HeightLabelPlacement.Midpoint(line.Polyline));
            var texts = CollectTexts(doc, view);
            var plan = Plan(texts, spotByLine);

            // A text is deleted only when its line is really gone. A line that still exists but is
            // no longer a collectable topo line (restyled, grouped) keeps its text, counted unlinked.
            var lineIdByText = texts.ToDictionary(text => text.Note.Id.Value, text => text.LineId);
            var toDelete = new List<ElementId>();
            int keptOrphans = 0;
            foreach (var textId in plan.DeleteTexts)
            {
                var lineId = lineIdByText[textId];
                if (lineId.HasValue && doc.GetElement(new ElementId(lineId.Value)) != null) keptOrphans++;
                else toDelete.Add(new ElementId(textId));
            }
            if (toDelete.Count > 0) doc.Delete(toDelete);

            var textById = texts.ToDictionary(text => text.Note.Id.Value, text => text.Note);
            foreach (var textId in plan.Relinked)
                HeightTextLinkStore.Set(textById[textId], plan.LineByText[textId]);

            foreach (var pair in plan.LineByText)
                Recentre(doc, textById[pair.Key], spotByLine[pair.Value]);

            if (plan.LinesNeedingText.Count > 0)
            {
                double sharedFeet = LastTypedHeights.Get(doc);
                foreach (var lineId in plan.LinesNeedingText)
                {
                    if (doc.GetElement(new ElementId(lineId)) is not CurveElement line) continue;
                    var note = CreateFor(doc, view, line, sharedFeet);
                    created?.Add(note.Id);
                }
            }

            return plan.UnlinkedTexts.Count + keptOrphans;
        }

        /// <summary>
        /// Creates the height text for one line, centred on its middle and rotated along it, showing
        /// <paramref name="sharedFeet"/> in metres, and links it to the line. Caller must already be
        /// inside a transaction.
        /// </summary>
        public static TextNote CreateFor(Document doc, View view, CurveElement line, double sharedFeet)
        {
            var curve = line.GeometryCurve;
            var polyline = curve.Tessellate().Select(point => new XyPoint(point.X, point.Y)).ToList();
            var spot = HeightLabelPlacement.Midpoint(polyline);
            double z = curve.GetEndPoint(0).Z;

            var options = new TextNoteOptions(HeightTextType.EnsureExists(doc))
            {
                HorizontalAlignment = HorizontalTextAlignment.Center,
                VerticalAlignment = VerticalTextAlignment.Middle,
                Rotation = spot.AngleRadians,
            };
            var note = TextNote.Create(
                doc, view.Id, new XYZ(spot.Point.X, spot.Point.Y, z),
                SurveyHeightText.Format(sharedFeet), options);

            HeightTextLinkStore.Set(note, line.Id.Value);
            return note;
        }

        /// <summary>
        /// Each topo line's height text, trimmed (Revit appends "\r"), as the linker would settle
        /// them — so it agrees with Label even when Label has not run. Lines without a text are
        /// absent. Reads only; needs no transaction.
        /// </summary>
        public static IReadOnlyDictionary<long, string> ReadHeights(Document doc, View view)
        {
            var spotByLine = TopoLineCollector.Collect(doc, view)
                .ToDictionary(line => line.LineId, line => HeightLabelPlacement.Midpoint(line.Polyline));
            var texts = CollectTexts(doc, view);
            var plan = Plan(texts, spotByLine);

            var textById = texts.ToDictionary(text => text.Note.Id.Value, text => text.Note);
            return plan.LineByText.ToDictionary(
                pair => pair.Value,
                pair => (textById[pair.Key].Text ?? "").Trim());
        }

        private sealed record HeightText(TextNote Note, long? LineId);

        private static List<HeightText> CollectTexts(Document doc, View view)
        {
            // Document-wide, narrowed by owner view: the collector's view constructor misses hidden
            // texts (Text Notes off, a filter), and every run would then give their lines another
            // text. ElementOwnerViewFilter keeps hidden elements.
            return new FilteredElementCollector(doc)
                .OfClass(typeof(TextNote))
                .WherePasses(new ElementOwnerViewFilter(view.Id))
                .Cast<TextNote>()
                .Where(note => note.GroupId == ElementId.InvalidElementId)
                .Where(HeightTextType.IsHeightText)
                .Select(note => new HeightText(
                    note,
                    HeightTextLinkStore.TryGetLineId(note, out long lineId) ? lineId : (long?)null))
                .ToList();
        }

        private static LinkPlan Plan(List<HeightText> texts, Dictionary<long, LabelSpot> spotByLine)
        {
            var claims = texts
                .Select(text => new TextClaim(
                    text.Note.Id.Value, text.LineId, new XyPoint(text.Note.Coord.X, text.Note.Coord.Y)))
                .ToList();
            var lines = spotByLine
                .Select(pair => new LineSpot(pair.Key, pair.Value.Point))
                .ToList();
            return HeightTextLinker.Plan(claims, lines);
        }

        /// <summary>
        /// Moves the text's anchor onto the spot and turns it to the spot's angle about that point,
        /// skipping either step when it is already within tolerance. Never changes the text itself.
        /// </summary>
        private static void Recentre(Document doc, TextNote note, LabelSpot spot)
        {
            // A text placed with the plain Text tool is left/top aligned; centre it on its anchor
            // first (only when it differs, so a settled text is not touched), then read Coord.
            if (note.HorizontalAlignment != HorizontalTextAlignment.Center)
                note.HorizontalAlignment = HorizontalTextAlignment.Center;
            if (note.VerticalAlignment != VerticalTextAlignment.Middle)
                note.VerticalAlignment = VerticalTextAlignment.Middle;

            var coord = note.Coord;
            double dx = spot.Point.X - coord.X;
            double dy = spot.Point.Y - coord.Y;
            if (Math.Sqrt(dx * dx + dy * dy) > MoveToleranceFeet)
                ElementTransformUtils.MoveElement(doc, note.Id, new XYZ(dx, dy, 0));

            var direction = note.BaseDirection;
            double turn = WrapAngle(spot.AngleRadians - Math.Atan2(direction.Y, direction.X));
            if (Math.Abs(turn) > RotateToleranceRadians)
            {
                var anchor = new XYZ(spot.Point.X, spot.Point.Y, coord.Z);
                var axis = Line.CreateBound(anchor, anchor + XYZ.BasisZ);
                ElementTransformUtils.RotateElement(doc, note.Id, axis, turn);
            }
        }

        /// <summary>Folds an angle into (−π, π].</summary>
        private static double WrapAngle(double angle)
        {
            while (angle > Math.PI) angle -= 2 * Math.PI;
            while (angle <= -Math.PI) angle += 2 * Math.PI;
            return angle;
        }
    }
}

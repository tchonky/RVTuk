using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using RVTuk.Core.TopoTools;
using RVTuk.Revit.Shared;

namespace RVTuk.Revit.TopoTools
{
    public sealed record ConvertOutcome(
        ElementId NewId, bool BecameToposolid, int PointsLost, int CreasesLost, int ParametersNotCarried);

    /// <summary>
    /// One floor becomes a toposolid, or one toposolid a floor, inside the caller's transaction.
    ///
    /// Everything is read first, then the source is deleted, then the target is created. With the
    /// source gone the new element never overlaps it, so Revit raises no overlap warning; if
    /// anything later fails, the caller's rollback brings the source back. The top surface is what
    /// is preserved; the thickness and bottom follow the new type.
    /// </summary>
    public static class SlabConverter
    {
        private const string OurVendorId = "KnafoKlimor";

        /// <summary>Caller must already be inside a transaction.</summary>
        /// <exception cref="InvalidOperationException">With a short reason, e.g. "it has no sketch to copy".</exception>
        public static ConvertOutcome Convert(Document doc, Element source, ElementId targetTypeId)
        {
            bool toToposolid = source is Floor;
            var sourceId = source.Id;

            var loops = ReadLoops(doc, source);
            var levelId = source.LevelId;
            if (levelId == ElementId.InvalidElementId) throw new InvalidOperationException("it has no level");
            double offset = OffsetParameter(source)?.AsDouble() ?? 0;
            var shape = SlabShape.Read(source, loops);
            var parameters = ParameterCarrier.Snapshot(source);
            var entities = OurEntities(source);
            bool pinned = source.Pinned;

            if (pinned) source.Pinned = false;
            doc.Delete(sourceId);

            Element target = toToposolid
                ? Toposolid.Create(doc, loops, targetTypeId, levelId)
                : Floor.Create(doc, loops, targetTypeId, levelId);
            OffsetParameter(target)?.Set(offset);
            doc.Regenerate();

            var replay = SlabShape.Replay(doc, target, shape, loops);
            var carry = ParameterCarrier.Apply(target, parameters);
            foreach (var entity in entities) target.SetEntity(entity);
            if (pinned) target.Pinned = true;

            return new ConvertOutcome(target.Id, toToposolid, replay.PointsLost, replay.CreasesLost, carry.Failed);
        }

        /// <summary>The source's sketch, loop by loop, curves as they are. Arcs stay arcs, holes stay holes.</summary>
        private static List<CurveLoop> ReadLoops(Document doc, Element source)
        {
            var sketchId = source switch
            {
                Floor floor => floor.SketchId,
                Toposolid toposolid => toposolid.SketchId,
                _ => ElementId.InvalidElementId,
            };
            if (doc.GetElement(sketchId) is not Sketch sketch)
                throw new InvalidOperationException("it has no sketch to copy");

            double tolerance = doc.Application.VertexTolerance;
            var loops = new List<CurveLoop>();
            foreach (CurveArray array in sketch.Profile)
            {
                var curves = new List<Curve>();
                foreach (Curve curve in array) curves.Add(curve);

                var segments = curves
                    // Qualified: Autodesk.Revit.DB has a Segment of its own.
                    .Select(c => new RVTuk.Core.TopoTools.Segment(
                        SlabShape.ToPoint(c.GetEndPoint(0)), SlabShape.ToPoint(c.GetEndPoint(1))))
                    .ToList();
                var chain = LoopChainer.Chain(segments, tolerance)
                    ?? throw new InvalidOperationException("its sketch has a loop that does not close");

                var loop = new CurveLoop();
                foreach (var link in chain)
                    loop.Append(link.Reversed ? curves[link.Index].CreateReversed() : curves[link.Index]);
                loops.Add(loop);
            }

            if (loops.Count == 0) throw new InvalidOperationException("its sketch is empty");
            return LoopOrder.OuterFirst(loops);
        }

        private static Parameter? OffsetParameter(Element element) =>
            element.get_Parameter(element is Toposolid
                ? BuiltInParameter.TOPOSOLID_HEIGHTABOVELEVEL_PARAM
                : BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);

        /// <summary>
        /// RVTuk's own storage on the source: today the Topo Tools point ledger and the Room Floor
        /// link. The parked-parameter record is left out because <see cref="ParameterCarrier"/>
        /// writes an updated one.
        /// </summary>
        private static IReadOnlyList<Entity> OurEntities(Element source)
        {
            var entities = new List<Entity>();
            foreach (var guid in source.GetEntitySchemaGuids())
            {
                if (guid == ParkedParameterStore.SchemaGuid) continue;

                var schema = Schema.Lookup(guid);
                if (schema == null) continue;
                if (!string.Equals(schema.VendorId, OurVendorId, StringComparison.OrdinalIgnoreCase)) continue;
                if (!schema.ReadAccessGranted() || !schema.WriteAccessGranted()) continue;

                var entity = source.GetEntity(schema);
                if (entity.IsValid()) entities.Add(entity);
            }
            return entities;
        }
    }
}

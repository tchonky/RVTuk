using System.Collections.Generic;
using Autodesk.Revit.DB;
using RVTuk.Core.Shared.Geometry;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// A toposolid with the two things routing needs: its plan footprint and its vertical extent.
    /// Both are read once per run — the footprint costs a sketch traversal and the extent a
    /// bounding box, and a run asks about them once per sampled point.
    /// </summary>
    public sealed class ToposolidTarget
    {
        public ToposolidTarget(
            Toposolid solid, IReadOnlyList<IReadOnlyList<XyPoint>> loops, double minZ, double maxZ)
        {
            Solid = solid;
            Loops = loops;
            MinZ = minZ;
            MaxZ = maxZ;
        }

        public Toposolid Solid { get; }
        public IReadOnlyList<IReadOnlyList<XyPoint>> Loops { get; }
        public double MinZ { get; }
        public double MaxZ { get; }

        public long Id => Solid.Id.Value;
        public string Name => Solid.Name;
    }
}

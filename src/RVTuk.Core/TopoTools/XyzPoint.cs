namespace RVTuk.Core.TopoTools
{
    /// <summary>
    /// A point in model space, in Revit's internal units. Only Topo Tools needs three dimensions —
    /// the shared <see cref="RVTuk.Core.Shared.Geometry.XyPoint"/> stays two-dimensional.
    /// </summary>
    public readonly record struct XyzPoint(double X, double Y, double Z);
}

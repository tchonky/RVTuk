namespace RVTuk.Core.Shared.Geometry
{
    /// <summary>A plain 2D point (view-plane projection — Z is deliberately not represented).</summary>
    public readonly record struct XyPoint(double X, double Y);
}

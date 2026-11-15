namespace RVTuk.Core.RoundPositions
{
    /// <summary>A point in centimetres. Revit hands out decimal feet; GridSnapper converts at the
    /// boundary so no other file has to remember the factor.</summary>
    public readonly record struct GridPoint(double X, double Y, double Z);
}

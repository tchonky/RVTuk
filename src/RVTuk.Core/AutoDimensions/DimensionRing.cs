namespace RVTuk.Core.AutoDimensions
{
    /// <summary>
    /// Which ring of dimension strings a reference line belongs to.
    ///
    /// THE VALUE IS THE PRIORITY. Ownership of an opening is settled by ring first, so Outer
    /// must sort before Inner. Do not renumber these.
    /// </summary>
    public enum DimensionRing
    {
        Outer = 0,
        Inner = 1,
    }
}

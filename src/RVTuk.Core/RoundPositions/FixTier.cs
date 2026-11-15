namespace RVTuk.Core.RoundPositions
{
    /// <summary>
    /// The dependency hierarchy, and the only ordering the tree uses. Each tier can move what sits
    /// below it — a level carries its whole floor, a wall drags its openings — so reading the tree
    /// top to bottom is reading the order it is safe to work in.
    /// </summary>
    public enum FixTier
    {
        BasePoints = 0,
        Levels = 1,
        Datums = 2,
        Walls = 3,
        Framing = 4,
        DraftingLines = 5,
        PointFamilies = 6,
        HostedFamilies = 7,
    }
}

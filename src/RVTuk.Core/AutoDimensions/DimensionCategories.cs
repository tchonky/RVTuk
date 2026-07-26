using System;

namespace RVTuk.Core.AutoDimensions
{
    /// <summary>Which Revit categories participate as dimension references in a run.</summary>
    [Flags]
    public enum DimensionCategories
    {
        None = 0,
        Walls = 1,
        Doors = 2,
        Windows = 4,
        // v2 — host objects needing top/bottom-face resolution. Present so the persisted mask
        // has room for them; CategoryMask.FromMask clamps them off until that logic exists.
        Ceilings = 8,
        Floors = 16,
    }

    /// <summary>Round-trips the category set through the single int persisted per project.</summary>
    public static class CategoryMask
    {
        /// <summary>The categories v1 can actually resolve references for.</summary>
        public const DimensionCategories Supported =
            DimensionCategories.Walls | DimensionCategories.Doors | DimensionCategories.Windows;

        /// <summary>What a project with no persisted selection starts with.</summary>
        public const DimensionCategories Default = Supported;

        public static int ToMask(DimensionCategories categories) => (int)categories;

        /// <summary>
        /// Clamped to <see cref="Supported"/>: a mask carrying v2 or unknown bits (a newer build,
        /// a hand-edited model) must never switch on behaviour this build doesn't implement.
        /// </summary>
        public static DimensionCategories FromMask(int mask) => (DimensionCategories)mask & Supported;
    }
}

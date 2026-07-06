using System;
using System.Collections.Generic;

namespace RVTuk.Core.AreaSubmission
{
    /// <summary>
    /// A 2D point representing a coordinate in the boundary loop.
    /// </summary>
    public class Point2D
    {
        public double X { get; set; }
        public double Y { get; set; }
    }

    /// <summary>
    /// Flags indicating validation errors on an area record.
    /// </summary>
    [Flags]
    public enum AreaError
    {
        None = 0,
        NoUsageCode = 1,
        BadBoundary = 2,
        ZeroArea = 4
    }

    /// <summary>
    /// A record of a measured area within a Revit project, including boundary geometry and metadata.
    /// </summary>
    public class AreaRecord
    {
        /// <summary>Relative level elevation in metres (LEVEL_ELEVATION, e.g. -4.9, 0, 4).</summary>
        public double LevelElevation { get; set; }
        public string? Number { get; set; }
        public string? Name { get; set; }
        /// <summary>Proposed usage code (USAGE_TYPE).</summary>
        public int? UsageCode { get; set; }
        /// <summary>Usage code as existing in the current permit (USAGE_TYPE_OLD); null when the
        /// area is new work with no permit history — emitted as an empty value, never mirrored
        /// from <see cref="UsageCode"/>.</summary>
        public int? UsageCodePrev { get; set; }
        /// <summary>Area as stated in the old permit (the AREA tag) — a manual/historical text
        /// value the robot does not recompute; empty for new work. Read from the RZ_AREA
        /// parameter.</summary>
        public string? PermitArea { get; set; }
        /// <summary>Dwelling-unit / unit number (the ASSET tag) for this specific area, read
        /// from the RZ_ASSET parameter; falls back to the submission-wide
        /// <see cref="AreaSubmissionConfig.Asset"/> when empty.</summary>
        public string? Asset { get; set; }
        public string Floor { get; set; } = "";
        public int PageNo { get; set; }
        /// <summary>The area plan viewport's plot scale (e.g. 100 for 1:100), read from
        /// <c>ViewPlan.Scale</c> at extraction time so the Area Calc window can auto-fill
        /// <see cref="AreaSubmissionConfig.Scale"/> and flag mixed scales across viewports.</summary>
        public int Scale { get; set; }
        public bool IsUnderground { get; set; }
        public double AreaValue { get; set; }
        public List<List<Point2D>> BoundaryLoops { get; set; } = new();
        public AreaError Errors { get; set; }
    }
}

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
        public string Level { get; set; } = "";
        public string? Number { get; set; }
        public string? Name { get; set; }
        public int? UsageCode { get; set; }
        public string Floor { get; set; } = "";
        public int PageNo { get; set; }
        public bool IsUnderground { get; set; }
        public double AreaValue { get; set; }
        public List<List<Point2D>> BoundaryLoops { get; set; } = new();
        public AreaError Errors { get; set; }
    }
}

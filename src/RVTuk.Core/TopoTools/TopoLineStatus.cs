namespace RVTuk.Core.TopoTools
{
    /// <summary>Why a topo line will or will not contribute points on the next run.</summary>
    public enum TopoLineStatus
    {
        /// <summary>Has an elevation, and its points land on a toposolid.</summary>
        Ready,

        /// <summary>On the Topo_Line style but its TOPO_Elevation was never filled in.</summary>
        NoElevation,

        /// <summary>Has an elevation, but no sampled point falls inside any toposolid's footprint.</summary>
        OutsideToposolid,
    }
}

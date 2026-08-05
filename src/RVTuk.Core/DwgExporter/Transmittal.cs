using System.Collections.Generic;

namespace RVTuk.Core.DwgExporter
{
    /// <summary>How a run bundles its output. <see cref="None"/> is 0 so an absent config key
    /// and the out-of-box default agree, the same rule every other DWG export key follows.</summary>
    public enum TransmittalMode
    {
        None = 0,
        /// <summary>One archive for the whole run.</summary>
        OneBundle = 1,
        /// <summary>One archive per drawing, named after it.</summary>
        PerDrawing = 2,
    }

    /// <summary>What a bundled file is, for grouping in the transmittal report.</summary>
    public enum TransmittalKind
    {
        Drawing,
        Image,
        Font,
        Other,
    }

    /// <summary>One file to put in the archive. Entry names are flat: a DWG references its
    /// images and xrefs by bare filename, so flattening is what makes them resolve once the
    /// receiver unzips.</summary>
    public class TransmittalEntry
    {
        public string SourcePath { get; set; } = "";
        public string EntryName { get; set; } = "";
        public TransmittalKind Kind { get; set; }
    }

    /// <summary>A font the export setup maps to, and where it was found — null when it could
    /// not be, which means the receiver has to have it already.</summary>
    public class ResolvedFont
    {
        public string Name { get; set; } = "";
        public string? FilePath { get; set; }
    }

    /// <summary>Everything that goes in the archive, and everything that doesn't but should
    /// be mentioned. <see cref="Warnings"/> is mutable so the caller can add what only it
    /// knows (e.g. that no font folder exists on this machine).</summary>
    public class TransmittalContents
    {
        public IReadOnlyList<TransmittalEntry> Entries { get; set; } = new List<TransmittalEntry>();
        public IReadOnlyList<string> FontsNotIncluded { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>Header facts for the transmittal report.</summary>
    public class TransmittalInfo
    {
        public System.DateTime CreatedUtc { get; set; }
        public string SheetSetName { get; set; } = "";
        public IReadOnlyList<string> ModelTitles { get; set; } = new List<string>();
        public string DwgSetupName { get; set; } = "";
        public string SheetNamingSetupName { get; set; } = "";
        public string ViewNamingSetupName { get; set; } = "";

        /// <summary>Set only for a per-drawing archive, so the report says which one it is.</summary>
        public string DrawingName { get; set; } = "";
    }
}

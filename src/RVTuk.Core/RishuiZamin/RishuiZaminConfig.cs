using System;

namespace RVTuk.Core.RishuiZamin
{
    /// <summary>
    /// Which of the two accepted marker encodings the DXF carries (the tag/value payload is
    /// identical; only the carrier differs — docs/autoarea/rishui-zamin-rules.md §5).
    /// </summary>
    public enum MarkerForm
    {
        /// <summary>Form A — the official spec's encoding (and the Garmoshka sample's): an
        /// <c>RZ_*_SYM</c> block INSERT per polygon with one ATTRIB per tag, closed by SEQEND.
        /// Shown as "Official" in the config UI.</summary>
        FormA,

        /// <summary>Form B — the tekenplus encoding: one plain TEXT per polygon whose content is
        /// <c>KEY=VALUE&amp;&amp;&amp;…</c> pairs. Shown as "Old" in the config UI.</summary>
        FormB,
    }

    /// <summary>
    /// Configuration for area submission export settings.
    /// </summary>
    public class RishuiZaminConfig
    {
        public int BuildingNo { get; set; } = 1;
        public string? Asset { get; set; }
        public int Scale { get; set; } = 100;
        public string OutputFolder { get; set; } = "";
        public string FileBaseName { get; set; } = "";

        /// <summary>Marker encoding for the DXF. Defaults to the official block/ATTRIB form
        /// (Form A); Form B (tekenplus-style TEXT) remains selectable for comparison against
        /// the previously verified output.</summary>
        public MarkerForm MarkerForm { get; set; } = MarkerForm.FormA;

        /// <summary>
        /// The real sheet size in drawing units (cm at 1:1): the Revit sheet's physical
        /// title-block size multiplied by <see cref="Scale"/>. When both are &gt; 0 the RZ_FRAME
        /// is drawn as exactly this rectangle anchored at (0,0) — matching how tekenplus frames
        /// the whole physical sheet — and the extracted, sheet-relative geometry is emitted
        /// untranslated inside it. When 0 (unknown), the frame falls back to the content
        /// bounding box plus a margin.
        /// </summary>
        public double SheetWidthCm { get; set; }
        public double SheetHeightCm { get; set; }
    }
}

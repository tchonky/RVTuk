namespace RVTuk.Core.DwgExporter
{
    /// <summary>
    /// One field of a PDF-export naming rule, resolved against a sheet: the filename is the
    /// concatenation of Prefix + Value + Suffix per part, with Separator inserted between a
    /// part and the next (the last part's separator is unused — Revit's combined-parameter
    /// convention).
    /// </summary>
    public class NamingRulePart
    {
        public string Prefix { get; set; } = "";
        public string Value { get; set; } = "";
        public string Suffix { get; set; } = "";
        public string Separator { get; set; } = "";
    }
}

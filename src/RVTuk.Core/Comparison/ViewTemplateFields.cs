namespace RVTuk.Core.Comparison
{
    /// <summary>Canonical setting keys used in ViewTemplateDto.Settings, shared by the
    /// extractor (RVTuk.Revit), comparer, and scorer so they agree on names.</summary>
    public static class ViewTemplateFields
    {
        public const string Scale = "VIEW_SCALE";
        public const string DetailLevel = "DETAIL_LEVEL";
        public const string Discipline = "DISCIPLINE";
        public const string Phase = "PHASE";
        public const string PhaseFilter = "PHASE_FILTER";
        public const string ViewRange = "VIEW_RANGE";
        public const string DisplayStyle = "DISPLAY_STYLE";
        public const string Underlay = "UNDERLAY";

        // Synthetic fields surfaced in diffs (not stored in Settings).
        public const string VgOverrides = "VG_OVERRIDES";
        public const string Filters = "FILTERS";

        /// <summary>Revit's default detail level; "non-default" counts toward completeness.</summary>
        public const string DefaultDetailLevel = "Coarse";
    }
}

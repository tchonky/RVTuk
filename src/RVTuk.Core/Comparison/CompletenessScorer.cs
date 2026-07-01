using System.Collections.Generic;
using RVTuk.Core.Models.Comparison;

namespace RVTuk.Core.Comparison
{
    /// <summary>Weighted measure (0..1) of how fully a view template is specified.
    /// Higher = controls more of the labor-intensive settings. This informs the
    /// recommendation but never decides on its own, and is NOT a recency claim.</summary>
    public static class CompletenessScorer
    {
        public static double Score(ViewTemplateDto t)
        {
            var settings = t.Settings.ToDictionary();
            double s = 0;

            // V/G overrides defined (the real labor). Extractor leaves hash empty when none.
            if (!string.IsNullOrEmpty(t.CategoryOverridesHash)) s += 0.30;
            // At least one view filter applied.
            if (t.FilterNames.Count > 0) s += 0.25;
            // Phase or view range controlled.
            if (HasValue(settings, ViewTemplateFields.Phase) || HasValue(settings, ViewTemplateFields.ViewRange)) s += 0.15;
            // Non-default detail level.
            if (settings.TryGetValue(ViewTemplateFields.DetailLevel, out var dl)
                && !string.IsNullOrEmpty(dl) && dl != ViewTemplateFields.DefaultDetailLevel) s += 0.10;
            // Scale set.
            if (HasValue(settings, ViewTemplateFields.Scale)) s += 0.10;
            // Discipline set.
            if (HasValue(settings, ViewTemplateFields.Discipline)) s += 0.10;

            return s;
        }

        private static bool HasValue(Dictionary<string, string> d, string key)
            => d.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v);
    }
}

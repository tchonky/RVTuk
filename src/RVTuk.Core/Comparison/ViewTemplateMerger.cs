using System.Collections.Generic;
using System.Linq;
using RVTuk.Core.Models.Comparison;

namespace RVTuk.Core.Comparison
{
    /// <summary>Copies a view template (a deep, independent copy) from a source snapshot into
    /// the Standard's view-template set.</summary>
    public class ViewTemplateMerger : ICategoryMerger
    {
        public string CategoryId => ViewTemplatesSnapshot.Category;

        public MergeResult AcceptIntoStandard(
            StandardSnapshot std, CategorySnapshot source, string itemKey,
            DependencyClosure deps, bool replace)
        {
            var src = (ViewTemplatesSnapshot)source;
            var template = src.Templates.FirstOrDefault(t => Key(t) == itemKey);
            if (template == null)
                return new MergeResult { Applied = false, Conflict = "not found" };

            var target = GetOrCreate(std);
            var existing = target.Templates.FirstOrDefault(t => Key(t) == itemKey);
            if (existing != null)
            {
                if (!replace)
                    return new MergeResult { Applied = false, Conflict = "exists" };
                target.Templates.Remove(existing);
            }

            target.Templates.Add(Clone(template));
            return new MergeResult { Applied = true };
        }

        private static string Key(ViewTemplateDto t) => t.ViewType + "|" + t.Name;

        private static ViewTemplatesSnapshot GetOrCreate(StandardSnapshot std)
        {
            var existing = std.Categories.OfType<ViewTemplatesSnapshot>().FirstOrDefault();
            if (existing != null) return existing;
            var created = new ViewTemplatesSnapshot();
            std.Categories.Add(created);
            return created;
        }

        private static ViewTemplateDto Clone(ViewTemplateDto t) => new ViewTemplateDto
        {
            Name = t.Name,
            UniqueId = t.UniqueId,
            ViewType = t.ViewType,
            CategoryOverridesHash = t.CategoryOverridesHash,
            Included = t.Included.Select(p => new ControlledParam(p.Id, p.Label, p.Controlled)).ToList(),
            Settings = t.Settings.Select(p => new KvPair(p.Key, p.Value)).ToList(),
            FilterNames = new List<string>(t.FilterNames),
        };
    }
}

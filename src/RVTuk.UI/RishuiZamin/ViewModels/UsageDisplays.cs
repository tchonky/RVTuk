using System.Globalization;
using RVTuk.Core.RishuiZamin;

namespace RVTuk.UI.RishuiZamin.ViewModels
{
    /// <summary>One coloured figure in a floor header: a group's m².</summary>
    public sealed class UsageTotalPart
    {
        public UsageTotalPart(UsageGroup group, double m2)
        {
            Group = group;
            Text = m2.ToString("F2", CultureInfo.InvariantCulture);
        }

        public UsageGroup Group { get; }
        public string Text { get; }
    }

    /// <summary>How a picker button shows the selected areas' shared usage: "«code» - «name»" in
    /// the group colour, "— none —", or "(mixed)". <see cref="Group"/> is null for the muted
    /// cases.</summary>
    public sealed class UsageValueDisplay
    {
        private UsageValueDisplay(string text, UsageGroup? group, bool isMixed)
        {
            Text = text;
            Group = group;
            IsMixed = isMixed;
        }

        public string Text { get; }
        public UsageGroup? Group { get; }
        public bool IsMixed { get; }

        public static UsageValueDisplay From(UsageValue value)
        {
            switch (value.Kind)
            {
                case UsageValueKind.Mixed:
                    return new UsageValueDisplay("(mixed)", null, true);
                case UsageValueKind.Code:
                    var code = value.Code!.Value;
                    var name = UsageCatalog.ByCode(code)?.HebrewName;
                    var text = code.ToString(CultureInfo.InvariantCulture) + (name == null ? "" : " - " + name);
                    return new UsageValueDisplay(text, UsageGroups.OfCode(code), false);
                default:
                    return new UsageValueDisplay("— none —", null, false);
            }
        }
    }
}

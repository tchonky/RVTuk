using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using RVTuk.Core.RishuiZamin;

namespace RVTuk.UI.RishuiZamin.Views
{
    /// <summary>A <see cref="UsageGroup"/> (or null) → its brush. The brushes are set in XAML
    /// from the theme, so the converter needs no resource lookup.</summary>
    public sealed class UsageGroupBrushConverter : IValueConverter
    {
        public Brush? Primary { get; set; }
        public Brush? Service { get; set; }
        public Brush? Other { get; set; }
        public Brush? Process { get; set; }
        public Brush? NoCode { get; set; }

        /// <summary>For null (no shared value): "— none —" and "(mixed)".</summary>
        public Brush? Muted { get; set; }

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value switch
            {
                UsageGroup.Primary => Primary,
                UsageGroup.Service => Service,
                UsageGroup.Other => Other,
                UsageGroup.Process => Process,
                UsageGroup.NoCode => NoCode,
                _ => Muted,
            };

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}

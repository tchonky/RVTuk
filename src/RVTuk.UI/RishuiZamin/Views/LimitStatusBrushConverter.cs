using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using RVTuk.Core.RishuiZamin;

namespace RVTuk.UI.RishuiZamin.Views
{
    /// <summary>A <see cref="LimitStatus"/> → its brush; the brushes are set in XAML from the
    /// theme.</summary>
    public sealed class LimitStatusBrushConverter : IValueConverter
    {
        public Brush? Ok { get; set; }
        public Brush? Close { get; set; }
        public Brush? Over { get; set; }
        public Brush? NotChecked { get; set; }

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value switch
            {
                LimitStatus.Ok => Ok,
                LimitStatus.Close => Close,
                LimitStatus.Over => Over,
                _ => NotChecked,
            };

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RVTuk.UI.Converters
{
    /// <summary>Collapses when the bound count is zero (or not an int); visible otherwise. Used
    /// for count badges (e.g. the Areas tab's flagged-row badge) that should only show up when
    /// there's something to report.</summary>
    public class CountToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is int count && count > 0 ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}

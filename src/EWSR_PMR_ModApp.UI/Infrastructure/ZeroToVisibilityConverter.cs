using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace EWSR_PMR_ModApp.UI.Infrastructure;

/// <summary>int == 0 → Visible; int > 0 → Collapsed  (for "empty state" panels)</summary>
[ValueConversion(typeof(int), typeof(Visibility))]
public sealed class ZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int n && n == 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

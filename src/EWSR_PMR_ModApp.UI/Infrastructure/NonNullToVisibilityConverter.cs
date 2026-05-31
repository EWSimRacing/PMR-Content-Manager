using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace EWSR_PMR_ModApp.UI.Infrastructure;

/// <summary>null/empty string → Collapsed; non-empty → Visible</summary>
[ValueConversion(typeof(string), typeof(Visibility))]
public sealed class NonNullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => !string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

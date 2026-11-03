using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Nexus.App.Converters;

public class EqualityToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null && parameter == null) return Visibility.Visible;
        if (value == null || parameter == null) return Visibility.Collapsed;

        bool equals = string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);
        return equals ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

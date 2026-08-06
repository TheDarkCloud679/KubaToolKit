using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace KubaToolKit.Modules.AtlassianSearch;

// Opposite of the built-in BooleanToVisibilityConverter -- used to show a
// free-text input exactly when a select-type input (driven by the same
// bool) is hidden, e.g. a required transition field with no known
// allowed values.
public sealed class InverseBooleanToVisibilityConverter
    : IValueConverter
{
    public object
    Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object
    ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) =>
        throw new NotSupportedException();
}

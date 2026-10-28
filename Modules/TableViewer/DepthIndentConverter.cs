using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace KubaToolKit.Modules.TableViewer;

// Turns a decoded field's nesting depth into a left margin, so struct and
// array contents read as an indented tree inside the results grid.
public class DepthIndentConverter : IValueConverter
{
    public object
    Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        var depth = value is int i ? i : 0;

        return new Thickness(depth * 14, 0, 0, 0);
    }

    public object
    ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) =>
        throw new NotSupportedException();
}

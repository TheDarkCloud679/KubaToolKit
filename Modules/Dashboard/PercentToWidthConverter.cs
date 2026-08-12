using System.Globalization;
using System.Windows.Data;

namespace KubaToolKit.Modules.Dashboard;

// Turns a 0-100 percent into a pixel width for a load mini-bar's fill --
// ConverterParameter is the bar's own total width (its track), so the
// fill never exceeds it regardless of how the percent is bound.
public sealed class PercentToWidthConverter
    : IValueConverter
{
    public object
    Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        var percent = value is double d ? d : 0;
        var trackWidth = parameter is string s && double.TryParse(s, CultureInfo.InvariantCulture, out var w) ? w : 60;

        return Math.Clamp(percent, 0, 100) / 100.0 * trackWidth;
    }

    public object
    ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) =>
        throw new NotSupportedException();
}

using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace KubaToolKit.Modules.AtlassianSearch;

// This site's priorities are named P0-P5ish -- red is reserved for the
// genuinely urgent tier (P0/P1/P2), everything else is blue. Missing
// priority (not every issue/request type carries one) is its own
// neutral gray rather than either color, so "no priority" doesn't read
// as "low priority".
internal static class JiraPriorityColors
{
    private static readonly Color HighColor = Color.FromRgb(0xE0, 0x43, 0x43);
    private static readonly Color LowColor = Color.FromRgb(0x2F, 0x6F, 0xED);
    private static readonly Color NeutralColor = Color.FromRgb(0x68, 0x70, 0x7E);

    private static readonly string[] HighUrgencyPrefixes = { "P0", "P1", "P2" };

    public static Color
    Get(
        string? priority)
    {
        if (string.IsNullOrWhiteSpace(priority) || string.Equals(priority, "No priority", StringComparison.OrdinalIgnoreCase))
        {
            return NeutralColor;
        }

        var trimmed = priority.Trim();

        var isHighUrgency =
            HighUrgencyPrefixes.Any(prefix => trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        return isHighUrgency ? HighColor : LowColor;
    }
}

public sealed class JiraPriorityBadgeBackgroundConverter
    : IValueConverter
{
    public object
    Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        var color = JiraPriorityColors.Get(value as string);

        var brush = new SolidColorBrush(Color.FromArgb(36, color.R, color.G, color.B));
        brush.Freeze();

        return brush;
    }

    public object
    ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class JiraPriorityBadgeForegroundConverter
    : IValueConverter
{
    public object
    Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        var brush = new SolidColorBrush(JiraPriorityColors.Get(value as string));
        brush.Freeze();

        return brush;
    }

    public object
    ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) =>
        throw new NotSupportedException();
}

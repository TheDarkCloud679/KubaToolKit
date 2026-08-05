using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace KubaToolKit.Modules.AtlassianSearch;

// Priority names and their urgency order are per-site (an admin can
// rename, add, or reorder them), so the gradient position comes from
// whatever the site's own /rest/api/3/priority order says rather than a
// hardcoded name list -- index 0 is that endpoint's own "most urgent"
// convention. Set once after the priorities are fetched; a badge for a
// name outside the known order (stale settings, race on first load)
// just falls back to neutral gray.
internal static class JiraPriorityColors
{
    public static List<string> Order { get; set; } = new();

    private static readonly Color HighColor = Color.FromRgb(0xE0, 0x43, 0x43);
    private static readonly Color LowColor = Color.FromRgb(0x2F, 0x6F, 0xED);
    private static readonly Color NeutralColor = Color.FromRgb(0x68, 0x70, 0x7E);

    public static Color
    Get(
        string? priority)
    {
        if (string.IsNullOrWhiteSpace(priority) || Order.Count == 0)
        {
            return NeutralColor;
        }

        var index = Order.FindIndex(p => string.Equals(p, priority, StringComparison.OrdinalIgnoreCase));

        if (index < 0)
        {
            return NeutralColor;
        }

        var fraction = Order.Count <= 1 ? 0.0 : (double)index / (Order.Count - 1);

        return Color.FromRgb(
            (byte)(HighColor.R + (LowColor.R - HighColor.R) * fraction),
            (byte)(HighColor.G + (LowColor.G - HighColor.G) * fraction),
            (byte)(HighColor.B + (LowColor.B - HighColor.B) * fraction));
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

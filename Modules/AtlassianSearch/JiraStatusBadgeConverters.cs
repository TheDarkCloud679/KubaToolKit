using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace KubaToolKit.Modules.AtlassianSearch;

// Status names/workflows are entirely custom per site, so there's no name
// to color mapping that would generalize -- keyed off Jira's own three
// built-in status categories instead ("new"/"indeterminate"/"done"),
// which every status, however it's named, is required to belong to. Set
// once after GetJiraStatusCategories loads; a status outside the known
// map (stale settings, race on first load) falls back to neutral gray.
internal static class JiraStatusColors
{
    public static Dictionary<string, string> CategoryByStatus { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Color DoneColor = Color.FromRgb(0x2E, 0xA0, 0x4A);
    private static readonly Color InProgressColor = Color.FromRgb(0x2F, 0x6F, 0xED);
    private static readonly Color ToDoColor = Color.FromRgb(0x8A, 0x91, 0x9E);
    private static readonly Color LightGrayColor = Color.FromRgb(0xB8, 0xBD, 0xC7);
    private static readonly Color NeutralColor = Color.FromRgb(0x68, 0x70, 0x7E);

    // Jira's own category ("new"/"indeterminate"/"done") lumps almost
    // every non-final status into a single "in progress" bucket -- this
    // instance's workflow (per the shared "Incident life cycle" diagram)
    // wants a finer split than that for two specific statuses, so those
    // are called out by name and checked before falling back to category.
    private static readonly Dictionary<string, Color> NameOverrides =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Résolu"] = DoneColor,
            ["Clôturé"] = DoneColor,
            ["Consigné"] = ToDoColor,
            ["Assigné"] = LightGrayColor
        };

    public static Color
    Get(
        string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return NeutralColor;
        }

        if (NameOverrides.TryGetValue(status, out var overrideColor))
        {
            return overrideColor;
        }

        if (!CategoryByStatus.TryGetValue(status, out var categoryKey))
        {
            return NeutralColor;
        }

        return categoryKey switch
        {
            "done" => DoneColor,
            "indeterminate" => InProgressColor,
            "new" => ToDoColor,
            _ => NeutralColor
        };
    }
}

public sealed class JiraStatusBadgeBackgroundConverter
    : IValueConverter
{
    public object
    Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        var color = JiraStatusColors.Get(value as string);

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

public sealed class JiraStatusBadgeForegroundConverter
    : IValueConverter
{
    public object
    Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        var brush = new SolidColorBrush(JiraStatusColors.Get(value as string));
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

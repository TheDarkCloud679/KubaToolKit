using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace KubaToolKit.Modules.AtlassianSearch;

// Badge for a comment's visibility on a Service Management issue --
// green "Public" (visible to the customer) vs gray "Internal".
internal static class CommentVisibilityColors
{
    public static readonly Color PublicColor = Color.FromRgb(0x00, 0x87, 0x5A);
    public static readonly Color InternalColor = Color.FromRgb(0x68, 0x70, 0x7E);
}

public sealed class CommentVisibilityBackgroundConverter
    : IValueConverter
{
    public object
    Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        var color = value is true ? CommentVisibilityColors.PublicColor : CommentVisibilityColors.InternalColor;

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

public sealed class CommentVisibilityForegroundConverter
    : IValueConverter
{
    public object
    Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        var color = value is true ? CommentVisibilityColors.PublicColor : CommentVisibilityColors.InternalColor;

        var brush = new SolidColorBrush(color);
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

public sealed class CommentVisibilityTextConverter
    : IValueConverter
{
    public object
    Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) =>
        value is true ? "Public" : "Internal";

    public object
    ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) =>
        throw new NotSupportedException();
}

// A comment's author name reduced to its first letter, for the small
// avatar circle next to each comment -- upper-cased since a real avatar
// image never comes back from Jira's comment API here, only the name.
public sealed class InitialLetterConverter
    : IValueConverter
{
    public object
    Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) =>
        value is string { Length: > 0 } name ? name[..1].ToUpper(culture) : "?";

    public object
    ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) =>
        throw new NotSupportedException();
}

using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace KubaToolKit.Modules.ApiClient;

/// Couleur associée à chaque méthode HTTP pour les badges de
/// l'arborescence des collections (repère visuel façon Postman : GET en
/// vert, DELETE en rouge...). Réutilise les teintes déjà définies dans
/// Styles/Colors.xaml (Success/Warning/Accent/Danger) pour rester
/// cohérent avec le reste de l'appli, complétées de deux teintes propres
/// aux méthodes qu'aucune de ces quatre ne couvrait (PATCH, HEAD).
internal static class HttpMethodColors
{
    private static readonly Color GetColor = Color.FromRgb(0x1F, 0xA9, 0x71);
    private static readonly Color PostColor = Color.FromRgb(0xF2, 0xA9, 0x3B);
    private static readonly Color PutColor = Color.FromRgb(0x2F, 0x6F, 0xED);
    private static readonly Color PatchColor = Color.FromRgb(0x17, 0xA2, 0xB8);
    private static readonly Color DeleteColor = Color.FromRgb(0xE5, 0x48, 0x4D);
    private static readonly Color HeadColor = Color.FromRgb(0x8B, 0x5C, 0xF6);
    private static readonly Color NeutralColor = Color.FromRgb(0x68, 0x70, 0x7E);

    public static Color
    Get(
        string? method) =>
        method?.Trim().ToUpperInvariant() switch
        {
            "GET" => GetColor,
            "POST" => PostColor,
            "PUT" => PutColor,
            "PATCH" => PatchColor,
            "DELETE" => DeleteColor,
            "HEAD" or "OPTIONS" => HeadColor,
            _ => NeutralColor
        };
}

/// Fond du badge : la couleur de la méthode en très faible opacité,
/// pour rester lisible avec le texte de la méthode par-dessus en plein.
public sealed class HttpMethodBadgeBackgroundConverter
    : IValueConverter
{
    public object
    Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        var color = HttpMethodColors.Get(value as string);

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

public sealed class HttpMethodBadgeForegroundConverter
    : IValueConverter
{
    public object
    Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        var brush = new SolidColorBrush(HttpMethodColors.Get(value as string));
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

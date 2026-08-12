using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace KubaToolKit.Shell;

// Best-effort visual flag for which AWS profile is selected -- picking the
// wrong one in an ops tool that can start/stop instances is exactly the
// kind of mistake a glance-level warning helps prevent. A user-assigned
// color (via ProfileColorPickerWindow) always wins; profiles nobody's
// picked a color for fall back to a naming-convention guess (substring,
// case-insensitive, since AWS profile names carry no metadata of their
// own to key off instead).
public sealed class ProfileRiskBrushConverter
    : IValueConverter
{
    // Populated once at startup (and refreshed after the picker saves) --
    // kept static since converters are instantiated by XAML, not
    // constructed with dependencies.
    public static Dictionary<string, string> Overrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Same hex values as Styles/Colors.xaml's Danger/Warning/Success --
    // kept as a local copy since converters run outside the XAML resource
    // system (same tradeoff MetricColorHelper already makes).
    private static readonly SolidColorBrush DangerBrush = Freeze(0xE5, 0x48, 0x4D);
    private static readonly SolidColorBrush WarningBrush = Freeze(0xF2, 0xA9, 0x3B);
    private static readonly SolidColorBrush SuccessBrush = Freeze(0x1E, 0x9E, 0x6B);
    private static readonly SolidColorBrush NeutralBrush = Freeze(0x8B, 0x8F, 0x98);

    public object
    Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        var name = (value as string ?? "").ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(name))
        {
            return Brushes.Transparent;
        }

        if (Overrides.TryGetValue(name, out var hex) && !string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                return Freeze((Color)ColorConverter.ConvertFromString(hex));
            }
            catch (Exception)
            {
                // Falls through to the naming guess below -- a corrupt
                // stored value shouldn't take the whole dropdown down.
            }
        }

        if (name.Contains("prod") || name.Contains("prd"))
        {
            return DangerBrush;
        }

        if (name.Contains("stag") || name.Contains("preprod") || name.Contains("pprd") || name.Contains("uat") || name.Contains("recette"))
        {
            return WarningBrush;
        }

        if (name.Contains("dev") || name.Contains("test") || name.Contains("sandbox") || name.Contains("sbx"))
        {
            return SuccessBrush;
        }

        return NeutralBrush;
    }

    public object
    ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) =>
        throw new NotSupportedException();

    private static SolidColorBrush
    Freeze(
        byte r,
        byte g,
        byte b) =>
        Freeze(Color.FromRgb(r, g, b));

    private static SolidColorBrush
    Freeze(
        Color color)
    {
        var brush = new SolidColorBrush(color);

        brush.Freeze();

        return brush;
    }
}

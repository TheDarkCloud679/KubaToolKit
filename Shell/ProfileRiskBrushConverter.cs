using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace KubaToolKit.Shell;

// Best-effort visual flag for which AWS profile is selected -- picking the
// wrong one in an ops tool that can start/stop instances is exactly the
// kind of mistake a glance-level warning helps prevent, so profiles whose
// name suggests production get a red dot, staging-like ones amber, dev/
// test-like ones green, and anything else a neutral dot. Matches by
// common naming convention (substring, case-insensitive) since AWS
// profile names carry no metadata of their own to key off instead.
public sealed class ProfileRiskBrushConverter
    : IValueConverter
{
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
        byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));

        brush.Freeze();

        return brush;
    }
}

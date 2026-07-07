using System.Windows.Media;

namespace KubaToolKit.Shared.Services;

/// Dégradé bleu (charge faible) -> rouge (charge élevée) utilisé pour
/// colorer les cellules de métriques du Dashboard.
public static class MetricColorHelper
{
    private static readonly Color LowColor = Color.FromRgb(0x2F, 0x6F, 0xED);
    private static readonly Color HighColor = Color.FromRgb(0xE5, 0x48, 0x4D);

    public static Brush?
    GetLoadBrush(
        double? ratio,
        double opacity = 0.20)
    {
        if (!ratio.HasValue)
        {
            return null;
        }

        double clamped =
            Math.Clamp(ratio.Value, 0, 1);

        byte r =
            (byte)(LowColor.R + (HighColor.R - LowColor.R) * clamped);

        byte g =
            (byte)(LowColor.G + (HighColor.G - LowColor.G) * clamped);

        byte b =
            (byte)(LowColor.B + (HighColor.B - LowColor.B) * clamped);

        byte a =
            (byte)(opacity * 255);

        var brush =
            new SolidColorBrush(
                Color.FromArgb(a, r, g, b));

        brush.Freeze();

        return brush;
    }
}

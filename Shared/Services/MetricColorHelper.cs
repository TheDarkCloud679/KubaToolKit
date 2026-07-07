using System.Windows.Media;

namespace KubaToolKit.Shared.Services;

/// Dégradé bleu (charge faible) -> rouge (charge élevée) utilisé pour
/// colorer les cellules de métriques du Dashboard.
public static class MetricColorHelper
{
    private static readonly Color LowColor = Color.FromRgb(0x2F, 0x6F, 0xED);
    private static readonly Color HighColor = Color.FromRgb(0xE5, 0x48, 0x4D);
    private static readonly Color SuccessColor = Color.FromRgb(0x1F, 0xA9, 0x71);
    private static readonly Color DangerColor = Color.FromRgb(0xE5, 0x48, 0x4D);
    private static readonly Color WarningColor = Color.FromRgb(0xF2, 0xA9, 0x3B);

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

        return ToBrush(r, g, b, opacity);
    }

    /// Colore un statut d'instance (RDS, etc.) : vert pour disponible,
    /// rouge pour arrêtée/en échec, orange pour les états transitoires.
    public static Brush?
    GetStatusBrush(
        string? status,
        double opacity = 0.20)
    {
        var normalized =
            status?.Trim().ToLowerInvariant()
            ?? "";

        Color? color = normalized switch
        {
            "available" => SuccessColor,

            "stopped"
                or "failed"
                or "incompatible-restore"
                or "storage-full"
                or "incompatible-parameters" => DangerColor,

            "starting"
                or "stopping"
                or "backing-up"
                or "modifying"
                or "rebooting"
                or "upgrading"
                or "maintenance"
                or "configuring-enhanced-monitoring" => WarningColor,

            _ => null
        };

        if (color == null)
        {
            return null;
        }

        return ToBrush(
            color.Value.R,
            color.Value.G,
            color.Value.B,
            opacity);
    }

    private static Brush
    ToBrush(
        byte r,
        byte g,
        byte b,
        double opacity)
    {
        byte a =
            (byte)(opacity * 255);

        var brush =
            new SolidColorBrush(
                Color.FromArgb(a, r, g, b));

        brush.Freeze();

        return brush;
    }
}

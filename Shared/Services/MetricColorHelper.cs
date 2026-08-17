using System.Windows.Media;

namespace KubaToolKit.Shared.Services;

public static class MetricColorHelper
{
    // Matches Styles/Colors.xaml's SuccessColor/WarningColor/DangerColor --
    // kept as a separate C# copy since this runs outside the XAML resource
    // system, but the values need to stay identical to it.
    private static readonly Color SuccessColor = Color.FromRgb(0x1E, 0x9E, 0x6B);
    private static readonly Color DangerColor = Color.FromRgb(0xE5, 0x48, 0x4D);
    private static readonly Color WarningColor = Color.FromRgb(0xF2, 0xA9, 0x3B);

    // Discrete traffic-light bands rather than a continuous blend between
    // two colors: a continuous blend's midpoint reads as neither "fine"
    // nor "attention" -- exactly what made the old blue-to-red load
    // gradient's medium-load purple ambiguous.
    private static Color?
    GetLoadColor(
        double? ratio)
    {
        if (!ratio.HasValue)
        {
            return null;
        }

        var clamped = Math.Clamp(ratio.Value, 0, 1);

        return clamped switch
        {
            < 0.5 => SuccessColor,
            < 0.8 => WarningColor,
            _ => DangerColor
        };
    }

    public static Brush?
    GetLoadBrush(
        double? ratio,
        double opacity = 0.20)
    {
        var color = GetLoadColor(ratio);

        return color.HasValue
            ? ToBrush(color.Value.R, color.Value.G, color.Value.B, opacity)
            : null;
    }

    // Solid (not translucent) severity color for the load mini-bar's fill
    // and percentage text, which sit on a neutral track rather than
    // needing to blend into the page background the way the soft pill/
    // text backgrounds above do.
    public static Brush?
    GetLoadAccentBrush(
        double? ratio)
    {
        var color = GetLoadColor(ratio);

        if (!color.HasValue)
        {
            return null;
        }

        var brush = new SolidColorBrush(color.Value);

        brush.Freeze();

        return brush;
    }

    private static Color?
    GetStatusColor(
        string? status)
    {
        var normalized =
            status?.Trim().ToLowerInvariant()
            ?? "";

        return normalized switch
        {
            "available" or "running" or "succeeded" => SuccessColor,

            "stopped"
                or "terminated"
                or "failed"
                or "incompatible-restore"
                or "storage-full"
                or "incompatible-parameters"
                or "timed_out"
                or "aborted" => DangerColor,

            "starting"
                or "stopping"
                or "shutting-down"
                or "pending"
                or "backing-up"
                or "modifying"
                or "rebooting"
                or "upgrading"
                or "maintenance"
                or "configuring-enhanced-monitoring"
                or "pending_redrive" => WarningColor,

            _ => null
        };
    }

    public static Brush?
    GetStatusBrush(
        string? status,
        double opacity = 0.20)
    {
        var color = GetStatusColor(status);

        return color.HasValue
            ? ToBrush(color.Value.R, color.Value.G, color.Value.B, opacity)
            : null;
    }

    // Solid dot/text color to pair with GetStatusBrush's soft background
    // in the pill-with-dot status badges.
    public static Brush?
    GetStatusAccentBrush(
        string? status)
    {
        var color = GetStatusColor(status);

        if (!color.HasValue)
        {
            return null;
        }

        var brush = new SolidColorBrush(color.Value);

        brush.Freeze();

        return brush;
    }

    // Step Functions history event types are compound PascalCase names
    // (TaskFailed, ExecutionSucceeded, MapStateEntered...), not the single
    // words GetStatusColor matches, so this checks suffixes/substrings
    // instead of the whole normalized string. Only flags the outcomes
    // worth a glance (succeeded / failed / aborted / timed out) -- the
    // many Entered/Exited/Scheduled/Started housekeeping events stay
    // neutral rather than forcing every event into a color.
    private static Color?
    GetStepFunctionsEventColor(
        string? eventType)
    {
        if (string.IsNullOrEmpty(eventType))
        {
            return null;
        }

        if (eventType.EndsWith("Succeeded", StringComparison.OrdinalIgnoreCase))
        {
            return SuccessColor;
        }

        if (eventType.EndsWith("Failed", StringComparison.OrdinalIgnoreCase)
            || eventType.Contains("Aborted", StringComparison.OrdinalIgnoreCase)
            || eventType.Contains("TimedOut", StringComparison.OrdinalIgnoreCase))
        {
            return DangerColor;
        }

        return null;
    }

    public static Brush?
    GetStepFunctionsEventBrush(
        string? eventType,
        double opacity = 0.20)
    {
        var color = GetStepFunctionsEventColor(eventType);

        return color.HasValue
            ? ToBrush(color.Value.R, color.Value.G, color.Value.B, opacity)
            : null;
    }

    public static Brush?
    GetStepFunctionsEventAccentBrush(
        string? eventType)
    {
        var color = GetStepFunctionsEventColor(eventType);

        if (!color.HasValue)
        {
            return null;
        }

        var brush = new SolidColorBrush(color.Value);

        brush.Freeze();

        return brush;
    }

    public static Brush?
    GetHttpStatusBrush(
        int statusCode,
        double opacity = 0.20)
    {
        Color? color = statusCode switch
        {
            >= 200 and < 300 => SuccessColor,
            >= 300 and < 400 => WarningColor,
            >= 400 => DangerColor,
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

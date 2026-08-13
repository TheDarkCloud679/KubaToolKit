using System.Globalization;
using System.Windows.Data;

namespace KubaToolKit.Shared.Converters;

// Splits a local "yyyy-MM-dd HH:mm:ss[.fff]" timestamp into the two pieces
// results grids show separately: ConverterParameter "date" for the small
// line above, anything else for the bold time line. Tolerates both the
// millisecond-precision format CloudWatch Logs Insights returns and the
// second-precision format CloudTrail returns.
public sealed class TimestampPartConverter
    : IValueConverter
{
    private static readonly string[]
        Formats =
        {
            "yyyy-MM-dd HH:mm:ss.fff",
            "yyyy-MM-dd HH:mm:ss"
        };

    public object
    Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var raw = value as string ?? "";

        if (!DateTime.TryParseExact(
                raw,
                Formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return raw;
        }

        return parameter as string == "date"
            ? parsed.ToString("dd/MM", CultureInfo.InvariantCulture)
            : parsed.ToString(raw.Contains('.') ? "HH:mm:ss.fff" : "HH:mm:ss", CultureInfo.InvariantCulture);
    }

    public object
    ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

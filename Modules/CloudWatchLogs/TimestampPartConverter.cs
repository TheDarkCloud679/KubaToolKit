using System.Globalization;
using System.Windows.Data;

namespace KubaToolKit.Modules.CloudWatchLogs;

// Splits the "yyyy-MM-dd HH:mm:ss.fff" local timestamp (already converted
// from UTC in CloudWatchService) into the two pieces the results grid
// shows separately: ConverterParameter "date" for the small line above,
// anything else for the bold time line.
public sealed class TimestampPartConverter
    : IValueConverter
{
    public object
    Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var raw = value as string ?? "";

        if (!DateTime.TryParseExact(
                raw,
                "yyyy-MM-dd HH:mm:ss.fff",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return raw;
        }

        return parameter as string == "date"
            ? parsed.ToString("dd/MM", CultureInfo.InvariantCulture)
            : parsed.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
    }

    public object
    ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

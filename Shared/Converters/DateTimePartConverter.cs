using System.Globalization;
using System.Windows.Data;

namespace KubaToolKit.Shared.Converters;

// Same split as TimestampPartConverter (date above, time below in results
// grids) but for values that are already a DateTime rather than a
// formatted string -- e.g. S3ObjectItem.LastModified.
public sealed class DateTimePartConverter
    : IValueConverter
{
    public object
    Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTime dateTime)
        {
            return "";
        }

        return parameter as string == "date"
            ? dateTime.ToString("dd/MM", CultureInfo.InvariantCulture)
            : dateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }

    public object
    ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

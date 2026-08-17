using System.Globalization;
using System.Windows.Data;

namespace KubaToolKit.Modules.Sqs;

// Drives the Available/In Flight badge coloring in SqsView: past a
// threshold (100 by default, overridable via ConverterParameter) a queue
// backlog is worth flagging rather than blending into every other count.
public sealed class MessageCountThresholdConverter
    : IValueConverter
{
    public object
    Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var count =
            value is int i ? i : 0;

        var threshold =
            parameter is string s && int.TryParse(s, out var t) ? t : 100;

        return count >= threshold;
    }

    public object
    ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

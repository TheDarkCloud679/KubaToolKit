using System.Globalization;
using System.Windows.Data;

namespace KubaToolKit.Modules.CloudWatchLogs;

// CloudWatch Logs Insights' @log field comes back as "<accountId>:<logGroupName>"
// (e.g. "767397756354:/aws/lambda/foo") -- the account id prefix is just noise
// for display, everyone already knows which account they searched.
public sealed class LogGroupNameConverter
    : IValueConverter
{
    public object
    Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var raw = value as string ?? "";

        var separatorIndex = raw.IndexOf(':');

        return separatorIndex >= 0 ? raw[(separatorIndex + 1)..] : raw;
    }

    public object
    ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

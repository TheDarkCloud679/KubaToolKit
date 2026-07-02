using KubaToolKit.Infrastructure;
using System.Windows.Controls;

namespace KubaToolKit.Modules.CloudWatchLogs;

public class CloudWatchLogsModule : IToolModule
{
    public CloudWatchLogsView TypedView { get; } = new();

    public string Name => "CloudWatch";

    public UserControl View => TypedView;
}

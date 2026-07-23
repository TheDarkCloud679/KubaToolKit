using KubaToolKit.Shared.Services;
using System.Windows.Media;

namespace KubaToolKit.Modules.Dashboard.Models;

public class Ec2DiskUsage
{
    public string InstanceId { get; set; } = "";
    public string InstanceName { get; set; } = "";
    public string MountPath { get; set; } = "";
    public double UsedPercent { get; set; }

    public string UsedPercentDisplay =>
        $"{UsedPercent:F1} %";

    public Brush? UsedPercentBackground =>
        MetricColorHelper.GetLoadBrush(UsedPercent / 100.0);
}

using KubaToolKit.Shared.Services;
using System.Windows.Media;

namespace KubaToolKit.Modules.Dashboard.Models;

public class Ec2MetricItem
{
    public string InstanceId { get; set; } = "";
    public string Name { get; set; } = "";
    public string InstanceType { get; set; } = "";
    public string State { get; set; } = "";
    public string AutoStart { get; set; } = "—";
    public string AutoStop { get; set; } = "—";

    // The worst mount point found for this instance's disk usage.
    public double? DiskPercent { get; set; }

    public string DiskDisplay =>
        DiskPercent.HasValue
            ? $"{DiskPercent.Value:F0} %"
            : "—";

    public Brush? DiskBackground =>
        MetricColorHelper.GetLoadBrush(
            DiskPercent.HasValue
                ? DiskPercent.Value / 100.0
                : (double?)null);

    public Brush? StateBackground =>
        MetricColorHelper.GetStatusBrush(State);
}

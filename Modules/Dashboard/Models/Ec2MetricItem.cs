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

    private double? DiskRatio =>
        DiskPercent.HasValue ? DiskPercent.Value / 100.0 : (double?)null;

    public double DiskBarPercent =>
        Math.Clamp((DiskRatio ?? 0) * 100, 0, 100);

    public Brush? DiskBackground =>
        MetricColorHelper.GetLoadBrush(DiskRatio);

    public Brush? DiskAccentBrush =>
        MetricColorHelper.GetLoadAccentBrush(DiskRatio);

    public Brush? StateBackground =>
        MetricColorHelper.GetStatusBrush(State);

    public Brush? StateAccentBrush =>
        MetricColorHelper.GetStatusAccentBrush(State);
}

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

    // Filled in by the "Disk report" button (the worst mount point found
    // for this instance), not on every dashboard refresh -- scanning every
    // mount point of every instance is too slow to run automatically.
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

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

    public Brush? StateBackground =>
        MetricColorHelper.GetStatusBrush(State);
}

using KubaToolKit.Shared.Services;
using System.Windows.Media;

namespace KubaToolKit.Modules.Dashboard.Models;

public class RdsMetricItem
{
    private const double ActivityScaleMax = 50;

    public string Identifier { get; set; } = "";
    public string Engine { get; set; } = "";
    public string Status { get; set; } = "";
    public double? CpuPercent { get; set; }
    public double? DatabaseConnections { get; set; }
    public string AutoStart { get; set; } = "—";
    public string AutoStop { get; set; } = "—";

    public string CpuDisplay =>
        CpuPercent.HasValue
            ? $"{CpuPercent.Value:F1} %"
            : "N/A";

    public string ActivityDisplay =>
        DatabaseConnections.HasValue
            ? $"{DatabaseConnections.Value:F0} sessions"
            : "N/A";

    public Brush? CpuBackground =>
        MetricColorHelper.GetLoadBrush(CpuRatio);

    public Brush? ActivityBackground =>
        MetricColorHelper.GetLoadBrush(ActivityRatio);

    public Brush? StatusBackground =>
        MetricColorHelper.GetStatusBrush(Status);

    private double? CpuRatio =>
        CpuPercent.HasValue ? CpuPercent.Value / 100.0 : (double?)null;

    private double? ActivityRatio =>
        DatabaseConnections.HasValue ? DatabaseConnections.Value / ActivityScaleMax : (double?)null;

    // 0-100 scale for both, regardless of each metric's own natural
    // range (CPU is already a percent; sessions are scaled against
    // ActivityScaleMax) -- lets the mini load-bar's width binding stay
    // the same for both columns.
    public double CpuBarPercent =>
        Math.Clamp((CpuRatio ?? 0) * 100, 0, 100);

    public double ActivityBarPercent =>
        Math.Clamp((ActivityRatio ?? 0) * 100, 0, 100);

    public Brush? CpuAccentBrush =>
        MetricColorHelper.GetLoadAccentBrush(CpuRatio);

    public Brush? ActivityAccentBrush =>
        MetricColorHelper.GetLoadAccentBrush(ActivityRatio);

    public Brush? StatusAccentBrush =>
        MetricColorHelper.GetStatusAccentBrush(Status);
}

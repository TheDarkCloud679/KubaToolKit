using KubaToolKit.Shared.Services;
using System.Windows.Media;

namespace KubaToolKit.Modules.Dashboard.Models;

public class RdsMetricItem
{
    // Repère approximatif (pas de vraie limite par instance sans lire
    // max_connections) au-delà duquel Activity est considérée "chargée".
    private const double ActivityScaleMax = 50;

    public string Identifier { get; set; } = "";
    public string Engine { get; set; } = "";
    public string Status { get; set; } = "";
    public double? CpuPercent { get; set; }
    public double? DatabaseConnections { get; set; }

    public string CpuDisplay =>
        CpuPercent.HasValue
            ? $"{CpuPercent.Value:F1} %"
            : "N/A";

    public string ActivityDisplay =>
        DatabaseConnections.HasValue
            ? $"{DatabaseConnections.Value:F0} sessions"
            : "N/A";

    public Brush? CpuBackground =>
        MetricColorHelper.GetLoadBrush(
            CpuPercent.HasValue
                ? CpuPercent.Value / 100.0
                : (double?)null);

    public Brush? ActivityBackground =>
        MetricColorHelper.GetLoadBrush(
            DatabaseConnections.HasValue
                ? DatabaseConnections.Value / ActivityScaleMax
                : (double?)null);
}

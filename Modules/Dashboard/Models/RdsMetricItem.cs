namespace KubaToolKit.Modules.Dashboard.Models;

public class RdsMetricItem
{
    public string Identifier { get; set; } = "";
    public string Engine { get; set; } = "";
    public string Status { get; set; } = "";
    public double? CpuPercent { get; set; }
    public double? DatabaseConnections { get; set; }

    public string CpuDisplay =>
        CpuPercent.HasValue
            ? $"{CpuPercent.Value:F1} %"
            : "N/A";

    public string ConnectionsDisplay =>
        DatabaseConnections.HasValue
            ? $"{DatabaseConnections.Value:F0}"
            : "N/A";
}

using Amazon.CloudWatch.Model;
using System.Windows.Media;

namespace KubaToolKit.Modules.Dashboard;

/// Décrit une courbe à tracer dans MetricChartWindow : d'où vient la
/// donnée CloudWatch et comment l'afficher.
public class ChartSeriesRequest
{
    public string Namespace { get; set; } = "";
    public string MetricName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Unit { get; set; } = "";
    public List<Dimension> Dimensions { get; set; } = new();
    public Color Color { get; set; }
}

using KubaToolKit.Shared.Services;
using System.Windows.Media;

namespace KubaToolKit.Modules.StepFunctions.Models;

public class ExecutionItem
{
    public string Name { get; set; } = "";
    public string Arn { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime? StartDate { get; set; }
    public DateTime? StopDate { get; set; }

    public string StartDisplay =>
        StartDate.HasValue
            ? StartDate.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            : "";

    public string StopDisplay =>
        StopDate.HasValue
            ? StopDate.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            : "";

    public Brush? StatusBackground => MetricColorHelper.GetStatusBrush(Status);
}

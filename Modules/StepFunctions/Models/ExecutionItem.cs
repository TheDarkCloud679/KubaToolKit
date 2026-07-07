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

    // Renseigné uniquement pour les exécutions EXPRESS reconstruites via
    // CloudWatch Logs : indique à ExecutionEventsWindow d'aller chercher
    // l'historique dans ces mêmes logs plutôt que via GetExecutionHistory
    // (non supporté par les state machines EXPRESS).
    public string? LogGroupIdentifier { get; set; }

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

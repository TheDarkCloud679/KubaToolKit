namespace KubaToolKit.Modules.StepFunctions.Models;

public class HistoryEventItem
{
    public long Id { get; set; }
    public string Type { get; set; } = "";
    public string Step { get; set; } = "";
    public string Resource { get; set; } = "";
    public DateTime? Timestamp { get; set; }

    public string DetailsJson { get; set; } = "{}";

    public string TimestampDisplay =>
        Timestamp.HasValue
            ? Timestamp.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff")
            : "";
}

namespace KubaToolKit.Modules.CloudWatchLogs.Models;

public class LogEntry
{
    public string Timestamp { get; set; } = "";
    public string LogGroup { get; set; } = "";
    public string Message { get; set; } = "";
}
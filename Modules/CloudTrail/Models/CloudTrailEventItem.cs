namespace KubaToolKit.Modules.CloudTrail.Models;

public class CloudTrailEventItem
{
    public string Timestamp { get; set; } = "";
    public string EventName { get; set; } = "";
    public string Username { get; set; } = "";
    public string Resources { get; set; } = "";
    public string EventSource { get; set; } = "";
    public string CloudTrailEventJson { get; set; } = "";
}

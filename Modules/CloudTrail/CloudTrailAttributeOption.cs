namespace KubaToolKit.Modules.CloudTrail;

public class CloudTrailAttributeOption
{
    public string Display { get; set; } = "";

    public string Key { get; set; } = "";

    public override string ToString() => Display;

    public static List<CloudTrailAttributeOption> All =>
        new()
        {
            new() { Display = "All events", Key = "" },
            new() { Display = "Event name", Key = "EventName" },
            new() { Display = "User name", Key = "Username" },
            new() { Display = "Resource name", Key = "ResourceName" },
            new() { Display = "Resource type", Key = "ResourceType" },
            new() { Display = "Event source", Key = "EventSource" },
            new() { Display = "Access key ID", Key = "AccessKeyId" },
            new() { Display = "Read only", Key = "ReadOnly" },
        };
}

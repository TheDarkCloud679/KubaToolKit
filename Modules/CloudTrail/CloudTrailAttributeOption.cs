namespace KubaToolKit.Modules.CloudTrail;

/// Une entrée du sélecteur "Attribute" du Shell : l'intitulé affiché et la
/// clé LookupAttributeKey (API CloudTrail LookupEvents) qu'elle représente.
/// Key = "" signifie "pas de filtre", c'est-à-dire tous les évènements de
/// la plage horaire choisie.
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

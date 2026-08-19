using System.Text.Json.Serialization;

namespace KubaToolKit.Modules.AtlassianSearch.Models;

public class IncidentEntry
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Solution { get; set; } = "";
    public List<IncidentLink> Links { get; set; } = new();

    // Not persisted as a field of its own JSON -- it's the path of the
    // file the entry was loaded from/should be saved to, same role as
    // CollectionNode.FilePath in the API Client module.
    [JsonIgnore]
    public string? FilePath { get; set; }
}

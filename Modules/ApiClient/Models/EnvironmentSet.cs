namespace KubaToolKit.Modules.ApiClient.Models;

public class EnvironmentSet
{
    public string Name { get; set; } = "";

    public string FilePath { get; set; } = "";

    public List<HeaderItem> Variables { get; set; } = new();

    // The request used by the "Get Token" toolbar button, saved from
    // whatever's currently loaded in the editor when the user clicks the
    // configure (gear) icon. Null until configured for this environment.
    public CollectionNode? TokenRequestConfig { get; set; }

    public Dictionary<string, string> ToSubstitutionMap() =>
        Variables
            .Where(v => v.Enabled && !string.IsNullOrWhiteSpace(v.Key))
            .GroupBy(v => v.Key)
            .ToDictionary(g => g.Key, g => g.Last().Value);
}

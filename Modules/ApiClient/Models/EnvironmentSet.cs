namespace KubaToolKit.Modules.ApiClient.Models;

public class EnvironmentSet
{
    public string Name { get; set; } = "";

    public string FilePath { get; set; } = "";

    public List<HeaderItem> Variables { get; set; } = new();

    public Dictionary<string, string> ToSubstitutionMap() =>
        Variables
            .Where(v => v.Enabled && !string.IsNullOrWhiteSpace(v.Key))
            .GroupBy(v => v.Key)
            .ToDictionary(g => g.Key, g => g.Last().Value);
}

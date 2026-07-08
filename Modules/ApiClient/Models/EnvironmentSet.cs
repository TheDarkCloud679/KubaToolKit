namespace KubaToolKit.Modules.ApiClient.Models;

public class EnvironmentSet
{
    public string Name { get; set; } = "";
    public Dictionary<string, string> Variables { get; set; } = new();
}

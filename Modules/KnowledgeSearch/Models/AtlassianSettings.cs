namespace KubaToolKit.Modules.KnowledgeSearch.Models;

public class AtlassianSettings
{
    public string BaseUrl { get; set; } = "";
    public string Email { get; set; } = "";
    public string ApiToken { get; set; } = "";

    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(BaseUrl)
        && !string.IsNullOrWhiteSpace(Email)
        && !string.IsNullOrWhiteSpace(ApiToken);
}

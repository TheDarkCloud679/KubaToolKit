namespace KubaToolKit.Modules.AtlassianSearch.Models;

public class AtlassianSettings
{
    public string BaseUrl { get; set; } = "";
    public string Email { get; set; } = "";
    public string ApiToken { get; set; } = "";

    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(BaseUrl)
        && !string.IsNullOrWhiteSpace(Email)
        && !string.IsNullOrWhiteSpace(ApiToken);

    // Confluence space keys starred from the search view -- when set, the
    // Space dropdown starts narrowed to just these instead of every space
    // on the site (typing still searches everything).
    public List<string> FavoriteConfluenceSpaceKeys { get; set; } = new();

    public List<SavedJiraStatsFilter> SavedJiraStatsFilters { get; set; } = new();

    // Empty = the incident Library stays on this machine only (the usual
    // %AppData% folder). Pointed at a folder a cloud-sync client (Google
    // Drive, OneDrive...) already keeps in sync, the whole team reads and
    // writes the same incidents automatically -- no more Export/Import.
    public string SharedLibraryFolder { get; set; } = "";
}

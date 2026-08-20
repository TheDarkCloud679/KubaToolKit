namespace KubaToolKit.Modules.AtlassianSearch.Models;

// One row of a Jira/Confluence keyword search -- not persisted, just the
// display shape for one hit. Shared between AttachLinkWindow (search to
// link into an incident, hence IsAttached) and the Search tab's free
// browsing (which ignores IsAttached).
public class AtlassianResultItem
{
    public IncidentLinkType Type { get; set; } = IncidentLinkType.Jira;
    public bool IsJira => Type == IncidentLinkType.Jira;
    public bool IsConfluence => Type == IncidentLinkType.Confluence;

    public string Key { get; set; } = "";
    public string Project { get; set; } = "";
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Priority { get; set; } = "";
    public string Status { get; set; } = "";
    public string PageId { get; set; } = "";
    public string Space { get; set; } = "";
    public string Url { get; set; } = "";

    // Raw ISO date string (Jira's "updated", Confluence's "lastModified"),
    // parsed once into SortDate/DisplayDate for filtering/sorting.
    public string DateRaw { get; set; } = "";
    public DateTime? SortDate { get; set; }
    public string DisplayDate { get; set; } = "";

    public bool IsAttached { get; set; }
}

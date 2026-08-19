namespace KubaToolKit.Modules.AtlassianSearch.Models;

public enum IncidentLinkType
{
    Jira,
    Confluence
}

// A Jira ticket or Confluence page attached to an IncidentEntry. Only the
// fields needed to display the row and re-open the item are kept -- the
// live content (description, comments, status transitions...) is always
// fetched fresh via JiraIssueViewerWindow/ConfluencePageViewerWindow when
// the user opens it, never cached here.
public class IncidentLink
{
    public IncidentLinkType Type { get; set; } = IncidentLinkType.Jira;

    // Jira only.
    public string Key { get; set; } = "";
    public string Project { get; set; } = "";
    public string Priority { get; set; } = "";
    public string Status { get; set; } = "";

    // Confluence only.
    public string PageId { get; set; } = "";
    public string Space { get; set; } = "";

    public string Title { get; set; } = "";
    public string Url { get; set; } = "";

    // Snapshot of the item's own last-updated/modified date at the moment
    // it was linked (raw ISO string) -- same "captured once, never
    // refreshed" philosophy as Priority/Status above, used to filter/sort
    // the linked-items list without an API round trip just to render it.
    public string Date { get; set; } = "";
}

namespace KubaToolKit.Modules.AtlassianSearch.Models;

public class JiraSearchResult
{
    public string Key { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Project { get; set; } = "";
    public string Reporter { get; set; } = "";
    public string Assignee { get; set; } = "";
    public string Priority { get; set; } = "";
    public string Status { get; set; } = "";
    public string UpdatedDisplay { get; set; } = "";
    public string Url { get; set; } = "";

    // Popout-window-only: flagged when a refresh finds this issue wasn't
    // present in the previous fetch. Not INotifyPropertyChanged -- the
    // popout calls DataGrid.Items.Refresh() after mutating it, which is
    // enough to re-evaluate a RowStyle trigger bound to it.
    public bool IsUnread { get; set; }
}

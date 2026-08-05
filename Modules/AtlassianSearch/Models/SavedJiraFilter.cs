namespace KubaToolKit.Modules.AtlassianSearch.Models;

// A named snapshot of the Jira filter row (e.g. "Unassigned > P2"), so a
// combination the user checks often doesn't need re-picking every time.
public class SavedJiraFilter
{
    public string Name { get; set; } = "";
    public string Query { get; set; } = "";
    public string Project { get; set; } = "";
    public string Reporter { get; set; } = "";
    public bool AssigneeIsUnassigned { get; set; }
    public string Assignee { get; set; } = "";
    public string Priority { get; set; } = "";

    // One of "=", ">", ">=", "<", "<=" -- JQL supports relative
    // comparison on priority since it's one of Jira's orderable fields.
    public string PriorityOperator { get; set; } = "=";

    public string Status { get; set; } = "";
}

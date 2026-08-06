namespace KubaToolKit.Modules.AtlassianSearch.Models;

// A named snapshot of the Jira filter row (e.g. "My urgent bugs"), so a
// combination the user checks often doesn't need re-picking every time.
public class SavedJiraFilter
{
    public string Name { get; set; } = "";
    public string Query { get; set; } = "";

    public string Project { get; set; } = "";
    public string ProjectOperator { get; set; } = "=";

    public string Reporter { get; set; } = "";
    public string ReporterOperator { get; set; } = "=";

    public string Assignee { get; set; } = "";
    public string AssigneeOperator { get; set; } = "=";

    public string Priority { get; set; } = "";
    public string PriorityOperator { get; set; } = "=";

    public string Status { get; set; } = "";
    public string StatusOperator { get; set; } = "=";
}

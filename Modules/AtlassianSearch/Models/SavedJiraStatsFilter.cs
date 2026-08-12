namespace KubaToolKit.Modules.AtlassianSearch.Models;

// A named snapshot of the Stats filter row -- same idea as SavedJiraFilter,
// just for the "how many did X resolve in period Y" query instead of a
// plain issue search.
public class SavedJiraStatsFilter
{
    public string Name { get; set; } = "";

    public string Project { get; set; } = "";
    public string ProjectOperator { get; set; } = "=";

    public string Assignee { get; set; } = "";
    public string AssigneeOperator { get; set; } = "=";

    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

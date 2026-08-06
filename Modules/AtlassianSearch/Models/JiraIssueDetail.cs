namespace KubaToolKit.Modules.AtlassianSearch.Models;

public class JiraIssueDetail
{
    public string Key { get; set; } = "";
    public string Summary { get; set; } = "";
    public string DescriptionHtml { get; set; } = "";
    public string Priority { get; set; } = "";
    public string Status { get; set; } = "";
    public string Reporter { get; set; } = "";
    public string Assignee { get; set; } = "";
    public string AssigneeAccountId { get; set; } = "";
    public string ProjectKey { get; set; } = "";
    public string Url { get; set; } = "";
}

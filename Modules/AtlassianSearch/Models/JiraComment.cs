namespace KubaToolKit.Modules.AtlassianSearch.Models;

public class JiraComment
{
    public string Author { get; set; } = "";
    public string Created { get; set; } = "";
    public string Body { get; set; } = "";

    // Only meaningful (and only ever set to false) for Service Management
    // issues -- "jsdPublic" isn't present at all on a comment that lives
    // on a plain Jira issue, so HasVisibilityInfo tells the UI whether to
    // show an internal/public badge at all.
    public bool IsPublic { get; set; } = true;
    public bool HasVisibilityInfo { get; set; }
}

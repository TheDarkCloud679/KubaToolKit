namespace KubaToolKit.Modules.AtlassianSearch.Models;

public class JiraTransition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    // Comment is common enough (and structurally different -- it goes
    // through "update.comment", not a plain field write) to get its own
    // flag; anything else the transition's screen requires (Resolution,
    // or an instance-specific custom field) is discovered generically.
    public bool RequiresComment { get; set; }
    public List<JiraRequiredField> RequiredFields { get; set; } = new();
}

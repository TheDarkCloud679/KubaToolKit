namespace KubaToolKit.Modules.AtlassianSearch.Models;

public class JiraTransition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    // Some workflows require a comment on specific transitions (e.g.
    // "Resolved" needing a resolution note) -- the transition screen's
    // own field requirements say so, not anything guessable from the name.
    public bool RequiresComment { get; set; }
}

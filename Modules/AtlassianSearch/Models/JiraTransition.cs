namespace KubaToolKit.Modules.AtlassianSearch.Models;

public class JiraTransition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    // Some workflows require a comment and/or a resolution on specific
    // transitions (e.g. "Resolved" needing a note and a resolution type)
    // -- the transition screen's own field requirements say so, not
    // anything guessable from the name.
    public bool RequiresComment { get; set; }
    public bool RequiresResolution { get; set; }
    public List<NameValue> ResolutionOptions { get; set; } = new();
}

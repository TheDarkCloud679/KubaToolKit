namespace KubaToolKit.Modules.Wiki.Models;

// The whole wiki, shared across every project/profile -- WikiSection.Folder
// is what used to be handled by having one WikiProject per project key.
public class WikiLibrary
{
    public List<WikiSection> Sections { get; set; } = new();
}

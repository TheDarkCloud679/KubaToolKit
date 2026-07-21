namespace KubaToolKit.Modules.Wiki.Models;

public class WikiProject
{
    public string Key { get; set; } = "";

    public List<WikiSection> Sections { get; set; } = new();
}

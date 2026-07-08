namespace KubaToolKit.Modules.ApiClient.Models;

public class CollectionNode
{
    public string Name { get; set; } = "";
    public bool IsRequest { get; set; }
    public string Method { get; set; } = "";
    public string Url { get; set; } = "";
    public List<HeaderItem> Headers { get; set; } = new();
    public string Body { get; set; } = "";
    public List<CollectionNode> Children { get; set; } = new();

    public string DisplayText =>
        IsRequest ? $"{Method}  {Name}" : Name;
}

using System.Collections.ObjectModel;

namespace KubaToolKit.Modules.ApiClient.Models;

public class CollectionNode
{
    public string Name { get; set; } = "";
    public bool IsRequest { get; set; }
    public string Method { get; set; } = "";
    public string Url { get; set; } = "";
    public List<HeaderItem> Headers { get; set; } = new();
    public string Body { get; set; } = "";

    public string BodyMode { get; set; } = "raw";
    public List<HeaderItem> BodyFormData { get; set; } = new();

    public List<HeaderItem> PostResponseExtractions { get; set; } = new();

    public AuthConfig Auth { get; set; } = new();

    public bool IsFavorite { get; set; }

    public bool IsFavoritesFolder { get; set; }

    public ObservableCollection<CollectionNode> Children { get; set; } = new();

    public string? FilePath { get; set; }

    public CollectionNode? Parent { get; set; }

    public string DisplayText =>
        (IsFavorite ? "⭐ " : "") + (IsRequest ? $"{Method}  {Name}" : Name);

    public CollectionNode
    GetRoot()
    {
        var node = this;

        while (node.Parent != null)
        {
            node = node.Parent;
        }

        return node;
    }

    public AuthConfig
    ResolveEffectiveAuth()
    {
        var node = this;

        while (node != null)
        {
            if (node.Auth.Type != AuthType.Inherit)
            {
                return node.Auth;
            }

            node = node.Parent;
        }

        return new AuthConfig { Type = AuthType.None };
    }
}

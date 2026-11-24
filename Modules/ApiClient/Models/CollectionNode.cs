using System.Collections.ObjectModel;

namespace KubaToolKit.Modules.ApiClient.Models;

public class CollectionNode
{
    public string Name { get; set; } = "";
    public bool IsRequest { get; set; }
    public string Method { get; set; } = "";
    public string Url { get; set; } = "";
    public List<HeaderItem> Headers { get; set; } = new();

    // Postman's ":name" path segments (e.g. /accounts/:accountId), as
    // opposed to {{var}} variables -- a separate mechanism Postman
    // collections commonly use, with its own value per request, not tied
    // to any environment.
    public List<HeaderItem> PathVariables { get; set; } = new();
    public string Body { get; set; } = "";

    public string BodyMode { get; set; } = "raw";
    public List<HeaderItem> BodyFormData { get; set; } = new();

    public List<HeaderItem> PostResponseExtractions { get; set; } = new();

    public AuthConfig Auth { get; set; } = new();

    // Only meaningful on a folder/collection-root node -- headers merged
    // into every request underneath at send time (see
    // ResolveInheritedDefaultHeaders), the same way Auth already
    // inherits via ResolveEffectiveAuth. A plain request node leaves this
    // empty; its own Headers list is unaffected and unrelated.
    public List<HeaderItem> DefaultHeaders { get; set; } = new();

    public bool IsFavorite { get; set; }

    public bool IsFavoritesFolder { get; set; }

    public ObservableCollection<CollectionNode> Children { get; set; } = new();

    public string? FilePath { get; set; }

    public CollectionNode? Parent { get; set; }

    // Set by CollectionStorageService.LoadCollections() when two or more
    // loaded collection root files share the same name (commonly a sync
    // tool's conflict copy) -- otherwise identical-looking, separate
    // trees in the UI with no way to tell which is which, or that
    // changes made on one (like default headers) don't apply to the
    // other.
    public bool HasDuplicateName { get; set; }

    public string TreeLabel =>
        Name
        + (HasDuplicateName && FilePath != null
            ? $"  ({System.IO.Path.GetFileName(FilePath)})"
            : "");

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

    // Called on a request's parent folder: walks from the collection
    // root down to this node, merging every ancestor's DefaultHeaders by
    // key -- a closer folder overrides a farther one for the same name.
    // The request's own headers are merged on top of this result by the
    // caller, always winning (see ApiClientView.ResolveHeadersForSend).
    public List<HeaderItem>
    ResolveInheritedDefaultHeaders()
    {
        var chain = new List<CollectionNode>();
        var node = this;

        while (node != null)
        {
            chain.Add(node);
            node = node.Parent;
        }

        chain.Reverse();

        var merged = new Dictionary<string, HeaderItem>(StringComparer.OrdinalIgnoreCase);

        foreach (var ancestor in chain)
        {
            foreach (var header in ancestor.DefaultHeaders)
            {
                if (header.Enabled && !string.IsNullOrWhiteSpace(header.Key))
                {
                    merged[header.Key] = header;
                }
            }
        }

        return merged.Values.ToList();
    }

    // A deep copy of this node's own request -- used when a new
    // environment is created from a template environment's "Get Token"
    // config, so the new one starts with the exact same auth method
    // (URL, body shape, auth type). Copied as-is, not blanked: a
    // properly-built token request references {{variables}} for whatever
    // actually differs per environment (credentials, URLs), which live
    // in the new environment's own (blank) Variables, not here.
    public CollectionNode
    Clone() =>
        new()
        {
            Name = Name,
            IsRequest = IsRequest,
            Method = Method,
            Url = Url,
            Headers = Headers.Select(h => new HeaderItem { Enabled = h.Enabled, Key = h.Key, Value = h.Value }).ToList(),
            PathVariables = PathVariables.Select(v => new HeaderItem { Enabled = v.Enabled, Key = v.Key, Value = v.Value }).ToList(),
            Body = Body,
            BodyMode = BodyMode,
            BodyFormData = BodyFormData.Select(f => new HeaderItem { Enabled = f.Enabled, Key = f.Key, Value = f.Value }).ToList(),
            PostResponseExtractions = PostResponseExtractions.Select(e => new HeaderItem { Enabled = e.Enabled, Key = e.Key, Value = e.Value }).ToList(),
            Auth =
                new AuthConfig
                {
                    Type = Auth.Type,
                    BearerToken = Auth.BearerToken,
                    Username = Auth.Username,
                    Password = Auth.Password,
                    ApiKeyName = Auth.ApiKeyName,
                    ApiKeyValue = Auth.ApiKeyValue
                }
        };
}

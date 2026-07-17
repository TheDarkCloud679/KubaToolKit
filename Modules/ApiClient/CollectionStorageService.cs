using KubaToolKit.Modules.ApiClient.Models;
using KubaToolKit.Shared.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KubaToolKit.Modules.ApiClient;

public class CollectionStorageService
{
    public static string RootFolder =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KubaToolKit",
            "ApiClient");

    public static string CollectionsFolder =>
        Path.Combine(RootFolder, "Collections");

    public static string EnvironmentsFolder =>
        Path.Combine(RootFolder, "Environments");

    public static string ValueLabelsFile =>
        Path.Combine(RootFolder, "ValueLabels.json");

    public void
    EnsureFoldersExist()
    {
        bool isFirstRun = !Directory.Exists(RootFolder);

        Directory.CreateDirectory(CollectionsFolder);
        Directory.CreateDirectory(EnvironmentsFolder);

        if (isFirstRun)
        {
            WriteReadme();
        }
    }

    private void
    WriteReadme()
    {
        try
        {
            File.WriteAllText(
                Path.Combine(RootFolder, "README.txt"),
                """
                KubaToolKit - API Client
                =========================

                This folder is not in the tool's git repo: your
                collections/environments stay local to this machine.

                Collections\   -> Postman "Collection v2.1" export
                                   (right-click a collection > Export)
                Environments\  -> Postman environment export
                                   (eye icon > ... > Export)

                Drop the exported .json files directly into these two
                folders, then click "Reload" (⟳) in the API Client tab.
                The {{key}} variables of a selected environment are
                substituted in the URL, headers, and body when sending.
                """);
        }
        catch
        {
        }
    }

    public List<CollectionNode>
    LoadCollections()
    {
        EnsureFoldersExist();

        var roots = new List<CollectionNode>();

        foreach (var file in Directory
                     .GetFiles(CollectionsFolder, "*.json")
                     .OrderBy(f => f))
        {
            try
            {
                var json = File.ReadAllText(file);

                var collection =
                    JsonSerializer.Deserialize<PostmanCollection>(json);

                if (collection == null)
                {
                    continue;
                }

                var root =
                    new CollectionNode
                    {
                        Name =
                            string.IsNullOrWhiteSpace(collection.Info?.Name)
                                ? Path.GetFileNameWithoutExtension(file)
                                : collection.Info!.Name,
                        IsRequest = false,
                        FilePath = file,
                        Auth = ParseAuth(collection.Auth)
                    };

                root.Children = BuildNodes(collection.Item, root);

                roots.Add(root);
            }
            catch (JsonException)
            {
            }
        }

        return roots;
    }

    private ObservableCollection<CollectionNode>
    BuildNodes(
        List<PostmanItem>? items,
        CollectionNode parent)
    {
        var nodes = new ObservableCollection<CollectionNode>();

        if (items == null)
        {
            return nodes;
        }

        foreach (var item in items)
        {
            if (item.Request != null)
            {
                var body = item.Request.Body;

                var bodyMode =
                    body == null
                        ? "none"
                        : body.Mode switch
                        {
                            "urlencoded" => "urlencoded",
                            "formdata" => "formdata",
                            _ => "raw"
                        };

                var bodyFormData =
                    bodyMode switch
                    {
                        "urlencoded" =>
                            body?.UrlEncoded?
                                .Where(p => !p.Disabled)
                                .Select(p => new HeaderItem
                                {
                                    Enabled = true,
                                    Key = p.Key,
                                    Value = p.Value
                                })
                                .ToList()
                            ?? new List<HeaderItem>(),

                        "formdata" =>
                            body?.FormData?
                                .Where(p => !p.Disabled)
                                .Select(p => new HeaderItem
                                {
                                    Enabled = true,
                                    Key = p.Key,
                                    Value = p.Value
                                })
                                .ToList()
                            ?? new List<HeaderItem>(),

                        _ => new List<HeaderItem>()
                    };

                nodes.Add(
                    new CollectionNode
                    {
                        Name = item.Name,
                        IsRequest = true,
                        Method = item.Request.Method ?? "GET",
                        Url = ExtractUrl(item.Request.Url),
                        Parent = parent,

                        Headers =
                            item.Request.Header?
                                .Where(h => !h.Disabled)
                                .Select(h => new HeaderItem
                                {
                                    Enabled = true,
                                    Key = h.Key,
                                    Value = h.Value
                                })
                                .ToList()
                            ?? new List<HeaderItem>(),

                        Body = body?.Raw ?? "",
                        BodyMode = bodyMode,
                        BodyFormData = bodyFormData,
                        IsFavorite = item.Request.Favorite == true,
                        Auth = ParseAuth(item.Request.Auth),

                        PostResponseExtractions =
                            item.Request.Extract?
                                .Select(h => new HeaderItem
                                {
                                    Enabled = !h.Disabled,
                                    Key = h.Key,
                                    Value = h.Value
                                })
                                .ToList()
                            ?? new List<HeaderItem>()
                    });
            }
            else if (item.Item != null)
            {
                var folder =
                    new CollectionNode
                    {
                        Name = item.Name,
                        IsRequest = false,
                        Parent = parent,
                        Auth = ParseAuth(item.Auth)
                    };

                folder.Children = BuildNodes(item.Item, folder);

                nodes.Add(folder);
            }
        }

        return nodes;
    }

    private string
    ExtractUrl(
        JsonElement url)
    {
        if (url.ValueKind == JsonValueKind.String)
        {
            return url.GetString() ?? "";
        }

        if (url.ValueKind == JsonValueKind.Object
            && url.TryGetProperty("raw", out var raw))
        {
            return raw.GetString() ?? "";
        }

        return "";
    }

    private static AuthConfig
    ParseAuth(
        PostmanAuth? auth)
    {
        if (auth == null)
        {
            return new AuthConfig { Type = AuthType.Inherit };
        }

        static string? Find(List<PostmanAuthAttribute>? attributes, string key) =>
            attributes?.FirstOrDefault(a => a.Key == key)?.Value;

        return auth.Type switch
        {
            "bearer" => new AuthConfig
            {
                Type = AuthType.Bearer,
                BearerToken = Find(auth.Bearer, "token") ?? ""
            },

            "basic" => new AuthConfig
            {
                Type = AuthType.Basic,
                Username = Find(auth.Basic, "username") ?? "",
                Password = Find(auth.Basic, "password") ?? ""
            },

            "apikey" => new AuthConfig
            {
                Type = AuthType.ApiKey,
                ApiKeyName = Find(auth.ApiKey, "key") ?? "",
                ApiKeyValue = Find(auth.ApiKey, "value") ?? ""
            },

            "noauth" => new AuthConfig { Type = AuthType.None },

            _ => new AuthConfig { Type = AuthType.Inherit }
        };
    }

    public List<EnvironmentSet>
    LoadEnvironments()
    {
        EnsureFoldersExist();

        var environments = new List<EnvironmentSet>();

        foreach (var file in Directory
                     .GetFiles(EnvironmentsFolder, "*.json")
                     .OrderBy(f => f))
        {
            try
            {
                var json = File.ReadAllText(file);

                var env =
                    JsonSerializer.Deserialize<PostmanEnvironment>(json);

                if (env == null)
                {
                    continue;
                }

                environments.Add(
                    new EnvironmentSet
                    {
                        Name =
                            string.IsNullOrWhiteSpace(env.Name)
                                ? Path.GetFileNameWithoutExtension(file)
                                : env.Name,

                        FilePath = file,

                        Variables =
                            env.Values?
                                .Select(v => new HeaderItem
                                {
                                    Enabled = v.Enabled,
                                    Key = v.Key,
                                    Value = v.Value
                                })
                                .ToList()
                            ?? new List<HeaderItem>()
                    });
            }
            catch (JsonException)
            {
            }
        }

        return environments;
    }

    public string? LastValueLabelsError { get; private set; }

    public Dictionary<string, Dictionary<string, string>>
    LoadValueLabels()
    {
        EnsureFoldersExist();

        LastValueLabelsError = null;

        if (!File.Exists(ValueLabelsFile))
        {
            try
            {
                File.WriteAllText(
                    ValueLabelsFile,
                    """
                    {
                      "_comment": "Maps technical codes to readable labels, shown in Cards view as 'Label (code)'. One entry per JSON field of the response (exact name as it appears in the JSON, e.g. providerCode), with the label to display for each observed code. This _comment key is ignored.",
                      "providerCode": {
                        "9": "Limoges"
                      },
                      "externalCode": {
                        "600": "Subscription"
                      }
                    }
                    """);
            }
            catch
            {
            }
        }

        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var json = File.ReadAllText(ValueLabelsFile);

            var documentOptions =
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                };

            if (JsonNode.Parse(json, documentOptions: documentOptions) is not JsonObject root)
            {
                LastValueLabelsError = "The file does not contain a valid JSON object.";

                return result;
            }

            foreach (var (field, node) in root)
            {
                if (node is not JsonObject codes)
                {
                    continue;
                }

                var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var (code, value) in codes)
                {
                    if (value != null)
                    {
                        labels[code] = value.ToString();
                    }
                }

                result[field] = labels;
            }
        }
        catch (Exception ex)
        {
            LastValueLabelsError = ex.Message;

            Logger.Error("CollectionStorageService: failed to load ValueLabels.json.", ex);
        }

        return result;
    }

    public void
    SaveEnvironment(
        EnvironmentSet environment)
    {
        JsonObject root;

        if (File.Exists(environment.FilePath))
        {
            root =
                JsonNode.Parse(File.ReadAllText(environment.FilePath))
                    as JsonObject
                ?? new JsonObject();
        }
        else
        {
            root = new JsonObject();
        }

        root["name"] = environment.Name;

        var values = new JsonArray();

        foreach (var variable in environment.Variables)
        {
            if (string.IsNullOrWhiteSpace(variable.Key))
            {
                continue;
            }

            values.Add(
                new JsonObject
                {
                    ["key"] = variable.Key,
                    ["value"] = variable.Value,
                    ["enabled"] = variable.Enabled
                });
        }

        root["values"] = values;

        File.WriteAllText(
            environment.FilePath,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public string
    CreateCollection(
        string name)
    {
        EnsureFoldersExist();

        var fileName = MakeUniqueFileName(SanitizeFileName(name));
        var filePath = Path.Combine(CollectionsFolder, fileName);

        var root =
            new JsonObject
            {
                ["info"] = new JsonObject
                {
                    ["name"] = name,
                    ["schema"] = "https://schema.getpostman.com/json/collection/v2.1.0/collection.json"
                },
                ["item"] = new JsonArray()
            };

        File.WriteAllText(
            filePath,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        return filePath;
    }

    public void
    DeleteCollectionFile(
        string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    public void
    SaveCollection(
        CollectionNode root)
    {
        if (string.IsNullOrEmpty(root.FilePath))
        {
            throw new InvalidOperationException(
                "This collection is not associated with any file.");
        }

        JsonObject fileRoot;

        if (File.Exists(root.FilePath))
        {
            fileRoot =
                JsonNode.Parse(File.ReadAllText(root.FilePath))
                    as JsonObject
                ?? new JsonObject();
        }
        else
        {
            fileRoot = new JsonObject();
        }

        var info = fileRoot["info"] as JsonObject ?? new JsonObject();

        info["name"] = root.Name;

        info["schema"] ??=
            "https://schema.getpostman.com/json/collection/v2.1.0/collection.json";

        fileRoot["info"] = info;
        fileRoot["item"] = BuildItemArray(root.Children);

        var authNode = BuildAuthNode(root.Auth);

        if (authNode != null)
        {
            fileRoot["auth"] = authNode;
        }
        else
        {
            fileRoot.Remove("auth");
        }

        File.WriteAllText(
            root.FilePath,
            fileRoot.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static JsonArray
    BuildItemArray(
        IEnumerable<CollectionNode> nodes)
    {
        var array = new JsonArray();

        foreach (var node in nodes)
        {
            if (node.IsFavoritesFolder)
            {
                continue;
            }

            if (node.IsRequest)
            {
                var request =
                    new JsonObject
                    {
                        ["method"] = node.Method,
                        ["url"] = node.Url,

                        ["header"] =
                            new JsonArray(
                                node.Headers
                                    .Select(h => (JsonNode)new JsonObject
                                    {
                                        ["key"] = h.Key,
                                        ["value"] = h.Value,
                                        ["disabled"] = !h.Enabled
                                    })
                                    .ToArray())
                    };

                var bodyNode = BuildBodyNode(node);

                if (bodyNode != null)
                {
                    request["body"] = bodyNode;
                }

                if (node.IsFavorite)
                {
                    request["_kubatoolkit_favorite"] = true;
                }

                var extractions =
                    node.PostResponseExtractions
                        .Where(h => !string.IsNullOrWhiteSpace(h.Key))
                        .ToList();

                if (extractions.Count > 0)
                {
                    request["_kubatoolkit_extract"] =
                        new JsonArray(
                            extractions
                                .Select(h => (JsonNode)new JsonObject
                                {
                                    ["key"] = h.Key,
                                    ["value"] = h.Value,
                                    ["disabled"] = !h.Enabled
                                })
                                .ToArray());
                }

                var requestAuthNode = BuildAuthNode(node.Auth);

                if (requestAuthNode != null)
                {
                    request["auth"] = requestAuthNode;
                }

                array.Add(
                    new JsonObject
                    {
                        ["name"] = node.Name,
                        ["request"] = request
                    });
            }
            else
            {
                var folderObject =
                    new JsonObject
                    {
                        ["name"] = node.Name,
                        ["item"] = BuildItemArray(node.Children)
                    };

                var folderAuthNode = BuildAuthNode(node.Auth);

                if (folderAuthNode != null)
                {
                    folderObject["auth"] = folderAuthNode;
                }

                array.Add(folderObject);
            }
        }

        return array;
    }

    private static JsonObject?
    BuildAuthNode(
        AuthConfig auth)
    {
        return auth.Type switch
        {
            AuthType.None => new JsonObject { ["type"] = "noauth" },

            AuthType.Bearer => new JsonObject
            {
                ["type"] = "bearer",
                ["bearer"] = new JsonArray(
                    new JsonObject { ["key"] = "token", ["value"] = auth.BearerToken, ["type"] = "string" })
            },

            AuthType.Basic => new JsonObject
            {
                ["type"] = "basic",
                ["basic"] = new JsonArray(
                    new JsonObject { ["key"] = "username", ["value"] = auth.Username, ["type"] = "string" },
                    new JsonObject { ["key"] = "password", ["value"] = auth.Password, ["type"] = "string" })
            },

            AuthType.ApiKey => new JsonObject
            {
                ["type"] = "apikey",
                ["apikey"] = new JsonArray(
                    new JsonObject { ["key"] = "key", ["value"] = auth.ApiKeyName, ["type"] = "string" },
                    new JsonObject { ["key"] = "value", ["value"] = auth.ApiKeyValue, ["type"] = "string" })
            },

            _ => null
        };
    }

    private static JsonObject?
    BuildBodyNode(
        CollectionNode node)
    {
        switch (node.BodyMode)
        {
            case "urlencoded":

                return new JsonObject
                {
                    ["mode"] = "urlencoded",
                    ["urlencoded"] =
                        new JsonArray(
                            node.BodyFormData
                                .Select(f => (JsonNode)new JsonObject
                                {
                                    ["key"] = f.Key,
                                    ["value"] = f.Value,
                                    ["disabled"] = !f.Enabled
                                })
                                .ToArray())
                };

            case "formdata":

                return new JsonObject
                {
                    ["mode"] = "formdata",
                    ["formdata"] =
                        new JsonArray(
                            node.BodyFormData
                                .Select(f => (JsonNode)new JsonObject
                                {
                                    ["key"] = f.Key,
                                    ["value"] = f.Value,
                                    ["disabled"] = !f.Enabled
                                })
                                .ToArray())
                };

            case "raw" when !string.IsNullOrEmpty(node.Body):

                return new JsonObject
                {
                    ["mode"] = "raw",
                    ["raw"] = node.Body
                };

            default:

                return null;
        }
    }

    private static string
    SanitizeFileName(
        string name)
    {
        var invalid = Path.GetInvalidFileNameChars();

        var cleaned =
            new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray())
                .Trim();

        return string.IsNullOrWhiteSpace(cleaned) ? "collection" : cleaned;
    }

    private string
    MakeUniqueFileName(
        string baseName)
    {
        var fileName = $"{baseName}.json";
        var counter = 1;

        while (File.Exists(Path.Combine(CollectionsFolder, fileName)))
        {
            fileName = $"{baseName} ({++counter}).json";
        }

        return fileName;
    }
}

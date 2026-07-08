using KubaToolKit.Modules.ApiClient.Models;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KubaToolKit.Modules.ApiClient;

/// Charge des collections/environnements exportés depuis Postman
/// (Export > Collection v2.1 / Export environment) déposés dans un
/// dossier local hors du dépôt git, pour éviter d'y committer des URLs ou
/// tokens spécifiques à un environnement.
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

                Ce dossier n'est pas dans le dépôt git de l'outil : vos
                collections/environnements restent locaux à ce poste.

                Collections\   -> export Postman "Collection v2.1"
                                   (clic droit sur une collection > Export)
                Environments\  -> export Postman d'un environnement
                                   (icône œil > ... > Export)

                Déposez les fichiers .json exportés directement dans ces
                deux dossiers, puis cliquez sur "Recharger" (⟳) dans
                l'onglet API Client. Les variables {{clé}} d'un
                environnement sélectionné sont substituées dans l'URL, les
                headers et le body au moment de l'envoi.
                """);
        }
        catch
        {
            // Non bloquant : l'absence de README n'empêche pas d'utiliser
            // le module.
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
                        FilePath = file
                    };

                root.Children = BuildNodes(collection.Item, root);

                roots.Add(root);
            }
            catch (JsonException)
            {
                // Fichier non reconnu comme une collection Postman : ignoré
                // pour ne pas bloquer le chargement des autres fichiers.
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
                        IsFavorite = item.Request.Favorite == true
                    });
            }
            else if (item.Item != null)
            {
                var folder =
                    new CollectionNode
                    {
                        Name = item.Name,
                        IsRequest = false,
                        Parent = parent
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

    /// Réécrit uniquement le tableau "values" du fichier d'environnement,
    /// en conservant tous les autres champs (id, _postman_variable_scope,
    /// etc.) tels quels pour que le fichier reste réimportable dans
    /// Postman.
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

    /// Crée un nouveau fichier de collection Postman v2.1 vide et retourne
    /// son chemin.
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

    /// Réécrit le fichier de collection à partir de l'arbre en mémoire.
    /// Ne modifie que "info.name" et "item" : les autres champs connus du
    /// fichier ("info.schema", etc.) sont conservés tels quels, mais toute
    /// métadonnée avancée par requête que KubaToolKit ne modélise pas
    /// (scripts, tests, auth par requête...) est perdue au premier
    /// ajout/suppression/renommage effectué depuis l'app.
    public void
    SaveCollection(
        CollectionNode root)
    {
        if (string.IsNullOrEmpty(root.FilePath))
        {
            throw new InvalidOperationException(
                "Cette collection n'est associée à aucun fichier.");
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

                array.Add(
                    new JsonObject
                    {
                        ["name"] = node.Name,
                        ["request"] = request
                    });
            }
            else
            {
                array.Add(
                    new JsonObject
                    {
                        ["name"] = node.Name,
                        ["item"] = BuildItemArray(node.Children)
                    });
            }
        }

        return array;
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

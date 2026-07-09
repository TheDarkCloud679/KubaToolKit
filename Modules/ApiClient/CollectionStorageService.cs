using KubaToolKit.Modules.ApiClient.Models;
using KubaToolKit.Shared.Services;
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

    /// Table de correspondance "code technique -> libellé lisible" pour
    /// la vue Cartes des réponses (ex : providerCode 9 -> "Limoges",
    /// affiché "Limoges (9)"). Un seul fichier plutôt qu'un par
    /// collection : ces codes (fournisseur, tarif...) sont en général
    /// partagés par toutes les requêtes d'une même API.
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
                        FilePath = file,
                        Auth = ParseAuth(collection.Auth)
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

    /// Absence de champ "auth" = hérite du parent, comme Postman. Un
    /// résultat "Inherit" côté racine (aucun ancêtre) se résout en "None"
    /// via CollectionNode.ResolveEffectiveAuth au moment de l'envoi.
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

    /// Renseigné après chaque LoadValueLabels() : message d'erreur si le
    /// fichier n'a pas pu être lu tel quel (JSON invalide...), sinon
    /// null. Le chargement retombe silencieusement sur "aucune
    /// correspondance" dans ce cas -- ce message sert uniquement à
    /// prévenir l'utilisateur que ses libellés ne sont pas appliqués, au
    /// lieu de le laisser deviner pourquoi "9" ne devient jamais "Limoges (9)".
    public string? LastValueLabelsError { get; private set; }

    /// Charge ValueLabels.json : { "champ JSON": { "code": "libellé" } }.
    /// Créé au premier appel avec un exemple commenté si absent, pour ne
    /// pas laisser l'utilisateur deviner le format. Jamais bloquant : un
    /// fichier absent/invalide retombe sur "aucune correspondance" plutôt
    /// que de faire planter l'affichage de la réponse (voir
    /// LastValueLabelsError pour le signaler côté UI).
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
                      "_comment": "Correspondance code technique -> libellé, affichée en Cartes sous la forme 'Libellé (code)'. Une entrée par champ JSON de la réponse (nom exact tel qu'il apparaît dans le JSON, ex: providerCode), avec pour chaque code observé le libellé à afficher. Cette clé _comment est ignorée.",
                      "providerCode": {
                        "9": "Limoges"
                      },
                      "externalCode": {
                        "600": "Abonnement"
                      }
                    }
                    """);
            }
            catch
            {
                // Non bloquant : voir le commentaire de la méthode.
            }
        }

        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var json = File.ReadAllText(ValueLabelsFile);

            // Tolérant aux virgules trainantes/commentaires : ce fichier
            // s'édite à la main, une virgule oubliée en fin de liste est
            // l'erreur la plus probable et ne doit pas invalider tout le
            // fichier silencieusement.
            var documentOptions =
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                };

            // JsonNode plutôt qu'une désérialisation forte-typée : "_comment"
            // vaut une simple chaîne (pas un objet {code: libellé}), ce
            // qu'un Dictionary<string, Dictionary<string,string>> rejetterait
            // en bloc. Ignorer silencieusement toute entrée qui n'a pas la
            // forme attendue est plus utile qu'un fichier entier invalidé.
            if (JsonNode.Parse(json, documentOptions: documentOptions) is not JsonObject root)
            {
                LastValueLabelsError = "Le fichier ne contient pas un objet JSON valide.";

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

            Logger.Error("CollectionStorageService: échec du chargement de ValueLabels.json.", ex);
        }

        return result;
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
            // Pseudo-dossier "Favoris" : purement visuel, jamais persisté
            // (les requêtes qu'il contient le sont déjà via leur vrai
            // emplacement, sous leur dossier d'origine).
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

    /// Null (donc absence du champ "auth") pour Inherit, afin de préserver
    /// la convention Postman "pas de champ = hérite du parent" plutôt que
    /// d'écrire un type "inherit" qui n'existe pas dans son schéma.
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

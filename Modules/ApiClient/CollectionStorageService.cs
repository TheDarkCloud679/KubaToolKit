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

                roots.Add(
                    new CollectionNode
                    {
                        Name =
                            string.IsNullOrWhiteSpace(collection.Info?.Name)
                                ? Path.GetFileNameWithoutExtension(file)
                                : collection.Info!.Name,
                        IsRequest = false,
                        Children = BuildNodes(collection.Item)
                    });
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
        List<PostmanItem>? items)
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
                nodes.Add(
                    new CollectionNode
                    {
                        Name = item.Name,
                        IsRequest = true,
                        Method = item.Request.Method ?? "GET",
                        Url = ExtractUrl(item.Request.Url),

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

                        Body = item.Request.Body?.Raw ?? ""
                    });
            }
            else if (item.Item != null)
            {
                nodes.Add(
                    new CollectionNode
                    {
                        Name = item.Name,
                        IsRequest = false,
                        Children = BuildNodes(item.Item)
                    });
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
}

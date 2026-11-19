using KubaToolKit.Modules.ApiClient;
using KubaToolKit.Modules.ApiClient.Models;
using KubaToolKit.Modules.AtlassianSearch;
using KubaToolKit.Modules.Wiki;
using KubaToolKit.Shared.Services;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KubaToolKit.Shared.ConfigExport;

// Bundles the API Client (collections + environments, secrets blanked) and
// the Atlassian-adjacent modules (Wiki, Incident Library, non-personal
// Atlassian settings) into one JSON file a colleague can import in one
// shot. Distinct from Team Sharing's live synced folder, which already
// covers Collections/Wiki/Incidents on an ongoing basis but deliberately
// never Environments or the personal Atlassian email/API token -- this is
// a one-off snapshot for someone not on that folder (or a backup), and it
// keeps the same "never share a credential" rule Team Sharing already
// follows.
public class ConfigExportService
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new() { WriteIndented = true };

    public ConfigExportBundle
    BuildBundle()
    {
        var bundle =
            new ConfigExportBundle
            {
                ExportedAtUtc = DateTime.UtcNow.ToString("O"),
                ExportedByAppVersion =
                    Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? ""
            };

        CollectCollections(bundle);
        CollectEnvironments(bundle);
        CollectAtlassianSettings(bundle);
        CollectWiki(bundle);
        CollectIncidents(bundle);

        return bundle;
    }

    public void
    Export(
        ConfigExportBundle bundle,
        string filePath) =>
        File.WriteAllText(filePath, JsonSerializer.Serialize(bundle, SerializerOptions));

    public ConfigImportSummary
    Import(
        string filePath)
    {
        var bundle =
            JsonSerializer.Deserialize<ConfigExportBundle>(File.ReadAllText(filePath), SerializerOptions)
            ?? throw new InvalidOperationException("This file doesn't look like a KubaToolKit export.");

        var summary = new ConfigImportSummary();

        ImportCollections(bundle, summary);
        ImportEnvironments(bundle, summary);
        ImportAtlassianSettings(bundle, summary);
        ImportWiki(bundle, summary);
        ImportIncidents(bundle, summary);

        return summary;
    }

    // --- Collect -------------------------------------------------------

    private static void
    CollectCollections(
        ConfigExportBundle bundle)
    {
        var folder = CollectionStorageService.CollectionsFolder;

        if (!Directory.Exists(folder))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(folder, "*.json"))
        {
            try
            {
                bundle.Collections.Add(
                    new ExportedJsonFile
                    {
                        FileName = Path.GetFileName(file),
                        Content = JsonNode.Parse(File.ReadAllText(file))
                    });
            }
            catch (Exception ex)
            {
                Logger.Error($"ConfigExportService: failed to read collection '{file}'.", ex);
            }
        }
    }

    private static void
    CollectEnvironments(
        ConfigExportBundle bundle)
    {
        var folder = CollectionStorageService.EnvironmentsFolder;

        if (!Directory.Exists(folder))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(folder, "*.json"))
        {
            try
            {
                var env = JsonSerializer.Deserialize<PostmanEnvironment>(File.ReadAllText(file));

                if (env == null)
                {
                    continue;
                }

                BlankEnvironmentSecrets(env);

                bundle.Environments.Add(
                    new ExportedJsonFile
                    {
                        FileName = Path.GetFileName(file),
                        Content = JsonSerializer.SerializeToNode(env)
                    });
            }
            catch (Exception ex)
            {
                Logger.Error($"ConfigExportService: failed to read environment '{file}'.", ex);
            }
        }
    }

    // Blanks every field that can hold a credential: the environment's own
    // variable values, and (if a Get Token request is configured) its
    // headers, body, and auth attribute values. Keys, URLs, extraction
    // rules, and enabled flags all stay -- they describe shape, not
    // secrets, and are exactly what a colleague needs to know which
    // values to go fill in themselves after importing.
    private static void
    BlankEnvironmentSecrets(
        PostmanEnvironment env)
    {
        if (env.Values != null)
        {
            foreach (var value in env.Values)
            {
                value.Value = "";
            }
        }

        var tokenRequest = env.TokenRequest;

        if (tokenRequest == null)
        {
            return;
        }

        if (tokenRequest.Header != null)
        {
            foreach (var header in tokenRequest.Header)
            {
                header.Value = "";
            }
        }

        if (tokenRequest.Body != null)
        {
            tokenRequest.Body.Raw = "";

            BlankFormParamValues(tokenRequest.Body.UrlEncoded);
            BlankFormParamValues(tokenRequest.Body.FormData);
        }

        if (tokenRequest.Auth != null)
        {
            BlankAuthAttributeValues(tokenRequest.Auth.Bearer);
            BlankAuthAttributeValues(tokenRequest.Auth.Basic);
            BlankAuthAttributeValues(tokenRequest.Auth.ApiKey);
        }
    }

    private static void
    BlankFormParamValues(
        List<PostmanFormParam>? parameters)
    {
        if (parameters == null)
        {
            return;
        }

        foreach (var parameter in parameters)
        {
            parameter.Value = "";
        }
    }

    private static void
    BlankAuthAttributeValues(
        List<PostmanAuthAttribute>? attributes)
    {
        if (attributes == null)
        {
            return;
        }

        foreach (var attribute in attributes)
        {
            attribute.Value = "";
        }
    }

    private static void
    CollectAtlassianSettings(
        ConfigExportBundle bundle)
    {
        var settings = new AtlassianSettingsService().Load();

        bundle.AtlassianBaseUrl = settings.BaseUrl;
        bundle.AtlassianFavoriteConfluenceSpaceKeys = settings.FavoriteConfluenceSpaceKeys.ToList();
        bundle.AtlassianSavedJiraStatsFilters = settings.SavedJiraStatsFilters.ToList();
    }

    private static void
    CollectWiki(
        ConfigExportBundle bundle)
    {
        var library = new WikiService().LoadLibrary();

        bundle.WikiSections = library.Sections;

        var imagesFolder = WikiService.GetImagesFolderPath();

        foreach (var fileName in library.Sections.SelectMany(s => s.ImageFileNames).Distinct())
        {
            var path = Path.Combine(imagesFolder, fileName);

            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                bundle.WikiImagesBase64[fileName] = Convert.ToBase64String(File.ReadAllBytes(path));
            }
            catch (Exception ex)
            {
                Logger.Error($"ConfigExportService: failed to read wiki image '{path}'.", ex);
            }
        }
    }

    private static void
    CollectIncidents(
        ConfigExportBundle bundle) =>
        bundle.Incidents = new IncidentLibraryStorageService().LoadIncidents();

    // --- Import ----------------------------------------------------------
    //
    // Every import step below only adds: nothing here ever overwrites or
    // deletes a file, a saved filter, or a wiki page that already exists
    // locally, mirroring how IncidentLibraryStorageService.ImportLibrary
    // already behaves (name collisions get a " (2)" suffix, never a
    // silent overwrite).

    private static void
    ImportCollections(
        ConfigExportBundle bundle,
        ConfigImportSummary summary)
    {
        if (bundle.Collections.Count == 0)
        {
            return;
        }

        Directory.CreateDirectory(CollectionStorageService.CollectionsFolder);

        foreach (var file in bundle.Collections)
        {
            if (file.Content == null)
            {
                continue;
            }

            var path = MakeUniqueFilePath(CollectionStorageService.CollectionsFolder, file.FileName);

            File.WriteAllText(path, file.Content.ToJsonString(SerializerOptions));

            summary.CollectionsImported++;
        }
    }

    private static void
    ImportEnvironments(
        ConfigExportBundle bundle,
        ConfigImportSummary summary)
    {
        if (bundle.Environments.Count == 0)
        {
            return;
        }

        Directory.CreateDirectory(CollectionStorageService.EnvironmentsFolder);

        var existingNames = GetExistingEnvironmentNames();

        foreach (var file in bundle.Environments)
        {
            if (file.Content == null)
            {
                continue;
            }

            var name =
                file.Content["name"]?.GetValue<string>()
                ?? Path.GetFileNameWithoutExtension(file.FileName);

            // Never overwritten, even blank-valued -- a colleague may
            // already have this environment set up with their own real
            // values, and there's no way to tell that apart here from one
            // they haven't touched yet.
            if (existingNames.Contains(name))
            {
                summary.EnvironmentsSkippedExisting++;
                continue;
            }

            var path = MakeUniqueFilePath(CollectionStorageService.EnvironmentsFolder, file.FileName);

            File.WriteAllText(path, file.Content.ToJsonString(SerializerOptions));

            summary.EnvironmentsImported++;
        }
    }

    private static HashSet<string>
    GetExistingEnvironmentNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var folder = CollectionStorageService.EnvironmentsFolder;

        if (!Directory.Exists(folder))
        {
            return names;
        }

        foreach (var file in Directory.GetFiles(folder, "*.json"))
        {
            try
            {
                var env = JsonSerializer.Deserialize<PostmanEnvironment>(File.ReadAllText(file));

                if (env != null && !string.IsNullOrWhiteSpace(env.Name))
                {
                    names.Add(env.Name);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"ConfigExportService: failed to read existing environment '{file}'.", ex);
            }
        }

        return names;
    }

    private static void
    ImportAtlassianSettings(
        ConfigExportBundle bundle,
        ConfigImportSummary summary)
    {
        var service = new AtlassianSettingsService();
        var settings = service.Load();
        var changed = false;

        if (string.IsNullOrWhiteSpace(settings.BaseUrl) && !string.IsNullOrWhiteSpace(bundle.AtlassianBaseUrl))
        {
            settings.BaseUrl = bundle.AtlassianBaseUrl;
            summary.AtlassianBaseUrlApplied = true;
            changed = true;
        }

        foreach (var key in bundle.AtlassianFavoriteConfluenceSpaceKeys)
        {
            if (settings.FavoriteConfluenceSpaceKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            settings.FavoriteConfluenceSpaceKeys.Add(key);
            summary.AtlassianFavoritesAdded++;
            changed = true;
        }

        foreach (var filter in bundle.AtlassianSavedJiraStatsFilters)
        {
            if (settings.SavedJiraStatsFilters.Any(f => f.Name == filter.Name))
            {
                continue;
            }

            settings.SavedJiraStatsFilters.Add(filter);
            summary.AtlassianFiltersAdded++;
            changed = true;
        }

        if (changed)
        {
            service.Save(settings);
        }
    }

    private static void
    ImportWiki(
        ConfigExportBundle bundle,
        ConfigImportSummary summary)
    {
        if (bundle.WikiSections.Count == 0)
        {
            return;
        }

        var wikiService = new WikiService();
        var library = wikiService.LoadLibrary();

        var existingKeys =
            new HashSet<(string Folder, string Name)>(
                library.Sections.Select(s => (s.Folder, s.Name)));

        var imagesFolder = WikiService.EnsureImagesFolder();

        foreach (var section in bundle.WikiSections)
        {
            if (existingKeys.Contains((section.Folder, section.Name)))
            {
                summary.WikiSectionsSkippedDuplicate++;
                continue;
            }

            for (var i = 0; i < section.ImageFileNames.Count; i++)
            {
                var copied = CopyWikiImageFromBundle(bundle, section.ImageFileNames[i], imagesFolder);

                if (copied == null)
                {
                    continue;
                }

                section.ImageFileNames[i] = copied;
                summary.WikiImagesCopied++;
            }

            library.Sections.Add(section);
            summary.WikiSectionsImported++;
        }

        if (summary.WikiSectionsImported > 0)
        {
            wikiService.SaveLibrary(library);
        }
    }

    // Always copies through a fresh temp file and WikiService's own
    // collision-safe naming, rather than trusting a same-named file
    // already in the images folder to be the same image -- two unrelated
    // wikis can easily both have a "diagram.png".
    private static string?
    CopyWikiImageFromBundle(
        ConfigExportBundle bundle,
        string originalFileName,
        string imagesFolder)
    {
        if (!bundle.WikiImagesBase64.TryGetValue(originalFileName, out var base64))
        {
            return null;
        }

        var tempPath =
            Path.Combine(
                Path.GetTempPath(),
                $"kubatoolkit-import-{Guid.NewGuid():N}{Path.GetExtension(originalFileName)}");

        try
        {
            File.WriteAllBytes(tempPath, Convert.FromBase64String(base64));

            return WikiService.CopyImageWithUniqueName(tempPath, imagesFolder);
        }
        catch (Exception ex)
        {
            Logger.Error($"ConfigExportService: failed to import wiki image '{originalFileName}'.", ex);

            return null;
        }
        finally
        {
            try
            {
                File.Delete(tempPath);
            }
            catch
            {
                // Best effort -- a leftover temp file isn't worth failing the import over.
            }
        }
    }

    private static void
    ImportIncidents(
        ConfigExportBundle bundle,
        ConfigImportSummary summary)
    {
        if (bundle.Incidents.Count == 0)
        {
            return;
        }

        // Reuses IncidentLibraryStorageService's own Export/Import file
        // format and collision-safe renaming instead of re-implementing
        // it here -- a temp file is the cheapest way to hand it the same
        // JSON-array-of-IncidentEntry shape it already expects.
        var tempPath =
            Path.Combine(Path.GetTempPath(), $"kubatoolkit-import-incidents-{Guid.NewGuid():N}.json");

        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(bundle.Incidents, SerializerOptions));

            summary.IncidentsImported = new IncidentLibraryStorageService().ImportLibrary(tempPath);
        }
        finally
        {
            try
            {
                File.Delete(tempPath);
            }
            catch
            {
                // Best effort -- a leftover temp file isn't worth failing the import over.
            }
        }
    }

    private static string
    MakeUniqueFilePath(
        string folder,
        string preferredFileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(preferredFileName);
        var extension = Path.GetExtension(preferredFileName);

        if (string.IsNullOrEmpty(extension))
        {
            extension = ".json";
        }

        var fileName = baseName + extension;
        var counter = 1;

        while (File.Exists(Path.Combine(folder, fileName)))
        {
            fileName = $"{baseName} ({++counter}){extension}";
        }

        return Path.Combine(folder, fileName);
    }
}

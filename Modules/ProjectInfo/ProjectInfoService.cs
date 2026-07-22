using KubaToolKit.Modules.ProjectInfo.Models;
using KubaToolKit.Shared.Services;
using System.IO;
using System.Text.Json;

namespace KubaToolKit.Modules.ProjectInfo;

public class ProjectInfoService
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new() { WriteIndented = true };

    // Global: only the profile -> project key map lives here. Each
    // project's own sections/rows live in its own folder instead, next to
    // its Wiki data and files -- see GetProjectDataFilePath.
    public static string
    GetFilePath() =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "project-info.json");

    public static string
    GetProjectFolderPath(
        string projectKey) =>
        Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Config",
            "ProjectFiles",
            SanitizeForFolderName(projectKey));

    public static string
    GetProjectDataFilePath(
        string projectKey) =>
        Path.Combine(GetProjectFolderPath(projectKey), "project-info.json");

    /// Creates the shared project folder if needed, dropping a README the
    /// first time so anyone who stumbles onto it (browsing the shared
    /// drive, say) understands what it's for without having to ask.
    /// Called from both Project Info's "Files folder" button and the Wiki
    /// module, so the note is written at most once regardless of which
    /// opens the folder first.
    public static string
    EnsureProjectFolder(
        string projectKey)
    {
        var folderPath = GetProjectFolderPath(projectKey);
        var isFirstRun = !Directory.Exists(folderPath);

        Directory.CreateDirectory(folderPath);

        if (isFirstRun)
        {
            WriteProjectFolderReadme(folderPath, projectKey);
        }

        return folderPath;
    }

    private static void
    WriteProjectFolderReadme(
        string folderPath,
        string projectKey)
    {
        try
        {
            File.WriteAllText(
                Path.Combine(folderPath, "README.txt"),
                $"""
                KubaToolKit - Project files ({projectKey})
                ===========================================

                Everything about this project lives here: its Project Info
                data (project-info.json), its Wiki (wiki.json, WikiImages\),
                and any file you drop in yourself (schemas, configs,
                exports, a key file for the FileZilla export...) to share
                it with colleagues, the same way you already share this
                KubaToolKit installation (network drive, sync tool, git...).

                Prod/Preprod/Test profiles of the same project
                automatically share this same folder -- see the "Project"
                field in the Project Info window.

                WikiImages\ -> images/PDFs attached from the Wiki module.
                               Don't rename or move these files: the Wiki
                               refers to them by file name.
                """);
        }
        catch (Exception ex)
        {
            Logger.Error("ProjectInfoService: failed to write the project folder README.", ex);
        }
    }

    private static string
    SanitizeForFolderName(
        string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();

        var sanitized =
            new string(value.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray())
                .Trim();

        return string.IsNullOrWhiteSpace(sanitized) ? "_" : sanitized;
    }

    public ProjectInfoRoot
    Load()
    {
        var filePath = GetFilePath();

        if (!File.Exists(filePath))
        {
            Logger.Debug($"ProjectInfoService: {filePath} missing, starting empty.");

            return new ProjectInfoRoot();
        }

        try
        {
            var json = File.ReadAllText(filePath);

            var root =
                JsonSerializer.Deserialize<ProjectInfoRoot>(json, SerializerOptions)
                ?? new ProjectInfoRoot();

            MigrateLegacyProjectsIfPresent(json, root);

            Logger.Debug($"ProjectInfoService: {root.ProfileProjectKeys.Count} profile key mapping(s) loaded from {filePath}.");

            return root;
        }
        catch (Exception ex)
        {
            Logger.Error($"ProjectInfoService: failed to read {filePath}.", ex);

            throw;
        }
    }

    public void
    Save(
        ProjectInfoRoot root)
    {
        var filePath = GetFilePath();

        try
        {
            var directory = Path.GetDirectoryName(filePath);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(
                filePath,
                JsonSerializer.Serialize(root, SerializerOptions));

            Logger.Debug($"ProjectInfoService: saved {root.ProfileProjectKeys.Count} profile key mapping(s) to {filePath}.");
        }
        catch (Exception ex)
        {
            Logger.Error($"ProjectInfoService: failed to write {filePath}.", ex);

            throw;
        }
    }

    private class LegacyProjectInfoRoot
    {
        public List<ProjectInfoProject> Projects { get; set; } = new();
    }

    /// One-time move of every project out of the old shared
    /// Config/project-info.json (which used to hold every project's
    /// sections/rows together) into its own
    /// Config/ProjectFiles/{key}/project-info.json, alongside that
    /// project's Wiki data and files. Only the profile-to-key map is kept
    /// in the global file afterwards, so this never runs twice.
    private void
    MigrateLegacyProjectsIfPresent(
        string json,
        ProjectInfoRoot root)
    {
        List<ProjectInfoProject> legacyProjects;

        try
        {
            var legacy = JsonSerializer.Deserialize<LegacyProjectInfoRoot>(json, SerializerOptions);
            legacyProjects = legacy?.Projects ?? new();
        }
        catch (Exception ex)
        {
            Logger.Error("ProjectInfoService: failed to inspect the legacy project-info.json for a Projects list to migrate.", ex);

            return;
        }

        if (legacyProjects.Count == 0)
        {
            return;
        }

        Logger.Debug($"ProjectInfoService: migrating {legacyProjects.Count} project(s) out of the legacy global project-info.json.");

        foreach (var project in legacyProjects)
        {
            if (File.Exists(GetProjectDataFilePath(project.Key)))
            {
                continue;
            }

            SaveProject(project);
        }

        Save(root);
    }

    public ProjectInfoProject
    LoadProject(
        string projectKey)
    {
        var filePath = GetProjectDataFilePath(projectKey);

        if (!File.Exists(filePath))
        {
            return new ProjectInfoProject { Key = projectKey };
        }

        try
        {
            var json = File.ReadAllText(filePath);

            var project =
                JsonSerializer.Deserialize<ProjectInfoProject>(json, SerializerOptions)
                ?? new ProjectInfoProject { Key = projectKey };

            project.Key = projectKey;

            Logger.Debug($"ProjectInfoService: loaded '{projectKey}' from {filePath}.");

            return project;
        }
        catch (Exception ex)
        {
            Logger.Error($"ProjectInfoService: failed to read {filePath}.", ex);

            throw;
        }
    }

    public void
    SaveProject(
        ProjectInfoProject project)
    {
        EnsureProjectFolder(project.Key);

        var filePath = GetProjectDataFilePath(project.Key);

        try
        {
            File.WriteAllText(
                filePath,
                JsonSerializer.Serialize(project, SerializerOptions));

            Logger.Debug($"ProjectInfoService: saved '{project.Key}' to {filePath}.");
        }
        catch (Exception ex)
        {
            Logger.Error($"ProjectInfoService: failed to write {filePath}.", ex);

            throw;
        }
    }

    // Longest/most specific marker first, so e.g. "_preprod" is tried before
    // "_prod" -- checked in this order regardless of list order below.
    private static readonly string[] EnvironmentSuffixes =
    {
        "preproduction", "preprod", "pre-prod",
        "production", "prod",
        "staging", "stage",
        "recette", "uat", "test",
        "development", "dev",
        "sandbox", "demo"
    };

    /// A profile with no explicit key shares data with its Prod/Preprod/Test
    /// siblings automatically: strips a trailing "_prod"/"-preprod"/"_test"
    /// (etc.) suffix from the profile name to get a common base key, instead
    /// of defaulting to the full profile name (which made every environment
    /// its own separate, unshared project).
    public string
    ResolveProjectKey(
        ProjectInfoRoot root,
        string profileName)
    {
        if (root.ProfileProjectKeys.TryGetValue(profileName, out var explicitKey)
            && !string.IsNullOrWhiteSpace(explicitKey))
        {
            return explicitKey;
        }

        var derivedKey = DeriveDefaultProjectKey(profileName);

        if (!string.Equals(derivedKey, profileName, StringComparison.OrdinalIgnoreCase))
        {
            MigrateProfileNamedProjectToSharedKey(profileName, derivedKey);
        }

        return derivedKey;
    }

    private static string
    DeriveDefaultProjectKey(
        string profileName)
    {
        var markers =
            EnvironmentSuffixes
                .SelectMany(suffix => new[] { "_" + suffix, "-" + suffix })
                .OrderByDescending(marker => marker.Length);

        foreach (var marker in markers)
        {
            if (!profileName.EndsWith(marker, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var baseName = profileName[..^marker.Length].TrimEnd('_', '-');

            if (!string.IsNullOrWhiteSpace(baseName))
            {
                return baseName;
            }
        }

        return profileName;
    }

    /// A project already saved under the old default (the full profile
    /// name, from before this auto-sharing existed) keeps working: its
    /// per-project file is moved to the shared key's folder if nothing
    /// claims it yet, or merged into the already-shared project's file if a
    /// sibling environment got there first.
    private void
    MigrateProfileNamedProjectToSharedKey(
        string profileName,
        string sharedKey)
    {
        var oldFilePath = GetProjectDataFilePath(profileName);

        if (!File.Exists(oldFilePath))
        {
            return;
        }

        var oldProject = LoadProject(profileName);
        var sharedFilePath = GetProjectDataFilePath(sharedKey);

        if (!File.Exists(sharedFilePath))
        {
            oldProject.Key = sharedKey;
            SaveProject(oldProject);
        }
        else
        {
            var sharedProject = LoadProject(sharedKey);
            sharedProject.Sections.AddRange(oldProject.Sections);
            SaveProject(sharedProject);
        }

        try
        {
            File.Delete(oldFilePath);
        }
        catch (Exception ex)
        {
            Logger.Error($"ProjectInfoService: failed to remove the migrated legacy file {oldFilePath}.", ex);
        }
    }

    public void
    SetProjectKey(
        ProjectInfoRoot root,
        string profileName,
        string projectKey)
    {
        root.ProfileProjectKeys[profileName] = projectKey;
    }

    public static readonly Dictionary<string, string[]> SectionPresets =
        new()
        {
            ["Contacts"] = new[] { "First name", "Last name", "Role", "Email", "Phone" },
            ["Network equipment"] = new[] { "Name", "Type", "IP address", "Location", "Notes" },
            ["VPN"] = new[] { "Name", "Type", "Address", "Credentials", "Notes" },
            ["Custom"] = new[] { "Column 1" }
        };
}

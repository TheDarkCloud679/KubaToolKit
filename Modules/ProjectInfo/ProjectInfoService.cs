using KubaToolKit.Modules.ProjectInfo.Models;
using KubaToolKit.Shared.Services;
using System.IO;
using System.Text.Json;

namespace KubaToolKit.Modules.ProjectInfo;

public class ProjectInfoService
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new() { WriteIndented = true };

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

                Shared storage for this project: drop any file here
                (schemas, configs, exports, a key file for the FileZilla
                export...) to share it with colleagues, the same way you
                already share this KubaToolKit installation (network
                drive, sync tool, git...).

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
            // Not critical: a missing README doesn't stop the folder
            // itself from working.
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

            Logger.Debug($"ProjectInfoService: {root.Projects.Count} project(s) loaded from {filePath}.");

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

            Logger.Debug($"ProjectInfoService: {root.Projects.Count} project(s) saved to {filePath}.");
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
            MigrateProfileNamedProjectToSharedKey(root, profileName, derivedKey);
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
    /// name, from before this auto-sharing existed) keeps working: promoted
    /// to the shared key if nothing claims it yet, or merged into the
    /// already-shared project if a sibling environment got there first.
    private void
    MigrateProfileNamedProjectToSharedKey(
        ProjectInfoRoot root,
        string profileName,
        string sharedKey)
    {
        var projectUnderProfileName =
            root.Projects.FirstOrDefault(p =>
                string.Equals(p.Key, profileName, StringComparison.OrdinalIgnoreCase));

        if (projectUnderProfileName == null)
        {
            return;
        }

        var sharedProject =
            root.Projects.FirstOrDefault(p =>
                string.Equals(p.Key, sharedKey, StringComparison.OrdinalIgnoreCase));

        if (sharedProject == null)
        {
            projectUnderProfileName.Key = sharedKey;
        }
        else if (sharedProject != projectUnderProfileName)
        {
            sharedProject.Sections.AddRange(projectUnderProfileName.Sections);
            root.Projects.Remove(projectUnderProfileName);
        }

        Save(root);
    }

    public void
    SetProjectKey(
        ProjectInfoRoot root,
        string profileName,
        string projectKey)
    {
        root.ProfileProjectKeys[profileName] = projectKey;
    }

    public ProjectInfoProject
    GetOrCreateProject(
        ProjectInfoRoot root,
        string projectKey)
    {
        var project =
            root.Projects.FirstOrDefault(p =>
                string.Equals(p.Key, projectKey, StringComparison.OrdinalIgnoreCase));

        if (project != null)
        {
            return project;
        }

        project = new ProjectInfoProject { Key = projectKey };
        root.Projects.Add(project);

        return project;
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

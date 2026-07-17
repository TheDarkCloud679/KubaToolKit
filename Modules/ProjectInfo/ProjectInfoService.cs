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

    public string
    ResolveProjectKey(
        ProjectInfoRoot root,
        string profileName) =>
        root.ProfileProjectKeys.TryGetValue(profileName, out var key)
        && !string.IsNullOrWhiteSpace(key)
            ? key
            : profileName;

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

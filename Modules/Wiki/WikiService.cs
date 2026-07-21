using KubaToolKit.Modules.ProjectInfo;
using KubaToolKit.Modules.Wiki.Models;
using KubaToolKit.Shared.Services;
using System.IO;
using System.Text.Json;

namespace KubaToolKit.Modules.Wiki;

public class WikiService
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new() { WriteIndented = true };

    public static string
    GetFilePath() =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "wiki.json");

    // Images live inside the same shared project files folder used by
    // Project Info's "Files folder" button, so both features point at one
    // shared location per project instead of two.
    public static string
    GetImagesFolderPath(
        string projectKey) =>
        Path.Combine(ProjectInfoService.GetProjectFolderPath(projectKey), "WikiImages");

    public WikiRoot
    Load()
    {
        var filePath = GetFilePath();

        if (!File.Exists(filePath))
        {
            Logger.Debug($"WikiService: {filePath} missing, starting empty.");

            return new WikiRoot();
        }

        try
        {
            var json = File.ReadAllText(filePath);

            var root =
                JsonSerializer.Deserialize<WikiRoot>(json, SerializerOptions)
                ?? new WikiRoot();

            Logger.Debug($"WikiService: {root.Projects.Count} project(s) loaded from {filePath}.");

            return root;
        }
        catch (Exception ex)
        {
            Logger.Error($"WikiService: failed to read {filePath}.", ex);

            throw;
        }
    }

    public void
    Save(
        WikiRoot root)
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

            Logger.Debug($"WikiService: {root.Projects.Count} project(s) saved to {filePath}.");
        }
        catch (Exception ex)
        {
            Logger.Error($"WikiService: failed to write {filePath}.", ex);

            throw;
        }
    }

    public WikiProject
    GetOrCreateProject(
        WikiRoot root,
        string projectKey)
    {
        var project =
            root.Projects.FirstOrDefault(p =>
                string.Equals(p.Key, projectKey, StringComparison.OrdinalIgnoreCase));

        if (project != null)
        {
            return project;
        }

        project = new WikiProject { Key = projectKey };
        root.Projects.Add(project);

        return project;
    }
}

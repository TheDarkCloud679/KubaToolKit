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

    /// Creates the images folder (and the shared project folder above it,
    /// with its own README) if needed, dropping a short note the first
    /// time explaining what these files are for someone who lands
    /// straight in this subfolder without seeing the parent one.
    public static string
    EnsureImagesFolder(
        string projectKey)
    {
        ProjectInfoService.EnsureProjectFolder(projectKey);

        var imagesFolder = GetImagesFolderPath(projectKey);
        var isFirstRun = !Directory.Exists(imagesFolder);

        Directory.CreateDirectory(imagesFolder);

        if (isFirstRun)
        {
            try
            {
                File.WriteAllText(
                    Path.Combine(imagesFolder, "README.txt"),
                    """
                    Images and PDFs attached from the Wiki module (KubaToolKit)
                    live here. Don't rename or move these files: the Wiki
                    refers to each one by its exact file name.
                    """);
            }
            catch (Exception ex)
            {
                Logger.Error("WikiService: failed to write the images folder README.", ex);
            }
        }

        return imagesFolder;
    }

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

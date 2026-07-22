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

    // Legacy global file every project used to share; migrated out into
    // each project's own folder the first time it's still found to exist.
    private static string
    GetLegacyFilePath() =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "wiki.json");

    public static string
    GetProjectFilePath(
        string projectKey) =>
        Path.Combine(ProjectInfoService.GetProjectFolderPath(projectKey), "wiki.json");

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

    public WikiProject
    LoadProject(
        string projectKey)
    {
        MigrateLegacyFileIfPresent();

        var filePath = GetProjectFilePath(projectKey);

        if (!File.Exists(filePath))
        {
            Logger.Debug($"WikiService: {filePath} missing, starting empty for '{projectKey}'.");

            return new WikiProject { Key = projectKey };
        }

        try
        {
            var json = File.ReadAllText(filePath);

            var project =
                JsonSerializer.Deserialize<WikiProject>(json, SerializerOptions)
                ?? new WikiProject { Key = projectKey };

            project.Key = projectKey;

            Logger.Debug($"WikiService: loaded '{projectKey}' from {filePath}.");

            return project;
        }
        catch (Exception ex)
        {
            Logger.Error($"WikiService: failed to read {filePath}.", ex);

            throw;
        }
    }

    public void
    SaveProject(
        WikiProject project)
    {
        ProjectInfoService.EnsureProjectFolder(project.Key);

        var filePath = GetProjectFilePath(project.Key);

        try
        {
            File.WriteAllText(
                filePath,
                JsonSerializer.Serialize(project, SerializerOptions));

            Logger.Debug($"WikiService: saved '{project.Key}' to {filePath}.");
        }
        catch (Exception ex)
        {
            Logger.Error($"WikiService: failed to write {filePath}.", ex);

            throw;
        }
    }

    /// One-time move of every project out of the old shared
    /// Config/wiki.json into its own Config/ProjectFiles/{key}/wiki.json,
    /// alongside that project's WikiImages and Project Info data. Renames
    /// the legacy file once done so this never runs again.
    private void
    MigrateLegacyFileIfPresent()
    {
        var legacyPath = GetLegacyFilePath();

        if (!File.Exists(legacyPath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(legacyPath);

            var legacyRoot =
                JsonSerializer.Deserialize<WikiRoot>(json, SerializerOptions)
                ?? new WikiRoot();

            foreach (var project in legacyRoot.Projects)
            {
                if (File.Exists(GetProjectFilePath(project.Key)))
                {
                    continue;
                }

                SaveProject(project);
            }

            var backupPath = legacyPath + $".migrated-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Move(legacyPath, backupPath);

            Logger.Debug($"WikiService: migrated {legacyRoot.Projects.Count} project(s) out of the legacy {legacyPath}, backed up to {backupPath}.");
        }
        catch (Exception ex)
        {
            Logger.Error($"WikiService: failed to migrate the legacy {legacyPath}.", ex);
        }
    }
}

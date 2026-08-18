using KubaToolKit.Modules.Wiki.Models;
using KubaToolKit.Shared.Services;
using System.IO;
using System.Text.Json;

namespace KubaToolKit.Modules.Wiki;

// The wiki used to be split per AWS-profile/project, each with its own
// wiki.json under Config/ProjectFiles/{key}/. It's now a single library
// shared across the whole app, with folders (WikiSection.Folder) taking
// over the "keep related pages together" job project-scoping used to do.
// The first time this runs after that change, every project's old
// wiki.json (and its WikiImages) is folded in as one folder named after
// that project, so nothing already written is lost.
public class WikiService
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new() { WriteIndented = true };

    private static string
    GetLibraryFolderPath() =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "Wiki");

    private static string
    GetLibraryFilePath() =>
        Path.Combine(GetLibraryFolderPath(), "wiki.json");

    // Images live alongside the library file itself now, rather than
    // inside a per-project shared folder.
    public static string
    GetImagesFolderPath() =>
        Path.Combine(GetLibraryFolderPath(), "WikiImages");

    private static string
    GetProjectFilesRootPath() =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "ProjectFiles");

    /// Creates the images folder (and the library folder above it) if
    /// needed, dropping a short note the first time explaining what these
    /// files are for someone who lands straight in this subfolder without
    /// seeing the parent one.
    public static string
    EnsureImagesFolder()
    {
        var imagesFolder = GetImagesFolderPath();
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

    public WikiLibrary
    LoadLibrary()
    {
        var filePath = GetLibraryFilePath();

        if (!File.Exists(filePath))
        {
            var migrated = MigratePerProjectWikisIfPresent();

            if (migrated != null)
            {
                SaveLibrary(migrated);

                return migrated;
            }

            Logger.Debug($"WikiService: {filePath} missing, starting empty.");

            return new WikiLibrary();
        }

        try
        {
            var json = File.ReadAllText(filePath);

            var library =
                JsonSerializer.Deserialize<WikiLibrary>(json, SerializerOptions)
                ?? new WikiLibrary();

            Logger.Debug($"WikiService: loaded the library from {filePath}.");

            return library;
        }
        catch (Exception ex)
        {
            Logger.Error($"WikiService: failed to read {filePath}.", ex);

            throw;
        }
    }

    public void
    SaveLibrary(
        WikiLibrary library)
    {
        Directory.CreateDirectory(GetLibraryFolderPath());

        var filePath = GetLibraryFilePath();

        try
        {
            File.WriteAllText(
                filePath,
                JsonSerializer.Serialize(library, SerializerOptions));

            Logger.Debug($"WikiService: saved the library to {filePath}.");
        }
        catch (Exception ex)
        {
            Logger.Error($"WikiService: failed to write {filePath}.", ex);

            throw;
        }
    }

    // One-time consolidation of every project's old wiki.json (and its
    // WikiImages) into the new shared library, each project's pages
    // becoming a folder named after that project. Returns null if there
    // was nothing to migrate (a genuinely fresh install), in which case
    // the caller just starts empty without writing a file yet.
    private WikiLibrary?
    MigratePerProjectWikisIfPresent()
    {
        var projectFilesRoot = GetProjectFilesRootPath();

        if (!Directory.Exists(projectFilesRoot))
        {
            return null;
        }

        var library = new WikiLibrary();
        var migratedAny = false;

        foreach (var projectFolder in Directory.GetDirectories(projectFilesRoot))
        {
            var oldWikiPath = Path.Combine(projectFolder, "wiki.json");

            if (!File.Exists(oldWikiPath))
            {
                continue;
            }

            try
            {
                var json = File.ReadAllText(oldWikiPath);

                var oldProject = JsonSerializer.Deserialize<WikiProject>(json, SerializerOptions);

                if (oldProject == null || oldProject.Sections.Count == 0)
                {
                    continue;
                }

                // The directory name is the authoritative sanitized project
                // key -- the JSON's own Key field was always overwritten by
                // the caller on load in the old per-project WikiService, so
                // it can't be trusted to still be accurate here.
                var folderName = Path.GetFileName(projectFolder);

                var oldImagesFolder = Path.Combine(projectFolder, "WikiImages");
                var newImagesFolder = EnsureImagesFolder();

                foreach (var section in oldProject.Sections)
                {
                    section.Folder = folderName;

                    for (var i = 0; i < section.ImageFileNames.Count; i++)
                    {
                        var fileName = section.ImageFileNames[i];
                        var sourcePath = Path.Combine(oldImagesFolder, fileName);

                        if (!File.Exists(sourcePath))
                        {
                            continue;
                        }

                        section.ImageFileNames[i] = CopyImageWithUniqueName(sourcePath, newImagesFolder);
                    }

                    library.Sections.Add(section);
                }

                migratedAny = true;

                Logger.Info(
                    $"WikiService: migrated {oldProject.Sections.Count} page(s) from project "
                    + $"'{folderName}' into the shared wiki, as folder '{folderName}'.");
            }
            catch (Exception ex)
            {
                Logger.Error($"WikiService: failed to migrate {oldWikiPath}.", ex);
            }
        }

        return migratedAny ? library : null;
    }

    // Shared with WikiWindow's "Add image" flow -- same collision handling
    // either way, so a page migrated from a project that happened to reuse
    // a file name (e.g. "diagram.png") another project also used doesn't
    // silently overwrite one of them.
    public static string
    CopyImageWithUniqueName(
        string sourcePath,
        string imagesFolder)
    {
        var fileName = Path.GetFileName(sourcePath);
        var targetPath = Path.Combine(imagesFolder, fileName);
        var counter = 1;

        while (File.Exists(targetPath))
        {
            fileName =
                $"{Path.GetFileNameWithoutExtension(sourcePath)}_{counter}{Path.GetExtension(sourcePath)}";

            targetPath = Path.Combine(imagesFolder, fileName);
            counter++;
        }

        File.Copy(sourcePath, targetPath);

        return fileName;
    }
}

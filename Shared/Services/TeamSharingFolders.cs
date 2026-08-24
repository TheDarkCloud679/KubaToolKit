using System.IO;
using System.Linq;

namespace KubaToolKit.Shared.Services;

// One place computing every module's shared-folder subfolder, so each
// storage service only needs "my shared subfolder, or null if sharing
// isn't set up" -- and one place for the "copy existing local data in"
// migration, shared by every module simple enough for it (a single file/
// folder, no per-item collision handling needed -- Incident Library
// keeps its own smarter Export/Import-based copy instead, since one-file-
// per-incident needs the same collision-safe renaming Import already
// does).
public static class TeamSharingFolders
{
    private static string?
    SharedRoot()
    {
        var folder = new TeamSharingSettingsService().Load().SharedFolder;

        return string.IsNullOrWhiteSpace(folder) ? null : folder;
    }

    public static string?
    SharedIncidentsFolder() =>
        SharedRoot() is { } root ? Path.Combine(root, "Incidents") : null;

    public static string?
    SharedWikiFolder() =>
        SharedRoot() is { } root ? Path.Combine(root, "Wiki") : null;

    public static string?
    SharedApiClientCollectionsFolder() =>
        SharedRoot() is { } root ? Path.Combine(root, "ApiClient", "Collections") : null;

    // Only copies when the destination doesn't exist yet or is completely
    // empty -- never merges into (or overwrites anything in) a folder a
    // colleague may already be using, to avoid clobbering their data.
    // Returns how many top-level entries were copied, or -1 if nothing
    // was copied because the destination already had content.
    public static int
    TryCopyIfDestinationEmpty(
        string sourceFolder,
        string destinationFolder)
    {
        if (!Directory.Exists(sourceFolder))
        {
            return 0;
        }

        if (Directory.Exists(destinationFolder) && Directory.EnumerateFileSystemEntries(destinationFolder).Any())
        {
            return -1;
        }

        Directory.CreateDirectory(destinationFolder);

        var copiedCount = 0;

        foreach (var file in Directory.GetFiles(sourceFolder))
        {
            File.Copy(file, Path.Combine(destinationFolder, Path.GetFileName(file)), overwrite: false);

            copiedCount++;
        }

        foreach (var dir in Directory.GetDirectories(sourceFolder))
        {
            var destSubDir = Path.Combine(destinationFolder, Path.GetFileName(dir));

            Directory.CreateDirectory(destSubDir);

            CopyDirectoryRecursive(dir, destSubDir);

            copiedCount++;
        }

        return copiedCount;
    }

    private static void
    CopyDirectoryRecursive(
        string source,
        string destination)
    {
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: false);
        }

        foreach (var dir in Directory.GetDirectories(source))
        {
            var destSubDir = Path.Combine(destination, Path.GetFileName(dir));

            Directory.CreateDirectory(destSubDir);

            CopyDirectoryRecursive(dir, destSubDir);
        }
    }
}

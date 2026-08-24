using System.IO;

namespace KubaToolKit.Shared.Services;

// A lightweight "recycle bin" for the Team-Sharing-enabled modules --
// anyone on a shared folder can delete something a colleague is relying
// on, so nothing is ever hard-deleted: it's moved (or, for a file several
// items share, snapshotted) into a "Backup" subfolder next to wherever it
// lived instead. Timestamped so repeated deletes of the same name never
// collide/overwrite each other.
public static class BackupHelper
{
    // One-file-per-item storage (an incident, an API Client collection):
    // moves the file into "<sameFolder>/Backup/" instead of deleting it.
    public static void
    MoveToBackup(
        string filePath)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        try
        {
            var backupPath = BuildBackupPath(filePath);

            File.Move(filePath, backupPath);
        }
        catch (Exception ex)
        {
            Logger.Error($"BackupHelper: failed to back up '{filePath}', deleting instead.", ex);

            File.Delete(filePath);
        }
    }

    // Several-items-share-one-file storage (a Wiki section, a Project
    // Info row, an item inside an API Client collection): call right
    // before overwriting the file with the item removed, to snapshot its
    // current (pre-removal) content. A no-op if the file doesn't exist
    // yet -- nothing to lose.
    public static void
    SnapshotBeforeDelete(
        string filePath)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        try
        {
            var backupPath = BuildBackupPath(filePath);

            File.Copy(filePath, backupPath);
        }
        catch (Exception ex)
        {
            Logger.Error($"BackupHelper: failed to snapshot '{filePath}' before a destructive save.", ex);
        }
    }

    private static string
    BuildBackupPath(
        string filePath)
    {
        var folder = Path.GetDirectoryName(filePath) ?? "";
        var backupFolder = Path.Combine(folder, "Backup");

        Directory.CreateDirectory(backupFolder);

        var name = Path.GetFileNameWithoutExtension(filePath);
        var ext = Path.GetExtension(filePath);
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");

        var backupPath = Path.Combine(backupFolder, $"{name}_{timestamp}{ext}");
        var counter = 1;

        while (File.Exists(backupPath))
        {
            backupPath = Path.Combine(backupFolder, $"{name}_{timestamp}_{++counter}{ext}");
        }

        return backupPath;
    }
}

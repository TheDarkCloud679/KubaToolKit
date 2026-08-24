using KubaToolKit.Modules.ApiClient;
using KubaToolKit.Modules.AtlassianSearch;
using KubaToolKit.Shared.Services;
using KubaToolKit.Shared.Windows;
using System.IO;
using System.Windows;

namespace KubaToolKit.Shell;

public partial class TeamSharingSettingsWindow
    : Window
{
    private readonly TeamSharingSettingsService _settingsService = new();

    public TeamSharingSettingsWindow()
    {
        InitializeComponent();

        SharedFolderTextBox.Text = _settingsService.Load().SharedFolder;
    }

    private void
    BrowseButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose a synced folder for the shared team data"
        };

        if (!string.IsNullOrWhiteSpace(SharedFolderTextBox.Text) && Directory.Exists(SharedFolderTextBox.Text))
        {
            dialog.InitialDirectory = SharedFolderTextBox.Text;
        }

        if (dialog.ShowDialog(this) == true)
        {
            SharedFolderTextBox.Text = dialog.FolderName;
        }
    }

    private void
    CancelButton_Click(
        object sender,
        RoutedEventArgs e) =>
        Close();

    // Each service resolves its own effective folder off whatever's
    // already saved -- reading them before AND after Save (rather than
    // duplicating that resolution logic here) is what lets this migrate
    // existing content without needing to know each module's default
    // path or shared-subfolder name itself.
    private void
    SaveButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var newSharedFolder = SharedFolderTextBox.Text.Trim();

        var oldIncidentsFolder = new IncidentLibraryStorageService().RootFolder;
        var oldWikiFolder = WikiFolderPath();
        var oldCollectionsFolder = CollectionStorageService.CollectionsFolder;

        _settingsService.Save(new TeamSharingSettings { SharedFolder = newSharedFolder });

        var newIncidentsFolder = new IncidentLibraryStorageService().RootFolder;
        var newWikiFolder = WikiFolderPath();
        var newCollectionsFolder = CollectionStorageService.CollectionsFolder;

        var migrated = new List<string>();

        if (!string.Equals(oldIncidentsFolder, newIncidentsFolder, StringComparison.OrdinalIgnoreCase))
        {
            MigrateIncidents(oldIncidentsFolder, newIncidentsFolder, migrated);
        }

        if (!string.Equals(oldWikiFolder, newWikiFolder, StringComparison.OrdinalIgnoreCase))
        {
            TryCopy("Wiki", oldWikiFolder, newWikiFolder, migrated);
        }

        if (!string.Equals(oldCollectionsFolder, newCollectionsFolder, StringComparison.OrdinalIgnoreCase))
        {
            TryCopy("API Client collections", oldCollectionsFolder, newCollectionsFolder, migrated);
        }

        if (migrated.Count > 0)
        {
            AppMessageBox.Show(
                "Copied into the shared folder:\n\n" + string.Join("\n", migrated)
                + "\n\nRestart KubaToolKit for the shared folder to take effect everywhere.",
                "Team Sharing");
        }
        else if (!string.Equals(oldIncidentsFolder, newIncidentsFolder, StringComparison.OrdinalIgnoreCase)
                 || !string.Equals(oldWikiFolder, newWikiFolder, StringComparison.OrdinalIgnoreCase)
                 || !string.Equals(oldCollectionsFolder, newCollectionsFolder, StringComparison.OrdinalIgnoreCase))
        {
            AppMessageBox.Show(
                "Restart KubaToolKit for the shared folder to take effect everywhere.",
                "Team Sharing");
        }

        Close();
    }

    // Every project's per-project data (Project Info's own "Files
    // folder", per-project sections) isn't included here yet -- its
    // per-project-key folder structure needs a bit more thought about
    // what exactly should be shared before it gets the same treatment.
    private static string
    WikiFolderPath() =>
        Modules.Wiki.WikiService.GetLibraryFolderPath();

    private static void
    MigrateIncidents(
        string oldFolder,
        string newFolder,
        List<string> migrated)
    {
        try
        {
            var hasExisting = Directory.Exists(oldFolder) && Directory.GetFiles(oldFolder, "*.json").Length > 0;

            if (!hasExisting)
            {
                return;
            }

            if (Directory.Exists(newFolder) && Directory.EnumerateFileSystemEntries(newFolder).Any())
            {
                // Something's already there (a colleague's data, most
                // likely) -- never merge/overwrite automatically.
                return;
            }

            // Both pinned to a specific folder via OverrideRootFolder --
            // by this point Team Sharing settings are already saved, so a
            // plain (unoverridden) instance would resolve to the NEW
            // folder for both the export and the import below.
            var oldStorage = new IncidentLibraryStorageService { OverrideRootFolder = oldFolder };
            var newStorage = new IncidentLibraryStorageService { OverrideRootFolder = newFolder };

            var tempFile = Path.GetTempFileName();

            oldStorage.ExportLibrary(tempFile);

            var importedCount = newStorage.ImportLibrary(tempFile);

            File.Delete(tempFile);

            migrated.Add($"- {importedCount} incident(s)");
        }
        catch (Exception ex)
        {
            Logger.Error("TeamSharingSettingsWindow: failed to migrate incidents.", ex);
        }
    }

    private static void
    TryCopy(
        string label,
        string oldFolder,
        string newFolder,
        List<string> migrated)
    {
        try
        {
            var copiedCount = TeamSharingFolders.TryCopyIfDestinationEmpty(oldFolder, newFolder);

            if (copiedCount > 0)
            {
                migrated.Add($"- {label}");
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"TeamSharingSettingsWindow: failed to copy {label}.", ex);
        }
    }
}

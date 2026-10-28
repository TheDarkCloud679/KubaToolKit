using KubaToolKit.Shared.Services;
using KubaToolKit.Shared.Windows;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace KubaToolKit.Modules.TableViewer;

// QTable is a native (non-.NET) external tool: its --def-file/--text/--html
// flags are documented inside the binary (via `strings`) but, as tested,
// don't reliably produce headless output -- every invocation we've tried
// just opens QTable's own window. "Try automatic export" below is still a
// real, best-effort attempt (not a stub): it launches QTable with the
// flags its own help text describes, then watches the table file's folder
// for a new .txt/.html file for a few seconds. When QTable (a newer build,
// or a different flag combination) does write one, this picks it up
// automatically; when it doesn't, the status message says so plainly
// instead of pretending it worked, so this can keep being adjusted as we
// learn more about what actually works.
public partial class TableViewerView : UserControl
{
    private static readonly TimeSpan ExportWaitTimeout = TimeSpan.FromSeconds(8);

    private readonly TableViewerSettingsService _settingsService = new();

    private readonly List<string> _tableFiles = new();

    public TableViewerView()
    {
        InitializeComponent();
    }

    private string?
    SelectedTableFilePath =>
        TableFilesListBox.SelectedItem as string;

    private void
    RefreshTableFilesList()
    {
        var previouslySelected = SelectedTableFilePath;

        TableFilesListBox.ItemsSource = null;
        TableFilesListBox.ItemsSource = _tableFiles;

        TableFilesListBox.SelectedItem =
            previouslySelected != null && _tableFiles.Contains(previouslySelected)
                ? previouslySelected
                : _tableFiles.FirstOrDefault();
    }

    private void
    AddTableFileButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var menu = AddTableFileButton.ContextMenu;

        // Opening a Button's ContextMenu from a Click handler (rather than
        // the usual right-click path) skips WPF's own placement logic, so
        // without this it anchors to the screen origin instead of the
        // button.
        menu.PlacementTarget = AddTableFileButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void
    AddTableFile_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose binary table file(s)",
            Filter = "Table files (*.dat)|*.dat|All files (*.*)|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        foreach (var file in dialog.FileNames)
        {
            if (!_tableFiles.Contains(file))
            {
                _tableFiles.Add(file);
            }
        }

        RefreshTableFilesList();
    }

    private void
    AddTableFolder_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose a folder of table files" };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var filesInFolder = Directory.GetFiles(dialog.FolderName);

        if (filesInFolder.Length == 0)
        {
            AppMessageBox.Show("That folder has no files in it.", "Table Viewer");

            return;
        }

        foreach (var file in filesInFolder)
        {
            if (!_tableFiles.Contains(file))
            {
                _tableFiles.Add(file);
            }
        }

        RefreshTableFilesList();
    }

    private void
    RemoveTableFile_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (SelectedTableFilePath == null)
        {
            return;
        }

        _tableFiles.Remove(SelectedTableFilePath);

        RefreshTableFilesList();
    }

    private void
    SettingsButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var window = new TableViewerSettingsWindow { Owner = Window.GetWindow(this) };

        window.ShowDialog();
    }

    private void
    BrowseDefinitionFile_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a definition file",
            Filter = "XML definition files (*.xml)|*.xml|All files (*.*)|*.*"
        };

        var qtableFolder = Path.GetDirectoryName(_settingsService.Load().QTableExePath);
        var xmlFolder = qtableFolder != null ? Path.Combine(qtableFolder, "xml") : null;

        if (xmlFolder != null && Directory.Exists(xmlFolder))
        {
            dialog.InitialDirectory = xmlFolder;
        }

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        DefinitionFilePathTextBox.Text = dialog.FileName;
    }

    private void
    OpenInQTable_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!TryGetQTableExePath(out var exePath))
        {
            return;
        }

        var args = new List<string>();

        if (SelectedTableFilePath != null)
        {
            args.Add(SelectedTableFilePath);
        }

        ShowCommandPreview(exePath, args);

        try
        {
            StartQTable(exePath, args);

            StatusText.Text = "QTable opened.";
        }
        catch (Exception ex)
        {
            Logger.Error("TableViewerView: failed to launch QTable.exe.", ex);

            StatusText.Text = $"Could not launch QTable.exe: {ex.Message}";
        }
    }

    private async void
    TryExport_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!TryGetQTableExePath(out var exePath))
        {
            return;
        }

        if (SelectedTableFilePath == null || !File.Exists(SelectedTableFilePath))
        {
            AppMessageBox.Show("Choose a binary table file first.", "Table Viewer");

            return;
        }

        var tableFilePath = SelectedTableFilePath;
        var tableFolder = Path.GetDirectoryName(tableFilePath) ?? ".";

        var args = new List<string>();

        if (!string.IsNullOrWhiteSpace(DefinitionFilePathTextBox.Text))
        {
            args.Add("--def-file");
            args.Add(DefinitionFilePathTextBox.Text);
        }

        args.Add("--text");
        args.Add(tableFilePath);

        ShowCommandPreview(exePath, args);

        TryExportButton.IsEnabled = false;
        StatusText.Text = "Launching QTable and watching for an exported file...";

        try
        {
            // Snapshot both the table file's own folder and QTable's
            // install folder -- either is a plausible place for it to
            // drop an export next to.
            var qtableFolder = Path.GetDirectoryName(exePath) ?? ".";

            var before = SnapshotExportCandidates(tableFolder, qtableFolder);

            StartQTable(exePath, args);

            var found = await WaitForNewExportFileAsync(tableFolder, qtableFolder, before, ExportWaitTimeout);

            if (found == null)
            {
                StatusText.Text =
                    "No automatic export was detected after launching QTable. QTable's own window has "
                    + "opened -- check there whether anything was written, and let me know what you see "
                    + "so the command above can be adjusted.";

                return;
            }

            var content = await File.ReadAllTextAsync(found);

            OutputTextBox.Text = content;
            OutputTextBox.Tag = found;
            OpenInViewerButton.IsEnabled = true;

            StatusText.Text = $"Export detected: {found}";
        }
        catch (Exception ex)
        {
            Logger.Error("TableViewerView: automatic export attempt failed.", ex);

            StatusText.Text = $"Automatic export failed: {ex.Message}";
        }
        finally
        {
            TryExportButton.IsEnabled = true;
        }
    }

    private void
    OpenInViewer_Click(
        object sender,
        RoutedEventArgs e)
    {
        var path = OutputTextBox.Tag as string;

        var title = path != null ? Path.GetFileName(path) : "Exported content";

        new FileViewerWindow(title, OutputTextBox.Text, path).Show();
    }

    private bool
    TryGetQTableExePath(
        out string exePath)
    {
        exePath = _settingsService.Load().QTableExePath.Trim();

        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            AppMessageBox.Show(
                "QTable.exe wasn't found. Use the settings (gear icon) to locate it.",
                "Table Viewer");

            return false;
        }

        return true;
    }

    private static void
    StartQTable(
        string exePath,
        List<string> args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = false
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        Process.Start(startInfo);
    }

    private void
    ShowCommandPreview(
        string exePath,
        List<string> args)
    {
        var quoted = args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a);

        CommandPreviewText.Text = $"\"{exePath}\" {string.Join(" ", quoted)}";
    }

    // One snapshot per candidate folder: the set of .txt/.html files
    // already there (with their last-write time) before launching QTable,
    // so a later pass can tell a pre-existing file from a freshly written
    // one.
    private static Dictionary<string, DateTime>
    SnapshotExportCandidates(
        params string[] folders)
    {
        var snapshot = new Dictionary<string, DateTime>();

        foreach (var folder in folders.Distinct())
        {
            if (!Directory.Exists(folder))
            {
                continue;
            }

            foreach (var file in EnumerateExportCandidates(folder))
            {
                snapshot[file] = File.GetLastWriteTimeUtc(file);
            }
        }

        return snapshot;
    }

    private static IEnumerable<string>
    EnumerateExportCandidates(
        string folder) =>
        Directory.EnumerateFiles(folder, "*.txt").Concat(Directory.EnumerateFiles(folder, "*.html"));

    private static async Task<string?>
    WaitForNewExportFileAsync(
        string folderA,
        string folderB,
        Dictionary<string, DateTime> before,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            foreach (var folder in new[] { folderA, folderB }.Distinct())
            {
                if (!Directory.Exists(folder))
                {
                    continue;
                }

                foreach (var file in EnumerateExportCandidates(folder))
                {
                    var writeTime = File.GetLastWriteTimeUtc(file);

                    var isNewOrChanged =
                        !before.TryGetValue(file, out var previousWriteTime) || writeTime > previousWriteTime;

                    if (isNewOrChanged && IsFileReady(file))
                    {
                        return file;
                    }
                }
            }

            await Task.Delay(500);
        }

        return null;
    }

    // QTable may still be writing the file when we first see it appear --
    // a plain open-for-read that doesn't throw is enough to tell it's done.
    private static bool
    IsFileReady(
        string file)
    {
        try
        {
            using var stream = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            return stream.Length > 0;
        }
        catch (IOException)
        {
            return false;
        }
    }
}

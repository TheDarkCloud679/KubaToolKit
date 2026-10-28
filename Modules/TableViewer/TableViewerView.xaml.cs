using KubaToolKit.Modules.TableViewer.Decoding;
using KubaToolKit.Shared.Services;
using KubaToolKit.Shared.Windows;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace KubaToolKit.Modules.TableViewer;

// Decodes proprietary binary table files natively, the same way QTable
// itself does -- reading each file's own XML definition (field by field,
// honoring <if>/<switch>/<array> exactly as QTable's engine would) --
// rather than shelling out to QTable.exe.
public partial class TableViewerView : UserControl
{
    private readonly TableViewerSettingsService _settingsService = new();

    private readonly List<TableFileItem> _tableFiles = new();

    public TableViewerView()
    {
        InitializeComponent();
    }

    private TableFileItem?
    SelectedTableFile =>
        TableFilesListBox.SelectedItem as TableFileItem;

    private void
    RefreshTableFilesList()
    {
        var previouslySelected = SelectedTableFile?.Path;

        TableFilesListBox.ItemsSource = null;
        TableFilesListBox.ItemsSource = _tableFiles;

        TableFilesListBox.SelectedItem =
            _tableFiles.FirstOrDefault(f => f.Path == previouslySelected) ?? _tableFiles.FirstOrDefault();
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
            // Proprietary format, not .dat -- extensions vary (e.g.
            // ADV00004.D010), so default to showing everything rather
            // than guessing a pattern that would hide real files.
            Filter = "All files (*.*)|*.*|Proprietary table files (*.D*)|*.D*",
            Multiselect = true
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        AddFiles(dialog.FileNames);
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

        AddFiles(filesInFolder);
    }

    private void
    AddFiles(
        IEnumerable<string> files)
    {
        var existing = _tableFiles.Select(f => f.Path).ToHashSet();

        foreach (var file in files)
        {
            if (existing.Add(file))
            {
                _tableFiles.Add(new TableFileItem { Path = file });
            }
        }

        RefreshTableFilesList();
    }

    private void
    RemoveTableFile_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (SelectedTableFile == null)
        {
            return;
        }

        _tableFiles.Remove(SelectedTableFile);

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
    Decode_Click(
        object sender,
        RoutedEventArgs e)
    {
        var checkedFiles = _tableFiles.Where(f => f.IsChecked).ToList();

        if (checkedFiles.Count == 0)
        {
            AppMessageBox.Show("Check at least one file to decode.", "Table Viewer");

            return;
        }

        var settings = _settingsService.Load();
        var combinedFields = new List<DecodedField>();
        var combinedNotes = new List<string>();
        var decodedCount = 0;
        var totalValues = 0;

        foreach (var file in checkedFiles)
        {
            TableDecodeResult result;

            try
            {
                result =
                    File.Exists(file.Path)
                        ? TableFileDecoder.Decode(file.Path, settings)
                        : TableDecodeResult.Failure("File no longer exists.");
            }
            catch (Exception ex)
            {
                Logger.Error($"TableViewerView: decode failed for '{file.Path}'.", ex);

                result = TableDecodeResult.Failure($"Decode failed: {ex.Message}");
            }

            // A header row per file -- shows up/down front whether it
            // decoded at all, and (when several files are checked) keeps
            // their fields visually grouped instead of one flat list.
            combinedFields.Add(
                new DecodedField
                {
                    Name = file.FileName,
                    Value =
                        result.Success
                            ? $"{result.Trigram} - {result.Fields.Count} value(s)"
                            : result.ErrorMessage ?? "Failed",
                    Type = "FILE",
                    Depth = 0
                });

            if (!result.Success)
            {
                combinedNotes.Add($"{file.FileName}: {result.ErrorMessage}");

                continue;
            }

            decodedCount++;
            totalValues += result.Fields.Count;

            combinedFields.AddRange(result.Fields.Select(f => f.WithDepthOffset(1)));

            combinedNotes.AddRange(
                checkedFiles.Count > 1 ? result.Notes.Select(n => $"{file.FileName}: {n}") : result.Notes);
        }

        ResultsGrid.ItemsSource = combinedFields;

        if (combinedNotes.Count > 0)
        {
            NotesList.ItemsSource = combinedNotes;
            NotesPanel.Visibility = Visibility.Visible;
        }
        else
        {
            NotesPanel.Visibility = Visibility.Collapsed;
        }

        StatusText.Text =
            checkedFiles.Count == 1
                ? (decodedCount == 1 ? $"Decoded {totalValues} value(s)." : "Decode failed -- see above.")
                : $"Decoded {decodedCount} of {checkedFiles.Count} file(s), {totalValues} value(s) total.";
    }
}

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
    Decode_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (SelectedTableFilePath == null || !File.Exists(SelectedTableFilePath))
        {
            AppMessageBox.Show("Choose a binary table file first.", "Table Viewer");

            return;
        }

        var settings = _settingsService.Load();

        TableDecodeResult result;

        try
        {
            result = TableFileDecoder.Decode(SelectedTableFilePath, settings);
        }
        catch (Exception ex)
        {
            Logger.Error("TableViewerView: decode failed.", ex);

            AppMessageBox.Show($"Decode failed: {ex.Message}", "Table Viewer");

            return;
        }

        if (!result.Success)
        {
            ResultsGrid.ItemsSource = null;
            NotesPanel.Visibility = Visibility.Collapsed;

            StatusText.Text = result.ErrorMessage;

            return;
        }

        ResultsGrid.ItemsSource = result.Fields;

        if (result.Notes.Count > 0)
        {
            NotesList.ItemsSource = result.Notes;
            NotesPanel.Visibility = Visibility.Visible;
        }
        else
        {
            NotesPanel.Visibility = Visibility.Collapsed;
        }

        StatusText.Text =
            $"Decoded as '{result.Trigram}' from {Path.GetFileName(result.DefinitionsFile)} -- {result.Fields.Count} value(s).";
    }
}

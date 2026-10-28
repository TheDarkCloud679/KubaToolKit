using KubaToolKit.Modules.TableViewer.Decoding;
using KubaToolKit.Shared.Services;
using KubaToolKit.Shared.Windows;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KubaToolKit.Modules.TableViewer;

// Decodes proprietary binary table files natively, the same way QTable
// itself does -- reading each file's own XML definition (field by field,
// honoring <if>/<switch>/<array> exactly as QTable's engine would) --
// rather than shelling out to QTable.exe.
public partial class TableViewerView : UserControl
{
    private static readonly Regex TableFileExtensionPattern =
        new(@"^\.D\d{3}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly TableViewerSettingsService _settingsService = new();
    private readonly TableViewerValueLabelsService _valueLabelsService = new();

    private readonly List<TableFileItem> _tableFiles = new();

    private CancellationTokenSource? _decodeCts;

    public TableViewerView()
    {
        InitializeComponent();

        // Not set via IsChecked="True" in XAML: that fires the Checked
        // event (-> RefreshResultsView) while InitializeComponent is
        // still connecting later-declared named elements, which crashed
        // the whole app on startup (ResultsGrid/NotesPanel were still
        // null at that point).
        ResultsViewGridRadio.IsChecked = true;
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

        // A real table-file folder is a mix of .D### data files and
        // unrelated stuff (logs, exports, whatever else lives there) --
        // only the former are table files QTable itself would open.
        var filesInFolder =
            Directory.GetFiles(dialog.FolderName)
                .Where(f => TableFileExtensionPattern.IsMatch(Path.GetExtension(f)))
                .ToArray();

        if (filesInFolder.Length == 0)
        {
            AppMessageBox.Show("That folder has no .D### table files in it.", "Table Viewer");

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
    TableViewerView_DragOver(
        object sender,
        DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            TableFilesDropOverlay.Visibility = Visibility.Visible;
        }
        else
        {
            e.Effects = DragDropEffects.None;
            TableFilesDropOverlay.Visibility = Visibility.Collapsed;
        }

        e.Handled = true;
    }

    private void
    TableViewerView_DragLeave(
        object sender,
        DragEventArgs e) =>
        TableFilesDropOverlay.Visibility = Visibility.Collapsed;

    private void
    TableViewerView_Drop(
        object sender,
        DragEventArgs e)
    {
        TableFilesDropOverlay.Visibility = Visibility.Collapsed;

        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        var dropped = (string[])e.Data.GetData(DataFormats.FileDrop);
        var toAdd = new List<string>();

        foreach (var path in dropped)
        {
            if (Directory.Exists(path))
            {
                // Same filter as "Add folder...": only the real table
                // files, not whatever else lives alongside them.
                toAdd.AddRange(
                    Directory.GetFiles(path).Where(f => TableFileExtensionPattern.IsMatch(Path.GetExtension(f))));
            }
            else if (File.Exists(path))
            {
                toAdd.Add(path);
            }
        }

        if (toAdd.Count == 0)
        {
            AppMessageBox.Show("No .D### table files found in what was dropped.", "Table Viewer");

            return;
        }

        AddFiles(toAdd);
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

    private async void
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

        _decodeCts = new CancellationTokenSource();

        DecodeButton.Visibility = Visibility.Collapsed;
        CancelDecodeButton.Visibility = Visibility.Visible;
        DecodeProgressBar.Visibility = Visibility.Visible;
        DecodeProgressBar.Maximum = checkedFiles.Count;
        DecodeProgressBar.Value = 0;
        StatusText.Text = $"Decoding 0 / {checkedFiles.Count}...";
        ResultsSummaryListBox.ItemsSource = null;
        ResultsGrid.ItemsSource = null;
        NotesPanel.Visibility = Visibility.Collapsed;

        var progress =
            new Progress<(int Done, string FileName)>(
                p =>
                {
                    DecodeProgressBar.Value = p.Done;
                    StatusText.Text = $"Decoding {p.Done} / {checkedFiles.Count} -- {p.FileName}...";
                });

        List<FileDecodeResultItem> results;
        bool cancelled;

        try
        {
            // The definitions XML (and any <table> it resolves) is
            // cached after its first use, so running this off the UI
            // thread is what actually keeps the window responsive across
            // a large batch -- the per-file work itself is plain
            // synchronous file IO and XML walking. DecodeAll stops and
            // returns gracefully on cancellation rather than throwing, so
            // whatever already decoded is kept.
            results = await Task.Run(() => DecodeAll(checkedFiles, settings, progress, _decodeCts.Token));
            cancelled = _decodeCts.IsCancellationRequested;
        }
        finally
        {
            DecodeButton.Visibility = Visibility.Visible;
            CancelDecodeButton.Visibility = Visibility.Collapsed;
            DecodeProgressBar.Visibility = Visibility.Collapsed;
            _decodeCts?.Dispose();
            _decodeCts = null;
        }

        ResultsSummaryListBox.ItemsSource = results;

        var succeeded = results.Count(r => r.Result.Success);
        var totalValues = results.Where(r => r.Result.Success).Sum(r => r.Result.Fields.Count);

        StatusText.Text =
            cancelled
                ? $"Cancelled -- decoded {succeeded} of {checkedFiles.Count} file(s) before stopping."
                : checkedFiles.Count == 1
                    ? (succeeded == 1 ? $"Decoded {totalValues} value(s)." : "Decode failed -- see the list.")
                    : $"Decoded {succeeded} of {checkedFiles.Count} file(s), {totalValues} value(s) total.";

        ResultsSummaryListBox.SelectedItem = results.FirstOrDefault(r => r.Result.Success) ?? results.FirstOrDefault();
    }

    private void
    CancelDecodeButton_Click(
        object sender,
        RoutedEventArgs e) =>
        _decodeCts?.Cancel();

    private static List<FileDecodeResultItem>
    DecodeAll(
        List<TableFileItem> files,
        TableViewerSettings settings,
        IProgress<(int Done, string FileName)> progress,
        CancellationToken cancellationToken)
    {
        var results = new List<FileDecodeResultItem>(files.Count);
        var done = 0;

        foreach (var file in files)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            progress.Report((done, file.FileName));

            TableDecodeResult result;

            try
            {
                result =
                    File.Exists(file.Path)
                        ? TableFileDecoder.Decode(file.Path, settings, cancellationToken)
                        : TableDecodeResult.Failure("File no longer exists.");
            }
            catch (Exception ex)
            {
                Logger.Error($"TableViewerView: decode failed for '{file.Path}'.", ex);

                result = TableDecodeResult.Failure($"Decode failed: {ex.Message}");
            }

            results.Add(new FileDecodeResultItem { FileName = file.FileName, Result = result });

            done++;
            progress.Report((done, file.FileName));
        }

        return results;
    }

    private void
    ResultsSummaryListBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e) =>
        RefreshResultsView();

    private void
    ResultsViewMode_Changed(
        object sender,
        RoutedEventArgs e) =>
        RefreshResultsView();

    private void
    RefreshResultsView()
    {
        if (ResultsSummaryListBox == null
            || ResultsGrid == null
            || ResultsCardsContent == null
            || ResultsCardsScroll == null
            || NotesPanel == null)
        {
            return;
        }

        var selected = ResultsSummaryListBox.SelectedItem as FileDecodeResultItem;

        if (selected == null)
        {
            ResultsGrid.ItemsSource = null;
            ResultsCardsContent.Content = null;
            NotesPanel.Visibility = Visibility.Collapsed;

            return;
        }

        if (selected.Result.Notes.Count > 0)
        {
            NotesList.ItemsSource = selected.Result.Notes;
            NotesPanel.Visibility = Visibility.Visible;
        }
        else
        {
            NotesPanel.Visibility = Visibility.Collapsed;
        }

        if (ResultsViewGridRadio?.IsChecked == true)
        {
            ResultsGrid.Visibility = Visibility.Visible;
            ResultsCardsScroll.Visibility = Visibility.Collapsed;
            ResultsGrid.ItemsSource = selected.Result.Fields;

            return;
        }

        ResultsGrid.Visibility = Visibility.Collapsed;
        ResultsCardsScroll.Visibility = Visibility.Visible;

        var valueLabels = _valueLabelsService.Load();

        ValueLabelsWarningIcon.Visibility =
            _valueLabelsService.LastError != null ? Visibility.Visible : Visibility.Collapsed;

        ValueLabelsWarningIcon.ToolTip =
            _valueLabelsService.LastError != null
                ? $"Invalid table-viewer-value-labels.json, no labels applied: {_valueLabelsService.LastError}"
                : null;

        ResultsCardsContent.Content =
            TableCardViewBuilder.Build(selected.Result.Fields, valueLabels)
            ?? new TextBlock
            {
                Text = "No fields decoded.",
                FontStyle = FontStyles.Italic,
                Foreground = (Brush)FindResource("TextMutedBrush"),
                Margin = new Thickness(8)
            };
    }

    private void
    OpenValueLabels_Click(
        object sender,
        RoutedEventArgs e)
    {
        _valueLabelsService.Load();

        Process.Start(
            new ProcessStartInfo
            {
                FileName = TableViewerValueLabelsService.ValueLabelsFile,
                UseShellExecute = true
            });
    }
}

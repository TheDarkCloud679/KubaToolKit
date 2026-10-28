using KubaToolKit.Shared.Services;
using System.IO;
using System.Windows;

namespace KubaToolKit.Modules.TableViewer;

public partial class TableViewerSettingsWindow
    : Window
{
    private readonly TableViewerSettingsService _settingsService = new();

    private readonly List<string> _definitionFiles;

    public TableViewerSettingsWindow()
    {
        InitializeComponent();

        var settings = _settingsService.Load();

        QTableExePathTextBox.Text = settings.QTableExePath;

        _definitionFiles = new List<string>(settings.DefaultDefinitionFiles);

        RefreshDefinitionFilesList();
    }

    private void
    RefreshDefinitionFilesList()
    {
        DefinitionFilesListBox.ItemsSource = null;
        DefinitionFilesListBox.ItemsSource = _definitionFiles;
    }

    private void
    AddDefinitionFile_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose definition file(s)",
            Filter = "XML definition files (*.xml)|*.xml|All files (*.*)|*.*",
            Multiselect = true
        };

        var qtableFolder = Path.GetDirectoryName(QTableExePathTextBox.Text);
        var xmlFolder = qtableFolder != null ? Path.Combine(qtableFolder, "xml") : null;

        if (xmlFolder != null && Directory.Exists(xmlFolder))
        {
            dialog.InitialDirectory = xmlFolder;
        }

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        foreach (var file in dialog.FileNames)
        {
            if (!_definitionFiles.Contains(file))
            {
                _definitionFiles.Add(file);
            }
        }

        RefreshDefinitionFilesList();
    }

    private void
    RemoveDefinitionFile_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (DefinitionFilesListBox.SelectedItem is not string selected)
        {
            return;
        }

        _definitionFiles.Remove(selected);

        RefreshDefinitionFilesList();
    }

    private void
    BrowseButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Locate QTable.exe",
            Filter = "QTable (QTable.exe)|QTable.exe|Executable files (*.exe)|*.exe|All files (*.*)|*.*"
        };

        if (File.Exists(QTableExePathTextBox.Text))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(QTableExePathTextBox.Text);
        }

        if (dialog.ShowDialog(this) == true)
        {
            QTableExePathTextBox.Text = dialog.FileName;
        }
    }

    private void
    CancelButton_Click(
        object sender,
        RoutedEventArgs e) =>
        Close();

    private void
    SaveButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _settingsService.Save(
            new TableViewerSettings
            {
                QTableExePath = QTableExePathTextBox.Text.Trim(),
                DefaultDefinitionFiles = _definitionFiles
            });

        Close();
    }
}

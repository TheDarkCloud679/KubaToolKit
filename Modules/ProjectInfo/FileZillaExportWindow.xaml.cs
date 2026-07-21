using KubaToolKit.Modules.ProjectInfo.Models;
using System.IO;
using System.Windows;

namespace KubaToolKit.Modules.ProjectInfo;

public partial class FileZillaExportWindow
    : Window
{
    private bool _confirmed;

    public FileZillaExportWindow(
        List<string> columns,
        FileZillaExportSettings? existing,
        string defaultFolderName,
        string initialKeyFileDirectory)
    {
        InitializeComponent();

        NameColumnCombo.ItemsSource = columns;
        HostColumnCombo.ItemsSource = columns;

        NameColumnCombo.SelectedItem =
            existing != null && columns.Contains(existing.NameColumn)
                ? existing.NameColumn
                : columns.FirstOrDefault();

        HostColumnCombo.SelectedItem =
            existing != null && columns.Contains(existing.HostColumn)
                ? existing.HostColumn
                : columns.FirstOrDefault();

        UsernameTextBox.Text = existing?.Username ?? "";
        PortTextBox.Text = string.IsNullOrWhiteSpace(existing?.Port) ? "22" : existing!.Port;
        KeyFileTextBox.Text = existing?.KeyFilePath ?? "";
        FolderNameTextBox.Text = string.IsNullOrWhiteSpace(existing?.FolderName) ? defaultFolderName : existing!.FolderName;

        _initialKeyFileDirectory = initialKeyFileDirectory;
    }

    private readonly string _initialKeyFileDirectory = "";

    public FileZillaExportSettings? Result { get; private set; }

    public static FileZillaExportSettings?
    Prompt(
        Window? owner,
        List<string> columns,
        FileZillaExportSettings? existing,
        string defaultFolderName,
        string initialKeyFileDirectory)
    {
        var window = new FileZillaExportWindow(columns, existing, defaultFolderName, initialKeyFileDirectory)
        {
            Owner = owner
        };

        window.ShowDialog();

        return window._confirmed ? window.Result : null;
    }

    private void
    BrowseKeyFile_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Key files (*.ppk;*.pem)|*.ppk;*.pem|All files (*.*)|*.*"
        };

        if (Directory.Exists(_initialKeyFileDirectory))
        {
            dialog.InitialDirectory = _initialKeyFileDirectory;
        }

        if (dialog.ShowDialog(this) == true)
        {
            KeyFileTextBox.Text = dialog.FileName;
        }
    }

    private void
    ExportButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (NameColumnCombo.SelectedItem is not string nameColumn
            || HostColumnCombo.SelectedItem is not string hostColumn)
        {
            MessageBox.Show("Pick a column for the name and one for the host/IP.", "Export to FileZilla");

            return;
        }

        if (string.IsNullOrWhiteSpace(FolderNameTextBox.Text))
        {
            MessageBox.Show("Enter a folder name.", "Export to FileZilla");

            return;
        }

        if (!int.TryParse(PortTextBox.Text.Trim(), out _))
        {
            MessageBox.Show("Port must be a number.", "Export to FileZilla");

            return;
        }

        Result = new FileZillaExportSettings
        {
            NameColumn = nameColumn,
            HostColumn = hostColumn,
            Username = UsernameTextBox.Text.Trim(),
            Port = PortTextBox.Text.Trim(),
            KeyFilePath = KeyFileTextBox.Text.Trim(),
            FolderName = FolderNameTextBox.Text.Trim()
        };

        _confirmed = true;

        Close();
    }

    private void
    CancelButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }
}

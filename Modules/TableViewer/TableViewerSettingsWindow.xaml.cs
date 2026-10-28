using KubaToolKit.Shared.Services;
using System.IO;
using System.Windows;

namespace KubaToolKit.Modules.TableViewer;

public partial class TableViewerSettingsWindow
    : Window
{
    private readonly TableViewerSettingsService _settingsService = new();

    public TableViewerSettingsWindow()
    {
        InitializeComponent();

        QTableExePathTextBox.Text = _settingsService.Load().QTableExePath;
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
        _settingsService.Save(new TableViewerSettings { QTableExePath = QTableExePathTextBox.Text.Trim() });

        Close();
    }
}

using KubaToolKit.Shared.Services;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace KubaToolKit.Modules.TableViewer;

public partial class TableViewerSettingsWindow
    : Window
{
    private readonly TableViewerSettingsService _settingsService = new();

    public TableViewerSettingsWindow()
    {
        InitializeComponent();

        var settings = _settingsService.Load();

        TablesXmlTextBox.Text = settings.TablesDefinitionsXmlPath;
        StatsXmlTextBox.Text = settings.StatsDefinitionsXmlPath;
        DumpsXmlTextBox.Text = settings.DumpsDefinitionsXmlPath;
    }

    private void
    BrowseTablesXml_Click(
        object sender,
        RoutedEventArgs e) =>
        Browse(TablesXmlTextBox);

    private void
    BrowseStatsXml_Click(
        object sender,
        RoutedEventArgs e) =>
        Browse(StatsXmlTextBox);

    private void
    BrowseDumpsXml_Click(
        object sender,
        RoutedEventArgs e) =>
        Browse(DumpsXmlTextBox);

    private void
    Browse(
        TextBox target)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a definitions file",
            Filter = "XML definitions (*.xml)|*.xml|All files (*.*)|*.*"
        };

        if (File.Exists(target.Text))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(target.Text);
        }

        if (dialog.ShowDialog(this) == true)
        {
            target.Text = dialog.FileName;
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
                TablesDefinitionsXmlPath = TablesXmlTextBox.Text.Trim(),
                StatsDefinitionsXmlPath = StatsXmlTextBox.Text.Trim(),
                DumpsDefinitionsXmlPath = DumpsXmlTextBox.Text.Trim()
            });

        Close();
    }
}

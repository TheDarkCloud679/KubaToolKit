using KubaToolKit.Shared.ConfigExport;
using KubaToolKit.Shared.Services;
using KubaToolKit.Shared.Windows;
using System.Windows;

namespace KubaToolKit.Shell;

public partial class ConfigExportWindow
    : Window
{
    private readonly ConfigExportService _service = new();

    public ConfigExportWindow()
    {
        InitializeComponent();
    }

    private void
    ExportButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog =
            new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json",
                FileName = $"KubaToolKit export {DateTime.Now:yyyy-MM-dd}.json"
            };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var bundle = _service.BuildBundle();

            _service.Export(bundle, dialog.FileName);

            AppMessageBox.Show(
                $"Exported {bundle.Collections.Count} collection(s), {bundle.Environments.Count} environment(s), "
                + $"{bundle.WikiSections.Count} wiki page(s), and {bundle.Incidents.Count} incident(s) "
                + $"to \"{dialog.FileName}\".",
                "Export configuration");
        }
        catch (Exception ex)
        {
            Logger.Error("ConfigExportWindow: export failed.", ex);

            AppMessageBox.Show(ex.Message, "Export error");
        }
    }

    private void
    ImportButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "JSON files (*.json)|*.json" };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var summary = _service.Import(dialog.FileName);

            AppMessageBox.Show(summary.ToDisplayText(), "Import configuration");
        }
        catch (Exception ex)
        {
            Logger.Error("ConfigExportWindow: import failed.", ex);

            AppMessageBox.Show(ex.Message, "Import error");
        }
    }
}

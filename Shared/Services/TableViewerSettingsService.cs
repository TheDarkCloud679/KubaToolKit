using System.IO;
using System.Text.Json;

namespace KubaToolKit.Shared.Services;

public class TableViewerSettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new() { WriteIndented = true };

    public static string
    GetFilePath() =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "table-viewer.json");

    public TableViewerSettings
    Load()
    {
        var filePath = GetFilePath();

        if (!File.Exists(filePath))
        {
            return new TableViewerSettings();
        }

        try
        {
            var json = File.ReadAllText(filePath);

            return
                JsonSerializer.Deserialize<TableViewerSettings>(json, SerializerOptions)
                ?? new TableViewerSettings();
        }
        catch (Exception ex)
        {
            Logger.Error($"TableViewerSettingsService: failed to read {filePath}.", ex);

            return new TableViewerSettings();
        }
    }

    public void
    Save(
        TableViewerSettings settings)
    {
        var filePath = GetFilePath();

        try
        {
            var directory = Path.GetDirectoryName(filePath);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(
                filePath,
                JsonSerializer.Serialize(settings, SerializerOptions));
        }
        catch (Exception ex)
        {
            Logger.Error($"TableViewerSettingsService: failed to write {filePath}.", ex);

            throw;
        }
    }
}

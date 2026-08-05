using KubaToolKit.Modules.AtlassianSearch.Models;
using KubaToolKit.Shared.Services;
using System.IO;
using System.Text.Json;

namespace KubaToolKit.Modules.AtlassianSearch;

public class AtlassianSettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new() { WriteIndented = true };

    // Personal Atlassian API token, same treatment as Config/config and
    // Config/credentials for AWS SSO: kept local and out of git, since it's
    // tied to one person's Atlassian account, not shared with the team.
    public static string
    GetFilePath() =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "atlassian.json");

    public AtlassianSettings
    Load()
    {
        var filePath = GetFilePath();

        if (!File.Exists(filePath))
        {
            return new AtlassianSettings();
        }

        try
        {
            var json = File.ReadAllText(filePath);

            return
                JsonSerializer.Deserialize<AtlassianSettings>(json, SerializerOptions)
                ?? new AtlassianSettings();
        }
        catch (Exception ex)
        {
            Logger.Error($"AtlassianSettingsService: failed to read {filePath}.", ex);

            return new AtlassianSettings();
        }
    }

    public void
    Save(
        AtlassianSettings settings)
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
            Logger.Error($"AtlassianSettingsService: failed to write {filePath}.", ex);

            throw;
        }
    }
}

using System.IO;
using System.Text.Json;

namespace KubaToolKit.Shared.Services;

public class TeamSharingSettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new() { WriteIndented = true };

    // Personal, per-machine preference (like Config/atlassian.json) --
    // kept local, never itself written into the shared folder it points
    // at.
    public static string
    GetFilePath() =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "team-sharing.json");

    public TeamSharingSettings
    Load()
    {
        var filePath = GetFilePath();

        if (File.Exists(filePath))
        {
            try
            {
                var json = File.ReadAllText(filePath);

                return
                    JsonSerializer.Deserialize<TeamSharingSettings>(json, SerializerOptions)
                    ?? new TeamSharingSettings();
            }
            catch (Exception ex)
            {
                Logger.Error($"TeamSharingSettingsService: failed to read {filePath}.", ex);

                return new TeamSharingSettings();
            }
        }

        // One-time migration: v3.4.0 briefly kept this as
        // AtlassianSettings.SharedLibraryFolder before it grew into this
        // module-agnostic setting. Read the raw JSON rather than through
        // AtlassianSettings itself, which no longer has that property, so
        // anyone who already set it there doesn't have to redo it.
        var migrated = TryMigrateFromAtlassianSettings();

        if (migrated != null)
        {
            Save(migrated);

            return migrated;
        }

        return new TeamSharingSettings();
    }

    public void
    Save(
        TeamSharingSettings settings)
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
            Logger.Error($"TeamSharingSettingsService: failed to write {filePath}.", ex);

            throw;
        }
    }

    private static TeamSharingSettings?
    TryMigrateFromAtlassianSettings()
    {
        try
        {
            var atlassianPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "atlassian.json");

            if (!File.Exists(atlassianPath))
            {
                return null;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(atlassianPath));

            if (!doc.RootElement.TryGetProperty("SharedLibraryFolder", out var el)
                || el.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var value = el.GetString();

            return string.IsNullOrWhiteSpace(value) ? null : new TeamSharingSettings { SharedFolder = value };
        }
        catch (Exception ex)
        {
            Logger.Error("TeamSharingSettingsService: migration from atlassian.json failed.", ex);

            return null;
        }
    }
}

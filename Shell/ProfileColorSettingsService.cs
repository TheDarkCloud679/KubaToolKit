using KubaToolKit.Shared.Services;
using System.IO;
using System.Text.Json;

namespace KubaToolKit.Shell;

public class ProfileColorSettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new() { WriteIndented = true };

    public static string
    GetFilePath() =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "profileColors.json");

    public ProfileColorSettings
    Load()
    {
        var filePath = GetFilePath();

        if (!File.Exists(filePath))
        {
            return new ProfileColorSettings();
        }

        try
        {
            var json = File.ReadAllText(filePath);

            return
                JsonSerializer.Deserialize<ProfileColorSettings>(json, SerializerOptions)
                ?? new ProfileColorSettings();
        }
        catch (Exception ex)
        {
            Logger.Error($"ProfileColorSettingsService: failed to read {filePath}.", ex);

            return new ProfileColorSettings();
        }
    }

    public void
    Save(
        ProfileColorSettings settings)
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
            Logger.Error($"ProfileColorSettingsService: failed to write {filePath}.", ex);

            throw;
        }
    }
}

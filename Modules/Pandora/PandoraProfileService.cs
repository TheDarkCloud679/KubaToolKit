using KubaToolKit.Modules.Pandora.Models;
using System.IO;
using System.Text.Json;

namespace KubaToolKit.Modules.Pandora;

public class PandoraProfileService
{
    public string
    GetProfilesFilePath() =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "pandora-profiles.json");

    public List<PandoraProfile>
    LoadProfiles()
    {
        var filePath = GetProfilesFilePath();

        if (!File.Exists(filePath))
        {
            return new();
        }

        var json = File.ReadAllText(filePath);

        return
            JsonSerializer.Deserialize<List<PandoraProfile>>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? new();
    }
}

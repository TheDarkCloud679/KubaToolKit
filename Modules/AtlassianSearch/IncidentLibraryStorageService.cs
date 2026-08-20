using KubaToolKit.Modules.AtlassianSearch.Models;
using KubaToolKit.Shared.Services;
using System.IO;
using System.Text.Json;

namespace KubaToolKit.Modules.AtlassianSearch;

// One JSON file per incident under %AppData%/KubaToolKit/Atlassian/Incidents,
// same "a folder of plain files, each individually copyable/shareable"
// convention as the API Client module's collections -- an incident is the
// natural sharable unit here (e.g. "here's the write-up for that payment
// terminal bug, with the tickets and Confluence pages already linked").
public class IncidentLibraryStorageService
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new() { WriteIndented = true };

    public static string RootFolder =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KubaToolKit",
            "Atlassian",
            "Incidents");

    public void
    EnsureFolderExists() =>
        Directory.CreateDirectory(RootFolder);

    public List<IncidentEntry>
    LoadIncidents()
    {
        EnsureFolderExists();

        var incidents = new List<IncidentEntry>();

        foreach (var file in Directory
                     .GetFiles(RootFolder, "*.json")
                     .OrderBy(f => f))
        {
            try
            {
                var json = File.ReadAllText(file);

                var entry = JsonSerializer.Deserialize<IncidentEntry>(json, SerializerOptions);

                if (entry == null)
                {
                    continue;
                }

                entry.FilePath = file;

                incidents.Add(entry);
            }
            catch (JsonException ex)
            {
                Logger.Error($"IncidentLibraryStorageService: failed to read {file}.", ex);
            }
        }

        return incidents;
    }

    public void
    SaveIncident(
        IncidentEntry entry)
    {
        if (string.IsNullOrEmpty(entry.FilePath))
        {
            throw new InvalidOperationException("This incident is not associated with any file.");
        }

        File.WriteAllText(
            entry.FilePath,
            JsonSerializer.Serialize(entry, SerializerOptions));
    }

    public IncidentEntry
    CreateIncident(
        string name)
    {
        EnsureFolderExists();

        var fileName = MakeUniqueFileName(SanitizeFileName(name));
        var filePath = Path.Combine(RootFolder, fileName);

        var entry = new IncidentEntry { Name = name, FilePath = filePath };

        SaveIncident(entry);

        return entry;
    }

    public void
    DeleteIncidentFile(
        string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    // A single JSON array of every incident -- unlike the one-file-per-
    // incident storage on disk, this is meant to be handed to a
    // colleague (or kept as a backup) as one self-contained file.
    public void
    ExportLibrary(
        string filePath)
    {
        var incidents = LoadIncidents();

        File.WriteAllText(
            filePath,
            JsonSerializer.Serialize(incidents, SerializerOptions));
    }

    // Imported incidents are always added as new entries, never
    // overwriting anything already here -- a name that collides with an
    // existing incident gets the same " (2)", " (3)"... suffix
    // CreateIncident already uses, rather than silently merging or
    // replacing someone's existing write-up.
    public int
    ImportLibrary(
        string filePath)
    {
        EnsureFolderExists();

        var json = File.ReadAllText(filePath);

        var imported = JsonSerializer.Deserialize<List<IncidentEntry>>(json, SerializerOptions) ?? new();

        foreach (var entry in imported)
        {
            var fileName = MakeUniqueFileName(SanitizeFileName(entry.Name));

            entry.FilePath = Path.Combine(RootFolder, fileName);

            SaveIncident(entry);
        }

        return imported.Count;
    }

    private static string
    SanitizeFileName(
        string name)
    {
        var invalid = Path.GetInvalidFileNameChars();

        var cleaned =
            new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray())
                .Trim();

        return string.IsNullOrWhiteSpace(cleaned) ? "incident" : cleaned;
    }

    private string
    MakeUniqueFileName(
        string baseName)
    {
        var fileName = $"{baseName}.json";
        var counter = 1;

        while (File.Exists(Path.Combine(RootFolder, fileName)))
        {
            fileName = $"{baseName} ({++counter}).json";
        }

        return fileName;
    }
}

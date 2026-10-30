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

    private static string DefaultRootFolder =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KubaToolKit",
            "Atlassian",
            "Incidents");

    // Lets TeamSharingSettingsWindow read/write a SPECIFIC folder during
    // migration (the old one, the new one) regardless of whatever's
    // currently saved in Team Sharing settings -- unset (the normal case
    // everywhere else) falls through to the usual resolution below.
    public string? OverrideRootFolder { get; set; }

    // TeamSharingFolders.SharedIncidentsFolder() is non-null once a
    // shared folder is set (Team Sharing settings) -- every colleague
    // pointed at the same synced folder then shares the Library live
    // instead of each having their own local %AppData% copy.
    public string RootFolder =>
        OverrideRootFolder ?? TeamSharingFolders.SharedIncidentsFolder() ?? DefaultRootFolder;

    // Returns false (rather than throwing) when the folder can't be
    // created or reached -- e.g. RootFolder resolving to a Team Sharing
    // path on a drive that isn't mounted right now (a real crash seen in
    // the wild: a synced-drive letter not yet available at startup).
    // AtlassianSearchView calls LoadIncidents() from its own constructor,
    // and every module view is constructed eagerly at app startup, so an
    // uncaught exception here took down the entire app before its window
    // ever showed, not just this module.
    public bool
    EnsureFolderExists()
    {
        try
        {
            Directory.CreateDirectory(RootFolder);

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"IncidentLibraryStorageService: couldn't create or reach '{RootFolder}'.", ex);

            return false;
        }
    }

    public List<IncidentEntry>
    LoadIncidents()
    {
        var incidents = new List<IncidentEntry>();

        if (!EnsureFolderExists())
        {
            return incidents;
        }

        string[] files;

        try
        {
            files = Directory.GetFiles(RootFolder, "*.json");
        }
        catch (Exception ex)
        {
            Logger.Error($"IncidentLibraryStorageService: couldn't list '{RootFolder}'.", ex);

            return incidents;
        }

        foreach (var file in files.OrderBy(f => f))
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

    // Moved into a Backup subfolder rather than actually deleted -- once
    // the Library lives in a folder several colleagues can write to,
    // anyone's accidental (or not) delete would otherwise be
    // unrecoverable for everyone else too.
    public void
    DeleteIncidentFile(
        string filePath) =>
        BackupHelper.MoveToBackup(filePath);

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

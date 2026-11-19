using KubaToolKit.Modules.AtlassianSearch.Models;
using KubaToolKit.Modules.Wiki.Models;
using System.Text.Json.Nodes;

namespace KubaToolKit.Shared.ConfigExport;

// Everything ConfigExportService bundles into one importable file. Kept as
// plain data (no behavior) so it round-trips through System.Text.Json
// without surprises -- ConfigExportService owns what goes in/out of each
// field and which values get blanked before they land here.
public class ConfigExportBundle
{
    public int FormatVersion { get; set; } = 1;
    public string ExportedAtUtc { get; set; } = "";
    public string ExportedByAppVersion { get; set; } = "";

    public List<ExportedJsonFile> Collections { get; set; } = new();

    // Secret-bearing fields (variable values, token-request body/auth/
    // header values) are already blanked by the time they're added here --
    // see ConfigExportService.BlankEnvironmentSecrets.
    public List<ExportedJsonFile> Environments { get; set; } = new();

    // Deliberately excludes AtlassianSettings.Email/ApiToken -- those are
    // one person's own Atlassian credentials, never meant to leave this
    // machine (same rule AtlassianSettingsService already documents).
    public string AtlassianBaseUrl { get; set; } = "";
    public List<string> AtlassianFavoriteConfluenceSpaceKeys { get; set; } = new();
    public List<SavedJiraStatsFilter> AtlassianSavedJiraStatsFilters { get; set; } = new();

    public List<WikiSection> WikiSections { get; set; } = new();

    // Keyed by the file name WikiSection.ImageFileNames references, so the
    // whole Wiki (text and images) travels in this one file instead of
    // needing the images folder copied alongside it separately.
    public Dictionary<string, string> WikiImagesBase64 { get; set; } = new();

    public List<IncidentEntry> Incidents { get; set; } = new();
}

// One collection or environment file, carried through verbatim (as a
// JsonNode) rather than round-tripped through a strongly-typed model --
// anything a Postman-format file happens to contain survives the trip
// unchanged, the same way CollectionStorageService's own save path leaves
// fields it doesn't know about alone.
public class ExportedJsonFile
{
    public string FileName { get; set; } = "";
    public JsonNode? Content { get; set; }
}

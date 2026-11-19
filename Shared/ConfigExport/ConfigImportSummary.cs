using System.Text;

namespace KubaToolKit.Shared.ConfigExport;

// What ConfigExportService.Import actually did, for the one message box
// ConfigExportWindow shows afterwards -- everything here is additive
// (nothing imported ever overwrites or deletes existing data), so the
// "skipped" counts are informational, not failures.
public class ConfigImportSummary
{
    public int CollectionsImported { get; set; }
    public int EnvironmentsImported { get; set; }
    public int EnvironmentsSkippedExisting { get; set; }
    public bool AtlassianBaseUrlApplied { get; set; }
    public int AtlassianFavoritesAdded { get; set; }
    public int AtlassianFiltersAdded { get; set; }
    public int WikiSectionsImported { get; set; }
    public int WikiSectionsSkippedDuplicate { get; set; }
    public int WikiImagesCopied { get; set; }
    public int IncidentsImported { get; set; }

    public string
    ToDisplayText()
    {
        var lines = new StringBuilder();

        lines.AppendLine($"Collections: {CollectionsImported} added.");

        lines.AppendLine(
            EnvironmentsSkippedExisting > 0
                ? $"Environments: {EnvironmentsImported} added, {EnvironmentsSkippedExisting} skipped (name already exists here)."
                : $"Environments: {EnvironmentsImported} added.");

        if (AtlassianBaseUrlApplied || AtlassianFavoritesAdded > 0 || AtlassianFiltersAdded > 0)
        {
            lines.AppendLine(
                $"Atlassian: "
                + (AtlassianBaseUrlApplied ? "site URL set, " : "")
                + $"{AtlassianFavoritesAdded} favorite space(s) added, {AtlassianFiltersAdded} saved filter(s) added.");
        }

        lines.AppendLine(
            WikiSectionsSkippedDuplicate > 0
                ? $"Wiki: {WikiSectionsImported} page(s) added ({WikiImagesCopied} image(s)), {WikiSectionsSkippedDuplicate} skipped (already present)."
                : $"Wiki: {WikiSectionsImported} page(s) added ({WikiImagesCopied} image(s)).");

        lines.Append($"Incidents: {IncidentsImported} added.");

        return lines.ToString();
    }
}

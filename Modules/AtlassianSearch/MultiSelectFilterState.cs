using KubaToolKit.Modules.AtlassianSearch.Models;

namespace KubaToolKit.Modules.AtlassianSearch;

// Backing state for a filter field that picks several values at once via
// MultiSelectPickerWindow, instead of the old single-ComboBox-selection
// (with "in"/"not in" only reachable by manually typing a comma list --
// not discoverable, and the reason this exists). AllOptions/SelectedValues
// are kept as plain field values (not a NameValue's paired display) since
// JQL and SavedJira(Stats)Filter both only ever need the raw value.
public class MultiSelectFilterState
{
    public List<NameValue> AllOptions { get; set; } = new();
    public List<string> SelectedValues { get; set; } = new();

    public string
    Summary() =>
        SelectedValues.Count switch
        {
            0 => "(Any)",
            1 => AllOptions
                .FirstOrDefault(o => string.Equals(o.Value, SelectedValues[0], StringComparison.OrdinalIgnoreCase))
                .Display
                ?? SelectedValues[0],
            _ => $"{SelectedValues.Count} selected"
        };

    // The raw comma-joined value JiraFieldFilter/AddJqlCondition already
    // understand -- no service-layer changes needed for multi-select.
    public string
    JqlValue() =>
        string.Join(",", SelectedValues);

    public void
    SetFromCommaList(
        string value) =>
        SelectedValues =
            string.IsNullOrWhiteSpace(value)
                ? new List<string>()
                : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}

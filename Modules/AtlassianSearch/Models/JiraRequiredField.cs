namespace KubaToolKit.Modules.AtlassianSearch.Models;

// A field a transition's screen requires beyond the standard comment
// (e.g. Resolution, or an instance-specific custom field like
// "Categories") -- discovered from the transition's own schema rather
// than hardcoded, since which fields are required (and what values they
// allow) is entirely workflow-specific.
public class JiraRequiredField
{
    public string FieldId { get; set; } = "";
    public string Name { get; set; } = "";
    public bool AllowsMultiple { get; set; }
    public List<NameValue> AllowedValues { get; set; } = new();

    public bool HasAllowedValues =>
        AllowedValues.Count > 0;

    // Written to by the viewer's data-bound ComboBox/TextBox as the user
    // fills it in; read back when Apply is clicked.
    public string EnteredValue { get; set; } = "";
}

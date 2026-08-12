namespace KubaToolKit.Shell;

// Profile name -> hex color (e.g. "#E5484D"), only for profiles the user
// explicitly assigned one via ProfileColorPickerWindow -- anything not in
// here falls back to ProfileRiskBrushConverter's naming-based guess.
public class ProfileColorSettings
{
    public Dictionary<string, string> Colors { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

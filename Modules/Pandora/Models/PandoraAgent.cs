using System.Windows.Media;

namespace KubaToolKit.Modules.Pandora.Models;

public class PandoraAgent
{
    public string Id { get; set; } = "";

    public string Alias { get; set; } = "";

    public string Address { get; set; } = "";

    public string Comments { get; set; } = "";

    public string OsName { get; set; } = "";

    /// Codes AGENT_STATUS_* de Pandora FMS : 0=Normal, 1=Critical,
    /// 2=Warning, 3=Unknown, 4=Alert fired, 5=Not init.
    public int Status { get; set; }

    public string StatusLabel =>
        Status switch
        {
            0 => "Normal",
            1 => "Critical",
            2 => "Warning",
            3 => "Unknown",
            4 => "Alert fired",
            5 => "Not init",
            _ => $"Status {Status}"
        };

    public Brush StatusBrush =>
        new SolidColorBrush(
            Status switch
            {
                0 => Color.FromRgb(0x3C, 0xB8, 0x78),
                1 => Color.FromRgb(0xE2, 0x57, 0x57),
                2 => Color.FromRgb(0xE2, 0xA9, 0x3C),
                4 => Color.FromRgb(0xE2, 0x57, 0x57),
                5 => Color.FromRgb(0x8C, 0x9B, 0xAB),
                _ => Color.FromRgb(0x8C, 0x9B, 0xAB)
            });
}

using System.Collections.ObjectModel;

namespace KubaToolKit.Modules.Pandora.Models;

public class PandoraGroupNode
{
    public string Name { get; set; } = "";

    public ObservableCollection<PandoraAgent> Agents { get; set; } = new();

    public int Count => Agents.Count;
}

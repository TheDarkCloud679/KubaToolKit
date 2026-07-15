using KubaToolKit.Infrastructure;
using System.Windows.Controls;

namespace KubaToolKit.Modules.Pandora;

public class PandoraModule : IToolModule
{
    public PandoraView TypedView { get; } = new();

    public string Name => "Pandora";

    public UserControl View => TypedView;
}

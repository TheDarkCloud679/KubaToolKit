using KubaToolKit.Infrastructure;
using System.Windows.Controls;

namespace KubaToolKit.Modules.AtlassianSearch;

public class AtlassianSearchModule : IToolModule
{
    public AtlassianSearchView TypedView { get; } = new();

    public string Name => "Atlassian Search";

    public UserControl View => TypedView;
}

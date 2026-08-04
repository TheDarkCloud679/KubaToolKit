using KubaToolKit.Infrastructure;
using System.Windows.Controls;

namespace KubaToolKit.Modules.KnowledgeSearch;

public class KnowledgeSearchModule : IToolModule
{
    public KnowledgeSearchView TypedView { get; } = new();

    public string Name => "Knowledge Search";

    public UserControl View => TypedView;
}

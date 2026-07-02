using KubaToolKit.Infrastructure;
using System.Windows.Controls;

namespace KubaToolKit.Modules.S3Explorer;

public class S3ExplorerModule : IToolModule
{
    public S3ExplorerView TypedView { get; } = new();

    public string Name => "S3 Explorer";

    public UserControl View => TypedView;
}

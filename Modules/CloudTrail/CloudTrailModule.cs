using KubaToolKit.Infrastructure;
using System.Windows.Controls;

namespace KubaToolKit.Modules.CloudTrail;

public class CloudTrailModule : IToolModule
{
    public CloudTrailView TypedView { get; } = new();

    public string Name => "CloudTrail";

    public UserControl View => TypedView;
}

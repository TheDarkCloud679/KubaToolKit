using KubaToolKit.Infrastructure;
using System.Windows.Controls;

namespace KubaToolKit.Modules.ApiClient;

public class ApiClientModule : IToolModule
{
    public ApiClientView TypedView { get; } = new();

    public string Name => "API Client";

    public UserControl View => TypedView;
}

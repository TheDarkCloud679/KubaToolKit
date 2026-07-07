using KubaToolKit.Infrastructure;
using System.Windows.Controls;

namespace KubaToolKit.Modules.Dashboard;

public class DashboardModule : IToolModule
{
    public DashboardView TypedView { get; } = new();

    public string Name => "Dashboard";

    public UserControl View => TypedView;
}

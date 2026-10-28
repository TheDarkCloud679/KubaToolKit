using KubaToolKit.Infrastructure;
using System.Windows.Controls;

namespace KubaToolKit.Modules.TableViewer;

public class TableViewerModule : IToolModule
{
    public TableViewerView TypedView { get; } = new();

    public string Name => "Table Viewer";

    public UserControl View => TypedView;
}

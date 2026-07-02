using System.Windows.Controls;

namespace KubaToolKit.Infrastructure;

/// A pluggable brick of the toolkit: a name for navigation and a self-contained view.
public interface IToolModule
{
    string Name { get; }

    UserControl View { get; }
}

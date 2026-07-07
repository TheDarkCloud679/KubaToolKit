using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KubaToolKit.Shared.Services;

/// Tri manuel des DataGrid de l'appli (au lieu du tri intégré, qui se
/// déclenche au simple clic) : partout, le double-clic sur un en-tête de
/// colonne trie, pour rester cohérent avec le double-clic déjà utilisé
/// ailleurs (ouverture de graphiques, de fenêtres de détail...).
public static class DataGridSortHelper
{
    public static void
    SortByColumn<T>(
        ObservableCollection<T> items,
        IEnumerable<DataGridColumn> columns,
        DataGridColumn? column,
        ref DataGridColumn? currentColumn,
        ref ListSortDirection currentDirection)
    {
        if (column?.SortMemberPath is not { } propertyName
            || typeof(T).GetProperty(propertyName) is not { } property)
        {
            return;
        }

        currentDirection =
            currentColumn == column
            && currentDirection == ListSortDirection.Ascending
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;

        currentColumn = column;

        var ordered =
            currentDirection == ListSortDirection.Ascending
                ? items.OrderBy(x => property.GetValue(x))
                : items.OrderByDescending(x => property.GetValue(x));

        var sorted = ordered.ToList();

        items.Clear();

        foreach (var item in sorted)
        {
            items.Add(item);
        }

        foreach (var col in columns)
        {
            col.SortDirection = null;
        }

        column.SortDirection = currentDirection;
    }

    public static T?
    FindAncestor<T>(
        DependencyObject? current)
        where T : DependencyObject
    {
        while (current != null && current is not T)
        {
            current = VisualTreeHelper.GetParent(current);
        }

        return current as T;
    }
}

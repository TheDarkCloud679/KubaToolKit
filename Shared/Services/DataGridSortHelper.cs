using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace KubaToolKit.Shared.Services;

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

    // Re-applies an already-chosen sort (e.g. after a data refresh
    // repopulates the collection) without the toggle-on-repeat-click
    // behavior SortByColumn has -- a refresh isn't a click on the header.
    public static void
    ReapplySort<T>(
        ObservableCollection<T> items,
        DataGridColumn? column,
        ListSortDirection direction)
    {
        if (column?.SortMemberPath is not { } propertyName
            || typeof(T).GetProperty(propertyName) is not { } property)
        {
            return;
        }

        var ordered =
            direction == ListSortDirection.Ascending
                ? items.OrderBy(x => property.GetValue(x))
                : items.OrderByDescending(x => property.GetValue(x));

        var sorted = ordered.ToList();

        items.Clear();

        foreach (var item in sorted)
        {
            items.Add(item);
        }
    }

    public static T?
    FindAncestor<T>(
        DependencyObject? current)
        where T : DependencyObject
    {
        while (current != null && current is not T)
        {
            // A click/event source inside text rendered via
            // TextBlock.Inlines (e.g. Run elements) isn't a Visual/
            // Visual3D, and VisualTreeHelper.GetParent throws on those --
            // step up through the logical tree instead until back on a
            // real Visual, then resume walking the visual tree.
            current =
                current is Visual or Visual3D
                    ? VisualTreeHelper.GetParent(current)
                    : LogicalTreeHelper.GetParent(current);
        }

        return current as T;
    }

    public static void
    RefreshColumnWidths(
        DataGrid grid)
    {
        foreach (var column in grid.Columns)
        {
            var width = column.Width;

            column.Width = new DataGridLength(0);
            column.Width = width;
        }
    }
}

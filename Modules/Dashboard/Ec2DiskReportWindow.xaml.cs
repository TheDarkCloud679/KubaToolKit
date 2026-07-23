using KubaToolKit.Modules.Dashboard.Models;
using KubaToolKit.Shared.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace KubaToolKit.Modules.Dashboard;

public partial class Ec2DiskReportWindow
    : Window
{
    private const double WarningThresholdPercent = 80;

    private readonly ObservableCollection<Ec2DiskUsage> _rows = new();
    private DataGridColumn? _sortColumn;
    private ListSortDirection _sortDirection = ListSortDirection.Ascending;

    public Ec2DiskReportWindow(
        List<Ec2DiskUsage> report)
    {
        InitializeComponent();

        ReportGrid.ItemsSource = _rows;

        foreach (var row in report)
        {
            _rows.Add(row);
        }

        var instanceCount =
            report.Select(r => r.InstanceId).Distinct().Count();

        var warningCount =
            report.Count(r => r.UsedPercent >= WarningThresholdPercent);

        SummaryTextBlock.Text =
            report.Count == 0
                ? "No disk metric found across any namespace for these instances. The agent service running isn't "
                    + "enough: its config also needs a disk/space measurement enabled (the way \"mem\" already is, "
                    + "since RAM does show up) -- then restart the agent for the change to take effect."
                : $"{report.Count} mount point(s) across {instanceCount} instance(s)."
                    + (warningCount > 0 ? $" {warningCount} at or above {WarningThresholdPercent:F0}%." : "");
    }

    private void
    ReportGrid_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        if (DataGridSortHelper.FindAncestor<DataGridColumnHeader>(e.OriginalSource as DependencyObject)
            is not { } header)
        {
            return;
        }

        DataGridSortHelper.SortByColumn(
            _rows,
            ReportGrid.Columns,
            header.Column,
            ref _sortColumn,
            ref _sortDirection);
    }
}

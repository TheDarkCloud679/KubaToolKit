using Amazon.CloudWatch.Model;
using KubaToolKit.Modules.Dashboard.Models;
using KubaToolKit.Modules.ProjectInfo;
using KubaToolKit.Modules.Wiki;
using KubaToolKit.Shared.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace KubaToolKit.Modules.Dashboard;

public partial class DashboardView
    : UserControl
{
    private static readonly Color CpuColor = Color.FromRgb(0x2F, 0x6F, 0xED);
    private static readonly Color RamColor = Color.FromRgb(0x8B, 0x5C, 0xF6);

    private readonly DashboardService _dashboardService = new();
    private readonly ObservableCollection<RdsMetricItem> _rdsMetrics = new();
    private readonly ObservableCollection<Ec2MetricItem> _ec2Metrics = new();
    private string? _currentProfile;
    private CancellationTokenSource? _loadCancellation;

    private GridLength _rdsExpandedHeight = new(280);

    private DataGridColumn? _rdsSortColumn;
    private ListSortDirection _rdsSortDirection = ListSortDirection.Ascending;

    private DataGridColumn? _ec2SortColumn;
    private ListSortDirection _ec2SortDirection = ListSortDirection.Ascending;

    public DashboardView()
    {
        InitializeComponent();

        RdsGrid.ItemsSource = _rdsMetrics;
        Ec2Grid.ItemsSource = _ec2Metrics;

        UpdateSectionRows();
    }

    private void
    RdsExpander_ExpandedCollapsed(
        object sender,
        RoutedEventArgs e)
    {
        if (RdsRow.Height.IsAbsolute)
        {
            _rdsExpandedHeight = RdsRow.Height;
        }

        UpdateSectionRows();
    }

    private void
    Ec2Expander_ExpandedCollapsed(
        object sender,
        RoutedEventArgs e)
    {
        UpdateSectionRows();
    }

    private void
    UpdateSectionRows()
    {
        if (RdsRow == null
            || Ec2Row == null
            || RdsExpander == null
            || Ec2Expander == null
            || RdsEc2Splitter == null)
        {
            return;
        }

        bool rdsExpanded = RdsExpander.IsExpanded;
        bool ec2Expanded = Ec2Expander.IsExpanded;

        if (ec2Expanded)
        {
            Ec2Row.Height = new GridLength(1, GridUnitType.Star);
            RdsRow.Height = rdsExpanded ? _rdsExpandedHeight : GridLength.Auto;
        }
        else
        {
            Ec2Row.Height = GridLength.Auto;
            RdsRow.Height = rdsExpanded ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        }

        RdsEc2Splitter.IsEnabled = rdsExpanded && ec2Expanded;
    }

    public async Task
    OnProfileChanged(
        string? profile)
    {
        _currentProfile = profile;

        await RefreshAsync();
    }

    public async Task
    RefreshAsync()
    {
        if (string.IsNullOrWhiteSpace(_currentProfile))
        {
            return;
        }

        Logger.Debug($"DashboardView: refreshing (profile '{_currentProfile}').");

        try
        {
            LoadingProgressBar.Visibility =
                Visibility.Visible;

            RefreshButton.IsEnabled =
                false;

            _loadCancellation?.Cancel();

            _loadCancellation =
                new CancellationTokenSource();

            var token =
                _loadCancellation.Token;

            var rdsTask =
                _dashboardService.GetRdsMetrics(
                    _currentProfile,
                    null,
                    token);

            var ec2Task =
                _dashboardService.GetEc2Instances(
                    _currentProfile,
                    token);

            await Task.WhenAll(rdsTask, ec2Task);

            _rdsMetrics.Clear();

            foreach (var metric in rdsTask.Result)
            {
                _rdsMetrics.Add(metric);
            }

            var ec2Instances = ec2Task.Result;

            // Scanning disk usage needs the instance list first (it has to
            // know which InstanceIds to look up), so it can't run alongside
            // rdsTask/ec2Task above -- it always runs one step behind them.
            var diskUsageByInstance =
                await _dashboardService.GetEc2DiskUsage(
                    _currentProfile,
                    ec2Instances,
                    token);

            foreach (var instance in ec2Instances)
            {
                instance.DiskPercent =
                    diskUsageByInstance.TryGetValue(instance.InstanceId, out var worstPercent)
                        ? worstPercent
                        : (double?)null;
            }

            _ec2Metrics.Clear();

            foreach (var instance in ec2Instances)
            {
                _ec2Metrics.Add(instance);
            }

            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    DataGridSortHelper.RefreshColumnWidths(RdsGrid);
                    DataGridSortHelper.RefreshColumnWidths(Ec2Grid);
                }),
                System.Windows.Threading.DispatcherPriority.Loaded);

            Logger.Info(
                $"DashboardView: refresh completed, {rdsTask.Result.Count} RDS, {ec2Instances.Count} EC2.");
        }
        catch (OperationCanceledException)
        {
            Logger.Debug("DashboardView: refresh cancelled.");
        }
        catch (Exception ex)
        {
            if (AwsSsoService.IsSsoExpired(ex))
            {
                Logger.Debug("DashboardView: SSO session expired, attempting reconnection.");

                var success =
                    await AwsSsoService.Login();

                if (success)
                {
                    await RefreshAsync();
                    return;
                }
            }

            Logger.Error(
                $"DashboardView: refresh failed (profile '{_currentProfile}').",
                ex);

            MessageBox.Show(
                ex.ToString(),
                "Dashboard loading error");
        }
        finally
        {
            LoadingProgressBar.Visibility =
                Visibility.Collapsed;

            RefreshButton.IsEnabled =
                true;
        }
    }

    private async void
    RefreshButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        await RefreshAsync();
    }

    private void
    ProjectInfoButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentProfile))
        {
            MessageBox.Show(
                "Please select an AWS profile first.",
                "Project Info");

            return;
        }

        var window = new ProjectInfoWindow(_currentProfile);

        window.Show();
    }

    private void
    WikiButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentProfile))
        {
            MessageBox.Show(
                "Please select an AWS profile first.",
                "Wiki");

            return;
        }

        var window = new WikiWindow(_currentProfile);

        window.Show();
    }

    private void
    RdsGrid_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        if (DataGridSortHelper.FindAncestor<DataGridColumnHeader>(e.OriginalSource as DependencyObject)
            is not { } header)
        {
            return;
        }

        DataGridSortHelper.SortByColumn(
            _rdsMetrics,
            RdsGrid.Columns,
            header.Column,
            ref _rdsSortColumn,
            ref _rdsSortDirection);
    }

    private void
    CpuMetric_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
        {
            return;
        }

        if (sender is not FrameworkElement element
            || element.DataContext is not RdsMetricItem item
            || string.IsNullOrWhiteSpace(_currentProfile))
        {
            return;
        }

        OpenRdsMetricChart(
            item,
            "CPUUtilization",
            "CPU Utilization",
            "%");
    }

    private void
    ActivityMetric_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
        {
            return;
        }

        if (sender is not FrameworkElement element
            || element.DataContext is not RdsMetricItem item
            || string.IsNullOrWhiteSpace(_currentProfile))
        {
            return;
        }

        OpenRdsMetricChart(
            item,
            "DatabaseConnections",
            "Activity (sessions)",
            "sessions");
    }

    private void
    OpenRdsMetricChart(
        RdsMetricItem item,
        string metricName,
        string metricDisplayName,
        string unit)
    {
        if (string.IsNullOrWhiteSpace(_currentProfile))
        {
            return;
        }

        var request = new ChartSeriesRequest
        {
            Namespace = "AWS/RDS",
            MetricName = metricName,
            DisplayName = metricDisplayName,
            Unit = unit,
            Color = CpuColor,
            Dimensions = new List<Dimension>
            {
                new Dimension
                {
                    Name = "DBInstanceIdentifier",
                    Value = item.Identifier
                }
            }
        };

        // Deferred past the double-click's mouse-up: opening the window
        // synchronously while that input is still being processed lets
        // Windows mistake it for a drag on the new window, which
        // immediately minimizes it (a known WPF double-click gotcha).
        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                var window =
                    new MetricChartWindow(
                        _currentProfile,
                        metricDisplayName,
                        item.Identifier,
                        new List<ChartSeriesRequest> { request });

                window.Show();
            }));
    }

    private void
    Ec2Grid_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        if (DataGridSortHelper.FindAncestor<DataGridColumnHeader>(e.OriginalSource as DependencyObject)
            is { } header)
        {
            DataGridSortHelper.SortByColumn(
                _ec2Metrics,
                Ec2Grid.Columns,
                header.Column,
                ref _ec2SortColumn,
                ref _ec2SortDirection);

            return;
        }

        if (Ec2Grid.SelectedItem is not Ec2MetricItem item
            || string.IsNullOrWhiteSpace(_currentProfile))
        {
            return;
        }

        var dimensions = new List<Dimension>
        {
            new Dimension
            {
                Name = "InstanceId",
                Value = item.InstanceId
            }
        };

        var seriesRequests = new List<ChartSeriesRequest>
        {
            new ChartSeriesRequest
            {
                Namespace = "AWS/EC2",
                MetricName = "CPUUtilization",
                DisplayName = "CPU",
                Unit = "%",
                Color = CpuColor,
                Dimensions = dimensions
            },
            new ChartSeriesRequest
            {
                Namespace = "CWAgent",
                MetricName = "mem_used_percent",
                DisplayName = "RAM",
                Unit = "%",
                Color = RamColor,
                Dimensions = dimensions
            }
        };

        // See the comment in OpenRdsMetricChart: deferred so the
        // double-click's mouse-up finishes processing before the window
        // appears, instead of Windows treating it as a drag and minimizing
        // it immediately.
        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                var window =
                    new MetricChartWindow(
                        _currentProfile,
                        "CPU / RAM",
                        item.Name,
                        seriesRequests);

                window.Show();
            }));
    }

}

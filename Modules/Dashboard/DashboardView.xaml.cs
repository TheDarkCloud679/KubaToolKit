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
using System.Windows.Media.Animation;
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

    // Star weights used when RDS and EC2 are BOTH expanded, so they split
    // the available height (50/50 by default) instead of one of them
    // being pinned to a fixed pixel size. GridSplitter mutates these
    // directly (dragging between two Star rows is a built-in WPF
    // behavior), so they only need to be re-read from the rows on the
    // next Expander toggle -- see CaptureStarWeights().
    private double _rdsStarWeight = 1;
    private double _ec2StarWeight = 1;

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
        CaptureStarWeights();
        AnimateSectionRows();
    }

    private void
    Ec2Expander_ExpandedCollapsed(
        object sender,
        RoutedEventArgs e)
    {
        CaptureStarWeights();
        AnimateSectionRows();
    }

    // Reads back whatever ratio the two rows currently hold, but only
    // while both are genuinely still Star/Star (i.e. both were expanded
    // just before this toggle) -- otherwise one of them is the
    // degenerate "single section, full height" Star(1) and would
    // overwrite a real user-dragged ratio with junk.
    private void
    CaptureStarWeights()
    {
        if (RdsRow.Height.IsStar && Ec2Row.Height.IsStar)
        {
            _rdsStarWeight = RdsRow.Height.Value;
            _ec2StarWeight = Ec2Row.Height.Value;
        }
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

        if (rdsExpanded && ec2Expanded)
        {
            RdsRow.Height = new GridLength(_rdsStarWeight, GridUnitType.Star);
            Ec2Row.Height = new GridLength(_ec2StarWeight, GridUnitType.Star);
        }
        else if (rdsExpanded)
        {
            RdsRow.Height = new GridLength(1, GridUnitType.Star);
            Ec2Row.Height = GridLength.Auto;
        }
        else if (ec2Expanded)
        {
            Ec2Row.Height = new GridLength(1, GridUnitType.Star);
            RdsRow.Height = GridLength.Auto;
        }
        else
        {
            RdsRow.Height = GridLength.Auto;
            Ec2Row.Height = GridLength.Auto;
        }

        RdsEc2Splitter.IsEnabled = rdsExpanded && ec2Expanded;
    }

    private bool _rowAnimationRunning;
    private DateTime _rowAnimationStart;
    private static readonly TimeSpan RowAnimationDuration = TimeSpan.FromSeconds(0.45);
    private static readonly CubicEase RowAnimationEase = new() { EasingMode = EasingMode.EaseOut };
    private double _rowAnimationFromRds;
    private double _rowAnimationToRds;
    private double _rowAnimationFromEc2;
    private double _rowAnimationToEc2;

    // RowDefinition.Height (GridLength) isn't itself animatable, and with
    // RDS/EC2 sharing one Grid, toggling either one can change both rows'
    // target size at once (RDS reflows to fill whatever EC2 just gave up,
    // or vice versa). A first attempt animated each row's MaxHeight
    // independently via BeginAnimation -- but with both rows Star-sized,
    // there's a stretch near the end where both MaxHeight values are
    // still below their true fair share at the same instant (each row's
    // own animation hasn't individually reached "Completed" yet), and
    // WPF's Grid Star solver doesn't hand the space one capped Star row
    // is missing to another Star row that's also capped -- it's just
    // left unused. That read as the section snapping back small instead
    // of smoothly reaching full size. Driving both rows' Height directly
    // in absolute pixels, one shared tick at a time, avoids the Star
    // solver entirely for the duration of the transition.
    private void
    AnimateSectionRows()
    {
        if (RdsRow == null
            || Ec2Row == null
            || RdsExpander == null
            || Ec2Expander == null
            || RdsEc2Splitter == null)
        {
            return;
        }

        var oldRdsHeight = RdsRow.ActualHeight;
        var oldEc2Height = Ec2Row.ActualHeight;

        UpdateSectionRows();

        // Let layout compute what the just-set Height (Auto/Star/fixed)
        // actually resolves to, without ever letting that unclamped size
        // reach the screen: measure it, then clamp straight back down to
        // the old size before this dispatcher operation yields to a
        // render pass, so nothing visibly jumps.
        RdsRow.MaxHeight = double.PositiveInfinity;
        Ec2Row.MaxHeight = double.PositiveInfinity;

        UpdateLayout();

        var targetRdsHeight = RdsRow.ActualHeight;
        var targetEc2Height = Ec2Row.ActualHeight;

        if (Math.Abs(oldRdsHeight - targetRdsHeight) < 0.5
            && Math.Abs(oldEc2Height - targetEc2Height) < 0.5)
        {
            return;
        }

        _rowAnimationFromRds = oldRdsHeight;
        _rowAnimationToRds = targetRdsHeight;
        _rowAnimationFromEc2 = oldEc2Height;
        _rowAnimationToEc2 = targetEc2Height;
        _rowAnimationStart = DateTime.UtcNow;

        RdsRow.Height = new GridLength(Math.Max(0, oldRdsHeight));
        Ec2Row.Height = new GridLength(Math.Max(0, oldEc2Height));

        if (!_rowAnimationRunning)
        {
            _rowAnimationRunning = true;
            CompositionTarget.Rendering += OnRowAnimationTick;
        }
    }

    private void
    OnRowAnimationTick(
        object? sender,
        EventArgs e)
    {
        var elapsed = (DateTime.UtcNow - _rowAnimationStart).TotalSeconds;
        var t = Math.Clamp(elapsed / RowAnimationDuration.TotalSeconds, 0, 1);
        var eased = RowAnimationEase.Ease(t);

        var rdsHeight = _rowAnimationFromRds + (_rowAnimationToRds - _rowAnimationFromRds) * eased;
        var ec2Height = _rowAnimationFromEc2 + (_rowAnimationToEc2 - _rowAnimationFromEc2) * eased;

        RdsRow.Height = new GridLength(Math.Max(0, rdsHeight));
        Ec2Row.Height = new GridLength(Math.Max(0, ec2Height));

        if (t < 1)
        {
            return;
        }

        CompositionTarget.Rendering -= OnRowAnimationTick;
        _rowAnimationRunning = false;

        RdsRow.MaxHeight = double.PositiveInfinity;
        Ec2Row.MaxHeight = double.PositiveInfinity;

        // Swap back to the real declarative sizing (Star/Auto) now that
        // the transition has settled, so window resizes and further
        // splitter drags behave normally instead of staying pinned to
        // this frame's absolute pixel snapshot.
        UpdateSectionRows();
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

using KubaToolKit.Modules.Dashboard.Models;
using KubaToolKit.Shared.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace KubaToolKit.Modules.Dashboard;

public partial class DashboardView
    : UserControl
{
    private readonly DashboardService _dashboardService = new();
    private readonly ObservableCollection<RdsMetricItem> _rdsMetrics = new();
    private string? _currentProfile;
    private CancellationTokenSource? _loadCancellation;

    public DashboardView()
    {
        InitializeComponent();

        RdsGrid.ItemsSource = _rdsMetrics;
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

        try
        {
            LoadingProgressBar.Visibility =
                Visibility.Visible;

            RefreshButton.IsEnabled =
                false;

            _loadCancellation?.Cancel();

            _loadCancellation =
                new CancellationTokenSource();

            var metrics =
                await _dashboardService.GetRdsMetrics(
                    _currentProfile,
                    null,
                    _loadCancellation.Token);

            _rdsMetrics.Clear();

            foreach (var metric in metrics)
            {
                _rdsMetrics.Add(metric);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (AwsSsoService.IsSsoExpired(ex))
            {
                var success =
                    await AwsSsoService.Login();

                if (success)
                {
                    await RefreshAsync();
                    return;
                }
            }

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
    CpuMetric_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
        {
            return;
        }

        OpenMetricChart(
            sender,
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

        OpenMetricChart(
            sender,
            "DatabaseConnections",
            "Activity (sessions)",
            "sessions");
    }

    private void
    OpenMetricChart(
        object sender,
        string metricName,
        string metricDisplayName,
        string unit)
    {
        if (sender is not FrameworkElement element
            || element.DataContext is not RdsMetricItem item
            || string.IsNullOrWhiteSpace(_currentProfile))
        {
            return;
        }

        var window =
            new MetricChartWindow(
                _currentProfile,
                item.Identifier,
                metricName,
                metricDisplayName,
                unit);

        window.Owner =
            Window.GetWindow(this);

        window.Show();
    }
}

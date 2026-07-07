using Amazon.CloudWatch.Model;
using KubaToolKit.Modules.Dashboard.Models;
using KubaToolKit.Shared.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

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

    // Hauteur de la section RDS mémorisée avant repli, pour la restaurer
    // (y compris si l'utilisateur l'a redimensionnée via le splitter).
    private GridLength _rdsExpandedHeight = new(280);

    public DashboardView()
    {
        InitializeComponent();

        RdsGrid.ItemsSource = _rdsMetrics;
        Ec2Grid.ItemsSource = _ec2Metrics;
    }

    private void
    RdsExpander_ExpandedCollapsed(
        object sender,
        RoutedEventArgs e)
    {
        if (RdsExpander.IsExpanded)
        {
            RdsRow.Height = _rdsExpandedHeight;
        }
        else
        {
            if (RdsRow.Height.IsAbsolute)
            {
                _rdsExpandedHeight = RdsRow.Height;
            }

            RdsRow.Height = GridLength.Auto;
        }

        UpdateSplitterState();
    }

    private void
    Ec2Expander_ExpandedCollapsed(
        object sender,
        RoutedEventArgs e)
    {
        Ec2Row.Height =
            Ec2Expander.IsExpanded
                ? new GridLength(1, GridUnitType.Star)
                : GridLength.Auto;

        UpdateSplitterState();
    }

    private void
    UpdateSplitterState()
    {
        // Redimensionner n'a de sens que si les deux sections sont
        // dépliées ; sinon il n'y a rien à répartir entre elles.
        RdsEc2Splitter.IsEnabled =
            RdsExpander.IsExpanded
            && Ec2Expander.IsExpanded;
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

            _ec2Metrics.Clear();

            foreach (var instance in ec2Task.Result)
            {
                _ec2Metrics.Add(instance);
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

        var window =
            new MetricChartWindow(
                _currentProfile,
                metricDisplayName,
                item.Identifier,
                new List<ChartSeriesRequest> { request });

        window.Owner =
            Window.GetWindow(this);

        window.Show();
    }

    private void
    Ec2Grid_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
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
                // Nécessite l'agent CloudWatch installé sur l'instance ;
                // sans lui cette courbe restera vide (signalé dans la
                // légende), seul le CPU (toujours disponible nativement)
                // s'affichera.
                Namespace = "CWAgent",
                MetricName = "mem_used_percent",
                DisplayName = "RAM",
                Unit = "%",
                Color = RamColor,
                Dimensions = dimensions
            }
        };

        var window =
            new MetricChartWindow(
                _currentProfile,
                "CPU / RAM",
                item.Name,
                seriesRequests);

        window.Owner =
            Window.GetWindow(this);

        window.Show();
    }
}

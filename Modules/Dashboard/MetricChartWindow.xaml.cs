using KubaToolKit.Shared.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace KubaToolKit.Modules.Dashboard;

public partial class MetricChartWindow
    : Window
{
    private readonly DashboardService _dashboardService = new();
    private readonly string _profile;
    private readonly string _dbInstanceIdentifier;
    private readonly string _metricName;
    private readonly string _unit;
    private List<(DateTime Timestamp, double Value)> _points = new();

    public MetricChartWindow(
        string profile,
        string dbInstanceIdentifier,
        string metricName,
        string metricDisplayName,
        string unit)
    {
        InitializeComponent();

        _profile = profile;
        _dbInstanceIdentifier = dbInstanceIdentifier;
        _metricName = metricName;
        _unit = unit;

        Title = $"{metricDisplayName} - {dbInstanceIdentifier}";
        TitleTextBlock.Text = metricDisplayName;
        SubtitleTextBlock.Text = $"{dbInstanceIdentifier} • Last 1 hour";

        Loaded += async (_, __) =>
            await LoadAsync();
    }

    private async Task
    LoadAsync()
    {
        try
        {
            LoadingProgressBar.Visibility =
                Visibility.Visible;

            _points =
                await _dashboardService.GetMetricHistory(
                    _profile,
                    _dbInstanceIdentifier,
                    _metricName,
                    TimeSpan.FromHours(1));

            DrawChart();
        }
        catch (Exception ex)
        {
            if (AwsSsoService.IsSsoExpired(ex))
            {
                var success =
                    await AwsSsoService.Login();

                if (success)
                {
                    await LoadAsync();
                    return;
                }
            }

            MessageBox.Show(
                ex.ToString(),
                "Metric loading error");
        }
        finally
        {
            LoadingProgressBar.Visibility =
                Visibility.Collapsed;
        }
    }

    private void
    ChartHost_SizeChanged(
        object sender,
        SizeChangedEventArgs e)
    {
        DrawChart();
    }

    private void
    DrawChart()
    {
        ChartCanvas.Children.Clear();

        double width =
            ChartCanvas.ActualWidth;

        double height =
            ChartCanvas.ActualHeight;

        if (width <= 0 || height <= 0)
        {
            return;
        }

        if (_points.Count == 0)
        {
            EmptyStateText.Visibility =
                Visibility.Visible;

            return;
        }

        EmptyStateText.Visibility =
            Visibility.Collapsed;

        const double leftAxisWidth = 46;
        const double bottomAxisHeight = 24;
        const double topPadding = 10;
        const double rightPadding = 10;

        double plotLeft = leftAxisWidth;
        double plotTop = topPadding;

        double plotWidth =
            Math.Max(10, width - leftAxisWidth - rightPadding);

        double plotHeight =
            Math.Max(10, height - bottomAxisHeight - topPadding);

        double minValue =
            _points.Min(p => p.Value);

        double maxValue =
            _points.Max(p => p.Value);

        if (Math.Abs(maxValue - minValue) < 0.0001)
        {
            // Plage plate : on ouvre un peu l'échelle pour que la ligne
            // ne colle pas aux bords du graphique.
            maxValue += 1;
            minValue = Math.Max(0, minValue - 1);
        }

        var accentBrush =
            (Brush)FindResource("AccentBrush");

        var borderBrush =
            (Brush)FindResource("BorderBrush");

        var mutedBrush =
            (Brush)FindResource("TextMutedBrush");

        var secondaryBrush =
            (Brush)FindResource("TextSecondaryBrush");

        // Gridlines horizontales + labels de valeur
        const int GridLines = 4;

        for (int i = 0; i <= GridLines; i++)
        {
            double ratio = (double)i / GridLines;
            double y = plotTop + plotHeight * ratio;
            double value = maxValue - (maxValue - minValue) * ratio;

            var line = new Line
            {
                X1 = plotLeft,
                X2 = plotLeft + plotWidth,
                Y1 = y,
                Y2 = y,
                Stroke = borderBrush,
                StrokeThickness = 1
            };

            ChartCanvas.Children.Add(line);

            var label = new TextBlock
            {
                Text = FormatValue(value),
                FontSize = 10,
                Foreground = mutedBrush
            };

            Canvas.SetLeft(label, 0);
            Canvas.SetTop(label, y - 7);

            ChartCanvas.Children.Add(label);
        }

        // Points -> coordonnées écran
        var startTime = _points.First().Timestamp;
        var endTime = _points.Last().Timestamp;

        double totalSeconds =
            Math.Max(1, (endTime - startTime).TotalSeconds);

        var screenPoints =
            _points.Select(p =>
            {
                double xRatio =
                    (p.Timestamp - startTime).TotalSeconds
                    / totalSeconds;

                double yRatio =
                    (p.Value - minValue)
                    / (maxValue - minValue);

                double x = plotLeft + plotWidth * xRatio;
                double y = plotTop + plotHeight * (1 - yRatio);

                return new Point(x, y);
            })
            .ToList();

        // Zone remplie sous la courbe
        var fillPoints = new PointCollection();

        fillPoints.Add(
            new Point(screenPoints.First().X, plotTop + plotHeight));

        foreach (var p in screenPoints)
        {
            fillPoints.Add(p);
        }

        fillPoints.Add(
            new Point(screenPoints.Last().X, plotTop + plotHeight));

        var accentColor = ((SolidColorBrush)accentBrush).Color;

        var fillBrush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1)
        };

        fillBrush.GradientStops.Add(
            new GradientStop(
                Color.FromArgb(70, accentColor.R, accentColor.G, accentColor.B),
                0));

        fillBrush.GradientStops.Add(
            new GradientStop(
                Color.FromArgb(0, accentColor.R, accentColor.G, accentColor.B),
                1));

        var fillPolygon = new Polygon
        {
            Points = fillPoints,
            Fill = fillBrush
        };

        ChartCanvas.Children.Add(fillPolygon);

        // Ligne de la courbe
        var polyline = new Polyline
        {
            Points = new PointCollection(screenPoints),
            Stroke = accentBrush,
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round
        };

        ChartCanvas.Children.Add(polyline);

        // Labels d'axe temporel (début / milieu / fin)
        AddTimeLabel(
            startTime,
            plotLeft,
            plotTop + plotHeight + 4,
            secondaryBrush,
            HorizontalAlignment.Left);

        AddTimeLabel(
            startTime.AddSeconds(totalSeconds / 2),
            plotLeft + plotWidth / 2,
            plotTop + plotHeight + 4,
            secondaryBrush,
            HorizontalAlignment.Center);

        AddTimeLabel(
            endTime,
            plotLeft + plotWidth,
            plotTop + plotHeight + 4,
            secondaryBrush,
            HorizontalAlignment.Right);
    }

    private void
    AddTimeLabel(
        DateTime time,
        double x,
        double y,
        Brush brush,
        HorizontalAlignment align)
    {
        var label = new TextBlock
        {
            Text = time.ToLocalTime().ToString("HH:mm"),
            FontSize = 10,
            Foreground = brush
        };

        ChartCanvas.Children.Add(label);

        label.Measure(
            new Size(double.PositiveInfinity, double.PositiveInfinity));

        double left = align switch
        {
            HorizontalAlignment.Center => x - label.DesiredSize.Width / 2,
            HorizontalAlignment.Right => x - label.DesiredSize.Width,
            _ => x
        };

        Canvas.SetLeft(label, left);
        Canvas.SetTop(label, y);
    }

    private string
    FormatValue(
        double value)
    {
        return _unit == "%"
            ? $"{value:F0}%"
            : $"{value:F0}";
    }
}

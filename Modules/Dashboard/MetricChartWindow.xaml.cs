using KubaToolKit.Shared.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace KubaToolKit.Modules.Dashboard;

public partial class MetricChartWindow
    : Window
{
    private class LoadedSeries
    {
        public string DisplayName { get; set; } = "";
        public string Unit { get; set; } = "";
        public Color Color { get; set; }
        public List<(DateTime Timestamp, double Value)> Points { get; set; } = new();
    }

    private readonly DashboardService _dashboardService = new();
    private readonly string _profile;
    private readonly string _subtitle;
    private readonly List<ChartSeriesRequest> _seriesRequests;
    private List<LoadedSeries> _series = new();

    public MetricChartWindow(
        string profile,
        string title,
        string subtitle,
        List<ChartSeriesRequest> seriesRequests)
    {
        InitializeComponent();

        _profile = profile;
        _subtitle = subtitle;
        _seriesRequests = seriesRequests;

        Title = title;
        TitleTextBlock.Text = title;
        SubtitleTextBlock.Text = subtitle;

        // Plage par défaut à l'ouverture : la dernière heure, comme avant
        // que la plage ne soit réglable -- l'utilisateur peut l'élargir
        // ensuite (Start/End + Apply), même logique que la recherche de
        // logs CloudWatch.
        var now = DateTime.Now;

        StartDatePicker.SelectedDate = now.AddHours(-1).Date;
        StartTimeTextBox.Text = now.AddHours(-1).ToString("HH:mm");
        EndDatePicker.SelectedDate = now.Date;
        EndTimeTextBox.Text = now.ToString("HH:mm");

        Loaded += async (_, __) =>
            await LoadAsync();
    }

    private async void
    ApplyRangeButton_Click(
        object sender,
        RoutedEventArgs e) =>
        await LoadAsync();

    private DateTime
    GetSelectedDateTime(
        DatePicker datePicker,
        TextBox timeTextBox)
    {
        var date = datePicker.SelectedDate ?? DateTime.Today;

        var timeOfDay =
            TimeSpan.TryParse(timeTextBox.Text, out var parsed)
                ? parsed
                : TimeSpan.Zero;

        return date.Date + timeOfDay;
    }

    private async Task
    LoadAsync()
    {
        try
        {
            LoadingProgressBar.Visibility =
                Visibility.Visible;

            var startLocal =
                GetSelectedDateTime(StartDatePicker, StartTimeTextBox);

            var endLocal =
                GetSelectedDateTime(EndDatePicker, EndTimeTextBox);

            if (endLocal <= startLocal)
            {
                MessageBox.Show(
                    "La date de fin doit être après la date de début.",
                    "Plage invalide");

                return;
            }

            var startUtc = startLocal.ToUniversalTime();
            var endUtc = endLocal.ToUniversalTime();

            SubtitleTextBlock.Text =
                $"{_subtitle} • {startLocal:dd/MM HH:mm} → {endLocal:dd/MM HH:mm}";

            var loaded = new List<LoadedSeries>();

            foreach (var request in _seriesRequests)
            {
                var points =
                    await _dashboardService.GetMetricHistory(
                        _profile,
                        request.Namespace,
                        request.MetricName,
                        request.Dimensions,
                        startUtc,
                        endUtc);

                loaded.Add(
                    new LoadedSeries
                    {
                        DisplayName = request.DisplayName,
                        Unit = request.Unit,
                        Color = request.Color,
                        Points = points
                    });
            }

            _series = loaded;

            DrawChart();
            DrawLegend();
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
    DrawLegend()
    {
        LegendPanel.Children.Clear();

        // Une seule courbe : le nom est déjà dans le titre, pas besoin de
        // légende séparée.
        if (_series.Count <= 1)
        {
            LegendPanel.Visibility = Visibility.Collapsed;
            return;
        }

        LegendPanel.Visibility = Visibility.Visible;

        foreach (var series in _series)
        {
            var item = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 16, 0)
            };

            var swatch = new Border
            {
                Width = 10,
                Height = 10,
                CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(series.Color),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            var hasData = series.Points.Count > 0;

            var label = new TextBlock
            {
                Text = hasData
                    ? series.DisplayName
                    : $"{series.DisplayName} (no data)",
                FontSize = 12,
                Foreground = (Brush)FindResource(
                    hasData ? "TextPrimaryBrush" : "TextMutedBrush"),
                VerticalAlignment = VerticalAlignment.Center
            };

            item.Children.Add(swatch);
            item.Children.Add(label);
            LegendPanel.Children.Add(item);
        }
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

        var allPoints =
            _series
                .SelectMany(s => s.Points)
                .ToList();

        if (allPoints.Count == 0)
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
            allPoints.Min(p => p.Value);

        double maxValue =
            allPoints.Max(p => p.Value);

        if (Math.Abs(maxValue - minValue) < 0.0001)
        {
            // Plage plate : on ouvre un peu l'échelle pour que la ligne
            // ne colle pas aux bords du graphique.
            maxValue += 1;
            minValue = Math.Max(0, minValue - 1);
        }

        var borderBrush =
            (Brush)FindResource("BorderBrush");

        var mutedBrush =
            (Brush)FindResource("TextMutedBrush");

        var secondaryBrush =
            (Brush)FindResource("TextSecondaryBrush");

        var unit =
            _series.FirstOrDefault()?.Unit
            ?? "";

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
                Text = FormatValue(value, unit),
                FontSize = 10,
                Foreground = mutedBrush
            };

            Canvas.SetLeft(label, 0);
            Canvas.SetTop(label, y - 7);

            ChartCanvas.Children.Add(label);
        }

        var startTime =
            allPoints.Min(p => p.Timestamp);

        var endTime =
            allPoints.Max(p => p.Timestamp);

        double totalSeconds =
            Math.Max(1, (endTime - startTime).TotalSeconds);

        bool singleSeries = _series.Count == 1;

        foreach (var series in _series)
        {
            if (series.Points.Count == 0)
            {
                continue;
            }

            var screenPoints =
                series.Points.Select(p =>
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

            // Zone remplie sous la courbe : seulement pour une courbe
            // unique, pour ne pas superposer plusieurs dégradés opaques.
            if (singleSeries)
            {
                var fillPoints = new PointCollection();

                fillPoints.Add(
                    new Point(screenPoints.First().X, plotTop + plotHeight));

                foreach (var p in screenPoints)
                {
                    fillPoints.Add(p);
                }

                fillPoints.Add(
                    new Point(screenPoints.Last().X, plotTop + plotHeight));

                var fillBrush = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(0, 1)
                };

                fillBrush.GradientStops.Add(
                    new GradientStop(
                        Color.FromArgb(70, series.Color.R, series.Color.G, series.Color.B),
                        0));

                fillBrush.GradientStops.Add(
                    new GradientStop(
                        Color.FromArgb(0, series.Color.R, series.Color.G, series.Color.B),
                        1));

                var fillPolygon = new Polygon
                {
                    Points = fillPoints,
                    Fill = fillBrush
                };

                ChartCanvas.Children.Add(fillPolygon);
            }

            var polyline = new Polyline
            {
                Points = new PointCollection(screenPoints),
                Stroke = new SolidColorBrush(series.Color),
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round
            };

            ChartCanvas.Children.Add(polyline);
        }

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
        double value,
        string unit)
    {
        return unit == "%"
            ? $"{value:F0}%"
            : $"{value:F0}";
    }
}

using KubaToolKit.Shared.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

    // Géométrie de la dernière zone de tracé dessinée, mémorisée pour
    // convertir une position souris (survol / sélection) en instant --
    // recalculée à chaque DrawChart() plutôt que dupliquée.
    private bool _hasPlotArea;
    private double _plotLeft;
    private double _plotTop;
    private double _plotWidth;
    private double _plotHeight;
    private DateTime _plotStartTimeUtc;
    private double _plotTotalSeconds;

    private bool _isSelecting;
    private double _selectionStartX;

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

            Logger.Info(
                $"MetricChartWindow: {loaded.Sum(s => s.Points.Count)} point(s) chargé(s) sur {loaded.Count} série(s).");
        }
        catch (Exception ex)
        {
            if (AwsSsoService.IsSsoExpired(ex))
            {
                Logger.Debug("MetricChartWindow: session SSO expirée, tentative de reconnexion.");

                var success =
                    await AwsSsoService.Login();

                if (success)
                {
                    await LoadAsync();
                    return;
                }
            }

            Logger.Error("MetricChartWindow: échec du chargement de la métrique.", ex);

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
        HideCrosshair();
        CancelSelection();
        _hasPlotArea = false;

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

        _hasPlotArea = true;
        _plotLeft = plotLeft;
        _plotTop = plotTop;
        _plotWidth = plotWidth;
        _plotHeight = plotHeight;
        _plotStartTimeUtc = startTime;
        _plotTotalSeconds = totalSeconds;

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

        // Labels d'axe temporel (début / milieu / fin) : sur une plage
        // courte l'heure seule suffit, mais dès qu'elle dépasse une
        // journée "HH:mm" seul devient ambigu (quel jour ?), donc on
        // ajoute la date.
        string timeFormat =
            totalSeconds >= 24 * 3600
                ? "dd/MM HH:mm"
                : "HH:mm";

        AddTimeLabel(
            startTime,
            plotLeft,
            plotTop + plotHeight + 4,
            secondaryBrush,
            HorizontalAlignment.Left,
            timeFormat);

        AddTimeLabel(
            startTime.AddSeconds(totalSeconds / 2),
            plotLeft + plotWidth / 2,
            plotTop + plotHeight + 4,
            secondaryBrush,
            HorizontalAlignment.Center,
            timeFormat);

        AddTimeLabel(
            endTime,
            plotLeft + plotWidth,
            plotTop + plotHeight + 4,
            secondaryBrush,
            HorizontalAlignment.Right,
            timeFormat);
    }

    // Survol : ligne verticale + infobulle date/heure et valeur de chaque
    // courbe au point le plus proche. Glisser : dessine un rectangle de
    // sélection puis, au relâchement, recharge le graphique borné à la
    // plage survolée -- un "zoom" sans dupliquer les champs Start/End,
    // qui restent la seule source de vérité de la plage affichée.
    private void
    OverlayCanvas_MouseMove(
        object sender,
        MouseEventArgs e)
    {
        if (!_hasPlotArea)
        {
            return;
        }

        var pos = e.GetPosition(OverlayCanvas);

        if (_isSelecting)
        {
            UpdateSelection(pos.X);
            return;
        }

        if (pos.X < _plotLeft
            || pos.X > _plotLeft + _plotWidth
            || pos.Y < _plotTop
            || pos.Y > _plotTop + _plotHeight)
        {
            HideCrosshair();
            return;
        }

        ShowCrosshair(pos.X);
    }

    private void
    OverlayCanvas_MouseLeave(
        object sender,
        MouseEventArgs e)
    {
        if (!_isSelecting)
        {
            HideCrosshair();
        }
    }

    private void
    OverlayCanvas_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (!_hasPlotArea)
        {
            return;
        }

        HideCrosshair();

        double x =
            Math.Clamp(
                e.GetPosition(OverlayCanvas).X,
                _plotLeft,
                _plotLeft + _plotWidth);

        _isSelecting = true;
        _selectionStartX = x;

        Canvas.SetLeft(SelectionRectangle, x);
        Canvas.SetTop(SelectionRectangle, _plotTop);
        SelectionRectangle.Width = 0;
        SelectionRectangle.Height = _plotHeight;
        SelectionRectangle.Visibility = Visibility.Visible;

        OverlayCanvas.CaptureMouse();
    }

    private async void
    OverlayCanvas_MouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (!_isSelecting)
        {
            return;
        }

        _isSelecting = false;
        OverlayCanvas.ReleaseMouseCapture();
        SelectionRectangle.Visibility = Visibility.Collapsed;

        double x =
            Math.Clamp(
                e.GetPosition(OverlayCanvas).X,
                _plotLeft,
                _plotLeft + _plotWidth);

        double left = Math.Min(_selectionStartX, x);
        double right = Math.Max(_selectionStartX, x);

        // Un simple clic (pas un vrai glisser) ne doit pas déclencher de
        // zoom sur une plage quasi nulle.
        const double MinDragPixels = 6;

        if (right - left < MinDragPixels)
        {
            return;
        }

        var zoomStartLocal = XToTimeUtc(left).ToLocalTime();
        var zoomEndLocal = XToTimeUtc(right).ToLocalTime();

        StartDatePicker.SelectedDate = zoomStartLocal.Date;
        StartTimeTextBox.Text = zoomStartLocal.ToString("HH:mm");
        EndDatePicker.SelectedDate = zoomEndLocal.Date;
        EndTimeTextBox.Text = zoomEndLocal.ToString("HH:mm");

        await LoadAsync();
    }

    private void
    CancelSelection()
    {
        _isSelecting = false;
        SelectionRectangle.Visibility = Visibility.Collapsed;
    }

    private void
    UpdateSelection(
        double currentX)
    {
        double x =
            Math.Clamp(currentX, _plotLeft, _plotLeft + _plotWidth);

        Canvas.SetLeft(SelectionRectangle, Math.Min(_selectionStartX, x));
        SelectionRectangle.Width = Math.Abs(x - _selectionStartX);
    }

    private DateTime
    XToTimeUtc(
        double x)
    {
        double ratio = (x - _plotLeft) / _plotWidth;

        return _plotStartTimeUtc.AddSeconds(ratio * _plotTotalSeconds);
    }

    private void
    ShowCrosshair(
        double x)
    {
        CrosshairLine.X1 = x;
        CrosshairLine.X2 = x;
        CrosshairLine.Y1 = _plotTop;
        CrosshairLine.Y2 = _plotTop + _plotHeight;
        CrosshairLine.Visibility = Visibility.Visible;

        var timeUtc = XToTimeUtc(x);

        TooltipTimeText.Text =
            timeUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm");

        TooltipValuesPanel.Children.Clear();

        bool showSeriesName = _series.Count > 1;

        foreach (var series in _series)
        {
            if (series.Points.Count == 0)
            {
                continue;
            }

            var nearest =
                series.Points
                    .OrderBy(p =>
                        Math.Abs((p.Timestamp - timeUtc).TotalSeconds))
                    .First();

            TooltipValuesPanel.Children.Add(
                new TextBlock
                {
                    FontSize = 11,
                    Foreground = new SolidColorBrush(series.Color),
                    Text = showSeriesName
                        ? $"{series.DisplayName}: {FormatValue(nearest.Value, series.Unit)}"
                        : FormatValue(nearest.Value, series.Unit)
                });
        }

        TooltipBorder.Visibility = Visibility.Visible;

        TooltipBorder.Measure(
            new Size(double.PositiveInfinity, double.PositiveInfinity));

        double tooltipX = x + 10;

        if (tooltipX + TooltipBorder.DesiredSize.Width > _plotLeft + _plotWidth)
        {
            tooltipX = x - 10 - TooltipBorder.DesiredSize.Width;
        }

        Canvas.SetLeft(TooltipBorder, tooltipX);
        Canvas.SetTop(TooltipBorder, _plotTop + 4);
    }

    private void
    HideCrosshair()
    {
        CrosshairLine.Visibility = Visibility.Collapsed;
        TooltipBorder.Visibility = Visibility.Collapsed;
    }

    private void
    AddTimeLabel(
        DateTime time,
        double x,
        double y,
        Brush brush,
        HorizontalAlignment align,
        string format)
    {
        var label = new TextBlock
        {
            Text = time.ToLocalTime().ToString(format),
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

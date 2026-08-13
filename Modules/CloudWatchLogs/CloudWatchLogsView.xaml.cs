using KubaToolKit.Modules.CloudWatchLogs.Models;
using KubaToolKit.Shared.Services;
using KubaToolKit.Shared.Windows;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace KubaToolKit.Modules.CloudWatchLogs;

public partial class CloudWatchLogsView
    : UserControl
{
    private readonly CloudWatchService _cloudWatchService = new();
    private ObservableCollection<LogGroupNode> _logGroupTree = new();
    private List<string> _allLogGroups = new();
    private List<LogGroupCategory> _logGroupCategories = new();
    private CancellationTokenSource? _searchCancellation;
    private string? _currentProfile;
    private string _currentSearchText = "";

    private const double QueryEditorLineHeight = 20;
    private const double QueryEditorMinHeight = 36;
    private const double QueryEditorMaxHeight = 420;

    // Star weights for LogGroups/Results when Log Groups is expanded, so
    // they split available height (50/50 by default, splitter-adjustable)
    // instead of Log Groups being pinned to a fixed pixel size. Same
    // pattern as the Dashboard's RDS/EC2 sections.
    private double _logGroupsStarWeight = 1;
    private double _resultsStarWeight = 1;

    private bool _rowAnimationRunning;
    private DateTime _rowAnimationStart;
    private static readonly TimeSpan RowAnimationDuration = TimeSpan.FromSeconds(0.45);
    private static readonly CubicEase RowAnimationEase = new() { EasingMode = EasingMode.EaseOut };
    private double _rowAnimationFromLogGroups;
    private double _rowAnimationToLogGroups;
    private double _rowAnimationFromResults;
    private double _rowAnimationToResults;

    private bool _debugModeEnabled;

    // Log Groups gets priority (fills the available height) while there's
    // nothing to show in Results yet -- no point reserving half the
    // window for an empty results list before a search has ever run.
    private bool _hasResults;

    public CloudWatchLogsView()
    {
        InitializeComponent();
        LoadLogGroupCategories();
        UpdateSectionRows();

        QueryEditorTextBox.TextChanged +=
            (_, _) => ResizeQueryEditorToFitContent();
    }

    private void
    LogGroupsExpander_ExpandedCollapsed(
        object sender,
        RoutedEventArgs e)
    {
        CaptureStarWeights();
        AnimateSectionRows();
    }

    // Reads back whatever ratio the two rows currently hold, but only
    // while both are genuinely still Star/Star (i.e. Log Groups was
    // expanded just before this toggle) -- otherwise Results is the
    // degenerate "Log Groups collapsed, full height" Star(1) and would
    // overwrite a real user-dragged ratio with junk.
    private void
    CaptureStarWeights()
    {
        if (LogGroupsRow.Height.IsStar && ResultsRow.Height.IsStar)
        {
            _logGroupsStarWeight = LogGroupsRow.Height.Value;
            _resultsStarWeight = ResultsRow.Height.Value;
        }
    }

    private void
    UpdateSectionRows()
    {
        if (LogGroupsRow == null
            || ResultsRow == null
            || LogGroupsExpander == null
            || LogGroupSplitter == null)
        {
            return;
        }

        bool logGroupsExpanded = LogGroupsExpander.IsExpanded;

        if (!logGroupsExpanded)
        {
            LogGroupsRow.Height = GridLength.Auto;
            ResultsRow.Height = new GridLength(1, GridUnitType.Star);
        }
        else if (!_hasResults)
        {
            LogGroupsRow.Height = new GridLength(1, GridUnitType.Star);
            ResultsRow.Height = GridLength.Auto;
        }
        else
        {
            LogGroupsRow.Height = new GridLength(_logGroupsStarWeight, GridUnitType.Star);
            ResultsRow.Height = new GridLength(_resultsStarWeight, GridUnitType.Star);
        }

        LogGroupSplitter.IsEnabled = logGroupsExpanded && _hasResults;
    }

    // RowDefinition.Height (GridLength) isn't itself animatable, and with
    // LogGroups/Results sharing one Grid, toggling Log Groups changes
    // both rows' target size at once. Both get measured and animated
    // together via absolute pixel heights driven from a single shared
    // CompositionTarget.Rendering tick -- see the Dashboard's
    // AnimateSectionRows for why this is safer than animating each row's
    // MaxHeight independently (two Star rows can momentarily both get
    // capped below their fair share at once, which reads as the section
    // snapping back small instead of settling at 50/50).
    private void
    AnimateSectionRows()
    {
        if (LogGroupsRow == null
            || ResultsRow == null
            || LogGroupsExpander == null
            || LogGroupSplitter == null)
        {
            return;
        }

        var oldLogGroupsHeight = LogGroupsRow.ActualHeight;
        var oldResultsHeight = ResultsRow.ActualHeight;

        UpdateSectionRows();

        LogGroupsRow.MaxHeight = double.PositiveInfinity;
        ResultsRow.MaxHeight = double.PositiveInfinity;

        UpdateLayout();

        var targetLogGroupsHeight = LogGroupsRow.ActualHeight;
        var targetResultsHeight = ResultsRow.ActualHeight;

        if (Math.Abs(oldLogGroupsHeight - targetLogGroupsHeight) < 0.5
            && Math.Abs(oldResultsHeight - targetResultsHeight) < 0.5)
        {
            return;
        }

        _rowAnimationFromLogGroups = oldLogGroupsHeight;
        _rowAnimationToLogGroups = targetLogGroupsHeight;
        _rowAnimationFromResults = oldResultsHeight;
        _rowAnimationToResults = targetResultsHeight;
        _rowAnimationStart = DateTime.UtcNow;

        LogGroupsRow.Height = new GridLength(Math.Max(0, oldLogGroupsHeight));
        ResultsRow.Height = new GridLength(Math.Max(0, oldResultsHeight));

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

        var logGroupsHeight = _rowAnimationFromLogGroups + (_rowAnimationToLogGroups - _rowAnimationFromLogGroups) * eased;
        var resultsHeight = _rowAnimationFromResults + (_rowAnimationToResults - _rowAnimationFromResults) * eased;

        LogGroupsRow.Height = new GridLength(Math.Max(0, logGroupsHeight));
        ResultsRow.Height = new GridLength(Math.Max(0, resultsHeight));

        if (t < 1)
        {
            return;
        }

        CompositionTarget.Rendering -= OnRowAnimationTick;
        _rowAnimationRunning = false;

        LogGroupsRow.MaxHeight = double.PositiveInfinity;
        ResultsRow.MaxHeight = double.PositiveInfinity;

        UpdateSectionRows();
    }

    // Driven by the Debug mode toggle now living in the Shell's shared
    // date-range row (only shown while CloudWatch is the active module).
    public void
    SetDebugModeVisible(
        bool visible)
    {
        _debugModeEnabled = visible;

        DebugModePanel.Visibility =
            visible ? Visibility.Visible : Visibility.Collapsed;

        if (!visible)
        {
            return;
        }

        try
        {
            QueryEditorTextBox.Text =
                _cloudWatchService
                    .BuildQuery(
                        _currentSearchText);

            Dispatcher.InvokeAsync(() =>
            {
                ApplyCloudWatchSyntaxHighlighting();
                ResizeQueryEditorToFitContent();
            });
        }
        catch
        {
        }
    }

    /// Reads the Shell-owned date/time range controls at the moment a custom query is run.
    public Func<(DateTime? StartDate, string StartTime, DateTime? EndDate, string EndTime)>? GetDateRange { get; set; }

    public bool IsSearchRunning => _searchCancellation != null;

    public void CancelSearch()
    {
        _searchCancellation?.Cancel();
    }

    private void
    LoadLogGroupCategories()
    {
        try
        {
            var filePath =
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "loggroup-categories.json");

            if (!File.Exists(
                    filePath))
            {
                return;
            }

            var json =
                File.ReadAllText(
                    filePath);

            _logGroupCategories =
                JsonSerializer
                    .Deserialize<
                        List<LogGroupCategory>>(
                        json)
                ?? new();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.ToString(),
                "Category loading error");
        }
    }

    public async Task
        LoadLogGroupsAsync(string? profile)
    {
        _currentProfile = profile;

        try
        {
            if (string.IsNullOrWhiteSpace(
                    profile))
            { return; }

            _allLogGroups =
                await _cloudWatchService
                    .GetLogGroups(
                        profile);
            BuildLogGroupTree(_allLogGroups);
            LogGroupsTreeView.ItemsSource = _logGroupTree;
        }
        catch (Exception ex)
        {
            if (AwsSsoService.IsSsoExpired(ex))
            {
                Logger.Debug("CloudWatchLogsView: SSO session expired, attempting reconnection.");

                var success =
                    await AwsSsoService.Login();

                if (success)
                {
                    await LoadLogGroupsAsync(profile);
                    return;
                }
            }

            Logger.Error(
                $"CloudWatchLogsView: failed to load log groups (profile '{profile}').",
                ex);

            MessageBox.Show(
                ex.ToString(),
                "Log groups loading error");
        }
    }

    private void
SearchAllLogsCheckBox_Changed(
    object sender,
    RoutedEventArgs e)
    {
        bool searchAll =
            SearchAllLogsCheckBox
                ?.IsChecked
            == true;

        LogGroupsTreeView.IsEnabled =
            !searchAll;

        foreach (var node in _logGroupTree)
        {
            node.IsChecked = searchAll;
        }
    }

    public async Task
    RunSearchAsync(
        string profile,
        string searchText,
        DateTime? startDate,
        string startTime,
        DateTime? endDate,
        string endTime)
    {
        _currentProfile = profile;
        _currentSearchText = searchText;

        Logger.Debug(
            $"CloudWatchLogsView: search '{searchText}' (profile '{profile}', {startDate:yyyy-MM-dd} {startTime} -> {endDate:yyyy-MM-dd} {endTime}).");

        try
        {
            SearchProgressBar.Value =
                0;

            ProgressTextBlock.Text =
                "Searching CloudWatch...";

            List<string>
                selectedLogGroups;

            if (SearchAllLogsCheckBox
                ?.IsChecked == true)
            {
                selectedLogGroups =
                    new();
            }
            else
            {
                selectedLogGroups =
                    GetSelectedLogGroups(
                        _logGroupTree);
            }

            var progress =
                new Progress<int>(
                    percent =>
                    {
                        SearchProgressBar.Value =
                            percent;

                        ProgressTextBlock.Text =
                            $"Searching... {percent}%";
                    });

            _searchCancellation =
                new CancellationTokenSource();

            var results =
                await _cloudWatchService
                    .SearchLogs(
                        profile,
                        searchText,
                        startDate,
                        startTime,
                        endDate,
                        endTime,
                        selectedLogGroups,
                        progress,
                        null,
                        _searchCancellation
                            .Token);

            Logger.Info(
                $"CloudWatchLogsView: search '{searchText}' completed, {results.Count} result(s).");

            DisplayResults(
                results);
        }
        catch (OperationCanceledException)
        {
            Logger.Debug("CloudWatchLogsView: search cancelled.");

            ProgressTextBlock.Text =
                "Search cancelled";

            SearchProgressBar.Value =
                0;
        }
        catch (Exception ex)
        {
            if (AwsSsoService.IsSsoExpired(ex))
            {
                Logger.Debug("CloudWatchLogsView: SSO session expired, attempting reconnection.");

                var success =
                    await AwsSsoService.Login();

                if (success)
                {
                    await RunSearchAsync(profile, searchText, startDate, startTime, endDate, endTime);
                    return;
                }
            }

            Logger.Error($"CloudWatchLogsView: search '{searchText}' failed.", ex);

            MessageBox.Show(ex.ToString(), "Search error");
        }
        finally
        {
            _searchCancellation = null;
        }
    }

    private List<string>
   GetSelectedLogGroups(
       IEnumerable<LogGroupNode> nodes)
    {
        var result =
            new List<string>();

        foreach (var node
                 in nodes)
        {
            if (node.IsChecked
                &&
                node.IsLeaf
                &&
                !string.IsNullOrWhiteSpace(
                    node.FullPath))
            {
                result.Add(
                    node.FullPath);
            }

            result.AddRange(
                GetSelectedLogGroups(
                    node.Children));
        }

        return result;
    }

    public async Task
        UseCustomQueryAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(
                    QueryEditorTextBox.Text))
            {
                MessageBox.Show(
                    "No custom query.");

                return;
            }

            if (string.IsNullOrWhiteSpace(
                    _currentProfile))
            {
                MessageBox.Show(
                    "Please select an AWS profile");

                return;
            }

            SearchProgressBar.Value =
                0;

            ProgressTextBlock.Text =
                "Executing custom query...";

            List<string>
    selectedLogGroups;

            if (SearchAllLogsCheckBox
                ?.IsChecked == true)
            {
                selectedLogGroups =
                    new();
            }
            else
            {
                selectedLogGroups =
                    GetSelectedLogGroups(
                        _logGroupTree);
            }

            var progress =
                new Progress<int>(
                    percent =>
                    {
                        SearchProgressBar.Value =
                            percent;

                        ProgressTextBlock.Text =
                            $"Searching... {percent}%";
                    });

            var (startDate, startTime, endDate, endTime) =
                GetDateRange?.Invoke()
                ?? (DateTime.Today, "00:00", DateTime.Today.AddDays(1), "00:00");

            var results =
                await _cloudWatchService
                    .SearchLogs(
                        _currentProfile,
                        _currentSearchText,
                        startDate,
                        startTime,
                        endDate,
                        endTime,
                        selectedLogGroups,
                        progress,
                        QueryEditorTextBox.Text);

            DisplayResults(
                results);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.ToString(),
                "Custom query error");
        }
    }

    private async void
        UseCustomQuery_Click(
            object sender,
            RoutedEventArgs e)
    {
        await UseCustomQueryAsync();
    }

    private void
    LogsDataGrid_DoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        try
        {
            if (sender
                is not DataGrid dataGrid)
            {
                return;
            }

            if (dataGrid.SelectedItem
                is not LogEntry selectedLog)
            {
                return;
            }

            // Deferred past the double-click's mouse-up: opening the window
            // synchronously while that input is still being processed lets
            // Windows mistake it for a drag on the new window, which
            // immediately minimizes it (a known WPF double-click gotcha).
            Dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                new Action(() =>
                {
                    var separatorIndex =
                        selectedLog.LogGroup.IndexOf(':');

                    var logGroupSubtitle =
                        separatorIndex >= 0
                            ? selectedLog.LogGroup[(separatorIndex + 1)..]
                            : selectedLog.LogGroup;

                    var viewer =
                        new JsonViewerWindow(
                            selectedLog.Message,
                            logGroupSubtitle);

                    viewer.Show();
                }));
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.ToString(),
                "Viewer error");
        }
    }

    private void
    ResizeQueryEditorToFitContent()
    {
        int lineCount =
            Math.Max(
                1,
                QueryEditorTextBox.Document.LineCount);

        double desiredHeight =
            Math.Clamp(
                lineCount * QueryEditorLineHeight + 16,
                QueryEditorMinHeight,
                QueryEditorMaxHeight);

        QueryEditorTextBox.Height =
            desiredHeight;
    }

    private void
        PreviewQuery_Click(
            object sender,
            RoutedEventArgs e)
    {
        try
        {
            QueryEditorTextBox.Text =
                _cloudWatchService
                    .BuildQuery(
                        _currentSearchText);

            Dispatcher.InvokeAsync(() =>
            {
                ApplyCloudWatchSyntaxHighlighting();
                ResizeQueryEditorToFitContent();
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.ToString(),
                "Preview query error");
        }
    }

    private void
        CopyQuery_Click(
            object sender,
            RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(
                QueryEditorTextBox.Text);

            MessageBox.Show(
                "Query copied.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.ToString(),
                "Copy error");
        }
    }

    private void
    DisplayResults(
        List<LogEntry> results)
    {
        var groupedResults =
            results
                .GroupBy(x =>
                    x.LogGroup)
                .OrderBy(x =>
                    x.Key)
                .Select(g =>
                    new LogGroupResult
                    {
                        LogGroup =
                            g.Key,

                        Count =
                            g.Count(),

                        Logs =
                            new ObservableCollection<LogEntry>(
                                g.OrderByDescending(
                                    x => x.Timestamp))
                    })
                .ToList();

        LogsGroupedItemsControl
            .ItemsSource =
                groupedResults;

        SearchProgressBar.Value =
            0;

        ProgressTextBlock.Text =
            $"Done ({results.Count} results)";

        CaptureStarWeights();

        _hasResults = groupedResults.Count > 0;

        AnimateSectionRows();
    }

    public void
    OnSearchTextChanged(
        string searchText)
    {
        _currentSearchText =
            searchText;

        try
        {
            if (!_debugModeEnabled)
            {
                return;
            }

            QueryEditorTextBox.Text =
                _cloudWatchService
                    .BuildQuery(
                        searchText);

            Dispatcher.InvokeAsync(() =>
            {
                ApplyCloudWatchSyntaxHighlighting();
                ResizeQueryEditorToFitContent();
            });
        }
        catch
        {
        }
    }

    private void
       ApplyCloudWatchSyntaxHighlighting()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(
                    QueryEditorTextBox.Text))
            {
                return;
            }

            QueryEditorTextBox.SyntaxHighlighting =
                null;

            QueryEditorTextBox.TextArea
                .TextView
                .LineTransformers
                .Clear();

            QueryEditorTextBox.TextArea
                .TextView
                .LineTransformers
                .Add(
                    new CloudWatchColorizer());

            QueryEditorTextBox.TextArea
                .TextView
                .Redraw();
        }
        catch
        {
        }
    }

    private string?
    GetSubCategory(
        string logGroup)
    {
        var lower =
            logGroup
                .ToLower();

        if (lower.Contains(
                "proxy"))
        {
            return "Proxy";
        }

        if (lower.Contains(
                "alert"))
        {
            return "Alert";
        }

        if (lower.Contains(
                "postgresql"))
        {
            return "Core";
        }

        if (lower.Contains(
                "instance"))
        {
            return "Instance";
        }

        return null;
    }

    private void
BuildLogGroupTree(
    List<string> logGroups)
    {
        _logGroupTree.Clear();

        var categoryNodes =
            new Dictionary<
                string,
                LogGroupNode>();

        foreach (var category
                 in _logGroupCategories)
        {
            var categoryNode =
                new LogGroupNode
                {
                    Name =
                        category.Name,

                    FullPath =
                        category.Name
                };

            _logGroupTree.Add(
                categoryNode);

            categoryNodes[
                category.Name] =
                    categoryNode;
        }

        var uncategorized =
            new LogGroupNode
            {
                Name =
                    "Uncategorized",

                FullPath =
                    "Uncategorized"
            };

        _logGroupTree.Add(
            uncategorized);

        foreach (var logGroup
                 in logGroups
                     .OrderBy(x => x))
        {
            LogGroupNode?
                targetCategory =
                    null;

            foreach (var category
                     in _logGroupCategories)
            {
                bool matched =
                    category.Patterns
                        .Any(pattern =>
                            logGroup.Contains(
                                pattern,
                                StringComparison
                                    .OrdinalIgnoreCase));

                if (!matched)
                {
                    continue;
                }

                targetCategory =
                    categoryNodes[
                        category.Name];

                break;
            }

            targetCategory
                ??=
                    uncategorized;

            var subCategory =
                GetSubCategory(
                    logGroup);

            LogGroupNode
                parentNode =
                    targetCategory;

            if (!string.IsNullOrWhiteSpace(
                    subCategory))
            {
                var subCategoryNode =
                    targetCategory
                        .Children
                        .FirstOrDefault(
                            x =>
                                x.Name
                                == subCategory);

                if (subCategoryNode
                    == null)
                {
                    subCategoryNode =
                        new LogGroupNode
                        {
                            Name =
                                subCategory,

                            FullPath =
                                subCategory
                        };

                    targetCategory
                        .Children
                        .Add(
                            subCategoryNode);
                }
                parentNode =
                    subCategoryNode;
            }

            var stageCategory =
                GetStageCategory(
                    logGroup);

            var stageNode =
                parentNode
                    .Children
                    .FirstOrDefault(
                        x =>
                            x.Name
                            == stageCategory);

            if (stageNode
                == null)
            {
                stageNode =
                    new LogGroupNode
                    {
                        Name =
                            stageCategory,

                        FullPath =
                            stageCategory
                    };

                parentNode
                    .Children
                    .Add(
                        stageNode);
            }

            stageNode
                .Children
                .Add(
                    new LogGroupNode
                    {
                        Name =
                            logGroup,

                        FullPath =
                            logGroup,

                        IsLeaf =
                            true
                    });
        }
    }

    private string
    GetStageCategory(
        string logGroup)
    {
        var match =
            Regex.Match(
                logGroup,
                @"-(a|b)(?=[\/\-]|$)",
                RegexOptions.IgnoreCase);

        if (match.Success)
        {
            return
                $"-{match.Groups[1]
                    .Value
                    .ToLower()}";
        }

        return "core";
    }

    private void
    LogGroupsTreeView_PreviewMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
    }
}

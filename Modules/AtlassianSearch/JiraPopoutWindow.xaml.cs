using KubaToolKit.Modules.AtlassianSearch.Models;
using KubaToolKit.Shared.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using KubaToolKit.Shared.Windows;

namespace KubaToolKit.Modules.AtlassianSearch;

// A standalone, auto-refreshing view of one Jira filter -- meant to stay
// open (e.g. on a second monitor) as a lightweight ticket watcher. Issues
// that appear between refreshes are highlighted (colored by priority)
// until opened in Jira or dismissed via "Mark all as read".
public partial class JiraPopoutWindow
    : Window
{
    private const string CurrentFiltersName = "(Current filters)";

    private readonly AtlassianService _atlassianService;
    private readonly AtlassianSettings _settings;
    private readonly Dictionary<string, SavedJiraFilter> _filtersByName = new(StringComparer.OrdinalIgnoreCase);

    private readonly ObservableCollection<JiraSearchResult> _results = new();
    private readonly HashSet<string> _knownKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _unreadKeys = new(StringComparer.OrdinalIgnoreCase);
    private bool _hasLoadedOnce;

    private SavedJiraFilter _activeFilter;

    private DataGridColumn? _sortColumn;
    private ListSortDirection _sortDirection = ListSortDirection.Ascending;

    private readonly DispatcherTimer _refreshTimer = new();

    // Fetched once (not per-issue) so opening several issues from this
    // window doesn't refetch it every time.
    private Dictionary<string, string> _jiraServiceDesksByProjectKey = new(StringComparer.OrdinalIgnoreCase);

    public JiraPopoutWindow(
        AtlassianService atlassianService,
        AtlassianSettings settings,
        SavedJiraFilter currentFilterSnapshot,
        List<SavedJiraFilter> savedFilters)
    {
        InitializeComponent();

        _atlassianService = atlassianService;
        _settings = settings;

        JiraGrid.ItemsSource = _results;

        currentFilterSnapshot.Name = CurrentFiltersName;
        _filtersByName[CurrentFiltersName] = currentFilterSnapshot;

        foreach (var filter in savedFilters)
        {
            _filtersByName[filter.Name] = filter;
        }

        var options =
            new List<NameValue> { new(CurrentFiltersName, CurrentFiltersName) }
                .Concat(
                    savedFilters
                        .Select(f => new NameValue(f.Name, f.Name))
                        .OrderBy(o => o.Display, StringComparer.OrdinalIgnoreCase))
                .ToList();

        SavedFilterCombo.ItemsSource = options;
        SavedFilterCombo.SelectedIndex = 0;

        _activeFilter = currentFilterSnapshot;

        _refreshTimer.Tick += (_, __) => _ = LoadAsync();

        Loaded += async (_, __) =>
        {
            _jiraServiceDesksByProjectKey = await _atlassianService.GetJiraServiceDesksByProjectKey(_settings);

            await LoadAsync();
        };

        Closed += (_, __) => _refreshTimer.Stop();
    }

    private void
    SavedFilterCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        var name = SavedFilterCombo.SelectedValue as string;

        if (string.IsNullOrWhiteSpace(name) || !_filtersByName.TryGetValue(name, out var filter))
        {
            return;
        }

        _activeFilter = filter;

        // A different filter is a different context -- carrying over
        // "new since last refresh" state across that switch would just
        // make everything look new.
        _knownKeys.Clear();
        _unreadKeys.Clear();
        _hasLoadedOnce = false;

        _ = LoadAsync();
    }

    private void
    RefreshIntervalCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        var minutesTag = (RefreshIntervalCombo.SelectedItem as ComboBoxItem)?.Tag as string;

        _refreshTimer.Stop();

        if (!int.TryParse(minutesTag, out var minutes) || minutes <= 0)
        {
            return;
        }

        _refreshTimer.Interval = TimeSpan.FromMinutes(minutes);
        _refreshTimer.Start();
    }

    private async void
    RefreshButton_Click(
        object sender,
        RoutedEventArgs e) =>
        await LoadAsync();

    private async Task
    LoadAsync()
    {
        try
        {
            var filter = _activeFilter;

            var results =
                await _atlassianService.SearchJira(
                    _settings,
                    filter.Query,
                    new JiraFieldFilter(filter.Project, filter.ProjectOperator),
                    new JiraFieldFilter(filter.Reporter, filter.ReporterOperator),
                    new JiraFieldFilter(filter.Assignee, filter.AssigneeOperator),
                    new JiraFieldFilter(filter.Priority, filter.PriorityOperator),
                    new JiraFieldFilter(filter.Status, filter.StatusOperator));

            // First load just establishes the baseline -- nothing should
            // look "new" the moment the window opens, only what shows up
            // on a refresh after that.
            if (_hasLoadedOnce)
            {
                foreach (var result in results)
                {
                    if (_knownKeys.Add(result.Key))
                    {
                        _unreadKeys.Add(result.Key);
                    }
                }
            }
            else
            {
                foreach (var result in results)
                {
                    _knownKeys.Add(result.Key);
                }

                _hasLoadedOnce = true;
            }

            foreach (var result in results)
            {
                result.IsUnread = _unreadKeys.Contains(result.Key);
            }

            _results.Clear();

            foreach (var result in results)
            {
                _results.Add(result);
            }

            if (_sortColumn != null)
            {
                DataGridSortHelper.ReapplySort(_results, _sortColumn, _sortDirection);
            }

            JiraGrid.Items.Refresh();

            StatusText.Text = $"{results.Count} issue(s) -- last refreshed {DateTime.Now:HH:mm:ss}.";
        }
        catch (Exception ex)
        {
            Logger.Error("JiraPopoutWindow: refresh failed.", ex);

            StatusText.Text = $"Refresh failed: {ex.Message}";
        }
    }

    private void
    MarkAllAsReadButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _unreadKeys.Clear();

        foreach (var result in _results)
        {
            result.IsUnread = false;
        }

        JiraGrid.Items.Refresh();
    }

    private void
    MarkAsRead(
        JiraSearchResult result)
    {
        _unreadKeys.Remove(result.Key);
        result.IsUnread = false;

        JiraGrid.Items.Refresh();
    }

    private void
    JiraGrid_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        if (DataGridSortHelper.FindAncestor<DataGridColumnHeader>(e.OriginalSource as DependencyObject)
            is { } header)
        {
            DataGridSortHelper.SortByColumn(_results, JiraGrid.Columns, header.Column, ref _sortColumn, ref _sortDirection);

            return;
        }

        if (JiraGrid.SelectedItem is JiraSearchResult result)
        {
            OpenJiraResult(result);
            MarkAsRead(result);
        }
    }

    private void
    OpenJiraResult_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is Button { DataContext: JiraSearchResult result })
        {
            OpenJiraResult(result);
            MarkAsRead(result);
        }
    }

    private void
    OpenJiraResult(
        JiraSearchResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Key))
        {
            OpenUrl(result.Url);

            return;
        }

        var isServiceDeskIssue = _jiraServiceDesksByProjectKey.ContainsKey(result.Project);

        var window =
            new JiraIssueViewerWindow(_atlassianService, _settings, result.Key, result.Url, isServiceDeskIssue);

        window.Show();
        window.Activate();
    }

    private static void
    OpenUrl(
        string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Error($"JiraPopoutWindow: failed to open '{url}'.", ex);

            AppMessageBox.Show(ex.ToString(), "Atlassian Search");
        }
    }
}

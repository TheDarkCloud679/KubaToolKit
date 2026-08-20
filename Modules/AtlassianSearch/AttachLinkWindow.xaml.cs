using KubaToolKit.Modules.AtlassianSearch.Models;
using KubaToolKit.Shared.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KubaToolKit.Shared.Windows;
using System.Globalization;

namespace KubaToolKit.Modules.AtlassianSearch;

public partial class AttachLinkWindow
    : Window
{
    private readonly AtlassianService _atlassianService;
    private readonly AtlassianSettings _settings;
    private readonly IncidentLibraryStorageService _storage;
    private readonly IncidentEntry _incident;
    private readonly Action _onLinksChanged;

    private List<AtlassianResultItem> _rawResults = new();
    private CancellationTokenSource? _searchCancellation;

    private static readonly NameValue AnyOption = new("", "(Any)");

    // Jira project key / status name -- both also double as the actual
    // JQL query parameters (see RunSearchAsync), not just a post-fetch
    // filter, so they can narrow a search that has no keyword at all.
    private string _filterProject = "";
    private string _filterStatus = "";

    // Confluence's CQL space filter needs the space KEY, but every result
    // row only ever carries the space's display name (there's no reason
    // to fetch the key just to show it) -- both are tracked so the combo
    // can drive the query by key while still matching results by name.
    private string _filterSpaceKey = "";
    private string _filterSpaceDisplay = "";

    private DateTime? _filterFrom;
    private DateTime? _filterTo;
    private bool _showJira = true;
    private bool _showConfluence = true;

    // null = keep the search's own order (Jira newest-first, Confluence by
    // relevance); set once either sort arrow is clicked.
    private bool? _sortDateAscending;

    public AttachLinkWindow(
        AtlassianService atlassianService,
        AtlassianSettings settings,
        IncidentLibraryStorageService storage,
        IncidentEntry incident,
        Action onLinksChanged)
    {
        InitializeComponent();

        _atlassianService = atlassianService;
        _settings = settings;
        _storage = storage;
        _incident = incident;
        _onLinksChanged = onLinksChanged;

        SubtitleText.Text = $"to incident \"{incident.Name}\"";

        _ = LoadFilterOptionsAsync();
    }

    // Populates Project/Status/Space from the site's own full lists
    // (the same source the Statistics tab's pickers use) rather than
    // from search results, so they can be set -- and used as real query
    // parameters, not just a post-fetch filter -- before a first search
    // has even run.
    private async Task
    LoadFilterOptionsAsync()
    {
        try
        {
            var projectsTask = _atlassianService.GetJiraProjects(_settings);
            var statusesTask = _atlassianService.GetJiraStatuses(_settings);
            var spacesTask = _atlassianService.GetConfluenceSpaces(_settings);

            await Task.WhenAll(projectsTask, statusesTask, spacesTask);

            FilterProjectCombo.ItemsSource = new List<NameValue> { AnyOption }.Concat(projectsTask.Result).ToList();
            FilterProjectCombo.SelectedIndex = 0;

            FilterStatusCombo.ItemsSource = new List<NameValue> { AnyOption }.Concat(statusesTask.Result).ToList();
            FilterStatusCombo.SelectedIndex = 0;

            FilterSpaceCombo.ItemsSource = new List<NameValue> { AnyOption }.Concat(spacesTask.Result).ToList();
            FilterSpaceCombo.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            Logger.Error("AttachLinkWindow: failed to load filter options.", ex);
        }
    }

    private void
    QueryTextBox_KeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _ = RunSearchAsync();
        }
    }

    private async void
    SearchButton_Click(
        object sender,
        RoutedEventArgs e) =>
        await RunSearchAsync();

    private async Task
    RunSearchAsync()
    {
        var query = QueryTextBox.Text.Trim();

        var hasAnyFilter =
            !string.IsNullOrEmpty(_filterProject)
            || !string.IsNullOrEmpty(_filterStatus)
            || !string.IsNullOrEmpty(_filterSpaceKey);

        // Mirrors SearchJira/SearchConfluence's own contract: each builds
        // a valid query from filters alone, but not from nothing at all.
        if (string.IsNullOrWhiteSpace(query) && !hasAnyFilter)
        {
            return;
        }

        if (!_showJira && !_showConfluence)
        {
            return;
        }

        _searchCancellation?.Cancel();
        _searchCancellation = new CancellationTokenSource();

        var cancellationToken = _searchCancellation.Token;

        SearchProgressBar.Visibility = Visibility.Visible;
        SearchButton.IsEnabled = false;
        EmptyStateText.Visibility = Visibility.Collapsed;

        try
        {
            var projectFilter =
                string.IsNullOrEmpty(_filterProject) ? JiraFieldFilter.Empty : new JiraFieldFilter(_filterProject, "=");

            var statusFilter =
                string.IsNullOrEmpty(_filterStatus) ? JiraFieldFilter.Empty : new JiraFieldFilter(_filterStatus, "=");

            var spaceKeys =
                string.IsNullOrEmpty(_filterSpaceKey) ? Array.Empty<string>() : new[] { _filterSpaceKey };

            var jiraTask =
                _showJira
                    ? _atlassianService.SearchJira(
                        _settings,
                        query,
                        projectFilter,
                        JiraFieldFilter.Empty,
                        JiraFieldFilter.Empty,
                        JiraFieldFilter.Empty,
                        statusFilter,
                        cancellationToken)
                    : Task.FromResult(new List<JiraSearchResult>());

            var confluenceTask =
                _showConfluence
                    ? _atlassianService.SearchConfluence(
                        _settings,
                        query,
                        spaceKeys,
                        null,
                        cancellationToken)
                    : Task.FromResult(new List<ConfluenceSearchResult>());

            await Task.WhenAll(jiraTask, confluenceTask);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            _rawResults =
                jiraTask.Result
                    .Select(r => BuildResultItem(new AtlassianResultItem
                    {
                        Type = IncidentLinkType.Jira,
                        Key = r.Key,
                        Project = r.Project,
                        Title = r.Summary,
                        Subtitle = $"Jira · {r.Reporter}",
                        Priority = r.Priority,
                        Status = r.Status,
                        Url = r.Url,
                        DateRaw = r.UpdatedDisplay
                    }))
                    .Concat(
                        confluenceTask.Result.Select(r => BuildResultItem(new AtlassianResultItem
                        {
                            Type = IncidentLinkType.Confluence,
                            Title = r.Title,
                            Subtitle = $"Confluence · {r.Space}",
                            PageId = r.Id,
                            Space = r.Space,
                            Url = r.Url,
                            DateRaw = r.LastModifiedDisplay
                        })))
                    .ToList();

            RefreshResultsDisplay();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.Error("AttachLinkWindow: search failed.", ex);

            AppMessageBox.Show(ex.Message, "Search error");
        }
        finally
        {
            SearchProgressBar.Visibility = Visibility.Collapsed;
            SearchButton.IsEnabled = true;
        }
    }

    // Parses DateRaw once right after a result is built, so filtering and
    // sorting never have to re-parse it on every keystroke/toggle.
    private static AtlassianResultItem
    BuildResultItem(
        AtlassianResultItem item)
    {
        if (DateTime.TryParse(item.DateRaw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            item.SortDate = parsed;
            item.DisplayDate = parsed.ToString("yyyy-MM-dd");
        }

        return item;
    }

    private void
    RefreshResultsDisplay()
    {
        foreach (var item in _rawResults)
        {
            item.IsAttached =
                item.IsJira
                    ? _incident.Links.Any(l => l.Type == IncidentLinkType.Jira && l.Key == item.Key)
                    : _incident.Links.Any(l => l.Type == IncidentLinkType.Confluence && l.PageId == item.PageId);
        }

        IEnumerable<AtlassianResultItem> filtered =
            _rawResults.Where(r => (r.IsJira && _showJira) || (r.IsConfluence && _showConfluence));

        if (!string.IsNullOrEmpty(_filterProject))
        {
            filtered = filtered.Where(r => string.Equals(r.Project, _filterProject, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(_filterSpaceDisplay))
        {
            filtered = filtered.Where(r => string.Equals(r.Space, _filterSpaceDisplay, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(_filterStatus))
        {
            filtered = filtered.Where(r => string.Equals(r.Status, _filterStatus, StringComparison.OrdinalIgnoreCase));
        }

        if (_filterFrom.HasValue)
        {
            filtered = filtered.Where(r => r.SortDate.HasValue && r.SortDate.Value.Date >= _filterFrom.Value.Date);
        }

        if (_filterTo.HasValue)
        {
            filtered = filtered.Where(r => r.SortDate.HasValue && r.SortDate.Value.Date <= _filterTo.Value.Date);
        }

        if (_sortDateAscending.HasValue)
        {
            filtered =
                _sortDateAscending.Value
                    ? filtered.OrderBy(r => r.SortDate ?? DateTime.MinValue)
                    : filtered.OrderByDescending(r => r.SortDate ?? DateTime.MinValue);
        }

        var displayed = filtered.ToList();

        ResultsItemsControl.ItemsSource = null;
        ResultsItemsControl.ItemsSource = displayed;

        EmptyStateText.Visibility = displayed.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        EmptyStateText.Text =
            _rawResults.Count == 0
                ? "No results for this search."
                : "No results match the current filters.";
    }

    private void
    FilterProjectCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        _filterProject = (string)FilterProjectCombo.SelectedValue;

        RefreshResultsDisplay();
    }

    private void
    FilterSpaceCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (FilterSpaceCombo.SelectedItem is NameValue selected)
        {
            // AnyOption's own Display ("(Any)") would otherwise read as a
            // real space name to filter by, once selected.Value is empty.
            _filterSpaceKey = selected.Value;
            _filterSpaceDisplay = string.IsNullOrEmpty(selected.Value) ? "" : selected.Display;
        }

        RefreshResultsDisplay();
    }

    private void
    TypeFilterCheckBox_Changed(
        object sender,
        RoutedEventArgs e)
    {
        // Both checkboxes default to IsChecked="True" in XAML, which fires
        // Checked during InitializeComponent itself -- for the first one
        // parsed, that's before the second one's x:Name field, or anything
        // declared later in the same XAML (ResultsItemsControl included,
        // via RefreshResultsDisplay below), is assigned yet.
        if (ShowJiraCheckBox == null || ShowConfluenceCheckBox == null || ResultsItemsControl == null)
        {
            return;
        }

        _showJira = ShowJiraCheckBox.IsChecked == true;
        _showConfluence = ShowConfluenceCheckBox.IsChecked == true;

        RefreshResultsDisplay();
    }

    private void
    FilterStatusCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        _filterStatus = (string)FilterStatusCombo.SelectedValue;

        RefreshResultsDisplay();
    }

    private void
    FilterDatePicker_SelectedDateChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        _filterFrom = FilterFromDatePicker.SelectedDate;
        _filterTo = FilterToDatePicker.SelectedDate;

        RefreshResultsDisplay();
    }

    private void
    SortDateAscendingButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _sortDateAscending = true;

        RefreshResultsDisplay();
    }

    private void
    SortDateDescendingButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _sortDateAscending = false;

        RefreshResultsDisplay();
    }

    private void
    Attach_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: AtlassianResultItem item })
        {
            return;
        }

        _incident.Links.Add(
            new IncidentLink
            {
                Type = item.Type,
                Key = item.Key,
                Project = item.Project,
                Priority = item.Priority,
                Status = item.Status,
                PageId = item.PageId,
                Space = item.Space,
                Title = item.Title,
                Url = item.Url,
                Date = item.DateRaw
            });

        try
        {
            _storage.SaveIncident(_incident);
        }
        catch (Exception ex)
        {
            _incident.Links.RemoveAt(_incident.Links.Count - 1);

            AppMessageBox.Show(ex.Message, "Save error");

            return;
        }

        RefreshResultsDisplay();

        _onLinksChanged();
    }

    private void
    CloseButton_Click(
        object sender,
        RoutedEventArgs e) =>
        Close();
}

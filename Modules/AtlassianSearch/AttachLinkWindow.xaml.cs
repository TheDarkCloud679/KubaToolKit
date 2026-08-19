using KubaToolKit.Modules.AtlassianSearch.Models;
using KubaToolKit.Shared.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KubaToolKit.Shared.Windows;
using System.Globalization;

namespace KubaToolKit.Modules.AtlassianSearch;

// A row of AttachLinkWindow's search results -- not persisted, just the
// display/attach-state shape for one Jira issue or Confluence page found
// by a keyword search.
public class AttachResultItem
{
    public IncidentLinkType Type { get; set; } = IncidentLinkType.Jira;
    public bool IsJira => Type == IncidentLinkType.Jira;
    public bool IsConfluence => Type == IncidentLinkType.Confluence;

    public string Key { get; set; } = "";
    public string Project { get; set; } = "";
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Priority { get; set; } = "";
    public string Status { get; set; } = "";
    public string PageId { get; set; } = "";
    public string Space { get; set; } = "";
    public string Url { get; set; } = "";

    // Raw ISO date string (Jira's "updated", Confluence's "lastModified"),
    // parsed once into SortDate/DisplayDate for filtering/sorting.
    public string DateRaw { get; set; } = "";
    public DateTime? SortDate { get; set; }
    public string DisplayDate { get; set; } = "";

    public bool IsAttached { get; set; }
}

public partial class AttachLinkWindow
    : Window
{
    private readonly AtlassianService _atlassianService;
    private readonly AtlassianSettings _settings;
    private readonly IncidentLibraryStorageService _storage;
    private readonly IncidentEntry _incident;
    private readonly Action _onLinksChanged;

    private List<AttachResultItem> _rawResults = new();
    private CancellationTokenSource? _searchCancellation;

    private static readonly NameValue AnyOption = new("", "(Any)");

    private string _filterProject = "";
    private string _filterSpace = "";
    private string _filterStatus = "";
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

        if (string.IsNullOrWhiteSpace(query))
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
            var jiraTask =
                _atlassianService.SearchJira(
                    _settings,
                    query,
                    JiraFieldFilter.Empty,
                    JiraFieldFilter.Empty,
                    JiraFieldFilter.Empty,
                    JiraFieldFilter.Empty,
                    JiraFieldFilter.Empty,
                    cancellationToken);

            var confluenceTask =
                _atlassianService.SearchConfluence(
                    _settings,
                    query,
                    Array.Empty<string>(),
                    null,
                    cancellationToken);

            await Task.WhenAll(jiraTask, confluenceTask);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            _rawResults =
                jiraTask.Result
                    .Select(r => BuildResultItem(new AttachResultItem
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
                        confluenceTask.Result.Select(r => BuildResultItem(new AttachResultItem
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

            _filterProject = "";
            _filterSpace = "";
            _filterStatus = "";
            _filterFrom = null;
            _filterTo = null;
            _sortDateAscending = null;

            PopulateFilterCombos();
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
    private static AttachResultItem
    BuildResultItem(
        AttachResultItem item)
    {
        if (DateTime.TryParse(item.DateRaw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            item.SortDate = parsed;
            item.DisplayDate = parsed.ToString("yyyy-MM-dd");
        }

        return item;
    }

    private void
    PopulateFilterCombos()
    {
        var projects =
            _rawResults
                .Where(r => r.IsJira && !string.IsNullOrWhiteSpace(r.Project))
                .Select(r => r.Project)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .Select(s => new NameValue(s, s))
                .Prepend(AnyOption)
                .ToList();

        var spaces =
            _rawResults
                .Where(r => r.IsConfluence && !string.IsNullOrWhiteSpace(r.Space))
                .Select(r => r.Space)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .Select(s => new NameValue(s, s))
                .Prepend(AnyOption)
                .ToList();

        var statuses =
            _rawResults
                .Where(r => r.IsJira && !string.IsNullOrWhiteSpace(r.Status))
                .Select(r => r.Status)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .Select(s => new NameValue(s, s))
                .Prepend(AnyOption)
                .ToList();

        FilterProjectCombo.ItemsSource = projects;
        FilterProjectCombo.SelectedIndex = 0;

        FilterSpaceCombo.ItemsSource = spaces;
        FilterSpaceCombo.SelectedIndex = 0;

        FilterStatusCombo.ItemsSource = statuses;
        FilterStatusCombo.SelectedIndex = 0;

        FilterFromDatePicker.SelectedDate = null;
        FilterToDatePicker.SelectedDate = null;
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

        IEnumerable<AttachResultItem> filtered =
            _rawResults.Where(r => (r.IsJira && _showJira) || (r.IsConfluence && _showConfluence));

        if (!string.IsNullOrEmpty(_filterProject))
        {
            filtered = filtered.Where(r => string.Equals(r.Project, _filterProject, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(_filterSpace))
        {
            filtered = filtered.Where(r => string.Equals(r.Space, _filterSpace, StringComparison.OrdinalIgnoreCase));
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
        _filterSpace = (string)FilterSpaceCombo.SelectedValue;

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
        if (sender is not Button { DataContext: AttachResultItem item })
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

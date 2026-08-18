using KubaToolKit.Modules.AtlassianSearch.Models;
using KubaToolKit.Shared.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KubaToolKit.Shared.Windows;

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
                    .Select(r => new AttachResultItem
                    {
                        Type = IncidentLinkType.Jira,
                        Key = r.Key,
                        Project = r.Project,
                        Title = r.Summary,
                        Subtitle = $"Jira · {r.Reporter}",
                        Priority = r.Priority,
                        Status = r.Status,
                        Url = r.Url
                    })
                    .Concat(
                        confluenceTask.Result.Select(r => new AttachResultItem
                        {
                            Type = IncidentLinkType.Confluence,
                            Title = r.Title,
                            Subtitle = $"Confluence · {r.Space}",
                            PageId = r.Id,
                            Space = r.Space,
                            Url = r.Url
                        }))
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

        ResultsItemsControl.ItemsSource = null;
        ResultsItemsControl.ItemsSource = _rawResults;

        EmptyStateText.Visibility = _rawResults.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyStateText.Text = "No results for this search.";
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
                Url = item.Url
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

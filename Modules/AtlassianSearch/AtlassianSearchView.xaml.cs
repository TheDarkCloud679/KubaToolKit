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

namespace KubaToolKit.Modules.AtlassianSearch;

public partial class AtlassianSearchView
    : UserControl
{
    private readonly AtlassianService _atlassianService = new();
    private readonly AtlassianSettingsService _settingsService = new();
    private AtlassianSettings _settings;

    private readonly ObservableCollection<ConfluenceSearchResult> _confluenceResults = new();
    private readonly ObservableCollection<JiraSearchResult> _jiraResults = new();

    private DataGridColumn? _confluenceSortColumn;
    private ListSortDirection _confluenceSortDirection = ListSortDirection.Ascending;

    private DataGridColumn? _jiraSortColumn;
    private ListSortDirection _jiraSortDirection = ListSortDirection.Ascending;

    // Cached so re-narrowing to favorites after a star toggle doesn't need
    // a fresh network round-trip.
    private List<NameValue> _allConfluenceSpaces = new();

    public AtlassianSearchView()
    {
        InitializeComponent();

        ConfluenceGrid.ItemsSource = _confluenceResults;
        JiraGrid.ItemsSource = _jiraResults;

        // Each dropdown is non-editable (its displayed value is always
        // exactly SelectedItem -- the one WPF behavior in this whole area
        // that's completely unambiguous) paired with its own search box
        // that only ever narrows which items are visible, never touching
        // the dropdown's selection.
        SetupSearchableCombo(ConfluenceSpaceSearchBox, ConfluenceSpaceCombo);
        SetupSearchableCombo(ConfluenceGroupSearchBox, ConfluenceGroupCombo);
        SetupSearchableCombo(ConfluencePageSearchBox, ConfluencePageCombo);
        SetupSearchableCombo(JiraProjectSearchBox, JiraProjectCombo);
        SetupSearchableCombo(JiraReporterSearchBox, JiraReporterCombo);
        SetupSearchableCombo(JiraAssigneeSearchBox, JiraAssigneeCombo);
        SetupSearchableCombo(JiraPrioritySearchBox, JiraPriorityCombo);
        SetupSearchableCombo(JiraStatusSearchBox, JiraStatusCombo);

        _settings = _settingsService.Load();

        UpdateStatusForMissingSettings();

        if (_settings.IsComplete)
        {
            _ = LoadFilterOptionsAsync();
        }
    }

    private const string DefaultJiraProjectName = "Customer Service";
    private static readonly NameValue AnyOption = new("", "(Any)");

    // Typing narrows which items the dropdown shows (the lists can be
    // long); picking one clears the search box, since at that point the
    // resolved value is what the combo itself is showing.
    private static void
    SetupSearchableCombo(
        TextBox searchBox,
        ComboBox combo)
    {
        searchBox.TextChanged += (_, __) =>
        {
            var text = searchBox.Text.Trim();

            combo.Items.Filter =
                string.IsNullOrEmpty(text)
                    ? null
                    : obj =>
                        obj is NameValue nv
                        && (nv.Value.Length == 0 || nv.Display.Contains(text, StringComparison.OrdinalIgnoreCase));

            combo.IsDropDownOpen = true;

            // Opening the dropdown moves keyboard focus onto its popup,
            // which would stop further typing from reaching the search box
            // -- pull focus straight back so it keeps behaving like a
            // normal search field while the list updates underneath it.
            // The popup's own focus grab isn't necessarily synchronous, so
            // this both retries immediately and again once every other
            // pending dispatcher operation (including whatever priority
            // that grab runs at) has drained.
            var caret = searchBox.CaretIndex;

            void RestoreSearchFocus()
            {
                searchBox.Focus();
                searchBox.CaretIndex = caret;
            }

            RestoreSearchFocus();

            searchBox.Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(RestoreSearchFocus));
        };

        combo.SelectionChanged += (_, __) => searchBox.Clear();
    }

    // Populates every filter dropdown, each independently -- one endpoint
    // being unavailable (a permission restriction, a site configuration
    // quirk...) shouldn't stop the others from loading, and any dropdown
    // left empty still accepts typed text since it's editable.
    private async Task
    LoadFilterOptionsAsync()
    {
        var spacesTask = _atlassianService.GetConfluenceSpaces(_settings);
        var projectsTask = _atlassianService.GetJiraProjects(_settings);
        var prioritiesTask = _atlassianService.GetJiraPriorities(_settings);
        var statusesTask = _atlassianService.GetJiraStatuses(_settings);
        var usersTask = _atlassianService.GetJiraUsers(_settings);

        await Task.WhenAll(spacesTask, projectsTask, prioritiesTask, statusesTask, usersTask);

        _allConfluenceSpaces = spacesTask.Result;

        PopulateConfluenceSpaceCombo();

        PopulateCombo(JiraProjectCombo, projectsTask.Result);
        PopulateCombo(JiraPriorityCombo, prioritiesTask.Result);
        PopulateCombo(JiraStatusCombo, statusesTask.Result);
        PopulateCombo(JiraReporterCombo, usersTask.Result);
        PopulateCombo(JiraAssigneeCombo, usersTask.Result);

        SelectComboOptionByDisplayName(JiraProjectCombo, projectsTask.Result, DefaultJiraProjectName);
    }

    // Favorited spaces sort to the top of the (still complete) list, marked
    // with a star, instead of hiding the rest of the site.
    private void
    PopulateConfluenceSpaceCombo()
    {
        var favoriteKeys = _settings.FavoriteConfluenceSpaceKeys;

        var favoriteSet = new HashSet<string>(favoriteKeys, StringComparer.OrdinalIgnoreCase);

        var ordered =
            _allConfluenceSpaces
                .OrderByDescending(s => favoriteSet.Contains(s.Value))
                .ThenBy(s => s.Display, StringComparer.OrdinalIgnoreCase)
                .Select(s =>
                    favoriteSet.Contains(s.Value)
                        ? new NameValue(s.Value, $"★ {s.Display}")
                        : s)
                .ToList();

        ConfluenceSpaceCombo.ItemsSource = new List<NameValue> { AnyOption }.Concat(ordered).ToList();
        ConfluenceSpaceCombo.Items.Filter = null;
        ConfluenceSpaceCombo.SelectedIndex = 0;

        if (favoriteKeys.Count == 1)
        {
            SelectComboItemByValue(ConfluenceSpaceCombo, favoriteKeys[0]);
        }

        UpdateFavoriteToggleVisual();
    }

    private static void
    SelectComboOptionByDisplayName(
        ComboBox combo,
        List<NameValue> options,
        string displayName)
    {
        var match =
            options.FirstOrDefault(o =>
                string.Equals(o.Display, displayName, StringComparison.OrdinalIgnoreCase));

        if (match.Value != null)
        {
            SelectComboItemByValue(combo, match.Value);
        }
    }

    private static void
    PopulateCombo(
        ComboBox combo,
        List<NameValue> options)
    {
        combo.ItemsSource = new List<NameValue> { AnyOption }.Concat(options).ToList();
        combo.Items.Filter = null;
        combo.SelectedIndex = 0;
    }

    private static void
    SelectComboItemByValue(
        ComboBox combo,
        string value)
    {
        if (combo.ItemsSource is not IEnumerable<NameValue> items)
        {
            return;
        }

        foreach (var item in items)
        {
            if (string.Equals(item.Value, value, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;

                return;
            }
        }
    }

    // The Space/Project/Priority/Status/Reporter/Assignee dropdowns pair
    // with their own search box, so a filter value can come from either
    // picking an item (SelectedValue holds the raw key/name to filter on)
    // or leaving nothing picked and typing free text into the search box
    // instead (SelectedValue is then "", the placeholder "(Any)" entry).
    private static string
    GetComboFilterValue(
        ComboBox combo,
        TextBox searchBox) =>
        combo.SelectedValue is string val && val.Length > 0
            ? val
            : searchBox.Text.Trim();

    // Group/Page aren't free-typable (their value is an opaque content Id,
    // not something you could usefully type), so only a real selection
    // counts even though they're searchable the same way as the others.
    private static string
    GetComboSelectionValue(
        ComboBox combo) =>
        combo.SelectedValue as string ?? "";

    private void
    FavoriteSpaceToggle_Click(
        object sender,
        RoutedEventArgs e)
    {
        var spaceKey = GetComboFilterValue(ConfluenceSpaceCombo, ConfluenceSpaceSearchBox);

        if (string.IsNullOrWhiteSpace(spaceKey))
        {
            FavoriteSpaceToggle.IsChecked = false;

            return;
        }

        var favorites = _settings.FavoriteConfluenceSpaceKeys;

        if (favorites.Any(k => string.Equals(k, spaceKey, StringComparison.OrdinalIgnoreCase)))
        {
            favorites.RemoveAll(k => string.Equals(k, spaceKey, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            favorites.Add(spaceKey);
        }

        _settingsService.Save(_settings);

        // Re-sorts/re-marks the list immediately instead of waiting for the
        // next full reload, keeping the space you just starred selected.
        PopulateConfluenceSpaceCombo();
        SelectComboItemByValue(ConfluenceSpaceCombo, spaceKey);
    }

    private void
    UpdateFavoriteToggleVisual()
    {
        var spaceKey = GetComboFilterValue(ConfluenceSpaceCombo, ConfluenceSpaceSearchBox);

        var isFavorite =
            !string.IsNullOrWhiteSpace(spaceKey)
            && _settings.FavoriteConfluenceSpaceKeys.Any(k =>
                string.Equals(k, spaceKey, StringComparison.OrdinalIgnoreCase));

        FavoriteSpaceToggle.IsChecked = isFavorite;
        FavoriteSpaceToggleGlyph.Text = isFavorite ? "★" : "☆";
    }

    private async void
    ConfluenceSpaceCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        UpdateFavoriteToggleVisual();

        PopulateCombo(ConfluenceGroupCombo, new List<NameValue>());
        PopulateCombo(ConfluencePageCombo, new List<NameValue>());

        var spaceKey = GetComboFilterValue(ConfluenceSpaceCombo, ConfluenceSpaceSearchBox);

        if (string.IsNullOrWhiteSpace(spaceKey) || !_settings.IsComplete)
        {
            return;
        }

        var groups = await _atlassianService.GetConfluenceSpaceGroups(_settings, spaceKey);

        PopulateCombo(ConfluenceGroupCombo, groups);
    }

    private async void
    ConfluenceGroupCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        PopulateCombo(ConfluencePageCombo, new List<NameValue>());

        var groupId = GetComboSelectionValue(ConfluenceGroupCombo);

        if (string.IsNullOrWhiteSpace(groupId))
        {
            return;
        }

        var pages = await _atlassianService.GetConfluenceChildPages(_settings, groupId);

        PopulateCombo(ConfluencePageCombo, pages);
    }

    private void
    UpdateStatusForMissingSettings()
    {
        if (!_settings.IsComplete)
        {
            StatusText.Text = "Set up the Jira/Confluence connection first (Settings).";
        }
    }

    private void
    SettingsButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var result =
            AtlassianSettingsWindow.Prompt(Window.GetWindow(this), _settings);

        if (result == null)
        {
            return;
        }

        _settings = result;
        _settingsService.Save(_settings);

        StatusText.Text = "";

        _ = LoadFilterOptionsAsync();
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

        if (!_settings.IsComplete)
        {
            MessageBox.Show(
                "Set up the Jira/Confluence connection first (Settings).",
                "Atlassian Search");

            return;
        }

        Logger.Debug($"AtlassianSearchView: searching for '{query}'.");

        try
        {
            LoadingProgressBar.Visibility = Visibility.Visible;
            SearchButton.IsEnabled = false;
            StatusText.Text = "";

            var confluenceTask = SearchConfluenceSafe(query);
            var jiraTask = SearchJiraSafe(query);

            await Task.WhenAll(confluenceTask, jiraTask);

            var (confluenceResults, confluenceError) = confluenceTask.Result;
            var (jiraResults, jiraError) = jiraTask.Result;

            _confluenceResults.Clear();

            foreach (var result in confluenceResults)
            {
                _confluenceResults.Add(result);
            }

            _jiraResults.Clear();

            foreach (var result in jiraResults)
            {
                _jiraResults.Add(result);
            }

            var errors =
                new[] { confluenceError, jiraError }
                    .Where(e => e != null)
                    .ToList();

            StatusText.Text =
                errors.Count > 0
                    ? string.Join(" ", errors)
                    : $"{confluenceResults.Count} Confluence page(s), {jiraResults.Count} Jira issue(s).";

            Logger.Info(
                $"AtlassianSearchView: search done, {confluenceResults.Count} Confluence, {jiraResults.Count} Jira.");
        }
        finally
        {
            LoadingProgressBar.Visibility = Visibility.Collapsed;
            SearchButton.IsEnabled = true;
        }
    }

    private async Task<(List<ConfluenceSearchResult> Results, string? Error)>
    SearchConfluenceSafe(
        string query)
    {
        try
        {
            var pageId = GetComboSelectionValue(ConfluencePageCombo);
            var groupId = GetComboSelectionValue(ConfluenceGroupCombo);

            var results =
                await _atlassianService.SearchConfluence(
                    _settings,
                    query,
                    GetComboFilterValue(ConfluenceSpaceCombo, ConfluenceSpaceSearchBox),
                    string.IsNullOrWhiteSpace(pageId) ? groupId : pageId);

            return (results, null);
        }
        catch (Exception ex)
        {
            Logger.Error("AtlassianSearchView: Confluence search failed.", ex);

            return (new List<ConfluenceSearchResult>(), $"Confluence: {ex.Message}");
        }
    }

    private async Task<(List<JiraSearchResult> Results, string? Error)>
    SearchJiraSafe(
        string query)
    {
        try
        {
            var results =
                await _atlassianService.SearchJira(
                    _settings,
                    query,
                    GetComboFilterValue(JiraProjectCombo, JiraProjectSearchBox),
                    GetComboFilterValue(JiraReporterCombo, JiraReporterSearchBox),
                    GetComboFilterValue(JiraAssigneeCombo, JiraAssigneeSearchBox),
                    GetComboFilterValue(JiraPriorityCombo, JiraPrioritySearchBox),
                    GetComboFilterValue(JiraStatusCombo, JiraStatusSearchBox));

            return (results, null);
        }
        catch (Exception ex)
        {
            Logger.Error("AtlassianSearchView: Jira search failed.", ex);

            return (new List<JiraSearchResult>(), $"Jira: {ex.Message}");
        }
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
            Logger.Error($"AtlassianSearchView: failed to open '{url}'.", ex);

            MessageBox.Show(ex.ToString(), "Atlassian Search");
        }
    }

    private void
    ConfluenceGrid_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        if (DataGridSortHelper.FindAncestor<DataGridColumnHeader>(e.OriginalSource as DependencyObject)
            is { } header)
        {
            DataGridSortHelper.SortByColumn(
                _confluenceResults,
                ConfluenceGrid.Columns,
                header.Column,
                ref _confluenceSortColumn,
                ref _confluenceSortDirection);

            return;
        }

        if (ConfluenceGrid.SelectedItem is ConfluenceSearchResult result)
        {
            OpenUrl(result.Url);
        }
    }

    private void
    JiraGrid_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        if (DataGridSortHelper.FindAncestor<DataGridColumnHeader>(e.OriginalSource as DependencyObject)
            is { } header)
        {
            DataGridSortHelper.SortByColumn(
                _jiraResults,
                JiraGrid.Columns,
                header.Column,
                ref _jiraSortColumn,
                ref _jiraSortDirection);

            return;
        }

        if (JiraGrid.SelectedItem is JiraSearchResult result)
        {
            OpenUrl(result.Url);
        }
    }

    private void
    OpenConfluenceResult_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ConfluenceSearchResult result })
        {
            OpenUrl(result.Url);
        }
    }

    private void
    OpenJiraResult_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is Button { DataContext: JiraSearchResult result })
        {
            OpenUrl(result.Url);
        }
    }
}

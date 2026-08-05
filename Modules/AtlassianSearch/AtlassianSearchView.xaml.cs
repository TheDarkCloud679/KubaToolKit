using KubaToolKit.Modules.AtlassianSearch.Models;
using KubaToolKit.Shared.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

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

    // The full (unfiltered) option list behind each dropdown, so typing a
    // search term can narrow what's shown without losing the rest -- a
    // combo's displayed ItemsSource can start narrower (Confluence spaces,
    // when favorites are set) than what's searchable.
    private readonly Dictionary<ComboBox, List<NameValue>> _comboMasterOptions = new();
    private bool _suppressComboFilter;

    // Cached so re-narrowing to favorites after a star toggle doesn't need
    // a fresh network round-trip.
    private List<NameValue> _allConfluenceSpaces = new();

    public AtlassianSearchView()
    {
        InitializeComponent();

        ConfluenceGrid.ItemsSource = _confluenceResults;
        JiraGrid.ItemsSource = _jiraResults;

        foreach (var combo in new[]
                 {
                     ConfluenceSpaceCombo, ConfluenceGroupCombo, ConfluencePageCombo,
                     JiraProjectCombo, JiraReporterCombo, JiraAssigneeCombo, JiraPriorityCombo, JiraStatusCombo
                 })
        {
            combo.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler(FilterableCombo_TextChanged), true);
        }

        _settings = _settingsService.Load();

        UpdateStatusForMissingSettings();

        if (_settings.IsComplete)
        {
            _ = LoadFilterOptionsAsync();
        }
    }

    private const string DefaultJiraProjectName = "Customer Service";
    private static readonly NameValue AnyOption = new("", "(Any)");

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

    // Starts the Space dropdown narrowed to favorited spaces (still
    // searchable to reach the rest, via the full list kept in
    // _comboMasterOptions) instead of every space on the site. With no
    // favorites, or exactly one, it behaves as before.
    private void
    PopulateConfluenceSpaceCombo()
    {
        var favoriteKeys = _settings.FavoriteConfluenceSpaceKeys;

        var favorites =
            _allConfluenceSpaces
                .Where(s => favoriteKeys.Any(k => string.Equals(k, s.Value, StringComparison.OrdinalIgnoreCase)))
                .ToList();

        _comboMasterOptions[ConfluenceSpaceCombo] = _allConfluenceSpaces;

        var initial = favorites.Count > 0 ? favorites : _allConfluenceSpaces;

        ConfluenceSpaceCombo.ItemsSource = new List<NameValue> { AnyOption }.Concat(initial).ToList();
        ConfluenceSpaceCombo.SelectedIndex = 0;

        if (favorites.Count == 1)
        {
            SelectComboItemByValue(ConfluenceSpaceCombo, favorites[0].Value);
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

    private void
    PopulateCombo(
        ComboBox combo,
        List<NameValue> options)
    {
        _comboMasterOptions[combo] = options;

        combo.ItemsSource = new List<NameValue> { AnyOption }.Concat(options).ToList();
        combo.SelectedIndex = 0;
    }

    // Every dropdown is a live search box: typing narrows the list to
    // matching entries (the lists can be long) without needing a separate
    // search field. Selecting an item also raises TextChanged (Text syncs
    // to the selection), which is detected and skipped below -- otherwise
    // rebuilding ItemsSource right after a pick would immediately wipe out
    // the selection that was just made.
    private void
    FilterableCombo_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        if (_suppressComboFilter || sender is not ComboBox combo)
        {
            return;
        }

        var text = combo.Text ?? "";

        if (combo.SelectedItem is NameValue selected
            && string.Equals(selected.Display, text, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!_comboMasterOptions.TryGetValue(combo, out var allOptions))
        {
            return;
        }

        var filtered =
            string.IsNullOrWhiteSpace(text)
                ? allOptions
                : allOptions
                    .Where(o => o.Display.Contains(text, StringComparison.OrdinalIgnoreCase))
                    .ToList();

        _suppressComboFilter = true;

        combo.ItemsSource = new List<NameValue> { AnyOption }.Concat(filtered).ToList();
        combo.SelectedItem = null;
        combo.Text = text;

        if (e.OriginalSource is TextBox textBox)
        {
            textBox.CaretIndex = text.Length;
        }

        combo.IsDropDownOpen = true;

        _suppressComboFilter = false;
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

    // The Space/Project/Priority/Status/Reporter/Assignee dropdowns stay
    // editable, so a filter value can come from either picking an item
    // (SelectedValue holds the raw key/name to filter on) or typing free
    // text that matches nothing in the list (SelectedValue is then null,
    // and combo.Text holds exactly what was typed).
    private static string
    GetComboFilterValue(
        ComboBox combo) =>
        combo.SelectedValue is string val
            ? val
            : (combo.Text ?? "").Trim();

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
        var spaceKey = GetComboFilterValue(ConfluenceSpaceCombo);

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

        UpdateFavoriteToggleVisual();
    }

    private void
    UpdateFavoriteToggleVisual()
    {
        var spaceKey = GetComboFilterValue(ConfluenceSpaceCombo);

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

        var spaceKey = GetComboFilterValue(ConfluenceSpaceCombo);

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
                    GetComboFilterValue(ConfluenceSpaceCombo),
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
                    GetComboFilterValue(JiraProjectCombo),
                    GetComboFilterValue(JiraReporterCombo),
                    GetComboFilterValue(JiraAssigneeCombo),
                    GetComboFilterValue(JiraPriorityCombo),
                    GetComboFilterValue(JiraStatusCombo));

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

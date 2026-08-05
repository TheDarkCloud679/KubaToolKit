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

    // Cached so the space picker doesn't need a fresh network round-trip
    // every time it's opened.
    private List<NameValue> _allConfluenceSpaces = new();
    private List<string> _selectedConfluenceSpaceKeys = new();

    // Set right when a search box's first keystroke opens its dropdown;
    // consumed (and cleared) the moment keyboard focus actually lands
    // somewhere else, so it's redirected straight back. Reacting to the
    // real focus change instead of guessing which dispatcher priority the
    // popup's own focus grab runs at -- two attempts at the latter both
    // missed it.
    private TextBox? _pendingFocusReturnBox;

    public AtlassianSearchView()
    {
        InitializeComponent();

        ConfluenceGrid.ItemsSource = _confluenceResults;
        JiraGrid.ItemsSource = _jiraResults;

        AddHandler(Keyboard.PreviewGotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(AtlassianSearchView_PreviewGotKeyboardFocus), true);

        // Each dropdown is non-editable (its displayed value is always
        // exactly SelectedItem -- the one WPF behavior in this whole area
        // that's completely unambiguous) paired with its own search box
        // that only ever narrows which items are visible, never touching
        // the dropdown's selection. Space is the exception -- it's a
        // separate popup window instead (see ConfluenceSpaceButton_Click).
        SetupSearchableCombo(ConfluenceGroupSearchBox, ConfluenceGroupCombo);
        SetupSearchableCombo(ConfluencePageSearchBox, ConfluencePageCombo);
        SetupSearchableCombo(ConfluenceArticleSearchBox, ConfluenceArticleCombo);
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

    private void
    AtlassianSearchView_PreviewGotKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e)
    {
        if (_pendingFocusReturnBox == null || ReferenceEquals(e.NewFocus, _pendingFocusReturnBox))
        {
            return;
        }

        var box = _pendingFocusReturnBox;
        _pendingFocusReturnBox = null;

        var caretIndex = box.Text.Length;

        Dispatcher.BeginInvoke(
            DispatcherPriority.Send,
            new Action(() =>
            {
                box.Focus();
                box.CaretIndex = caretIndex;
            }));
    }

    // Typing narrows which items the dropdown shows (the lists can be
    // long); picking one clears the search box, since at that point the
    // resolved value is what the combo itself is showing.
    private void
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

            var wasOpen = combo.IsDropDownOpen;

            combo.IsDropDownOpen = true;

            // Only the closed -> open transition steals focus; flag it so
            // the class handler above redirects focus back the moment it
            // notices the steal, whenever that actually happens.
            if (!wasOpen)
            {
                _pendingFocusReturnBox = searchBox;

                Dispatcher.BeginInvoke(
                    DispatcherPriority.ApplicationIdle,
                    new Action(() =>
                    {
                        if (ReferenceEquals(_pendingFocusReturnBox, searchBox))
                        {
                            _pendingFocusReturnBox = null;
                        }
                    }));
            }
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

        // A single starred space becomes the default selection, same as
        // starring used to auto-select it in the old combo; more than one
        // favorite is ambiguous, so it's left as "any" until picked.
        _selectedConfluenceSpaceKeys =
            _settings.FavoriteConfluenceSpaceKeys.Count == 1
                ? new List<string>(_settings.FavoriteConfluenceSpaceKeys)
                : new List<string>();

        await UpdateConfluenceSpaceSelectionAsync();

        PopulateCombo(JiraProjectCombo, projectsTask.Result);
        PopulateCombo(JiraPriorityCombo, prioritiesTask.Result);
        PopulateCombo(JiraStatusCombo, statusesTask.Result);
        PopulateCombo(JiraReporterCombo, usersTask.Result);
        PopulateCombo(JiraAssigneeCombo, usersTask.Result);

        SelectComboOptionByDisplayName(JiraProjectCombo, projectsTask.Result, DefaultJiraProjectName);
    }

    private void
    ConfluenceSpaceButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var result =
            ConfluenceSpacePickerWindow.Prompt(
                Window.GetWindow(this),
                _settings,
                _settingsService,
                _allConfluenceSpaces,
                _selectedConfluenceSpaceKeys);

        if (result == null)
        {
            return;
        }

        _selectedConfluenceSpaceKeys = result;

        _ = UpdateConfluenceSpaceSelectionAsync();
    }

    // Reflects the current space selection on the button and, since Group/
    // Page/Article only make sense scoped to exactly one space, cascades
    // into them: enabled and (re)loaded for a single space, cleared and
    // disabled otherwise.
    private async Task
    UpdateConfluenceSpaceSelectionAsync()
    {
        ConfluenceSpaceButton.Content = DescribeSelectedConfluenceSpaces();

        PopulateCombo(ConfluenceGroupCombo, new List<NameValue>());
        PopulateCombo(ConfluencePageCombo, new List<NameValue>());
        PopulateCombo(ConfluenceArticleCombo, new List<NameValue>());

        var singleSpace = _selectedConfluenceSpaceKeys.Count == 1;

        ConfluenceGroupSearchBox.IsEnabled = singleSpace;
        ConfluenceGroupCombo.IsEnabled = singleSpace;
        ConfluencePageSearchBox.IsEnabled = singleSpace;
        ConfluencePageCombo.IsEnabled = singleSpace;
        ConfluenceArticleSearchBox.IsEnabled = singleSpace;
        ConfluenceArticleCombo.IsEnabled = singleSpace;

        if (!singleSpace || !_settings.IsComplete)
        {
            return;
        }

        var groups = await _atlassianService.GetConfluenceSpaceGroups(_settings, _selectedConfluenceSpaceKeys[0]);

        PopulateCombo(ConfluenceGroupCombo, groups);
    }

    private string
    DescribeSelectedConfluenceSpaces()
    {
        if (_selectedConfluenceSpaceKeys.Count == 0)
        {
            return "(Any)";
        }

        if (_selectedConfluenceSpaceKeys.Count == 1)
        {
            var key = _selectedConfluenceSpaceKeys[0];

            var match =
                _allConfluenceSpaces.FirstOrDefault(s =>
                    string.Equals(s.Value, key, StringComparison.OrdinalIgnoreCase));

            return match.Display ?? key;
        }

        return $"{_selectedConfluenceSpaceKeys.Count} spaces";
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

    private async void
    ConfluenceGroupCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        PopulateCombo(ConfluencePageCombo, new List<NameValue>());
        PopulateCombo(ConfluenceArticleCombo, new List<NameValue>());

        var groupId = GetComboSelectionValue(ConfluenceGroupCombo);

        if (string.IsNullOrWhiteSpace(groupId))
        {
            return;
        }

        var pages = await _atlassianService.GetConfluenceChildPages(_settings, groupId);

        PopulateCombo(ConfluencePageCombo, pages);
    }

    // Confluence's KB pages are commonly three levels deep in practice
    // (e.g. "Articles de depannage" > "BackOffice" > the actual article),
    // not two -- Page's children are loaded the same way Group's are.
    private async void
    ConfluencePageCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        PopulateCombo(ConfluenceArticleCombo, new List<NameValue>());

        var pageId = GetComboSelectionValue(ConfluencePageCombo);

        if (string.IsNullOrWhiteSpace(pageId))
        {
            return;
        }

        var articles = await _atlassianService.GetConfluenceChildPages(_settings, pageId);

        PopulateCombo(ConfluenceArticleCombo, articles);
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

        if (!_settings.IsComplete)
        {
            MessageBox.Show(
                "Set up the Jira/Confluence connection first (Settings).",
                "Atlassian Search");

            return;
        }

        var hasQuery = !string.IsNullOrWhiteSpace(query);

        Logger.Debug(
            hasQuery
                ? $"AtlassianSearchView: searching for '{query}'."
                : "AtlassianSearchView: no search text -- showing the most recent Confluence pages instead.");

        try
        {
            LoadingProgressBar.Visibility = Visibility.Visible;
            SearchButton.IsEnabled = false;
            StatusText.Text = "";

            var confluenceTask = SearchConfluenceSafe(query);

            // Jira has no "recent items" equivalent -- an empty JQL text
            // clause is invalid, so it's skipped rather than erroring.
            var jiraTask =
                hasQuery
                    ? SearchJiraSafe(query)
                    : Task.FromResult<(List<JiraSearchResult> Results, string? Error)>((new(), null));

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

            // Repopulating can toggle the vertical scrollbar on/off, which
            // changes the columns' available width -- without this, the
            // last (button) column stays sized for the pre-population
            // layout and ends up clipped behind the now-visible scrollbar.
            Dispatcher.BeginInvoke(
                new Action(() => DataGridSortHelper.RefreshColumnWidths(ConfluenceGrid)),
                DispatcherPriority.Loaded);

            Dispatcher.BeginInvoke(
                new Action(() => DataGridSortHelper.RefreshColumnWidths(JiraGrid)),
                DispatcherPriority.Loaded);

            var errors =
                new[] { confluenceError, jiraError }
                    .Where(e => e != null)
                    .ToList();

            StatusText.Text =
                errors.Count > 0
                    ? string.Join(" ", errors)
                    : hasQuery
                        ? $"{confluenceResults.Count} Confluence page(s), {jiraResults.Count} Jira issue(s)."
                        : $"{confluenceResults.Count} most recent Confluence page(s).";

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
            var articleId = GetComboSelectionValue(ConfluenceArticleCombo);
            var pageId = GetComboSelectionValue(ConfluencePageCombo);
            var groupId = GetComboSelectionValue(ConfluenceGroupCombo);

            var ancestorId =
                !string.IsNullOrWhiteSpace(articleId) ? articleId
                : !string.IsNullOrWhiteSpace(pageId) ? pageId
                : groupId;

            var results =
                await _atlassianService.SearchConfluence(
                    _settings,
                    query,
                    _selectedConfluenceSpaceKeys,
                    ancestorId);

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
            OpenConfluenceResult(result);
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
            OpenConfluenceResult(result);
        }
    }

    // Falls back to the browser if a result somehow has no content Id
    // (shouldn't happen for a real search hit, but the parsing here is
    // defensive throughout, so this stays defensive too).
    private void
    OpenConfluenceResult(
        ConfluenceSearchResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Id))
        {
            OpenUrl(result.Url);

            return;
        }

        var window =
            new ConfluencePageViewerWindow(_atlassianService, _settings, result.Id, result.Title, result.Url)
            {
                Owner = Window.GetWindow(this)
            };

        window.Show();
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

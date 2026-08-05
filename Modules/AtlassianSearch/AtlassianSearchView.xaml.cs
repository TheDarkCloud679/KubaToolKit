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

    public AtlassianSearchView()
    {
        InitializeComponent();

        ConfluenceGrid.ItemsSource = _confluenceResults;
        JiraGrid.ItemsSource = _jiraResults;

        _settings = _settingsService.Load();

        UpdateStatusForMissingSettings();

        if (_settings.IsComplete)
        {
            _ = LoadFilterOptionsAsync();
        }
    }

    // Populates every filter dropdown, each independently -- one endpoint
    // being unavailable (a permission restriction, a site not having the
    // newer Confluence label API...) shouldn't stop the others from
    // loading, and any dropdown left empty still accepts typed text since
    // it's editable.
    private async Task
    LoadFilterOptionsAsync()
    {
        var spacesTask = _atlassianService.GetConfluenceSpaces(_settings);
        var labelsTask = _atlassianService.GetConfluenceLabels(_settings);
        var projectsTask = _atlassianService.GetJiraProjects(_settings);
        var prioritiesTask = _atlassianService.GetJiraPriorities(_settings);
        var statusesTask = _atlassianService.GetJiraStatuses(_settings);
        var usersTask = _atlassianService.GetJiraUsers(_settings);

        await Task.WhenAll(spacesTask, labelsTask, projectsTask, prioritiesTask, statusesTask, usersTask);

        PopulateCombo(ConfluenceSpaceCombo, spacesTask.Result);
        PopulateCombo(ConfluenceLabelCombo, labelsTask.Result);
        PopulateCombo(JiraProjectCombo, projectsTask.Result);
        PopulateCombo(JiraPriorityCombo, prioritiesTask.Result);
        PopulateCombo(JiraStatusCombo, statusesTask.Result);
        PopulateCombo(JiraReporterCombo, usersTask.Result);
        PopulateCombo(JiraAssigneeCombo, usersTask.Result);
    }

    private static void
    PopulateCombo(
        ComboBox combo,
        List<NameValue> options)
    {
        combo.Items.Clear();

        combo.Items.Add(new ComboBoxItem { Content = "(Any)", Tag = "" });

        foreach (var option in options)
        {
            combo.Items.Add(new ComboBoxItem { Content = option.Display, Tag = option.Value });
        }

        combo.SelectedIndex = 0;
    }

    // The dropdown is editable, so a filter value can come from either
    // picking an item (its Tag holds the raw key/name to filter on) or
    // typing free text that matches nothing in the list (SelectedItem is
    // then null, and combo.Text holds exactly what was typed).
    private static string
    GetComboFilterValue(
        ComboBox combo) =>
        combo.SelectedItem is ComboBoxItem { Tag: string tag }
            ? tag
            : (combo.Text ?? "").Trim();

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
            var results =
                await _atlassianService.SearchConfluence(
                    _settings,
                    query,
                    GetComboFilterValue(ConfluenceSpaceCombo),
                    GetComboFilterValue(ConfluenceLabelCombo));

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

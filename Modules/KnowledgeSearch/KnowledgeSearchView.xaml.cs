using KubaToolKit.Modules.KnowledgeSearch.Models;
using KubaToolKit.Shared.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace KubaToolKit.Modules.KnowledgeSearch;

public partial class KnowledgeSearchView
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

    public KnowledgeSearchView()
    {
        InitializeComponent();

        ConfluenceGrid.ItemsSource = _confluenceResults;
        JiraGrid.ItemsSource = _jiraResults;

        _settings = _settingsService.Load();

        UpdateStatusForMissingSettings();
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
                "Knowledge Search");

            return;
        }

        Logger.Debug($"KnowledgeSearchView: searching for '{query}'.");

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
                $"KnowledgeSearchView: search done, {confluenceResults.Count} Confluence, {jiraResults.Count} Jira.");
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
                    ConfluenceSpaceTextBox.Text.Trim(),
                    ConfluenceLabelTextBox.Text.Trim());

            return (results, null);
        }
        catch (Exception ex)
        {
            Logger.Error("KnowledgeSearchView: Confluence search failed.", ex);

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
                    JiraProjectTextBox.Text.Trim(),
                    JiraReporterTextBox.Text.Trim(),
                    JiraAssigneeTextBox.Text.Trim(),
                    JiraPriorityTextBox.Text.Trim(),
                    JiraStatusTextBox.Text.Trim());

            return (results, null);
        }
        catch (Exception ex)
        {
            Logger.Error("KnowledgeSearchView: Jira search failed.", ex);

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
            Logger.Error($"KnowledgeSearchView: failed to open '{url}'.", ex);

            MessageBox.Show(ex.ToString(), "Knowledge Search");
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

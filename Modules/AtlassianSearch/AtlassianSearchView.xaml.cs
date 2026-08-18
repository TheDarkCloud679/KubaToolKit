using Amazon.Runtime.CredentialManagement;
using KubaToolKit.Modules.ApiClient;
using KubaToolKit.Modules.AtlassianSearch.Models;
using KubaToolKit.Modules.ProjectInfo;
using KubaToolKit.Modules.Wiki;
using KubaToolKit.Shared.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using KubaToolKit.Shared.Windows;

namespace KubaToolKit.Modules.AtlassianSearch;

public partial class AtlassianSearchView
    : UserControl
{
    private readonly AtlassianService _atlassianService = new();
    private readonly AtlassianSettingsService _settingsService = new();
    private readonly IncidentLibraryStorageService _incidentStorage = new();
    private AtlassianSettings _settings;

    private readonly ObservableCollection<JiraSearchResult> _statsResults = new();

    private DataGridColumn? _statsSortColumn;
    private ListSortDirection _statsSortDirection = ListSortDirection.Ascending;

    // Non-null exactly while a stats query is in flight -- RunStatsButton
    // doubles as the cancel button during that window, same idea as the
    // Search/Cancel toggle the CloudWatch/CloudTrail/S3 modules use.
    private CancellationTokenSource? _statsCancellation;

    // Queues are a Jira Service Management concept scoped to one service
    // desk project -- resolved once here so OpenJiraResult can tell whether
    // a given issue's project is a service desk (isServiceDeskIssue).
    private Dictionary<string, string> _jiraServiceDesksByProjectKey = new(StringComparer.OrdinalIgnoreCase);

    // Backing state for every Statistics filter that supports picking
    // several values at once via MultiSelectPickerWindow.
    private readonly MultiSelectFilterState _statsProjectFilter = new();
    private readonly MultiSelectFilterState _statsAssigneeFilter = new();
    private readonly MultiSelectFilterState _statsStatusFilter = new();
    private readonly MultiSelectFilterState _statsModuleFilter = new();
    private readonly MultiSelectFilterState _statsEscalationFilter = new();

    private List<IncidentEntry> _incidents = new();
    private IncidentEntry? _selectedIncident;

    private WikiView? _wikiView;
    private ProjectInfoView? _projectInfoView;

    public AtlassianSearchView()
    {
        InitializeComponent();

        StatsGrid.ItemsSource = _statsResults;

        StatsViewListRadio.IsChecked = true;

        // Set here rather than IsSelected="True" in XAML -- that would
        // fire SelectionChanged (and so UpdateStatsChart, which touches
        // StatsChartItemsControl) mid-parse, before that element -- declared
        // later in the tree -- has actually been assigned yet.
        StatsGroupByCombo.SelectedIndex = 0;

        UpdateStatsViewVisibility();

        _settings = _settingsService.Load();

        PopulateJiraStatsSavedFilterCombo();

        UpdateStatusForMissingSettings();

        if (_settings.IsComplete)
        {
            _ = LoadFilterOptionsAsync();
        }

        LoadIncidents();

        PopulateProjectInfoProfileCombo();
    }

    // Project Info stays scoped per AWS profile (unlike the Wiki, which
    // went generic) -- the main nav's profile picker is deliberately hidden
    // in Atlassian mode, so this tab needs its own.
    private void
    PopulateProjectInfoProfileCombo()
    {
        try
        {
            var chain = new CredentialProfileStoreChain();

            var profiles =
                chain.ListProfiles()
                    .Select(x => x.Name)
                    .Where(x => x != "default")
                    .OrderBy(x => x)
                    .ToList();

            ProjectInfoProfileCombo.ItemsSource = profiles;

            if (profiles.Count > 0)
            {
                ProjectInfoProfileCombo.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("AtlassianSearchView: failed to load AWS profiles for Project Info.", ex);
        }
    }

    private void
    ProjectInfoProfileCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (ProjectInfoProfileCombo.SelectedItem is not string profileName)
        {
            return;
        }

        if (_projectInfoView == null)
        {
            return;
        }

        _projectInfoView.ChangeProfile(profileName);
    }

    private static readonly NameValue AnyOption = new("", "(Any)");

    // LibraryTabRadio has IsChecked="True" in XAML so Library opens by
    // default -- that fires this Checked handler once during
    // InitializeComponent, before StatsTabContent -- declared later in the
    // same XAML -- is wired up yet. The null guard skips that first,
    // premature call; the real initial state comes from the panels' own
    // XAML-default Visibility instead (same fix used throughout the app,
    // e.g. API Client's request-config tab strip).
    private void
    AtlassianTab_Changed(
        object sender,
        RoutedEventArgs e)
    {
        if (StatsTabContent == null || WikiTabContent == null || ProjectInfoTabContent == null)
        {
            return;
        }

        // Flush whichever tab is being left, so a debounced save isn't lost
        // to a tab switch happening before its 800ms timer fires.
        _wikiView?.FlushPendingSave();
        _projectInfoView?.FlushPendingSave();

        LibraryTabContent.Visibility =
            LibraryTabRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        StatsTabContent.Visibility =
            StatsTabRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        WikiTabContent.Visibility =
            WikiTabRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        ProjectInfoTabContent.Visibility =
            ProjectInfoTabRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        ProfilePickerPanel.Visibility =
            ProjectInfoTabRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        if (WikiTabRadio.IsChecked == true && _wikiView == null)
        {
            _wikiView = new WikiView();
            WikiTabContent.Content = _wikiView;
        }

        if (ProjectInfoTabRadio.IsChecked == true && _projectInfoView == null)
        {
            var profileName = ProjectInfoProfileCombo.SelectedItem as string ?? "";

            _projectInfoView = new ProjectInfoView(profileName);
            ProjectInfoTabContent.Content = _projectInfoView;
        }
    }

    // ===================================================================
    // Incident library
    // ===================================================================

    private class IncidentListRow
    {
        public IncidentEntry Entry { get; set; } = null!;
        public string Name { get; set; } = "";
        public string CountLabel { get; set; } = "";
        public Brush RowBackground { get; set; } = Brushes.Transparent;
        public Brush CountForeground { get; set; } = Brushes.Gray;
    }

    private class LinkRow
    {
        public IncidentLink Link { get; set; } = null!;
        public bool IsJira { get; set; }
        public bool IsConfluence { get; set; }
        public string Key { get; set; } = "";
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string Priority { get; set; } = "";
        public string Status { get; set; } = "";
    }

    private void
    LoadIncidents()
    {
        _incidents = _incidentStorage.LoadIncidents();

        RefreshIncidentList();
    }

    private void
    RefreshIncidentList()
    {
        var query = IncidentSearchBox.Text.Trim();

        var rows =
            _incidents
                .Where(i => string.IsNullOrEmpty(query) || i.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .Select(i =>
                {
                    var isSelected = ReferenceEquals(i, _selectedIncident);

                    return new IncidentListRow
                    {
                        Entry = i,
                        Name = i.Name,
                        CountLabel = i.Links.Count == 1 ? "1 link" : $"{i.Links.Count} links",
                        RowBackground = isSelected ? (Brush)FindResource("AccentSoftBrush") : Brushes.Transparent,
                        CountForeground = isSelected ? (Brush)FindResource("AccentPressedBrush") : (Brush)FindResource("TextMutedBrush")
                    };
                })
                .ToList();

        IncidentListItemsControl.ItemsSource = rows;
    }

    private void
    IncidentSearchBox_TextChanged(
        object sender,
        TextChangedEventArgs e) =>
        RefreshIncidentList();

    private void
    NewIncident_Click(
        object sender,
        RoutedEventArgs e)
    {
        var name =
            TextInputWindow.Prompt(
                Window.GetWindow(this),
                "New incident",
                "Name:");

        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        IncidentEntry entry;

        try
        {
            entry = _incidentStorage.CreateIncident(name);
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(ex.Message, "Create error");

            return;
        }

        _incidents.Add(entry);

        SelectIncident(entry);
    }

    private void
    DeleteIncident_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button { Tag: IncidentListRow row })
        {
            return;
        }

        if (AppMessageBox.Show(
                $"Permanently delete the incident \"{row.Entry.Name}\" (including its file)?",
                "Delete incident",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning)
            != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _incidentStorage.DeleteIncidentFile(row.Entry.FilePath ?? "");
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(ex.Message, "Delete error");

            return;
        }

        _incidents.Remove(row.Entry);

        if (ReferenceEquals(_selectedIncident, row.Entry))
        {
            _selectedIncident = null;

            UpdateIncidentDetailPanel();
        }

        RefreshIncidentList();
    }

    private void
    IncidentRow_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: IncidentListRow row })
        {
            return;
        }

        SelectIncident(row.Entry);
    }

    private void
    SelectIncident(
        IncidentEntry entry)
    {
        _selectedIncident = entry;

        RefreshIncidentList();
        UpdateIncidentDetailPanel();
    }

    private void
    UpdateIncidentDetailPanel()
    {
        if (_selectedIncident == null)
        {
            NoIncidentSelectedPanel.Visibility = Visibility.Visible;
            IncidentDetailPanel.Visibility = Visibility.Collapsed;

            return;
        }

        NoIncidentSelectedPanel.Visibility = Visibility.Collapsed;
        IncidentDetailPanel.Visibility = Visibility.Visible;

        IncidentNameTextBox.Text = _selectedIncident.Name;
        IncidentDescriptionTextBox.Text = _selectedIncident.Description;

        LinksSearchBox.Text = "";

        RefreshLinksList();
    }

    private void
    IncidentNameTextBox_LostFocus(
        object sender,
        RoutedEventArgs e)
    {
        if (_selectedIncident == null)
        {
            return;
        }

        var name = IncidentNameTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name) || name == _selectedIncident.Name)
        {
            return;
        }

        _selectedIncident.Name = name;

        SaveSelectedIncident();
        RefreshIncidentList();
    }

    private void
    IncidentDescriptionTextBox_LostFocus(
        object sender,
        RoutedEventArgs e)
    {
        if (_selectedIncident == null)
        {
            return;
        }

        var description = IncidentDescriptionTextBox.Text;

        if (description == _selectedIncident.Description)
        {
            return;
        }

        _selectedIncident.Description = description;

        SaveSelectedIncident();
    }

    private void
    SaveSelectedIncident()
    {
        if (_selectedIncident == null)
        {
            return;
        }

        try
        {
            _incidentStorage.SaveIncident(_selectedIncident);
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(ex.Message, "Save error");
        }
    }

    private void
    RefreshLinksList()
    {
        if (_selectedIncident == null)
        {
            return;
        }

        LinkCountText.Text =
            _selectedIncident.Links.Count == 1
                ? "1 linked item"
                : $"{_selectedIncident.Links.Count} linked items";

        var query = LinksSearchBox.Text.Trim();

        var rows =
            _selectedIncident.Links
                .Where(l => string.IsNullOrEmpty(query)
                    || l.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || l.Key.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Select(l => new LinkRow
                {
                    Link = l,
                    IsJira = l.Type == IncidentLinkType.Jira,
                    IsConfluence = l.Type == IncidentLinkType.Confluence,
                    Key = l.Key,
                    Title = l.Title,
                    Subtitle = l.Type == IncidentLinkType.Jira ? "Jira ticket" : $"Confluence page · {l.Space}",
                    Priority = l.Priority,
                    Status = l.Status
                })
                .ToList();

        LinksItemsControl.ItemsSource = rows;

        if (_selectedIncident.Links.Count == 0)
        {
            NoLinksText.Text = "No ticket or page linked yet. Click \"Link an item\" to search for one.";
            NoLinksText.Visibility = Visibility.Visible;
        }
        else if (rows.Count == 0)
        {
            NoLinksText.Text = $"No link matches \"{query}\".";
            NoLinksText.Visibility = Visibility.Visible;
        }
        else
        {
            NoLinksText.Visibility = Visibility.Collapsed;
        }
    }

    private void
    LinksSearchBox_TextChanged(
        object sender,
        TextChangedEventArgs e) =>
        RefreshLinksList();

    private void
    AttachLink_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_selectedIncident == null)
        {
            return;
        }

        if (!_settings.IsComplete)
        {
            AppMessageBox.Show(
                "Set up the Jira/Confluence connection first (Settings).",
                "Atlassian");

            return;
        }

        var window =
            new AttachLinkWindow(
                _atlassianService,
                _settings,
                _incidentStorage,
                _selectedIncident,
                onLinksChanged: () =>
                {
                    RefreshLinksList();
                    RefreshIncidentList();
                })
            {
                Owner = Window.GetWindow(this)
            };

        window.ShowDialog();
    }

    private void
    Unlink_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button { Tag: LinkRow row } || _selectedIncident == null)
        {
            return;
        }

        _selectedIncident.Links.Remove(row.Link);

        SaveSelectedIncident();
        RefreshLinksList();
        RefreshIncidentList();
    }

    private void
    LinkRow_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || sender is not FrameworkElement { Tag: LinkRow row })
        {
            return;
        }

        if (row.Link.Type == IncidentLinkType.Jira)
        {
            OpenJiraResult(
                new JiraSearchResult
                {
                    Key = row.Link.Key,
                    Project = row.Link.Project,
                    Summary = row.Link.Title,
                    Priority = row.Link.Priority,
                    Status = row.Link.Status,
                    Url = row.Link.Url
                });
        }
        else
        {
            OpenConfluenceResult(
                new ConfluenceSearchResult
                {
                    Id = row.Link.PageId,
                    Title = row.Link.Title,
                    Space = row.Link.Space,
                    Url = row.Link.Url
                });
        }
    }

    // ===================================================================
    // Statistics (unchanged from before the incident library)
    // ===================================================================

    // Populates every Statistics filter dropdown, each independently -- one
    // endpoint being unavailable (a permission restriction, a site
    // configuration quirk...) shouldn't stop the others from loading.
    private async Task
    LoadFilterOptionsAsync()
    {
        var projectsTask = _atlassianService.GetJiraProjects(_settings);
        var statusesTask = _atlassianService.GetJiraStatuses(_settings);
        var statusCategoriesTask = _atlassianService.GetJiraStatusCategories(_settings);
        var serviceDesksTask = _atlassianService.GetJiraServiceDesksByProjectKey(_settings);
        var usersTask = _atlassianService.GetJiraUsers(_settings);
        var moduleOptionsTask = _atlassianService.GetJiraFieldOptions(_settings, "Component (migrated)");
        var escalationOptionsTask = _atlassianService.GetJiraFieldOptions(_settings, "Escalade");

        await Task.WhenAll(
            projectsTask, statusesTask, statusCategoriesTask, serviceDesksTask, usersTask,
            moduleOptionsTask, escalationOptionsTask);

        JiraStatusColors.CategoryByStatus = statusCategoriesTask.Result;
        _jiraServiceDesksByProjectKey = serviceDesksTask.Result;

        _statsProjectFilter.AllOptions = projectsTask.Result;
        _statsAssigneeFilter.AllOptions = usersTask.Result;
        _statsStatusFilter.AllOptions = statusesTask.Result;
        _statsModuleFilter.AllOptions = moduleOptionsTask.Result;
        _statsEscalationFilter.AllOptions = escalationOptionsTask.Result;

        UpdateFilterButton(StatsProjectPickerButton, _statsProjectFilter);
        UpdateFilterButton(StatsAssigneePickerButton, _statsAssigneeFilter);
        UpdateFilterButton(StatsStatusPickerButton, _statsStatusFilter);
        UpdateFilterButton(StatsModulePickerButton, _statsModuleFilter);
        UpdateFilterButton(StatsEscalationPickerButton, _statsEscalationFilter);
    }

    // Shared by every multi-select filter button below -- opens the
    // picker pre-seeded with whatever's currently selected, and on OK
    // (a null result means Cancel) writes the new selection back into
    // both the state and the button's own summary text.
    private void
    OpenMultiSelectFilter(
        Button button,
        MultiSelectFilterState state,
        string title)
    {
        var result =
            MultiSelectPickerWindow.Prompt(Window.GetWindow(this), title, state.AllOptions, state.SelectedValues);

        if (result == null)
        {
            return;
        }

        state.SelectedValues = result;

        UpdateFilterButton(button, state);
    }

    private void
    StatsProjectPickerButton_Click(
        object sender,
        RoutedEventArgs e) =>
        OpenMultiSelectFilter(StatsProjectPickerButton, _statsProjectFilter, "Select project(s)");

    private void
    StatsAssigneePickerButton_Click(
        object sender,
        RoutedEventArgs e) =>
        OpenMultiSelectFilter(StatsAssigneePickerButton, _statsAssigneeFilter, "Select person(s)");

    private void
    StatsStatusPickerButton_Click(
        object sender,
        RoutedEventArgs e) =>
        OpenMultiSelectFilter(StatsStatusPickerButton, _statsStatusFilter, "Select status(es)");

    private void
    StatsModulePickerButton_Click(
        object sender,
        RoutedEventArgs e) =>
        OpenMultiSelectFilter(StatsModulePickerButton, _statsModuleFilter, "Select module(s)");

    private void
    StatsEscalationPickerButton_Click(
        object sender,
        RoutedEventArgs e) =>
        OpenMultiSelectFilter(StatsEscalationPickerButton, _statsEscalationFilter, "Select escalade(s)");

    private static void
    UpdateFilterButton(
        Button button,
        MultiSelectFilterState state) =>
        button.Content = state.Summary();

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

    private static string
    GetOperatorValue(
        ComboBox operatorCombo) =>
        (operatorCombo.SelectedItem as ComboBoxItem)?.Content as string ?? "=";

    private static void
    SelectOperatorValue(
        ComboBox operatorCombo,
        string op)
    {
        foreach (ComboBoxItem item in operatorCombo.Items)
        {
            if (string.Equals(item.Content as string, op, StringComparison.Ordinal))
            {
                operatorCombo.SelectedItem = item;

                return;
            }
        }

        operatorCombo.SelectedIndex = 0;
    }

    private void
    PopulateJiraStatsSavedFilterCombo()
    {
        var options =
            _settings.SavedJiraStatsFilters
                .Select(f => new NameValue(f.Name, f.Name))
                .OrderBy(o => o.Display, StringComparer.OrdinalIgnoreCase)
                .ToList();

        PopulateCombo(StatsSavedFilterCombo, options);

        DeleteStatsFilterButton.IsEnabled = false;
    }

    private void
    StatsSavedFilterCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        var name = StatsSavedFilterCombo.SelectedValue as string;

        DeleteStatsFilterButton.IsEnabled = !string.IsNullOrWhiteSpace(name);

        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var filter =
            _settings.SavedJiraStatsFilters.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

        if (filter != null)
        {
            ApplyJiraStatsSavedFilter(filter);
        }
    }

    private void
    ApplyJiraStatsSavedFilter(
        SavedJiraStatsFilter filter)
    {
        ApplyMultiSelectFilterToUi(StatsProjectPickerButton, _statsProjectFilter, StatsProjectOperatorCombo, filter.Project, filter.ProjectOperator);
        ApplyMultiSelectFilterToUi(StatsAssigneePickerButton, _statsAssigneeFilter, StatsAssigneeOperatorCombo, filter.Assignee, filter.AssigneeOperator);
        ApplyMultiSelectFilterToUi(StatsStatusPickerButton, _statsStatusFilter, StatsStatusOperatorCombo, filter.Status, filter.StatusOperator);
        ApplyMultiSelectFilterToUi(StatsModulePickerButton, _statsModuleFilter, StatsModuleOperatorCombo, filter.Module, filter.ModuleOperator);
        ApplyMultiSelectFilterToUi(StatsEscalationPickerButton, _statsEscalationFilter, StatsEscalationOperatorCombo, filter.Escalation, filter.EscalationOperator);

        StatsFromDatePicker.SelectedDate = filter.From;
        StatsToDatePicker.SelectedDate = filter.To;
    }

    // Maps a saved filter's (comma-list) value/operator back onto both
    // the picker state and its button's summary text -- the inverse of
    // building a JiraFieldFilter from them.
    private void
    ApplyMultiSelectFilterToUi(
        Button button,
        MultiSelectFilterState state,
        ComboBox operatorCombo,
        string value,
        string op)
    {
        state.SetFromCommaList(value);
        SelectOperatorValue(operatorCombo, NormalizeMultiSelectOperator(op));
        UpdateFilterButton(button, state);
    }

    // A filter saved before multi-select existed may carry "="/"!=" --
    // equivalent to "in"/"not in" against a single value, so still
    // restorable rather than silently dropped.
    private static string
    NormalizeMultiSelectOperator(
        string op) =>
        op switch
        {
            "=" => "in",
            "!=" => "not in",
            "in" or "not in" => op,
            _ => "in"
        };

    private void
    SaveStatsFilterButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var name =
            TextInputWindow.Prompt(
                Window.GetWindow(this),
                "Save stats filter",
                "Name for this saved list:");

        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var filter = BuildJiraStatsFilterSnapshot();
        filter.Name = name;

        _settings.SavedJiraStatsFilters.RemoveAll(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        _settings.SavedJiraStatsFilters.Add(filter);

        _settingsService.Save(_settings);

        PopulateJiraStatsSavedFilterCombo();
        SelectComboItemByValue(StatsSavedFilterCombo, name);
    }

    private void
    DeleteStatsFilterButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var name = StatsSavedFilterCombo.SelectedValue as string;

        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        _settings.SavedJiraStatsFilters.RemoveAll(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

        _settingsService.Save(_settings);

        PopulateJiraStatsSavedFilterCombo();
    }

    private SavedJiraStatsFilter
    BuildJiraStatsFilterSnapshot() =>
        new()
        {
            Project = _statsProjectFilter.JqlValue(),
            ProjectOperator = GetOperatorValue(StatsProjectOperatorCombo),
            Assignee = _statsAssigneeFilter.JqlValue(),
            AssigneeOperator = GetOperatorValue(StatsAssigneeOperatorCombo),
            Status = _statsStatusFilter.JqlValue(),
            StatusOperator = GetOperatorValue(StatsStatusOperatorCombo),
            Module = _statsModuleFilter.JqlValue(),
            ModuleOperator = GetOperatorValue(StatsModuleOperatorCombo),
            Escalation = _statsEscalationFilter.JqlValue(),
            EscalationOperator = GetOperatorValue(StatsEscalationOperatorCombo),
            From = StatsFromDatePicker.SelectedDate,
            To = StatsToDatePicker.SelectedDate
        };

    private async void
    RunStatsButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_statsCancellation != null)
        {
            _statsCancellation.Cancel();

            return;
        }

        if (!_settings.IsComplete)
        {
            AppMessageBox.Show(
                "Set up the Jira/Confluence connection first (Settings).",
                "Atlassian");

            return;
        }

        _statsCancellation = new CancellationTokenSource();

        var cancellationToken = _statsCancellation.Token;

        try
        {
            RunStatsButton.Content = "Cancel";
            StatsProgressBar.Visibility = Visibility.Visible;
            StatsStatusText.Text = "Running...";

            var project = new JiraFieldFilter(_statsProjectFilter.JqlValue(), GetOperatorValue(StatsProjectOperatorCombo));
            var assignee = new JiraFieldFilter(_statsAssigneeFilter.JqlValue(), GetOperatorValue(StatsAssigneeOperatorCombo));
            var status = new JiraFieldFilter(_statsStatusFilter.JqlValue(), GetOperatorValue(StatsStatusOperatorCombo));
            var module = new JiraFieldFilter(_statsModuleFilter.JqlValue(), GetOperatorValue(StatsModuleOperatorCombo));
            var escalation = new JiraFieldFilter(_statsEscalationFilter.JqlValue(), GetOperatorValue(StatsEscalationOperatorCombo));

            var results =
                await _atlassianService.SearchJiraStats(
                    _settings,
                    project,
                    assignee,
                    status,
                    module,
                    escalation,
                    StatsFromDatePicker.SelectedDate,
                    StatsToDatePicker.SelectedDate,
                    cancellationToken);

            _statsResults.Clear();

            foreach (var result in results)
            {
                _statsResults.Add(result);
            }

            StatsStatusText.Text = $"{results.Count} incident(s) found.";

            UpdateStatsChart();
        }
        catch (OperationCanceledException)
        {
            StatsStatusText.Text = "Cancelled.";
        }
        catch (Exception ex)
        {
            Logger.Error("AtlassianSearchView: stats query failed.", ex);

            StatsStatusText.Text = $"Stats query failed: {ex.Message}";
        }
        finally
        {
            RunStatsButton.Content = "Run stats";
            StatsProgressBar.Visibility = Visibility.Collapsed;

            _statsCancellation?.Dispose();
            _statsCancellation = null;
        }
    }

    private void
    StatsViewRadio_Checked(
        object sender,
        RoutedEventArgs e) =>
        UpdateStatsViewVisibility();

    private void
    UpdateStatsViewVisibility()
    {
        var showChart = StatsViewChartRadio.IsChecked == true;

        StatsGrid.Visibility = showChart ? Visibility.Collapsed : Visibility.Visible;
        StatsChartScrollViewer.Visibility = showChart ? Visibility.Visible : Visibility.Collapsed;
        StatsGroupByLabel.Visibility = showChart ? Visibility.Visible : Visibility.Collapsed;
        StatsGroupByCombo.Visibility = showChart ? Visibility.Visible : Visibility.Collapsed;

        if (showChart)
        {
            UpdateStatsChart();
        }
    }

    private void
    StatsGroupByCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e) =>
        UpdateStatsChart();

    // Computed client-side from whatever SearchJiraStats last returned --
    // no extra round trip needed, and it stays a genuine breakdown of the
    // exact same result set the List view shows, not a separately-queried
    // approximation.
    private void
    UpdateStatsChart()
    {
        if (StatsGroupByCombo.ItemsSource == null && StatsGroupByCombo.Items.Count == 0)
        {
            return;
        }

        var groupBy = (StatsGroupByCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Period";

        var groups =
            groupBy == "Period"
                ? BuildPeriodGroups()
                : BuildCategoryGroups(groupBy);

        const double MaxBarHeight = 160;

        var max = groups.Count > 0 ? groups.Max(g => g.Count) : 0;

        var bars =
            groups
                .Select(g =>
                    new ChartBarItem
                    {
                        Label = g.Label,
                        Count = g.Count,
                        BarHeight = max > 0 ? Math.Max(4, g.Count / (double)max * MaxBarHeight) : 4
                    })
                .ToList();

        StatsChartItemsControl.ItemsSource = bars;
    }

    private List<(string Label, int Count)>
    BuildCategoryGroups(
        string groupBy)
    {
        Func<JiraSearchResult, string> keySelector =
            groupBy switch
            {
                "Status" => r => string.IsNullOrWhiteSpace(r.Status) ? "(None)" : r.Status,
                "Priority" => r => string.IsNullOrWhiteSpace(r.Priority) ? "(None)" : r.Priority,
                "Project" => r => string.IsNullOrWhiteSpace(r.Project) ? "(None)" : r.Project,
                _ => r => string.IsNullOrWhiteSpace(r.Assignee) ? "(Unassigned)" : r.Assignee,
            };

        return
            _statsResults
                .GroupBy(keySelector)
                .Select(g => (Label: g.Key, Count: g.Count()))
                .OrderByDescending(g => g.Count)
                .ToList();
    }

    // "How many per month" -- bucketed by resolution date (falling back to
    // creation date for issues that were never resolved, e.g. when État
    // includes open/pending, so they still land somewhere on the timeline
    // rather than being silently dropped), regardless of whether From/To
    // narrowed the underlying query -- month is always the granularity,
    // per how this was asked for.
    private List<(string Label, int Count)>
    BuildPeriodGroups()
    {
        var buckets = new SortedDictionary<DateTime, int>();

        foreach (var result in _statsResults)
        {
            var date = ParseJiraDate(result.ResolvedDateRaw) ?? ParseJiraDate(result.CreatedDateRaw);

            if (date == null)
            {
                continue;
            }

            var monthStart = new DateTime(date.Value.Year, date.Value.Month, 1);

            buckets[monthStart] = buckets.GetValueOrDefault(monthStart) + 1;
        }

        return
            buckets
                .Select(kv => (Label: kv.Key.ToString("MMM yyyy", CultureInfo.InvariantCulture), Count: kv.Value))
                .ToList();
    }

    private static DateTime?
    ParseJiraDate(
        string raw) =>
        !string.IsNullOrWhiteSpace(raw) && DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    private void
    StatsGrid_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        if (DataGridSortHelper.FindAncestor<DataGridColumnHeader>(e.OriginalSource as DependencyObject)
            is { } header)
        {
            DataGridSortHelper.SortByColumn(
                _statsResults,
                StatsGrid.Columns,
                header.Column,
                ref _statsSortColumn,
                ref _statsSortDirection);

            return;
        }

        if (StatsGrid.SelectedItem is JiraSearchResult result)
        {
            OpenJiraResult(result);
        }
    }

    // ===================================================================
    // Shared: settings, popout, opening a result
    // ===================================================================

    private void
    UpdateStatusForMissingSettings()
    {
        if (!_settings.IsComplete)
        {
            StatusText.Text = "Set up the Jira/Confluence connection first (Settings).";
            StatusText.Visibility = Visibility.Visible;
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
        StatusText.Visibility = Visibility.Collapsed;

        _ = LoadFilterOptionsAsync();
    }

    // The popout starts unfiltered -- it has its own saved-filter combo to
    // pick from once open, and there's no longer an embedded Jira search
    // card here to seed an initial filter from.
    private void
    PopoutJiraButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!_settings.IsComplete)
        {
            AppMessageBox.Show(
                "Set up the Jira/Confluence connection first (Settings).",
                "Atlassian");

            return;
        }

        // No Owner: an owned non-modal window minimizing/restoring can
        // cascade to the main window in WPF -- same reasoning as the
        // other popups (MainWindow_Closing closes it explicitly instead).
        var window = new JiraPopoutWindow(_atlassianService, _settings, new SavedJiraFilter(), _settings.SavedJiraFilters);

        window.Show();
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

            AppMessageBox.Show(ex.ToString(), "Atlassian");
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

        // No Owner: an owned non-modal window minimizing/restoring can
        // cascade to the main window in WPF -- same reasoning as the
        // other popups (MainWindow_Closing closes it explicitly instead).
        var window = new ConfluencePageViewerWindow(_atlassianService, _settings, result.Id, result.Title, result.Url);

        window.Show();
    }

    private void
    OpenJiraResult_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is Button { DataContext: JiraSearchResult result })
        {
            OpenJiraResult(result);
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

        // No Owner: an owned non-modal window minimizing/restoring can
        // cascade to the main window in WPF -- same reasoning as the
        // other popups (MainWindow_Closing closes it explicitly instead).
        var window =
            new JiraIssueViewerWindow(_atlassianService, _settings, result.Key, result.Url, isServiceDeskIssue);

        window.Show();
    }
}

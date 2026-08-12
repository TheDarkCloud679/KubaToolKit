using KubaToolKit.Modules.ApiClient;
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
    private readonly ObservableCollection<JiraSearchResult> _statsResults = new();

    private DataGridColumn? _confluenceSortColumn;
    private ListSortDirection _confluenceSortDirection = ListSortDirection.Ascending;

    private DataGridColumn? _jiraSortColumn;
    private ListSortDirection _jiraSortDirection = ListSortDirection.Ascending;

    private DataGridColumn? _statsSortColumn;
    private ListSortDirection _statsSortDirection = ListSortDirection.Ascending;

    // Cached so the space picker doesn't need a fresh network round-trip
    // every time it's opened.
    private List<NameValue> _allConfluenceSpaces = new();
    private List<string> _selectedConfluenceSpaceKeys = new();

    // Queues are a Jira Service Management concept scoped to one service
    // desk project, keyed by a service desk Id that's a different value
    // from the project key shown in the Project dropdown -- resolved here
    // once so Queue's cascade doesn't need a network round-trip per project.
    private Dictionary<string, string> _jiraServiceDesksByProjectKey = new(StringComparer.OrdinalIgnoreCase);
    private string? _selectedJiraServiceDeskId;

    // Needed to evaluate a ">"/"<" priority filter client-side when
    // browsing a queue's results (see ApplyPriorityFilter) -- JQL search
    // doesn't need this, Jira resolves priority comparison against its
    // own configured order server-side.
    private List<string> _jiraPriorityRankOrder = new();

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
        StatsGrid.ItemsSource = _statsResults;

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
        SetupSearchableCombo(JiraQueueSearchBox, JiraQueueCombo);
        SetupSearchableCombo(JiraReporterSearchBox, JiraReporterCombo);
        SetupSearchableCombo(JiraAssigneeSearchBox, JiraAssigneeCombo);
        SetupSearchableCombo(JiraPrioritySearchBox, JiraPriorityCombo);
        SetupSearchableCombo(JiraStatusSearchBox, JiraStatusCombo);
        SetupSearchableCombo(StatsProjectSearchBox, StatsProjectCombo);
        SetupSearchableCombo(StatsAssigneeSearchBox, StatsAssigneeCombo);
        SetupSearchableCombo(StatsStatusSearchBox, StatsStatusCombo);

        StatsViewListRadio.IsChecked = true;

        // Set here rather than IsSelected="True" in XAML -- that would
        // fire SelectionChanged (and so UpdateStatsChart, which touches
        // StatsChartItemsControl) mid-parse, before that element -- declared
        // later in the tree -- has actually been assigned yet.
        StatsGroupByCombo.SelectedIndex = 0;

        UpdateStatsViewVisibility();

        _settings = _settingsService.Load();

        PopulateJiraSavedFilterCombo();
        PopulateJiraStatsSavedFilterCombo();

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
        var priorityRankOrderTask = _atlassianService.GetJiraPriorityRankOrder(_settings);
        var statusesTask = _atlassianService.GetJiraStatuses(_settings);
        var statusCategoriesTask = _atlassianService.GetJiraStatusCategories(_settings);
        var serviceDesksTask = _atlassianService.GetJiraServiceDesksByProjectKey(_settings);
        var usersTask = _atlassianService.GetJiraUsers(_settings);

        await Task.WhenAll(
            spacesTask, projectsTask, prioritiesTask, priorityRankOrderTask,
            statusesTask, statusCategoriesTask, serviceDesksTask, usersTask);

        JiraStatusColors.CategoryByStatus = statusCategoriesTask.Result;
        _jiraServiceDesksByProjectKey = serviceDesksTask.Result;
        _jiraPriorityRankOrder = priorityRankOrderTask.Result;

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
        PopulateCombo(StatsProjectCombo, projectsTask.Result);
        PopulateCombo(StatsAssigneeCombo, usersTask.Result);
        PopulateCombo(StatsStatusCombo, statusesTask.Result);

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

    private static JiraFieldFilter
    GetJiraFieldFilter(
        ComboBox valueCombo,
        TextBox searchBox,
        ComboBox operatorCombo) =>
        new(GetComboFilterValue(valueCombo, searchBox), GetOperatorValue(operatorCombo));

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

    // Queues only exist for Service Management projects, keyed by a
    // service desk Id rather than the project key shown here -- a plain
    // Jira project (no service desk) just leaves Queue empty, no error.
    private async void
    JiraProjectCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        PopulateCombo(JiraQueueCombo, new List<NameValue>());

        _selectedJiraServiceDeskId = null;

        var projectKey = GetComboFilterValue(JiraProjectCombo, JiraProjectSearchBox);

        if (string.IsNullOrWhiteSpace(projectKey)
            || !_jiraServiceDesksByProjectKey.TryGetValue(projectKey, out var serviceDeskId))
        {
            return;
        }

        _selectedJiraServiceDeskId = serviceDeskId;

        var queues = await _atlassianService.GetJiraQueues(_settings, serviceDeskId);

        PopulateCombo(JiraQueueCombo, queues);
    }

    private void
    PopulateJiraSavedFilterCombo()
    {
        var options =
            _settings.SavedJiraFilters
                .Select(f => new NameValue(f.Name, f.Name))
                .OrderBy(o => o.Display, StringComparer.OrdinalIgnoreCase)
                .ToList();

        PopulateCombo(JiraSavedFilterCombo, options);

        DeleteJiraFilterButton.IsEnabled = false;
    }

    private void
    JiraSavedFilterCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        var name = JiraSavedFilterCombo.SelectedValue as string;

        DeleteJiraFilterButton.IsEnabled = !string.IsNullOrWhiteSpace(name);

        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var filter =
            _settings.SavedJiraFilters.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

        if (filter != null)
        {
            ApplyJiraSavedFilter(filter);
        }
    }

    private void
    ApplyJiraSavedFilter(
        SavedJiraFilter filter)
    {
        QueryTextBox.Text = filter.Query;

        ApplyJiraFieldFilterToUi(JiraProjectCombo, JiraProjectSearchBox, JiraProjectOperatorCombo, filter.Project, filter.ProjectOperator);
        ApplyJiraFieldFilterToUi(JiraReporterCombo, JiraReporterSearchBox, JiraReporterOperatorCombo, filter.Reporter, filter.ReporterOperator);
        ApplyJiraFieldFilterToUi(JiraAssigneeCombo, JiraAssigneeSearchBox, JiraAssigneeOperatorCombo, filter.Assignee, filter.AssigneeOperator);
        ApplyJiraFieldFilterToUi(JiraPriorityCombo, JiraPrioritySearchBox, JiraPriorityOperatorCombo, filter.Priority, filter.PriorityOperator);
        ApplyJiraFieldFilterToUi(JiraStatusCombo, JiraStatusSearchBox, JiraStatusOperatorCombo, filter.Status, filter.StatusOperator);
    }

    // A saved "in"/"not in" value is a comma-separated list that will
    // never match a single combo item, so the search box (which
    // GetComboFilterValue falls back to) has to carry it -- the combo
    // selection is reset first so a leftover prior selection can't win
    // over the just-restored text.
    private static void
    ApplyJiraFieldFilterToUi(
        ComboBox valueCombo,
        TextBox searchBox,
        ComboBox operatorCombo,
        string value,
        string op)
    {
        SelectOperatorValue(operatorCombo, op);

        SelectComboItemByValue(valueCombo, "");
        searchBox.Text = value;

        if (!string.IsNullOrWhiteSpace(value) && !value.Contains(','))
        {
            SelectComboItemByValue(valueCombo, value);
        }
    }

    private void
    SaveJiraFilterButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var name =
            TextInputWindow.Prompt(
                Window.GetWindow(this),
                "Save Jira filter",
                "Name for this saved list:");

        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var filter = BuildJiraFilterSnapshot();
        filter.Name = name;

        _settings.SavedJiraFilters.RemoveAll(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        _settings.SavedJiraFilters.Add(filter);

        _settingsService.Save(_settings);

        PopulateJiraSavedFilterCombo();
        SelectComboItemByValue(JiraSavedFilterCombo, name);
    }

    private void
    DeleteJiraFilterButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var name = JiraSavedFilterCombo.SelectedValue as string;

        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        _settings.SavedJiraFilters.RemoveAll(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

        _settingsService.Save(_settings);

        PopulateJiraSavedFilterCombo();
    }

    private SavedJiraFilter
    BuildJiraFilterSnapshot() =>
        new()
        {
            Query = QueryTextBox.Text.Trim(),
            Project = GetComboFilterValue(JiraProjectCombo, JiraProjectSearchBox),
            ProjectOperator = GetOperatorValue(JiraProjectOperatorCombo),
            Reporter = GetComboFilterValue(JiraReporterCombo, JiraReporterSearchBox),
            ReporterOperator = GetOperatorValue(JiraReporterOperatorCombo),
            Assignee = GetComboFilterValue(JiraAssigneeCombo, JiraAssigneeSearchBox),
            AssigneeOperator = GetOperatorValue(JiraAssigneeOperatorCombo),
            Priority = GetComboFilterValue(JiraPriorityCombo, JiraPrioritySearchBox),
            PriorityOperator = GetOperatorValue(JiraPriorityOperatorCombo),
            Status = GetComboFilterValue(JiraStatusCombo, JiraStatusSearchBox),
            StatusOperator = GetOperatorValue(JiraStatusOperatorCombo)
        };

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
        ApplyJiraFieldFilterToUi(StatsProjectCombo, StatsProjectSearchBox, StatsProjectOperatorCombo, filter.Project, filter.ProjectOperator);
        ApplyJiraFieldFilterToUi(StatsAssigneeCombo, StatsAssigneeSearchBox, StatsAssigneeOperatorCombo, filter.Assignee, filter.AssigneeOperator);
        ApplyJiraFieldFilterToUi(StatsStatusCombo, StatsStatusSearchBox, StatsStatusOperatorCombo, filter.Status, filter.StatusOperator);

        SelectOperatorValue(StatsModuleOperatorCombo, filter.ModuleOperator);
        StatsModuleSearchBox.Text = filter.Module;

        SelectOperatorValue(StatsEscalationOperatorCombo, filter.EscalationOperator);
        StatsEscalationSearchBox.Text = filter.Escalation;

        StatsFromDatePicker.SelectedDate = filter.From;
        StatsToDatePicker.SelectedDate = filter.To;
    }

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
            Project = GetComboFilterValue(StatsProjectCombo, StatsProjectSearchBox),
            ProjectOperator = GetOperatorValue(StatsProjectOperatorCombo),
            Assignee = GetComboFilterValue(StatsAssigneeCombo, StatsAssigneeSearchBox),
            AssigneeOperator = GetOperatorValue(StatsAssigneeOperatorCombo),
            Status = GetComboFilterValue(StatsStatusCombo, StatsStatusSearchBox),
            StatusOperator = GetOperatorValue(StatsStatusOperatorCombo),
            Module = StatsModuleSearchBox.Text.Trim(),
            ModuleOperator = GetOperatorValue(StatsModuleOperatorCombo),
            Escalation = StatsEscalationSearchBox.Text.Trim(),
            EscalationOperator = GetOperatorValue(StatsEscalationOperatorCombo),
            From = StatsFromDatePicker.SelectedDate,
            To = StatsToDatePicker.SelectedDate
        };

    private async void
    RunStatsButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!_settings.IsComplete)
        {
            MessageBox.Show(
                "Set up the Jira/Confluence connection first (Settings).",
                "Atlassian Search");

            return;
        }

        try
        {
            RunStatsButton.IsEnabled = false;
            StatsStatusText.Text = "Running...";

            var project = GetJiraFieldFilter(StatsProjectCombo, StatsProjectSearchBox, StatsProjectOperatorCombo);
            var assignee = GetJiraFieldFilter(StatsAssigneeCombo, StatsAssigneeSearchBox, StatsAssigneeOperatorCombo);
            var status = GetJiraFieldFilter(StatsStatusCombo, StatsStatusSearchBox, StatsStatusOperatorCombo);
            var module = new JiraFieldFilter(StatsModuleSearchBox.Text.Trim(), GetOperatorValue(StatsModuleOperatorCombo));
            var escalation = new JiraFieldFilter(StatsEscalationSearchBox.Text.Trim(), GetOperatorValue(StatsEscalationOperatorCombo));

            var results =
                await _atlassianService.SearchJiraStats(
                    _settings,
                    project,
                    assignee,
                    status,
                    module,
                    escalation,
                    StatsFromDatePicker.SelectedDate,
                    StatsToDatePicker.SelectedDate);

            _statsResults.Clear();

            foreach (var result in results)
            {
                _statsResults.Add(result);
            }

            StatsStatusText.Text = $"{results.Count} incident(s) found.";

            UpdateStatsChart();
        }
        catch (Exception ex)
        {
            Logger.Error("AtlassianSearchView: stats query failed.", ex);

            StatsStatusText.Text = $"Stats query failed: {ex.Message}";
        }
        finally
        {
            RunStatsButton.IsEnabled = true;
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

        var groupBy = (StatsGroupByCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Assignee";

        Func<JiraSearchResult, string> keySelector =
            groupBy switch
            {
                "Status" => r => string.IsNullOrWhiteSpace(r.Status) ? "(None)" : r.Status,
                "Priority" => r => string.IsNullOrWhiteSpace(r.Priority) ? "(None)" : r.Priority,
                "Project" => r => string.IsNullOrWhiteSpace(r.Project) ? "(None)" : r.Project,
                _ => r => string.IsNullOrWhiteSpace(r.Assignee) ? "(Unassigned)" : r.Assignee,
            };

        var groups =
            _statsResults
                .GroupBy(keySelector)
                .Select(g => new { Label = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .ToList();

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

    // The popout only ever runs a plain JQL search (see BuildJiraFilterSnapshot)
    // -- a selected Queue's own fixed criteria isn't something SavedJiraFilter
    // can express, so it's intentionally left out of what gets popped out.
    private void
    PopoutJiraButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!_settings.IsComplete)
        {
            MessageBox.Show(
                "Set up the Jira/Confluence connection first (Settings).",
                "Atlassian Search");

            return;
        }

        // No Owner: an owned non-modal window minimizing/restoring can
        // cascade to the main window in WPF -- same reasoning as the
        // other popups (MainWindow_Closing closes it explicitly instead).
        var window = new JiraPopoutWindow(_atlassianService, _settings, BuildJiraFilterSnapshot(), _settings.SavedJiraFilters);

        window.Show();
    }

    // Both results sections start collapsed and stay that way until
    // expanded by hand -- a collapsed section's row still needs to shrink
    // to just its header height (Auto) rather than keep its "*" share of
    // the window, otherwise it'd just be a mostly-empty card instead of
    // actually reclaiming the space for whatever is expanded.
    private void
    ResultsExpander_Changed(
        object sender,
        RoutedEventArgs e)
    {
        ConfluenceRow.Height =
            ConfluenceResultsExpander.IsExpanded ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;

        JiraRow.Height =
            JiraResultsExpander.IsExpanded ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;

        StatsRow.Height =
            StatsResultsExpander.IsExpanded ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
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

    private async void
    RefreshButton_Click(
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

            // A blank query box alone means nothing to search on for Jira
            // (unlike Confluence's "recent pages" fallback) -- but a saved
            // filter like "Unassigned > P2" is meaningful on its own, so
            // any structural filter being set counts too, not just text
            // or a selected queue.
            var queueSelected = !string.IsNullOrWhiteSpace(GetComboSelectionValue(JiraQueueCombo));

            var jiraHasCriteria =
                hasQuery
                || queueSelected
                || !string.IsNullOrWhiteSpace(GetComboFilterValue(JiraProjectCombo, JiraProjectSearchBox))
                || !string.IsNullOrWhiteSpace(GetComboFilterValue(JiraReporterCombo, JiraReporterSearchBox))
                || !string.IsNullOrWhiteSpace(GetComboFilterValue(JiraAssigneeCombo, JiraAssigneeSearchBox))
                || !string.IsNullOrWhiteSpace(GetComboFilterValue(JiraPriorityCombo, JiraPrioritySearchBox))
                || !string.IsNullOrWhiteSpace(GetComboFilterValue(JiraStatusCombo, JiraStatusSearchBox));

            var jiraTask =
                jiraHasCriteria
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
            var queueId = GetComboSelectionValue(JiraQueueCombo);

            var reporterFilter = GetJiraFieldFilter(JiraReporterCombo, JiraReporterSearchBox, JiraReporterOperatorCombo);
            var assigneeFilter = GetJiraFieldFilter(JiraAssigneeCombo, JiraAssigneeSearchBox, JiraAssigneeOperatorCombo);
            var priorityFilter = GetJiraFieldFilter(JiraPriorityCombo, JiraPrioritySearchBox, JiraPriorityOperatorCombo);
            var statusFilter = GetJiraFieldFilter(JiraStatusCombo, JiraStatusSearchBox, JiraStatusOperatorCombo);

            if (!string.IsNullOrWhiteSpace(queueId) && !string.IsNullOrWhiteSpace(_selectedJiraServiceDeskId))
            {
                var queueResults = await _atlassianService.GetQueueIssues(_settings, _selectedJiraServiceDeskId, queueId);

                // The queue endpoint itself takes no extra filters, but
                // Jira's own queue view lets you narrow by these same
                // fields, so it's done client-side over the fetched page
                // instead (this only sees whatever GetQueueIssues already
                // fetched, not the queue's whole result set).
                IEnumerable<JiraSearchResult> filtered = queueResults;

                if (!string.IsNullOrWhiteSpace(query))
                {
                    filtered = filtered.Where(r => r.Summary.Contains(query, StringComparison.OrdinalIgnoreCase));
                }

                filtered = ApplyClientFieldFilter(filtered, r => r.Reporter, reporterFilter);
                filtered = ApplyClientFieldFilter(filtered, r => r.Assignee, assigneeFilter);
                filtered = ApplyClientFieldFilter(filtered, r => r.Status, statusFilter);
                filtered = ApplyClientPriorityFilter(filtered, priorityFilter);

                return (filtered.ToList(), null);
            }

            var results =
                await _atlassianService.SearchJira(
                    _settings,
                    query,
                    GetJiraFieldFilter(JiraProjectCombo, JiraProjectSearchBox, JiraProjectOperatorCombo),
                    reporterFilter,
                    assigneeFilter,
                    priorityFilter,
                    statusFilter);

            return (results, null);
        }
        catch (Exception ex)
        {
            Logger.Error("AtlassianSearchView: Jira search failed.", ex);

            return (new List<JiraSearchResult>(), $"Jira: {ex.Message}");
        }
    }

    // "in"/"not in" split the typed value on commas the same way the
    // service layer does for JQL; "!=" and "not in" both mean "exclude a
    // match", everything else means "require one".
    private static IEnumerable<JiraSearchResult>
    ApplyClientFieldFilter(
        IEnumerable<JiraSearchResult> results,
        Func<JiraSearchResult, string> selector,
        JiraFieldFilter filter)
    {
        if (string.IsNullOrWhiteSpace(filter.Value))
        {
            return results;
        }

        var values =
            filter.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        bool IsMatch(JiraSearchResult r) =>
            values.Any(v => string.Equals(selector(r), v, StringComparison.OrdinalIgnoreCase));

        return filter.Operator is "!=" or "not in"
            ? results.Where(r => !IsMatch(r))
            : results.Where(IsMatch);
    }

    // Same idea as ApplyClientFieldFilter, but ">"/">="/"<"/"<=" need the
    // site's actual priority rank order to mean anything (JQL search
    // doesn't need this -- Jira resolves that comparison server-side).
    private IEnumerable<JiraSearchResult>
    ApplyClientPriorityFilter(
        IEnumerable<JiraSearchResult> results,
        JiraFieldFilter filter)
    {
        if (string.IsNullOrWhiteSpace(filter.Value))
        {
            return results;
        }

        if (filter.Operator is not (">" or ">=" or "<" or "<="))
        {
            return ApplyClientFieldFilter(results, r => r.Priority, filter);
        }

        var targetIndex =
            _jiraPriorityRankOrder.FindIndex(p => string.Equals(p, filter.Value, StringComparison.OrdinalIgnoreCase));

        if (targetIndex < 0)
        {
            return results;
        }

        bool Matches(JiraSearchResult r)
        {
            var index =
                _jiraPriorityRankOrder.FindIndex(p => string.Equals(p, r.Priority, StringComparison.OrdinalIgnoreCase));

            if (index < 0)
            {
                return false;
            }

            // Index 0 is the site's most urgent priority, so a *lower*
            // index means *higher* urgency -- ">" (more urgent than)
            // means a smaller index than the target's.
            return filter.Operator switch
            {
                ">" => index < targetIndex,
                ">=" => index <= targetIndex,
                "<" => index > targetIndex,
                "<=" => index >= targetIndex,
                _ => false
            };
        }

        return results.Where(Matches);
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
            OpenJiraResult(result);
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

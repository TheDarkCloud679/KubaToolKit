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
    // several values at once via MultiSelectPickerWindow -- edited through
    // StatsFilterWindow now that they no longer live inline on the tab
    // itself. Each one's "in"/"not in" operator is tracked separately here
    // rather than via a ComboBox, since that ComboBox now only exists
    // inside the (not always open) filter popup.
    private readonly MultiSelectFilterState _statsProjectFilter = new();
    private string _statsProjectOperator = "in";
    private readonly MultiSelectFilterState _statsAssigneeFilter = new();
    private string _statsAssigneeOperator = "in";
    private readonly MultiSelectFilterState _statsStatusFilter = new();
    private string _statsStatusOperator = "in";
    private readonly MultiSelectFilterState _statsModuleFilter = new();
    private string _statsModuleOperator = "in";
    private readonly MultiSelectFilterState _statsEscalationFilter = new();
    private string _statsEscalationOperator = "in";
    private readonly MultiSelectFilterState _statsRequestTypeFilter = new();
    private string _statsRequestTypeOperator = "in";
    private DateTime? _statsFrom;
    private DateTime? _statsTo;

    private List<IncidentEntry> _incidents = new();
    private IncidentEntry? _selectedIncident;

    private string _linkFilterProject = "";
    private string _linkFilterSpace = "";
    private string _linkFilterStatus = "";
    private DateTime? _linkFilterFrom;
    private DateTime? _linkFilterTo;
    private bool _linkShowJira = true;
    private bool _linkShowConfluence = true;

    // null = the links' own storage order; set once either sort arrow is
    // clicked.
    private bool? _linkSortDateAscending;

    private WikiView? _wikiView;
    private ProjectInfoView? _projectInfoView;

    // Search tab -- free Jira/Confluence keyword search, not tied to any
    // incident. Mirrors AttachLinkWindow's filter state, but that window is
    // scoped to one incident's Links, whereas this is just for browsing.
    private List<AtlassianResultItem> _searchRawResults = new();
    private CancellationTokenSource? _searchCancellation;

    private string _searchFilterProject = "";
    private string _searchFilterStatus = "";
    private string _searchFilterSpaceKey = "";
    private string _searchFilterSpaceDisplay = "";
    private DateTime? _searchFilterFrom;
    private DateTime? _searchFilterTo;
    private bool _searchShowJira = true;
    private bool _searchShowConfluence = true;
    private bool? _searchSortDateAscending;

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
            _ = LoadSearchFilterOptionsAsync();
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
        if (StatsTabContent == null || WikiTabContent == null || ProjectInfoTabContent == null || SearchTabContent == null)
        {
            return;
        }

        // Flush whichever tab is being left, so a debounced save isn't lost
        // to a tab switch happening before its 800ms timer fires.
        _wikiView?.FlushPendingSave();
        _projectInfoView?.FlushPendingSave();

        LibraryTabContent.Visibility =
            LibraryTabRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        SearchTabContent.Visibility =
            SearchTabRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

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
        public Brush RowBorderBrush { get; set; } = Brushes.Transparent;
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
        public string DisplayDate { get; set; } = "";
        public DateTime? SortDate { get; set; }
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
                        RowBackground = isSelected ? (Brush)FindResource("AccentSoftBrush") : (Brush)FindResource("SurfaceAltBrush"),
                        RowBorderBrush = isSelected ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("BorderBrush"),
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
    ExportLibraryButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog =
            new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json",
                FileName = $"Atlassian incidents {DateTime.Now:yyyy-MM-dd}.json"
            };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            _incidentStorage.ExportLibrary(dialog.FileName);

            AppMessageBox.Show(
                $"Exported {_incidents.Count} incident(s) to \"{dialog.FileName}\".",
                "Export library");
        }
        catch (Exception ex)
        {
            Logger.Error("AtlassianSearchView: failed to export the incident library.", ex);

            AppMessageBox.Show(ex.Message, "Export error");
        }
    }

    private void
    ImportLibraryButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "JSON files (*.json)|*.json" };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var importedCount = _incidentStorage.ImportLibrary(dialog.FileName);

            LoadIncidents();

            AppMessageBox.Show($"Imported {importedCount} incident(s).", "Import library");
        }
        catch (Exception ex)
        {
            Logger.Error("AtlassianSearchView: failed to import an incident library file.", ex);

            AppMessageBox.Show(ex.Message, "Import error");
        }
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
        FlushIncidentDetailEdits();

        _selectedIncident = entry;

        RefreshIncidentList();
        UpdateIncidentDetailPanel();
    }

    // Incident rows are plain Borders (not Focusable), so clicking one to
    // switch incidents never fires LostFocus on whichever detail TextBox is
    // currently focused -- without this, an in-progress edit (e.g. the
    // Solution field) is silently overwritten by UpdateIncidentDetailPanel
    // before it ever gets saved.
    private void
    FlushIncidentDetailEdits()
    {
        if (_selectedIncident == null)
        {
            return;
        }

        var name = IncidentNameTextBox.Text.Trim();
        var description = IncidentDescriptionTextBox.Text;
        var solution = IncidentSolutionTextBox.Text;

        var changed = false;

        if (!string.IsNullOrWhiteSpace(name) && name != _selectedIncident.Name)
        {
            _selectedIncident.Name = name;
            changed = true;
        }

        if (description != _selectedIncident.Description)
        {
            _selectedIncident.Description = description;
            changed = true;
        }

        if (solution != _selectedIncident.Solution)
        {
            _selectedIncident.Solution = solution;
            changed = true;
        }

        if (changed)
        {
            SaveSelectedIncident();
        }
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
        IncidentSolutionTextBox.Text = _selectedIncident.Solution;

        LinksSearchBox.Text = "";

        _linkFilterProject = "";
        _linkFilterSpace = "";
        _linkFilterStatus = "";
        _linkFilterFrom = null;
        _linkFilterTo = null;
        _linkSortDateAscending = null;

        PopulateLinkFilterCombos();
        RefreshLinksList();
    }

    private void
    PopulateLinkFilterCombos()
    {
        if (_selectedIncident == null)
        {
            return;
        }

        var projects =
            _selectedIncident.Links
                .Where(l => l.Type == IncidentLinkType.Jira && !string.IsNullOrWhiteSpace(l.Project))
                .Select(l => l.Project)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .Select(s => new NameValue(s, s))
                .Prepend(AnyOption)
                .ToList();

        var spaces =
            _selectedIncident.Links
                .Where(l => l.Type == IncidentLinkType.Confluence && !string.IsNullOrWhiteSpace(l.Space))
                .Select(l => l.Space)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .Select(s => new NameValue(s, s))
                .Prepend(AnyOption)
                .ToList();

        var statuses =
            _selectedIncident.Links
                .Where(l => l.Type == IncidentLinkType.Jira && !string.IsNullOrWhiteSpace(l.Status))
                .Select(l => l.Status)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .Select(s => new NameValue(s, s))
                .Prepend(AnyOption)
                .ToList();

        LinkFilterProjectCombo.ItemsSource = projects;
        LinkFilterProjectCombo.SelectedIndex = 0;

        LinkFilterSpaceCombo.ItemsSource = spaces;
        LinkFilterSpaceCombo.SelectedIndex = 0;

        LinkFilterStatusCombo.ItemsSource = statuses;
        LinkFilterStatusCombo.SelectedIndex = 0;

        LinkFilterFromDatePicker.SelectedDate = null;
        LinkFilterToDatePicker.SelectedDate = null;
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
    IncidentSolutionTextBox_LostFocus(
        object sender,
        RoutedEventArgs e)
    {
        if (_selectedIncident == null)
        {
            return;
        }

        var solution = IncidentSolutionTextBox.Text;

        if (solution == _selectedIncident.Solution)
        {
            return;
        }

        _selectedIncident.Solution = solution;

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

        IEnumerable<IncidentLink> links = _selectedIncident.Links;

        links =
            links.Where(l => string.IsNullOrEmpty(query)
                || l.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                || l.Key.Contains(query, StringComparison.OrdinalIgnoreCase));

        links =
            links.Where(l =>
                (l.Type == IncidentLinkType.Jira && _linkShowJira)
                || (l.Type == IncidentLinkType.Confluence && _linkShowConfluence));

        if (!string.IsNullOrEmpty(_linkFilterProject))
        {
            links = links.Where(l => string.Equals(l.Project, _linkFilterProject, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(_linkFilterSpace))
        {
            links = links.Where(l => string.Equals(l.Space, _linkFilterSpace, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(_linkFilterStatus))
        {
            links = links.Where(l => string.Equals(l.Status, _linkFilterStatus, StringComparison.OrdinalIgnoreCase));
        }

        if (_linkFilterFrom.HasValue)
        {
            links =
                links.Where(l =>
                    DateTime.TryParse(l.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                    && d.Date >= _linkFilterFrom.Value.Date);
        }

        if (_linkFilterTo.HasValue)
        {
            links =
                links.Where(l =>
                    DateTime.TryParse(l.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                    && d.Date <= _linkFilterTo.Value.Date);
        }

        var rows =
            links
                .Select(l =>
                {
                    DateTime.TryParse(l.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate);

                    return new LinkRow
                    {
                        Link = l,
                        IsJira = l.Type == IncidentLinkType.Jira,
                        IsConfluence = l.Type == IncidentLinkType.Confluence,
                        Key = l.Key,
                        Title = l.Title,
                        Subtitle = l.Type == IncidentLinkType.Jira ? "Jira ticket" : $"Confluence page · {l.Space}",
                        Priority = l.Priority,
                        Status = l.Status,
                        SortDate = parsedDate == default ? null : parsedDate,
                        DisplayDate = parsedDate == default ? "" : parsedDate.ToString("yyyy-MM-dd")
                    };
                })
                .ToList();

        if (_linkSortDateAscending.HasValue)
        {
            rows =
                (_linkSortDateAscending.Value
                    ? rows.OrderBy(r => r.SortDate ?? DateTime.MinValue)
                    : rows.OrderByDescending(r => r.SortDate ?? DateTime.MinValue))
                    .ToList();
        }

        LinksItemsControl.ItemsSource = rows;

        var isFiltered =
            !string.IsNullOrEmpty(query)
            || !string.IsNullOrEmpty(_linkFilterSpace)
            || !string.IsNullOrEmpty(_linkFilterStatus)
            || _linkFilterFrom.HasValue
            || _linkFilterTo.HasValue;

        if (_selectedIncident.Links.Count == 0)
        {
            NoLinksText.Text = "No ticket or page linked yet. Click \"Link an item\" to search for one.";
            NoLinksText.Visibility = Visibility.Visible;
        }
        else if (rows.Count == 0)
        {
            NoLinksText.Text = isFiltered ? "No link matches the current search/filters." : "";
            NoLinksText.Visibility = Visibility.Visible;
        }
        else
        {
            NoLinksText.Visibility = Visibility.Collapsed;
        }
    }

    private void
    LinkFilterProjectCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        _linkFilterProject = (string)LinkFilterProjectCombo.SelectedValue;

        RefreshLinksList();
    }

    private void
    LinkFilterSpaceCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        _linkFilterSpace = (string)LinkFilterSpaceCombo.SelectedValue;

        RefreshLinksList();
    }

    private void
    LinkTypeFilterCheckBox_Changed(
        object sender,
        RoutedEventArgs e)
    {
        // Both checkboxes default to IsChecked="True" in XAML, which fires
        // Checked during InitializeComponent itself -- for the first one
        // parsed, that's before the second one's x:Name field, or anything
        // declared later in the same XAML (LinksItemsControl included, via
        // RefreshLinksList below), is assigned yet.
        if (LinkShowJiraCheckBox == null || LinkShowConfluenceCheckBox == null || LinksItemsControl == null)
        {
            return;
        }

        _linkShowJira = LinkShowJiraCheckBox.IsChecked == true;
        _linkShowConfluence = LinkShowConfluenceCheckBox.IsChecked == true;

        RefreshLinksList();
    }

    private void
    LinkFilterStatusCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        _linkFilterStatus = (string)LinkFilterStatusCombo.SelectedValue;

        RefreshLinksList();
    }

    private void
    LinkFilterDatePicker_SelectedDateChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        _linkFilterFrom = LinkFilterFromDatePicker.SelectedDate;
        _linkFilterTo = LinkFilterToDatePicker.SelectedDate;

        RefreshLinksList();
    }

    private void
    LinkSortDateAscendingButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _linkSortDateAscending = true;

        RefreshLinksList();
    }

    private void
    LinkSortDateDescendingButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _linkSortDateAscending = false;

        RefreshLinksList();
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
                    // A newly-attached item's project/space/status won't be
                    // in the filter dropdowns until they're rebuilt from
                    // the incident's current links -- otherwise it stays
                    // invisible to those filters until the incident is
                    // reselected.
                    PopulateLinkFilterCombos();
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

        if (AppMessageBox.Show(
                $"Unlink \"{row.Title}\" from this incident?",
                "Unlink item",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning)
            != MessageBoxResult.Yes)
        {
            return;
        }

        _selectedIncident.Links.Remove(row.Link);

        SaveSelectedIncident();
        PopulateLinkFilterCombos();
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
    // Search tab -- free Jira/Confluence search, not tied to any incident.
    // ===================================================================

    // Populates Project/Status/Space from the site's own full lists, the
    // same approach AttachLinkWindow uses, so they work as real query
    // parameters before a first search has even run.
    private async Task
    LoadSearchFilterOptionsAsync()
    {
        try
        {
            var projectsTask = _atlassianService.GetJiraProjects(_settings);
            var statusesTask = _atlassianService.GetJiraStatuses(_settings);
            var spacesTask = _atlassianService.GetConfluenceSpaces(_settings);

            await Task.WhenAll(projectsTask, statusesTask, spacesTask);

            SearchFilterProjectCombo.ItemsSource = new List<NameValue> { AnyOption }.Concat(projectsTask.Result).ToList();
            SearchFilterProjectCombo.SelectedIndex = 0;

            SearchFilterStatusCombo.ItemsSource = new List<NameValue> { AnyOption }.Concat(statusesTask.Result).ToList();
            SearchFilterStatusCombo.SelectedIndex = 0;

            SearchFilterSpaceCombo.ItemsSource = new List<NameValue> { AnyOption }.Concat(spacesTask.Result).ToList();
            SearchFilterSpaceCombo.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            Logger.Error("AtlassianSearchView: failed to load Search tab filter options.", ex);
        }
    }

    private void
    SearchQueryTextBox_KeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _ = RunFreeSearchAsync();
        }
    }

    private async void
    SearchTabSearchButton_Click(
        object sender,
        RoutedEventArgs e) =>
        await RunFreeSearchAsync();

    private async Task
    RunFreeSearchAsync()
    {
        var query = SearchQueryTextBox.Text.Trim();

        var hasAnyFilter =
            !string.IsNullOrEmpty(_searchFilterProject)
            || !string.IsNullOrEmpty(_searchFilterStatus)
            || !string.IsNullOrEmpty(_searchFilterSpaceKey);

        // Mirrors SearchJira/SearchConfluence's own contract: each builds
        // a valid query from filters alone, but not from nothing at all.
        if (string.IsNullOrWhiteSpace(query) && !hasAnyFilter)
        {
            return;
        }

        if (!_searchShowJira && !_searchShowConfluence)
        {
            return;
        }

        _searchCancellation?.Cancel();
        _searchCancellation = new CancellationTokenSource();

        var cancellationToken = _searchCancellation.Token;

        SearchProgressBar.Visibility = Visibility.Visible;
        SearchTabSearchButton.IsEnabled = false;
        SearchEmptyStateText.Visibility = Visibility.Collapsed;

        try
        {
            var projectFilter =
                string.IsNullOrEmpty(_searchFilterProject) ? JiraFieldFilter.Empty : new JiraFieldFilter(_searchFilterProject, "=");

            var statusFilter =
                string.IsNullOrEmpty(_searchFilterStatus) ? JiraFieldFilter.Empty : new JiraFieldFilter(_searchFilterStatus, "=");

            var spaceKeys =
                string.IsNullOrEmpty(_searchFilterSpaceKey) ? Array.Empty<string>() : new[] { _searchFilterSpaceKey };

            var jiraTask =
                _searchShowJira
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
                _searchShowConfluence
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

            _searchRawResults =
                jiraTask.Result
                    .Select(r => BuildSearchResultItem(new AtlassianResultItem
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
                        confluenceTask.Result.Select(r => BuildSearchResultItem(new AtlassianResultItem
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

            RefreshFreeSearchResultsDisplay();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.Error("AtlassianSearchView: free search failed.", ex);

            AppMessageBox.Show(ex.Message, "Search error");
        }
        finally
        {
            SearchProgressBar.Visibility = Visibility.Collapsed;
            SearchTabSearchButton.IsEnabled = true;
        }
    }

    // Parses DateRaw once right after a result is built, so filtering and
    // sorting never have to re-parse it on every keystroke/toggle.
    private static AtlassianResultItem
    BuildSearchResultItem(
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
    RefreshFreeSearchResultsDisplay()
    {
        IEnumerable<AtlassianResultItem> filtered =
            _searchRawResults.Where(r => (r.IsJira && _searchShowJira) || (r.IsConfluence && _searchShowConfluence));

        if (!string.IsNullOrEmpty(_searchFilterProject))
        {
            filtered = filtered.Where(r => string.Equals(r.Project, _searchFilterProject, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(_searchFilterSpaceDisplay))
        {
            filtered = filtered.Where(r => string.Equals(r.Space, _searchFilterSpaceDisplay, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(_searchFilterStatus))
        {
            filtered = filtered.Where(r => string.Equals(r.Status, _searchFilterStatus, StringComparison.OrdinalIgnoreCase));
        }

        if (_searchFilterFrom.HasValue)
        {
            filtered = filtered.Where(r => r.SortDate.HasValue && r.SortDate.Value.Date >= _searchFilterFrom.Value.Date);
        }

        if (_searchFilterTo.HasValue)
        {
            filtered = filtered.Where(r => r.SortDate.HasValue && r.SortDate.Value.Date <= _searchFilterTo.Value.Date);
        }

        if (_searchSortDateAscending.HasValue)
        {
            filtered =
                _searchSortDateAscending.Value
                    ? filtered.OrderBy(r => r.SortDate ?? DateTime.MinValue)
                    : filtered.OrderByDescending(r => r.SortDate ?? DateTime.MinValue);
        }

        var displayed = filtered.ToList();

        SearchResultsItemsControl.ItemsSource = null;
        SearchResultsItemsControl.ItemsSource = displayed;

        SearchEmptyStateText.Visibility = displayed.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        SearchEmptyStateText.Text =
            _searchRawResults.Count == 0
                ? "No results for this search."
                : "No results match the current filters.";
    }

    private void
    SearchFilterProjectCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        _searchFilterProject = (string)SearchFilterProjectCombo.SelectedValue;

        RefreshFreeSearchResultsDisplay();
    }

    private void
    SearchFilterSpaceCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (SearchFilterSpaceCombo.SelectedItem is NameValue selected)
        {
            // AnyOption's own Display ("(Any)") would otherwise read as a
            // real space name to filter by, once selected.Value is empty.
            _searchFilterSpaceKey = selected.Value;
            _searchFilterSpaceDisplay = string.IsNullOrEmpty(selected.Value) ? "" : selected.Display;
        }

        RefreshFreeSearchResultsDisplay();
    }

    private void
    SearchFilterStatusCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        _searchFilterStatus = (string)SearchFilterStatusCombo.SelectedValue;

        RefreshFreeSearchResultsDisplay();
    }

    private void
    SearchFilterDatePicker_SelectedDateChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        _searchFilterFrom = SearchFilterFromDatePicker.SelectedDate;
        _searchFilterTo = SearchFilterToDatePicker.SelectedDate;

        RefreshFreeSearchResultsDisplay();
    }

    private void
    SearchTypeFilterCheckBox_Changed(
        object sender,
        RoutedEventArgs e)
    {
        // Both checkboxes default to IsChecked="True" in XAML, which fires
        // Checked during InitializeComponent itself -- for the first one
        // parsed, that's before the second one's x:Name field, or anything
        // declared later in the same XAML (SearchResultsItemsControl
        // included, via RefreshFreeSearchResultsDisplay below), is assigned
        // yet.
        if (SearchShowJiraCheckBox == null || SearchShowConfluenceCheckBox == null || SearchResultsItemsControl == null)
        {
            return;
        }

        _searchShowJira = SearchShowJiraCheckBox.IsChecked == true;
        _searchShowConfluence = SearchShowConfluenceCheckBox.IsChecked == true;

        RefreshFreeSearchResultsDisplay();
    }

    private void
    SearchSortDateAscendingButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _searchSortDateAscending = true;

        RefreshFreeSearchResultsDisplay();
    }

    private void
    SearchSortDateDescendingButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _searchSortDateAscending = false;

        RefreshFreeSearchResultsDisplay();
    }

    private void
    SearchResultRow_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || sender is not FrameworkElement { Tag: AtlassianResultItem item })
        {
            return;
        }

        OpenSearchResult(item);
    }

    private void
    SearchResultOpenButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is Button { Tag: AtlassianResultItem item })
        {
            OpenSearchResult(item);
        }
    }

    private void
    OpenSearchResult(
        AtlassianResultItem item)
    {
        if (item.IsJira)
        {
            OpenJiraResult(
                new JiraSearchResult
                {
                    Key = item.Key,
                    Project = item.Project,
                    Summary = item.Title,
                    Priority = item.Priority,
                    Status = item.Status,
                    Url = item.Url
                });
        }
        else
        {
            OpenConfluenceResult(
                new ConfluenceSearchResult
                {
                    Id = item.PageId,
                    Title = item.Title,
                    Space = item.Space,
                    Url = item.Url
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
        var requestTypeOptionsTask = _atlassianService.GetJiraFieldOptions(_settings, "Request Type");

        await Task.WhenAll(
            projectsTask, statusesTask, statusCategoriesTask, serviceDesksTask, usersTask,
            moduleOptionsTask, escalationOptionsTask, requestTypeOptionsTask);

        JiraStatusColors.CategoryByStatus = statusCategoriesTask.Result;
        _jiraServiceDesksByProjectKey = serviceDesksTask.Result;

        _statsProjectFilter.AllOptions = projectsTask.Result;
        _statsAssigneeFilter.AllOptions = usersTask.Result;
        _statsStatusFilter.AllOptions = statusesTask.Result;
        _statsModuleFilter.AllOptions = moduleOptionsTask.Result;
        _statsEscalationFilter.AllOptions = escalationOptionsTask.Result;
        _statsRequestTypeFilter.AllOptions = requestTypeOptionsTask.Result;
    }

    // Opens the popup holding every Statistics filter (moved out of the
    // tab's own header once it had six filters plus a date range -- too
    // crowded to stay inline). Picker selections made inside it apply
    // straight to these MultiSelectFilterState instances (shared by
    // reference), so only the operator/date values need reading back once
    // the popup closes.
    private void
    StatsFiltersButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var window =
            new StatsFilterWindow(
                _statsProjectFilter, _statsProjectOperator,
                _statsAssigneeFilter, _statsAssigneeOperator,
                _statsStatusFilter, _statsStatusOperator,
                _statsModuleFilter, _statsModuleOperator,
                _statsEscalationFilter, _statsEscalationOperator,
                _statsRequestTypeFilter, _statsRequestTypeOperator,
                _statsFrom, _statsTo)
            {
                Owner = Window.GetWindow(this)
            };

        window.ShowDialog();

        _statsProjectOperator = window.ProjectOperator;
        _statsAssigneeOperator = window.AssigneeOperator;
        _statsStatusOperator = window.StatusOperator;
        _statsModuleOperator = window.ModuleOperator;
        _statsEscalationOperator = window.EscalationOperator;
        _statsRequestTypeOperator = window.RequestTypeOperator;
        _statsFrom = window.From;
        _statsTo = window.To;

        UpdateStatsFilterSummary();
    }

    // Counts how many of the filter dimensions are actually narrowed, so
    // the Filters button reads e.g. "Filters (2)" instead of forcing the
    // popup open just to check what's set.
    private void
    UpdateStatsFilterSummary()
    {
        var activeCount =
            new[]
            {
                _statsProjectFilter.SelectedValues.Count > 0,
                _statsAssigneeFilter.SelectedValues.Count > 0,
                _statsStatusFilter.SelectedValues.Count > 0,
                _statsModuleFilter.SelectedValues.Count > 0,
                _statsEscalationFilter.SelectedValues.Count > 0,
                _statsRequestTypeFilter.SelectedValues.Count > 0,
                _statsFrom.HasValue,
                _statsTo.HasValue
            }.Count(active => active);

        StatsFiltersButton.Content = activeCount == 0 ? "Filters" : $"Filters ({activeCount})";
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
        _statsProjectFilter.SetFromCommaList(filter.Project);
        _statsProjectOperator = NormalizeMultiSelectOperator(filter.ProjectOperator);

        _statsAssigneeFilter.SetFromCommaList(filter.Assignee);
        _statsAssigneeOperator = NormalizeMultiSelectOperator(filter.AssigneeOperator);

        _statsStatusFilter.SetFromCommaList(filter.Status);
        _statsStatusOperator = NormalizeMultiSelectOperator(filter.StatusOperator);

        _statsModuleFilter.SetFromCommaList(filter.Module);
        _statsModuleOperator = NormalizeMultiSelectOperator(filter.ModuleOperator);

        _statsEscalationFilter.SetFromCommaList(filter.Escalation);
        _statsEscalationOperator = NormalizeMultiSelectOperator(filter.EscalationOperator);

        _statsRequestTypeFilter.SetFromCommaList(filter.RequestType);
        _statsRequestTypeOperator = NormalizeMultiSelectOperator(filter.RequestTypeOperator);

        _statsFrom = filter.From;
        _statsTo = filter.To;

        UpdateStatsFilterSummary();
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
            ProjectOperator = _statsProjectOperator,
            Assignee = _statsAssigneeFilter.JqlValue(),
            AssigneeOperator = _statsAssigneeOperator,
            Status = _statsStatusFilter.JqlValue(),
            StatusOperator = _statsStatusOperator,
            Module = _statsModuleFilter.JqlValue(),
            ModuleOperator = _statsModuleOperator,
            Escalation = _statsEscalationFilter.JqlValue(),
            EscalationOperator = _statsEscalationOperator,
            RequestType = _statsRequestTypeFilter.JqlValue(),
            RequestTypeOperator = _statsRequestTypeOperator,
            From = _statsFrom,
            To = _statsTo
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

            var project = new JiraFieldFilter(_statsProjectFilter.JqlValue(), _statsProjectOperator);
            var assignee = new JiraFieldFilter(_statsAssigneeFilter.JqlValue(), _statsAssigneeOperator);
            var status = new JiraFieldFilter(_statsStatusFilter.JqlValue(), _statsStatusOperator);
            var module = new JiraFieldFilter(_statsModuleFilter.JqlValue(), _statsModuleOperator);
            var escalation = new JiraFieldFilter(_statsEscalationFilter.JqlValue(), _statsEscalationOperator);
            var requestType = new JiraFieldFilter(_statsRequestTypeFilter.JqlValue(), _statsRequestTypeOperator);

            var results =
                await _atlassianService.SearchJiraStats(
                    _settings,
                    project,
                    assignee,
                    status,
                    module,
                    escalation,
                    requestType,
                    _statsFrom,
                    _statsTo,
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
        _ = LoadSearchFilterOptionsAsync();
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

        WindowActivation.ShowActivated(window);
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

        WindowActivation.ShowActivated(window);
    }
}

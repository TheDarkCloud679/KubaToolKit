using KubaToolKit.Modules.ApiClient;
using KubaToolKit.Modules.AtlassianSearch.Models;
using KubaToolKit.Modules.ProjectInfo;
using KubaToolKit.Modules.Wiki;
using KubaToolKit.Shared.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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

    // Queues are a Jira Service Management concept scoped to one service
    // desk project -- resolved once here so OpenJiraResult can tell whether
    // a given issue's project is a service desk (isServiceDeskIssue).
    private Dictionary<string, string> _jiraServiceDesksByProjectKey = new(StringComparer.OrdinalIgnoreCase);

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

    // Project Info stays scoped per AWS profile (unlike the Wiki, which
    // went generic) -- kept in sync with the main nav's own Profile combo
    // (MainWindow shows that combo for Atlassian mode same as everywhere
    // else, see MainWindow.UpdateProfileRowVisibility) rather than keeping
    // a second, separately-selected copy of it in here.
    private string _projectInfoProfile = "";

    public AtlassianSearchView()
    {
        InitializeComponent();

        _settings = _settingsService.Load();

        UpdateStatusForMissingSettings();

        if (_settings.IsComplete)
        {
            _ = LoadJiraLookupsAsync();
        }

        LoadIncidents();
    }

    // Called by MainWindow whenever the main nav's Profile combo changes
    // (or, on startup/tab switch, to push its current value) so Project
    // Info always reflects whatever profile is already selected elsewhere
    // in the app, with no separate selection of its own to fall out of
    // sync.
    public void
    SetProjectInfoProfile(
        string? profile)
    {
        _projectInfoProfile = profile ?? "";

        _projectInfoView?.ChangeProfile(_projectInfoProfile);
    }

    private static readonly NameValue AnyOption = new("", "(Any)");

    // LibraryTabRadio has IsChecked="True" in XAML so Library opens by
    // default -- that fires this Checked handler once during
    // InitializeComponent, before WikiTabContent -- declared later in the
    // same XAML -- is wired up yet. The null guard skips that first,
    // premature call; the real initial state comes from the panels' own
    // XAML-default Visibility instead (same fix used throughout the app,
    // e.g. API Client's request-config tab strip).
    private void
    AtlassianTab_Changed(
        object sender,
        RoutedEventArgs e)
    {
        if (WikiTabContent == null || ProjectInfoTabContent == null)
        {
            return;
        }

        // Flush whichever tab is being left, so a debounced save isn't lost
        // to a tab switch happening before its 800ms timer fires.
        _wikiView?.FlushPendingSave();
        _projectInfoView?.FlushPendingSave();

        LibraryTabContent.Visibility =
            LibraryTabRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        WikiTabContent.Visibility =
            WikiTabRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        ProjectInfoTabContent.Visibility =
            ProjectInfoTabRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        if (WikiTabRadio.IsChecked == true && _wikiView == null)
        {
            _wikiView = new WikiView();
            WikiTabContent.Content = _wikiView;
        }

        if (ProjectInfoTabRadio.IsChecked == true && _projectInfoView == null)
        {
            _projectInfoView = new ProjectInfoView(_projectInfoProfile);
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

        // Only set on the selected card, and only when the current search
        // matched somewhere other than the incident's own Name (which is
        // already shown as the card's title) -- a short excerpt around the
        // match, split in three so the middle Run can be styled as a
        // highlight without a converter.
        public Visibility SnippetVisibility { get; set; } = Visibility.Collapsed;
        public string SnippetBefore { get; set; } = "";
        public string SnippetMatch { get; set; } = "";
        public string SnippetAfter { get; set; } = "";
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
                .Where(i => string.IsNullOrEmpty(query) || IncidentMatchesQuery(i, query))
                .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .Select(i =>
                {
                    var isSelected = ReferenceEquals(i, _selectedIncident);

                    var snippet =
                        isSelected && !string.IsNullOrEmpty(query)
                            ? BuildIncidentSnippet(i, query)
                            : null;

                    return new IncidentListRow
                    {
                        Entry = i,
                        Name = i.Name,
                        CountLabel = i.Links.Count == 1 ? "1 link" : $"{i.Links.Count} links",
                        RowBackground = isSelected ? (Brush)FindResource("AccentSoftBrush") : (Brush)FindResource("SurfaceAltBrush"),
                        RowBorderBrush = isSelected ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("BorderBrush"),
                        CountForeground = isSelected ? (Brush)FindResource("AccentPressedBrush") : (Brush)FindResource("TextMutedBrush"),
                        SnippetVisibility = snippet != null ? Visibility.Visible : Visibility.Collapsed,
                        SnippetBefore = snippet?.Before ?? "",
                        SnippetMatch = snippet?.Match ?? "",
                        SnippetAfter = snippet?.After ?? ""
                    };
                })
                .ToList();

        IncidentListItemsControl.ItemsSource = rows;
    }

    // Beyond the incident's own name, also matches its description/
    // solution text and its linked Jira/Confluence items (key or title) --
    // an incident is often found by a symptom described in the solution,
    // or by the ticket key someone already has in hand, not just its name.
    private static bool
    IncidentMatchesQuery(
        IncidentEntry incident,
        string query) =>
        incident.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
        || incident.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
        || incident.Solution.Contains(query, StringComparison.OrdinalIgnoreCase)
        || incident.Links.Any(l =>
            l.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
            || l.Key.Contains(query, StringComparison.OrdinalIgnoreCase));

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

    // Context window kept small -- this is a one-line preview on the card
    // itself, not a full reading pane.
    private const int SnippetContextChars = 40;

    // Only called for the selected card (see RefreshIncidentList) -- shows
    // where a content search actually matched, right on the card, instead
    // of requiring a trip into the description/solution editor to find it.
    // Name matches aren't snippet-ed: the name is already the card's own
    // title, right above.
    private static (string Before, string Match, string After)?
    BuildIncidentSnippet(
        IncidentEntry entry,
        string query) =>
        FindSnippet(entry.Description, query)
        ?? FindSnippet(entry.Solution, query)
        ?? BuildLinkSnippet(entry, query);

    private static (string Before, string Match, string After)?
    BuildLinkSnippet(
        IncidentEntry entry,
        string query)
    {
        var link =
            entry.Links.FirstOrDefault(l =>
                l.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                || l.Key.Contains(query, StringComparison.OrdinalIgnoreCase));

        if (link == null)
        {
            return null;
        }

        var text =
            !string.IsNullOrEmpty(link.Key) && !string.IsNullOrEmpty(link.Title)
                ? $"{link.Key} — {link.Title}"
                : (string.IsNullOrEmpty(link.Key) ? link.Title : link.Key);

        return FindSnippet(text, query) ?? ("", text, "");
    }

    private static (string Before, string Match, string After)?
    FindSnippet(
        string text,
        string query)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var index = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);

        if (index < 0)
        {
            return null;
        }

        var start = Math.Max(0, index - SnippetContextChars);
        var end = Math.Min(text.Length, index + query.Length + SnippetContextChars);

        var before = (start > 0 ? "…" : "") + text[start..index].TrimStart();
        var match = text.Substring(index, query.Length);
        var after = text[(index + query.Length)..end].TrimEnd() + (end < text.Length ? "…" : "");

        return (before, match, after);
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
    // Shared: settings, popout, opening a result
    // ===================================================================

    // The two Jira lookups the Library's link badges/detail viewer still
    // need on their own -- status category drives each status badge's
    // color (JiraStatusColors), service desk membership decides whether
    // OpenJiraResult opens an issue as a service-desk ticket.
    private async Task
    LoadJiraLookupsAsync()
    {
        var statusCategoriesTask = _atlassianService.GetJiraStatusCategories(_settings);
        var serviceDesksTask = _atlassianService.GetJiraServiceDesksByProjectKey(_settings);

        await Task.WhenAll(statusCategoriesTask, serviceDesksTask);

        JiraStatusColors.CategoryByStatus = statusCategoriesTask.Result;
        _jiraServiceDesksByProjectKey = serviceDesksTask.Result;
    }

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

        _ = LoadJiraLookupsAsync();
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

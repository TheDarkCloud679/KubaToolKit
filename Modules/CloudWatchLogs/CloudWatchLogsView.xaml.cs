using KubaToolKit.Modules.CloudWatchLogs.Models;
using KubaToolKit.Shared.Services;
using KubaToolKit.Shared.Windows;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace KubaToolKit.Modules.CloudWatchLogs;

public partial class CloudWatchLogsView
    : UserControl
{
    private readonly CloudWatchService _cloudWatchService = new();
    private ObservableCollection<LogGroupNode> _logGroupTree = new();
    private List<string> _allLogGroups = new();
    private List<LogGroupCategory> _logGroupCategories = new();
    private CancellationTokenSource? _searchCancellation;
    private string? _currentProfile;
    private string _currentSearchText = "";

    private const double QueryEditorBaseHeight = 120;
    private const double QueryEditorLineHeight = 20;
    private const double QueryEditorMaxHeight = 420;
    private const double LogGroupsBaseHeight = 300;
    private const double LogGroupsMinHeight = 100;
    private double _queryEditorCurrentHeight = QueryEditorBaseHeight;

    public CloudWatchLogsView()
    {
        InitializeComponent();
        LoadLogGroupCategories();
    }

    /// Reads the Shell-owned date/time range controls at the moment a custom query is run.
    public Func<(DateTime? StartDate, string StartTime, DateTime? EndDate, string EndTime)>? GetDateRange { get; set; }

    public bool IsSearchRunning => _searchCancellation != null;

    public void CancelSearch()
    {
        _searchCancellation?.Cancel();
    }

    private void
    LoadLogGroupCategories()
    {
        try
        {
            var filePath =
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "loggroup-categories.json");

            if (!File.Exists(
                    filePath))
            {
                return;
            }

            var json =
                File.ReadAllText(
                    filePath);

            _logGroupCategories =
                JsonSerializer
                    .Deserialize<
                        List<LogGroupCategory>>(
                        json)
                ?? new();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.ToString(),
                "Category loading error");
        }
    }

    public async Task
        LoadLogGroupsAsync(string? profile)
    {
        _currentProfile = profile;

        try
        {
            if (string.IsNullOrWhiteSpace(
                    profile))
            { return; }

            _allLogGroups =
                await _cloudWatchService
                    .GetLogGroups(
                        profile);
            BuildLogGroupTree(_allLogGroups);
            LogGroupsTreeView.ItemsSource = _logGroupTree;
        }
        catch (Exception ex)
        {
            if (AwsSsoService.IsSsoExpired(ex))
            {
                var success =
                    await AwsSsoService.Login();

                if (success)
                {
                    await LoadLogGroupsAsync(profile);
                    return;
                }
            }

            MessageBox.Show(
                ex.ToString(),
                "Erreur chargement log groups");
        }
    }

    private void
SearchAllLogsCheckBox_Changed(
    object sender,
    RoutedEventArgs e)
    {
        bool searchAll =
            SearchAllLogsCheckBox
                ?.IsChecked
            == true;

        LogGroupsTreeView.IsEnabled =
            !searchAll;

        // "Search in all log groups" doit visuellement cocher (ou décocher)
        // tous les dossiers ; chaque nœud propage déjà l'état à ses enfants.
        foreach (var node in _logGroupTree)
        {
            node.IsChecked = searchAll;
        }
    }

    public async Task
    RunSearchAsync(
        string profile,
        string searchText,
        DateTime? startDate,
        string startTime,
        DateTime? endDate,
        string endTime)
    {
        _currentProfile = profile;
        _currentSearchText = searchText;

        try
        {
            SearchProgressBar.Value =
                0;

            ProgressTextBlock.Text =
                "Searching CloudWatch...";

            List<string>
                selectedLogGroups;

            if (SearchAllLogsCheckBox
                ?.IsChecked == true)
            {
                selectedLogGroups =
                    new();
            }
            else
            {
                selectedLogGroups =
                    GetSelectedLogGroups(
                        _logGroupTree);
            }

            var progress =
                new Progress<int>(
                    percent =>
                    {
                        SearchProgressBar.Value =
                            percent;

                        ProgressTextBlock.Text =
                            $"Searching... {percent}%";
                    });

            _searchCancellation =
                new CancellationTokenSource();

            var results =
                await _cloudWatchService
                    .SearchLogs(
                        profile,
                        searchText,
                        startDate,
                        startTime,
                        endDate,
                        endTime,
                        selectedLogGroups,
                        progress,
                        null,
                        _searchCancellation
                            .Token);

            DisplayResults(
                results);
        }
        catch (OperationCanceledException)
        {
            ProgressTextBlock.Text =
                "Search cancelled";

            SearchProgressBar.Value =
                0;
        }
        catch (Exception ex)
        {
            if (AwsSsoService.IsSsoExpired(ex))
            {
                var success =
                    await AwsSsoService.Login();

                if (success)
                {
                    await RunSearchAsync(profile, searchText, startDate, startTime, endDate, endTime);
                    return;
                }
            }

            MessageBox.Show(ex.ToString(), "Search error");
        }
        finally
        {
            _searchCancellation = null;
        }
    }

    private List<string>
   GetSelectedLogGroups(
       IEnumerable<LogGroupNode> nodes)
    {
        var result =
            new List<string>();

        foreach (var node
                 in nodes)
        {
            if (node.IsChecked
                &&
                node.IsLeaf
                &&
                !string.IsNullOrWhiteSpace(
                    node.FullPath))
            {
                result.Add(
                    node.FullPath);
            }

            result.AddRange(
                GetSelectedLogGroups(
                    node.Children));
        }

        return result;
    }

    public async Task
        UseCustomQueryAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(
                    QueryEditorTextBox.Text))
            {
                MessageBox.Show(
                    "No custom query.");

                return;
            }

            if (string.IsNullOrWhiteSpace(
                    _currentProfile))
            {
                MessageBox.Show(
                    "Choisir un profil AWS");

                return;
            }

            SearchProgressBar.Value =
                0;

            ProgressTextBlock.Text =
                "Executing custom query...";

            List<string>
    selectedLogGroups;

            if (SearchAllLogsCheckBox
                ?.IsChecked == true)
            {
                selectedLogGroups =
                    new();
            }
            else
            {
                selectedLogGroups =
                    GetSelectedLogGroups(
                        _logGroupTree);
            }

            var progress =
                new Progress<int>(
                    percent =>
                    {
                        SearchProgressBar.Value =
                            percent;

                        ProgressTextBlock.Text =
                            $"Searching... {percent}%";
                    });

            var (startDate, startTime, endDate, endTime) =
                GetDateRange?.Invoke()
                ?? (DateTime.Today, "00:00", DateTime.Today.AddDays(1), "00:00");

            var results =
                await _cloudWatchService
                    .SearchLogs(
                        _currentProfile,
                        _currentSearchText,
                        startDate,
                        startTime,
                        endDate,
                        endTime,
                        selectedLogGroups,
                        progress,
                        QueryEditorTextBox.Text);

            DisplayResults(
                results);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.ToString(),
                "Custom query error");
        }
    }

    private async void
        UseCustomQuery_Click(
            object sender,
            RoutedEventArgs e)
    {
        await UseCustomQueryAsync();
    }

    private void
    LogsDataGrid_DoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        try
        {
            if (sender
                is not DataGrid dataGrid)
            {
                return;
            }

            if (dataGrid.SelectedItem
                is not LogEntry selectedLog)
            {
                return;
            }

            var viewer =
                new JsonViewerWindow(
                    selectedLog.Message);

            viewer.Owner =
                Window.GetWindow(this);

            viewer.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.ToString(),
                "Viewer error");
        }
    }

    private void
       DebugModeCheckBox_Checked(
           object sender,
           RoutedEventArgs e)
    {
        DebugPanel.Visibility =
           Visibility.Visible;

        try
        {
            QueryEditorTextBox.Text =
                _cloudWatchService
                    .BuildQuery(
                        _currentSearchText);

            Dispatcher.InvokeAsync(() =>
            {
                ApplyCloudWatchSyntaxHighlighting();
                ResizeQueryEditorToFitContent();
            });
        }
        catch
        {
        }
    }

    private void
    DebugModeCheckBox_Unchecked(
        object sender,
        RoutedEventArgs e)
    {
        DebugPanel.Visibility =
            Visibility.Collapsed;

        // Rendre à Log Groups l'espace qu'on lui avait emprunté.
        LogGroupsRow.Height =
            new GridLength(LogGroupsBaseHeight);

        QueryEditorTextBox.Height =
            QueryEditorBaseHeight;

        _queryEditorCurrentHeight =
            QueryEditorBaseHeight;
    }

    /// Agrandit l'éditeur de requête pour afficher toutes les lignes
    /// pré-remplies sans scroll interne (dans une limite raisonnable), en
    /// empruntant l'espace nécessaire au panneau Log Groups plutôt qu'aux
    /// résultats.
    private void
    ResizeQueryEditorToFitContent()
    {
        int lineCount =
            Math.Max(
                1,
                QueryEditorTextBox.Document.LineCount);

        double desiredHeight =
            Math.Clamp(
                lineCount * QueryEditorLineHeight + 16,
                QueryEditorBaseHeight,
                QueryEditorMaxHeight);

        QueryEditorTextBox.Height =
            desiredHeight;

        double extraNeeded =
            desiredHeight
            - QueryEditorBaseHeight;

        LogGroupsRow.Height =
            new GridLength(
                Math.Max(
                    LogGroupsMinHeight,
                    LogGroupsBaseHeight - extraNeeded));

        _queryEditorCurrentHeight =
            desiredHeight;
    }

    private void
        PreviewQuery_Click(
            object sender,
            RoutedEventArgs e)
    {
        try
        {
            QueryEditorTextBox.Text =
                _cloudWatchService
                    .BuildQuery(
                        _currentSearchText);

            Dispatcher.InvokeAsync(() =>
            {
                ApplyCloudWatchSyntaxHighlighting();
                ResizeQueryEditorToFitContent();
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.ToString(),
                "Preview query error");
        }
    }

    private void
        CopyQuery_Click(
            object sender,
            RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(
                QueryEditorTextBox.Text);

            MessageBox.Show(
                "Query copied.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.ToString(),
                "Copy error");
        }
    }

    private void
    DisplayResults(
        List<LogEntry> results)
    {
        var groupedResults =
            results
                .GroupBy(x =>
                    x.LogGroup)
                .OrderBy(x =>
                    x.Key)
                .Select(g =>
                    new LogGroupResult
                    {
                        LogGroup =
                            g.Key,

                        Count =
                            g.Count(),

                        Logs =
                            new ObservableCollection<LogEntry>(
                                g.OrderByDescending(
                                    x => x.Timestamp))
                    })
                .ToList();

        LogsGroupedItemsControl
            .ItemsSource =
                groupedResults;

        SearchProgressBar.Value =
            100;

        ProgressTextBlock.Text =
            $"Done ({results.Count} results)";
    }

    public void
    OnSearchTextChanged(
        string searchText)
    {
        _currentSearchText =
            searchText;

        try
        {
            if (DebugModeCheckBox
                ?.IsChecked != true)
            {
                return;
            }

            QueryEditorTextBox.Text =
                _cloudWatchService
                    .BuildQuery(
                        searchText);

            Dispatcher.InvokeAsync(() =>
            {
                ApplyCloudWatchSyntaxHighlighting();
                ResizeQueryEditorToFitContent();
            });
        }
        catch
        {
        }
    }

    private void
       ApplyCloudWatchSyntaxHighlighting()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(
                    QueryEditorTextBox.Text))
            {
                return;
            }

            QueryEditorTextBox.SyntaxHighlighting =
                null;

            QueryEditorTextBox.TextArea
                .TextView
                .LineTransformers
                .Clear();

            QueryEditorTextBox.TextArea
                .TextView
                .LineTransformers
                .Add(
                    new CloudWatchColorizer());

            QueryEditorTextBox.TextArea
                .TextView
                .Redraw();
        }
        catch
        {
        }
    }

    private string?
    GetSubCategory(
        string logGroup)
    {
        var lower =
            logGroup
                .ToLower();

        if (lower.Contains(
                "proxy"))
        {
            return "Proxy";
        }

        if (lower.Contains(
                "alert"))
        {
            return "Alert";
        }

        if (lower.Contains(
                "postgresql"))
        {
            return "Core";
        }

        if (lower.Contains(
                "instance"))
        {
            return "Instance";
        }

        return null;
    }

    private void
BuildLogGroupTree(
    List<string> logGroups)
    {
        _logGroupTree.Clear();

        // dictionnaire des catégories
        var categoryNodes =
            new Dictionary<
                string,
                LogGroupNode>();

        // créer catégories du JSON
        foreach (var category
                 in _logGroupCategories)
        {
            var categoryNode =
                new LogGroupNode
                {
                    Name =
                        category.Name,

                    FullPath =
                        category.Name
                };

            _logGroupTree.Add(
                categoryNode);

            categoryNodes[
                category.Name] =
                    categoryNode;
        }

        // catégorie fallback
        var uncategorized =
            new LogGroupNode
            {
                Name =
                    "Uncategorized",

                FullPath =
                    "Uncategorized"
            };

        _logGroupTree.Add(
            uncategorized);

        // ranger les logs
        foreach (var logGroup
                 in logGroups
                     .OrderBy(x => x))
        {
            LogGroupNode?
                targetCategory =
                    null;

            // chercher un match
            foreach (var category
                     in _logGroupCategories)
            {
                bool matched =
                    category.Patterns
                        .Any(pattern =>
                            logGroup.Contains(
                                pattern,
                                StringComparison
                                    .OrdinalIgnoreCase));

                if (!matched)
                {
                    continue;
                }

                targetCategory =
                    categoryNodes[
                        category.Name];

                break;
            }

            // fallback
            targetCategory
                ??=
                    uncategorized;

            // sous-catégorie optionnelle
            var subCategory =
                GetSubCategory(
                    logGroup);

            LogGroupNode
                parentNode =
                    targetCategory;

            // seulement si utile
            if (!string.IsNullOrWhiteSpace(
                    subCategory))
            {
                var subCategoryNode =
                    targetCategory
                        .Children
                        .FirstOrDefault(
                            x =>
                                x.Name
                                == subCategory);

                if (subCategoryNode
                    == null)
                {
                    subCategoryNode =
                        new LogGroupNode
                        {
                            Name =
                                subCategory,

                            FullPath =
                                subCategory
                        };

                    targetCategory
                        .Children
                        .Add(
                            subCategoryNode);
                }
                parentNode =
                    subCategoryNode;
            }

            // stage plus profond
            var stageCategory =
                GetStageCategory(
                    logGroup);

            var stageNode =
                parentNode
                    .Children
                    .FirstOrDefault(
                        x =>
                            x.Name
                            == stageCategory);

            if (stageNode
                == null)
            {
                stageNode =
                    new LogGroupNode
                    {
                        Name =
                            stageCategory,

                        FullPath =
                            stageCategory
                    };

                parentNode
                    .Children
                    .Add(
                        stageNode);
            }

            stageNode
                .Children
                .Add(
                    new LogGroupNode
                    {
                        Name =
                            logGroup,

                        FullPath =
                            logGroup,

                        IsLeaf =
                            true
                    });
        }
    }

    private string
    GetStageCategory(
        string logGroup)
    {
        var match =
            Regex.Match(
                logGroup,
                @"-(a|b)(?=[\/\-]|$)",
                RegexOptions.IgnoreCase);

        if (match.Success)
        {
            return
                $"-{match.Groups[1]
                    .Value
                    .ToLower()}";
        }

        return "core";
    }

    private void
    LogGroupsTreeView_PreviewMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        // laisser le Shell gérer
    }
}

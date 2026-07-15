using KubaToolKit.Modules.CloudTrail.Models;
using KubaToolKit.Shared.Services;
using KubaToolKit.Shared.Windows;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace KubaToolKit.Modules.CloudTrail;

public partial class CloudTrailView
    : UserControl
{
    private readonly CloudTrailService _cloudTrailService = new();
    private CancellationTokenSource? _searchCancellation;

    public CloudTrailView()
    {
        InitializeComponent();
    }

    public bool IsSearchRunning => _searchCancellation != null;

    public void
    CancelSearch()
    {
        _searchCancellation?.Cancel();
    }

    public async Task
    RunSearchAsync(
        string profile,
        string attributeKey,
        string attributeValue,
        DateTime? startDate,
        string startTime,
        DateTime? endDate,
        string endTime)
    {
        try
        {
            // Effacer immédiatement l'ancien résultat plutôt qu'à la toute
            // fin : sur "All events" (pas de filtre), la pagination peut
            // prendre longtemps (LookupEvents est limitée à ~2 req/s côté
            // AWS) et laisser l'ancien tableau affiché pendant tout ce
            // temps donnait l'impression que la recherche ne faisait rien.
            EventsGroupedItemsControl.ItemsSource = null;
            SearchProgressBar.Value = 0;
            SearchProgressBar.IsIndeterminate = true;
            ProgressTextBlock.Text = "Searching CloudTrail...";

            // Total inconnu à l'avance : on affiche le nombre d'évènements
            // trouvés au fil de la pagination plutôt qu'un faux
            // pourcentage, la barre reste indéterminée pendant la recherche.
            var progress =
                new Progress<int>(count =>
                {
                    ProgressTextBlock.Text = $"Searching... {count} event(s) found so far";
                });

            _searchCancellation = new CancellationTokenSource();

            var (results, truncated) =
                await _cloudTrailService.SearchEvents(
                    profile,
                    attributeKey,
                    attributeValue,
                    startDate,
                    startTime,
                    endDate,
                    endTime,
                    progress,
                    _searchCancellation.Token);

            DisplayResults(results, truncated);
        }
        catch (OperationCanceledException)
        {
            ProgressTextBlock.Text = "Search cancelled";
        }
        catch (Exception ex)
        {
            if (AwsSsoService.IsSsoExpired(ex))
            {
                var success = await AwsSsoService.Login();

                if (success)
                {
                    await RunSearchAsync(
                        profile,
                        attributeKey,
                        attributeValue,
                        startDate,
                        startTime,
                        endDate,
                        endTime);

                    return;
                }
            }

            MessageBox.Show(ex.ToString(), "Search error");
        }
        finally
        {
            SearchProgressBar.IsIndeterminate = false;
            _searchCancellation = null;
        }
    }

    private void
    EventsDataGrid_DoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        try
        {
            if (sender is not DataGrid dataGrid)
            {
                return;
            }

            if (dataGrid.SelectedItem is not CloudTrailEventItem selectedEvent)
            {
                return;
            }

            var viewer = new JsonViewerWindow(selectedEvent.CloudTrailEventJson);
            viewer.Owner = Window.GetWindow(this);
            viewer.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "Viewer error");
        }
    }

    private void
    DisplayResults(
        List<CloudTrailEventItem> results,
        bool truncated)
    {
        var groupedResults =
            results
                .GroupBy(x => x.EventSource)
                .OrderBy(x => x.Key)
                .Select(g =>
                    new CloudTrailEventGroup
                    {
                        EventSource = g.Key,
                        Count = g.Count(),

                        Events =
                            new ObservableCollection<CloudTrailEventItem>(
                                g.OrderByDescending(x => x.Timestamp))
                    })
                .ToList();

        EventsGroupedItemsControl.ItemsSource = groupedResults;

        SearchProgressBar.Value = 100;

        ProgressTextBlock.Text =
            truncated
                ? $"Done ({results.Count} results, truncated — narrow the time range or pick an attribute)"
                : $"Done ({results.Count} results)";
    }
}

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
            SearchProgressBar.Value = 0;
            ProgressTextBlock.Text = "Searching CloudTrail...";

            var progress =
                new Progress<int>(percent =>
                {
                    SearchProgressBar.Value = percent;
                    ProgressTextBlock.Text = $"Searching... {percent}%";
                });

            _searchCancellation = new CancellationTokenSource();

            var results =
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

            DisplayResults(results);
        }
        catch (OperationCanceledException)
        {
            ProgressTextBlock.Text = "Search cancelled";
            SearchProgressBar.Value = 0;
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
        List<CloudTrailEventItem> results)
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
        ProgressTextBlock.Text = $"Done ({results.Count} results)";
    }
}

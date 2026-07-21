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
        Logger.Debug(
            $"CloudTrailView: search attribute='{attributeKey}' value='{attributeValue}' (profile '{profile}').");

        try
        {
            EventsGroupedItemsControl.ItemsSource = null;
            SearchProgressBar.Value = 0;
            SearchProgressBar.IsIndeterminate = true;
            ProgressTextBlock.Text = "Searching CloudTrail...";

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

            Logger.Info(
                $"CloudTrailView: search completed, {results.Count} result(s)"
                + (truncated ? " (truncated)." : "."));

            DisplayResults(results, truncated);
        }
        catch (OperationCanceledException)
        {
            Logger.Debug("CloudTrailView: search cancelled.");

            ProgressTextBlock.Text = "Search cancelled";
        }
        catch (Exception ex)
        {
            if (AwsSsoService.IsSsoExpired(ex))
            {
                Logger.Debug("CloudTrailView: SSO session expired, attempting reconnection.");

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

            Logger.Error("CloudTrailView: search failed.", ex);

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

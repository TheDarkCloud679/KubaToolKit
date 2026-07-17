using KubaToolKit.Modules.StepFunctions.Models;
using KubaToolKit.Shared.Services;
using KubaToolKit.Shared.Windows;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace KubaToolKit.Modules.StepFunctions;

public partial class ExecutionEventsWindow
    : Window
{
    private readonly StepFunctionsService _stepFunctionsService = new();
    private readonly ObservableCollection<HistoryEventItem> _events = new();
    private readonly string _profile;
    private readonly string _executionArn;
    private readonly string? _logGroupIdentifier;
    private CancellationTokenSource? _loadCancellation;

    private DataGridColumn? _sortColumn;
    private ListSortDirection _sortDirection = ListSortDirection.Ascending;

    public ExecutionEventsWindow(
        string profile,
        string executionName,
        string executionArn,
        string? logGroupIdentifier)
    {
        InitializeComponent();

        _profile = profile;
        _executionArn = executionArn;
        _logGroupIdentifier = logGroupIdentifier;

        ExecutionNameTextBlock.Text = executionName;

        EventsGrid.ItemsSource = _events;

        Loaded += async (_, __) =>
            await RefreshAsync();
    }

    private async Task
    RefreshAsync()
    {
        try
        {
            LoadingProgressBar.Visibility =
                Visibility.Visible;

            RefreshButton.IsEnabled =
                false;

            _loadCancellation?.Cancel();

            _loadCancellation =
                new CancellationTokenSource();

            var events =
                _logGroupIdentifier != null
                    ? await _stepFunctionsService.GetExpressExecutionHistoryFromLogs(
                        _profile,
                        _logGroupIdentifier,
                        _executionArn,
                        _loadCancellation.Token)
                    : await _stepFunctionsService.GetExecutionHistory(
                        _profile,
                        _executionArn,
                        _loadCancellation.Token);

            _events.Clear();

            foreach (var historyEvent in events)
            {
                _events.Add(historyEvent);
            }

            Dispatcher.BeginInvoke(
                new Action(() => DataGridSortHelper.RefreshColumnWidths(EventsGrid)),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
        catch (OperationCanceledException)
        {
            Logger.Debug("ExecutionEventsWindow: load cancelled.");
        }
        catch (Exception ex)
        {
            Logger.Error("ExecutionEventsWindow: failed to load history.", ex);

            MessageBox.Show(
                ex.ToString(),
                "Execution history loading error");
        }
        finally
        {
            LoadingProgressBar.Visibility =
                Visibility.Collapsed;

            RefreshButton.IsEnabled =
                true;
        }
    }

    private async void
    RefreshButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        await RefreshAsync();
    }

    private void
    EventsGrid_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        if (DataGridSortHelper.FindAncestor<DataGridColumnHeader>(e.OriginalSource as DependencyObject)
            is { } header)
        {
            DataGridSortHelper.SortByColumn(
                _events,
                EventsGrid.Columns,
                header.Column,
                ref _sortColumn,
                ref _sortDirection);

            return;
        }

        if (EventsGrid.SelectedItem is not HistoryEventItem historyEvent)
        {
            return;
        }

        var viewer =
            new JsonViewerWindow(historyEvent.DetailsJson);

        viewer.Owner = this;

        viewer.Show();
    }
}

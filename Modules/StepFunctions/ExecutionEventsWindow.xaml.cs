using KubaToolKit.Modules.StepFunctions.Models;
using KubaToolKit.Shared.Windows;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace KubaToolKit.Modules.StepFunctions;

public partial class ExecutionEventsWindow
    : Window
{
    private readonly StepFunctionsService _stepFunctionsService = new();
    private readonly ObservableCollection<HistoryEventItem> _events = new();
    private readonly string _profile;
    private readonly string _executionArn;
    private CancellationTokenSource? _loadCancellation;

    public ExecutionEventsWindow(
        string profile,
        string executionName,
        string executionArn)
    {
        InitializeComponent();

        _profile = profile;
        _executionArn = executionArn;

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
                await _stepFunctionsService.GetExecutionHistory(
                    _profile,
                    _executionArn,
                    _loadCancellation.Token);

            _events.Clear();

            foreach (var historyEvent in events)
            {
                _events.Add(historyEvent);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
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

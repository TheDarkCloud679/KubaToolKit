using KubaToolKit.Modules.Sqs.Models;
using KubaToolKit.Shared.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace KubaToolKit.Modules.Sqs;

public partial class SqsView
    : UserControl
{
    private readonly SqsService _sqsService = new();
    private readonly ObservableCollection<SqsQueueItem> _queues = new();
    private string? _currentProfile;
    private CancellationTokenSource? _loadCancellation;

    public SqsView()
    {
        InitializeComponent();

        QueuesGrid.ItemsSource = _queues;
    }

    public async Task
    OnProfileChanged(
        string? profile)
    {
        _currentProfile = profile;

        await RefreshAsync();
    }

    public async Task
    RefreshAsync()
    {
        if (string.IsNullOrWhiteSpace(_currentProfile))
        {
            return;
        }

        try
        {
            LoadingProgressBar.Visibility =
                Visibility.Visible;

            RefreshButton.IsEnabled =
                false;

            _loadCancellation?.Cancel();

            _loadCancellation =
                new CancellationTokenSource();

            var queues =
                await _sqsService.ListQueuesWithCounts(
                    _currentProfile,
                    null,
                    _loadCancellation.Token);

            _queues.Clear();

            foreach (var queue in queues)
            {
                _queues.Add(queue);
            }

            Dispatcher.BeginInvoke(
                new Action(() => DataGridSortHelper.RefreshColumnWidths(QueuesGrid)),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
        catch (OperationCanceledException)
        {
            Logger.Debug("SqsView: refresh cancelled.");
        }
        catch (Exception ex)
        {
            if (AwsSsoService.IsSsoExpired(ex))
            {
                Logger.Debug("SqsView: SSO session expired, attempting reconnection.");

                var success =
                    await AwsSsoService.Login();

                if (success)
                {
                    await RefreshAsync();
                    return;
                }
            }

            Logger.Error(
                $"SqsView: refresh failed (profile '{_currentProfile}').",
                ex);

            MessageBox.Show(
                ex.ToString(),
                "SQS loading error");
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
    SearchMessage_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button button
            || button.DataContext is not SqsQueueItem queue)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_currentProfile))
        {
            MessageBox.Show(
                "Select an AWS profile");

            return;
        }

        var window =
            new SqsMessagesWindow(
                _currentProfile,
                queue.Name,
                queue.Url);

        window.Owner =
            Window.GetWindow(this);

        window.Show();
    }
}

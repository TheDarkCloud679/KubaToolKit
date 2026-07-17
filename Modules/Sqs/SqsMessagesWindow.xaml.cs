using KubaToolKit.Modules.Sqs.Models;
using KubaToolKit.Shared.Services;
using KubaToolKit.Shared.Windows;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace KubaToolKit.Modules.Sqs;

public partial class SqsMessagesWindow
    : Window
{
    private const int MaxMessages = 200;

    private readonly SqsService _sqsService = new();
    private readonly ObservableCollection<SqsMessageItem> _messages = new();
    private readonly string _profile;
    private readonly string _queueUrl;
    private CancellationTokenSource? _searchCancellation;

    public SqsMessagesWindow(
        string profile,
        string queueName,
        string queueUrl)
    {
        InitializeComponent();

        _profile = profile;
        _queueUrl = queueUrl;

        QueueNameTextBlock.Text = queueName;

        MessagesGrid.ItemsSource = _messages;

        Loaded += async (_, __) =>
            await RunSearchAsync();
    }

    private async void
    SearchButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        await RunSearchAsync();
    }

    private async void
    SearchTextBox_KeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        await RunSearchAsync();
    }

    private async Task
    RunSearchAsync()
    {
        try
        {
            SearchButton.IsEnabled = false;

            SearchProgressBar.Visibility =
                Visibility.Visible;

            SearchProgressBar.Value = 0;

            StatusTextBlock.Text =
                "Searching...";

            _searchCancellation?.Cancel();

            _searchCancellation =
                new CancellationTokenSource();

            var progress =
                new Progress<int>(percent =>
                {
                    SearchProgressBar.Value = percent;
                });

            var messages =
                await _sqsService.PeekMessages(
                    _profile,
                    _queueUrl,
                    SearchTextBox.Text,
                    MaxMessages,
                    progress,
                    _searchCancellation.Token);

            _messages.Clear();

            foreach (var message in messages)
            {
                _messages.Add(message);
            }

            Dispatcher.BeginInvoke(
                new Action(() => DataGridSortHelper.RefreshColumnWidths(MessagesGrid)),
                System.Windows.Threading.DispatcherPriority.Loaded);

            StatusTextBlock.Text =
                $"{messages.Count} message(s) • read only, no deletion";
        }
        catch (OperationCanceledException)
        {
            Logger.Debug("SqsMessagesWindow: search cancelled.");
        }
        catch (Exception ex)
        {
            Logger.Error(
                $"SqsMessagesWindow: failed to search '{_queueUrl}'.",
                ex);

            MessageBox.Show(
                ex.ToString(),
                "SQS search error");
        }
        finally
        {
            SearchButton.IsEnabled = true;

            SearchProgressBar.Visibility =
                Visibility.Collapsed;
        }
    }

    private void
    MessagesGrid_DoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        if (MessagesGrid.SelectedItem
            is not SqsMessageItem message)
        {
            return;
        }

        var viewer =
            new JsonViewerWindow(message.Body);

        viewer.Owner = this;

        viewer.Show();
    }
}

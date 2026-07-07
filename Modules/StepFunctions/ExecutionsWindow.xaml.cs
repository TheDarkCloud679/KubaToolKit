using KubaToolKit.Modules.StepFunctions.Models;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace KubaToolKit.Modules.StepFunctions;

public partial class ExecutionsWindow
    : Window
{
    private readonly StepFunctionsService _stepFunctionsService = new();
    private readonly ObservableCollection<ExecutionItem> _executions = new();
    private readonly string _profile;
    private readonly string _stateMachineArn;
    private CancellationTokenSource? _loadCancellation;

    public ExecutionsWindow(
        string profile,
        string stateMachineName,
        string stateMachineArn)
    {
        InitializeComponent();

        _profile = profile;
        _stateMachineArn = stateMachineArn;

        StateMachineNameTextBlock.Text = stateMachineName;

        ExecutionsGrid.ItemsSource = _executions;

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

            var executions =
                await _stepFunctionsService.ListExecutions(
                    _profile,
                    _stateMachineArn,
                    _loadCancellation.Token);

            _executions.Clear();

            foreach (var execution in executions)
            {
                _executions.Add(execution);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.ToString(),
                "Executions loading error");
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
    ExecutionsGrid_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        if (ExecutionsGrid.SelectedItem is not ExecutionItem execution)
        {
            return;
        }

        var window =
            new ExecutionEventsWindow(
                _profile,
                execution.Name,
                execution.Arn);

        window.Owner = this;

        window.Show();
    }
}

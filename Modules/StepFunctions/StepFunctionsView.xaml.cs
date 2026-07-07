using KubaToolKit.Modules.StepFunctions.Models;
using KubaToolKit.Shared.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace KubaToolKit.Modules.StepFunctions;

public partial class StepFunctionsView
    : UserControl
{
    private readonly StepFunctionsService _stepFunctionsService = new();
    private readonly ObservableCollection<StateMachineItem> _stateMachines = new();
    private string? _currentProfile;
    private CancellationTokenSource? _loadCancellation;

    public StepFunctionsView()
    {
        InitializeComponent();

        StateMachinesGrid.ItemsSource = _stateMachines;
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

            var stateMachines =
                await _stepFunctionsService.ListStateMachines(
                    _currentProfile,
                    _loadCancellation.Token);

            _stateMachines.Clear();

            foreach (var stateMachine in stateMachines)
            {
                _stateMachines.Add(stateMachine);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (AwsSsoService.IsSsoExpired(ex))
            {
                var success =
                    await AwsSsoService.Login();

                if (success)
                {
                    await RefreshAsync();
                    return;
                }
            }

            MessageBox.Show(
                ex.ToString(),
                "Step Functions loading error");
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
    StateMachinesGrid_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        if (StateMachinesGrid.SelectedItem is not StateMachineItem stateMachine
            || string.IsNullOrWhiteSpace(_currentProfile))
        {
            return;
        }

        var window =
            new ExecutionsWindow(
                _currentProfile,
                stateMachine.Name,
                stateMachine.Arn);

        window.Owner =
            Window.GetWindow(this);

        window.Show();
    }
}

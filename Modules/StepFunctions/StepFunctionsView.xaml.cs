using KubaToolKit.Modules.StepFunctions.Models;
using KubaToolKit.Shared.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace KubaToolKit.Modules.StepFunctions;

public partial class StepFunctionsView
    : UserControl
{
    private readonly StepFunctionsService _stepFunctionsService = new();
    private readonly ObservableCollection<StateMachineItem> _stateMachines = new();
    private string? _currentProfile;
    private CancellationTokenSource? _loadCancellation;

    private DataGridColumn? _sortColumn;
    private ListSortDirection _sortDirection = ListSortDirection.Ascending;

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

            Dispatcher.BeginInvoke(
                new Action(() => DataGridSortHelper.RefreshColumnWidths(StateMachinesGrid)),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
        catch (OperationCanceledException)
        {
            Logger.Debug("StepFunctionsView: refresh cancelled.");
        }
        catch (Exception ex)
        {
            if (AwsSsoService.IsSsoExpired(ex))
            {
                Logger.Debug("StepFunctionsView: SSO session expired, attempting reconnection.");

                var success =
                    await AwsSsoService.Login();

                if (success)
                {
                    await RefreshAsync();
                    return;
                }
            }

            Logger.Error(
                $"StepFunctionsView: refresh failed (profile '{_currentProfile}').",
                ex);

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
        if (DataGridSortHelper.FindAncestor<DataGridColumnHeader>(e.OriginalSource as DependencyObject)
            is { } header)
        {
            DataGridSortHelper.SortByColumn(
                _stateMachines,
                StateMachinesGrid.Columns,
                header.Column,
                ref _sortColumn,
                ref _sortDirection);

            return;
        }

        if (StateMachinesGrid.SelectedItem is not StateMachineItem stateMachine
            || string.IsNullOrWhiteSpace(_currentProfile))
        {
            return;
        }

        var window =
            new ExecutionsWindow(
                _currentProfile,
                stateMachine.Name,
                stateMachine.Arn,
                stateMachine.Type);

        window.Owner =
            Window.GetWindow(this);

        window.Show();
    }
}

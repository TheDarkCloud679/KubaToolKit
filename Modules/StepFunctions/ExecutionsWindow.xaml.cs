using KubaToolKit.Modules.StepFunctions.Models;
using KubaToolKit.Shared.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace KubaToolKit.Modules.StepFunctions;

public partial class ExecutionsWindow
    : Window
{
    private readonly StepFunctionsService _stepFunctionsService = new();
    private readonly ObservableCollection<ExecutionItem> _executions = new();
    private readonly string _profile;
    private readonly string _stateMachineArn;
    private readonly bool _isExpress;
    private CancellationTokenSource? _loadCancellation;

    private DataGridColumn? _sortColumn;
    private ListSortDirection _sortDirection = ListSortDirection.Ascending;

    public ExecutionsWindow(
        string profile,
        string stateMachineName,
        string stateMachineArn,
        string stateMachineType)
    {
        InitializeComponent();

        _profile = profile;
        _stateMachineArn = stateMachineArn;
        _isExpress = string.Equals(stateMachineType, "EXPRESS", StringComparison.OrdinalIgnoreCase);

        StateMachineNameTextBlock.Text = stateMachineName;

        if (_isExpress)
        {
            ExpressHintTextBlock.Visibility = Visibility.Visible;
        }

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
                _isExpress
                    ? await _stepFunctionsService.ListExpressExecutionsFromLogs(
                        _profile,
                        _stateMachineArn,
                        _loadCancellation.Token)
                    : await _stepFunctionsService.ListExecutions(
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
        catch (ExpressLoggingNotConfiguredException ex)
        {
            MessageBox.Show(
                ex.Message,
                "Journalisation non configurée");

            Close();
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
        if (DataGridSortHelper.FindAncestor<DataGridColumnHeader>(e.OriginalSource as DependencyObject)
            is { } header)
        {
            DataGridSortHelper.SortByColumn(
                _executions,
                ExecutionsGrid.Columns,
                header.Column,
                ref _sortColumn,
                ref _sortDirection);

            return;
        }

        if (ExecutionsGrid.SelectedItem is not ExecutionItem execution)
        {
            return;
        }

        var window =
            new ExecutionEventsWindow(
                _profile,
                execution.Name,
                execution.Arn,
                execution.LogGroupIdentifier);

        window.Owner = this;

        window.Show();
    }
}

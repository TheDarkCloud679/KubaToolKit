using KubaToolKit.Modules.AtlassianSearch.Models;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace KubaToolKit.Modules.AtlassianSearch;

// Every filter the Statistics tab supports, moved out of that tab's own
// (increasingly crowded) header into one dedicated popup. The picker
// buttons mutate the caller's MultiSelectFilterState instances directly
// (same objects, passed by reference) the moment a selection is made --
// there's no staged/undo state for those, same as before this popup
// existed. Operator/date values are read back into properties whenever
// the window closes (OnClosing, not just the Close button), so closing
// via the titlebar X still hands the caller whatever was last selected.
public partial class StatsFilterWindow
    : Window
{
    private readonly MultiSelectFilterState _projectFilter;
    private readonly MultiSelectFilterState _assigneeFilter;
    private readonly MultiSelectFilterState _statusFilter;
    private readonly MultiSelectFilterState _moduleFilter;
    private readonly MultiSelectFilterState _escalationFilter;
    private readonly MultiSelectFilterState _requestTypeFilter;

    public string ProjectOperator { get; private set; }
    public string AssigneeOperator { get; private set; }
    public string StatusOperator { get; private set; }
    public string ModuleOperator { get; private set; }
    public string EscalationOperator { get; private set; }
    public string RequestTypeOperator { get; private set; }
    public DateTime? From { get; private set; }
    public DateTime? To { get; private set; }

    public StatsFilterWindow(
        MultiSelectFilterState projectFilter,
        string projectOperator,
        MultiSelectFilterState assigneeFilter,
        string assigneeOperator,
        MultiSelectFilterState statusFilter,
        string statusOperator,
        MultiSelectFilterState moduleFilter,
        string moduleOperator,
        MultiSelectFilterState escalationFilter,
        string escalationOperator,
        MultiSelectFilterState requestTypeFilter,
        string requestTypeOperator,
        DateTime? from,
        DateTime? to)
    {
        InitializeComponent();

        _projectFilter = projectFilter;
        _assigneeFilter = assigneeFilter;
        _statusFilter = statusFilter;
        _moduleFilter = moduleFilter;
        _escalationFilter = escalationFilter;
        _requestTypeFilter = requestTypeFilter;

        ProjectOperator = projectOperator;
        AssigneeOperator = assigneeOperator;
        StatusOperator = statusOperator;
        ModuleOperator = moduleOperator;
        EscalationOperator = escalationOperator;
        RequestTypeOperator = requestTypeOperator;
        From = from;
        To = to;

        SelectOperatorValue(ProjectOperatorCombo, projectOperator);
        SelectOperatorValue(AssigneeOperatorCombo, assigneeOperator);
        SelectOperatorValue(StatusOperatorCombo, statusOperator);
        SelectOperatorValue(ModuleOperatorCombo, moduleOperator);
        SelectOperatorValue(EscalationOperatorCombo, escalationOperator);
        SelectOperatorValue(RequestTypeOperatorCombo, requestTypeOperator);

        FromDatePicker.SelectedDate = from;
        ToDatePicker.SelectedDate = to;

        UpdateFilterButton(ProjectPickerButton, _projectFilter);
        UpdateFilterButton(AssigneePickerButton, _assigneeFilter);
        UpdateFilterButton(StatusPickerButton, _statusFilter);
        UpdateFilterButton(ModulePickerButton, _moduleFilter);
        UpdateFilterButton(EscalationPickerButton, _escalationFilter);
        UpdateFilterButton(RequestTypePickerButton, _requestTypeFilter);
    }

    private void
    OpenMultiSelectFilter(
        Button button,
        MultiSelectFilterState state,
        string title)
    {
        var result =
            MultiSelectPickerWindow.Prompt(this, title, state.AllOptions, state.SelectedValues);

        if (result == null)
        {
            return;
        }

        state.SelectedValues = result;

        UpdateFilterButton(button, state);
    }

    private void
    ProjectPickerButton_Click(
        object sender,
        RoutedEventArgs e) =>
        OpenMultiSelectFilter(ProjectPickerButton, _projectFilter, "Select project(s)");

    private void
    AssigneePickerButton_Click(
        object sender,
        RoutedEventArgs e) =>
        OpenMultiSelectFilter(AssigneePickerButton, _assigneeFilter, "Select person(s)");

    private void
    StatusPickerButton_Click(
        object sender,
        RoutedEventArgs e) =>
        OpenMultiSelectFilter(StatusPickerButton, _statusFilter, "Select status(es)");

    private void
    ModulePickerButton_Click(
        object sender,
        RoutedEventArgs e) =>
        OpenMultiSelectFilter(ModulePickerButton, _moduleFilter, "Select module(s)");

    private void
    EscalationPickerButton_Click(
        object sender,
        RoutedEventArgs e) =>
        OpenMultiSelectFilter(EscalationPickerButton, _escalationFilter, "Select escalade(s)");

    private void
    RequestTypePickerButton_Click(
        object sender,
        RoutedEventArgs e) =>
        OpenMultiSelectFilter(RequestTypePickerButton, _requestTypeFilter, "Select request type(s)");

    private static void
    UpdateFilterButton(
        Button button,
        MultiSelectFilterState state) =>
        button.Content = state.Summary();

    private static void
    SelectOperatorValue(
        ComboBox operatorCombo,
        string op)
    {
        foreach (ComboBoxItem item in operatorCombo.Items)
        {
            if (string.Equals(item.Content as string, op, StringComparison.Ordinal))
            {
                operatorCombo.SelectedItem = item;

                return;
            }
        }

        operatorCombo.SelectedIndex = 0;
    }

    private static string
    GetOperatorValue(
        ComboBox operatorCombo) =>
        (operatorCombo.SelectedItem as ComboBoxItem)?.Content as string ?? "in";

    private void
    ClearAllButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _projectFilter.SelectedValues = new();
        _assigneeFilter.SelectedValues = new();
        _statusFilter.SelectedValues = new();
        _moduleFilter.SelectedValues = new();
        _escalationFilter.SelectedValues = new();
        _requestTypeFilter.SelectedValues = new();

        UpdateFilterButton(ProjectPickerButton, _projectFilter);
        UpdateFilterButton(AssigneePickerButton, _assigneeFilter);
        UpdateFilterButton(StatusPickerButton, _statusFilter);
        UpdateFilterButton(ModulePickerButton, _moduleFilter);
        UpdateFilterButton(EscalationPickerButton, _escalationFilter);
        UpdateFilterButton(RequestTypePickerButton, _requestTypeFilter);

        FromDatePicker.SelectedDate = null;
        ToDatePicker.SelectedDate = null;
    }

    private void
    CloseButton_Click(
        object sender,
        RoutedEventArgs e) =>
        Close();

    protected override void
    OnClosing(
        CancelEventArgs e)
    {
        ProjectOperator = GetOperatorValue(ProjectOperatorCombo);
        AssigneeOperator = GetOperatorValue(AssigneeOperatorCombo);
        StatusOperator = GetOperatorValue(StatusOperatorCombo);
        ModuleOperator = GetOperatorValue(ModuleOperatorCombo);
        EscalationOperator = GetOperatorValue(EscalationOperatorCombo);
        RequestTypeOperator = GetOperatorValue(RequestTypeOperatorCombo);
        From = FromDatePicker.SelectedDate;
        To = ToDatePicker.SelectedDate;

        base.OnClosing(e);
    }
}

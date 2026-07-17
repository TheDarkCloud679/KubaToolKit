using KubaToolKit.Modules.ApiClient.Models;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace KubaToolKit.Modules.ApiClient;

public partial class EnvironmentEditorWindow
    : Window
{
    private readonly CollectionStorageService _storage;
    private readonly EnvironmentSet _environment;
    private readonly ObservableCollection<HeaderItem> _variables = new();

    public bool Saved { get; private set; }

    public EnvironmentEditorWindow(
        CollectionStorageService storage,
        EnvironmentSet environment)
    {
        InitializeComponent();

        _storage = storage;
        _environment = environment;

        NameTextBox.Text = environment.Name;

        VariablesGrid.ItemsSource = _variables;

        foreach (var variable in environment.Variables)
        {
            _variables.Add(
                new HeaderItem
                {
                    Enabled = variable.Enabled,
                    Key = variable.Key,
                    Value = variable.Value
                });
        }
    }

    private void
    SaveButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        VariablesGrid.CommitEdit(DataGridEditingUnit.Row, true);

        if (string.IsNullOrWhiteSpace(NameTextBox.Text))
        {
            MessageBox.Show(
                "Enter an environment name");

            return;
        }

        _environment.Name = NameTextBox.Text.Trim();
        _environment.Variables = _variables.ToList();

        try
        {
            _storage.SaveEnvironment(_environment);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Environment save error");

            return;
        }

        Saved = true;

        Close();
    }

    private void
    CancelButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }

    private void
    DeleteVariableRow_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is Button button
            && button.DataContext is HeaderItem item)
        {
            _variables.Remove(item);
        }
    }
}

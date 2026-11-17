using Amazon.Runtime.CredentialManagement;
using KubaToolKit.Modules.ApiClient.Models;
using KubaToolKit.Shared.Windows;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace KubaToolKit.Modules.ApiClient;

public partial class AwsApiSyncWindow
    : Window
{
    private readonly AwsApiGatewayService _awsService = new();
    private readonly ObservableCollection<HeaderItem> _variables;
    private readonly ObservableCollection<DiscoveredApiEndpoint> _results = new();
    private readonly List<string> _existingVariableNames;

    public AwsApiSyncWindow(
        ObservableCollection<HeaderItem> variables)
    {
        InitializeComponent();

        _variables = variables;

        _existingVariableNames =
            _variables
                .Select(v => v.Key)
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct()
                .OrderBy(k => k)
                .ToList();

        ResultsGrid.ItemsSource = _results;

        var chain = new CredentialProfileStoreChain();

        ProfileCombo.ItemsSource =
            chain.ListProfiles()
                .Select(p => p.Name)
                .Where(n => n != "default")
                .OrderBy(n => n)
                .ToList();
    }

    // Each row's "Maps to" ComboBox is created fresh by the cell template,
    // so its suggestion list is set here rather than via a binding -- the
    // window has no DataContext of its own and the suggestion list is the
    // same for every row (the environment's existing variable names), not
    // per-row data.
    private void
    MapsToCombo_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        ((ComboBox)sender).ItemsSource = _existingVariableNames;
    }

    private async void
    DiscoverButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var profile = ProfileCombo.SelectedItem?.ToString();

        if (string.IsNullOrWhiteSpace(profile))
        {
            AppMessageBox.Show(
                "Select an AWS profile first.",
                "Sync from AWS");

            return;
        }

        DiscoverButton.IsEnabled = false;
        StatusText.Text = "Discovering APIs...";
        _results.Clear();

        try
        {
            var existingNames =
                new HashSet<string>(_existingVariableNames, StringComparer.Ordinal);

            var endpoints =
                await _awsService.DiscoverEndpoints(profile);

            foreach (var endpoint in endpoints)
            {
                endpoint.ExistingVariableNames = existingNames;

                _results.Add(endpoint);
            }

            StatusText.Text =
                endpoints.Count == 0
                    ? "No API Gateway custom domain mappings found for this profile."
                    : $"{endpoints.Count} endpoint(s) found.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "";

            AppMessageBox.Show(
                ex.ToString(),
                "AWS discovery error");
        }
        finally
        {
            DiscoverButton.IsEnabled = true;
        }
    }

    private void
    ApplyButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var applied = 0;

        foreach (var row in _results)
        {
            var name = row.MapsTo?.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var existing = _variables.FirstOrDefault(v => v.Key == name);

            var updated =
                new HeaderItem { Enabled = true, Key = name, Value = row.Url };

            if (existing != null)
            {
                // Replacing the item (rather than mutating existing.Value
                // in place) is what makes the grid in the environment
                // editor underneath actually repaint that row -- HeaderItem
                // doesn't raise property-changed notifications.
                _variables[_variables.IndexOf(existing)] = updated;
            }
            else
            {
                _variables.Add(updated);
            }

            applied++;
        }

        if (applied == 0)
        {
            AppMessageBox.Show(
                "Choose a target variable for at least one row first.",
                "Sync from AWS");

            return;
        }

        AppMessageBox.Show(
            $"{applied} variable(s) updated. Click Save in the environment editor to keep them.",
            "Sync from AWS");

        Close();
    }

    private void
    CancelButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }
}

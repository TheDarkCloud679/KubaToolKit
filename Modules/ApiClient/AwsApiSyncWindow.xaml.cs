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
    private readonly List<string> _comboSuggestions;
    private readonly HashSet<string> _existingVariableNames;
    private List<DiscoveredApiEndpoint> _allDiscovered = new();

    public AwsApiSyncWindow(
        ObservableCollection<HeaderItem> variables,
        HashSet<string>? collectionVariables = null)
    {
        InitializeComponent();

        _variables = variables;

        _existingVariableNames =
            new HashSet<string>(
                _variables
                    .Select(v => v.Key)
                    .Where(k => !string.IsNullOrWhiteSpace(k)),
                StringComparer.Ordinal);

        // A variable the collection references but that's either absent
        // here or sitting empty -- these are what "Show only missing"
        // narrows the discovered list down to, and what the "Maps to"
        // suggestions lead with, so the names actually needed are the
        // easiest ones to pick.
        var missingVariableNames =
            (collectionVariables ?? new HashSet<string>())
                .Where(name =>
                    !_variables.Any(v =>
                        v.Key == name
                        && v.Enabled
                        && !string.IsNullOrWhiteSpace(v.Value)))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

        _comboSuggestions =
            missingVariableNames
                .Concat(
                    _existingVariableNames
                        .Except(missingVariableNames, StringComparer.Ordinal)
                        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                .ToList();

        if (missingVariableNames.Count > 0)
        {
            const int maxShown = 12;

            var shown = string.Join(", ", missingVariableNames.Take(maxShown));

            var suffix =
                missingVariableNames.Count > maxShown
                    ? $", and {missingVariableNames.Count - maxShown} more"
                    : "";

            MissingVariablesText.Text =
                $"Collection needs {missingVariableNames.Count} variable(s) not set here yet: {shown}{suffix}.";

            MissingVariablesText.Visibility = Visibility.Visible;
        }

        ResultsGrid.ItemsSource = _results;

        var dominantRegion = GetDominantRegionSegment();

        if (dominantRegion != null)
        {
            StatusText.Text = $"This environment's URLs mostly point to \"{dominantRegion}\".";
        }

        var chain = new CredentialProfileStoreChain();

        ProfileCombo.ItemsSource =
            chain.ListProfiles()
                .Select(p => p.Name)
                .Where(n => n != "default")
                .OrderBy(n => n)
                .ToList();

        // Set here, not via IsChecked="True" in XAML -- that fires the
        // Checked handler during InitializeComponent(), before _variables
        // (assigned further up in this same constructor, but after that
        // call) exists yet, and ApplyFilter() reading it null-refs.
        ShowOnlyMissingCheckBox.IsChecked = true;
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
        ((ComboBox)sender).ItemsSource = _comboSuggestions;
    }

    private void
    ShowOnlyMissingCheckBox_Changed(
        object sender,
        RoutedEventArgs e)
    {
        ApplyFilter();
    }

    // "Missing" = this endpoint's URL isn't already the value of some
    // variable in this environment -- a plain URL comparison, not a guess
    // at which AWS API name corresponds to which variable name (nothing
    // reliably ties "Contract-API-DijonProd" to "contractUrl" besides the
    // user picking it in "Maps to"). Checking the box hides endpoints
    // that are already represented somewhere, whatever they're called.
    private void
    ApplyFilter()
    {
        var existingUrls =
            new HashSet<string>(
                _variables
                    .Where(v => v.Enabled && !string.IsNullOrWhiteSpace(v.Value))
                    .Select(v => v.Value.Trim()),
                StringComparer.OrdinalIgnoreCase);

        var source =
            ShowOnlyMissingCheckBox.IsChecked == true
                ? _allDiscovered.Where(e => !existingUrls.Contains(e.Url))
                : _allDiscovered;

        _results.Clear();

        foreach (var endpoint in source)
        {
            _results.Add(endpoint);
        }
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
        _allDiscovered = new List<DiscoveredApiEndpoint>();

        try
        {
            var endpoints =
                await _awsService.DiscoverEndpoints(profile);

            foreach (var endpoint in endpoints)
            {
                endpoint.ExistingVariableNames = _existingVariableNames;
            }

            _allDiscovered = endpoints;

            ApplyFilter();

            StatusText.Text =
                endpoints.Count == 0
                    ? "No API Gateway custom domain mappings found for this profile."
                    : $"{endpoints.Count} endpoint(s) found, {_results.Count} shown.";
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

    // For a host like "user.api.dkr.kubapay.io" or "api.nam.kubapay.io",
    // the label right before the last two ("dkr", "nam") is this
    // deployment's region/project marker in every domain seen so far,
    // regardless of how many subdomain levels precede it. Used only to
    // flag a likely mix-up, not to validate URLs generally -- a host with
    // fewer than 3 labels (no marker position) is simply not checked.
    private static string?
    ExtractRegionSegment(
        string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var labels = uri.Host.Split('.');

        return labels.Length >= 3 ? labels[^3] : null;
    }

    // The region/project marker already dominant among this
    // environment's own URL-valued variables -- what a newly applied
    // mapping gets compared against to catch exactly the mistake that
    // motivated this check: syncing from one region/project's AWS
    // account while a different region's environment is the one open,
    // silently mixing their URLs together.
    private string?
    GetDominantRegionSegment()
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var variable in _variables)
        {
            if (!variable.Enabled)
            {
                continue;
            }

            var region = ExtractRegionSegment(variable.Value);

            if (region == null)
            {
                continue;
            }

            counts[region] = counts.GetValueOrDefault(region) + 1;
        }

        return counts.Count == 0
            ? null
            : counts.OrderByDescending(kv => kv.Value).First().Key;
    }

    private void
    ApplyButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var toApply =
            _results
                .Select(row => (row, name: row.MapsTo?.Trim()))
                .Where(x => !string.IsNullOrWhiteSpace(x.name))
                .ToList();

        if (toApply.Count == 0)
        {
            AppMessageBox.Show(
                "Choose a target variable for at least one row first.",
                "Sync from AWS");

            return;
        }

        var dominantRegion = GetDominantRegionSegment();

        if (dominantRegion != null)
        {
            var mismatches =
                toApply
                    .Select(x => (x.row, x.name, region: ExtractRegionSegment(x.row.Url)))
                    .Where(x => x.region != null
                        && !string.Equals(x.region, dominantRegion, StringComparison.OrdinalIgnoreCase))
                    .ToList();

            if (mismatches.Count > 0)
            {
                var lines =
                    mismatches.Select(x => $"{x.name} -> {x.row.Url}  (\"{x.region}\", not \"{dominantRegion}\")");

                var confirmed =
                    AppMessageBox.Show(
                        $"This environment's URLs mostly point to \"{dominantRegion}\", but "
                        + $"{mismatches.Count} mapping(s) about to be applied point elsewhere -- easy to "
                        + "mix up when syncing from a different region/project's AWS account while this "
                        + "environment happens to be the one open:\n\n"
                        + string.Join("\n", lines)
                        + "\n\nApply anyway?",
                        "Different region/project detected",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning)
                    == MessageBoxResult.Yes;

                if (!confirmed)
                {
                    return;
                }
            }
        }

        var applied = 0;

        foreach (var (row, name) in toApply)
        {
            var existing = _variables.FirstOrDefault(v => v.Key == name);

            var updated =
                new HeaderItem { Enabled = true, Key = name!, Value = row.Url };

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

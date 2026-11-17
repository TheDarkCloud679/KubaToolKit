using System.ComponentModel;

namespace KubaToolKit.Modules.ApiClient.Models;

// One row of what AwsApiGatewayService.DiscoverEndpoints found -- a
// custom domain mapped to a REST API's stage, with the full URL that
// mapping produces. MapsTo and StatusText are purely local UI state
// (no AWS-side or saved mapping yet -- that's the later "adaptive" step),
// set by the user in AwsApiSyncWindow before applying.
public class DiscoveredApiEndpoint
    : INotifyPropertyChanged
{
    public string ApiName { get; set; } = "";
    public string ApiId { get; set; } = "";
    public string Stage { get; set; } = "";
    public string Domain { get; set; } = "";
    public string BasePath { get; set; } = "";
    public string Url { get; set; } = "";

    // Shared across all rows by the window, so each row's StatusText can
    // tell "this will update an existing variable" from "this will add a
    // new one" as soon as the user picks a target.
    public HashSet<string> ExistingVariableNames { get; set; } = new(StringComparer.Ordinal);

    private string _mapsTo = "";

    public string MapsTo
    {
        get => _mapsTo;
        set
        {
            if (_mapsTo == value)
            {
                return;
            }

            _mapsTo = value;

            OnPropertyChanged(nameof(MapsTo));
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public string StatusText =>
        string.IsNullOrWhiteSpace(MapsTo)
            ? ""
            : ExistingVariableNames.Contains(MapsTo.Trim())
                ? "Will update"
                : "Will create";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

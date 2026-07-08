using KubaToolKit.Modules.ApiClient.Models;
using KubaToolKit.Shared.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace KubaToolKit.Modules.ApiClient;

public partial class ApiClientView
    : UserControl
{
    private readonly ApiClientService _apiClientService = new();
    private readonly CollectionStorageService _collectionStorage = new();
    private readonly ObservableCollection<HeaderItem> _headers = new();
    private readonly ObservableCollection<HeaderItem> _params = new();
    private readonly ObservableCollection<HeaderItem> _autoHeaders = new();
    private readonly ObservableCollection<CollectionNode> _collections = new();
    private CancellationTokenSource? _sendCancellation;
    private bool _syncingUrlAndParams;
    private bool _collectionsSortDescending;
    private bool _showAutoHeaders = true;

    public ApiClientView()
    {
        InitializeComponent();

        HeadersGrid.ItemsSource = _headers;
        ParamsGrid.ItemsSource = _params;
        AutoHeadersGrid.ItemsSource = _autoHeaders;
        CollectionsTreeView.ItemsSource = _collections;

        _headers.Add(
            new HeaderItem { Key = "Content-Type", Value = "application/json" });

        _headers.CollectionChanged += (_, __) => RefreshAutoHeaders();

        UrlTextBox.TextChanged += (_, __) => SyncParamsFromUrl();
        UrlTextBox.TextChanged += (_, __) => RefreshAutoHeaders();

        LoadCollectionsAndEnvironments();

        RefreshAutoHeaders();
    }

    private void
    AddParamRow_Click(
        object sender,
        RoutedEventArgs e)
    {
        _params.Add(new HeaderItem());
    }

    private void
    AddHeaderRow_Click(
        object sender,
        RoutedEventArgs e)
    {
        _headers.Add(new HeaderItem());
    }

    private void
    HeadersGrid_LostFocus(
        object sender,
        RoutedEventArgs e)
    {
        RefreshAutoHeaders();
    }

    private void
    MethodCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        RefreshAutoHeaders();
    }

    private void
    BodyTextBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        RefreshAutoHeaders();
    }

    private void
    ToggleAutoHeaders_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        _showAutoHeaders = !_showAutoHeaders;

        AutoHeadersGrid.Visibility =
            _showAutoHeaders ? Visibility.Visible : Visibility.Collapsed;

        ToggleAutoHeadersText.Text =
            _showAutoHeaders
                ? "Masquer les en-têtes générés automatiquement"
                : "Afficher les en-têtes générés automatiquement";
    }

    /// Aperçu des en-têtes que KubaToolKit ajoute lui-même à l'envoi
    /// (User-Agent, Accept, Accept-Encoding via HttpClient, Host,
    /// Content-Length) quand l'utilisateur ne les a pas déjà définis
    /// explicitement dans la grille Headers.
    private void
    RefreshAutoHeaders()
    {
        if (HeadersGrid == null)
        {
            return;
        }

        _autoHeaders.Clear();

        bool Has(string key) =>
            _headers.Any(h =>
                h.Enabled
                && string.Equals(h.Key, key, StringComparison.OrdinalIgnoreCase));

        var method =
            (MethodCombo?.SelectedItem as ComboBoxItem)?.Content as string
            ?? "GET";

        bool hasBody =
            !string.IsNullOrEmpty(BodyTextBox?.Text)
            && ApiClientService.AllowsBody(method);

        if (hasBody && !Has("Content-Type"))
        {
            _autoHeaders.Add(
                new HeaderItem { Key = "Content-Type", Value = "application/json" });
        }

        if (hasBody)
        {
            _autoHeaders.Add(
                new HeaderItem { Key = "Content-Length", Value = "<calculé à l'envoi>" });
        }

        if (!Has("Host"))
        {
            var host =
                Uri.TryCreate(UrlTextBox?.Text, UriKind.Absolute, out var uri)
                    ? uri.Host
                    : null;

            _autoHeaders.Add(
                new HeaderItem { Key = "Host", Value = host ?? "<calculé à l'envoi>" });
        }

        if (!Has("User-Agent"))
        {
            _autoHeaders.Add(
                new HeaderItem { Key = "User-Agent", Value = "KubaToolKit/1.0" });
        }

        if (!Has("Accept"))
        {
            _autoHeaders.Add(
                new HeaderItem { Key = "Accept", Value = "*/*" });
        }

        if (!Has("Accept-Encoding"))
        {
            _autoHeaders.Add(
                new HeaderItem { Key = "Accept-Encoding", Value = "gzip, deflate, br" });
        }

        if (!Has("Connection"))
        {
            _autoHeaders.Add(
                new HeaderItem { Key = "Connection", Value = "keep-alive" });
        }
    }

    private void
    CollectionsHeader_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
        {
            return;
        }

        _collectionsSortDescending = !_collectionsSortDescending;

        SortNodes(_collections, _collectionsSortDescending);
    }

    private static void
    SortNodes(
        ObservableCollection<CollectionNode> nodes,
        bool descending)
    {
        var sorted =
            descending
                ? nodes.OrderByDescending(n => n.Name, StringComparer.OrdinalIgnoreCase).ToList()
                : nodes.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ToList();

        nodes.Clear();

        foreach (var node in sorted)
        {
            nodes.Add(node);

            SortNodes(node.Children, descending);
        }
    }

    /// Reconstruit la grille Params à partir de la chaîne de requête de
    /// l'URL (Postman : les deux vues restent synchronisées).
    private void
    SyncParamsFromUrl()
    {
        if (_syncingUrlAndParams)
        {
            return;
        }

        _syncingUrlAndParams = true;

        try
        {
            _params.Clear();

            var url = UrlTextBox.Text;
            var queryIndex = url.IndexOf('?');

            if (queryIndex < 0 || queryIndex == url.Length - 1)
            {
                return;
            }

            foreach (var pair in url[(queryIndex + 1)..]
                         .Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=', 2);

                _params.Add(
                    new HeaderItem
                    {
                        Enabled = true,
                        Key = Uri.UnescapeDataString(parts[0]),
                        Value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : ""
                    });
            }
        }
        finally
        {
            _syncingUrlAndParams = false;
        }
    }

    /// Reconstruit la chaîne de requête de l'URL à partir de la grille
    /// Params (lignes activées et avec une clé non vide seulement).
    private void
    SyncUrlFromParams()
    {
        if (_syncingUrlAndParams)
        {
            return;
        }

        _syncingUrlAndParams = true;

        try
        {
            var baseUrl = UrlTextBox.Text.Split('?')[0];

            var enabledParams =
                _params
                    .Where(p => p.Enabled && !string.IsNullOrWhiteSpace(p.Key))
                    .ToList();

            UrlTextBox.Text =
                enabledParams.Count == 0
                    ? baseUrl
                    : $"{baseUrl}?{string.Join(
                        "&",
                        enabledParams.Select(p =>
                            $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value ?? "")}"))}";
        }
        finally
        {
            _syncingUrlAndParams = false;
        }
    }

    private void
    ParamsGrid_LostFocus(
        object sender,
        RoutedEventArgs e)
    {
        SyncUrlFromParams();
    }

    private void
    LoadCollectionsAndEnvironments(
        string? reselectEnvironmentName = null)
    {
        reselectEnvironmentName ??=
            (EnvironmentCombo.SelectedItem as EnvironmentSet)?.Name;

        try
        {
            _collections.Clear();

            foreach (var root in _collectionStorage.LoadCollections())
            {
                _collections.Add(root);
            }

            _collectionsSortDescending = false;
            SortNodes(_collections, _collectionsSortDescending);

            var environments =
                new List<EnvironmentSet>
                {
                    new() { Name = "(No Environment)" }
                };

            environments.AddRange(
                _collectionStorage.LoadEnvironments());

            EnvironmentCombo.ItemsSource = environments;

            var toReselect =
                reselectEnvironmentName != null
                    ? environments.FirstOrDefault(env =>
                        env.Name == reselectEnvironmentName)
                    : null;

            EnvironmentCombo.SelectedItem = toReselect ?? environments[0];
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Collections loading error");
        }
    }

    private void
    ReloadCollections_Click(
        object sender,
        RoutedEventArgs e)
    {
        LoadCollectionsAndEnvironments();
    }

    private void
    OpenCollectionsFolder_Click(
        object sender,
        RoutedEventArgs e)
    {
        _collectionStorage.EnsureFoldersExist();

        Process.Start(
            new ProcessStartInfo
            {
                FileName = CollectionStorageService.RootFolder,
                UseShellExecute = true
            });
    }

    private void
    AddEnvironment_Click(
        object sender,
        RoutedEventArgs e)
    {
        _collectionStorage.EnsureFoldersExist();

        Process.Start(
            new ProcessStartInfo
            {
                FileName = CollectionStorageService.EnvironmentsFolder,
                UseShellExecute = true
            });
    }

    private void
    ReloadEnvironments_Click(
        object sender,
        RoutedEventArgs e)
    {
        LoadCollectionsAndEnvironments();
    }

    private void
    EditEnvironment_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (EnvironmentCombo.SelectedItem is not EnvironmentSet environment
            || string.IsNullOrEmpty(environment.FilePath))
        {
            MessageBox.Show(
                "Sélectionner d'abord un environnement (ou en ajouter un via \"+\").",
                "Aucun environnement sélectionné");

            return;
        }

        var editor =
            new EnvironmentEditorWindow(_collectionStorage, environment);

        editor.Owner = Window.GetWindow(this);

        editor.ShowDialog();

        if (editor.Saved)
        {
            LoadCollectionsAndEnvironments(environment.Name);
        }
    }

    private void
    CollectionsTreeView_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        if (CollectionsTreeView.SelectedItem is not CollectionNode node
            || !node.IsRequest)
        {
            return;
        }

        foreach (ComboBoxItem item in MethodCombo.Items)
        {
            if (string.Equals(
                    item.Content as string,
                    node.Method,
                    StringComparison.OrdinalIgnoreCase))
            {
                MethodCombo.SelectedItem = item;
                break;
            }
        }

        // Déclenche TextChanged -> SyncParamsFromUrl() automatiquement.
        UrlTextBox.Text = node.Url;

        _headers.Clear();

        foreach (var header in node.Headers)
        {
            _headers.Add(header);
        }

        BodyTextBox.Text = node.Body;
    }

    private void
    AuthTypeCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (BearerAuthPanel == null
            || BasicAuthPanel == null
            || ApiKeyAuthPanel == null)
        {
            return;
        }

        BearerAuthPanel.Visibility = Visibility.Collapsed;
        BasicAuthPanel.Visibility = Visibility.Collapsed;
        ApiKeyAuthPanel.Visibility = Visibility.Collapsed;

        switch (AuthTypeCombo.SelectedIndex)
        {
            case 1:
                BearerAuthPanel.Visibility = Visibility.Visible;
                break;

            case 2:
                BasicAuthPanel.Visibility = Visibility.Visible;
                break;

            case 3:
                ApiKeyAuthPanel.Visibility = Visibility.Visible;
                break;
        }
    }

    private async void
    UrlTextBox_KeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        await SendAsync();
    }

    private async void
    SendButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        await SendAsync();
    }

    public async Task
    SendAsync()
    {
        var url = UrlTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(url))
        {
            MessageBox.Show(
                "Entrer une URL");

            return;
        }

        var method =
            (MethodCombo.SelectedItem as ComboBoxItem)?.Content as string
            ?? "GET";

        // Les DataGrid gardent une ligne d'édition en cours (vide) tant
        // qu'elles n'ont pas perdu le focus ; on force leur validation
        // pour qu'elle soit bien incluse dans _headers/_params.
        HeadersGrid.CommitEdit(DataGridEditingUnit.Row, true);
        ParamsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        SyncUrlFromParams();

        url = UrlTextBox.Text.Trim();

        try
        {
            StatusBadge.Visibility = Visibility.Collapsed;
            TimingTextBlock.Text = "";
            SendProgressBar.Visibility = Visibility.Visible;
            SendButton.IsEnabled = false;

            _sendCancellation?.Cancel();
            _sendCancellation = new CancellationTokenSource();

            var auth = BuildAuthConfig();

            var variables =
                (EnvironmentCombo.SelectedItem as EnvironmentSet)?.ToSubstitutionMap();

            var result =
                await _apiClientService.SendAsync(
                    method,
                    url,
                    _headers.ToList(),
                    BodyTextBox.Text,
                    auth,
                    variables,
                    _sendCancellation.Token);

            StatusTextBlock.Text = result.StatusDisplay;
            StatusBadge.Background = result.StatusBackground;
            StatusBadge.Visibility = Visibility.Visible;
            TimingTextBlock.Text = $"{result.ElapsedMs} ms • {result.Body.Length:N0} chars";

            ResponseHeadersTextBox.Text = result.Headers;

            LoadResponseBody(result.Body);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ResponseHeadersTextBox.Text = "";
            ResponseBodyEditor.Text = "";
            MessageBox.Show(
                ex.Message,
                "Request error");
        }
        finally
        {
            SendProgressBar.Visibility = Visibility.Collapsed;
            SendButton.IsEnabled = true;
        }
    }

    private void
    LoadResponseBody(
        string body)
    {
        ResponseBodyEditor.Text =
            JsonFormattingHelper.FormatJson(body);

        ResponseBodyEditor.TextArea
            .TextView
            .LineTransformers
            .Clear();

        ResponseBodyEditor.TextArea
            .TextView
            .LineTransformers
            .Add(new JsonFormattingHelper.JsonColorizer());

        ResponseBodyEditor.TextArea
            .TextView
            .Redraw();
    }

    private AuthConfig
    BuildAuthConfig()
    {
        var type = AuthTypeCombo.SelectedIndex switch
        {
            1 => AuthType.Bearer,
            2 => AuthType.Basic,
            3 => AuthType.ApiKey,
            _ => AuthType.None
        };

        return new AuthConfig
        {
            Type = type,
            BearerToken = BearerTokenTextBox.Text,
            Username = BasicUsernameTextBox.Text,
            Password = BasicPasswordBox.Password,
            ApiKeyName = ApiKeyNameTextBox.Text,
            ApiKeyValue = ApiKeyValueTextBox.Text
        };
    }
}

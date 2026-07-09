using KubaToolKit.Modules.ApiClient.Models;
using KubaToolKit.Shared.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace KubaToolKit.Modules.ApiClient;

public partial class ApiClientView
    : UserControl
{
    private readonly ApiClientService _apiClientService = new();
    private readonly CollectionStorageService _collectionStorage = new();
    private readonly ObservableCollection<HeaderItem> _headers = new();
    private readonly ObservableCollection<HeaderItem> _params = new();
    private readonly ObservableCollection<HeaderItem> _autoHeaders = new();
    private readonly ObservableCollection<HeaderItem> _bodyFormData = new();
    private readonly ObservableCollection<HeaderItem> _bodyUrlEncoded = new();
    private readonly ObservableCollection<HeaderItem> _extractions = new();
    private readonly ObservableCollection<CollectionNode> _collections = new();
    private CancellationTokenSource? _sendCancellation;
    private bool _syncingUrlAndParams;
    private bool _collectionsSortDescending;
    private bool _showAutoHeaders = true;
    private string? _binaryFilePath;
    private string _lastResponseBody = "";

    // Le nœud dont le contenu est actuellement chargé dans l'éditeur (null
    // pour une requête ad hoc jamais enregistrée) : sert à résoudre "Inherit
    // auth from parent" en remontant node.Parent.
    private CollectionNode? _currentRequestNode;

    public ApiClientView()
    {
        Logger.Debug("ApiClientView: InitializeComponent.");

        InitializeComponent();

        HeadersGrid.ItemsSource = _headers;
        ParamsGrid.ItemsSource = _params;
        AutoHeadersGrid.ItemsSource = _autoHeaders;
        BodyFormGrid.ItemsSource = _bodyFormData;
        ExtractionsGrid.ItemsSource = _extractions;
        CollectionsTreeView.ItemsSource = _collections;

        _headers.CollectionChanged += (_, __) => RefreshAutoHeaders();
        _bodyFormData.CollectionChanged += (_, __) => RefreshAutoHeaders();
        _bodyUrlEncoded.CollectionChanged += (_, __) => RefreshAutoHeaders();

        UrlTextBox.TextChanged += (_, __) => SyncParamsFromUrl();
        UrlTextBox.TextChanged += (_, __) => RefreshAutoHeaders();

        LoadCollectionsAndEnvironments();

        RefreshAutoHeaders();

        Logger.Debug("ApiClientView: constructeur terminé.");
    }

    // Utilise des accès null-conditionnels : lors du parsing XAML initial,
    // le Checked du premier RadioButton coché peut se déclencher avant que
    // ses frères déclarés plus loin dans le fichier (ex. BodyGraphQlRadio)
    // n'existent encore.
    private string
    GetSelectedBodyMode()
    {
        if (BodyNoneRadio?.IsChecked == true) return "none";
        if (BodyFormDataRadio?.IsChecked == true) return "formdata";
        if (BodyUrlEncodedRadio?.IsChecked == true) return "urlencoded";
        if (BodyBinaryRadio?.IsChecked == true) return "binary";
        if (BodyGraphQlRadio?.IsChecked == true) return "graphql";
        return "raw";
    }

    private string
    GetRawContentType() =>
        ((RawContentTypeCombo?.SelectedItem as ComboBoxItem)?.Content as string) switch
        {
            "Text" => "text/plain",
            "JavaScript" => "application/javascript",
            "HTML" => "text/html",
            "XML" => "application/xml",
            _ => "application/json"
        };

    private void
    BodyMode_Checked(
        object sender,
        RoutedEventArgs e)
    {
        if (BodyNonePanel == null)
        {
            return;
        }

        var mode = GetSelectedBodyMode();

        BodyNonePanel.Visibility =
            mode == "none" ? Visibility.Visible : Visibility.Collapsed;

        BodyFormPanel.Visibility =
            mode is "formdata" or "urlencoded" ? Visibility.Visible : Visibility.Collapsed;

        BodyRawPanel.Visibility =
            mode == "raw" ? Visibility.Visible : Visibility.Collapsed;

        BodyBinaryPanel.Visibility =
            mode == "binary" ? Visibility.Visible : Visibility.Collapsed;

        BodyGraphQlPanel.Visibility =
            mode == "graphql" ? Visibility.Visible : Visibility.Collapsed;

        if (mode == "formdata")
        {
            BodyFormGrid.ItemsSource = _bodyFormData;
        }
        else if (mode == "urlencoded")
        {
            BodyFormGrid.ItemsSource = _bodyUrlEncoded;
        }

        RefreshAutoHeaders();
    }

    private void
    AddBodyFormRow_Click(
        object sender,
        RoutedEventArgs e)
    {
        (GetSelectedBodyMode() == "urlencoded" ? _bodyUrlEncoded : _bodyFormData)
            .Add(new HeaderItem());
    }

    private void
    AddExtractionRow_Click(
        object sender,
        RoutedEventArgs e)
    {
        _extractions.Add(new HeaderItem());
    }

    private void
    SelectBinaryFile_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog();

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _binaryFilePath = dialog.FileName;
        BinaryFilePathText.Text = dialog.FileName;

        RefreshAutoHeaders();
    }

    private void
    RawContentTypeCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        RefreshAutoHeaders();
    }

    private void
    GraphQlQueryTextBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
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

    /// Bouton "✕" partagé par Params/Headers/BodyFormGrid : retire la
    /// ligne cliquée de la collection actuellement liée à SA grille (peu
    /// importe laquelle), pas besoin d'un handler par grille.
    private void
    DeleteHeaderItemRow_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button button
            || button.DataContext is not HeaderItem item)
        {
            return;
        }

        if (DataGridSortHelper.FindAncestor<DataGrid>(button) is not { } grid)
        {
            return;
        }

        (grid.ItemsSource as ObservableCollection<HeaderItem>)?.Remove(item);

        if (grid == HeadersGrid)
        {
            RefreshAutoHeaders();
        }
        else if (grid == ParamsGrid)
        {
            // Sans ça, l'URL garde le paramètre supprimé et
            // SyncParamsFromUrl le réinjecterait au prochain changement
            // d'URL (les deux vues restent normalement synchronisées).
            SyncUrlFromParams();
        }
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

        AutoHeadersGridContainer.Visibility =
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

        // Reflète l'Authorization réellement envoyée (onglet Auth de la
        // requête, ou celle héritée du dossier/collection parent), comme
        // Postman qui l'affiche calculée plutôt que de la faire saisir
        // dans la grille Headers elle-même.
        var resolvedAuth = ResolveAuthConfigForSend();

        // Dès qu'un type d'auth concret est résolu (pas None/Inherit non
        // résolu), la ligne apparaît toujours, même si le token/mot de
        // passe est encore vide au moment de l'aperçu — comme Postman, qui
        // l'affiche avec une valeur "tentative" plutôt que de l'omettre.
        switch (resolvedAuth.Type)
        {
            case AuthType.Bearer:

                if (!Has("Authorization"))
                {
                    var value =
                        string.IsNullOrWhiteSpace(resolvedAuth.BearerToken)
                            ? "Bearer <calculé à l'envoi>"
                            : "Bearer " + Mask(resolvedAuth.BearerToken);

                    _autoHeaders.Add(new HeaderItem { Key = "Authorization", Value = value });
                }

                break;

            case AuthType.Basic:

                if (!Has("Authorization"))
                {
                    var value =
                        string.IsNullOrWhiteSpace(resolvedAuth.Username)
                        && string.IsNullOrEmpty(resolvedAuth.Password)
                            ? "Basic <calculé à l'envoi>"
                            : "Basic " + Mask($"{resolvedAuth.Username}:{resolvedAuth.Password}");

                    _autoHeaders.Add(new HeaderItem { Key = "Authorization", Value = value });
                }

                break;

            case AuthType.ApiKey:

                var keyName =
                    string.IsNullOrWhiteSpace(resolvedAuth.ApiKeyName)
                        ? "Authorization"
                        : resolvedAuth.ApiKeyName;

                if (!Has(keyName))
                {
                    var value =
                        string.IsNullOrEmpty(resolvedAuth.ApiKeyValue)
                            ? "<calculé à l'envoi>"
                            : Mask(resolvedAuth.ApiKeyValue);

                    _autoHeaders.Add(new HeaderItem { Key = keyName, Value = value });
                }

                break;
        }

        var method =
            (MethodCombo?.SelectedItem as ComboBoxItem)?.Content as string
            ?? "GET";

        var mode = GetSelectedBodyMode();

        bool hasBody =
            ApiClientService.AllowsBody(method)
            && mode switch
            {
                "none" => false,
                "formdata" => _bodyFormData.Any(f => f.Enabled && !string.IsNullOrWhiteSpace(f.Key)),
                "urlencoded" => _bodyUrlEncoded.Any(f => f.Enabled && !string.IsNullOrWhiteSpace(f.Key)),
                "binary" => !string.IsNullOrWhiteSpace(_binaryFilePath),
                "graphql" => !string.IsNullOrWhiteSpace(GraphQlQueryTextBox?.Text),
                _ => !string.IsNullOrEmpty(BodyTextBox?.Text)
            };

        if (hasBody && !Has("Content-Type"))
        {
            var contentType = mode switch
            {
                "formdata" => "multipart/form-data; boundary=<calculé à l'envoi>",
                "urlencoded" => "application/x-www-form-urlencoded",
                "binary" => "application/octet-stream",
                "graphql" => "application/json",
                _ => GetRawContentType()
            };

            _autoHeaders.Add(
                new HeaderItem { Key = "Content-Type", Value = contentType });
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

    private static string
    Mask(
        string? value) =>
        string.IsNullOrEmpty(value) ? "" : new string('•', Math.Clamp(value.Length, 8, 24));

    private void
    AuthField_Changed(
        object sender,
        RoutedEventArgs e)
    {
        RefreshAutoHeaders();
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

    /// Les favoris restent toujours en tête de leur fratrie, quel que soit
    /// le sens du tri ; seul l'ordre alphabétique à l'intérieur de chaque
    /// groupe (favoris / non-favoris) suit le sens demandé.
    private static void
    SortNodes(
        ObservableCollection<CollectionNode> nodes,
        bool descending)
    {
        var byFavorite = nodes.OrderByDescending(n => n.IsFavorite);

        var sorted =
            (descending
                ? byFavorite.ThenByDescending(n => n.Name, StringComparer.OrdinalIgnoreCase)
                : byFavorite.ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();

        nodes.Clear();

        foreach (var node in sorted)
        {
            nodes.Add(node);

            SortNodes(node.Children, descending);
        }
    }

    /// Insère, en tête de chaque collection racine, un pseudo-dossier
    /// "Favoris" regroupant (mêmes références, pas de copie) toutes les
    /// requêtes marquées favorites où qu'elles soient nichées dans cette
    /// collection, triées par ordre alphabétique. Purement visuel : ne
    /// touche ni Parent ni le fichier .json (voir CollectionStorageService,
    /// qui l'ignore explicitement à la sauvegarde).
    private void
    RebuildFavoritesFolders()
    {
        foreach (var root in _collections)
        {
            if (root.Children.Count > 0
                && root.Children[0].IsFavoritesFolder)
            {
                root.Children.RemoveAt(0);
            }

            var favorites =
                CollectFavorites(root)
                    .OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

            if (favorites.Count == 0)
            {
                continue;
            }

            var favoritesFolder =
                new CollectionNode
                {
                    Name = "Favoris",
                    IsRequest = false,
                    IsFavorite = true,
                    IsFavoritesFolder = true
                };

            foreach (var favorite in favorites)
            {
                favoritesFolder.Children.Add(favorite);
            }

            root.Children.Insert(0, favoritesFolder);
        }
    }

    private static IEnumerable<CollectionNode>
    CollectFavorites(
        CollectionNode node)
    {
        foreach (var child in node.Children)
        {
            if (child.IsFavoritesFolder)
            {
                continue;
            }

            if (child.IsRequest && child.IsFavorite)
            {
                yield return child;
            }

            foreach (var nested in CollectFavorites(child))
            {
                yield return nested;
            }
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
            RebuildFavoritesFolders();

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
            Logger.Error("ApiClientView: échec du chargement des collections/environnements.", ex);

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

        _currentRequestNode = node;

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

        _bodyFormData.Clear();
        _bodyUrlEncoded.Clear();
        _binaryFilePath = null;
        BinaryFilePathText.Text = "Aucun fichier sélectionné";
        GraphQlQueryTextBox.Text = "";
        GraphQlVariablesTextBox.Text = "";

        switch (node.BodyMode)
        {
            case "none":
                BodyNoneRadio.IsChecked = true;
                break;

            case "formdata":
                foreach (var field in node.BodyFormData) _bodyFormData.Add(field);
                BodyFormDataRadio.IsChecked = true;
                break;

            case "urlencoded":
                foreach (var field in node.BodyFormData) _bodyUrlEncoded.Add(field);
                BodyUrlEncodedRadio.IsChecked = true;
                break;

            default:
                BodyRawRadio.IsChecked = true;
                break;
        }

        BearerTokenTextBox.Text = node.Auth.BearerToken;
        BasicUsernameTextBox.Text = node.Auth.Username;
        BasicPasswordBox.Password = node.Auth.Password;
        ApiKeyNameTextBox.Text = string.IsNullOrEmpty(node.Auth.ApiKeyName) ? "X-API-Key" : node.Auth.ApiKeyName;
        ApiKeyValueTextBox.Text = node.Auth.ApiKeyValue;

        _extractions.Clear();

        foreach (var extraction in node.PostResponseExtractions)
        {
            _extractions.Add(extraction);
        }

        AuthTypeCombo.SelectedIndex = node.Auth.Type switch
        {
            AuthType.None => 1,
            AuthType.Bearer => 2,
            AuthType.Basic => 3,
            AuthType.ApiKey => 4,
            _ => 0
        };

        // Ne pas dépendre uniquement des événements Checked/SelectionChanged
        // déclenchés ci-dessus : si la requête chargée a la même méthode,
        // le même mode de body ou le même type d'auth que la précédente,
        // ils ne se redéclenchent pas (la valeur ne change pas), et
        // l'aperçu resterait basé sur l'état d'avant le chargement.
        RefreshAutoHeaders();
    }

    /// Un clic droit ne sélectionne pas le TreeViewItem visé par défaut en
    /// WPF : sans ça, le menu contextuel agirait sur l'élément sélectionné
    /// précédemment plutôt que celui sous le curseur.
    private void
    CollectionsTreeView_PreviewMouseRightButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (DataGridSortHelper.FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject)
            is { } item)
        {
            item.IsSelected = true;
            item.Focus();
        }
    }

    private void
    CollectionsContextMenu_Opened(
        object sender,
        RoutedEventArgs e)
    {
        var node = CollectionsTreeView.SelectedItem as CollectionNode;
        var isRequest = node?.IsRequest == true;
        var isRealNode = node != null && node.IsFavoritesFolder != true;
        var isFolder = isRealNode && !isRequest;

        // Le pseudo-dossier "Favoris" n'est pas un vrai nœud : rien n'y
        // est modifiable directement (les requêtes qu'il liste le restent
        // depuis leur vrai dossier, ou via ce même menu puisque ce sont
        // les mêmes objets).
        AddRequestMenuItem.IsEnabled = isFolder;
        AddFolderMenuItem.IsEnabled = isFolder;
        UpdateRequestMenuItem.IsEnabled = isRequest;
        RenameMenuItem.IsEnabled = isRealNode;
        DeleteMenuItem.IsEnabled = isRealNode;

        FavoriteMenuItem.IsEnabled = isRequest;
        FavoriteMenuItem.Header =
            node?.IsFavorite == true
                ? "★ Retirer des favoris"
                : "☆ Ajouter aux favoris";

        // Pour une requête, l'onglet Auth de l'éditeur suffit déjà ; ce
        // menu ne sert qu'à définir l'auth partagée d'un dossier/collection.
        FolderAuthMenuItem.IsEnabled = isFolder;
        ApplyBearerToAllMenuItem.IsEnabled = isFolder;
    }

    private void
    ToggleFavorite_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (CollectionsTreeView.SelectedItem is not CollectionNode node
            || !node.IsRequest)
        {
            return;
        }

        node.IsFavorite = !node.IsFavorite;

        // Fait remonter/redescendre immédiatement le nœud dans sa fratrie,
        // et met à jour le pseudo-dossier "Favoris" en tête de sa
        // collection, plutôt que d'attendre un rechargement.
        SortNodes(_collections, _collectionsSortDescending);
        RebuildFavoritesFolders();

        SaveCollectionOf(node);
    }

    private void
    EditFolderAuth_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (CollectionsTreeView.SelectedItem is not CollectionNode node
            || node.IsRequest)
        {
            return;
        }

        var editor =
            new FolderAuthWindow(node)
            {
                Owner = Window.GetWindow(this)
            };

        editor.ShowDialog();

        if (editor.Saved)
        {
            SaveCollectionOf(node);
            RefreshAutoHeaders();
        }
    }

    private void
    ApplyBearerTokenToAllRequests_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (CollectionsTreeView.SelectedItem is not CollectionNode target
            || target.IsRequest)
        {
            return;
        }

        var token =
            TextInputWindow.Prompt(
                Window.GetWindow(this),
                "Bearer Token pour toutes les requêtes",
                "Token (une variable {{...}} d'environnement est possible) :",
                "{{TMP_AUTH_Service_IdToken}}");

        if (token == null)
        {
            return;
        }

        var count = ApplyBearerTokenRecursive(target, token);

        SaveCollectionOf(target);
        RefreshAutoHeaders();

        MessageBox.Show(
            $"{count} requête(s) mise(s) à jour sous « {target.Name} ».",
            "Bearer Token appliqué");
    }

    private static int
    ApplyBearerTokenRecursive(
        CollectionNode node,
        string token)
    {
        var count = 0;

        foreach (var child in node.Children)
        {
            if (child.IsFavoritesFolder)
            {
                // Mêmes objets que sous leur vrai dossier : les compter ici
                // aussi les compterait deux fois.
                continue;
            }

            if (child.IsRequest)
            {
                child.Auth = new AuthConfig { Type = AuthType.Bearer, BearerToken = token };
                count++;
            }
            else
            {
                count += ApplyBearerTokenRecursive(child, token);
            }
        }

        return count;
    }

    private void
    NewCollection_Click(
        object sender,
        RoutedEventArgs e)
    {
        var name =
            TextInputWindow.Prompt(
                Window.GetWindow(this),
                "Nouvelle collection",
                "Nom de la collection :",
                "Ma collection");

        if (name == null)
        {
            return;
        }

        try
        {
            _collectionStorage.CreateCollection(name);
            LoadCollectionsAndEnvironments();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Collection creation error");
        }
    }

    private void
    AddFolder_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (CollectionsTreeView.SelectedItem is not CollectionNode target
            || target.IsRequest)
        {
            return;
        }

        var name =
            TextInputWindow.Prompt(
                Window.GetWindow(this),
                "Nouveau dossier",
                "Nom du dossier :",
                "Nouveau dossier");

        if (name == null)
        {
            return;
        }

        target.Children.Add(
            new CollectionNode { Name = name, IsRequest = false, Parent = target });

        SaveCollectionOf(target);
    }

    private void
    AddRequestToCollection_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (CollectionsTreeView.SelectedItem is not CollectionNode target
            || target.IsRequest)
        {
            return;
        }

        var name =
            TextInputWindow.Prompt(
                Window.GetWindow(this),
                "Nouvelle requête",
                "Nom de la requête :",
                "Nouvelle requête");

        if (name == null)
        {
            return;
        }

        HeadersGrid.CommitEdit(DataGridEditingUnit.Row, true);
        ParamsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        BodyFormGrid.CommitEdit(DataGridEditingUnit.Row, true);
        SyncUrlFromParams();

        var mode = GetSelectedBodyMode();

        var newNode =
            new CollectionNode
            {
                Name = name,
                IsRequest = true,
                Parent = target,
                Method = (MethodCombo.SelectedItem as ComboBoxItem)?.Content as string ?? "GET",
                Url = UrlTextBox.Text.Trim(),

                Headers =
                    _headers
                        .Select(h => new HeaderItem { Enabled = h.Enabled, Key = h.Key, Value = h.Value })
                        .ToList(),

                Body = BodyTextBox.Text,
                BodyMode = mode,

                BodyFormData =
                    (mode == "urlencoded" ? _bodyUrlEncoded : _bodyFormData)
                        .Select(f => new HeaderItem { Enabled = f.Enabled, Key = f.Key, Value = f.Value })
                        .ToList(),

                Auth = BuildAuthConfig(),

                PostResponseExtractions =
                    _extractions
                        .Select(x => new HeaderItem { Enabled = x.Enabled, Key = x.Key, Value = x.Value })
                        .ToList()
            };

        target.Children.Add(newNode);

        _currentRequestNode = newNode;

        SaveCollectionOf(target);
    }

    private void
    UpdateRequestFromEditor_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (CollectionsTreeView.SelectedItem is not CollectionNode node
            || !node.IsRequest)
        {
            return;
        }

        HeadersGrid.CommitEdit(DataGridEditingUnit.Row, true);
        ParamsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        BodyFormGrid.CommitEdit(DataGridEditingUnit.Row, true);
        SyncUrlFromParams();

        var mode = GetSelectedBodyMode();

        node.Method = (MethodCombo.SelectedItem as ComboBoxItem)?.Content as string ?? "GET";
        node.Url = UrlTextBox.Text.Trim();

        node.Headers =
            _headers
                .Select(h => new HeaderItem { Enabled = h.Enabled, Key = h.Key, Value = h.Value })
                .ToList();

        node.Body = BodyTextBox.Text;
        node.BodyMode = mode;

        node.BodyFormData =
            (mode == "urlencoded" ? _bodyUrlEncoded : _bodyFormData)
                .Select(f => new HeaderItem { Enabled = f.Enabled, Key = f.Key, Value = f.Value })
                .ToList();

        node.Auth = BuildAuthConfig();

        node.PostResponseExtractions =
            _extractions
                .Select(x => new HeaderItem { Enabled = x.Enabled, Key = x.Key, Value = x.Value })
                .ToList();

        // Method peut changer le texte affiché ("GET  Nom" -> "POST  Nom").
        RefreshNodeDisplay(node);
        RebuildFavoritesFolders();

        SaveCollectionOf(node);
    }

    private void
    RenameNode_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (CollectionsTreeView.SelectedItem is not CollectionNode node)
        {
            return;
        }

        var name =
            TextInputWindow.Prompt(
                Window.GetWindow(this),
                "Renommer",
                "Nouveau nom :",
                node.Name);

        if (name == null)
        {
            return;
        }

        node.Name = name;

        RefreshNodeDisplay(node);
        RebuildFavoritesFolders();

        SaveCollectionOf(node);
    }

    private void
    DeleteNode_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (CollectionsTreeView.SelectedItem is not CollectionNode node)
        {
            return;
        }

        if (node.Parent == null)
        {
            if (MessageBox.Show(
                    $"Supprimer définitivement la collection \"{node.Name}\" (fichier inclus) ?",
                    "Supprimer la collection",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning)
                != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                _collectionStorage.DeleteCollectionFile(node.FilePath ?? "");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Delete error");
                return;
            }

            _collections.Remove(node);
            return;
        }

        if (MessageBox.Show(
                $"Supprimer \"{node.Name}\" ?",
                "Supprimer",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning)
            != MessageBoxResult.Yes)
        {
            return;
        }

        var parent = node.Parent;

        parent.Children.Remove(node);
        RebuildFavoritesFolders();

        SaveCollectionOf(parent);
    }

    /// CollectionNode n'implémente pas INotifyPropertyChanged : le retirer
    /// puis le réinsérer au même endroit force le TreeView à régénérer son
    /// conteneur et donc relire DisplayText/Name à jour (même trick que
    /// SortNodes pour le tri).
    private void
    RefreshNodeDisplay(
        CollectionNode node)
    {
        var siblings = node.Parent?.Children ?? _collections;
        var index = siblings.IndexOf(node);

        if (index < 0)
        {
            return;
        }

        siblings.RemoveAt(index);
        siblings.Insert(index, node);
    }

    private void
    SaveCollectionOf(
        CollectionNode node)
    {
        try
        {
            _collectionStorage.SaveCollection(node.GetRoot());
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Collection save error");
        }
    }

    private void
    AuthTypeCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (InheritAuthPanel == null
            || BearerAuthPanel == null
            || BasicAuthPanel == null
            || ApiKeyAuthPanel == null)
        {
            return;
        }

        InheritAuthPanel.Visibility = Visibility.Collapsed;
        BearerAuthPanel.Visibility = Visibility.Collapsed;
        BasicAuthPanel.Visibility = Visibility.Collapsed;
        ApiKeyAuthPanel.Visibility = Visibility.Collapsed;

        switch (AuthTypeCombo.SelectedIndex)
        {
            case 0:
                InheritAuthPanel.Visibility = Visibility.Visible;
                break;

            case 2:
                BearerAuthPanel.Visibility = Visibility.Visible;
                break;

            case 3:
                BasicAuthPanel.Visibility = Visibility.Visible;
                break;

            case 4:
                ApiKeyAuthPanel.Visibility = Visibility.Visible;
                break;
        }

        RefreshAutoHeaders();
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
        // pour qu'elle soit bien incluse dans _headers/_params/le body.
        HeadersGrid.CommitEdit(DataGridEditingUnit.Row, true);
        ParamsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        BodyFormGrid.CommitEdit(DataGridEditingUnit.Row, true);
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

            var auth = ResolveAuthConfigForSend();

            var variables =
                (EnvironmentCombo.SelectedItem as EnvironmentSet)?.ToSubstitutionMap();

            Logger.Debug(
                $"ApiClientView: auth résolue pour l'envoi -- type={auth.Type}, "
                + $"bearerToken='{MaskForLog(auth.BearerToken)}', "
                + $"variables disponibles=[{string.Join(", ", (variables ?? new()).Select(kv => $"{kv.Key}({kv.Value.Length} car.)"))}].");

            var requestBody =
                new RequestBody
                {
                    Mode = GetSelectedBodyMode(),
                    Raw = BodyTextBox.Text,
                    RawContentType = GetRawContentType(),
                    FormData = _bodyFormData.ToList(),
                    UrlEncoded = _bodyUrlEncoded.ToList(),
                    BinaryFilePath = _binaryFilePath,
                    GraphQlQuery = GraphQlQueryTextBox.Text,
                    GraphQlVariables = GraphQlVariablesTextBox.Text
                };

            Logger.Debug($"ApiClientView: envoi {method} {url}");

            var result =
                await _apiClientService.SendAsync(
                    method,
                    url,
                    _headers.ToList(),
                    requestBody,
                    auth,
                    variables,
                    _sendCancellation.Token);

            Logger.Info($"ApiClientView: {method} {url} -> {result.StatusDisplay} ({result.ElapsedMs} ms)");

            StatusTextBlock.Text = result.StatusDisplay;
            StatusBadge.Background = result.StatusBackground;
            StatusBadge.Visibility = Visibility.Visible;
            TimingTextBlock.Text = $"{result.ElapsedMs} ms • {result.Body.Length:N0} chars";

            ResponseHeadersTextBox.Text = result.Headers;

            LoadResponseBody(result.Body);

            if (result.StatusCode is >= 200 and < 300)
            {
                ApplyPostResponseExtractions(result.Body);
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Debug($"ApiClientView: requête {method} {url} annulée.");
        }
        catch (Exception ex)
        {
            Logger.Error($"ApiClientView: échec de la requête {method} {url}.", ex);

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
        _lastResponseBody = body;

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

        RefreshResponseView();
        UpdatePaginationControls();
    }

    /// Détecte comment paginer la requête actuelle à partir de ses
    /// propres Params (jamais de la réponse : c'est la requête qui porte
    /// les paramètres à modifier pour changer de page) : "offset" (avec
    /// un pas déduit de size/limit/pageSize, ou de la réponse à défaut)
    /// ou "page"/"pageNumber"/"pageIndex" (pas de 1). Retourne null si
    /// aucun des deux schémas n'est présent dans les Params.
    private (HeaderItem Param, string Mode, int Step)?
    DetectPagination()
    {
        var offsetParam =
            _params.FirstOrDefault(p =>
                p.Enabled
                && string.Equals(p.Key?.Trim(), "offset", StringComparison.OrdinalIgnoreCase));

        if (offsetParam != null)
        {
            var step =
                TryGetIntParam("size")
                ?? TryGetIntParam("limit")
                ?? TryGetIntParam("pageSize")
                ?? TryGetResponseIntField("size")
                ?? TryGetResponseIntField("pageSize")
                ?? 20;

            return (offsetParam, "offset", step);
        }

        var pageKeys = new[] { "page", "pagenumber", "pageindex" };

        var pageParam =
            _params.FirstOrDefault(p =>
                p.Enabled
                && p.Key != null
                && pageKeys.Contains(p.Key.Trim().ToLowerInvariant()));

        return pageParam != null
            ? (pageParam, "page", 1)
            : null;
    }

    private int?
    TryGetIntParam(
        string key)
    {
        var param =
            _params.FirstOrDefault(p =>
                p.Enabled
                && string.Equals(p.Key?.Trim(), key, StringComparison.OrdinalIgnoreCase));

        return param != null && int.TryParse(param.Value, out var value)
            ? value
            : null;
    }

    private int?
    TryGetResponseIntField(
        string key)
    {
        if (string.IsNullOrWhiteSpace(_lastResponseBody))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(_lastResponseBody);

            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(key, out var element)
                && element.ValueKind == JsonValueKind.Number
                    ? element.GetInt32()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// Affiche/active les boutons Page précédente/suivante selon ce que
    /// DetectPagination() trouve dans les Params actuels, et essaie
    /// d'estimer s'il reste une page suivante à partir de champs
    /// habituels de la réponse (items/total/totalItems/count, ou
    /// pages/totalPages) -- purement indicatif, jamais bloquant si ces
    /// champs sont absents ou nommés différemment par l'API.
    private void
    UpdatePaginationControls()
    {
        var pagination = DetectPagination();

        if (pagination == null)
        {
            PrevPageButton.Visibility = Visibility.Collapsed;
            NextPageButton.Visibility = Visibility.Collapsed;
            PageInfoTextBlock.Visibility = Visibility.Collapsed;

            return;
        }

        var (param, mode, step) = pagination.Value;

        int.TryParse(param.Value, out var current);

        PrevPageButton.Visibility = Visibility.Visible;
        NextPageButton.Visibility = Visibility.Visible;
        PageInfoTextBlock.Visibility = Visibility.Visible;

        PrevPageButton.IsEnabled = current > 0;

        if (mode == "offset")
        {
            var total =
                TryGetResponseIntField("items")
                ?? TryGetResponseIntField("total")
                ?? TryGetResponseIntField("totalItems")
                ?? TryGetResponseIntField("count");

            NextPageButton.IsEnabled = !total.HasValue || current + step < total.Value;

            PageInfoTextBlock.Text =
                total.HasValue
                    ? $"offset {current} / {total.Value}"
                    : $"offset {current}";
        }
        else
        {
            var totalPages =
                TryGetResponseIntField("pages")
                ?? TryGetResponseIntField("totalPages");

            NextPageButton.IsEnabled = !totalPages.HasValue || current + 1 < totalPages.Value;

            PageInfoTextBlock.Text =
                totalPages.HasValue
                    ? $"page {current} / {totalPages.Value}"
                    : $"page {current}";
        }
    }

    private async void
    PrevPage_Click(
        object sender,
        RoutedEventArgs e) =>
        await GoToAdjacentPageAsync(-1);

    private async void
    NextPage_Click(
        object sender,
        RoutedEventArgs e) =>
        await GoToAdjacentPageAsync(1);

    private async Task
    GoToAdjacentPageAsync(
        int direction)
    {
        var pagination = DetectPagination();

        if (pagination == null)
        {
            return;
        }

        var (param, mode, step) = pagination.Value;

        int.TryParse(param.Value, out var current);

        var next =
            mode == "offset"
                ? current + direction * step
                : current + direction;

        if (next < 0)
        {
            next = 0;
        }

        param.Value = next.ToString();

        // HeaderItem n'implémente pas INotifyPropertyChanged (mutation
        // depuis le code, pas depuis une cellule éditée) : forcer le
        // DataGrid à relire la valeur affichée.
        ParamsGrid.Items.Refresh();

        SyncUrlFromParams();

        await SendAsync();
    }

    private void
    ResponseViewMode_Changed(
        object sender,
        RoutedEventArgs e) =>
        RefreshResponseView();

    /// Bascule entre la vue "Brut" (JSON tel quel, avec coloration) et la
    /// vue "Cartes" (un bloc par objet/élément de tableau, voir
    /// JsonCardViewBuilder). Reconstruit la vue Cartes à chaque appel :
    /// pas de cache, la réponse ne change qu'après un nouvel envoi.
    private void
    RefreshResponseView()
    {
        // Le RadioButton "Cartes" a IsChecked="True" dans le XAML : son
        // événement Checked se déclenche pendant InitializeComponent(),
        // avant que ResponseBodyEditor/ResponsePrettyContainer (plus bas
        // dans l'arbre) n'existent encore.
        if (ResponseBodyEditor == null
            || ResponsePrettyContainer == null
            || ResponsePrettyContent == null
            || ResponseAnchorsPanel == null)
        {
            return;
        }

        if (ResponseViewRawRadio?.IsChecked == true)
        {
            ResponseBodyEditor.Visibility = Visibility.Visible;
            ResponsePrettyContainer.Visibility = Visibility.Collapsed;
        }
        else
        {
            ResponseBodyEditor.Visibility = Visibility.Collapsed;
            ResponsePrettyContainer.Visibility = Visibility.Visible;

            _lastCardsView = JsonCardViewBuilder.Build(_lastResponseBody);

            ResponsePrettyContent.Content =
                _lastCardsView?.Root
                ?? new TextBlock
                {
                    Text = "Réponse non-JSON : voir la vue \"Brut\".",
                    FontStyle = FontStyles.Italic,
                    Foreground = (Brush)FindResource("TextMutedBrush"),
                    Margin = new Thickness(8)
                };

            BuildResponseAnchorsBar(_lastCardsView?.Anchors);
        }

        // Le contenu vient d'être reconstruit (nouvelle réponse, ou
        // changement de mode) : toute référence à un élément mis en
        // surbrillance précédemment est maintenant obsolète.
        RunResponseSearch();
    }

    private JsonCardViewResult? _lastCardsView;
    private List<JsonCardSearchEntry> _cardSearchMatches = new();
    private readonly List<int> _rawSearchMatchOffsets = new();
    private int _searchMatchIndex = -1;
    private static readonly Brush SearchMatchBrush = CreateSearchMatchBrush();

    private static Brush
    CreateSearchMatchBrush()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0xFF, 0xE9, 0x8A));
        brush.Freeze();

        return brush;
    }

    private void
    ResponseSearchBox_TextChanged(
        object sender,
        TextChangedEventArgs e) =>
        RunResponseSearch();

    private void
    ResponseSearchBox_PreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:

                MoveToSearchMatch(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
                e.Handled = true;

                break;

            case Key.Escape:

                ResponseSearchBox.Text = "";
                e.Handled = true;

                break;
        }
    }

    private void
    ResponseSearchPrev_Click(
        object sender,
        RoutedEventArgs e) =>
        MoveToSearchMatch(-1);

    private void
    ResponseSearchNext_Click(
        object sender,
        RoutedEventArgs e) =>
        MoveToSearchMatch(1);

    /// Recherche mode-consciente : en vue Cartes, filtre les entrées
    /// indexées par JsonCardViewBuilder (clés, valeurs, badges, titres
    /// de bloc) et surligne le résultat courant ; en vue Brut, cherche
    /// directement dans le texte de l'éditeur AvalonEdit et sélectionne
    /// le résultat courant. Les deux se pilotent avec les mêmes boutons
    /// ▲/▼ et le même compteur.
    private void
    RunResponseSearch()
    {
        ClearSearchHighlight();

        var query = ResponseSearchBox.Text?.Trim() ?? "";

        _cardSearchMatches = new List<JsonCardSearchEntry>();
        _rawSearchMatchOffsets.Clear();
        _searchMatchIndex = -1;

        if (string.IsNullOrEmpty(query))
        {
            ResponseSearchCountText.Visibility = Visibility.Collapsed;
            ResponseSearchPrevButton.Visibility = Visibility.Collapsed;
            ResponseSearchNextButton.Visibility = Visibility.Collapsed;

            return;
        }

        var isRawMode = ResponseViewRawRadio?.IsChecked == true;

        if (isRawMode)
        {
            var text = ResponseBodyEditor.Text ?? "";
            var searchFrom = 0;

            while (true)
            {
                var found = text.IndexOf(query, searchFrom, StringComparison.OrdinalIgnoreCase);

                if (found < 0)
                {
                    break;
                }

                _rawSearchMatchOffsets.Add(found);
                searchFrom = found + Math.Max(query.Length, 1);
            }

            _searchMatchIndex = _rawSearchMatchOffsets.Count > 0 ? 0 : -1;
        }
        else
        {
            _cardSearchMatches =
                _lastCardsView?.SearchEntries
                    .Where(entry => entry.Text.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .ToList()
                ?? new List<JsonCardSearchEntry>();

            _searchMatchIndex = _cardSearchMatches.Count > 0 ? 0 : -1;
        }

        var totalMatches = isRawMode ? _rawSearchMatchOffsets.Count : _cardSearchMatches.Count;

        ResponseSearchCountText.Visibility = Visibility.Visible;
        ResponseSearchPrevButton.Visibility = Visibility.Visible;
        ResponseSearchNextButton.Visibility = Visibility.Visible;
        ResponseSearchPrevButton.IsEnabled = totalMatches > 0;
        ResponseSearchNextButton.IsEnabled = totalMatches > 0;

        UpdateSearchCountText(totalMatches);
        ShowCurrentSearchMatch(query.Length);
    }

    private void
    MoveToSearchMatch(
        int direction)
    {
        var isRawMode = ResponseViewRawRadio?.IsChecked == true;
        var total = isRawMode ? _rawSearchMatchOffsets.Count : _cardSearchMatches.Count;

        if (total == 0)
        {
            return;
        }

        ClearSearchHighlight();

        _searchMatchIndex = (_searchMatchIndex + direction + total) % total;

        UpdateSearchCountText(total);
        ShowCurrentSearchMatch(ResponseSearchBox.Text?.Trim().Length ?? 0);
    }

    private void
    ShowCurrentSearchMatch(
        int queryLength)
    {
        if (_searchMatchIndex < 0)
        {
            return;
        }

        if (ResponseViewRawRadio?.IsChecked == true)
        {
            var offset = _rawSearchMatchOffsets[_searchMatchIndex];

            ResponseBodyEditor.Select(offset, queryLength);
            ResponseBodyEditor.ScrollToLine(
                ResponseBodyEditor.Document.GetLineByOffset(offset).LineNumber);

            return;
        }

        var match = _cardSearchMatches[_searchMatchIndex];

        match.Element.Background = SearchMatchBrush;

        ExpandAndScrollTo(match.Element);
    }

    private void
    ClearSearchHighlight()
    {
        if (_searchMatchIndex >= 0
            && _searchMatchIndex < _cardSearchMatches.Count)
        {
            _cardSearchMatches[_searchMatchIndex].Element.Background = Brushes.Transparent;
        }
    }

    private void
    UpdateSearchCountText(
        int totalMatches)
    {
        ResponseSearchCountText.Text =
            totalMatches == 0
                ? "0/0"
                : $"{_searchMatchIndex + 1}/{totalMatches}";
    }

    /// Barre de raccourcis au-dessus de la vue Cartes : un chip par bloc
    /// nommé (transitAccount, productContainers...) qui fait défiler
    /// directement jusqu'à ce bloc via BringIntoView(), plutôt que de
    /// devoir tout parcourir à la molette pour une réponse volumineuse.
    private void
    BuildResponseAnchorsBar(
        IReadOnlyList<JsonCardAnchor>? anchors)
    {
        ResponseAnchorsPanel.Children.Clear();

        if (anchors == null
            || anchors.Count == 0)
        {
            ResponseAnchorsPanel.Visibility = Visibility.Collapsed;

            return;
        }

        ResponseAnchorsPanel.Visibility = Visibility.Visible;

        foreach (var anchor in anchors)
        {
            var target = anchor.Element;

            var chip =
                new Border
                {
                    Background = (Brush)FindResource("AccentSoftBrush"),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(10, 4, 10, 4),
                    Margin = new Thickness(0, 0, 6, 6),
                    Cursor = Cursors.Hand,
                    Child = new TextBlock
                    {
                        Text = anchor.Label,
                        FontSize = 12,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = (Brush)FindResource("AccentBrush")
                    }
                };

            chip.MouseLeftButtonUp +=
                (_, _) => ExpandAndScrollTo(target);

            ResponseAnchorsPanel.Children.Add(chip);
        }
    }

    /// Les blocs de la vue Cartes sont repliés par défaut (voir
    /// JsonCardViewBuilder.BuildCard) : sauter directement dessus ne
    /// sert à rien tant qu'il reste replié, ou que l'un de ses parents
    /// (lui aussi replié) le cache. Déplie toute la chaîne, puis attend
    /// que la mise en page ait pris en compte la nouvelle hauteur avant
    /// de défiler (BringIntoView() juste après IsExpanded=true utiliserait
    /// encore les anciennes dimensions, repliées).
    private static void
    ExpandAndScrollTo(
        FrameworkElement target)
    {
        for (DependencyObject? node = target;
             node != null;
             node = VisualTreeHelper.GetParent(node))
        {
            if (node is Expander expander)
            {
                expander.IsExpanded = true;
            }
        }

        target.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() => target.BringIntoView()));
    }

    /// Équivalent simplifié d'un script Postman "pm.environment.set(...)" :
    /// pour chaque règle activée (grille "Extraction → Environnement"),
    /// copie le champ JSON de premier niveau correspondant de la réponse
    /// dans la variable d'environnement visée (créée si elle n'existe pas
    /// encore), puis sauvegarde le fichier d'environnement.
    private void
    ApplyPostResponseExtractions(
        string responseBody)
    {
        var rules =
            _extractions
                .Where(r => r.Enabled
                    && !string.IsNullOrWhiteSpace(r.Key)
                    && !string.IsNullOrWhiteSpace(r.Value))
                .ToList();

        Logger.Debug(
            $"ApiClientView: extraction post-réponse -- {rules.Count} règle(s) active(s) "
            + $"({string.Join(", ", rules.Select(r => $"{r.Key}->{r.Value}"))}), "
            + $"environnement sélectionné = "
            + $"{(EnvironmentCombo.SelectedItem as EnvironmentSet)?.Name ?? "(aucun)"}.");

        if (rules.Count == 0)
        {
            Logger.Debug(
                "ApiClientView: extraction ignorée -- aucune règle active sur cette requête.");

            return;
        }

        if (EnvironmentCombo.SelectedItem is not EnvironmentSet environment
            || string.IsNullOrEmpty(environment.FilePath))
        {
            Logger.Debug(
                "ApiClientView: extraction ignorée -- aucun environnement (avec fichier) sélectionné.");

            return;
        }

        JsonElement root;

        try
        {
            root = JsonDocument.Parse(responseBody).RootElement;
        }
        catch (JsonException ex)
        {
            Logger.Debug(
                $"ApiClientView: extraction ignorée -- réponse non-JSON ({ex.Message}).");

            return;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            Logger.Debug(
                $"ApiClientView: extraction ignorée -- réponse JSON de type {root.ValueKind}, objet attendu.");

            return;
        }

        var updated = 0;

        foreach (var rule in rules)
        {
            if (!root.TryGetProperty(rule.Key, out var valueElement))
            {
                Logger.Debug(
                    $"ApiClientView: extraction -- champ '{rule.Key}' absent du premier niveau de la réponse.");

                continue;
            }

            var value =
                valueElement.ValueKind switch
                {
                    JsonValueKind.String => valueElement.GetString() ?? "",
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Null => "",
                    _ => valueElement.GetRawText()
                };

            // S'il existe plusieurs entrées pour la même clé (import Postman
            // ou édition manuelle malencontreuse), ToSubstitutionMap() ne
            // regarde que la dernière : ne mettre à jour que la première et
            // laisser une copie obsolète/vide trainer casserait
            // silencieusement la substitution. On nettoie donc les doublons
            // ici pour ne garder qu'une seule entrée, à jour.
            var duplicates =
                environment.Variables
                    .Where(v => v.Key == rule.Value)
                    .ToList();

            var wasExisting = duplicates.Count > 0;

            if (duplicates.Count > 0)
            {
                var existing = duplicates[0];

                existing.Enabled = true;
                existing.Value = value;

                foreach (var duplicate in duplicates.Skip(1))
                {
                    Logger.Debug(
                        $"ApiClientView: extraction -- doublon de la variable '{rule.Value}' supprimé "
                        + $"(valeur obsolète='{MaskForLog(duplicate.Value)}').");

                    environment.Variables.Remove(duplicate);
                }
            }
            else
            {
                environment.Variables.Add(
                    new HeaderItem { Enabled = true, Key = rule.Value, Value = value });
            }

            Logger.Debug(
                $"ApiClientView: extraction -- '{rule.Key}' = '{value}' -> variable '{rule.Value}' "
                + $"({(wasExisting ? "mise à jour" : "créée")}).");

            updated++;
        }

        if (updated == 0)
        {
            Logger.Debug(
                "ApiClientView: extraction -- aucune règle ne correspondait à un champ de la réponse, rien à sauvegarder.");

            return;
        }

        try
        {
            _collectionStorage.SaveEnvironment(environment);

            Logger.Info(
                $"ApiClientView: environnement '{environment.Name}' mis à jour ({updated} variable(s) extraite(s) de la réponse) -> fichier '{environment.FilePath}'.");
        }
        catch (Exception ex)
        {
            Logger.Error("ApiClientView: échec de la mise à jour de l'environnement après extraction.", ex);
        }
    }

    /// Ne journalise jamais un secret en clair : montre juste de quoi
    /// vérifier qu'un placeholder {{...}} a bien été remplacé par autre
    /// chose (longueur + début), sans exposer le token complet dans les
    /// logs.
    private static string
    MaskForLog(
        string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "(vide)";
        }

        return value.Length <= 12
            ? value
            : $"{value[..12]}…({value.Length} car.)";
    }

    /// État brut du panneau Auth (peut être "Inherit") : à utiliser pour
    /// sauvegarder sur un CollectionNode, jamais directement pour envoyer
    /// une requête (voir ResolveAuthConfigForSend).
    private AuthConfig
    BuildAuthConfig()
    {
        var type = AuthTypeCombo.SelectedIndex switch
        {
            1 => AuthType.None,
            2 => AuthType.Bearer,
            3 => AuthType.Basic,
            4 => AuthType.ApiKey,
            _ => AuthType.Inherit
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

    /// Résout "Inherit" en remontant vers le parent du nœud actuellement
    /// chargé dans l'éditeur (le nœud lui-même est ignoré : c'est justement
    /// lui qui vaut "Inherit" dans ce cas). Sans nœud chargé (nouvelle
    /// requête non enregistrée) ou sans parent concret, retombe sur "None".
    private AuthConfig
    ResolveAuthConfigForSend()
    {
        var raw = BuildAuthConfig();

        return raw.Type != AuthType.Inherit
            ? raw
            : _currentRequestNode?.Parent?.ResolveEffectiveAuth()
                ?? new AuthConfig { Type = AuthType.None };
    }
}

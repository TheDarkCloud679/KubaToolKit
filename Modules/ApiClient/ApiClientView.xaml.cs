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

        Logger.Debug("ApiClientView: constructor finished.");
    }

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
                ? "Hide automatically generated headers"
                : "Show automatically generated headers";
    }

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

        var resolvedAuth = ResolveAuthConfigForSend();

        switch (resolvedAuth.Type)
        {
            case AuthType.Bearer:

                if (!Has("Authorization"))
                {
                    var value =
                        string.IsNullOrWhiteSpace(resolvedAuth.BearerToken)
                            ? "Bearer <computed on send>"
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
                            ? "Basic <computed on send>"
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
                            ? "<computed on send>"
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
                "formdata" => "multipart/form-data; boundary=<computed on send>",
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
                new HeaderItem { Key = "Content-Length", Value = "<computed on send>" });
        }

        if (!Has("Host"))
        {
            var host =
                Uri.TryCreate(UrlTextBox?.Text, UriKind.Absolute, out var uri)
                    ? uri.Host
                    : null;

            _autoHeaders.Add(
                new HeaderItem { Key = "Host", Value = host ?? "<computed on send>" });
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
                    Name = "Favorites",
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
            Logger.Error("ApiClientView: failed to load collections/environments.", ex);

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
    OpenValueLabels_Click(
        object sender,
        RoutedEventArgs e)
    {
        _collectionStorage.LoadValueLabels();

        Process.Start(
            new ProcessStartInfo
            {
                FileName = CollectionStorageService.ValueLabelsFile,
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
                "Select an environment first (or add one via \"+\").",
                "No environment selected");

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
        BinaryFilePathText.Text = "No file selected";
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

        RefreshAutoHeaders();
    }

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

    private List<CollectionNode> _requestSearchMatches = new();
    private int _requestSearchMatchIndex = -1;

    private void
    RequestSearchBox_TextChanged(
        object sender,
        TextChangedEventArgs e) =>
        RunRequestSearch();

    private void
    RequestSearchBox_PreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:

                MoveToRequestMatch(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
                e.Handled = true;

                break;

            case Key.Escape:

                RequestSearchBox.Text = "";
                e.Handled = true;

                break;
        }
    }

    private void
    RunRequestSearch()
    {
        var query = RequestSearchBox.Text?.Trim() ?? "";

        _requestSearchMatches.Clear();
        _requestSearchMatchIndex = -1;

        if (string.IsNullOrEmpty(query))
        {
            RequestSearchCountText.Visibility = Visibility.Collapsed;

            return;
        }

        _requestSearchMatches =
            _collections
                .SelectMany(EnumerateRequestNodes)
                .Where(n => n.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .ToList();

        _requestSearchMatchIndex = _requestSearchMatches.Count > 0 ? 0 : -1;

        RequestSearchCountText.Visibility = Visibility.Visible;
        UpdateRequestSearchCountText();

        if (_requestSearchMatchIndex >= 0)
        {
            ExpandAndSelectTreeNode(_requestSearchMatches[_requestSearchMatchIndex]);
        }
    }

    private static IEnumerable<CollectionNode>
    EnumerateRequestNodes(
        CollectionNode node)
    {
        if (node.IsRequest)
        {
            yield return node;
        }

        foreach (var child in node.Children)
        {
            foreach (var descendant in EnumerateRequestNodes(child))
            {
                yield return descendant;
            }
        }
    }

    private void
    MoveToRequestMatch(
        int direction)
    {
        if (_requestSearchMatches.Count == 0)
        {
            return;
        }

        _requestSearchMatchIndex =
            (_requestSearchMatchIndex + direction + _requestSearchMatches.Count)
            % _requestSearchMatches.Count;

        UpdateRequestSearchCountText();
        ExpandAndSelectTreeNode(_requestSearchMatches[_requestSearchMatchIndex]);
    }

    private void
    UpdateRequestSearchCountText()
    {
        RequestSearchCountText.Text =
            _requestSearchMatches.Count == 0
                ? "0/0"
                : $"{_requestSearchMatchIndex + 1}/{_requestSearchMatches.Count}";
    }

    private void
    ExpandAndSelectTreeNode(
        CollectionNode node)
    {
        var ancestors = new List<CollectionNode>();
        var current = node.Parent;

        while (current != null)
        {
            ancestors.Insert(0, current);
            current = current.Parent;
        }

        ItemsControl container = CollectionsTreeView;

        foreach (var ancestor in ancestors)
        {
            container.UpdateLayout();

            if (container.ItemContainerGenerator.ContainerFromItem(ancestor)
                is not TreeViewItem ancestorItem)
            {
                return;
            }

            ancestorItem.IsExpanded = true;

            container = ancestorItem;
        }

        container.UpdateLayout();

        if (container.ItemContainerGenerator.ContainerFromItem(node) is not TreeViewItem targetItem)
        {
            return;
        }

        targetItem.IsSelected = true;

        targetItem.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() => targetItem.BringIntoView()));
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

        AddRequestMenuItem.IsEnabled = isFolder;
        AddFolderMenuItem.IsEnabled = isFolder;
        UpdateRequestMenuItem.IsEnabled = isRequest;
        RenameMenuItem.IsEnabled = isRealNode;
        DeleteMenuItem.IsEnabled = isRealNode;

        FavoriteMenuItem.IsEnabled = isRequest;
        FavoriteMenuItem.Header =
            node?.IsFavorite == true
                ? "★ Remove from favorites"
                : "☆ Add to favorites";

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
                "Bearer Token for all requests",
                "Token (an environment {{...}} variable is allowed):",
                "{{TMP_AUTH_Service_IdToken}}");

        if (token == null)
        {
            return;
        }

        var count = ApplyBearerTokenRecursive(target, token);

        SaveCollectionOf(target);
        RefreshAutoHeaders();

        MessageBox.Show(
            $"{count} request(s) updated under \"{target.Name}\".",
            "Bearer Token applied");
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
                "New collection",
                "Collection name:",
                "My collection");

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
                "New folder",
                "Folder name:",
                "New folder");

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
                "New request",
                "Request name:",
                "New request");

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
                "Rename",
                "New name:",
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
                    $"Permanently delete the collection \"{node.Name}\" (including its file)?",
                    "Delete collection",
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
                $"Delete \"{node.Name}\"?",
                "Delete",
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
                "Enter a URL");

            return;
        }

        var method =
            (MethodCombo.SelectedItem as ComboBoxItem)?.Content as string
            ?? "GET";

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
                $"ApiClientView: auth resolved for send -- type={auth.Type}, "
                + $"bearerToken='{MaskForLog(auth.BearerToken)}', "
                + $"available variables=[{string.Join(", ", (variables ?? new()).Select(kv => $"{kv.Key}({kv.Value.Length} chars)"))}].");

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

            Logger.Debug($"ApiClientView: sending {method} {url}");

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
            Logger.Debug($"ApiClientView: request {method} {url} cancelled.");
        }
        catch (Exception ex)
        {
            Logger.Error($"ApiClientView: request {method} {url} failed.", ex);

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
        UpdateSizeSortControls();
    }

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

        ParamsGrid.Items.Refresh();

        SyncUrlFromParams();

        await SendAsync();
    }

    private HeaderItem?
    DetectSizeParam()
    {
        var sizeKeys = new[] { "size", "limit", "pagesize" };

        return _params.FirstOrDefault(p =>
            p.Enabled
            && p.Key != null
            && sizeKeys.Contains(p.Key.Trim().ToLowerInvariant()));
    }

    private HeaderItem?
    DetectSortParam()
    {
        var sortKeys = new[] { "sort", "order", "direction", "sortdirection", "sortorder" };

        return _params.FirstOrDefault(p =>
            p.Enabled
            && p.Key != null
            && sortKeys.Contains(p.Key.Trim().ToLowerInvariant())
            && p.Value != null
            && (string.Equals(p.Value.Trim(), "asc", StringComparison.OrdinalIgnoreCase)
                || string.Equals(p.Value.Trim(), "desc", StringComparison.OrdinalIgnoreCase)));
    }

    private void
    UpdateSizeSortControls()
    {
        var sizeParam = DetectSizeParam();

        PageSizeLabel.Visibility = sizeParam != null ? Visibility.Visible : Visibility.Collapsed;
        PageSizeBox.Visibility = sizeParam != null ? Visibility.Visible : Visibility.Collapsed;

        if (sizeParam != null)
        {
            PageSizeBox.Text = sizeParam.Value;
        }

        var sortParam = DetectSortParam();

        SortToggleButton.Visibility = sortParam != null ? Visibility.Visible : Visibility.Collapsed;

        if (sortParam != null)
        {
            var isDescending = string.Equals(sortParam.Value?.Trim(), "desc", StringComparison.OrdinalIgnoreCase);

            SortToggleButton.Content = isDescending ? "Sort ▼ Descending" : "Sort ▲ Ascending";
        }
    }

    private void
    ResetPageToStart()
    {
        var pagination = DetectPagination();

        if (pagination != null)
        {
            pagination.Value.Param.Value = "0";
        }
    }

    private async void
    PageSizeBox_KeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;

        await ApplyPageSizeAsync();
    }

    private async void
    PageSizeBox_LostFocus(
        object sender,
        RoutedEventArgs e) =>
        await ApplyPageSizeAsync();

    private async Task
    ApplyPageSizeAsync()
    {
        var sizeParam = DetectSizeParam();

        if (sizeParam == null)
        {
            return;
        }

        if (!int.TryParse(PageSizeBox.Text.Trim(), out var newSize)
            || newSize <= 0)
        {
            PageSizeBox.Text = sizeParam.Value;

            return;
        }

        if (sizeParam.Value == newSize.ToString())
        {
            return;
        }

        sizeParam.Value = newSize.ToString();

        ResetPageToStart();
        ParamsGrid.Items.Refresh();
        SyncUrlFromParams();

        await SendAsync();
    }

    private async void
    SortToggle_Click(
        object sender,
        RoutedEventArgs e)
    {
        var sortParam = DetectSortParam();

        if (sortParam == null)
        {
            return;
        }

        var isDescending = string.Equals(sortParam.Value?.Trim(), "desc", StringComparison.OrdinalIgnoreCase);

        sortParam.Value = isDescending ? "asc" : "desc";

        ResetPageToStart();
        ParamsGrid.Items.Refresh();
        SyncUrlFromParams();

        await SendAsync();
    }

    private void
    ResponseViewMode_Changed(
        object sender,
        RoutedEventArgs e) =>
        RefreshResponseView();

    private void
    RefreshResponseView()
    {
        if (ResponseBodyEditor == null
            || ResponsePrettyContainer == null
            || ResponsePrettyContent == null)
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

            var valueLabels = _collectionStorage.LoadValueLabels();

            ValueLabelsWarningText.Visibility =
                _collectionStorage.LastValueLabelsError != null
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            ValueLabelsWarningText.ToolTip =
                _collectionStorage.LastValueLabelsError != null
                    ? $"Invalid ValueLabels.json, no labels applied: {_collectionStorage.LastValueLabelsError}"
                    : null;

            _lastCardsView = JsonCardViewBuilder.Build(_lastResponseBody, valueLabels);

            ResponsePrettyContent.Content =
                _lastCardsView?.Root
                ?? new TextBlock
                {
                    Text = "Non-JSON response: see the \"Raw\" view.",
                    FontStyle = FontStyles.Italic,
                    Foreground = (Brush)FindResource("TextMutedBrush"),
                    Margin = new Thickness(8)
                };
        }

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

        ExpandAndScrollTo(match.Element, match.Ancestors);
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

    private static void
    ExpandAndScrollTo(
        FrameworkElement target,
        IReadOnlyList<Expander> ancestors)
    {
        foreach (var ancestor in ancestors)
        {
            ancestor.IsExpanded = true;
        }

        if (target is Expander self)
        {
            self.IsExpanded = true;
        }

        target.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() => target.BringIntoView()));
    }

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
            $"ApiClientView: post-response extraction -- {rules.Count} active rule(s) "
            + $"({string.Join(", ", rules.Select(r => $"{r.Key}->{r.Value}"))}), "
            + $"selected environment = "
            + $"{(EnvironmentCombo.SelectedItem as EnvironmentSet)?.Name ?? "(none)"}.");

        if (rules.Count == 0)
        {
            Logger.Debug(
                "ApiClientView: extraction skipped -- no active rule on this request.");

            return;
        }

        if (EnvironmentCombo.SelectedItem is not EnvironmentSet environment
            || string.IsNullOrEmpty(environment.FilePath))
        {
            Logger.Debug(
                "ApiClientView: extraction skipped -- no environment (with file) selected.");

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
                $"ApiClientView: extraction skipped -- non-JSON response ({ex.Message}).");

            return;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            Logger.Debug(
                $"ApiClientView: extraction skipped -- JSON response of type {root.ValueKind}, expected an object.");

            return;
        }

        var updated = 0;

        foreach (var rule in rules)
        {
            if (!root.TryGetProperty(rule.Key, out var valueElement))
            {
                Logger.Debug(
                    $"ApiClientView: extraction -- field '{rule.Key}' missing from top level of the response.");

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
                        $"ApiClientView: extraction -- duplicate of variable '{rule.Value}' removed "
                        + $"(stale value='{MaskForLog(duplicate.Value)}').");

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
                + $"({(wasExisting ? "updated" : "created")}).");

            updated++;
        }

        if (updated == 0)
        {
            Logger.Debug(
                "ApiClientView: extraction -- no rule matched a field of the response, nothing to save.");

            return;
        }

        try
        {
            _collectionStorage.SaveEnvironment(environment);

            Logger.Info(
                $"ApiClientView: environment '{environment.Name}' updated ({updated} variable(s) extracted from the response) -> file '{environment.FilePath}'.");
        }
        catch (Exception ex)
        {
            Logger.Error("ApiClientView: failed to update the environment after extraction.", ex);
        }
    }

    private static string
    MaskForLog(
        string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "(empty)";
        }

        return value.Length <= 12
            ? value
            : $"{value[..12]}…({value.Length} chars)";
    }

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

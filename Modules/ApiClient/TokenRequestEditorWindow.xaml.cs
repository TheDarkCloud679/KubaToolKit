using KubaToolKit.Modules.ApiClient.Models;
using KubaToolKit.Shared.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using KubaToolKit.Shared.Windows;

namespace KubaToolKit.Modules.ApiClient;

public partial class TokenRequestEditorWindow
    : Window
{
    private readonly CollectionStorageService _storage;
    private readonly EnvironmentSet _environment;
    private readonly ObservableCollection<HeaderItem> _headers = new();
    private readonly ObservableCollection<HeaderItem> _bodyFormData = new();
    private readonly ObservableCollection<HeaderItem> _extractions = new();

    public bool Saved { get; private set; }

    public TokenRequestEditorWindow(
        CollectionStorageService storage,
        EnvironmentSet environment)
    {
        InitializeComponent();

        _storage = storage;
        _environment = environment;

        SubtitleText.Text = $"Saved with environment \"{environment.Name}\", kept in memory until you change it.";

        HeadersGrid.ItemsSource = _headers;
        BodyFormGrid.ItemsSource = _bodyFormData;
        ExtractionsGrid.ItemsSource = _extractions;

        var node = environment.TokenRequestConfig;

        if (node == null)
        {
            return;
        }

        MethodCombo.SelectedItem =
            MethodCombo.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(i => string.Equals(i.Content as string, node.Method, StringComparison.OrdinalIgnoreCase))
            ?? MethodCombo.Items[1];

        UrlTextBox.Text = node.Url;

        foreach (var header in node.Headers)
        {
            _headers.Add(new HeaderItem { Enabled = header.Enabled, Key = header.Key, Value = header.Value });
        }

        foreach (var field in node.BodyFormData)
        {
            _bodyFormData.Add(new HeaderItem { Enabled = field.Enabled, Key = field.Key, Value = field.Value });
        }

        foreach (var rule in node.PostResponseExtractions)
        {
            _extractions.Add(new HeaderItem { Enabled = rule.Enabled, Key = rule.Key, Value = rule.Value });
        }

        BodyRawTextBox.Text = node.Body;

        (node.BodyMode switch
        {
            "none" => BodyNoneRadio,
            "formdata" => BodyFormDataRadio,
            "urlencoded" => BodyUrlEncodedRadio,
            _ => BodyRawRadio
        }).IsChecked = true;

        AuthTypeCombo.SelectedIndex = node.Auth.Type switch
        {
            AuthType.Bearer => 1,
            AuthType.Basic => 2,
            AuthType.ApiKey => 3,
            _ => 0
        };

        BearerTokenTextBox.Text = node.Auth.BearerToken;
        BasicUsernameTextBox.Text = node.Auth.Username;
        BasicPasswordBox.Password = node.Auth.Password;
        ApiKeyNameTextBox.Text = string.IsNullOrEmpty(node.Auth.ApiKeyName) ? "X-API-Key" : node.Auth.ApiKeyName;
        ApiKeyValueTextBox.Text = node.Auth.ApiKeyValue;
    }

    // AuthTabRadio/BodyRawRadio carry IsChecked="True" in XAML, so their
    // Checked handlers fire during InitializeComponent's sequential element
    // connection, before later-declared named elements in this same file
    // are wired up. Guard on the last element each handler touches and
    // rely on XAML-default Visibility for the correct initial state.
    private void
    Tab_Changed(
        object sender,
        RoutedEventArgs e)
    {
        if (ExtractionTabContent == null)
        {
            return;
        }

        AuthTabContent.Visibility =
            AuthTabRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        HeadersTabContent.Visibility =
            HeadersTabRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        BodyTabContent.Visibility =
            BodyTabRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        ExtractionTabContent.Visibility =
            ExtractionTabRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void
    BodyMode_Checked(
        object sender,
        RoutedEventArgs e)
    {
        if (BodyFormPanel == null)
        {
            return;
        }

        var mode = GetSelectedBodyMode();

        BodyNonePanel.Visibility =
            mode == "none" ? Visibility.Visible : Visibility.Collapsed;

        BodyRawTextBox.Visibility =
            mode == "raw" ? Visibility.Visible : Visibility.Collapsed;

        BodyFormPanel.Visibility =
            mode is "formdata" or "urlencoded" ? Visibility.Visible : Visibility.Collapsed;
    }

    private string
    GetSelectedBodyMode()
    {
        if (BodyNoneRadio?.IsChecked == true) return "none";
        if (BodyFormDataRadio?.IsChecked == true) return "formdata";
        if (BodyUrlEncodedRadio?.IsChecked == true) return "urlencoded";

        return "raw";
    }

    private void
    AuthTypeCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (BearerAuthPanel == null)
        {
            return;
        }

        BearerAuthPanel.Visibility =
            AuthTypeCombo.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;

        BasicAuthPanel.Visibility =
            AuthTypeCombo.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;

        ApiKeyAuthPanel.Visibility =
            AuthTypeCombo.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void
    AddHeaderRow_Click(
        object sender,
        RoutedEventArgs e)
    {
        _headers.Add(new HeaderItem());
    }

    private void
    AddBodyFormRow_Click(
        object sender,
        RoutedEventArgs e)
    {
        _bodyFormData.Add(new HeaderItem());
    }

    private void
    AddExtractionRow_Click(
        object sender,
        RoutedEventArgs e)
    {
        _extractions.Add(new HeaderItem());
    }

    private void
    DeleteRow_Click(
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

    private void
    SaveButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        HeadersGrid.CommitEdit(DataGridEditingUnit.Row, true);
        BodyFormGrid.CommitEdit(DataGridEditingUnit.Row, true);
        ExtractionsGrid.CommitEdit(DataGridEditingUnit.Row, true);

        if (string.IsNullOrWhiteSpace(UrlTextBox.Text.Trim()))
        {
            AppMessageBox.Show(
                "Enter a URL for the Get Token request.");

            return;
        }

        var mode = GetSelectedBodyMode();

        _environment.TokenRequestConfig =
            new CollectionNode
            {
                Name = "Token Request",
                IsRequest = true,
                Method = (MethodCombo.SelectedItem as ComboBoxItem)?.Content as string ?? "POST",
                Url = UrlTextBox.Text.Trim(),

                Headers =
                    _headers
                        .Select(h => new HeaderItem { Enabled = h.Enabled, Key = h.Key, Value = h.Value })
                        .ToList(),

                Body = BodyRawTextBox.Text,
                BodyMode = mode,

                BodyFormData =
                    _bodyFormData
                        .Select(f => new HeaderItem { Enabled = f.Enabled, Key = f.Key, Value = f.Value })
                        .ToList(),

                Auth = BuildAuthConfig(),

                PostResponseExtractions =
                    _extractions
                        .Select(x => new HeaderItem { Enabled = x.Enabled, Key = x.Key, Value = x.Value })
                        .ToList()
            };

        try
        {
            _storage.SaveEnvironment(_environment);
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(
                ex.Message,
                "Save error");

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
}

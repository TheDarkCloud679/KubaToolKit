using KubaToolKit.Modules.ApiClient.Models;
using KubaToolKit.Shared.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace KubaToolKit.Modules.ApiClient;

public partial class ApiClientView
    : UserControl
{
    private readonly ApiClientService _apiClientService = new();
    private readonly ObservableCollection<HeaderItem> _headers = new();
    private CancellationTokenSource? _sendCancellation;

    public ApiClientView()
    {
        InitializeComponent();

        HeadersGrid.ItemsSource = _headers;

        _headers.Add(
            new HeaderItem { Key = "Content-Type", Value = "application/json" });
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

        // Le DataGrid garde une ligne d'édition en cours (vide) tant
        // qu'elle n'a pas perdu le focus ; on force sa validation pour
        // qu'elle soit bien incluse dans _headers.
        HeadersGrid.CommitEdit(DataGridEditingUnit.Row, true);

        try
        {
            StatusBadge.Visibility = Visibility.Collapsed;
            TimingTextBlock.Text = "";
            SendProgressBar.Visibility = Visibility.Visible;
            SendButton.IsEnabled = false;

            _sendCancellation?.Cancel();
            _sendCancellation = new CancellationTokenSource();

            var auth = BuildAuthConfig();

            var result =
                await _apiClientService.SendAsync(
                    method,
                    url,
                    _headers.ToList(),
                    BodyTextBox.Text,
                    auth,
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

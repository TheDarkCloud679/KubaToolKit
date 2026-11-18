using KubaToolKit.Modules.ApiClient.Models;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace KubaToolKit.Modules.ApiClient;

public partial class FolderAuthWindow
    : Window
{
    private readonly CollectionNode _node;
    private readonly ObservableCollection<HeaderItem> _defaultHeaders = new();

    public bool Saved { get; private set; }

    public FolderAuthWindow(
        CollectionNode node)
    {
        InitializeComponent();

        _node = node;

        TitleText.Text = $"Authentication & Default Headers — {node.Name}";

        BearerTokenTextBox.Text = node.Auth.BearerToken;
        BasicUsernameTextBox.Text = node.Auth.Username;
        BasicPasswordBox.Password = node.Auth.Password;
        ApiKeyNameTextBox.Text = string.IsNullOrEmpty(node.Auth.ApiKeyName) ? "X-API-Key" : node.Auth.ApiKeyName;
        ApiKeyValueTextBox.Text = node.Auth.ApiKeyValue;

        AuthTypeCombo.SelectedIndex = node.Auth.Type switch
        {
            AuthType.None => 1,
            AuthType.Bearer => 2,
            AuthType.Basic => 3,
            AuthType.ApiKey => 4,
            _ => 0
        };

        DefaultHeadersGrid.ItemsSource = _defaultHeaders;

        foreach (var header in node.DefaultHeaders)
        {
            _defaultHeaders.Add(
                new HeaderItem { Enabled = header.Enabled, Key = header.Key, Value = header.Value });
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
    }

    private void
    SaveButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        DefaultHeadersGrid.CommitEdit(DataGridEditingUnit.Row, true);

        var type = AuthTypeCombo.SelectedIndex switch
        {
            1 => AuthType.None,
            2 => AuthType.Bearer,
            3 => AuthType.Basic,
            4 => AuthType.ApiKey,
            _ => AuthType.Inherit
        };

        _node.Auth = new AuthConfig
        {
            Type = type,
            BearerToken = BearerTokenTextBox.Text,
            Username = BasicUsernameTextBox.Text,
            Password = BasicPasswordBox.Password,
            ApiKeyName = ApiKeyNameTextBox.Text,
            ApiKeyValue = ApiKeyValueTextBox.Text
        };

        _node.DefaultHeaders =
            _defaultHeaders
                .Where(h => !string.IsNullOrWhiteSpace(h.Key))
                .Select(h => new HeaderItem { Enabled = h.Enabled, Key = h.Key, Value = h.Value })
                .ToList();

        Saved = true;

        Close();
    }

    private void
    DeleteDefaultHeaderRow_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is Button button
            && button.DataContext is HeaderItem item)
        {
            _defaultHeaders.Remove(item);
        }
    }

    private void
    CancelButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }
}

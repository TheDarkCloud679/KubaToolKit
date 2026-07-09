using KubaToolKit.Modules.ApiClient.Models;
using System.Windows;
using System.Windows.Controls;

namespace KubaToolKit.Modules.ApiClient;

public partial class FolderAuthWindow
    : Window
{
    private readonly CollectionNode _node;

    public bool Saved { get; private set; }

    public FolderAuthWindow(
        CollectionNode node)
    {
        InitializeComponent();

        _node = node;

        TitleText.Text = $"Authentification — {node.Name}";

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

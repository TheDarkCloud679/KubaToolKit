using KubaToolKit.Modules.KnowledgeSearch.Models;
using System.Windows;
using System.Windows.Media;

namespace KubaToolKit.Modules.KnowledgeSearch;

public partial class AtlassianSettingsWindow
    : Window
{
    private readonly AtlassianService _atlassianService = new();
    private bool _saved;

    public AtlassianSettingsWindow(
        AtlassianSettings existing)
    {
        InitializeComponent();

        BaseUrlTextBox.Text = existing.BaseUrl;
        EmailTextBox.Text = existing.Email;
        ApiTokenTextBox.Text = existing.ApiToken;
    }

    public AtlassianSettings? Result { get; private set; }

    public static AtlassianSettings?
    Prompt(
        Window? owner,
        AtlassianSettings existing)
    {
        var window = new AtlassianSettingsWindow(existing) { Owner = owner };

        window.ShowDialog();

        return window._saved ? window.Result : null;
    }

    private AtlassianSettings
    ReadFromFields() =>
        new()
        {
            BaseUrl = BaseUrlTextBox.Text.Trim(),
            Email = EmailTextBox.Text.Trim(),
            ApiToken = ApiTokenTextBox.Text.Trim()
        };

    private async void
    TestConnectionButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var settings = ReadFromFields();

        if (!settings.IsComplete)
        {
            TestResultText.Text = "Fill in all three fields first.";
            TestResultText.Foreground = (Brush)FindResource("TextMutedBrush");

            return;
        }

        TestResultText.Text = "Testing...";
        TestResultText.Foreground = (Brush)FindResource("TextMutedBrush");

        var (success, message) = await _atlassianService.TestConnection(settings);

        TestResultText.Text = message;
        TestResultText.Foreground =
            (Brush)FindResource(success ? "SuccessBrush" : "DangerBrush");
    }

    private void
    SaveButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Result = ReadFromFields();
        _saved = true;

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

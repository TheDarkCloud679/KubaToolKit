using KubaToolKit.Shared.Services;
using System.Windows;

namespace KubaToolKit.Shared.Windows;

public partial class JsonViewerWindow
    : Window
{
    private readonly string
        _rawMessage;

    public JsonViewerWindow(
        string message)
    {
        InitializeComponent();

        _rawMessage =
            message;

        LoadJson();
    }

    private void
        LoadJson()
    {
        try
        {
            JsonTextBox.Text =
                JsonFormattingHelper.FormatJson(
                    _rawMessage);

            JsonTextBox.SyntaxHighlighting =
                null;

            JsonTextBox.TextArea
                .TextView
                .LineTransformers
                .Clear();

            JsonTextBox.TextArea
                .TextView
                .LineTransformers
                .Add(
                    new JsonFormattingHelper.JsonColorizer());

            JsonTextBox.TextArea
                .TextView
                .Redraw();

            JsonInfoText.Text =
                $"{JsonTextBox.LineCount} lines • {JsonTextBox.Text.Length:N0} chars";
        }
        catch
        {
            JsonTextBox.Text =
                _rawMessage;
        }
    }

    private void
        CopyButton_Click(
            object sender,
            RoutedEventArgs e)
    {
        Clipboard.SetText(
            JsonTextBox.Text);
    }
}

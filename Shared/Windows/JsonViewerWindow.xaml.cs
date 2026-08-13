using ICSharpCode.AvalonEdit.Editing;
using KubaToolKit.Shared.Services;
using System.Windows;

namespace KubaToolKit.Shared.Windows;

public partial class JsonViewerWindow
    : Window
{
    private readonly string
        _rawMessage;

    public JsonViewerWindow(
        string message,
        string? subtitle = null)
    {
        InitializeComponent();

        _rawMessage =
            message;

        SubtitleText.Text =
            subtitle ?? "";

        SubtitleText.Visibility =
            string.IsNullOrWhiteSpace(subtitle)
                ? Visibility.Collapsed
                : Visibility.Visible;

        RemoveLineNumberSeparator();

        LoadJson();
    }

    // ShowLineNumbers="True" adds both a LineNumberMargin and a dotted
    // vertical separator line next to it -- keep the numbers (asked for
    // "prettier", not gone), drop the dotted-line-editor look that
    // doesn't match anything else in the app.
    private void
    RemoveLineNumberSeparator()
    {
        var leftMargins =
            JsonTextBox.TextArea.LeftMargins;

        for (var i = leftMargins.Count - 1; i >= 0; i--)
        {
            if (DottedLineMargin.IsDottedLineMargin(leftMargins[i]))
            {
                leftMargins.RemoveAt(i);
            }
        }
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

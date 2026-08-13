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

        ConfigureLineNumberMargin();

        LoadJson();
    }

    // ShowLineNumbers="True" adds both a LineNumberMargin and a dotted
    // vertical separator line next to it -- keep the numbers (asked for
    // "prettier", not gone), drop the dotted-line-editor look that
    // doesn't match anything else in the app. That separator was also
    // the only thing giving the numbers any breathing room on either
    // side, though, so removing it left them jammed against the card's
    // left edge and the code text with no gap -- give LineNumberMargin
    // its own Margin instead now that it's the sole element there.
    private void
    ConfigureLineNumberMargin()
    {
        var leftMargins =
            JsonTextBox.TextArea.LeftMargins;

        for (var i = leftMargins.Count - 1; i >= 0; i--)
        {
            if (DottedLineMargin.IsDottedLineMargin(leftMargins[i]))
            {
                leftMargins.RemoveAt(i);

                continue;
            }

            if (leftMargins[i] is LineNumberMargin lineNumberMargin)
            {
                lineNumberMargin.Margin =
                    new Thickness(4, 0, 14, 0);
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

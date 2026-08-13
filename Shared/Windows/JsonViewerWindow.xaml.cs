using ICSharpCode.AvalonEdit.Editing;
using KubaToolKit.Shared.Services;
using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace KubaToolKit.Shared.Windows;

public partial class JsonViewerWindow
    : Window
{
    private const string
        CopyIconData = "M8,8 L20,8 L20,20 L8,20 Z M16,8 L16,5 A2,2 0 0 0 14,3 L5,3 A2,2 0 0 0 3,5 L3,14 A2,2 0 0 0 5,16 L8,16";

    private const string
        CheckIconData = "M4,12 L10,18 L20,6";

    private readonly string
        _rawMessage;

    private DispatcherTimer?
        _copyResetTimer;

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

        CopyButtonIcon.Data =
            Geometry.Parse(CheckIconData);

        CopyButtonText.Text =
            "Copied";

        _copyResetTimer?.Stop();

        _copyResetTimer =
            new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1600)
            };

        _copyResetTimer.Tick +=
            (_, _) =>
            {
                CopyButtonIcon.Data =
                    Geometry.Parse(CopyIconData);

                CopyButtonText.Text =
                    "Copy";

                _copyResetTimer!.Stop();
            };

        _copyResetTimer.Start();
    }
}

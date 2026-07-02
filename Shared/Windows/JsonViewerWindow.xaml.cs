using ICSharpCode.AvalonEdit.Highlighting;
using System.Text.Json;
using System.Windows;
using ICSharpCode.AvalonEdit.Rendering;
using ICSharpCode.AvalonEdit.Document;
using System.Text.RegularExpressions;
using System.Windows.Media;

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
                FormatMessage(
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
                    new JsonColorizer());

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

    private string
    FormatMessage(
        string message)
    {
        try
        {
            using var document =
                JsonDocument.Parse(
                    message);

            var cleaned =
                ParseNestedJson(
                    document.RootElement);

            return JsonSerializer
                .Serialize(
                    cleaned,
                    new JsonSerializerOptions
                    {
                        WriteIndented =
                            true
                    });
        }
        catch
        {
            return message;
        }
    }

    private object?
        ParseNestedJson(
            JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:

                var obj =
                    new Dictionary<string, object?>();

                foreach (var property
                         in element
                             .EnumerateObject())
                {
                    obj[property.Name] =
                        ParseNestedJson(
                            property.Value);
                }

                return obj;

            case JsonValueKind.Array:

                return element
                    .EnumerateArray()
                    .Select(ParseNestedJson)
                    .ToList();

            case JsonValueKind.String:

                var stringValue =
                    element.GetString();

                if (string.IsNullOrWhiteSpace(
                        stringValue))
                {
                    return stringValue;
                }

                // Détecte JSON stringifié
                if ((stringValue.StartsWith("{")
                     && stringValue.EndsWith("}"))
                    ||
                    (stringValue.StartsWith("[")
                     && stringValue.EndsWith("]")))
                {
                    try
                    {
                        using var nestedDoc =
                            JsonDocument.Parse(
                                stringValue);

                        return ParseNestedJson(
                            nestedDoc.RootElement);
                    }
                    catch
                    {
                    }
                }

                return stringValue;

            case JsonValueKind.Number:

                if (element.TryGetInt64(
                        out var longValue))
                {
                    return longValue;
                }

                return element.GetDouble();

            case JsonValueKind.True:
                return true;

            case JsonValueKind.False:
                return false;

            case JsonValueKind.Null:
                return null;

            default:
                return element.ToString();
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
    public class JsonColorizer
    : DocumentColorizingTransformer
    {
        protected override void
            ColorizeLine(
                DocumentLine line)
        {
            var text =
                CurrentContext.Document
                    .GetText(line);

            // Clés JSON
            HighlightRegex(
                line,
                text,
                "\"[^\"]+\"(?=\\s*:)",
                Brushes.RoyalBlue);

            // Strings
            HighlightRegex(
                line,
                text,
                ":\\s*\".*?\"",
                Brushes.IndianRed);

            // Numbers
            HighlightRegex(
                line,
                text,
                @"\b\d+\b",
                Brushes.MediumPurple);

            // true false null
            HighlightRegex(
                line,
                text,
                @"\b(true|false|null)\b",
                Brushes.DarkCyan);

            // Brackets
            HighlightRegex(
                line,
                text,
                @"[\{\}\[\]]",
                Brushes.Gray);
        }

        private void
            HighlightRegex(
                DocumentLine line,
                string text,
                string pattern,
                Brush brush)
        {
            foreach (Match match
                     in Regex.Matches(
                         text,
                         pattern))
            {
                ChangeLinePart(
                    line.Offset
                    + match.Index,

                    line.Offset
                    + match.Index
                    + match.Length,

                    element =>
                    {
                        element.TextRunProperties
                            .SetForegroundBrush(
                                brush);
                    });
            }
        }
    }
}
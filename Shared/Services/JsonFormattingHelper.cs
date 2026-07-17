using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace KubaToolKit.Shared.Services;

public static class JsonFormattingHelper
{
    public static string
    FormatJson(
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

    private static object?
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

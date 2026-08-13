using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace KubaToolKit.Shared.Behaviors;

// Lightweight, regex-based tinting for JSON-ish log messages inside a
// TextBlock (Text alone can't hold colored Inlines via a plain binding).
// Not a real JSON parser, just tags "key": / "string" / number-bool-null
// / punctuation tokens by pattern, so it degrades gracefully on
// non-JSON messages (plain text just comes through untinted) instead of
// throwing on malformed input.
public static class JsonHighlightBehavior
{
    private static readonly Regex TokenPattern = new(
        """("[^"\\]*(?:\\.[^"\\]*)*"\s*:)|("[^"\\]*(?:\\.[^"\\]*)*")|(\btrue\b|\bfalse\b|\bnull\b|-?\d+\.?\d*)|([{}\[\]:,])""",
        RegexOptions.Compiled);

    private static readonly Brush KeyBrush = (Brush)new BrushConverter().ConvertFromString("#0C8599")!;
    private static readonly Brush StringBrush = (Brush)new BrushConverter().ConvertFromString("#1E9E6B")!;
    private static readonly Brush LiteralBrush = (Brush)new BrushConverter().ConvertFromString("#C2540C")!;
    private static readonly Brush PunctuationBrush = (Brush)new BrushConverter().ConvertFromString("#8B8F98")!;

    static JsonHighlightBehavior()
    {
        KeyBrush.Freeze();
        StringBrush.Freeze();
        LiteralBrush.Freeze();
        PunctuationBrush.Freeze();
    }

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached(
            "Text",
            typeof(string),
            typeof(JsonHighlightBehavior),
            new PropertyMetadata(null, OnTextChanged));

    public static void
    SetText(
        DependencyObject element,
        string value)
    {
        element.SetValue(TextProperty, value);
    }

    public static string
    GetText(
        DependencyObject element)
    {
        return (string)element.GetValue(TextProperty);
    }

    private static void
    OnTextChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock textBlock)
        {
            return;
        }

        textBlock.Inlines.Clear();

        var text = e.NewValue as string ?? "";

        if (text.Length == 0)
        {
            return;
        }

        var lastIndex = 0;

        foreach (Match match in TokenPattern.Matches(text))
        {
            if (match.Index > lastIndex)
            {
                textBlock.Inlines.Add(
                    new Run(text[lastIndex..match.Index]));
            }

            var brush =
                match.Value is "{" or "}" or "[" or "]" or ":" or "," ? PunctuationBrush
                : match.Value.StartsWith('"') && match.Value.EndsWith(':') ? KeyBrush
                : match.Value.StartsWith('"') ? StringBrush
                : LiteralBrush;

            textBlock.Inlines.Add(
                new Run(match.Value) { Foreground = brush });

            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < text.Length)
        {
            textBlock.Inlines.Add(
                new Run(text[lastIndex..]));
        }
    }
}

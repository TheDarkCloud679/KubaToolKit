using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace KubaToolKit.Modules.CloudWatchLogs;

public class CloudWatchColorizer
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
            @"\b(fields|filter|sort|limit|parse|stats|display|dedup|asc|desc|like|in)\b",
            Brushes.DodgerBlue);

        HighlightRegex(
            line,
            text,
            @"@\w+",
            Brushes.DeepSkyBlue);

        HighlightRegex(
            line,
            text,
            @"'[^']*'",
            Brushes.Peru);

        HighlightRegex(
            line,
            text,
            @"\/.*?\/",
            Brushes.IndianRed);

        HighlightRegex(
            line,
            text,
            @"\b\d+\b",
            Brushes.MediumPurple);

        HighlightRegex(
            line,
            text,
            @"\|",
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
                     pattern,
                     RegexOptions.IgnoreCase))
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

using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Highlighting;
using KubaToolKit.Shared.Services;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace KubaToolKit.Shared.Windows;

public partial class
    FileViewerWindow
    : Window
{
    private const string
        CopyIconData = "M8,8 L20,8 L20,20 L8,20 Z M16,8 L16,5 A2,2 0 0 0 14,3 L5,3 A2,2 0 0 0 3,5 L3,14 A2,2 0 0 0 5,16 L8,16";

    private const string
        CheckIconData = "M4,12 L10,18 L20,6";

    private string _lastSearchText = string.Empty;

    private List<int> _searchMatches = new();
    private int _currentMatchIndex = -1;

    private DispatcherTimer?
        _copyResetTimer;

    public
    FileViewerWindow(
        string title,
        string content,
        string? subtitle = null)
    {
        InitializeComponent();

        ContentEditor
            .Options
            .HighlightCurrentLine =
                true;

        ContentEditor
            .Options
            .ConvertTabsToSpaces =
                false;

        Title =
            title;

        FileNameTextBlock.Text =
            title;

        SubtitleText.Text =
            subtitle ?? "";

        SubtitleText.Visibility =
            string.IsNullOrWhiteSpace(subtitle)
                ? Visibility.Collapsed
                : Visibility.Visible;

        ConfigureLineNumberMargin();

        var formattedContent =
            FormatContent(
                content);

        ContentEditor.Text =
            formattedContent;

        ApplySyntaxHighlighting(
            title,
            formattedContent);

        FileInfoText.Text =
            $"{ContentEditor.LineCount} lines • {formattedContent.Length:N0} chars";

        _cardsView =
            JsonCardViewBuilder.Build(formattedContent);

        if (_cardsView != null)
        {
            CardsContent.Content =
                _cardsView.Root;

            ViewModeRow.Visibility =
                Visibility.Visible;

            // Setting IsChecked here (rather than via IsChecked="True" in
            // XAML) fires ViewMode_Changed only now, after InitializeComponent
            // has already wired up ContentEditor/CardsScrollViewer -- doing
            // it from XAML fired the Checked event mid-parse, before those
            // fields existed yet, and crashed with a NullReferenceException.
            CardsViewRadio.IsChecked =
                true;
        }

        PreviewKeyDown +=
        FileViewerWindow_PreviewKeyDown;
    }

    private JsonCardViewResult?
        _cardsView;

    private void
    ViewMode_Changed(
        object sender,
        RoutedEventArgs e)
    {
        var showCards =
            CardsViewRadio.IsChecked == true;

        CardsScrollViewer.Visibility =
            showCards ? Visibility.Visible : Visibility.Collapsed;

        ContentEditor.Visibility =
            showCards ? Visibility.Collapsed : Visibility.Visible;
    }

    // Same fix as JsonViewerWindow: ShowLineNumbers="True" pairs the line
    // numbers with a dotted separator line that doesn't match anything
    // else in the app -- drop the separator, give the numbers their own
    // margin so they don't end up jammed against the card edge/text.
    private void
    ConfigureLineNumberMargin()
    {
        var leftMargins =
            ContentEditor.TextArea.LeftMargins;

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
ContentEditor_ContextMenuOpening(
    object sender,
    ContextMenuEventArgs e)
    {
        CopySelectedMenuItem.IsEnabled =
            !string.IsNullOrWhiteSpace(
                ContentEditor.SelectedText);
    }

    private void
FileViewerWindow_PreviewKeyDown(
    object sender,
    KeyEventArgs e)
    {
        if (e.Key == Key.F3)
        {
            FindNext_Click(
                sender,
                new RoutedEventArgs());

            e.Handled = true;
        }
        if (e.Key == Key.F
            &&
            Keyboard.Modifiers
                == ModifierKeys.Control)
        {
            // Search only operates on the raw text -- switch out of Cards
            // view first so the highlighted match is actually visible.
            if (_cardsView != null)
            {
                RawViewRadio.IsChecked =
                    true;
            }

            SearchPanel.Visibility =
                Visibility.Visible;

            SearchTextBox.Focus();

            SearchTextBox.SelectAll();

            e.Handled =
                true;

            return;
        }

        if (e.Key == Key.Escape)
        {
            SearchPanel.Visibility =
                Visibility.Collapsed;
            ContentEditor.Focus();

            e.Handled = true;
        }
    }

    private void
ApplySyntaxHighlighting(
    string fileName,
    string content)
    {
        var extension =
            Path.GetExtension(
                fileName)
            .ToLowerInvariant();

        switch (extension)
        {
            case ".json":
                ContentEditor.SyntaxHighlighting =
                    HighlightingManager
                        .Instance
                        .GetDefinition(
                            "JavaScript");
                break;

            case ".xml":
                ContentEditor.SyntaxHighlighting =
                    HighlightingManager
                        .Instance
                        .GetDefinition(
                            "XML");
                break;

            case ".sql":
                ContentEditor.SyntaxHighlighting =
                    HighlightingManager
                        .Instance
                        .GetDefinition(
                            "SQL");
                break;

            case ".cs":
                ContentEditor.SyntaxHighlighting =
                    HighlightingManager
                        .Instance
                        .GetDefinition(
                            "C#");
                break;

            case ".html":
                ContentEditor.SyntaxHighlighting =
                    HighlightingManager
                        .Instance
                        .GetDefinition(
                            "HTML");
                break;

            case ".js":
                ContentEditor.SyntaxHighlighting =
                    HighlightingManager
                        .Instance
                        .GetDefinition(
                            "JavaScript");
                break;

            case ".log":
            case ".txt":
                var trimmed =
                    content.TrimStart();

                if (trimmed.StartsWith("{")
                    ||
                    trimmed.StartsWith("["))
                {
                    ContentEditor
                        .SyntaxHighlighting =
                            HighlightingManager
                                .Instance
                                .GetDefinition(
                                    "JavaScript");
                }
                break;
        }
    }

   private void
   CopyAll_Click(object sender, RoutedEventArgs e)
    {
        ContentEditor.SelectAll();
        Clipboard.SetText(
        ContentEditor.Text);
        ContentEditor.Focus();

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

    private void
CopySelected_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(
                ContentEditor.SelectedText))
        {
            return;
        }
        Clipboard.SetText(ContentEditor.SelectedText);
    }
    private void
FindNext_Click(
    object sender,
    RoutedEventArgs e)
    {
        var searchText =
            SearchTextBox.Text;

        if (string.IsNullOrWhiteSpace(
                searchText))
        {
            return;
        }

        var text =
            ContentEditor.Text;

        if (_lastSearchText != searchText)
        {
            _lastSearchText =
                searchText;

            _searchMatches.Clear();

            _currentMatchIndex =
                -1;

            var startIndex =
                0;

            while (true)
            {
                var found =
                    text.IndexOf(
                        searchText,
                        startIndex,
                        StringComparison.OrdinalIgnoreCase);

                if (found < 0)
                {
                    break;
                }

                _searchMatches.Add(
                    found);

                startIndex =
                    found + 1;
            }
        }

        if (_searchMatches.Count == 0)
        {
            SearchCountText.Text =
                "0 / 0";

            MessageBox.Show(
                "Text not found.");

            return;
        }

        _currentMatchIndex++;

        if (_currentMatchIndex >= _searchMatches.Count)
        {
            _currentMatchIndex =
                0;
        }

        var index =
            _searchMatches[
                _currentMatchIndex];

        SearchCountText.Text =
            $"{_currentMatchIndex + 1} / {_searchMatches.Count}";

        ContentEditor.Select(
            index,
            searchText.Length);

        ContentEditor.ScrollToLine(
            ContentEditor.Document
                .GetLineByOffset(index)
                .LineNumber);

        SearchTextBox.Focus();
    }

    private void
SearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        FindNext_Click(sender, new RoutedEventArgs());
        e.Handled = true;
    }

    private void
FindPrevious_Click(
    object sender,
    RoutedEventArgs e)
    {
    }

    private string
    FormatContent(
        string content)
    {
        try
        {
            var trimmed =
                content.TrimStart();

            if (trimmed.StartsWith("{")
                || trimmed.StartsWith("["))
            {
                using var doc =
                    JsonDocument.Parse(
                        content);

                return JsonSerializer.Serialize(
                    doc.RootElement,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
            }

            return content;
        }
        catch
        {
            return content;
        }
    }
}
using ICSharpCode.AvalonEdit.Highlighting;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace KubaToolKit.Shared.Windows;

public partial class
    FileViewerWindow
    : Window
{
    private string _lastSearchText = string.Empty;

    private List<int> _searchMatches = new();
    private int _currentMatchIndex = -1;

    public
    FileViewerWindow(
        string title,
        string content)
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

        var formattedContent =
            FormatContent(
                content);

        ContentEditor.Text =
            formattedContent;

        ApplySyntaxHighlighting(
            title,
            formattedContent);
        PreviewKeyDown +=
        FileViewerWindow_PreviewKeyDown;
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
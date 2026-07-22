using KubaToolKit.Modules.ApiClient;
using KubaToolKit.Modules.ProjectInfo;
using KubaToolKit.Modules.Wiki.Models;
using KubaToolKit.Shared.Services;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace KubaToolKit.Modules.Wiki;

public partial class WikiWindow
    : Window
{
    private readonly WikiService _wikiService = new();
    private readonly string _profileName;
    private readonly WikiProject _project;

    private WikiSection? _currentSection;
    private bool _loadingSection;

    private readonly DispatcherTimer _saveDebounceTimer;

    public WikiWindow(
        string profileName)
    {
        InitializeComponent();

        _saveDebounceTimer =
            new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };

        _saveDebounceTimer.Tick += (_, __) =>
        {
            _saveDebounceTimer.Stop();
            Save();
        };

        Closing += (_, __) =>
        {
            if (_saveDebounceTimer.IsEnabled)
            {
                _saveDebounceTimer.Stop();
                Save();
            }
        };

        _profileName = profileName;

        // Same project key as Project Info (Prod/Preprod/Test sharing and
        // its migration both already live there), so the wiki lines up
        // with whatever project the user already set/uses there.
        var projectInfoService = new ProjectInfoService();
        var projectInfoRoot = projectInfoService.Load();
        var projectKey = projectInfoService.ResolveProjectKey(projectInfoRoot, profileName);

        _project = _wikiService.LoadProject(projectKey);

        Title = $"Wiki - {_project.Key} (profile: {_profileName})";
        TitleTextBlock.Text = Title;

        SectionsListBox.ItemsSource = _project.Sections;

        if (_project.Sections.Count > 0)
        {
            SectionsListBox.SelectedIndex = 0;
        }

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                SearchTextBox.Focus();
                SearchTextBox.SelectAll();
                e.Handled = true;
            }
        };
    }

    private void
    AddSectionButton_Click(
        object sender,
        RoutedEventArgs e) =>
        AddSection(_project.Sections.Count);

    private void
    NewSectionNameTextBox_KeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AddSection(_project.Sections.Count);
        }
    }

    private void
    AddSectionAbove_Click(
        object sender,
        RoutedEventArgs e)
    {
        var index = SectionsListBox.SelectedItem is WikiSection s
            ? _project.Sections.IndexOf(s)
            : _project.Sections.Count;

        AddSection(Math.Max(0, index));
    }

    private void
    AddSectionBelow_Click(
        object sender,
        RoutedEventArgs e)
    {
        var index = SectionsListBox.SelectedItem is WikiSection s
            ? _project.Sections.IndexOf(s) + 1
            : _project.Sections.Count;

        AddSection(index);
    }

    private void
    AddSection(
        int index)
    {
        var name = NewSectionNameTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            name = $"Section {_project.Sections.Count + 1}";
        }

        if (_project.Sections.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show($"A section \"{name}\" already exists.", "Wiki");

            return;
        }

        var section = new WikiSection { Name = name };

        _project.Sections.Insert(Math.Clamp(index, 0, _project.Sections.Count), section);

        NewSectionNameTextBox.Text = "";

        RefreshSectionsList();
        SectionsListBox.SelectedItem = section;

        Save();
    }

    private void
    RenameSection_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (SectionsListBox.SelectedItem is not WikiSection section)
        {
            return;
        }

        var newName =
            TextInputWindow.Prompt(this, "Rename section", "New name:", section.Name);

        if (string.IsNullOrWhiteSpace(newName)
            || string.Equals(newName, section.Name, StringComparison.Ordinal))
        {
            return;
        }

        if (_project.Sections.Any(s =>
                s != section && string.Equals(s.Name, newName, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show($"A section \"{newName}\" already exists.", "Wiki");

            return;
        }

        section.Name = newName;

        RefreshSectionsList();
        SectionsListBox.SelectedItem = section;
        TitleTextBlock.Text = Title;

        Save();
    }

    private void
    DeleteSection_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (SectionsListBox.SelectedItem is not WikiSection section)
        {
            return;
        }

        if (MessageBox.Show(
                $"Delete section \"{section.Name}\" and its content? Attached files are kept on disk.",
                "Confirm",
                MessageBoxButton.YesNo) != MessageBoxResult.Yes)
        {
            return;
        }

        _project.Sections.Remove(section);

        RefreshSectionsList();

        Save();
    }

    private void
    SortSectionsAscendingButton_Click(
        object sender,
        RoutedEventArgs e) =>
        SortSections(ascending: true);

    private void
    SortSectionsDescendingButton_Click(
        object sender,
        RoutedEventArgs e) =>
        SortSections(ascending: false);

    private void
    SortSections(
        bool ascending)
    {
        var selected = _currentSection;

        var sorted =
            ascending
                ? _project.Sections.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList()
                : _project.Sections.OrderByDescending(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();

        _project.Sections.Clear();
        _project.Sections.AddRange(sorted);

        RefreshSectionsList();

        SectionsListBox.SelectedItem = selected;

        Save();
    }

    private void
    SectionsContextMenu_Opened(
        object sender,
        RoutedEventArgs e)
    {
        var hasSelection = SectionsListBox.SelectedItem is WikiSection;

        RenameMenuItem.IsEnabled = hasSelection;
        DeleteMenuItem.IsEnabled = hasSelection;
    }

    private void
    RefreshSectionsList()
    {
        SectionsListBox.ItemsSource = null;
        SectionsListBox.ItemsSource = _project.Sections;

        // A stale match could point at a section that no longer exists
        // (or no longer matches after a rename), or would jump to the
        // wrong position after other sections got added/removed.
        _searchMatches.Clear();
        _currentMatchIndex = -1;
        _lastSearchQuery = "";
        SearchResultsText.Text = "";
    }

    private void
    SectionsListBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        _currentSection = SectionsListBox.SelectedItem as WikiSection;

        if (_currentSection == null)
        {
            ContentBorder.Visibility = Visibility.Collapsed;
            NoSelectionText.Visibility = Visibility.Visible;

            return;
        }

        ContentBorder.Visibility = Visibility.Visible;
        NoSelectionText.Visibility = Visibility.Collapsed;

        _loadingSection = true;

        try
        {
            ContentTextBox.Text = _currentSection.Text;
            ImageOnlyCheckBox.IsChecked = _currentSection.ImageOnlyMode;
        }
        finally
        {
            _loadingSection = false;
        }

        RefreshImages();
    }

    private void
    ContentTextBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        if (_loadingSection || _currentSection == null)
        {
            return;
        }

        _currentSection.Text = ContentTextBox.Text;

        ScheduleSave();
    }

    private void
    AddImageButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_currentSection == null)
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter =
                "Images and PDF (*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.pdf)|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.pdf",
            Multiselect = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var imagesFolder = WikiService.EnsureImagesFolder(_project.Key);

            foreach (var sourcePath in dialog.FileNames)
            {
                var fileName = CopyImageWithUniqueName(sourcePath, imagesFolder);

                _currentSection.ImageFileNames.Add(fileName);
            }

            RefreshImages();
            Save();
        }
        catch (Exception ex)
        {
            Logger.Error("WikiWindow: failed to attach image.", ex);

            MessageBox.Show(ex.ToString(), "Wiki - add image");
        }
    }

    private static string
    CopyImageWithUniqueName(
        string sourcePath,
        string imagesFolder)
    {
        var fileName = Path.GetFileName(sourcePath);
        var targetPath = Path.Combine(imagesFolder, fileName);
        var counter = 1;

        while (File.Exists(targetPath))
        {
            fileName =
                $"{Path.GetFileNameWithoutExtension(sourcePath)}_{counter}{Path.GetExtension(sourcePath)}";

            targetPath = Path.Combine(imagesFolder, fileName);
            counter++;
        }

        File.Copy(sourcePath, targetPath);

        return fileName;
    }

    private void
    RefreshImages()
    {
        ImagesPanel.Children.Clear();

        if (_currentSection == null)
        {
            return;
        }

        var imagesFolder = WikiService.GetImagesFolderPath(_project.Key);

        foreach (var fileName in _currentSection.ImageFileNames)
        {
            ImagesPanel.Children.Add(BuildThumbnail(fileName, Path.Combine(imagesFolder, fileName)));
        }

        UpdateContentModeVisibility();
    }

    private void
    ImageOnlyCheckBox_Changed(
        object sender,
        RoutedEventArgs e)
    {
        if (_loadingSection || _currentSection == null)
        {
            return;
        }

        _currentSection.ImageOnlyMode = ImageOnlyCheckBox.IsChecked == true;

        UpdateContentModeVisibility();
        Save();
    }

    private void
    UpdateContentModeVisibility()
    {
        if (_currentSection == null)
        {
            return;
        }

        var imageOnly = _currentSection.ImageOnlyMode;

        ContentTextBox.Visibility = imageOnly ? Visibility.Collapsed : Visibility.Visible;
        FeaturedImageBorder.Visibility = imageOnly ? Visibility.Visible : Visibility.Collapsed;

        if (!imageOnly)
        {
            return;
        }

        var firstImage =
            _currentSection.ImageFileNames.FirstOrDefault(f =>
                !string.Equals(Path.GetExtension(f), ".pdf", StringComparison.OrdinalIgnoreCase));

        if (firstImage == null)
        {
            FeaturedImage.Source = null;
            FeaturedImage.Visibility = Visibility.Collapsed;
            FeaturedImageEmptyText.Text = "No image attached yet -- add one below.";
            FeaturedImageEmptyText.Visibility = Visibility.Visible;

            return;
        }

        var fullPath = Path.Combine(WikiService.GetImagesFolderPath(_project.Key), firstImage);

        if (!File.Exists(fullPath))
        {
            FeaturedImage.Source = null;
            FeaturedImage.Visibility = Visibility.Collapsed;
            FeaturedImageEmptyText.Text = $"Missing file: {firstImage}";
            FeaturedImageEmptyText.Visibility = Visibility.Visible;

            return;
        }

        FeaturedImageEmptyText.Visibility = Visibility.Collapsed;
        FeaturedImage.Visibility = Visibility.Visible;

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(fullPath, UriKind.Absolute);
        bitmap.EndInit();

        FeaturedImage.Source = bitmap;

        // A newly loaded image (new section, or attachments changed)
        // starts fresh rather than keeping whatever zoom/pan was left
        // over from a previous one.
        ResetFeaturedImageZoom();
    }

    private Point? _featuredImagePanStart;
    private double _featuredImagePanStartHorizontalOffset;
    private double _featuredImagePanStartVerticalOffset;

    private void
    ResetFeaturedImageZoom()
    {
        FeaturedImageScale.ScaleX = 1;
        FeaturedImageScale.ScaleY = 1;

        FeaturedImageScrollViewer.ScrollToHorizontalOffset(0);
        FeaturedImageScrollViewer.ScrollToVerticalOffset(0);
    }

    private void
    FeaturedImageScrollViewer_PreviewMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        e.Handled = true;

        var oldScale = FeaturedImageScale.ScaleX;
        var zoomFactor = e.Delta > 0 ? 1.1 : 1.0 / 1.1;
        var newScale = Math.Clamp(oldScale * zoomFactor, 0.2, 8.0);

        if (newScale == oldScale)
        {
            return;
        }

        // Whatever was at the viewport's center stays at its center after
        // the zoom, instead of the content's top-left corner staying
        // pinned (which visually drags the zoom toward that corner).
        var viewportCenterX =
            FeaturedImageScrollViewer.HorizontalOffset + FeaturedImageScrollViewer.ViewportWidth / 2;

        var viewportCenterY =
            FeaturedImageScrollViewer.VerticalOffset + FeaturedImageScrollViewer.ViewportHeight / 2;

        var unscaledCenterX = viewportCenterX / oldScale;
        var unscaledCenterY = viewportCenterY / oldScale;

        FeaturedImageScale.ScaleX = newScale;
        FeaturedImageScale.ScaleY = newScale;

        // The ScrollViewer's extent only reflects the new scale after a
        // layout pass -- adjusting offsets before that would clamp
        // against the still-stale (pre-zoom) scrollable range.
        FeaturedImageScrollViewer.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() =>
            {
                FeaturedImageScrollViewer.ScrollToHorizontalOffset(
                    unscaledCenterX * newScale - FeaturedImageScrollViewer.ViewportWidth / 2);

                FeaturedImageScrollViewer.ScrollToVerticalOffset(
                    unscaledCenterY * newScale - FeaturedImageScrollViewer.ViewportHeight / 2);
            }));
    }

    private void
    FeaturedImage_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ResetFeaturedImageZoom();

            return;
        }

        _featuredImagePanStart = e.GetPosition(FeaturedImageScrollViewer);
        _featuredImagePanStartHorizontalOffset = FeaturedImageScrollViewer.HorizontalOffset;
        _featuredImagePanStartVerticalOffset = FeaturedImageScrollViewer.VerticalOffset;

        FeaturedImage.CaptureMouse();
    }

    private void
    FeaturedImage_MouseMove(
        object sender,
        MouseEventArgs e)
    {
        if (_featuredImagePanStart == null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(FeaturedImageScrollViewer);
        var delta = current - _featuredImagePanStart.Value;

        FeaturedImageScrollViewer.ScrollToHorizontalOffset(_featuredImagePanStartHorizontalOffset - delta.X);
        FeaturedImageScrollViewer.ScrollToVerticalOffset(_featuredImagePanStartVerticalOffset - delta.Y);
    }

    private void
    FeaturedImage_MouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        _featuredImagePanStart = null;

        FeaturedImage.ReleaseMouseCapture();
    }

    private FrameworkElement
    BuildThumbnail(
        string fileName,
        string fullPath)
    {
        var stack = new StackPanel
        {
            Width = 100,
            Margin = new Thickness(0, 0, 8, 8)
        };

        var imageBorder = new Border
        {
            Width = 100,
            Height = 80,
            BorderBrush = (System.Windows.Media.Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Cursor = Cursors.Hand,
            ToolTip = fileName
        };

        var isPdf = string.Equals(Path.GetExtension(fileName), ".pdf", StringComparison.OrdinalIgnoreCase);

        if (!File.Exists(fullPath))
        {
            imageBorder.Child = new TextBlock
            {
                Text = "(missing file)",
                FontSize = 10,
                FontStyle = FontStyles.Italic,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }
        else if (isPdf)
        {
            // WPF can't render a PDF preview without an extra library --
            // a plain icon still supports the same double-click-to-link/
            // right-click-to-open/remove behavior as an actual image.
            imageBorder.Child = new TextBlock
            {
                Text = "📄 PDF",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }
        else
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 100;
            bitmap.UriSource = new Uri(fullPath, UriKind.Absolute);
            bitmap.EndInit();

            imageBorder.Child = new Image
            {
                Source = bitmap,
                Stretch = Stretch.Uniform
            };
        }

        void OpenAttachment()
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = fullPath, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Logger.Error("WikiWindow: failed to open attachment.", ex);

                MessageBox.Show(ex.ToString(), "Wiki - open attachment");
            }
        }

        imageBorder.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount != 2)
            {
                return;
            }

            // A PDF has no inline preview to speak of, so double-click
            // opens it in the OS default viewer instead of inserting a
            // reference -- an actual image still links into the text.
            if (isPdf)
            {
                OpenAttachment();
            }
            else
            {
                InsertImageReference(fileName);
            }
        };

        var openItem = new MenuItem { Header = "Open" };
        openItem.Click += (_, __) => OpenAttachment();

        var removeItem = new MenuItem { Header = "Remove attachment" };
        removeItem.Click += (_, __) =>
        {
            _currentSection?.ImageFileNames.Remove(fileName);

            RefreshImages();
            Save();
        };

        imageBorder.ContextMenu = new ContextMenu { Items = { openItem, removeItem } };

        var nameText = new TextBlock
        {
            Text = fileName,
            FontSize = 10,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0)
        };

        stack.Children.Add(imageBorder);
        stack.Children.Add(nameText);

        return stack;
    }

    private void
    InsertImageReference(
        string fileName)
    {
        var marker = $"📎 {fileName}";
        var caret = ContentTextBox.CaretIndex;

        ContentTextBox.Text = ContentTextBox.Text.Insert(caret, marker);
        ContentTextBox.CaretIndex = caret + marker.Length;
        ContentTextBox.Focus();
    }

    private void
    ScheduleSave()
    {
        _saveDebounceTimer.Stop();
        _saveDebounceTimer.Start();
    }

    private void
    Save()
    {
        try
        {
            _wikiService.SaveProject(_project);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "Wiki - save error");
        }
    }

    // Offset == -1 for a section-name match (just select the section);
    // >= 0 for a match inside that section's text, at that character index.
    private readonly List<(WikiSection Section, int Offset)> _searchMatches = new();
    private int _currentMatchIndex = -1;
    private string _lastSearchQuery = "";

    private void
    SearchTextBox_KeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;

        if (Keyboard.Modifiers == ModifierKeys.Shift)
        {
            SearchPreviousButton_Click(sender, e);
        }
        else
        {
            SearchNextButton_Click(sender, e);
        }
    }

    private void
    SearchNextButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (RunSearchIfQueryChanged())
        {
            return;
        }

        GoToNextMatch();
    }

    private void
    SearchPreviousButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (RunSearchIfQueryChanged())
        {
            return;
        }

        GoToPreviousMatch();
    }

    private bool
    RunSearchIfQueryChanged()
    {
        var query = SearchTextBox.Text.Trim();

        if (string.Equals(query, _lastSearchQuery, StringComparison.Ordinal))
        {
            return false;
        }

        _lastSearchQuery = query;
        RunSearch(query);

        return true;
    }

    private void
    RunSearch(
        string query)
    {
        _searchMatches.Clear();
        _currentMatchIndex = -1;

        if (string.IsNullOrWhiteSpace(query))
        {
            SearchResultsText.Text = "";

            return;
        }

        foreach (var section in _project.Sections)
        {
            if (section.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                _searchMatches.Add((section, -1));
            }

            var searchFrom = 0;

            while (true)
            {
                var found = section.Text.IndexOf(query, searchFrom, StringComparison.OrdinalIgnoreCase);

                if (found < 0)
                {
                    break;
                }

                _searchMatches.Add((section, found));
                searchFrom = found + Math.Max(query.Length, 1);
            }
        }

        if (_searchMatches.Count == 0)
        {
            SearchResultsText.Text = "No match";

            return;
        }

        GoToNextMatch();
    }

    private void
    GoToNextMatch()
    {
        if (_searchMatches.Count == 0)
        {
            return;
        }

        _currentMatchIndex = (_currentMatchIndex + 1) % _searchMatches.Count;

        GoToMatch(_currentMatchIndex);
    }

    private void
    GoToPreviousMatch()
    {
        if (_searchMatches.Count == 0)
        {
            return;
        }

        _currentMatchIndex =
            (_currentMatchIndex - 1 + _searchMatches.Count) % _searchMatches.Count;

        GoToMatch(_currentMatchIndex);
    }

    private void
    GoToMatch(
        int index)
    {
        var (section, offset) = _searchMatches[index];

        if (!ReferenceEquals(SectionsListBox.SelectedItem, section))
        {
            SectionsListBox.SelectedItem = section;
        }

        if (offset >= 0)
        {
            // Deferred: switching sections just above reloads
            // ContentTextBox.Text, and GetLineIndexFromCharacterIndex needs
            // a layout pass over the new text before it answers correctly.
            ContentTextBox.Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() =>
                {
                    var length = Math.Min(_lastSearchQuery.Length, ContentTextBox.Text.Length - offset);

                    if (length <= 0)
                    {
                        return;
                    }

                    // Not ContentTextBox.Focus(): keeping focus on the
                    // search box is what makes repeated Enter presses keep
                    // advancing through results (a TextBox has no
                    // per-substring highlight, only selection -- which
                    // stays visible, just in a muted color, without focus).
                    ContentTextBox.Select(offset, length);

                    var line = ContentTextBox.GetLineIndexFromCharacterIndex(offset);

                    ContentTextBox.ScrollToLine(Math.Max(0, line - 2));
                }));
        }

        SearchResultsText.Text = $"{index + 1} / {_searchMatches.Count}";
    }
}

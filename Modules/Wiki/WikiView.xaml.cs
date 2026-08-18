using KubaToolKit.Modules.ApiClient;
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
using KubaToolKit.Shared.Windows;

namespace KubaToolKit.Modules.Wiki;

public partial class WikiView
    : UserControl
{
    private readonly WikiService _wikiService = new();
    private readonly WikiLibrary _library;

    private WikiSection? _currentSection;
    private bool _loadingSection;

    // Folders start collapsed -- a folder is added to this set the first
    // time someone expands it. Not persisted: it's just current-session
    // browsing state, same idea as _sectionExpanded in ProjectInfoView.
    private readonly HashSet<string> _expandedFolders = new(StringComparer.OrdinalIgnoreCase);

    private readonly DispatcherTimer _saveDebounceTimer;

    public WikiView()
    {
        InitializeComponent();

        _saveDebounceTimer =
            new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };

        _saveDebounceTimer.Tick += (_, __) =>
        {
            _saveDebounceTimer.Stop();
            Save();
        };

        _library = _wikiService.LoadLibrary();

        RefreshSectionsList();

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

    // Called before switching away from this tab, or before the app
    // closes -- a UserControl has no Closing event to flush a pending
    // debounced save from the way the old standalone window did.
    public void
    FlushPendingSave()
    {
        if (_saveDebounceTimer.IsEnabled)
        {
            _saveDebounceTimer.Stop();
            Save();
        }
    }

    private class SectionGroup
    {
        public string FolderName { get; set; } = "";
        public Visibility HeaderVisibility { get; set; } = Visibility.Visible;
        public Visibility RowsVisibility { get; set; } = Visibility.Visible;
        public double ChevronAngle { get; set; }
        public List<SectionRow> Rows { get; set; } = new();
    }

    private class SectionRow
    {
        public WikiSection Section { get; set; } = null!;
        public string Name { get; set; } = "";
        public Brush RowBackground { get; set; } = Brushes.Transparent;
    }

    private void
    AddSectionButton_Click(
        object sender,
        RoutedEventArgs e) =>
        AddSection(_library.Sections.Count);

    private void
    NewSectionNameTextBox_KeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AddSection(_library.Sections.Count);
        }
    }

    private void
    AddSectionAbove_Click(
        object sender,
        RoutedEventArgs e)
    {
        var index = _currentSection != null
            ? _library.Sections.IndexOf(_currentSection)
            : _library.Sections.Count;

        AddSection(Math.Max(0, index));
    }

    private void
    AddSectionBelow_Click(
        object sender,
        RoutedEventArgs e)
    {
        var index = _currentSection != null
            ? _library.Sections.IndexOf(_currentSection) + 1
            : _library.Sections.Count;

        AddSection(index);
    }

    private void
    AddSection(
        int index)
    {
        var name = NewSectionNameTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            name = $"Page {_library.Sections.Count + 1}";
        }

        if (_library.Sections.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            AppMessageBox.Show($"A page \"{name}\" already exists.", "Wiki");

            return;
        }

        // A newly added page defaults to the same folder as whatever's
        // currently selected -- adding a related page from within a folder
        // shouldn't drop it back to ungrouped.
        var section = new WikiSection { Name = name, Folder = _currentSection?.Folder ?? "" };

        _library.Sections.Insert(Math.Clamp(index, 0, _library.Sections.Count), section);

        NewSectionNameTextBox.Text = "";

        SelectSection(section);

        Save();
    }

    private void
    RenameSection_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_currentSection == null)
        {
            return;
        }

        var newName =
            TextInputWindow.Prompt(Window.GetWindow(this), "Rename page", "New name:", _currentSection.Name);

        if (string.IsNullOrWhiteSpace(newName)
            || string.Equals(newName, _currentSection.Name, StringComparison.Ordinal))
        {
            return;
        }

        if (_library.Sections.Any(s =>
                s != _currentSection && string.Equals(s.Name, newName, StringComparison.OrdinalIgnoreCase)))
        {
            AppMessageBox.Show($"A page \"{newName}\" already exists.", "Wiki");

            return;
        }

        _currentSection.Name = newName;

        RefreshSectionsList();

        Save();
    }

    private void
    DeleteSection_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_currentSection == null)
        {
            return;
        }

        if (AppMessageBox.Show(
                $"Delete page \"{_currentSection.Name}\" and its content? Attached files are kept on disk.",
                "Confirm",
                MessageBoxButton.YesNo) != MessageBoxResult.Yes)
        {
            return;
        }

        _library.Sections.Remove(_currentSection);

        SelectSection(null);

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
        var sorted =
            ascending
                ? _library.Sections.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList()
                : _library.Sections.OrderByDescending(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();

        _library.Sections.Clear();
        _library.Sections.AddRange(sorted);

        RefreshSectionsList();

        Save();
    }

    // RenameMenuItem/DeleteMenuItem live inside UserControl.Resources, so
    // (unlike elements in the main visual tree) they don't get their own
    // InitializeComponent-wired fields -- found by name on the sender
    // ContextMenu itself instead.
    private void
    SectionsContextMenu_Opened(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu)
        {
            return;
        }

        var hasSelection = _currentSection != null;

        if (menu.FindName("RenameMenuItem") is MenuItem renameItem)
        {
            renameItem.IsEnabled = hasSelection;
        }

        if (menu.FindName("DeleteMenuItem") is MenuItem deleteItem)
        {
            deleteItem.IsEnabled = hasSelection;
        }
    }

    private void
    RefreshSectionsList()
    {
        var folders =
            _library.Sections
                .Where(s => !string.IsNullOrWhiteSpace(s.Folder))
                .Select(s => s.Folder)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

        var groups = new List<SectionGroup>();

        foreach (var folder in folders)
        {
            groups.Add(
                BuildGroup(
                    folder,
                    _library.Sections.Where(s => string.Equals(s.Folder, folder, StringComparison.OrdinalIgnoreCase))));
        }

        var ungrouped = _library.Sections.Where(s => string.IsNullOrWhiteSpace(s.Folder)).ToList();

        if (ungrouped.Count > 0)
        {
            groups.Add(BuildGroup(folders.Count > 0 ? "(No folder)" : "", ungrouped));
        }

        SectionGroupsItemsControl.ItemsSource = groups;

        SectionFolderCombo.ItemsSource = folders;

        // A stale match could point at a page that no longer exists (or no
        // longer matches after a rename), or would jump to the wrong
        // position after other pages got added/removed/reordered.
        _searchMatches.Clear();
        _currentMatchIndex = -1;
        _lastSearchQuery = "";
        SearchResultsText.Text = "";
    }

    private SectionGroup
    BuildGroup(
        string folderName,
        IEnumerable<WikiSection> sections)
    {
        var hasHeader = !string.IsNullOrEmpty(folderName);

        // Driven entirely by _expandedFolders -- whoever puts a page into a
        // folder (SelectSection, CommitFolderChange) is responsible for
        // calling EnsureFolderExpanded first so it doesn't vanish from
        // view, but past that a folder stays exactly as collapsed/expanded
        // as it was last explicitly toggled, current selection or not.
        var isExpanded = !hasHeader || _expandedFolders.Contains(folderName);

        return new SectionGroup
        {
            FolderName = folderName,
            HeaderVisibility = hasHeader ? Visibility.Visible : Visibility.Collapsed,
            RowsVisibility = isExpanded ? Visibility.Visible : Visibility.Collapsed,
            ChevronAngle = isExpanded ? 0 : -90,
            Rows =
                sections
                    .Select(s => new SectionRow
                    {
                        Section = s,
                        Name = s.Name,
                        RowBackground =
                            ReferenceEquals(s, _currentSection)
                                ? (Brush)FindResource("AccentSoftBrush")
                                : Brushes.Transparent
                    })
                    .ToList()
        };
    }

    // The "(No folder)" group is a display-only placeholder -- real pages
    // in it have Folder == "", never the literal string "(No folder)".
    private void
    EnsureFolderExpanded(
        WikiSection section) =>
        _expandedFolders.Add(string.IsNullOrWhiteSpace(section.Folder) ? "(No folder)" : section.Folder);

    private void
    FolderHeader_Click(
        object sender,
        MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: SectionGroup group } || string.IsNullOrEmpty(group.FolderName))
        {
            return;
        }

        if (group.RowsVisibility == Visibility.Visible)
        {
            _expandedFolders.Remove(group.FolderName);
        }
        else
        {
            _expandedFolders.Add(group.FolderName);
        }

        RefreshSectionsList();
    }

    private void
    SectionRow_Click(
        object sender,
        MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: SectionRow row })
        {
            return;
        }

        SelectSection(row.Section);
    }

    private void
    SelectSection(
        WikiSection? section)
    {
        _currentSection = section;

        if (section != null)
        {
            EnsureFolderExpanded(section);
        }

        RefreshSectionsList();

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
            SectionFolderCombo.Text = _currentSection.Folder;
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
    SectionFolderCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e) =>
        CommitFolderChange();

    private void
    SectionFolderCombo_LostFocus(
        object sender,
        RoutedEventArgs e) =>
        CommitFolderChange();

    private void
    CommitFolderChange()
    {
        if (_loadingSection || _currentSection == null)
        {
            return;
        }

        var folder = SectionFolderCombo.Text.Trim();

        if (folder == _currentSection.Folder)
        {
            return;
        }

        _currentSection.Folder = folder;

        EnsureFolderExpanded(_currentSection);

        RefreshSectionsList();
        Save();
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

        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        try
        {
            var imagesFolder = WikiService.EnsureImagesFolder();

            foreach (var sourcePath in dialog.FileNames)
            {
                var fileName = WikiService.CopyImageWithUniqueName(sourcePath, imagesFolder);

                _currentSection.ImageFileNames.Add(fileName);
            }

            RefreshImages();
            Save();
        }
        catch (Exception ex)
        {
            Logger.Error("WikiView: failed to attach image.", ex);

            AppMessageBox.Show(ex.ToString(), "Wiki - add image");
        }
    }

    private void
    RefreshImages()
    {
        ImagesPanel.Children.Clear();

        if (_currentSection == null)
        {
            return;
        }

        var imagesFolder = WikiService.GetImagesFolderPath();

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

        var fullPath = Path.Combine(WikiService.GetImagesFolderPath(), firstImage);

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

        // A newly loaded image (new page, or attachments changed) starts
        // fresh rather than keeping whatever zoom/pan was left over from a
        // previous one.
        ResetFeaturedImageZoom();
    }

    private Point? _featuredImagePanStart;
    private double _featuredImagePanStartHorizontalOffset;
    private double _featuredImagePanStartVerticalOffset;

    // Default to fitting the whole image in the window instead of its
    // native pixel size (which is usually bigger than the window and needs
    // a manual zoom-out first) -- zooming in from a fully visible image
    // beats zooming out then back in to the spot you wanted.
    private void
    ResetFeaturedImageZoom()
    {
        FeaturedImageScrollViewer.ScrollToHorizontalOffset(0);
        FeaturedImageScrollViewer.ScrollToVerticalOffset(0);

        // The ScrollViewer's viewport size, and the image's natural size
        // right after a fresh Source assignment, aren't available until
        // after a layout pass.
        FeaturedImageScrollViewer.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(ApplyFitToViewportZoom));
    }

    private void
    ApplyFitToViewportZoom()
    {
        var viewportWidth = FeaturedImageScrollViewer.ViewportWidth;
        var viewportHeight = FeaturedImageScrollViewer.ViewportHeight;

        if (FeaturedImage.Source is not BitmapSource bitmap
            || viewportWidth <= 0 || viewportHeight <= 0
            || bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
        {
            FeaturedImageScale.ScaleX = 1;
            FeaturedImageScale.ScaleY = 1;

            return;
        }

        // Never upscale past 100% by default: shrinking a large image to
        // fit is the point, a small image just stays at its own size
        // instead of being blown up and looking blurry.
        var fitScale =
            Math.Min(
                viewportWidth / bitmap.PixelWidth,
                viewportHeight / bitmap.PixelHeight);

        var scale = Math.Min(1.0, fitScale);

        FeaturedImageScale.ScaleX = scale;
        FeaturedImageScale.ScaleY = scale;
    }

    private void
    FeaturedImageScrollViewer_ZoomWheel(
        object sender,
        MouseWheelEventArgs e)
    {
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
        // layout pass -- adjusting offsets before that would clamp against
        // the still-stale (pre-zoom) scrollable range.
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
            BorderBrush = (Brush)FindResource("BorderBrush"),
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
                Foreground = (Brush)FindResource("TextMutedBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }
        else if (isPdf)
        {
            // WPF can't render a PDF preview without an extra library -- a
            // plain icon still supports the same double-click-to-link/
            // right-click-to-open/remove behavior as an actual image.
            imageBorder.Child = new TextBlock
            {
                Text = "📄 PDF",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
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
                Logger.Error("WikiView: failed to open attachment.", ex);

                AppMessageBox.Show(ex.ToString(), "Wiki - open attachment");
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
            _wikiService.SaveLibrary(_library);
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(ex.ToString(), "Wiki - save error");
        }
    }

    // Offset == -1 for a page-name match (just select the page); >= 0 for
    // a match inside that page's text, at that character index.
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

        foreach (var section in _library.Sections)
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

        if (!ReferenceEquals(_currentSection, section))
        {
            SelectSection(section);
        }

        if (offset >= 0)
        {
            // Deferred: switching pages just above reloads
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

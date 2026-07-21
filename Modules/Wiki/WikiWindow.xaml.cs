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
    private readonly WikiRoot _root;
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

        _root = _wikiService.Load();
        _project = _wikiService.GetOrCreateProject(_root, projectKey);

        Title = $"Wiki - {_project.Key} (profile: {_profileName})";
        TitleTextBlock.Text = Title;

        SectionsListBox.ItemsSource = _project.Sections;

        if (_project.Sections.Count > 0)
        {
            SectionsListBox.SelectedIndex = 0;
        }
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
                $"Delete section \"{section.Name}\" and its content? Attached image files are kept on disk.",
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
            Filter = "Images (*.png;*.jpg;*.jpeg;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp",
            Multiselect = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var imagesFolder = WikiService.GetImagesFolderPath(_project.Key);

            Directory.CreateDirectory(imagesFolder);

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

        if (File.Exists(fullPath))
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
        else
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

        imageBorder.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                InsertImageReference(fileName);
            }
        };

        var openItem = new MenuItem { Header = "Open image" };
        openItem.Click += (_, __) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = fullPath, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Logger.Error("WikiWindow: failed to open image.", ex);

                MessageBox.Show(ex.ToString(), "Wiki - open image");
            }
        };

        var removeItem = new MenuItem { Header = "Remove image" };
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
            _wikiService.Save(_root);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "Wiki - save error");
        }
    }
}

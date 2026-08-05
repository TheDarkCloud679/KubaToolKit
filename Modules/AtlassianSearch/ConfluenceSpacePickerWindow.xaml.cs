using KubaToolKit.Modules.AtlassianSearch.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace KubaToolKit.Modules.AtlassianSearch;

// A self-contained popup instead of a ComboBox: picking a space had run
// into a string of ComboBox popup quirks (opening upward and covering the
// search box in a short window, mouse-over inside the popup stealing
// keyboard focus from the search box) that weren't worth continuing to
// fight -- a plain ListBox in its own Window sidesteps all of it.
public partial class ConfluenceSpacePickerWindow
    : Window
{
    private class SpaceItem
    {
        public string Value { get; init; } = "";
        public string BaseDisplay { get; init; } = "";
        public bool IsFavorite { get; set; }

        public string Display =>
            IsFavorite ? $"★ {BaseDisplay}" : BaseDisplay;
    }

    private readonly AtlassianSettings _settings;
    private readonly AtlassianSettingsService _settingsService;
    private readonly List<NameValue> _allSpaces;
    private List<SpaceItem> _items = new();
    private bool _confirmed;

    public List<string> SelectedKeys { get; private set; }

    public ConfluenceSpacePickerWindow(
        AtlassianSettings settings,
        AtlassianSettingsService settingsService,
        List<NameValue> allSpaces,
        List<string> initiallySelectedKeys)
    {
        InitializeComponent();

        _settings = settings;
        _settingsService = settingsService;
        _allSpaces = allSpaces;
        SelectedKeys = new List<string>(initiallySelectedKeys);

        MultiSelectCheckBox.IsChecked = initiallySelectedKeys.Count > 1;
        SpacesListBox.SelectionMode =
            MultiSelectCheckBox.IsChecked == true ? SelectionMode.Multiple : SelectionMode.Single;

        RebuildItems();
        RestoreSelection();
    }

    public static List<string>?
    Prompt(
        Window? owner,
        AtlassianSettings settings,
        AtlassianSettingsService settingsService,
        List<NameValue> allSpaces,
        List<string> initiallySelectedKeys)
    {
        var window =
            new ConfluenceSpacePickerWindow(settings, settingsService, allSpaces, initiallySelectedKeys)
            {
                Owner = owner
            };

        window.ShowDialog();

        return window._confirmed ? window.SelectedKeys : null;
    }

    private void
    RebuildItems()
    {
        var favoriteSet =
            new HashSet<string>(_settings.FavoriteConfluenceSpaceKeys, StringComparer.OrdinalIgnoreCase);

        _items =
            _allSpaces
                .Select(s =>
                    new SpaceItem
                    {
                        Value = s.Value,
                        BaseDisplay = s.Display,
                        IsFavorite = favoriteSet.Contains(s.Value)
                    })
                .OrderByDescending(i => i.IsFavorite)
                .ThenBy(i => i.BaseDisplay, StringComparer.OrdinalIgnoreCase)
                .ToList();

        ApplyFilter();
    }

    private void
    ApplyFilter()
    {
        var text = SearchBox.Text.Trim();

        SpacesListBox.ItemsSource =
            string.IsNullOrEmpty(text)
                ? _items
                : _items.Where(i => i.BaseDisplay.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void
    RestoreSelection() =>
        ApplySelection(SelectedKeys);

    // SelectedItems can only be written to in Multiple mode -- Single mode
    // requires going through SelectedItem instead, or WPF throws.
    private void
    ApplySelection(
        IEnumerable<string> keys)
    {
        var keySet = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);

        if (SpacesListBox.SelectionMode == SelectionMode.Single)
        {
            SpacesListBox.SelectedItem =
                SpacesListBox.Items.Cast<SpaceItem>().FirstOrDefault(item => keySet.Contains(item.Value));

            return;
        }

        foreach (var item in SpacesListBox.Items.Cast<SpaceItem>())
        {
            if (keySet.Contains(item.Value))
            {
                SpacesListBox.SelectedItems.Add(item);
            }
        }
    }

    private void
    SearchBox_TextChanged(
        object sender,
        TextChangedEventArgs e) =>
        ApplyFilter();

    private void
    MultiSelectCheckBox_Changed(
        object sender,
        RoutedEventArgs e)
    {
        SpacesListBox.SelectionMode =
            MultiSelectCheckBox.IsChecked == true ? SelectionMode.Multiple : SelectionMode.Single;
    }

    private void
    SpacesListBox_PreviewMouseRightButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(SpacesListBox, e.OriginalSource as DependencyObject)
            is ListBoxItem container)
        {
            container.IsSelected = true;
        }
    }

    private void
    SpacesListBox_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        if (MultiSelectCheckBox.IsChecked == true)
        {
            return;
        }

        if (SpacesListBox.SelectedItem is SpaceItem item)
        {
            SelectedKeys = new List<string> { item.Value };
            _confirmed = true;

            Close();
        }
    }

    private void
    ContextMenu_Opened(
        object sender,
        RoutedEventArgs e)
    {
        if (SpacesListBox.SelectedItem is not SpaceItem item)
        {
            ToggleFavoriteMenuItem.IsEnabled = false;
            ToggleFavoriteMenuItem.Header = "Add to favorites";

            return;
        }

        ToggleFavoriteMenuItem.IsEnabled = true;
        ToggleFavoriteMenuItem.Header = item.IsFavorite ? "Remove from favorites" : "Add to favorites";
    }

    private void
    ToggleFavorite_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (SpacesListBox.SelectedItem is not SpaceItem item)
        {
            return;
        }

        var favorites = _settings.FavoriteConfluenceSpaceKeys;

        if (favorites.Any(k => string.Equals(k, item.Value, StringComparison.OrdinalIgnoreCase)))
        {
            favorites.RemoveAll(k => string.Equals(k, item.Value, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            favorites.Add(item.Value);
        }

        _settingsService.Save(_settings);

        var previousSelection =
            SpacesListBox.SelectedItems.Cast<SpaceItem>().Select(i => i.Value).ToList();

        RebuildItems();

        ApplySelection(previousSelection);
    }

    private void
    Clear_Click(
        object sender,
        RoutedEventArgs e)
    {
        SelectedKeys = new List<string>();
        _confirmed = true;

        Close();
    }

    private void
    Cancel_Click(
        object sender,
        RoutedEventArgs e) =>
        Close();

    private void
    Ok_Click(
        object sender,
        RoutedEventArgs e)
    {
        SelectedKeys = SpacesListBox.SelectedItems.Cast<SpaceItem>().Select(i => i.Value).ToList();
        _confirmed = true;

        Close();
    }
}

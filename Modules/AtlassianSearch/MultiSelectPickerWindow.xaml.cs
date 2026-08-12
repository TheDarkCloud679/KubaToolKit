using KubaToolKit.Modules.AtlassianSearch.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace KubaToolKit.Modules.AtlassianSearch;

// A generic multi-select popup for any filter that should support "in"/
// "not in" against several values at once -- same idea (and the same
// ListBox-in-its-own-Window shape, for the same ComboBox-popup-quirks
// reasons) as ConfluenceSpacePickerWindow, just without that one's
// space-specific favoriting/single-vs-multi toggle.
public partial class MultiSelectPickerWindow
    : Window
{
    private readonly List<NameValue> _allOptions;
    private bool _confirmed;

    public List<string> SelectedValues { get; private set; }

    public MultiSelectPickerWindow(
        string title,
        List<NameValue> allOptions,
        List<string> initiallySelected)
    {
        InitializeComponent();

        Title = title;

        _allOptions = allOptions;
        SelectedValues = new List<string>(initiallySelected);

        OptionsListBox.ItemsSource = _allOptions;

        RestoreSelection();
    }

    public static List<string>?
    Prompt(
        Window? owner,
        string title,
        List<NameValue> allOptions,
        List<string> initiallySelected)
    {
        var window =
            new MultiSelectPickerWindow(title, allOptions, initiallySelected)
            {
                Owner = owner
            };

        window.ShowDialog();

        return window._confirmed ? window.SelectedValues : null;
    }

    private void
    RestoreSelection()
    {
        var selectedSet = new HashSet<string>(SelectedValues, StringComparer.OrdinalIgnoreCase);

        foreach (var item in OptionsListBox.Items.Cast<NameValue>())
        {
            if (selectedSet.Contains(item.Value))
            {
                OptionsListBox.SelectedItems.Add(item);
            }
        }
    }

    private void
    SearchBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        var text = SearchBox.Text.Trim();

        var previousSelection =
            OptionsListBox.SelectedItems.Cast<NameValue>().Select(i => i.Value).ToList();

        OptionsListBox.ItemsSource =
            string.IsNullOrEmpty(text)
                ? _allOptions
                : _allOptions.Where(o => o.Display.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();

        var selectedSet = new HashSet<string>(previousSelection, StringComparer.OrdinalIgnoreCase);

        foreach (var item in OptionsListBox.Items.Cast<NameValue>())
        {
            if (selectedSet.Contains(item.Value))
            {
                OptionsListBox.SelectedItems.Add(item);
            }
        }
    }

    private void
    OptionsListBox_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        SelectedValues = OptionsListBox.SelectedItems.Cast<NameValue>().Select(i => i.Value).ToList();
        _confirmed = true;

        Close();
    }

    private void
    Clear_Click(
        object sender,
        RoutedEventArgs e)
    {
        SelectedValues = new List<string>();
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
        SelectedValues = OptionsListBox.SelectedItems.Cast<NameValue>().Select(i => i.Value).ToList();
        _confirmed = true;

        Close();
    }
}

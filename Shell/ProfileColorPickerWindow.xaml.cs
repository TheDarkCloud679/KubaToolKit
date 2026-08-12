using KubaToolKit.Shared.Services;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace KubaToolKit.Shell;

// Lets the user override ProfileRiskBrushConverter's naming-based guess
// per profile -- a curated swatch palette rather than a full color-wheel
// picker, since "pick one of these" covers the actual need (making a
// profile visually distinct/memorable) without building a much bigger
// control for it.
public partial class ProfileColorPickerWindow
    : Window
{
    private static readonly (string? Hex, string Name)[] Palette =
    {
        (null, "Auto"),
        ("#E5484D", "Red"),
        ("#F2A93B", "Amber"),
        ("#1E9E6B", "Green"),
        ("#0C8599", "Teal"),
        ("#FF6B4A", "Coral"),
        ("#3B82F6", "Blue"),
        ("#6366F1", "Indigo"),
        ("#8B5CF6", "Purple"),
        ("#EC4899", "Pink"),
        ("#84CC16", "Lime"),
        ("#92592B", "Brown"),
        ("#6B7280", "Gray"),
    };

    private static readonly Color AccentColor = (Color)ColorConverter.ConvertFromString("#0C8599");
    private static readonly Color RingColor = (Color)ColorConverter.ConvertFromString("#CBCBD1");

    private readonly ProfileColorSettingsService _settingsService;
    private readonly ProfileColorSettings _settings;

    private class ProfileRow
    {
        public string Name { get; set; } = "";
        public List<SwatchOption> Swatches { get; set; } = new();
    }

    private class SwatchOption
    {
        public string ProfileName { get; set; } = "";
        public string? ColorHex { get; set; }
        public Brush Fill { get; set; } = Brushes.Transparent;
        public Brush RingBrush { get; set; } = Brushes.Transparent;
        public double RingThickness { get; set; } = 1;
        public string ToolTipText { get; set; } = "";
    }

    public ProfileColorPickerWindow(
        IEnumerable<string> profiles,
        ProfileColorSettingsService settingsService,
        ProfileColorSettings settings)
    {
        InitializeComponent();

        _settingsService = settingsService;
        _settings = settings;

        RowsItemsControl.ItemsSource =
            profiles.Select(BuildRow).ToList();
    }

    private ProfileRow
    BuildRow(
        string profileName)
    {
        _settings.Colors.TryGetValue(profileName, out var currentHex);

        return new ProfileRow
        {
            Name = profileName,
            Swatches =
                Palette
                    .Select(option => BuildSwatch(profileName, option.Hex, option.Name, currentHex))
                    .ToList()
        };
    }

    private SwatchOption
    BuildSwatch(
        string profileName,
        string? hex,
        string name,
        string? currentHex)
    {
        var isSelected =
            string.IsNullOrWhiteSpace(hex)
                ? string.IsNullOrWhiteSpace(currentHex)
                : string.Equals(hex, currentHex, StringComparison.OrdinalIgnoreCase);

        return new SwatchOption
        {
            ProfileName = profileName,
            ColorHex = hex,
            Fill = string.IsNullOrWhiteSpace(hex) ? Brushes.Transparent : new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
            RingBrush = new SolidColorBrush(isSelected ? AccentColor : RingColor),
            RingThickness = isSelected ? 2.5 : 1,
            ToolTipText = name
        };
    }

    private void
    Swatch_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SwatchOption option })
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(option.ColorHex))
        {
            _settings.Colors.Remove(option.ProfileName);
        }
        else
        {
            _settings.Colors[option.ProfileName] = option.ColorHex;
        }

        try
        {
            _settingsService.Save(_settings);
        }
        catch (Exception ex)
        {
            Logger.Error("ProfileColorPickerWindow: failed to save.", ex);
        }

        // Rebuild rather than mutate in place -- simplest way to move the
        // selection ring to the newly-picked swatch across every row's
        // ItemsControl.
        RowsItemsControl.ItemsSource =
            (RowsItemsControl.ItemsSource as List<ProfileRow> ?? new List<ProfileRow>())
                .Select(row => row.Name)
                .Select(BuildRow)
                .ToList();
    }

    private void
    Close_Click(
        object sender,
        RoutedEventArgs e) =>
        Close();
}

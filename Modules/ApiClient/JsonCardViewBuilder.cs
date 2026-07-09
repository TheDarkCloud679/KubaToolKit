using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KubaToolKit.Modules.ApiClient;

/// Un raccourci de navigation vers un bloc de la vue Cartes (voir
/// JsonCardViewBuilder.Build) : Element.BringIntoView() fait défiler
/// automatiquement le ScrollViewer ambiant jusqu'à ce bloc.
public sealed record JsonCardAnchor(
    string Label,
    FrameworkElement Element);

public sealed record JsonCardViewResult(
    UIElement Root,
    IReadOnlyList<JsonCardAnchor> Anchors);

/// Transforme un corps de réponse JSON en une arborescence de "cartes"
/// WPF lisibles (un bloc par objet/élément de tableau, badges colorés
/// pour les champs d'état, dates reformatées) plutôt que le JSON brut.
/// Purement un rendu alternatif : la vue "Brut" (AvalonEdit) reste la
/// source de vérité, celle-ci ne fait qu'aider à la lecture.
public static class JsonCardViewBuilder
{
    private const int MaxArrayItemsRendered = 200;
    private const int MaxDepth = 14;

    private static readonly Color SuccessColor = Color.FromRgb(0x1F, 0xA9, 0x71);
    private static readonly Color DangerColor = Color.FromRgb(0xE5, 0x48, 0x4D);
    private static readonly Color WarningColor = Color.FromRgb(0xF2, 0xA9, 0x3B);
    private static readonly Color NeutralColor = Color.FromRgb(0x68, 0x70, 0x7E);

    private static readonly Brush SurfaceBrush = MakeBrush(0xFF, 0xFF, 0xFF);
    private static readonly Brush SurfaceAltBrush = MakeBrush(0xF7, 0xF9, 0xFC);
    private static readonly Brush BorderBrush = MakeBrush(0xE1, 0xE5, 0xEC);
    private static readonly Brush TextPrimaryBrush = MakeBrush(0x1E, 0x24, 0x30);
    private static readonly Brush TextSecondaryBrush = MakeBrush(0x68, 0x70, 0x7E);
    private static readonly Brush TextMutedBrush = MakeBrush(0x98, 0xA1, 0xAF);

    /// Null si le texte n'est pas du JSON valide (l'appelant doit alors
    /// se rabattre sur la vue brute).
    public static JsonCardViewResult?
    Build(
        string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        JsonDocument doc;

        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        using (doc)
        {
            var root = doc.RootElement;
            var anchors = new List<JsonCardAnchor>();

            UIElement rootElement =
                root.ValueKind switch
                {
                    JsonValueKind.Object => BuildObjectBody(root, 0, anchors),
                    JsonValueKind.Array => BuildArrayItemsPanel(root, 0, anchors),
                    _ => MutedText(root.GetRawText())
                };

            return new JsonCardViewResult(rootElement, anchors);
        }
    }

    private static Panel
    BuildObjectBody(
        JsonElement obj,
        int depth,
        List<JsonCardAnchor> anchors)
    {
        var panel = new StackPanel();

        if (depth > MaxDepth)
        {
            panel.Children.Add(MutedText("(imbrication trop profonde, voir la vue Brut)"));

            return panel;
        }

        var scalarProps = new List<JsonProperty>();
        var complexProps = new List<JsonProperty>();

        foreach (var prop in obj.EnumerateObject())
        {
            if (IsSimpleValue(prop.Value))
            {
                scalarProps.Add(prop);
            }
            else
            {
                complexProps.Add(prop);
            }
        }

        if (scalarProps.Count == 0
            && complexProps.Count == 0)
        {
            panel.Children.Add(MutedText("(objet vide)"));

            return panel;
        }

        if (scalarProps.Count > 0)
        {
            panel.Children.Add(BuildFieldsGrid(scalarProps));
        }

        foreach (var prop in complexProps)
        {
            var block = BuildComplexBlock(prop.Name, prop.Value, depth, anchors);

            block.Margin = new Thickness(0, scalarProps.Count > 0 || panel.Children.Count > 1 ? 10 : 0, 0, 0);

            panel.Children.Add(block);
        }

        return panel;
    }

    private static bool
    IsSimpleValue(
        JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.Object => false,
            JsonValueKind.Array =>
                !element.EnumerateArray()
                    .Any(item => item.ValueKind is JsonValueKind.Object or JsonValueKind.Array),
            _ => true
        };

    private static Grid
    BuildFieldsGrid(
        IEnumerable<JsonProperty> props)
    {
        var grid = new Grid();

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var row = 0;

        foreach (var prop in props)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var keyBlock =
                new TextBlock
                {
                    Text = Humanize(prop.Name),
                    Foreground = TextSecondaryBrush,
                    FontSize = 12,
                    Margin = new Thickness(0, 3, 10, 3),
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Top
                };

            Grid.SetRow(keyBlock, row);
            Grid.SetColumn(keyBlock, 0);
            grid.Children.Add(keyBlock);

            var valueElement =
                BuildScalarValueElement(prop.Name, prop.Value);

            valueElement.Margin = new Thickness(0, 3, 0, 3);
            valueElement.HorizontalAlignment = HorizontalAlignment.Left;

            Grid.SetRow(valueElement, row);
            Grid.SetColumn(valueElement, 1);
            grid.Children.Add(valueElement);

            row++;
        }

        return grid;
    }

    private static FrameworkElement
    BuildScalarValueElement(
        string propertyName,
        JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Null:

                return MutedText("—");

            case JsonValueKind.True:

                return new TextBlock { Text = "Oui", Foreground = TextPrimaryBrush, FontSize = 12 };

            case JsonValueKind.False:

                return new TextBlock { Text = "Non", Foreground = TextPrimaryBrush, FontSize = 12 };

            case JsonValueKind.Number:

                return new TextBlock { Text = value.GetRawText(), Foreground = TextPrimaryBrush, FontSize = 12 };

            case JsonValueKind.Array:

                var items =
                    value.EnumerateArray()
                        .Select(FormatScalarElement)
                        .ToList();

                return items.Count == 0
                    ? MutedText("—")
                    : new TextBlock
                    {
                        Text = string.Join(", ", items),
                        Foreground = TextPrimaryBrush,
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap
                    };

            default: // String

                var text = value.GetString() ?? "";

                if (string.IsNullOrEmpty(text))
                {
                    return MutedText("—");
                }

                if (LooksLikeStateField(propertyName))
                {
                    return BuildBadge(text, GetStateColor(text));
                }

                return new TextBlock
                {
                    Text = FormatDateIfLooksLikeOne(text),
                    Foreground = TextPrimaryBrush,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap
                };
        }
    }

    private static string
    FormatScalarElement(
        JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Null => "—",
            _ => element.GetRawText()
        };

    /// Les blocs "nommés" (propriété d'objet -> objet ou tableau) jusqu'à
    /// une profondeur de 1 deviennent des raccourcis dans la barre de
    /// navigation rapide au-dessus de la réponse ; au-delà, une réponse
    /// très imbriquée en produirait beaucoup trop pour être utile.
    private const int MaxAnchorDepth = 1;

    private static Border
    BuildComplexBlock(
        string propertyName,
        JsonElement value,
        int depth,
        List<JsonCardAnchor> anchors)
    {
        var label = Humanize(propertyName);

        Border card =
            value.ValueKind == JsonValueKind.Object
                ? BuildCard(label, null, BuildObjectBody(value, depth + 1, anchors), depth)
                : BuildCard(
                    $"{label} ({value.GetArrayLength()})",
                    null,
                    BuildArrayItemsPanel(value, depth + 1, anchors),
                    depth);

        if (depth <= MaxAnchorDepth)
        {
            anchors.Add(new JsonCardAnchor(label, card));
        }

        return card;
    }

    private static Panel
    BuildArrayItemsPanel(
        JsonElement array,
        int depth,
        List<JsonCardAnchor> anchors)
    {
        var itemsPanel = new StackPanel();
        var items = array.EnumerateArray().ToList();

        if (items.Count == 0)
        {
            itemsPanel.Children.Add(MutedText("Aucun élément."));

            return itemsPanel;
        }

        var renderCount = Math.Min(items.Count, MaxArrayItemsRendered);

        for (var i = 0; i < renderCount; i++)
        {
            var item = items[i];

            UIElement child;

            if (item.ValueKind == JsonValueKind.Object)
            {
                var (headerText, badge) = BuildArrayItemHeader(item, i);

                child = BuildCard(headerText, badge, BuildObjectBody(item, depth + 1, anchors), depth);
            }
            else if (item.ValueKind == JsonValueKind.Array)
            {
                child = BuildCard($"[{i}]", null, BuildArrayItemsPanel(item, depth + 1, anchors), depth);
            }
            else
            {
                child = new TextBlock
                {
                    Text = FormatScalarElement(item),
                    Foreground = TextPrimaryBrush,
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 0, 4)
                };
            }

            itemsPanel.Children.Add(child);
        }

        if (items.Count > renderCount)
        {
            itemsPanel.Children.Add(
                MutedText($"… {items.Count - renderCount} élément(s) de plus (voir la vue Brut)."));
        }

        return itemsPanel;
    }

    private static (string Text, UIElement? Badge)
    BuildArrayItemHeader(
        JsonElement item,
        int index)
    {
        var parts = new List<string> { $"#{index + 1}" };

        var primary =
            TryGetString(item, "name")
            ?? TryGetString(item, "type")
            ?? TryGetString(item, "riderType");

        if (!string.IsNullOrEmpty(primary))
        {
            parts.Add(primary);
        }

        var id = TryGetIdLike(item);

        if (!string.IsNullOrEmpty(id))
        {
            parts.Add($"ID {id}");
        }

        var state =
            TryGetString(item, "state")
            ?? TryGetString(item, "status");

        UIElement? badge =
            string.IsNullOrEmpty(state)
                ? null
                : BuildBadge(state, GetStateColor(state));

        return (string.Join(" · ", parts), badge);
    }

    private static string?
    TryGetString(
        JsonElement obj,
        string propertyName) =>
        obj.ValueKind == JsonValueKind.Object
        && obj.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string?
    TryGetIdLike(
        JsonElement obj)
    {
        if (obj.ValueKind != JsonValueKind.Object
            || !obj.TryGetProperty("id", out var idElement))
        {
            return null;
        }

        return idElement.ValueKind switch
        {
            JsonValueKind.String => idElement.GetString(),
            JsonValueKind.Number => idElement.GetRawText(),
            _ => null
        };
    }

    private static bool
    LooksLikeStateField(
        string propertyName) =>
        propertyName.EndsWith("state", StringComparison.OrdinalIgnoreCase)
        || propertyName.EndsWith("status", StringComparison.OrdinalIgnoreCase);

    private static Color
    GetStateColor(
        string value)
    {
        var normalized = value.Trim().ToLowerInvariant();

        return normalized switch
        {
            "active" or "available" or "valid" or "enabled" or "succeeded" or "ok" => SuccessColor,

            "inactive"
                or "expired"
                or "blocked"
                or "suspended"
                or "cancelled"
                or "canceled"
                or "failed"
                or "lost"
                or "stolen"
                or "revoked"
                or "disabled" => DangerColor,

            "pending" or "processing" or "created" or "transitioning" => WarningColor,

            _ => NeutralColor
        };
    }

    /// "transitAccountId" -> "Transit Account Id".
    private static string
    Humanize(
        string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return key;
        }

        var spaced = Regex.Replace(key, "(?<!^)([A-Z])", " $1");

        return char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }

    private static string
    FormatDateIfLooksLikeOne(
        string text)
    {
        if (text.Length < 19
            || text[4] != '-'
            || text[7] != '-'
            || (text[10] != 'T' && text[10] != ' '))
        {
            return text;
        }

        return DateTime.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture)
            : text;
    }

    private static Border
    BuildCard(
        string header,
        UIElement? badge,
        UIElement content,
        int depth)
    {
        var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };

        headerPanel.Children.Add(
            new TextBlock
            {
                Text = header,
                FontWeight = FontWeights.SemiBold,
                FontSize = Math.Max(11, 13 - depth),
                Foreground = TextPrimaryBrush,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            });

        if (badge != null)
        {
            ((FrameworkElement)badge).Margin = new Thickness(8, 0, 0, 0);
            ((FrameworkElement)badge).VerticalAlignment = VerticalAlignment.Center;
            headerPanel.Children.Add(badge);
        }

        var stack = new StackPanel();

        stack.Children.Add(headerPanel);

        stack.Children.Add(
            new Border { Margin = new Thickness(0, 8, 0, 0), Child = content });

        return new Border
        {
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Background = depth % 2 == 0 ? SurfaceBrush : SurfaceAltBrush,
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 8),
            Child = stack
        };
    }

    private static TextBlock
    MutedText(
        string text) =>
        new()
        {
            Text = text,
            Foreground = TextMutedBrush,
            FontSize = 12,
            FontStyle = FontStyles.Italic
        };

    private static Border
    BuildBadge(
        string text,
        Color color)
    {
        var background = new SolidColorBrush(Color.FromArgb(38, color.R, color.G, color.B));
        background.Freeze();

        var foreground = new SolidColorBrush(color);
        foreground.Freeze();

        return new Border
        {
            Background = background,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 1, 8, 1),
            Child = new TextBlock
            {
                Text = text,
                Foreground = foreground,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold
            }
        };
    }

    private static Brush
    MakeBrush(
        byte r,
        byte g,
        byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();

        return brush;
    }
}

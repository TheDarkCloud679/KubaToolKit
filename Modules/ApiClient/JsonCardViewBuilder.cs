using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace KubaToolKit.Modules.ApiClient;

/// Une entrée cherchable de la vue Cartes (clé, valeur, badge ou titre
/// de bloc) : Element est un conteneur dédié (Border transparent) dont
/// on peut basculer Background sans jamais perturber l'apparence
/// d'origine de son contenu (y compris un badge déjà coloré). Ancestors
/// liste les Expander parents (du plus extérieur au plus proche) à
/// déplier avant de défiler jusqu'à Element -- capturés explicitement à
/// la construction plutôt que remontés depuis l'arbre visuel à
/// l'exécution, qui peut ne pas être entièrement connecté tant qu'un
/// Expander reste replié.
public sealed record JsonCardSearchEntry(
    string Text,
    Border Element,
    IReadOnlyList<Expander> Ancestors);

public sealed record JsonCardViewResult(
    UIElement Root,
    IReadOnlyList<JsonCardSearchEntry> SearchEntries);

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

    private static readonly Brush CopyFeedbackBrush = MakeAlphaBrush(0x50, SuccessColor);

    /// Contexte de construction interne, filé à travers la récursion :
    /// évite de répéter plusieurs paramètres dans chaque signature.
    private sealed class BuildContext
    {
        public readonly List<JsonCardSearchEntry> SearchEntries = new();

        /// Pile des Expander "en cours" pendant la récursion : chaque
        /// BeginCard pousse sa carte avant de construire son contenu, et
        /// la retire une fois fini -- un instantané de cette pile au
        /// moment où une entrée est enregistrée donne exactement la
        /// chaîne de parents à déplier pour la révéler.
        public readonly List<Expander> AncestorStack = new();

        /// Correspondance "champ JSON -> {code -> libellé}" (voir
        /// CollectionStorageService.LoadValueLabels) : un champ scalaire
        /// dont le nom et la valeur matchent s'affiche "Libellé (code)"
        /// plutôt que le code brut.
        public IReadOnlyDictionary<string, Dictionary<string, string>> ValueLabels =
            new Dictionary<string, Dictionary<string, string>>();
    }

    /// Null si le texte n'est pas du JSON valide (l'appelant doit alors
    /// se rabattre sur la vue brute). valueLabels : voir BuildContext.
    public static JsonCardViewResult?
    Build(
        string json,
        IReadOnlyDictionary<string, Dictionary<string, string>>? valueLabels = null)
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

            var ctx = new BuildContext
            {
                ValueLabels = valueLabels ?? new Dictionary<string, Dictionary<string, string>>()
            };

            UIElement rootElement =
                root.ValueKind switch
                {
                    JsonValueKind.Object => BuildObjectBody(root, 0, ctx),
                    JsonValueKind.Array => BuildArrayItemsPanel(root, 0, ctx),
                    _ => MutedText(root.GetRawText())
                };

            return new JsonCardViewResult(rootElement, ctx.SearchEntries);
        }
    }

    private static Panel
    BuildObjectBody(
        JsonElement obj,
        int depth,
        BuildContext ctx)
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
            panel.Children.Add(BuildFieldsGrid(scalarProps, ctx));
        }

        foreach (var prop in complexProps)
        {
            var block = BuildComplexBlock(prop.Name, prop.Value, depth, ctx);

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
        IEnumerable<JsonProperty> props,
        BuildContext ctx)
    {
        var grid = new Grid();

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var row = 0;

        foreach (var prop in props)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var keyText = Humanize(prop.Name);

            var keyBlock =
                new TextBlock
                {
                    Text = keyText,
                    Foreground = TextSecondaryBrush,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Top
                };

            var keyWrapper = WrapForSearch(keyText, keyBlock, ctx);
            keyWrapper.Margin = new Thickness(0, 3, 10, 3);

            Grid.SetRow(keyWrapper, row);
            Grid.SetColumn(keyWrapper, 0);
            grid.Children.Add(keyWrapper);

            var valueElement =
                BuildScalarValueElement(prop.Name, prop.Value, ctx);

            var valueWrapper = WrapForSearch(GetDisplayText(valueElement), valueElement, ctx);
            valueWrapper.Margin = new Thickness(0, 3, 0, 3);
            valueWrapper.HorizontalAlignment = HorizontalAlignment.Left;

            MakeCopyable(valueWrapper, GetDisplayText(valueElement));

            Grid.SetRow(valueWrapper, row);
            Grid.SetColumn(valueWrapper, 1);
            grid.Children.Add(valueWrapper);

            row++;
        }

        return grid;
    }

    private static FrameworkElement
    BuildScalarValueElement(
        string propertyName,
        JsonElement value,
        BuildContext ctx)
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

                var rawNumber = value.GetRawText();
                var numberLabel = TryGetValueLabel(ctx, propertyName, rawNumber);

                return new TextBlock
                {
                    Text = numberLabel != null ? $"{numberLabel} ({rawNumber})" : rawNumber,
                    Foreground = TextPrimaryBrush,
                    FontSize = 12
                };

            case JsonValueKind.Array:

                var items =
                    value.EnumerateArray()
                        .Select(item => FormatScalarElementWithLabel(ctx, propertyName, item))
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

                var stringLabel = TryGetValueLabel(ctx, propertyName, text);

                return new TextBlock
                {
                    Text = stringLabel != null ? $"{stringLabel} ({text})" : FormatDateIfLooksLikeOne(text),
                    Foreground = TextPrimaryBrush,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap
                };
        }
    }

    /// Voir BuildContext.ValueLabels : null si aucune correspondance
    /// n'est configurée pour ce champ+valeur.
    private static string?
    TryGetValueLabel(
        BuildContext ctx,
        string propertyName,
        string rawValue) =>
        ctx.ValueLabels.TryGetValue(propertyName, out var codes)
        && codes.TryGetValue(rawValue, out var label)
        && !string.IsNullOrWhiteSpace(label)
            ? label
            : null;

    private static string
    FormatScalarElement(
        JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Null => "—",
            _ => element.GetRawText()
        };

    /// Comme FormatScalarElement, mais applique aussi la correspondance
    /// de BuildContext.ValueLabels à chaque élément d'un tableau de
    /// codes (ex: "allowedOperators": [9, 165] -> "Movima (9), Transdev
    /// Marsan (165)") -- la lecture d'un champ scalaire simple appliquait
    /// déjà la même règle, un tableau ne devrait pas se comporter
    /// différemment juste parce qu'il a plusieurs valeurs.
    private static string
    FormatScalarElementWithLabel(
        BuildContext ctx,
        string propertyName,
        JsonElement element)
    {
        var raw = FormatScalarElement(element);
        var label = TryGetValueLabel(ctx, propertyName, raw);

        return label != null ? $"{label} ({raw})" : raw;
    }

    private static Expander
    BuildComplexBlock(
        string propertyName,
        JsonElement value,
        int depth,
        BuildContext ctx)
    {
        var label = Humanize(propertyName);

        var headerText =
            value.ValueKind == JsonValueKind.Object
                ? label
                : $"{label} ({value.GetArrayLength()})";

        var card = BeginCard(headerText, null, depth, ctx);

        card.Content =
            value.ValueKind == JsonValueKind.Object
                ? BuildCardContent(card, ctx, () => BuildObjectBody(value, depth + 1, ctx))
                : BuildCardContent(card, ctx, () => BuildArrayItemsPanel(value, depth + 1, ctx));

        return card;
    }

    /// Pousse `card` sur la pile d'ancêtres avant d'appeler `build()`
    /// (donc avant que son contenu n'enregistre la moindre entrée
    /// cherchable), puis la retire -- voir BuildContext.AncestorStack.
    private static UIElement
    BuildCardContent(
        Expander card,
        BuildContext ctx,
        Func<UIElement> build)
    {
        ctx.AncestorStack.Add(card);

        var content = build();

        ctx.AncestorStack.RemoveAt(ctx.AncestorStack.Count - 1);

        return content;
    }

    private static Panel
    BuildArrayItemsPanel(
        JsonElement array,
        int depth,
        BuildContext ctx)
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

                var itemCard = BeginCard(headerText, badge, depth, ctx);

                itemCard.Content = BuildCardContent(itemCard, ctx, () => BuildObjectBody(item, depth + 1, ctx));

                child = itemCard;
            }
            else if (item.ValueKind == JsonValueKind.Array)
            {
                var arrayItemCard = BeginCard($"[{i}]", null, depth, ctx);

                arrayItemCard.Content =
                    BuildCardContent(arrayItemCard, ctx, () => BuildArrayItemsPanel(item, depth + 1, ctx));

                child = arrayItemCard;
            }
            else
            {
                var text = FormatScalarElement(item);

                var scalarBlock =
                    new TextBlock
                    {
                        Text = text,
                        Foreground = TextPrimaryBrush,
                        FontSize = 12
                    };

                var wrapper = WrapForSearch(text, scalarBlock, ctx);
                wrapper.Margin = new Thickness(0, 0, 0, 4);

                MakeCopyable(wrapper, text);

                child = wrapper;
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

    /// Un Expander (style global de l'appli : coins arrondis, flèche,
    /// déjà utilisé pour "Response Headers"/"Body"/etc.) plutôt qu'un
    /// simple Border : replié par défaut, la réponse peut compter des
    /// dizaines de blocs imbriqués et les afficher tous dépliés d'un
    /// coup serait illisible. Content n'est délibérément pas encore
    /// affecté ici : l'appelant doit pousser la carte retournée sur
    /// BuildContext.AncestorStack (via BuildCardContent) avant de
    /// construire son contenu, pour que toute entrée cherchable qui y
    /// est enregistrée connaisse cette carte comme ancêtre.
    private static Expander
    BeginCard(
        string header,
        UIElement? badge,
        int depth,
        BuildContext ctx)
    {
        var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };

        var titleBlock =
            new TextBlock
            {
                Text = header,
                FontWeight = FontWeights.SemiBold,
                FontSize = Math.Max(11, 13 - depth),
                Foreground = TextPrimaryBrush,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };

        headerPanel.Children.Add(WrapForSearch(header, titleBlock, ctx));

        if (badge != null)
        {
            var badgeElement = (FrameworkElement)badge;

            badgeElement.VerticalAlignment = VerticalAlignment.Center;

            var badgeWrapper = WrapForSearch(GetDisplayText(badgeElement), badgeElement, ctx);
            badgeWrapper.Margin = new Thickness(8, 0, 0, 0);

            headerPanel.Children.Add(badgeWrapper);
        }

        return new Expander
        {
            Header = headerPanel,
            IsExpanded = false,
            Background = depth % 2 == 0 ? SurfaceBrush : SurfaceAltBrush,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 8)
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

    /// Enveloppe un élément dans un Border transparent dédié et
    /// enregistre (texte affiché, wrapper) comme entrée cherchable : le
    /// wrapper peut ensuite être surligné (Background) par la recherche
    /// sans jamais avoir à mémoriser/restaurer l'apparence d'origine de
    /// son contenu (y compris un badge déjà coloré).
    private static Border
    WrapForSearch(
        string text,
        FrameworkElement element,
        BuildContext ctx)
    {
        var wrapper =
            new Border
            {
                Background = Brushes.Transparent,
                CornerRadius = new CornerRadius(3),
                Child = element
            };

        if (!string.IsNullOrWhiteSpace(text))
        {
            ctx.SearchEntries.Add(new JsonCardSearchEntry(text, wrapper, ctx.AncestorStack.ToArray()));
        }

        return wrapper;
    }

    /// Double-clic, Ctrl+C (une fois la valeur cliquée pour lui donner le
    /// focus) ou clic droit -> "Copier" : trois façons de récupérer une
    /// valeur affichée (identifiant, email, numéro...) sans devoir passer
    /// par la vue Brut. Un bref flash vert confirme la copie plutôt qu'une
    /// boîte de dialogue, qui interromprait la lecture pour un geste aussi
    /// fréquent.
    private static void
    MakeCopyable(
        Border wrapper,
        string copyText)
    {
        if (string.IsNullOrEmpty(copyText))
        {
            return;
        }

        wrapper.Cursor = Cursors.Hand;
        wrapper.ToolTip = "Double-clic, Ctrl+C ou clic droit pour copier";
        wrapper.Focusable = true;

        void Copy()
        {
            try
            {
                Clipboard.SetText(copyText);
                FlashCopyFeedback(wrapper);
                ShowCopiedBubble(wrapper);
            }
            catch
            {
                // Presse-papiers verrouillé par une autre appli (rare) :
                // pas grave si une copie échoue silencieusement ici.
            }
        }

        wrapper.MouseLeftButtonDown += (_, e) =>
        {
            wrapper.Focus();

            if (e.ClickCount == 2)
            {
                Copy();
                e.Handled = true;
            }
        };

        wrapper.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                Copy();
                e.Handled = true;
            }
        };

        var copyMenuItem = new MenuItem { Header = "Copier" };
        copyMenuItem.Click += (_, __) => Copy();

        wrapper.ContextMenu = new ContextMenu { Items = { copyMenuItem } };
    }

    private static void
    FlashCopyFeedback(
        Border wrapper)
    {
        var original = wrapper.Background;

        wrapper.Background = CopyFeedbackBrush;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };

        timer.Tick += (_, __) =>
        {
            timer.Stop();
            wrapper.Background = original;
        };

        timer.Start();
    }

    /// Petite bulle "Copié" flottant au-dessus de la valeur, plutôt que de
    /// se fier au seul flash de fond -- pas assez explicite pour que
    /// l'utilisateur comprenne que la copie a bien eu lieu.
    private static void
    ShowCopiedBubble(
        Border wrapper)
    {
        var bubble = new Border
        {
            Background = new SolidColorBrush(SuccessColor),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 3, 8, 3),
            Child = new TextBlock
            {
                Text = "Copié ✓",
                Foreground = Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold
            }
        };

        var popup = new Popup
        {
            PlacementTarget = wrapper,
            Placement = PlacementMode.Top,
            VerticalOffset = -4,
            AllowsTransparency = true,
            StaysOpen = true,
            Child = bubble,
            IsOpen = true
        };

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };

        timer.Tick += (_, __) =>
        {
            timer.Stop();
            popup.IsOpen = false;
        };

        timer.Start();
    }

    private static string
    GetDisplayText(
        FrameworkElement element) =>
        element switch
        {
            TextBlock textBlock => textBlock.Text,
            Border { Child: TextBlock innerText } => innerText.Text,
            _ => ""
        };

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

    private static Brush
    MakeAlphaBrush(
        byte alpha,
        Color color)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        brush.Freeze();

        return brush;
    }
}

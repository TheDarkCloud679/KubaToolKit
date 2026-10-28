using KubaToolKit.Modules.TableViewer.Decoding;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace KubaToolKit.Modules.TableViewer;

// Same look and feel as API Client's JsonCardViewBuilder (the "Cards"
// view for a JSON response): nested, collapsible cards instead of a flat
// grid, with a field's value swapped for a human label when one is
// configured. Built from DecodedField's own Depth rather than a JSON
// tree, since that's what the decoder already produces.
public static class TableCardViewBuilder
{
    private static readonly Brush SurfaceBrush = MakeBrush(0xFF, 0xFF, 0xFF);
    private static readonly Brush SurfaceAltBrush = MakeBrush(0xF7, 0xF9, 0xFC);
    private static readonly Brush BorderBrush = MakeBrush(0xE1, 0xE5, 0xEC);
    private static readonly Brush TextPrimaryBrush = MakeBrush(0x1E, 0x24, 0x30);
    private static readonly Brush TextSecondaryBrush = MakeBrush(0x68, 0x70, 0x7E);
    private static readonly Brush TextMutedBrush = MakeBrush(0x98, 0xA1, 0xAF);
    private static readonly Brush CopyFeedbackBrush = MakeAlphaBrush(0x50, Color.FromRgb(0x1F, 0xA9, 0x71));

    private const int AccentBarMaxDepth = 2;

    private sealed class FieldNode
    {
        public required DecodedField Field { get; init; }

        public List<FieldNode> Children { get; } = new();
    }

    public static UIElement?
    Build(
        IReadOnlyList<DecodedField> fields,
        IReadOnlyDictionary<string, Dictionary<string, string>> valueLabels)
    {
        if (fields.Count == 0)
        {
            return null;
        }

        var panel = new StackPanel();

        RenderChildren(BuildTree(fields), panel, 0, valueLabels);

        return panel;
    }

    private static List<FieldNode>
    BuildTree(
        IReadOnlyList<DecodedField> fields)
    {
        var roots = new List<FieldNode>();
        var stack = new Stack<FieldNode>();

        foreach (var field in fields)
        {
            var node = new FieldNode { Field = field };

            while (stack.Count > 0 && stack.Peek().Field.Depth >= field.Depth)
            {
                stack.Pop();
            }

            if (stack.Count == 0)
            {
                roots.Add(node);
            }
            else
            {
                stack.Peek().Children.Add(node);
            }

            stack.Push(node);
        }

        return roots;
    }

    private static void
    RenderChildren(
        List<FieldNode> nodes,
        Panel parent,
        int depth,
        IReadOnlyDictionary<string, Dictionary<string, string>> valueLabels)
    {
        var scalars = nodes.Where(n => n.Children.Count == 0).ToList();
        var containers = nodes.Where(n => n.Children.Count > 0).ToList();

        if (scalars.Count == 0 && containers.Count == 0)
        {
            parent.Children.Add(MutedText("(empty)"));

            return;
        }

        if (scalars.Count > 0)
        {
            var scalarsElement = BuildScalarsSection(scalars, depth, valueLabels);

            // At the top level specifically, a run of one-off fields that
            // doesn't repeat (e.g. ADV's own version/structure/network/
            // login settings) has no card of its own to land in otherwise
            // -- landed as bare text with nothing containing it, unlike
            // everything else on the page. Wrapped in one "Général" card,
            // collapsed by default like every other card here. Nested
            // ungrouped scalars (already inside some real card) don't
            // need this -- they're already contained.
            if (depth == 0 && scalarsElement is Grid)
            {
                var generalCard = BeginGroupCard("Général", depth);

                generalCard.Content = scalarsElement;
                generalCard.Margin = new Thickness(0, 0, 0, containers.Count > 0 ? 8 : 0);

                parent.Children.Add(generalCard);
            }
            else
            {
                parent.Children.Add(scalarsElement);
            }
        }

        foreach (var node in containers)
        {
            var card = BeginCard(node, depth);

            var content = new StackPanel();

            RenderChildren(node.Children, content, depth + 1, valueLabels);

            card.Content = content;
            card.Margin = new Thickness(0, scalars.Count > 0 || parent.Children.Count > 1 ? 8 : 0, 0, 0);

            parent.Children.Add(card);
        }
    }

    // A scalar-only array (e.g. 237 Translation entries, each just
    // Signature id/Element id/String size/Translation) has no per-entry
    // card of its own to land in, so every entry's fields used to land as
    // one undifferentiated flat list -- unreadable past a handful of
    // entries, with no visual cue for where one ends and the next
    // starts, and every single one always on screen at once. Entries
    // repeat the same field names in the same order, so that's detected
    // here and each one rendered as its own collapsed-by-default "#N"
    // card instead, the same shell real sub-objects get.
    private static UIElement
    BuildScalarsSection(
        List<FieldNode> scalars,
        int depth,
        IReadOnlyDictionary<string, Dictionary<string, string>> valueLabels)
    {
        var period = DetectRepeatPeriod(scalars);

        if (period == null)
        {
            return BuildFieldsGrid(scalars, valueLabels);
        }

        var panel = new StackPanel();
        var groupCount = scalars.Count / period.Value;

        for (var g = 0; g < groupCount; g++)
        {
            var group = scalars.GetRange(g * period.Value, period.Value);

            var card = BeginGroupCard(BuildGroupSummaryHeader(g + 1, group, valueLabels), depth);

            card.Content = BuildFieldsGrid(group, valueLabels);
            card.Margin = new Thickness(0, 0, 0, g == groupCount - 1 ? 0 : 4);

            panel.Children.Add(card);
        }

        return panel;
    }

    // "#1" alone doesn't say anything -- e.g. a currency/denomination
    // entry (Type/Mantisse/Exposant/Devise) reads as "#1 Coin 10 EUR"
    // instead, so the entry's own gist shows without expanding it. Built
    // from the entry's own non-zero, non-empty values (an entry's own
    // zeroes are rarely what identifies it, and skipping them is what
    // keeps this from reading as noise), label-translated the same way
    // the grid itself would, capped so one entry with many fields can't
    // blow out every header's width.
    private static string
    BuildGroupSummaryHeader(
        int index,
        List<FieldNode> group,
        IReadOnlyDictionary<string, Dictionary<string, string>> valueLabels)
    {
        var parts = new List<string>();

        foreach (var node in group)
        {
            var value = node.Field.Value;

            if (string.IsNullOrWhiteSpace(value) || value == "0")
            {
                continue;
            }

            parts.Add(TryGetValueLabel(valueLabels, node.Field.Name, value) ?? value);
        }

        var summary = string.Join(" ", parts);

        if (summary.Length > 60)
        {
            summary = summary[..60] + "...";
        }

        return summary.Length > 0 ? $"#{index} {summary}" : $"#{index}";
    }

    // The smallest period >1 whose field-name sequence tiles the whole
    // list exactly (e.g. 948 entries cycling Signature id/Element
    // id/String size/Translation every 4 -> period 4). Only divisors of
    // the count are tried, so this stays fast even for a few thousand
    // entries; null when nothing repeats (an ordinary flat grid then).
    private static int?
    DetectRepeatPeriod(
        List<FieldNode> scalars)
    {
        var n = scalars.Count;

        if (n < 4)
        {
            return null;
        }

        for (var period = 2; period <= n / 2; period++)
        {
            if (n % period != 0)
            {
                continue;
            }

            var tiles = true;

            for (var i = period; i < n; i++)
            {
                if (scalars[i].Field.Name != scalars[i % period].Field.Name)
                {
                    tiles = false;

                    break;
                }
            }

            if (!tiles)
            {
                continue;
            }

            // A period whose own template is just the same field name
            // over and over (e.g. a run of unrelated repeated counters,
            // not one record's several fields) isn't a "record" worth
            // numbering -- an ordinary flat grid reads at least as well.
            var distinctNamesInPeriod = scalars.Take(period).Select(s => s.Field.Name).Distinct().Count();

            if (distinctNamesInPeriod > 1)
            {
                return period;
            }
        }

        return null;
    }

    private static Grid
    BuildFieldsGrid(
        IEnumerable<FieldNode> scalars,
        IReadOnlyDictionary<string, Dictionary<string, string>> valueLabels)
    {
        var grid = new Grid();

        // Both size to their own content rather than a fixed/stretched
        // width -- a wide window otherwise left the value column
        // stretched across a lot of empty space while a merely long
        // label name still wrapped to two lines in its fixed column.
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var row = 0;

        foreach (var node in scalars)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var keyBlock =
                new TextBlock
                {
                    Text = node.Field.Name,
                    Foreground = TextSecondaryBrush,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 1, 10, 1)
                };

            Grid.SetRow(keyBlock, row);
            Grid.SetColumn(keyBlock, 0);
            grid.Children.Add(keyBlock);

            var label = TryGetValueLabel(valueLabels, node.Field.Name, node.Field.Value);

            var valueWrapper =
                new Border
                {
                    Background = Brushes.Transparent,
                    CornerRadius = new CornerRadius(3),
                    Margin = new Thickness(0, 1, 0, 1),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Child =
                        new TextBlock
                        {
                            Text = label != null ? $"{label} ({node.Field.Value})" : node.Field.Value,
                            Foreground = TextPrimaryBrush,
                            FontSize = 11,
                            TextWrapping = TextWrapping.Wrap
                        }
                };

            MakeCopyable(valueWrapper, node.Field.Value);

            Grid.SetRow(valueWrapper, row);
            Grid.SetColumn(valueWrapper, 1);
            grid.Children.Add(valueWrapper);

            row++;
        }

        return grid;
    }

    private static string?
    TryGetValueLabel(
        IReadOnlyDictionary<string, Dictionary<string, string>> valueLabels,
        string fieldName,
        string rawValue) =>
        valueLabels.TryGetValue(fieldName, out var codes)
        && codes.TryGetValue(rawValue, out var label)
        && !string.IsNullOrWhiteSpace(label)
            ? label
            : null;

    private static Expander
    BeginCard(
        FieldNode node,
        int depth) =>
        BeginCard(node.Field.Name, node.Field.Value, depth);

    // A numbered entry within a repeating group (e.g. "#1" of 27 "cartes"
    // entries) is still just a card -- same shell, no field behind its
    // header to pull a value from.
    private static Expander
    BeginGroupCard(
        string header,
        int depth) =>
        BeginCard(header, "", depth);

    private static Expander
    BeginCard(
        string header,
        string value,
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

        if (!string.IsNullOrWhiteSpace(value))
        {
            headerPanel.Children.Add(
                new TextBlock
                {
                    Text = $"  -  {value}",
                    FontSize = Math.Max(10, 12 - depth),
                    Foreground = TextMutedBrush,
                    VerticalAlignment = VerticalAlignment.Center
                });
        }

        var expander =
            new Expander
            {
                Header = headerPanel,
                IsExpanded = false,
                Background = depth % 2 == 0 ? SurfaceBrush : SurfaceAltBrush,
                BorderBrush = BorderBrush,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 4)
            };

        // Past the first couple of levels, the default Expander style's
        // left accent bar just piles up alongside identical ones from
        // every ancestor -- same chrome minus the bar past that depth.
        if (depth >= AccentBarMaxDepth
            && Application.Current?.TryFindResource("ExpanderNoAccentBarStyle") is Style flatStyle)
        {
            expander.Style = flatStyle;
        }

        return expander;
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
        wrapper.ToolTip = "Double-click, Ctrl+C, or right-click to copy";
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
            }
        }

        wrapper.MouseLeftButtonDown +=
            (_, e) =>
            {
                wrapper.Focus();

                if (e.ClickCount == 2)
                {
                    Copy();
                    e.Handled = true;
                }
            };

        wrapper.PreviewKeyDown +=
            (_, e) =>
            {
                if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
                {
                    Copy();
                    e.Handled = true;
                }
            };

        var copyMenuItem = new MenuItem { Header = "Copy" };
        copyMenuItem.Click += (_, _) => Copy();

        wrapper.ContextMenu = new ContextMenu { Items = { copyMenuItem } };
    }

    private static void
    FlashCopyFeedback(
        Border wrapper)
    {
        var original = wrapper.Background;

        wrapper.Background = CopyFeedbackBrush;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };

        timer.Tick +=
            (_, _) =>
            {
                timer.Stop();
                wrapper.Background = original;
            };

        timer.Start();
    }

    private static void
    ShowCopiedBubble(
        Border wrapper)
    {
        var bubble =
            new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1F, 0xA9, 0x71)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 3, 8, 3),
                Child =
                    new TextBlock
                    {
                        Text = "Copied ✓",
                        Foreground = Brushes.White,
                        FontSize = 11,
                        FontWeight = FontWeights.SemiBold
                    }
            };

        var popup =
            new Popup
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

        timer.Tick +=
            (_, _) =>
            {
                timer.Stop();
                popup.IsOpen = false;
            };

        timer.Start();
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

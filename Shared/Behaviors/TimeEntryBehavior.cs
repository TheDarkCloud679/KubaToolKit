using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace KubaToolKit.Shared.Behaviors;

/// Édition d'un TextBox au format "HH:mm" par segments : cliquer sur la
/// partie heure ou minute la sélectionne entièrement (façon champ heure
/// de Windows), taper un chiffre remplace le premier puis le second
/// chiffre du segment actif, haut/bas incrémentent/décrémentent,
/// gauche/droite/Tab changent de segment. Activé via
/// behaviors:TimeEntryBehavior.Enabled="True" en XAML -- un seul endroit
/// pour ce comportement, partagé par la recherche de logs CloudWatch et
/// le sélecteur de plage du graphique Dashboard (plutôt que deux copies
/// qui finiraient par diverger).
public static class TimeEntryBehavior
{
    private sealed class EditState
    {
        public bool EditingSecondHourDigit;
        public bool EditingSecondMinuteDigit;
    }

    private static readonly Dictionary<TextBox, EditState> States = new();

    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached(
            "Enabled",
            typeof(bool),
            typeof(TimeEntryBehavior),
            new PropertyMetadata(false, OnEnabledChanged));

    public static void
    SetEnabled(
        DependencyObject element,
        bool value) =>
        element.SetValue(EnabledProperty, value);

    public static bool
    GetEnabled(
        DependencyObject element) =>
        (bool)element.GetValue(EnabledProperty);

    private static void
    OnEnabledChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox textBox)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            States[textBox] = new EditState();

            textBox.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
            textBox.GotKeyboardFocus += OnGotKeyboardFocus;
            textBox.LostFocus += OnLostFocus;
            textBox.PreviewKeyDown += OnPreviewKeyDown;
        }
        else
        {
            States.Remove(textBox);

            textBox.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
            textBox.GotKeyboardFocus -= OnGotKeyboardFocus;
            textBox.LostFocus -= OnLostFocus;
            textBox.PreviewKeyDown -= OnPreviewKeyDown;
        }
    }

    private static void
    OnPreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        e.Handled = true;

        // Index du caractère cliqué, indépendant du focus actuel : permet
        // de savoir si l'utilisateur vise la partie heure ou minute même
        // au tout premier clic (avant que le focus ne soit posé).
        int charIndex =
            textBox.GetCharacterIndexFromPoint(
                e.GetPosition(textBox),
                true);

        if (!textBox.IsKeyboardFocusWithin)
        {
            textBox.Focus();
        }

        textBox.Dispatcher.BeginInvoke(
            new Action(() =>
            {
                if (charIndex <= 2)
                {
                    SelectHourPart(textBox);
                }
                else
                {
                    SelectMinutePart(textBox);
                }
            }),
            DispatcherPriority.Input);
    }

    private static void
    OnGotKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        textBox.Dispatcher.BeginInvoke(
            new Action(() => SelectHourPart(textBox)),
            DispatcherPriority.Input);
    }

    private static void
    OnLostFocus(
        object sender,
        RoutedEventArgs e) =>
        FormatTimeTextBox(sender as TextBox);

    private static void
    OnPreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (sender is not TextBox textBox
            || !States.TryGetValue(textBox, out var state))
        {
            return;
        }

        string[] parts = textBox.Text.Split(':');

        // sécurité
        if (parts.Length != 2)
        {
            textBox.Text = "00:00";
            parts = textBox.Text.Split(':');
        }

        // navigation gauche/droite
        if (e.Key == Key.Left)
        {
            SelectHourPart(textBox);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Right || e.Key == Key.Tab)
        {
            SelectMinutePart(textBox);
            e.Handled = true;
            return;
        }

        // flèches haut/bas
        if (e.Key == Key.Up || e.Key == Key.Down)
        {
            bool increase = e.Key == Key.Up;
            bool editingHours = textBox.SelectionStart < 3;

            if (editingHours)
            {
                int hours = int.Parse(parts[0]);
                hours += increase ? 1 : -1;

                if (hours > 23) hours = 0;
                if (hours < 0) hours = 23;

                textBox.Text = $"{hours:00}:{parts[1]}";
                SelectHourPart(textBox);
            }
            else
            {
                int minutes = int.Parse(parts[1]);
                minutes += increase ? 1 : -1;

                if (minutes > 59) minutes = 0;
                if (minutes < 0) minutes = 59;

                textBox.Text = $"{parts[0]}:{minutes:00}";
                SelectMinutePart(textBox);
            }

            e.Handled = true;
            return;
        }

        // chiffres seulement
        bool isDigit =
            (e.Key >= Key.D0 && e.Key <= Key.D9)
            || (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9);

        if (!isDigit)
        {
            return;
        }

        string digit =
            e.Key >= Key.NumPad0
                ? ((int)e.Key - (int)Key.NumPad0).ToString()
                : ((int)e.Key - (int)Key.D0).ToString();

        bool editingHour = textBox.SelectionStart < 3;

        // édition HH
        if (editingHour)
        {
            if (!state.EditingSecondHourDigit)
            {
                parts[0] = $"{digit}{parts[0][1]}";
                state.EditingSecondHourDigit = true;

                textBox.Text = $"{parts[0]}:{parts[1]}";
                SelectHourPart(textBox, false);
            }
            else
            {
                parts[0] = $"{parts[0][0]}{digit}";

                int hours = Math.Clamp(int.Parse(parts[0]), 0, 23);

                textBox.Text = $"{hours:00}:{parts[1]}";
                state.EditingSecondHourDigit = false;

                SelectMinutePart(textBox);
            }
        }
        else
        {
            if (!state.EditingSecondMinuteDigit)
            {
                parts[1] = $"{digit}{parts[1][1]}";
                state.EditingSecondMinuteDigit = true;

                textBox.Text = $"{parts[0]}:{parts[1]}";
                SelectMinutePart(textBox, false);
            }
            else
            {
                parts[1] = $"{parts[1][0]}{digit}";

                int minutes = Math.Clamp(int.Parse(parts[1]), 0, 59);

                textBox.Text = $"{parts[0]}:{minutes:00}";
                state.EditingSecondMinuteDigit = false;

                SelectHourPart(textBox);
            }
        }

        e.Handled = true;
    }

    private static void
    SelectHourPart(
        TextBox textBox,
        bool reset = true)
    {
        if (reset && States.TryGetValue(textBox, out var state))
        {
            state.EditingSecondHourDigit = false;
        }

        textBox.SelectionStart = 0;
        textBox.SelectionLength = 2;
    }

    private static void
    SelectMinutePart(
        TextBox textBox,
        bool reset = true)
    {
        if (reset && States.TryGetValue(textBox, out var state))
        {
            state.EditingSecondMinuteDigit = false;
        }

        textBox.SelectionStart = 3;
        textBox.SelectionLength = 2;
    }

    private static void
    FormatTimeTextBox(
        TextBox? textBox)
    {
        if (textBox == null)
        {
            return;
        }

        string[] parts = textBox.Text.Split(':');

        if (parts.Length != 2)
        {
            textBox.Text = "00:00";
            return;
        }

        int hours =
            Math.Clamp(
                int.TryParse(parts[0], out var h) ? h : 0,
                0,
                23);

        int minutes =
            Math.Clamp(
                int.TryParse(parts[1], out var m) ? m : 0,
                0,
                59);

        textBox.Text = $"{hours:00}:{minutes:00}";
    }
}

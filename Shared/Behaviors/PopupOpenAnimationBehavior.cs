using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace KubaToolKit.Shared.Behaviors;

// A dropdown Popup's content lives in a separate visual tree branch that
// WPF can keep alive across open/close cycles, so a plain Loaded (or
// IsDropDownOpen property trigger) on that content doesn't reliably
// re-fire on every reopen -- it only fired once, the very first time.
// Popup.Opened is a genuine CLR event (not a RoutedEvent, so it can't be
// wired via a XAML EventTrigger either) that WPF does raise every single
// time the popup opens, so this behavior hooks that directly and drives
// the fade/rise from code instead.
public static class PopupOpenAnimationBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(PopupOpenAnimationBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static void
    SetIsEnabled(
        DependencyObject element,
        bool value)
    {
        element.SetValue(
            IsEnabledProperty,
            value);
    }

    public static bool
    GetIsEnabled(
        DependencyObject element)
    {
        return (bool)element.GetValue(
            IsEnabledProperty);
    }

    private static void
    OnIsEnabledChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e)
    {
        if (d is not Popup popup)
        {
            return;
        }

        popup.Opened -= OnPopupOpened;

        if ((bool)e.NewValue)
        {
            popup.Opened += OnPopupOpened;
        }
    }

    private static void
    OnPopupOpened(
        object? sender,
        EventArgs e)
    {
        if (sender is not Popup { Child: FrameworkElement child })
        {
            return;
        }

        var translate = new TranslateTransform(0, -6);

        child.RenderTransform = translate;
        child.Opacity = 0;

        var duration = new Duration(TimeSpan.FromSeconds(0.14));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        child.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, duration) { EasingFunction = ease });

        translate.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(-6, 0, duration) { EasingFunction = ease });
    }
}

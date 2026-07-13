using System.Windows;
using System.Windows.Media.Animation;

namespace KubaToolKit.Shared.Behaviors;

/// Anime en douceur l'ouverture/fermeture d'un conteneur (typiquement le
/// ContentPresenter/ItemsPresenter caché par défaut d'un Expander ou d'un
/// TreeViewItem), au lieu du repli instantané de WPF (juste
/// Visibility="Collapsed" du jour au lendemain). Se pilote depuis un
/// ControlTemplate via un Trigger qui reflète IsExpanded sur cette
/// propriété attachée plutôt que directement sur Visibility -- voir
/// Styles/Controls.xaml (styles Expander et TreeViewItem).
public static class SmoothExpandBehavior
{
    private const int ExpandMs = 180;
    private const int CollapseMs = 140;

    public static readonly DependencyProperty IsExpandedProperty =
        DependencyProperty.RegisterAttached(
            "IsExpanded",
            typeof(bool),
            typeof(SmoothExpandBehavior),
            new PropertyMetadata(false, OnIsExpandedChanged));

    public static void
    SetIsExpanded(
        DependencyObject element,
        bool value) =>
        element.SetValue(IsExpandedProperty, value);

    public static bool
    GetIsExpanded(
        DependencyObject element) =>
        (bool)element.GetValue(IsExpandedProperty);

    private static void
    OnIsExpandedChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            Expand(element);
        }
        else
        {
            Collapse(element);
        }
    }

    private static void
    Expand(
        FrameworkElement element)
    {
        element.Visibility = Visibility.Visible;

        // Mesure la hauteur "naturelle" du contenu (Height reste à Auto
        // jusqu'ici) pour savoir jusqu'où animer -- indispensable puisque
        // ce contenu n'a jamais été affiché et n'a donc pas d'ActualHeight
        // exploitable avant cette mesure explicite.
        element.Measure(
            new Size(
                element.ActualWidth > 0 ? element.ActualWidth : double.PositiveInfinity,
                double.PositiveInfinity));

        var targetHeight = element.DesiredSize.Height;

        element.Height = 0;

        var animation =
            new DoubleAnimation
            {
                From = 0,
                To = targetHeight,
                Duration = new Duration(TimeSpan.FromMilliseconds(ExpandMs)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

        // Revient à Auto une fois déplié : un contenu qui change de taille
        // ensuite (nouvelle ligne dans une grille, etc.) doit pouvoir
        // s'ajuster librement plutôt que rester bloqué à la hauteur mesurée
        // au moment du dépli.
        animation.Completed += (_, _) => element.Height = double.NaN;

        element.BeginAnimation(FrameworkElement.HeightProperty, animation);
    }

    private static void
    Collapse(
        FrameworkElement element)
    {
        var currentHeight =
            double.IsNaN(element.Height) || double.IsNaN(element.ActualHeight)
                ? element.ActualHeight
                : element.Height;

        // Stoppe une animation de dépli en cours et fige la hauteur
        // actuelle avant de repartir dans l'autre sens, sinon le repli
        // reprendrait depuis une valeur Height=Auto non animable.
        element.BeginAnimation(FrameworkElement.HeightProperty, null);
        element.Height = currentHeight;

        var animation =
            new DoubleAnimation
            {
                From = currentHeight,
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(CollapseMs)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

        animation.Completed += (_, _) => element.Visibility = Visibility.Collapsed;

        element.BeginAnimation(FrameworkElement.HeightProperty, animation);
    }
}

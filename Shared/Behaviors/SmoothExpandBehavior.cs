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
        // Measure(new Size(width, double.PositiveInfinity)) donnait des
        // hauteurs aberrantes dès que le contenu incluait un DataGrid (une
        // largeur/hauteur non contrainte perturbe son calcul de layout,
        // menant à une carte qui restait bien plus haute que son contenu
        // une fois "dépliée"). UpdateLayout() force à la place une vraie
        // passe de mise en page, avec les contraintes réelles de l'arbre
        // visuel (largeur du parent, etc.), donc une ActualHeight fiable
        // quel que soit le contenu.
        element.BeginAnimation(FrameworkElement.HeightProperty, null);
        element.Opacity = 0;
        element.Visibility = Visibility.Visible;
        element.Height = double.NaN;
        element.UpdateLayout();

        var targetHeight = element.ActualHeight;

        element.Height = 0;

        var opacityAnimation =
            new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(ExpandMs))
            };

        element.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);

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

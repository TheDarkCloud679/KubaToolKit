using System.Windows;

namespace KubaToolKit.Shared.Behaviors;

// Just a value store -- MainWindow's own PreviewMouseWheel handler is what
// actually reads this (per ScrollViewer, walking up the tree) and applies
// it as pixels-per-notch = LinesPerNotch * 16. A plain attached property
// rather than a per-ScrollViewer event subscription, since the app already
// funnels every wheel event through one place at the Window level.
public static class ScrollSpeedBehavior
{
    public static readonly DependencyProperty LinesPerNotchProperty =
        DependencyProperty.RegisterAttached(
            "LinesPerNotch",
            typeof(double),
            typeof(ScrollSpeedBehavior),
            new PropertyMetadata(0.0));

    public static void
    SetLinesPerNotch(
        DependencyObject element,
        double value)
    {
        element.SetValue(
            LinesPerNotchProperty,
            value);
    }

    public static double
    GetLinesPerNotch(
        DependencyObject element)
    {
        return (double)element.GetValue(
            LinesPerNotchProperty);
    }
}

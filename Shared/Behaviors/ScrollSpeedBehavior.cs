using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace KubaToolKit.Shared.Behaviors;

public static class ScrollSpeedBehavior
{
    public static readonly DependencyProperty LinesPerNotchProperty =
        DependencyProperty.RegisterAttached(
            "LinesPerNotch",
            typeof(double),
            typeof(ScrollSpeedBehavior),
            new PropertyMetadata(0.0, OnLinesPerNotchChanged));

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

    private static void
    OnLinesPerNotchChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer scrollViewer)
        {
            return;
        }

        scrollViewer.PreviewMouseWheel -=
            OnPreviewMouseWheel;

        if ((double)e.NewValue > 0)
        {
            scrollViewer.PreviewMouseWheel +=
                OnPreviewMouseWheel;
        }
    }

    private static void
    OnPreviewMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        double linesPerNotch =
            GetLinesPerNotch(scrollViewer);

        double offset =
            -e.Delta / 120.0
            * linesPerNotch
            * 16;

        scrollViewer.ScrollToVerticalOffset(
            scrollViewer.VerticalOffset + offset);

        e.Handled = true;
    }
}

using System.Windows.Controls;
using System.Windows.Input;

namespace KubaToolKit.Modules.Wiki;

// A ScrollViewer's own mouse-wheel-scrolls behavior can't be suppressed
// from a PreviewMouseWheel handler by setting e.Handled = true: WPF
// registers ScrollViewer's wheel class handler with handledEventsToo,
// so it still scrolls regardless. The featured-image viewer wants the
// wheel exclusively for zoom (panning is already a manual left-click
// drag), which fights that built-in scroll for every tick -- this
// override just turns the built-in behavior off.
public class NoWheelScrollViewer
    : ScrollViewer
{
    protected override void
    OnMouseWheel(
        MouseWheelEventArgs e)
    {
    }
}

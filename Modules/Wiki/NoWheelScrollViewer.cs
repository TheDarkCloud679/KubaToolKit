using System.Windows.Controls;
using System.Windows.Input;

namespace KubaToolKit.Modules.Wiki;

// ScrollViewer's own wheel-scroll behavior lives inside the base
// OnMouseWheel override -- it runs regardless of whether an earlier
// PreviewMouseWheel handler already marked the event Handled, since its
// class handler is registered with handledEventsToo. Routing the
// decision through this override instead of fighting it from a
// PreviewMouseWheel handler is what actually lets the plain wheel be
// repurposed for zoom while Ctrl+wheel keeps the normal vertical scroll.
public class NoWheelScrollViewer
    : ScrollViewer
{
    public event MouseWheelEventHandler? ZoomWheel;

    protected override void
    OnMouseWheel(
        MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            base.OnMouseWheel(e);

            return;
        }

        ZoomWheel?.Invoke(this, e);

        e.Handled = true;
    }
}

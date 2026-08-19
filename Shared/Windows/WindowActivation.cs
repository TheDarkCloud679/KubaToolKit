using System.Windows;

namespace KubaToolKit.Shared.Windows;

// Window.Activate() alone is unreliable for bringing a freshly-shown
// window to the front -- Windows' anti-focus-stealing heuristics can
// silently ignore it, especially once something async (a WebBrowser
// control's lazy ActiveX initialization, a network fetch...) happens
// after the window is already on screen. Toggling Topmost forces a real
// Z-order re-evaluation, which reliably brings it forward where
// Activate() alone sometimes doesn't.
public static class WindowActivation
{
    public static void
    ForceToForeground(
        Window window)
    {
        window.Activate();
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();
    }
}

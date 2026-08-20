using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace KubaToolKit.Shared.Windows;

// Window.Activate() (and even toggling Topmost) can still lose to
// Windows' foreground-lock heuristic: a background process asking for
// the foreground is routinely denied unless it briefly attaches its
// input queue to whatever currently owns it first. That's the actual
// Win32-level trick this wraps -- AttachThreadInput just long enough for
// SetForegroundWindow to be honored, then detaches again.
public static class WindowActivation
{
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    public static void
    ForceToForeground(
        Window window)
    {
        // Only meaningful once the window actually has a Win32 handle --
        // that doesn't exist until it's been shown at least once.
        var handle = new WindowInteropHelper(window).Handle;

        if (handle == IntPtr.Zero)
        {
            window.Activate();

            return;
        }

        var foregroundWindow = GetForegroundWindow();
        var foregroundThreadId = GetWindowThreadProcessId(foregroundWindow, IntPtr.Zero);
        var currentThreadId = GetCurrentThreadId();

        if (foregroundThreadId != 0 && foregroundThreadId != currentThreadId)
        {
            AttachThreadInput(currentThreadId, foregroundThreadId, true);

            try
            {
                SetForegroundWindow(handle);
            }
            finally
            {
                AttachThreadInput(currentThreadId, foregroundThreadId, false);
            }
        }
        else
        {
            SetForegroundWindow(handle);
        }

        window.Activate();
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();
    }
}

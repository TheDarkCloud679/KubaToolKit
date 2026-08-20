using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

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

    // Use this instead of a plain Show() for a window that must open
    // already in front, not just get corrected a moment after appearing.
    // Setting Topmost before Show() means it's placed above everything
    // else in the Z-order the instant it's actually created, instead of
    // trying to fix its position afterward -- which raced against
    // whatever else Windows was doing right then and still let it flash
    // behind briefly before ForceToForeground caught up.
    public static void
    ShowActivated(
        Window window)
    {
        window.Topmost = true;

        window.Show();

        ForceToForeground(window);

        window.Topmost = false;

        // The above still isn't 100% reliable on its own -- depending on
        // exact OS/timing conditions it can silently fail. A few short,
        // early rechecks catch that without risking stealing focus back
        // from a window the user deliberately switched to later on: each
        // one bails the instant this window is genuinely in front, and
        // the whole thing gives up for good well under a second in.
        var handle = new WindowInteropHelper(window).Handle;
        var attemptsLeft = 5;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };

        timer.Tick += (_, __) =>
        {
            attemptsLeft--;

            if (GetForegroundWindow() == handle || attemptsLeft <= 0)
            {
                timer.Stop();

                return;
            }

            ForceToForeground(window);
        };

        timer.Start();
    }

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

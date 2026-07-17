using PulseBar.Windows.Interop;

namespace PulseBar.Windows.Taskbar;

public static class WindowStyles
{
    /// <summary>
    /// Makes the overlay a non-activating tool window: hidden from Alt-Tab and
    /// never stealing focus from the foreground app.
    /// </summary>
    public static void MakeUnobtrusiveOverlay(IntPtr hwnd)
    {
        var exStyle = (long)User32.GetWindowLongPtr(hwnd, User32.GWL_EXSTYLE);
        exStyle |= User32.WS_EX_TOOLWINDOW | User32.WS_EX_NOACTIVATE;
        User32.SetWindowLongPtr(hwnd, User32.GWL_EXSTYLE, (IntPtr)exStyle);
    }

    public static uint RegisterTaskbarCreatedMessage()
        => User32.RegisterWindowMessageW(TaskbarLocator.TaskbarCreatedMessageName);

    /// <summary>
    /// Re-asserts topmost z-order without stealing focus. Fullscreen apps push the
    /// overlay behind; calling this on the reposition cycle brings it back once the
    /// fullscreen app is gone.
    /// </summary>
    public static void EnsureTopmost(IntPtr hwnd)
        => User32.SetWindowPos(
            hwnd, User32.HWND_TOPMOST, 0, 0, 0, 0,
            User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_NOACTIVATE);
}

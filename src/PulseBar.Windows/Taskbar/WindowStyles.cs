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
}

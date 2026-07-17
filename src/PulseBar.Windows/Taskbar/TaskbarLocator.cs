using System.Drawing;
using PulseBar.Windows.Interop;

namespace PulseBar.Windows.Taskbar;

public enum TaskbarEdge
{
    Left = 0,
    Top = 1,
    Right = 2,
    Bottom = 3,
}

/// <summary>Everything read from the live taskbar, in device pixels.</summary>
public sealed record TaskbarInfo(
    Rectangle Bounds,
    Rectangle? TrayBounds,
    TaskbarEdge Edge,
    bool AutoHide);

/// <summary>
/// Reads the primary taskbar geometry via FindWindow("Shell_TrayWnd") + GetWindowRect.
/// Never injects into or reparents onto Explorer windows.
/// </summary>
public static class TaskbarLocator
{
    public const string TaskbarCreatedMessageName = "TaskbarCreated";

    /// <summary>Null when the taskbar window cannot be found (e.g. Explorer restarting).</summary>
    public static TaskbarInfo? Locate()
    {
        var taskbar = User32.FindWindowW("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero || !User32.GetWindowRect(taskbar, out var taskbarRect))
        {
            return null;
        }

        var bounds = taskbarRect.ToRectangle();

        Rectangle? trayBounds = null;
        var tray = User32.FindWindowExW(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        if (tray != IntPtr.Zero && User32.GetWindowRect(tray, out var trayRect))
        {
            var rect = trayRect.ToRectangle();
            if (rect.Width > 0 && rect.Height > 0)
            {
                trayBounds = rect;
            }
        }

        return new TaskbarInfo(bounds, trayBounds, GetEdge(bounds), IsAutoHide());
    }

    /// <summary>
    /// Where the overlay should sit (device px): right-aligned just left of the tray
    /// area, vertically centered in the taskbar. Returns null for vertical taskbars
    /// (callers fall back to floating mode).
    /// </summary>
    public static Rectangle? ComputeOverlayRect(
        TaskbarInfo taskbar,
        int overlayWidthPx,
        int overlayHeightPx,
        int marginPx = 8,
        int safeTrayFallbackPx = 220)
    {
        if (taskbar.Edge is TaskbarEdge.Left or TaskbarEdge.Right)
        {
            return null;
        }

        var bounds = taskbar.Bounds;

        // Anchor to the tray's left edge when known, otherwise keep a safe margin
        // from the right end of the taskbar.
        var rightLimit = taskbar.TrayBounds?.Left ?? bounds.Right - safeTrayFallbackPx;

        var x = rightLimit - marginPx - overlayWidthPx;
        var minX = bounds.Left + marginPx;
        if (x < minX)
        {
            x = minX;
        }

        var y = bounds.Top + (bounds.Height - overlayHeightPx) / 2;
        return new Rectangle(x, y, overlayWidthPx, overlayHeightPx);
    }

    private static TaskbarEdge GetEdge(Rectangle bounds)
    {
        var data = new Shell32.APPBARDATA
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Shell32.APPBARDATA>(),
        };
        var result = Shell32.SHAppBarMessage(Shell32.ABM_GETTASKBARPOS, ref data);
        if (result != UIntPtr.Zero)
        {
            return (TaskbarEdge)data.uEdge;
        }

        // SHAppBarMessage unavailable: a wider-than-tall window is horizontal.
        return bounds.Width >= bounds.Height ? TaskbarEdge.Bottom : TaskbarEdge.Left;
    }

    private static bool IsAutoHide()
    {
        var data = new Shell32.APPBARDATA
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Shell32.APPBARDATA>(),
        };
        var state = Shell32.SHAppBarMessage(Shell32.ABM_GETSTATE, ref data);
        return ((long)state & Shell32.ABS_AUTOHIDE) != 0;
    }
}

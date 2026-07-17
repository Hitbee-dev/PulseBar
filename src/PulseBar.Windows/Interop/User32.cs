using System.Runtime.InteropServices;

namespace PulseBar.Windows.Interop;

internal static class User32
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly System.Drawing.Rectangle ToRectangle()
            => System.Drawing.Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr FindWindowW(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr FindWindowExW(
        IntPtr parent, IntPtr childAfter, string? className, string? windowName);

    [DllImport("user32.dll")]
    internal static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern uint RegisterWindowMessageW(string message);

    internal const int GWL_EXSTYLE = -20;
    internal const long WS_EX_TOOLWINDOW = 0x00000080;
    internal const long WS_EX_NOACTIVATE = 0x08000000;

    internal static readonly IntPtr HWND_TOPMOST = new(-1);
    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll")]
    internal static extern bool SetWindowPos(
        IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    internal static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr newLong);
}

internal static class Shell32
{
    internal const uint ABM_GETSTATE = 0x00000004;
    internal const uint ABM_GETTASKBARPOS = 0x00000005;
    internal const int ABS_AUTOHIDE = 0x0000001;

    [StructLayout(LayoutKind.Sequential)]
    internal struct APPBARDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public User32.RECT rc;
        public IntPtr lParam;
    }

    [DllImport("shell32.dll")]
    internal static extern UIntPtr SHAppBarMessage(uint message, ref APPBARDATA data);
}

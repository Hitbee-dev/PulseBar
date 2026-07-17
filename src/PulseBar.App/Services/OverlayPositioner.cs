using System.Windows;
using Microsoft.Extensions.Logging;
using PulseBar.Core.Configuration;
using PulseBar.Windows.Taskbar;

namespace PulseBar.App.Services;

/// <summary>
/// Places the overlay window: docked left of the tray area when possible,
/// floating (user-draggable, position persisted per monitor) otherwise.
/// All Win32 coordinates are device px; WPF Left/Top are DIPs.
/// </summary>
public sealed class OverlayPositioner
{
    private readonly IConfigurationService _config;
    private readonly ILogger<OverlayPositioner> _logger;

    public OverlayPositioner(IConfigurationService config, ILogger<OverlayPositioner> logger)
    {
        _config = config;
        _logger = logger;
    }

    public bool IsFloating { get; private set; } = true;

    public void Reposition(Window window)
    {
        try
        {
            if (_config.Current.Appearance.Mode == DisplayMode.TaskbarOverlay
                && TryDockToTaskbar(window))
            {
                IsFloating = false;
                return;
            }
        }
        catch (Exception ex)
        {
            // Overlay placement failure must never take the app down (spec 5.1).
            _logger.LogWarning(ex, "Taskbar docking failed; using floating mode.");
        }

        IsFloating = true;
        ApplyFloatingPosition(window);
    }

    public void SaveFloatingPosition(Window window)
    {
        if (!IsFloating)
        {
            return;
        }

        var key = MonitorKey(window);
        _config.Update(c => c.Appearance.FloatingPositions[key] =
            new FloatingPosition { X = window.Left, Y = window.Top });
    }

    private bool TryDockToTaskbar(Window window)
    {
        var taskbar = TaskbarLocator.Locate();
        if (taskbar is null || taskbar.AutoHide)
        {
            return false;
        }

        var compositionTarget = PresentationSource.FromVisual(window)?.CompositionTarget;
        if (compositionTarget is null || window.ActualWidth <= 0)
        {
            return false;
        }

        var toDevice = compositionTarget.TransformToDevice;
        var widthPx = (int)Math.Ceiling(window.ActualWidth * toDevice.M11);
        var heightPx = (int)Math.Ceiling(window.ActualHeight * toDevice.M22);

        var rect = TaskbarLocator.ComputeOverlayRect(taskbar, widthPx, heightPx);
        if (rect is null)
        {
            return false; // Vertical taskbar: the horizontal bar doesn't fit inside it.
        }

        var fromDevice = compositionTarget.TransformFromDevice;
        window.Left = rect.Value.X * fromDevice.M11;
        window.Top = rect.Value.Y * fromDevice.M22;
        return true;
    }

    private void ApplyFloatingPosition(Window window)
    {
        var key = MonitorKey(window);
        if (_config.Current.Appearance.FloatingPositions.TryGetValue(key, out var saved))
        {
            window.Left = saved.X;
            window.Top = saved.Y;
            return;
        }

        // Default: just above the taskbar area, bottom-right of the work area.
        var workArea = SystemParameters.WorkArea;
        window.Left = workArea.Right - Math.Max(window.ActualWidth, 200) - 16;
        window.Top = workArea.Bottom - Math.Max(window.ActualHeight, 24) - 8;
    }

    private static string MonitorKey(Window window)
    {
        try
        {
            var screen = System.Windows.Forms.Screen.FromPoint(
                new System.Drawing.Point((int)window.Left, (int)window.Top));
            return screen.DeviceName;
        }
        catch (Exception)
        {
            return "primary";
        }
    }
}

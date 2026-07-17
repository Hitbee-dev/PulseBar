using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using PulseBar.App.Views;
using PulseBar.Core.Configuration;
using PulseBar.Core.Localization;
using PulseBar.Windows.Startup;

namespace PulseBar.App.Services;

/// <summary>
/// Notification-area icon with the localized context menu.
/// Uses the WinForms NotifyIcon (runs fine on the WPF dispatcher thread).
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly ILocalizationService _loc;
    private readonly IConfigurationService _config;
    private readonly IStartupManager _startup;
    private readonly IAppPaths _paths;
    private readonly ILogger<TrayIconService> _logger;

    private NotifyIcon? _icon;
    private SettingsWindow? _settingsWindow;

    public TrayIconService(
        ILocalizationService loc,
        IConfigurationService config,
        IStartupManager startup,
        IAppPaths paths,
        ILogger<TrayIconService> logger)
    {
        _loc = loc;
        _config = config;
        _startup = startup;
        _paths = paths;
        _logger = logger;
    }

    /// <summary>Raised when the user asks for a manual provider refresh.</summary>
    public event EventHandler? RefreshRequested;

    /// <summary>Raised when the user opts in to Claude OTel telemetry installation.</summary>
    public event EventHandler? OtelInstallRequested;

    public void RequestRefresh() => RefreshRequested?.Invoke(this, EventArgs.Empty);

    public void ShowSettingsWindow() => ShowSettings();

    public void Initialize()
    {
        _icon = new NotifyIcon
        {
            Icon = CreateTrayIcon(),
            Text = _loc["App_Name"],
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };
        _icon.DoubleClick += (_, _) => ShowSettings();

        _loc.PropertyChanged += (_, _) =>
        {
            if (_icon is not null)
            {
                _icon.ContextMenuStrip = BuildMenu();
            }
        };
    }

    public void Dispose()
    {
        if (_icon is not null)
        {
            _icon.Visible = false;
            _icon.Dispose();
            _icon = null;
        }
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        menu.Items.Add(_loc["Tray_Refresh"], null, (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_loc["Tray_Settings"], null, (_, _) => ShowSettings());

        var startupItem = new ToolStripMenuItem(_loc["Tray_StartupRegister"])
        {
            CheckOnClick = true,
            Checked = _startup.IsRegistered(),
        };
        startupItem.CheckedChanged += (_, _) => ToggleStartup(startupItem.Checked);
        menu.Items.Add(startupItem);

        menu.Items.Add(
            _loc["Tray_InstallOtel"], null, (_, _) => OtelInstallRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(_loc["Tray_OpenLogs"], null, (_, _) => OpenLogsFolder());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_loc["Tray_Exit"], null, (_, _) => System.Windows.Application.Current.Shutdown());

        return menu;
    }

    private void ShowSettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(new ViewModels.SettingsViewModel(_loc, _config));
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private void ToggleStartup(bool register)
    {
        try
        {
            if (register)
            {
                _startup.Register();
            }
            else
            {
                _startup.Unregister();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to change startup registration.");
        }
    }

    private void OpenLogsFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _paths.LogsDir,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open logs folder.");
        }
    }

    /// <summary>Draws the three-bar "pulse" icon at runtime (no binary asset needed).</summary>
    private static Icon CreateTrayIcon()
    {
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            using var accent = new SolidBrush(Color.FromArgb(0, 200, 140));
            g.FillRectangle(accent, 2, 9, 3, 6);
            g.FillRectangle(accent, 7, 5, 3, 10);
            g.FillRectangle(accent, 12, 1, 3, 14);
        }

        var handle = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}

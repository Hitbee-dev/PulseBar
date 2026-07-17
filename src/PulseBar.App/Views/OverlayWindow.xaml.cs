using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using PulseBar.App.Services;
using PulseBar.App.ViewModels;
using PulseBar.Core.Localization;
using PulseBar.Windows.Taskbar;

namespace PulseBar.App.Views;

public partial class OverlayWindow : Window
{
    private readonly OverlayPositioner _positioner;
    private readonly Action _openSettings;
    private readonly Action _requestRefresh;
    private uint _taskbarCreatedMessage;

    public OverlayWindow(
        OverlayViewModel viewModel,
        ILocalizationService loc,
        OverlayPositioner positioner,
        Action openSettings,
        Action requestRefresh)
    {
        _positioner = positioner;
        _openSettings = openSettings;
        _requestRefresh = requestRefresh;
        DataContext = new { Bar = viewModel, Loc = loc };
        InitializeComponent();

        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) => _positioner.Reposition(this);
        SizeChanged += (_, _) => _positioner.Reposition(this);
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseDoubleClick += (_, _) => _openSettings();

        // Taskbar/tray widths change while apps come and go; track them cheaply.
        var repositionTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5),
        };
        repositionTimer.Tick += (_, _) => _positioner.Reposition(this);
        repositionTimer.Start();

        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        Closed += (_, _) =>
        {
            repositionTimer.Stop();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        };
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        => Dispatcher.BeginInvoke(() => _positioner.Reposition(this));

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        WindowStyles.MakeUnobtrusiveOverlay(hwnd);

        _taskbarCreatedMessage = WindowStyles.RegisterTaskbarCreatedMessage();
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Explorer restarted: the taskbar window is new, so re-anchor the overlay.
        if (_taskbarCreatedMessage != 0 && msg == (int)_taskbarCreatedMessage)
        {
            Dispatcher.BeginInvoke(() => _positioner.Reposition(this));
        }

        return IntPtr.Zero;
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_positioner.IsFloating)
        {
            DragMove();
            _positioner.SaveFloatingPosition(this);
        }
        else
        {
            _openSettings();
        }
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => _requestRefresh();

    private void OnSettingsClick(object sender, RoutedEventArgs e) => _openSettings();

    private void OnExitClick(object sender, RoutedEventArgs e)
        => System.Windows.Application.Current.Shutdown();
}

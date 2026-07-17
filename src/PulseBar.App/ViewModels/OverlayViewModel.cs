using System.ComponentModel;
using System.Windows.Threading;
using PulseBar.Core.Configuration;
using PulseBar.Core.Interfaces;
using PulseBar.Core.Localization;
using PulseBar.Core.Models;
using PulseBar.Core.Services;

namespace PulseBar.App.ViewModels;

public sealed class OverlayViewModel : INotifyPropertyChanged
{
    private readonly IConfigurationService _config;
    private readonly ILocalizationService _loc;
    private readonly Dispatcher _dispatcher;

    private string _systemLine = "…";
    private string _providerLine = "";

    private IReadOnlyList<UsageSnapshot> _snapshots = [];

    public OverlayViewModel(
        ISystemMetricsSource metrics,
        IConfigurationService config,
        ILocalizationService loc,
        Services.ProviderManager providers)
    {
        _config = config;
        _loc = loc;
        _dispatcher = Dispatcher.CurrentDispatcher;

        _providerLine = loc["Common_NotConnected"];
        metrics.MetricsUpdated += OnMetricsUpdated;
        providers.SnapshotsUpdated += (_, snapshots) => _dispatcher.BeginInvoke(() =>
        {
            _snapshots = snapshots;
            ProviderLine = CompactBarFormatter.ProviderLine(_snapshots, _loc.T);
        });
        config.ConfigChanged += (_, _) => _dispatcher.BeginInvoke(RaiseAppearanceChanged);
        loc.PropertyChanged += (_, _) => _dispatcher.BeginInvoke(() =>
        {
            ProviderLine = CompactBarFormatter.ProviderLine(_snapshots, _loc.T);
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string SystemLine
    {
        get => _systemLine;
        private set
        {
            if (_systemLine != value)
            {
                _systemLine = value;
                Raise(nameof(SystemLine));
            }
        }
    }

    /// <summary>AI provider summary; placeholder until providers are wired (Phase 4/5).</summary>
    public string ProviderLine
    {
        get => _providerLine;
        private set
        {
            if (_providerLine != value)
            {
                _providerLine = value;
                Raise(nameof(ProviderLine));
            }
        }
    }

    public bool ShowProviderLine => _config.Current.Appearance.Layout == BarLayout.TwoLine;

    public string FontFamily => _config.Current.Appearance.FontFamily;

    public double FontSize => _config.Current.Appearance.FontSize;

    public double BarOpacity => _config.Current.Appearance.Opacity;

    private void OnMetricsUpdated(object? sender, SystemMetrics metrics)
    {
        _dispatcher.BeginInvoke(() =>
        {
            var appearance = _config.Current.Appearance;
            SystemLine = CompactBarFormatter.SystemLine(metrics, _config.Current.Metrics, appearance.Layout);
        });
    }

    private void RaiseAppearanceChanged()
    {
        Raise(nameof(ShowProviderLine));
        Raise(nameof(FontFamily));
        Raise(nameof(FontSize));
        Raise(nameof(BarOpacity));
    }

    private void Raise(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

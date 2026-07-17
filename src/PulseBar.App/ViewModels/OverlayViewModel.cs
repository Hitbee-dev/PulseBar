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

    private IReadOnlyList<BarSegment> _systemSegments = [new BarSegment("…", BarSegmentKind.Label)];
    private IReadOnlyList<BarSegment> _providerSegments = [];

    private readonly Services.FableUsageService? _fable;
    private IReadOnlyList<UsageSnapshot> _snapshots = [];
    private string _tooltipText = "";

    public OverlayViewModel(
        ISystemMetricsSource metrics,
        IConfigurationService config,
        ILocalizationService loc,
        Services.ProviderManager providers,
        Services.FableUsageService? fable = null)
    {
        _config = config;
        _loc = loc;
        _fable = fable;
        _dispatcher = Dispatcher.CurrentDispatcher;

        _providerSegments = [new BarSegment(loc["Common_NotConnected"], BarSegmentKind.Stale)];
        metrics.MetricsUpdated += OnMetricsUpdated;
        providers.SnapshotsUpdated += (_, snapshots) => _dispatcher.BeginInvoke(() =>
        {
            _snapshots = snapshots;
            RefreshProviderTexts();
        });
        if (fable is not null)
        {
            fable.Updated += (_, _) => _dispatcher.BeginInvoke(RefreshProviderTexts);
        }

        config.ConfigChanged += (_, _) => _dispatcher.BeginInvoke(RaiseAppearanceChanged);
        loc.PropertyChanged += (_, _) => _dispatcher.BeginInvoke(RefreshProviderTexts);
    }

    private void RefreshProviderTexts()
    {
        ProviderSegments = CompactBarFormatter.ProviderSegments(_snapshots, _loc.T, _config.Current.Thresholds);
        TooltipText = BuildTooltip();
    }

    public string TooltipText
    {
        get => _tooltipText;
        private set
        {
            if (_tooltipText != value)
            {
                _tooltipText = value;
                Raise(nameof(TooltipText));
            }
        }
    }

    private string BuildTooltip()
    {
        var lines = new List<string>();

        foreach (var snapshot in _snapshots)
        {
            var name = snapshot.ProviderId switch
            {
                "codex" => "Codex",
                "claude" => "Claude",
                _ => snapshot.ProviderId,
            };

            if (snapshot.Freshness == DataFreshness.AuthenticationRequired)
            {
                lines.Add($"{name}: {_loc["Freshness_AuthenticationRequired"]}");
                continue;
            }

            foreach (var window in snapshot.Windows)
            {
                var reset = window.ResetsAt is { } resetsAt
                    ? " · " + _loc.T("Tooltip_Resets", resetsAt.ToLocalTime().ToString("MM-dd HH:mm"))
                    : "";
                var stale = window.Freshness == DataFreshness.Stale ? $" ({_loc["Freshness_Stale"]})" : "";
                lines.Add($"{name} {window.DisplayName}: {UnitFormatter.Percent(window.UsedPercent)}%{reset}{stale}");
            }

            if (snapshot.Plan is not null)
            {
                lines.Add($"{name} plan: {snapshot.Plan}");
            }
        }

        if (_fable is not null)
        {
            var usage = _fable.Current;
            if (usage.Today.TotalTokens > 0 || usage.SevenDays.TotalTokens > 0)
            {
                lines.Add(_loc["Claude_FableTokens"]);
                lines.Add(
                    $"  {_loc["Tooltip_Today"]} {UnitFormatter.CountCompact(usage.Today.TotalTokens)}" +
                    $" · {_loc["Tooltip_SevenDays"]} {UnitFormatter.CountCompact(usage.SevenDays.TotalTokens)}");
            }
        }

        return lines.Count == 0 ? _loc["Common_NotConnected"] : string.Join(Environment.NewLine, lines);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<BarSegment> SystemSegments
    {
        get => _systemSegments;
        private set
        {
            _systemSegments = value;
            Raise(nameof(SystemSegments));
        }
    }

    /// <summary>AI provider summary segments ("연동 필요" placeholder until providers report).</summary>
    public IReadOnlyList<BarSegment> ProviderSegments
    {
        get => _providerSegments;
        private set
        {
            _providerSegments = value;
            Raise(nameof(ProviderSegments));
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
            SystemSegments = CompactBarFormatter.SystemSegments(
                metrics,
                _config.Current.Metrics,
                _config.Current.Appearance.Layout,
                _config.Current.Thresholds);
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

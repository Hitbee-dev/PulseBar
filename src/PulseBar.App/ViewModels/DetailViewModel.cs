using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Media;
using System.Windows.Threading;
using PulseBar.Core.Interfaces;
using PulseBar.Core.Localization;
using PulseBar.Core.Models;
using PulseBar.Core.Services;

namespace PulseBar.App.ViewModels;

public sealed record ProviderCard(string Title, string Status, IReadOnlyList<string> Lines);

public sealed class DetailViewModel : INotifyPropertyChanged
{
    public const double GraphWidth = 336;
    public const double GraphHeight = 60;

    private readonly ISystemMetricsSource _metrics;
    private readonly Services.ProviderManager _providers;
    private readonly Services.FableUsageService _fable;
    private readonly Dispatcher _dispatcher;

    private string _cpuText = "";
    private string _memoryText = "";
    private string _gpuText = "";
    private string _vramText = "";
    private string _diskText = "";
    private string _networkText = "";
    private PointCollection _cpuPoints = [];

    public DetailViewModel(
        ISystemMetricsSource metrics,
        Services.ProviderManager providers,
        Services.FableUsageService fable,
        ILocalizationService loc)
    {
        _metrics = metrics;
        _providers = providers;
        _fable = fable;
        Loc = loc;
        _dispatcher = Dispatcher.CurrentDispatcher;

        providers.SnapshotsUpdated += (_, _) => _dispatcher.BeginInvoke(RebuildCards);
        fable.Updated += (_, _) => _dispatcher.BeginInvoke(RebuildCards);
        loc.PropertyChanged += (_, _) => _dispatcher.BeginInvoke(() =>
        {
            RebuildCards();
            Tick();
        });

        RebuildCards();
        Tick();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ILocalizationService Loc { get; }

    public ObservableCollection<ProviderCard> Cards { get; } = [];

    public string CpuText { get => _cpuText; private set => Set(ref _cpuText, value, nameof(CpuText)); }
    public string MemoryText { get => _memoryText; private set => Set(ref _memoryText, value, nameof(MemoryText)); }
    public string GpuText { get => _gpuText; private set => Set(ref _gpuText, value, nameof(GpuText)); }
    public string VramText { get => _vramText; private set => Set(ref _vramText, value, nameof(VramText)); }
    public string DiskText { get => _diskText; private set => Set(ref _diskText, value, nameof(DiskText)); }
    public string NetworkText { get => _networkText; private set => Set(ref _networkText, value, nameof(NetworkText)); }

    public PointCollection CpuPoints
    {
        get => _cpuPoints;
        private set
        {
            _cpuPoints = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CpuPoints)));
        }
    }

    /// <summary>Called by the window's 1 s timer while the popup is open (UI thread).</summary>
    public void Tick()
    {
        var m = _metrics.Latest;
        if (m is null)
        {
            return;
        }

        CpuText = $"CPU  {UnitFormatter.Percent(m.CpuPercent)}%";
        MemoryText =
            $"RAM  {UnitFormatter.Percent(m.MemoryUsedPercent)}%  " +
            $"({UnitFormatter.BytesCompact(m.MemoryUsedBytes)} / {UnitFormatter.BytesCompact(m.MemoryTotalBytes)})";
        GpuText = m.GpuAdapterName is null
            ? $"GPU  {UnitFormatter.Percent(m.GpuPercent)}%"
            : $"GPU  {UnitFormatter.Percent(m.GpuPercent)}%  ({m.GpuAdapterName})";
        VramText = m.VramTotalBytes is { } total
            ? $"VRAM  {UnitFormatter.BytesCompact(m.VramUsedBytes ?? 0)} / {UnitFormatter.BytesCompact(total)}"
            : $"VRAM  {UnitFormatter.BytesCompact(m.VramUsedBytes ?? 0)}";
        DiskText =
            $"Disk  {UnitFormatter.Percent(m.DiskActivePercent)}%  " +
            $"R {UnitFormatter.BytesPerSecond(m.DiskReadBytesPerSec ?? 0)} · " +
            $"W {UnitFormatter.BytesPerSecond(m.DiskWriteBytesPerSec ?? 0)}";
        NetworkText =
            $"Net  ↓ {UnitFormatter.BytesPerSecond(m.NetworkReceivedBytesPerSec ?? 0)} · " +
            $"↑ {UnitFormatter.BytesPerSecond(m.NetworkSentBytesPerSec ?? 0)}";

        CpuPoints = BuildCpuPoints(_metrics.GetHistory());
    }

    /// <summary>Maps the last 60 CPU samples onto the graph canvas (left = oldest).</summary>
    public static PointCollection BuildCpuPoints(IReadOnlyList<SystemMetrics> history)
    {
        var samples = history
            .Where(h => h.CpuPercent is not null)
            .TakeLast(60)
            .Select(h => h.CpuPercent!.Value)
            .ToList();

        var points = new PointCollection();
        if (samples.Count < 2)
        {
            return points;
        }

        var stepX = GraphWidth / 59.0;
        var startIndex = 60 - samples.Count;
        for (var i = 0; i < samples.Count; i++)
        {
            var x = (startIndex + i) * stepX;
            var y = GraphHeight - (samples[i] / 100.0 * GraphHeight);
            points.Add(new System.Windows.Point(x, y));
        }

        return points;
    }

    private void RebuildCards()
    {
        Cards.Clear();

        foreach (var snapshot in _providers.CurrentSnapshots)
        {
            var title = snapshot.ProviderId switch
            {
                "codex" => "Codex",
                "claude" => "Claude",
                _ => snapshot.ProviderId,
            };

            var status = Loc[$"Freshness_{snapshot.Freshness}"] + " · " + FormatAge(snapshot.CollectedAt);

            var lines = new List<string>();
            foreach (var window in snapshot.Windows)
            {
                var reset = window.ResetsAt is { } resetsAt
                    ? " · " + Loc.T("Tooltip_Resets", resetsAt.ToLocalTime().ToString("MM-dd HH:mm"))
                    : "";
                lines.Add($"{window.DisplayName}: {UnitFormatter.Percent(window.UsedPercent)}%{reset}");
            }

            if (snapshot.AccountLabel is not null)
            {
                lines.Add($"{Loc["Detail_Account"]}: {snapshot.AccountLabel}");
            }

            if (snapshot.Plan is not null)
            {
                lines.Add($"{Loc["Detail_Plan"]}: {snapshot.Plan}");
            }

            if (snapshot.CreditBalance is { } credits and > 0)
            {
                lines.Add($"{Loc["Detail_Credits"]}: {credits}");
            }

            if (snapshot.ModelUsageToday.TryGetValue("codex", out var codexToday))
            {
                lines.Add(
                    $"{Loc["Tooltip_Today"]} {UnitFormatter.CountCompact(codexToday.TotalTokens)} tokens · " +
                    $"{Loc["Tooltip_SevenDays"]} {UnitFormatter.CountCompact(
                        snapshot.ModelUsageSevenDays.GetValueOrDefault("codex", TokenUsage.Zero).TotalTokens)}");
            }

            if (snapshot.ProviderId == "claude")
            {
                var fable = _fable.Current;
                if (fable.Today.TotalTokens > 0 || fable.SevenDays.TotalTokens > 0)
                {
                    lines.Add(Loc["Claude_FableTokens"]);
                    lines.Add(
                        $"  {Loc["Tooltip_Today"]} {UnitFormatter.CountCompact(fable.Today.TotalTokens)} · " +
                        $"{Loc["Tooltip_SevenDays"]} {UnitFormatter.CountCompact(fable.SevenDays.TotalTokens)}");
                }
            }

            if (lines.Count == 0)
            {
                lines.Add(Loc["Common_NotConnected"]);
            }

            Cards.Add(new ProviderCard(title, status, lines));
        }
    }

    private string FormatAge(DateTimeOffset collectedAt)
    {
        var age = DateTimeOffset.Now - collectedAt;
        return age switch
        {
            { TotalMinutes: < 1 } => Loc.T("Common_LastUpdated", Loc["Common_JustNow"]),
            { TotalHours: < 1 } => Loc.T("Common_LastUpdated", Loc.T("Common_MinutesAgo", (int)age.TotalMinutes)),
            _ => Loc.T("Common_LastUpdated", Loc.T("Common_HoursAgo", (int)age.TotalHours)),
        };
    }

    private void Set(ref string field, string value, string name)
    {
        if (field != value)
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}

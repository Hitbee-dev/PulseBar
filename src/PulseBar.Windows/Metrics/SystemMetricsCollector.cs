using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseBar.Core.Configuration;
using PulseBar.Core.Interfaces;
using PulseBar.Core.Models;
using PulseBar.Core.Services;
using PulseBar.Windows.Interop;

namespace PulseBar.Windows.Metrics;

/// <summary>
/// Background loop that samples all system metrics every poll interval (default 1 s)
/// off the UI thread. A failing sampler yields null values; it never kills the loop.
/// Keeps the last 5 minutes of samples in memory.
/// </summary>
public sealed class SystemMetricsCollector : BackgroundService, ISystemMetricsSource
{
    private const int HistoryCapacity = 300; // 5 minutes at 1 s

    private readonly IConfigurationService _config;
    private readonly ILogger<SystemMetricsCollector> _logger;
    private readonly RingBuffer<SystemMetrics> _history = new(HistoryCapacity);
    private readonly CpuSampler _cpu = new();

    private PdhQuery? _query;
    private PdhCounter? _diskTime;
    private PdhCounter? _diskRead;
    private PdhCounter? _diskWrite;
    private PdhCounter? _netReceived;
    private PdhCounter? _netSent;
    private PdhCounter? _gpuEngine;
    private PdhCounter? _gpuMemory;

    private GpuAdapterInfo? _primaryAdapter;
    private DateTimeOffset _lastTick = DateTimeOffset.MinValue;

    public SystemMetricsCollector(IConfigurationService config, ILogger<SystemMetricsCollector> logger)
    {
        _config = config;
        _logger = logger;
    }

    public event EventHandler<SystemMetrics>? MetricsUpdated;

    public SystemMetrics? Latest { get; private set; }

    public IReadOnlyList<SystemMetrics> GetHistory() => _history.ToArray();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The whole loop runs on a thread-pool thread; PDH never touches the UI thread.
        await Task.Yield();

        InitializeCounters();
        SelectPrimaryAdapter();

        var interval = TimeSpan.FromMilliseconds(Math.Max(250, _config.Current.Metrics.PollIntervalMs));
        using var timer = new PeriodicTimer(interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    Tick();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Metrics tick failed.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    public override void Dispose()
    {
        _query?.Dispose();
        base.Dispose();
    }

    private void InitializeCounters()
    {
        try
        {
            _query = new PdhQuery();
            _diskTime = _query.TryAddCounter(@"\PhysicalDisk(_Total)\% Disk Time");
            _diskRead = _query.TryAddCounter(@"\PhysicalDisk(_Total)\Disk Read Bytes/sec");
            _diskWrite = _query.TryAddCounter(@"\PhysicalDisk(_Total)\Disk Write Bytes/sec");
            _netReceived = _query.TryAddCounter(@"\Network Interface(*)\Bytes Received/sec");
            _netSent = _query.TryAddCounter(@"\Network Interface(*)\Bytes Sent/sec");
            _gpuEngine = _query.TryAddCounter(@"\GPU Engine(*)\Utilization Percentage");
            _gpuMemory = _query.TryAddCounter(@"\GPU Adapter Memory(*)\Dedicated Usage");

            // Rate counters need a baseline sample; its values are discarded.
            _query.Collect();

            LogMissing(_diskTime, @"\PhysicalDisk(_Total)\% Disk Time");
            LogMissing(_netReceived, @"\Network Interface(*)\Bytes Received/sec");
            LogMissing(_gpuEngine, @"\GPU Engine(*)\Utilization Percentage");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PDH initialization failed; PDH-based metrics will be unavailable.");
            _query?.Dispose();
            _query = null;
        }
    }

    private void LogMissing(PdhCounter? counter, string path)
    {
        if (counter is null)
        {
            _logger.LogWarning("Performance counter not available on this machine: {Path}", path);
        }
    }

    private void SelectPrimaryAdapter()
    {
        try
        {
            var adapters = DxgiAdapters.Enumerate()
                .Where(a => !a.IsSoftware)
                .OrderByDescending(a => a.DedicatedVideoMemoryBytes)
                .ToList();
            _primaryAdapter = adapters.FirstOrDefault();

            if (_primaryAdapter is not null)
            {
                _logger.LogInformation(
                    "Primary GPU adapter: {Name} ({Vram} bytes dedicated, {Luid})",
                    _primaryAdapter.Description,
                    _primaryAdapter.DedicatedVideoMemoryBytes,
                    _primaryAdapter.LuidKey);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DXGI adapter enumeration failed; VRAM totals unavailable.");
        }
    }

    private void Tick()
    {
        var now = DateTimeOffset.Now;

        // After sleep/resume (or any long stall) the CPU delta covers the whole gap;
        // drop the baseline so the next sample starts fresh.
        if (_lastTick != DateTimeOffset.MinValue && now - _lastTick > TimeSpan.FromSeconds(5))
        {
            _cpu.Reset();
        }

        _lastTick = now;

        var cpuPercent = _cpu.Sample();
        var memory = MemorySampler.Sample();

        double? diskTime = null, diskRead = null, diskWrite = null;
        double? netReceived = null, netSent = null;
        double? gpuPercent = null;
        ulong? vramUsed = null;

        if (_query is not null && _query.Collect())
        {
            diskTime = _diskTime?.ReadDouble() is { } dt ? Math.Clamp(dt, 0.0, 100.0) : null;
            diskRead = _diskRead?.ReadDouble();
            diskWrite = _diskWrite?.ReadDouble();

            var metrics = _config.Current.Metrics;
            if (_netReceived is not null)
            {
                netReceived = NetworkAdapterFilter.Sum(
                    _netReceived.ReadArray(), metrics.IncludedAdapters, metrics.ExcludedAdapters);
            }

            if (_netSent is not null)
            {
                netSent = NetworkAdapterFilter.Sum(
                    _netSent.ReadArray(), metrics.IncludedAdapters, metrics.ExcludedAdapters);
            }

            if (_gpuEngine is not null)
            {
                var utilization = GpuMetricsParser.AggregateUtilization(_gpuEngine.ReadArray());
                gpuPercent = SelectAdapterValue(utilization);
            }

            if (_gpuMemory is not null)
            {
                var memoryPerAdapter = GpuMetricsParser.AggregateDedicatedMemory(_gpuMemory.ReadArray());
                var used = SelectAdapterValue(memoryPerAdapter);
                vramUsed = used is null ? null : (ulong)Math.Max(0, used.Value);
            }
        }

        var snapshot = new SystemMetrics(
            CpuPercent: cpuPercent,
            MemoryUsedPercent: memory?.UsedPercent,
            MemoryTotalBytes: memory?.TotalBytes ?? 0,
            MemoryUsedBytes: memory?.UsedBytes ?? 0,
            GpuPercent: gpuPercent,
            GpuAdapterName: _primaryAdapter?.Description,
            VramUsedBytes: vramUsed,
            VramTotalBytes: _primaryAdapter?.DedicatedVideoMemoryBytes is > 0 and var total ? total : null,
            DiskActivePercent: diskTime,
            DiskReadBytesPerSec: diskRead,
            DiskWriteBytesPerSec: diskWrite,
            NetworkReceivedBytesPerSec: netReceived,
            NetworkSentBytesPerSec: netSent,
            CollectedAt: now);

        Latest = snapshot;
        _history.Add(snapshot);
        MetricsUpdated?.Invoke(this, snapshot);
    }

    /// <summary>Value for the primary adapter; falls back to the busiest adapter when unknown.</summary>
    private double? SelectAdapterValue(IReadOnlyDictionary<string, double> perAdapter)
    {
        if (perAdapter.Count == 0)
        {
            return null;
        }

        if (_primaryAdapter is not null
            && perAdapter.TryGetValue(_primaryAdapter.LuidKey, out var primary))
        {
            return primary;
        }

        return perAdapter.Values.Max();
    }
}

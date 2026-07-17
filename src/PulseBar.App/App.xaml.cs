using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseBar.App.Services;
using PulseBar.App.ViewModels;
using PulseBar.App.Views;
using PulseBar.Core.Configuration;
using PulseBar.Core.Interfaces;
using PulseBar.Core.Localization;
using PulseBar.Core.Logging;
using PulseBar.Storage.Sqlite;
using PulseBar.Windows.Metrics;
using PulseBar.Windows.Startup;

namespace PulseBar.App;

public partial class App : Application
{
    private const string MutexName = @"Local\PulseBar.SingleInstance";

    private IHost? _host;
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(initiallyOwned: true, MutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            Shutdown();
            return;
        }

        var paths = new AppPaths();
        paths.EnsureCreated();

        _host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddDebug();
                logging.AddProvider(new FileLoggerProvider(paths.LogsDir));
            })
            .ConfigureServices(services =>
            {
                services.AddSingleton<IAppPaths>(paths);
                services.AddSingleton<IConfigurationService, ConfigurationService>();
                services.AddSingleton<ILocalizationService>(sp =>
                {
                    var config = sp.GetRequiredService<IConfigurationService>();
                    return new LocalizationService(config.Current.Appearance.Language);
                });
                services.AddSingleton<IStartupManager, StartupManager>();
                services.AddSingleton<TrayIconService>();
                services.AddSingleton<OverlayPositioner>();
                services.AddSingleton<SystemMetricsCollector>();
                services.AddSingleton<ISystemMetricsSource>(sp => sp.GetRequiredService<SystemMetricsCollector>());
                services.AddHostedService(sp => sp.GetRequiredService<SystemMetricsCollector>());
                services.AddSingleton<ProviderManager>();
                services.AddHostedService(sp => sp.GetRequiredService<ProviderManager>());
                services.AddSingleton<ITokenUsageRepository>(_ =>
                {
                    var repository = new SqliteTokenUsageRepository(paths.DatabaseFile);
                    repository.Initialize();
                    return repository;
                });
                services.AddSingleton<OtelReceiverService>();
                services.AddHostedService(sp => sp.GetRequiredService<OtelReceiverService>());
                services.AddSingleton<OtelQueueIngestService>();
                services.AddHostedService(sp => sp.GetRequiredService<OtelQueueIngestService>());
                services.AddSingleton<FableUsageService>();
                services.AddSingleton<UserActions>();
                services.AddSingleton<WslOtelHelperService>();
                services.AddHostedService(sp => sp.GetRequiredService<WslOtelHelperService>());
            })
            .Build();

        var logger = _host.Services.GetRequiredService<ILogger<App>>();

        DispatcherUnhandledException += (_, args) =>
        {
            logger.LogError(args.Exception, "Unhandled UI exception.");
            args.Handled = true;
        };

        // Load config before hosted services start (the metrics collector reads it on start).
        var config = _host.Services.GetRequiredService<IConfigurationService>();
        config.Load();
        _host.Services.GetRequiredService<ILocalizationService>().SetLanguage(config.Current.Appearance.Language);
        FileLoggerProvider.Cleanup(
            paths.LogsDir, config.Current.Storage.LogRetentionDays, maxTotalBytes: 50 * 1024 * 1024);

        _host.Start();

        var tray = _host.Services.GetRequiredService<TrayIconService>();
        tray.Initialize();

        var providers = _host.Services.GetRequiredService<ProviderManager>();
        tray.RefreshRequested += (_, _) => providers.RefreshAll();

        var loc = _host.Services.GetRequiredService<ILocalizationService>();
        var receiver = _host.Services.GetRequiredService<OtelReceiverService>();
        var queueIngest = _host.Services.GetRequiredService<OtelQueueIngestService>();
        var fable = _host.Services.GetRequiredService<FableUsageService>();
        receiver.EventsIngested += (_, _) => fable.Refresh();
        queueIngest.EventsIngested += (_, _) => fable.Refresh();
        Task.Run(fable.Refresh);

        var repository = _host.Services.GetRequiredService<ITokenUsageRepository>();
        Task.Run(() => repository.PruneOlderThan(
            DateTimeOffset.Now.AddDays(-config.Current.Storage.TokenEventRetentionDays)));

        var metricsSource = _host.Services.GetRequiredService<ISystemMetricsSource>();
        var actions = _host.Services.GetRequiredService<UserActions>();

        DetailWindow? detailWindow = null;
        void OpenDetail()
        {
            if (detailWindow is { IsLoaded: true })
            {
                detailWindow.Activate();
                return;
            }

            detailWindow = new DetailWindow(
                new DetailViewModel(metricsSource, providers, fable, loc), actions);
            detailWindow.Closed += (_, _) => detailWindow = null;
            detailWindow.Show();
        }

        var overlayViewModel = new OverlayViewModel(metricsSource, config, loc, providers, fable);
        var overlay = new OverlayWindow(
            overlayViewModel,
            loc,
            _host.Services.GetRequiredService<OverlayPositioner>(),
            OpenDetail,
            tray.ShowSettingsWindow,
            tray.RequestRefresh);
        overlay.Show();

        // Diagnostics: `PulseBar.exe --show-detail[-ai]` opens the detail popup immediately.
        if (e.Args.Any(a => a.StartsWith("--show-detail")))
        {
            OpenDetail();
            if (e.Args.Contains("--show-detail-ai"))
            {
                detailWindow?.SelectAiTab();
            }
        }

        logger.LogInformation("PulseBar started.");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            _host.Services.GetRequiredService<TrayIconService>().Dispose();
            _host.StopAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            _host.Dispose();
        }

        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}

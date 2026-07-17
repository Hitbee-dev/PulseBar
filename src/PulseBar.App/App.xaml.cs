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

        _host.Start();

        var tray = _host.Services.GetRequiredService<TrayIconService>();
        tray.Initialize();

        var providers = _host.Services.GetRequiredService<ProviderManager>();
        tray.RefreshRequested += (_, _) => providers.RefreshAll();

        var overlayViewModel = new OverlayViewModel(
            _host.Services.GetRequiredService<ISystemMetricsSource>(),
            config,
            _host.Services.GetRequiredService<ILocalizationService>(),
            providers);
        var overlay = new OverlayWindow(
            overlayViewModel,
            _host.Services.GetRequiredService<ILocalizationService>(),
            _host.Services.GetRequiredService<OverlayPositioner>(),
            tray.ShowSettingsWindow,
            tray.RequestRefresh);
        overlay.Show();

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

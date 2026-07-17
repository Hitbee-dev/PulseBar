using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseBar.App.Services;
using PulseBar.Core.Configuration;
using PulseBar.Core.Localization;
using PulseBar.Core.Logging;
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
            })
            .Build();

        var logger = _host.Services.GetRequiredService<ILogger<App>>();

        DispatcherUnhandledException += (_, args) =>
        {
            logger.LogError(args.Exception, "Unhandled UI exception.");
            args.Handled = true;
        };

        _host.Start();

        _host.Services.GetRequiredService<IConfigurationService>().Load();
        _host.Services
            .GetRequiredService<ILocalizationService>()
            .SetLanguage(_host.Services.GetRequiredService<IConfigurationService>().Current.Appearance.Language);
        _host.Services.GetRequiredService<TrayIconService>().Initialize();

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

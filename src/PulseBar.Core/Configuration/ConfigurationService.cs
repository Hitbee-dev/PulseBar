using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace PulseBar.Core.Configuration;

public interface IConfigurationService
{
    AppConfig Current { get; }
    void Load();
    void Save();
    event EventHandler<AppConfig>? ConfigChanged;
    void Update(Action<AppConfig> mutate);
}

/// <summary>
/// Loads/saves config.json under %LOCALAPPDATA%\PulseBar.
/// A corrupt config is backed up (never silently deleted) and replaced by defaults.
/// Writes are atomic: temp file + rename.
/// </summary>
public sealed class ConfigurationService : IConfigurationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly IAppPaths _paths;
    private readonly ILogger<ConfigurationService> _logger;
    private readonly object _gate = new();

    public ConfigurationService(IAppPaths paths, ILogger<ConfigurationService> logger)
    {
        _paths = paths;
        _logger = logger;
        Current = new AppConfig();
    }

    public AppConfig Current { get; private set; }

    public event EventHandler<AppConfig>? ConfigChanged;

    public void Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_paths.ConfigFile))
            {
                _logger.LogInformation("No config file found at {Path}; using defaults.", _paths.ConfigFile);
                Current = new AppConfig();
                return;
            }

            try
            {
                var json = File.ReadAllText(_paths.ConfigFile);
                Current = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                var backup = BackupCorruptConfig();
                _logger.LogWarning(ex,
                    "Config file could not be parsed; backed up to {Backup} and reverted to defaults.", backup);
                Current = new AppConfig();
            }
        }
    }

    public void Save()
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_paths.ConfigFile)!);
            var json = JsonSerializer.Serialize(Current, JsonOptions);
            var tmp = _paths.ConfigFile + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _paths.ConfigFile, overwrite: true);
        }
    }

    public void Update(Action<AppConfig> mutate)
    {
        lock (_gate)
        {
            mutate(Current);
            Save();
        }

        ConfigChanged?.Invoke(this, Current);
    }

    private string? BackupCorruptConfig()
    {
        try
        {
            Directory.CreateDirectory(_paths.BackupsDir);
            var backup = Path.Combine(
                _paths.BackupsDir,
                $"config.corrupt.{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
            File.Copy(_paths.ConfigFile, backup, overwrite: true);
            return backup;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to back up corrupt config file.");
            return null;
        }
    }
}

using Microsoft.Win32;

namespace PulseBar.Windows.Startup;

public interface IStartupManager
{
    bool IsRegistered();
    void Register();
    void Unregister();
}

/// <summary>
/// Registers PulseBar under HKCU\...\Run (per-user, no admin rights).
/// </summary>
public sealed class StartupManager : IStartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PulseBar";

    private readonly string _executablePath;

    public StartupManager()
        : this(Environment.ProcessPath ?? throw new InvalidOperationException("Process path unavailable."))
    {
    }

    public StartupManager(string executablePath)
    {
        _executablePath = executablePath;
    }

    public static string BuildCommand(string executablePath) => $"\"{executablePath}\"";

    public bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string;
    }

    public void Register()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        key.SetValue(ValueName, BuildCommand(_executablePath));
    }

    public void Unregister()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}

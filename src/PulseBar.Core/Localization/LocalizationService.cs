using System.ComponentModel;
using System.Globalization;

namespace PulseBar.Core.Localization;

public interface ILocalizationService : INotifyPropertyChanged
{
    /// <summary>Indexer for XAML bindings: {Binding [Tray_Exit], Source=...}.</summary>
    string this[string key] { get; }

    string CurrentLanguage { get; }

    /// <summary>Returns the localized string, or the key itself when missing.</summary>
    string T(string key);

    /// <summary>Format-style lookup: T("Common_MinutesAgo", 3).</summary>
    string T(string key, params object[] args);

    /// <summary>Accepts "ko-KR"/"en-US" (or "ko"/"en"); unknown values fall back to en-US.</summary>
    void SetLanguage(string cultureName);
}

public sealed class LocalizationService : ILocalizationService
{
    public const string DefaultLanguage = "ko-KR";

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Tables =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["en-US"] = LocalizedStrings.En,
            ["ko-KR"] = LocalizedStrings.Ko,
        };

    private IReadOnlyDictionary<string, string> _table = LocalizedStrings.Ko;

    public LocalizationService(string? language = null)
    {
        SetLanguage(language ?? DefaultLanguage);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string CurrentLanguage { get; private set; } = DefaultLanguage;

    public string this[string key] => T(key);

    public string T(string key)
        => _table.TryGetValue(key, out var value) ? value : key;

    public string T(string key, params object[] args)
        => _table.TryGetValue(key, out var value)
            ? string.Format(CultureInfo.CurrentCulture, value, args)
            : key;

    public void SetLanguage(string cultureName)
    {
        var normalized = Normalize(cultureName);
        CurrentLanguage = normalized;
        _table = Tables[normalized];

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    private static string Normalize(string cultureName)
        => !string.IsNullOrWhiteSpace(cultureName)
           && cultureName.StartsWith("ko", StringComparison.OrdinalIgnoreCase)
            ? "ko-KR"
            : "en-US";
}

using System.ComponentModel;
using System.Runtime.CompilerServices;
using PulseBar.Core.Configuration;
using PulseBar.Core.Localization;

namespace PulseBar.App.ViewModels;

public sealed record LanguageOption(string Code, string DisplayName);

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly IConfigurationService _config;
    private string _selectedLanguage;
    private BarLayout _selectedLayout;

    public SettingsViewModel(ILocalizationService loc, IConfigurationService config)
    {
        Loc = loc;
        _config = config;
        _selectedLanguage = config.Current.Appearance.Language;
        _selectedLayout = config.Current.Appearance.Layout;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ILocalizationService Loc { get; }

    public IReadOnlyList<LanguageOption> Languages { get; } =
    [
        new("ko-KR", "한국어"),
        new("en-US", "English"),
    ];

    public IReadOnlyList<BarLayout> Layouts { get; } =
        [BarLayout.OneLine, BarLayout.TwoLine, BarLayout.UltraCompact];

    public string SelectedLanguage
    {
        get => _selectedLanguage;
        set => Set(ref _selectedLanguage, value);
    }

    public BarLayout SelectedLayout
    {
        get => _selectedLayout;
        set => Set(ref _selectedLayout, value);
    }

    public void Save()
    {
        _config.Update(c =>
        {
            c.Appearance.Language = SelectedLanguage;
            c.Appearance.Layout = SelectedLayout;
        });
        Loc.SetLanguage(SelectedLanguage);
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}

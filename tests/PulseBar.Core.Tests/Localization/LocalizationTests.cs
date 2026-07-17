using PulseBar.Core.Localization;

namespace PulseBar.Core.Tests.Localization;

public class LocalizationTests
{
    [Fact]
    public void KoreanAndEnglishTables_HaveIdenticalKeySets()
    {
        var enKeys = LocalizedStrings.En.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
        var koKeys = LocalizedStrings.Ko.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();

        Assert.Equal(enKeys, koKeys);
    }

    [Fact]
    public void NoTableContainsEmptyValues()
    {
        Assert.All(LocalizedStrings.En.Values, v => Assert.False(string.IsNullOrWhiteSpace(v)));
        Assert.All(LocalizedStrings.Ko.Values, v => Assert.False(string.IsNullOrWhiteSpace(v)));
    }

    [Fact]
    public void DefaultLanguage_IsKorean()
    {
        var loc = new LocalizationService();

        Assert.Equal("ko-KR", loc.CurrentLanguage);
        Assert.Equal("종료", loc["Tray_Exit"]);
    }

    [Fact]
    public void SetLanguage_SwitchesToEnglish_AndRaisesIndexerChange()
    {
        var loc = new LocalizationService("ko-KR");
        var changed = new List<string?>();
        loc.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        loc.SetLanguage("en-US");

        Assert.Equal("en-US", loc.CurrentLanguage);
        Assert.Equal("Exit", loc["Tray_Exit"]);
        Assert.Contains("Item[]", changed);
    }

    [Theory]
    [InlineData("ko", "ko-KR")]
    [InlineData("ko-KR", "ko-KR")]
    [InlineData("en", "en-US")]
    [InlineData("en-GB", "en-US")]
    [InlineData("fr-FR", "en-US")]
    [InlineData("", "en-US")]
    public void SetLanguage_NormalizesCultureNames(string input, string expected)
    {
        var loc = new LocalizationService(input);

        Assert.Equal(expected, loc.CurrentLanguage);
    }

    [Fact]
    public void MissingKey_ReturnsKeyItself()
    {
        var loc = new LocalizationService();

        Assert.Equal("Nope_Missing", loc["Nope_Missing"]);
    }

    [Fact]
    public void FormatLookup_AppliesArguments()
    {
        var loc = new LocalizationService("en-US");

        Assert.Equal("3 min ago", loc.T("Common_MinutesAgo", 3));
    }
}

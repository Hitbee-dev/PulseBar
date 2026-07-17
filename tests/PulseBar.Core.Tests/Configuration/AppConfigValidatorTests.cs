using PulseBar.Core.Configuration;

namespace PulseBar.Core.Tests.Configuration;

public class AppConfigValidatorTests
{
    private static AppConfig ConfigWith(string? distro = null, string? executablePath = null)
        => new()
        {
            Providers =
            [
                new ProviderProfileConfig
                {
                    Id = "p",
                    ProviderId = "codex",
                    WslDistribution = distro,
                    ExecutablePath = executablePath,
                },
            ],
        };

    [Fact]
    public void DefaultConfig_IsValid()
    {
        Assert.True(AppConfigValidator.IsValid(new AppConfig()));
    }

    [Theory]
    [InlineData("Ubuntu-22.04")]
    [InlineData("debian_12")]
    [InlineData(null)]
    public void WslDistribution_NormalNames_AreValid(string? distro)
    {
        Assert.True(AppConfigValidator.IsValid(ConfigWith(distro: distro)));
    }

    [Theory]
    [InlineData("Ubuntu & calc.exe")]
    [InlineData("u\" -- pwned")]
    [InlineData("a|b")]
    [InlineData("")]
    public void WslDistribution_ShellMetacharacters_AreRejected(string distro)
    {
        Assert.False(AppConfigValidator.IsValid(ConfigWith(distro: distro)));
    }

    [Theory]
    [InlineData(@"C:\Program Files\codex\codex.exe")]
    [InlineData("/snap/bin/codex")]
    [InlineData(null)]
    public void ExecutablePath_NormalPaths_AreValid(string? path)
    {
        Assert.True(AppConfigValidator.IsValid(ConfigWith(executablePath: path)));
    }

    [Theory]
    [InlineData("codex & calc.exe")]
    [InlineData("codex\" | whoami")]
    [InlineData("a>b")]
    public void ExecutablePath_CmdMetacharacters_AreRejected(string path)
    {
        Assert.False(AppConfigValidator.IsValid(ConfigWith(executablePath: path)));
    }

    [Fact]
    public void Import_MaliciousConfig_IsRejectedByService()
    {
        var dir = Path.Combine(Path.GetTempPath(), "PulseBarTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(dir);
            var service = new ConfigurationService(
                paths, Microsoft.Extensions.Logging.Abstractions.NullLogger<ConfigurationService>.Instance);
            service.Load();

            var malicious = Path.Combine(dir, "import.json");
            Directory.CreateDirectory(dir);
            File.WriteAllText(malicious, """
                {"schemaVersion":1,"providers":[
                  {"id":"x","providerId":"claude","environment":"wsl",
                   "wslDistribution":"Ubuntu & calc.exe","enabled":true}]}
                """);

            Assert.False(service.ImportFrom(malicious));
            Assert.Empty(service.Current.Providers);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

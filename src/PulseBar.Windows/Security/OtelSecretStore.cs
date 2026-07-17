using System.Security.Cryptography;
using System.Text;

namespace PulseBar.Windows.Security;

/// <summary>
/// Bearer secret for the loopback OTLP receiver, protected at rest with
/// DPAPI (CurrentUser) per spec §12.1.
/// </summary>
public static class OtelSecretStore
{
    public static string GetOrCreate(string path)
    {
        if (File.Exists(path))
        {
            try
            {
                var unprotected = ProtectedData.Unprotect(
                    File.ReadAllBytes(path), optionalEntropy: null, DataProtectionScope.CurrentUser);
                var secret = Encoding.UTF8.GetString(unprotected);
                if (!string.IsNullOrWhiteSpace(secret))
                {
                    return secret;
                }
            }
            catch (CryptographicException)
            {
                // Unreadable (different user/machine): regenerate below.
            }
        }

        var fresh = GenerateSecret();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, ProtectedData.Protect(
            Encoding.UTF8.GetBytes(fresh), optionalEntropy: null, DataProtectionScope.CurrentUser));
        return fresh;
    }

    private static string GenerateSecret()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}

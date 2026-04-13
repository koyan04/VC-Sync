using System.Security.Cryptography;
using System.Text;
using System.IO;

namespace VCSyncBackupApp.Services;

public sealed class SecretStore
{
    private readonly string _secretFilePath;

    public SecretStore(string appDataPath)
    {
        _secretFilePath = Path.Combine(appDataPath, "passphrase.bin");
    }

    public async Task SavePassphraseAsync(string passphrase)
    {
        var plainBytes = Encoding.UTF8.GetBytes(passphrase);
        var protectedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(_secretFilePath, protectedBytes);
    }

    public async Task<string> LoadPassphraseAsync()
    {
        if (!File.Exists(_secretFilePath))
        {
            return string.Empty;
        }

        var protectedBytes = await File.ReadAllBytesAsync(_secretFilePath);
        var plainBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plainBytes);
    }
}

using System.Runtime.InteropServices;

namespace ErgoProxy.Core.Credentials;

public static class CredentialStoreFactory
{
    public static ICredentialStore CreateDefault(string? vaultDirectory = null)
    {
        var vaultPath = vaultDirectory != null 
            ? Path.Combine(vaultDirectory, "vault.enc") 
            : null;

        var encryptedFallback = new EncryptedFileCredentialStore(vaultPath);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return new LinuxSecretCredentialStore(encryptedFallback);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsCredentialStore(encryptedFallback);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new MacKeychainCredentialStore(encryptedFallback);
        }

        return encryptedFallback;
    }
}

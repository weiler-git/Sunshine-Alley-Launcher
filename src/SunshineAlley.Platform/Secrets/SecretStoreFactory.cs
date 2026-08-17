using SunshineAlley.Core;

namespace SunshineAlley.Platform.Secrets;

public static class SecretStoreFactory
{
    public static ISecretStore Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsCredentialSecretStore();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacKeychainSecretStore();
        }

        if (OperatingSystem.IsLinux())
        {
            return new LinuxSecretServiceStore();
        }

        throw new PlatformNotSupportedException(
            "Sunshine Alley supports Windows, Linux, and macOS secret stores.");
    }
}

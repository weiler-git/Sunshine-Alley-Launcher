using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using SunshineAlley.Core;
using SunshineAlley.Platform.Processes;

namespace SunshineAlley.Platform.Identity;

public sealed class DeviceIdentityService : IDeviceIdentityService
{
    private readonly ISettingsStore _settingsStore;

    public DeviceIdentityService(ISettingsStore settingsStore) => _settingsStore = settingsStore;

    public async Task<DeviceIdentity> GetAsync(CancellationToken cancellationToken = default)
    {
        if (OperatingSystem.IsWindows())
        {
            string? machineGuid = ReadWindowsMachineGuid();
            if (!string.IsNullOrWhiteSpace(machineGuid))
            {
                // Preserve the same value the legacy launcher sent to the server.
                return new DeviceIdentity(
                    machineGuid.Trim(),
                    DeviceIdentitySource.WindowsMachineGuid,
                    false);
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            string? source = await ReadFirstAsync(
                ["/sys/class/dmi/id/product_uuid", "/etc/machine-id", "/var/lib/dbus/machine-id"],
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(source))
            {
                return new DeviceIdentity(
                    CreateStableGuid("linux:" + source.Trim()),
                    DeviceIdentitySource.LinuxMachineId,
                    false);
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            string? platformUuid = await ReadMacPlatformUuidAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(platformUuid))
            {
                string value = Guid.TryParse(platformUuid, out Guid parsed)
                    ? parsed.ToString("D")
                    : CreateStableGuid("mac:" + platformUuid.Trim());
                return new DeviceIdentity(
                    value,
                    DeviceIdentitySource.MacPlatformUuid,
                    false);
            }
        }

        string? fallback = await _settingsStore.GetAsync<string>(
            SettingsKeys.FallbackDeviceId,
            cancellationToken);
        if (!Guid.TryParse(fallback, out Guid fallbackGuid))
        {
            fallbackGuid = Guid.NewGuid();
            await _settingsStore.SetAsync(
                SettingsKeys.FallbackDeviceId,
                fallbackGuid.ToString("D"),
                cancellationToken);
        }

        return new DeviceIdentity(
            fallbackGuid.ToString("D"),
            DeviceIdentitySource.PersistedFallback,
            true);
    }

    public static string CreateStableGuid(string source)
    {
        Span<byte> bytes = stackalloc byte[16];
        SHA256.HashData(Encoding.UTF8.GetBytes(source), bytes);
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x80); // UUID version 8: application-defined.
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80); // RFC 4122 variant.
        string value = Convert.ToHexStringLower(bytes);
        return $"{value[..8]}-{value[8..12]}-{value[12..16]}-{value[16..20]}-{value[20..]}";
    }

    private static string? ReadWindowsMachineGuid()
    {
        try
        {
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using RegistryKey? key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography", false);
                if (key?.GetValue("MachineGuid") is string value && !string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }

        return null;
    }

    private static async Task<string?> ReadFirstAsync(
        IEnumerable<string> paths,
        CancellationToken cancellationToken)
    {
        foreach (string path in paths)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                string value = await File.ReadAllTextAsync(path, cancellationToken);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Try the next OS-provided identifier.
            }
            catch (IOException)
            {
                // Try the next OS-provided identifier.
            }
        }

        return null;
    }

    private static async Task<string?> ReadMacPlatformUuidAsync(CancellationToken cancellationToken)
    {
        const string ioreg = "/usr/sbin/ioreg";
        if (!File.Exists(ioreg))
        {
            return null;
        }

        CommandResult result = await ProcessRunner.RunAsync(
            ioreg,
            ["-rd1", "-c", "IOPlatformExpertDevice"],
            cancellationToken: cancellationToken);
        if (result.ExitCode != 0)
        {
            return null;
        }

        Match match = Regex.Match(
            result.StandardOutput,
            "\\\"IOPlatformUUID\\\"\\s*=\\s*\\\"(?<uuid>[^\\\"]+)\\\"",
            RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["uuid"].Value : null;
    }
}

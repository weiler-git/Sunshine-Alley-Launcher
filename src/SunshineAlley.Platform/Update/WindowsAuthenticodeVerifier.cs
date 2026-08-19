using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using SunshineAlley.Core;

namespace SunshineAlley.Platform.Update;

internal static class WindowsAuthenticodeVerifier
{
    private static readonly Guid GenericVerifyV2 =
        new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    public static void Verify(string executable)
    {
#if SUNSHINE_ALLOW_UNSIGNED_UPDATES
        _ = executable;
        return;
#else
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Authenticode validation requires Windows.");
        }

        IntPtr filePath = Marshal.StringToCoTaskMemUni(executable);
        IntPtr fileInfoPointer = IntPtr.Zero;
        try
        {
            var fileInfo = new WinTrustFileInfo
            {
                StructSize = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                FilePath = filePath
            };
            fileInfoPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, fileInfoPointer, false);

            var trustData = new WinTrustData
            {
                StructSize = (uint)Marshal.SizeOf<WinTrustData>(),
                UIChoice = 2,
                RevocationChecks = 0,
                UnionChoice = 1,
                FileInfo = fileInfoPointer,
                StateAction = 0,
                ProviderFlags = 0,
                UIContext = 0
            };
            Guid action = GenericVerifyV2;
            int result = WinVerifyTrust(IntPtr.Zero, ref action, ref trustData);

            // 0x800B0109 = CERT_E_UNTRUSTEDROOT. This is expected for the
            // deliberately self-signed Sunshine Alley production certificate.
            // Other failures (bad digest, malformed signature, no signature, etc.)
            // are still rejected.
            const int CertEUntrustedRoot = unchecked((int)0x800B0109);
            if (result != 0 && result != CertEUntrustedRoot)
            {
                throw new LauncherException(
                    $"The downloaded launcher failed Authenticode signature validation (0x{result:X8}).");
            }
        }
        finally
        {
            if (fileInfoPointer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(fileInfoPointer);
            }

            Marshal.FreeCoTaskMem(filePath);
        }

        using X509Certificate signed = X509Certificate.CreateFromSignedFile(executable);
        using X509Certificate2 signer = X509CertificateLoader.LoadCertificate(
            signed.GetRawCertData());
        string expectedThumbprint = GetExpectedSigningCertificateThumbprint();
        string actualThumbprint = signer.Thumbprint?.Replace(" ", string.Empty, StringComparison.Ordinal)
            ?? string.Empty;

        if (!string.Equals(actualThumbprint, expectedThumbprint, StringComparison.OrdinalIgnoreCase))
        {
            throw new LauncherException(
                $"The update was signed by an unexpected certificate (thumbprint '{actualThumbprint}').");
        }

        string expectedPublisher = GetExpectedPublisher();
        string simpleName = signer.GetNameInfo(X509NameType.SimpleName, false);
        if (!string.Equals(simpleName, expectedPublisher, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(signer.Subject, expectedPublisher, StringComparison.OrdinalIgnoreCase))
        {
            throw new LauncherException(
                $"The update was signed by '{signer.Subject}', not the exact configured Sunshine Alley publisher '{expectedPublisher}'.");
        }
#endif
    }

    private static string GetExpectedPublisher()
    {
        string? value = Assembly.GetEntryAssembly()?
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(
                attribute.Key,
                "UpdatePublisherSubject",
                StringComparison.Ordinal))
            ?.Value;
        return string.IsNullOrWhiteSpace(value) ? "Sunshine Alley" : value;
    }


    private static string GetExpectedSigningCertificateThumbprint()
    {
        string? value = Assembly.GetEntryAssembly()?
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(
                attribute.Key,
                "UpdateSigningCertificateThumbprint",
                StringComparison.Ordinal))
            ?.Value;

        // Fallback pins the current Sunshine Alley production self-signed
        // certificate even if the project file has not yet been updated to
        // emit UpdateSigningCertificateThumbprint assembly metadata.
        return string.IsNullOrWhiteSpace(value)
            ? "8BAC2119463F067837614CE999B8E5B43F34592C"
            : value.Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int WinVerifyTrust(
        IntPtr window,
        ref Guid actionId,
        ref WinTrustData data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint StructSize;
        public IntPtr FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint StructSize;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UIChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProviderFlags;
        public uint UIContext;
        public IntPtr SignatureSettings;
    }
}

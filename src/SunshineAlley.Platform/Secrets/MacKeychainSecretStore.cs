using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using SunshineAlley.Core;

namespace SunshineAlley.Platform.Secrets;

internal sealed class MacKeychainSecretStore : ISecretStore
{
    private const int Success = 0;
    private const int ItemNotFound = -25300;
    private static readonly byte[] Service = Encoding.UTF8.GetBytes("sunshine-alley-launcher");

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(OperatingSystem.IsMacOS());

    public Task<string?> GetAsync(string name, CancellationToken cancellationToken = default) =>
        Task.Run(() => Get(name), cancellationToken);

    public Task SetAsync(
        string name,
        string value,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Set(name, value), cancellationToken);

    public Task RemoveAsync(string name, CancellationToken cancellationToken = default) =>
        Task.Run(() => Remove(name), cancellationToken);

    private static string? Get(string name)
    {
        byte[] account = Encoding.UTF8.GetBytes(name);
        int status = SecKeychainFindGenericPassword(
            IntPtr.Zero,
            (uint)Service.Length,
            Service,
            (uint)account.Length,
            account,
            out uint length,
            out IntPtr data,
            out IntPtr item);
        if (status == ItemNotFound)
        {
            return null;
        }

        EnsureSuccess(status, "read");
        try
        {
            var bytes = new byte[checked((int)length)];
            try
            {
                Marshal.Copy(data, bytes, 0, bytes.Length);
                return Encoding.UTF8.GetString(bytes);
            }
            finally
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes);
            }
        }
        finally
        {
            if (data != IntPtr.Zero)
            {
                SecKeychainItemFreeContent(IntPtr.Zero, data);
            }

            Release(item);
        }
    }

    private static void Set(string name, string value)
    {
        byte[] account = Encoding.UTF8.GetBytes(name);
        byte[] password = Encoding.UTF8.GetBytes(value);
        int status = SecKeychainFindGenericPassword(
            IntPtr.Zero,
            (uint)Service.Length,
            Service,
            (uint)account.Length,
            account,
            out _,
            out IntPtr existingData,
            out IntPtr item);
        try
        {
            if (status == Success)
            {
                int updateStatus = SecKeychainItemModifyAttributesAndData(
                    item,
                    IntPtr.Zero,
                    (uint)password.Length,
                    password);
                EnsureSuccess(updateStatus, "update");
                return;
            }

            if (status != ItemNotFound)
            {
                EnsureSuccess(status, "find");
            }

            int addStatus = SecKeychainAddGenericPassword(
                IntPtr.Zero,
                (uint)Service.Length,
                Service,
                (uint)account.Length,
                account,
                (uint)password.Length,
                password,
                out IntPtr newItem);
            try
            {
                EnsureSuccess(addStatus, "store");
            }
            finally
            {
                Release(newItem);
            }
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(password);
            if (existingData != IntPtr.Zero)
            {
                SecKeychainItemFreeContent(IntPtr.Zero, existingData);
            }

            Release(item);
        }
    }

    private static void Remove(string name)
    {
        byte[] account = Encoding.UTF8.GetBytes(name);
        int status = SecKeychainFindGenericPassword(
            IntPtr.Zero,
            (uint)Service.Length,
            Service,
            (uint)account.Length,
            account,
            out _,
            out IntPtr data,
            out IntPtr item);
        if (status == ItemNotFound)
        {
            return;
        }

        EnsureSuccess(status, "find");
        try
        {
            EnsureSuccess(SecKeychainItemDelete(item), "remove");
        }
        finally
        {
            if (data != IntPtr.Zero)
            {
                SecKeychainItemFreeContent(IntPtr.Zero, data);
            }

            Release(item);
        }
    }

    private static void EnsureSuccess(int status, string operation)
    {
        if (status != Success)
        {
            throw new Win32Exception(status, $"macOS Keychain could not {operation} the launcher key.");
        }
    }

    private static void Release(IntPtr value)
    {
        if (value != IntPtr.Zero)
        {
            CFRelease(value);
        }
    }

    [DllImport("/System/Library/Frameworks/Security.framework/Security")]
    private static extern int SecKeychainFindGenericPassword(
        IntPtr keychainOrArray,
        uint serviceNameLength,
        byte[] serviceName,
        uint accountNameLength,
        byte[] accountName,
        out uint passwordLength,
        out IntPtr passwordData,
        out IntPtr itemRef);

    [DllImport("/System/Library/Frameworks/Security.framework/Security")]
    private static extern int SecKeychainAddGenericPassword(
        IntPtr keychain,
        uint serviceNameLength,
        byte[] serviceName,
        uint accountNameLength,
        byte[] accountName,
        uint passwordLength,
        byte[] passwordData,
        out IntPtr itemRef);

    [DllImport("/System/Library/Frameworks/Security.framework/Security")]
    private static extern int SecKeychainItemModifyAttributesAndData(
        IntPtr itemRef,
        IntPtr attributes,
        uint length,
        byte[] data);

    [DllImport("/System/Library/Frameworks/Security.framework/Security")]
    private static extern int SecKeychainItemDelete(IntPtr itemRef);

    [DllImport("/System/Library/Frameworks/Security.framework/Security")]
    private static extern int SecKeychainItemFreeContent(IntPtr attributes, IntPtr data);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr value);
}

using System.Runtime.InteropServices;

namespace SunshineAlley.Platform.Installation;

internal static class WindowsShortcutService
{
    private const string ShortcutName = "Sunshine Alley Launcher.lnk";
    private static readonly Guid ShellLinkClassId =
        new("00021401-0000-0000-C000-000000000046");

    public static void Create(
        string executable,
        bool startMenu,
        bool desktop)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (startMenu)
        {
            string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            CreateShortcut(Path.Combine(programs, ShortcutName), executable);
        }

        if (desktop)
        {
            string desktopDirectory = Environment.GetFolderPath(
                Environment.SpecialFolder.DesktopDirectory);
            CreateShortcut(Path.Combine(desktopDirectory, ShortcutName), executable);
        }
    }

    public static void Remove()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        foreach (string path in GetShortcutPaths())
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static IEnumerable<string> GetShortcutPaths()
    {
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            ShortcutName);
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            ShortcutName);
    }

    private static void CreateShortcut(string shortcutPath, string executable)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
        Type shellLinkType = Type.GetTypeFromCLSID(
            ShellLinkClassId,
            throwOnError: true)!;
        object shellLinkObject = Activator.CreateInstance(shellLinkType)
            ?? throw new InvalidOperationException("Windows could not create the Shell Link COM object.");
        try
        {
            var link = (IShellLinkW)shellLinkObject;
            link.SetPath(executable);
            link.SetWorkingDirectory(Path.GetDirectoryName(executable)!);
            link.SetDescription("Sunshine Alley Valheim Launcher");
            link.SetIconLocation(executable, 0);
            ((IPersistFile)shellLinkObject).Save(shortcutPath, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(shellLinkObject);
        }
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath(IntPtr file, int count, IntPtr data, uint flags);
        void GetIDList(out IntPtr itemIdList);
        void SetIDList(IntPtr itemIdList);
        void GetDescription(IntPtr name, int count);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory(IntPtr directory, int count);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments(IntPtr arguments, int count);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCommand);
        void SetShowCmd(int showCommand);
        void GetIconLocation(IntPtr iconPath, int count, out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid classId);
        [PreserveSig]
        int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string fileName, bool remember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string fileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string fileName);
    }
}

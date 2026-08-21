namespace SunshineAlley.Platform;

public sealed record PlatformPaths(
    string ProductRoot,
    string ApplicationDirectory,
    string ConfigurationDirectory,
    string DataDirectory,
    string CacheDirectory,
    string StateDirectory,
    string LogDirectory)
{
    public const string LinuxApplicationId = "games.sunshinealley.launcher";

    public string SettingsFile => Path.Combine(ConfigurationDirectory, "settings.json");

    public string InstallationMarkerFile =>
        Path.Combine(ApplicationDirectory, "install.json");

    public string UpdateCacheDirectory => Path.Combine(CacheDirectory, "Updates");

    public string LinuxExecutablePath =>
        Path.Combine(ApplicationDirectory, "SunshineAlleyLauncher");

    public string LinuxDesktopEntryPath => Path.Combine(
        Path.GetDirectoryName(ProductRoot)!,
        "applications",
        LinuxApplicationId + ".desktop");

    public string LinuxIconPath => Path.Combine(
        Path.GetDirectoryName(ProductRoot)!,
        "icons",
        "hicolor",
        "256x256",
        "apps",
        LinuxApplicationId + ".png");

    public static PlatformPaths CreateDefault()
    {
        if (OperatingSystem.IsWindows())
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string productRoot = Path.Combine(local, "Sunshine Alley");
            string data = Path.Combine(productRoot, "Data");
            return new PlatformPaths(
                productRoot,
                Path.Combine(productRoot, "App"),
                Path.Combine(productRoot, "Config"),
                data,
                Path.Combine(productRoot, "Cache"),
                data,
                Path.Combine(data, "logs"));
        }

        string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsLinux())
        {
            return CreateLinux(user);
        }

        if (OperatingSystem.IsMacOS())
        {
            string applicationSupport = Path.Combine(user, "Library", "Application Support");
            string productRoot = Path.Combine(applicationSupport, "Sunshine Alley");
            string data = Path.Combine(productRoot, "Launcher");
            return new PlatformPaths(
                productRoot,
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory)),
                data,
                data,
                Path.Combine(user, "Library", "Caches", "Sunshine Alley", "Launcher"),
                data,
                Path.Combine(data, "logs"));
        }

        throw new PlatformNotSupportedException(
            "Sunshine Alley paths are supported on Windows, Linux, and macOS.");
    }

    public static PlatformPaths CreateLinux(
        string userProfile,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        if (string.IsNullOrWhiteSpace(userProfile)
            || !Path.IsPathFullyQualified(userProfile))
        {
            throw new ArgumentException(
                "The Linux user profile must be an absolute path.",
                nameof(userProfile));
        }

        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;
        string user = Path.TrimEndingDirectorySeparator(Path.GetFullPath(userProfile));
        string configurationRoot = GetXdgPath(
            "XDG_CONFIG_HOME",
            Path.Combine(user, ".config"),
            getEnvironmentVariable);
        string dataRoot = GetXdgPath(
            "XDG_DATA_HOME",
            Path.Combine(user, ".local", "share"),
            getEnvironmentVariable);
        string cacheRoot = GetXdgPath(
            "XDG_CACHE_HOME",
            Path.Combine(user, ".cache"),
            getEnvironmentVariable);
        string stateRoot = GetXdgPath(
            "XDG_STATE_HOME",
            Path.Combine(user, ".local", "state"),
            getEnvironmentVariable);

        string productRoot = Path.Combine(dataRoot, "sunshine-alley");
        string configuration = Path.Combine(
            configurationRoot,
            "sunshine-alley",
            "launcher");
        string cache = Path.Combine(cacheRoot, "sunshine-alley", "launcher");
        string state = Path.Combine(stateRoot, "sunshine-alley", "launcher");
        return new PlatformPaths(
            productRoot,
            Path.Combine(productRoot, "launcher"),
            configuration,
            Path.Combine(productRoot, "data"),
            cache,
            state,
            Path.Combine(state, "logs"));
    }

    public void EnsureDirectories()
    {
        EnsureDirectory(ConfigurationDirectory);
        EnsureDirectory(DataDirectory);
        EnsureDirectory(CacheDirectory);
        EnsureDirectory(UpdateCacheDirectory);
        EnsureDirectory(StateDirectory);
        EnsureDirectory(LogDirectory);
    }

    private static void EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException($"The launcher-owned directory is a symbolic link: '{path}'.");
        }

        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead
            | UnixFileMode.UserWrite
            | UnixFileMode.UserExecute);
    }

    private static string GetXdgPath(
        string name,
        string fallback,
        Func<string, string?> getEnvironmentVariable)
    {
        string? value = getEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return Path.GetFullPath(fallback);
        }

        try
        {
            if (!Path.IsPathFullyQualified(value))
            {
                return Path.GetFullPath(fallback);
            }

            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
        }
        catch (Exception exception) when (exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return Path.GetFullPath(fallback);
        }
    }
}

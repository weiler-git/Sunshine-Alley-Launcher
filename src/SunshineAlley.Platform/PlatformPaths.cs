namespace SunshineAlley.Platform;

public sealed record PlatformPaths(
    string ProductRoot,
    string ApplicationDirectory,
    string ConfigurationDirectory,
    string DataDirectory,
    string CacheDirectory,
    string LogDirectory)
{
    public string SettingsFile => Path.Combine(ConfigurationDirectory, "settings.json");

    public string InstallationMarkerFile =>
        Path.Combine(ApplicationDirectory, "install.json");

    public string UpdateCacheDirectory => Path.Combine(CacheDirectory, "Updates");

    public static PlatformPaths CreateDefault()
    {
        string configurationRoot;
        string dataRoot;
        string cacheRoot;

        if (OperatingSystem.IsWindows())
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string productRoot = Path.Combine(local, "Sunshine Alley");
            return new PlatformPaths(
                productRoot,
                Path.Combine(productRoot, "App"),
                Path.Combine(productRoot, "Config"),
                Path.Combine(productRoot, "Data"),
                Path.Combine(productRoot, "Cache"),
                Path.Combine(productRoot, "Data", "logs"));
        }
        else if (OperatingSystem.IsMacOS())
        {
            string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            configurationRoot = Path.Combine(user, "Library", "Application Support");
            dataRoot = configurationRoot;
            cacheRoot = Path.Combine(user, "Library", "Caches");
        }
        else
        {
            string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            configurationRoot = GetEnvironmentPath("XDG_CONFIG_HOME", Path.Combine(user, ".config"));
            dataRoot = GetEnvironmentPath("XDG_DATA_HOME", Path.Combine(user, ".local", "share"));
            cacheRoot = GetEnvironmentPath("XDG_CACHE_HOME", Path.Combine(user, ".cache"));
        }

        string productPath = OperatingSystem.IsLinux()
            ? Path.Combine("sunshine-alley", "launcher")
            : Path.Combine("Sunshine Alley", "Launcher");

        string configuration = Path.Combine(configurationRoot, productPath);
        string data = Path.Combine(dataRoot, productPath);
        string cache = Path.Combine(cacheRoot, productPath);
        return new PlatformPaths(
            Path.GetDirectoryName(configuration) ?? configuration,
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory)),
            configuration,
            data,
            cache,
            Path.Combine(data, "logs"));
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(ConfigurationDirectory);
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(UpdateCacheDirectory);
        Directory.CreateDirectory(LogDirectory);
    }

    private static string GetEnvironmentPath(string name, string fallback)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : Path.GetFullPath(value);
    }
}

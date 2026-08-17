using System.Text.Json;
using SunshineAlley.Core;

namespace SunshineAlley.Platform.Storage;

public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, JsonElement>? _values;

    public JsonSettingsStore(string path) => _path = Path.GetFullPath(path);

    public async Task<T?> GetAsync<T>(
        string key,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            if (!_values!.TryGetValue(key, out JsonElement element))
            {
                return default;
            }

            return DeserializeCompatible<T>(element);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetAsync<T>(
        string key,
        T value,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            _values![key] = JsonSerializer.SerializeToElement(value, JsonOptions);
            await SaveAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            if (_values!.Remove(key))
            {
                await SaveAsync(cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyDictionary<string, JsonElement>> GetByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            return _values!
                .Where(item => item.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(
                    item => item.Key,
                    item => item.Value.Clone(),
                    StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_values is not null)
        {
            return;
        }

        if (!File.Exists(_path))
        {
            _values = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            return;
        }

        try
        {
            await using FileStream stream = File.OpenRead(_path);
            Dictionary<string, JsonElement>? loaded = await JsonSerializer.DeserializeAsync<
                Dictionary<string, JsonElement>>(stream, JsonOptions, cancellationToken);
            _values = new Dictionary<string, JsonElement>(
                loaded ?? new Dictionary<string, JsonElement>(),
                StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException exception)
        {
            throw new LauncherException($"Settings file '{_path}' is not valid JSON.", exception);
        }
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    _values,
                    JsonOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    temporary,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            File.Move(temporary, _path, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static T? DeserializeCompatible<T>(JsonElement element)
    {
        Type targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        if (targetType == typeof(bool) && element.ValueKind == JsonValueKind.String)
        {
            if (bool.TryParse(element.GetString(), out bool value))
            {
                return JsonSerializer.Deserialize<T>(
                    value ? "true" : "false",
                    JsonOptions);
            }
        }

        if (targetType == typeof(string) && element.ValueKind is not JsonValueKind.String)
        {
            return (T)(object)element.ToString();
        }

        return element.Deserialize<T>(JsonOptions);
    }
}

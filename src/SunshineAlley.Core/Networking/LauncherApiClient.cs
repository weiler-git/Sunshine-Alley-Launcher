using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SunshineAlley.Core;

public sealed class LauncherApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly LauncherIdentity _identity;
    private readonly Uri _baseUri;

    public LauncherApiClient(
        HttpClient httpClient,
        LauncherIdentity identity,
        Uri? baseUri = null)
    {
        _httpClient = httpClient;
        _identity = identity;
        _baseUri = baseUri ?? LauncherConstants.ServiceBaseUri;
    }

    public Task<ServerListResponse> GetServerListAsync(
        string sessionGuid,
        int currentAuthLevel,
        CancellationToken cancellationToken = default)
    {
        var request = new ServerListRequest
        {
            SessionGuid = sessionGuid,
            PublicKey = currentAuthLevel < 1 ? _identity.PublicKeyXml : null
        };

        return PostSignedAsync<ServerListRequest, ServerListResponse>(
            "LauncherServerList",
            request,
            cancellationToken);
    }

    public Task<FileValidationResponse> VerifyFilesAsync(
        VerifyModPackRequest request,
        CancellationToken cancellationToken = default) =>
        PostSignedAsync<VerifyModPackRequest, FileValidationResponse>(
            "LauncherVerifyFiles",
            request,
            cancellationToken);

    private async Task<TResponse> PostSignedAsync<TRequest, TResponse>(
        string operation,
        TRequest request,
        CancellationToken cancellationToken)
    {
        string json = JsonSerializer.Serialize(request, JsonOptions);
        string signature = _identity.Sign(json);
        Uri endpoint = new(_baseUri, $"valheimapi?Exec={Uri.EscapeDataString(operation)}");

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["jsonData"] = json,
            ["sender"] = $"launcher:{_identity.DeviceId}",
            ["signature"] = signature
        });
        using HttpResponseMessage response = await _httpClient.PostAsync(
            endpoint,
            content,
            cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new LauncherException(
                $"Launcher API {operation} failed with HTTP {(int)response.StatusCode} ({response.StatusCode}).");
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            throw new LauncherException($"Launcher API {operation} returned an empty response.");
        }

        if (body.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
        {
            throw new LauncherException($"Launcher API {operation} returned {SanitizeError(body)}.");
        }

        try
        {
            return JsonSerializer.Deserialize<TResponse>(body, JsonOptions)
                ?? throw new JsonException("Response was JSON null.");
        }
        catch (JsonException exception)
        {
            throw new LauncherException(
                $"Launcher API {operation} returned an invalid JSON response.",
                exception);
        }
    }

    private static string SanitizeError(string body)
    {
        string firstLine = body.Split(['\r', '\n'], 2)[0];
        return firstLine.Length <= 160 ? firstLine : firstLine[..160];
    }
}

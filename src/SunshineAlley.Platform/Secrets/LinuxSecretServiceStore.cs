using SunshineAlley.Core;
using SunshineAlley.Platform.Processes;

namespace SunshineAlley.Platform.Secrets;

internal sealed class LinuxSecretServiceStore : ISecretStore
{
    private const string Service = "sunshine-alley-launcher";

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(
            OperatingSystem.IsLinux()
            && ProcessRunner.FindExecutable("secret-tool") is not null);

    public async Task<string?> GetAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        string executable = GetExecutable();
        CommandResult result = await ProcessRunner.RunAsync(
            executable,
            ["lookup", "service", Service, "account", name],
            cancellationToken: cancellationToken);
        if (result.ExitCode == 1 && string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return null;
        }

        EnsureSuccess(result, "read");
        return result.StandardOutput.TrimEnd('\r', '\n');
    }

    public async Task SetAsync(
        string name,
        string value,
        CancellationToken cancellationToken = default)
    {
        string executable = GetExecutable();
        CommandResult result = await ProcessRunner.RunAsync(
            executable,
            [
                "store",
                "--label=Sunshine Alley Launcher RSA identity",
                "service",
                Service,
                "account",
                name
            ],
            value + "\n",
            cancellationToken);
        EnsureSuccess(result, "store");
    }

    public async Task RemoveAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        string executable = GetExecutable();
        CommandResult result = await ProcessRunner.RunAsync(
            executable,
            ["clear", "service", Service, "account", name],
            cancellationToken: cancellationToken);
        if (result.ExitCode is not (0 or 1))
        {
            EnsureSuccess(result, "remove");
        }
    }

    private static string GetExecutable() =>
        ProcessRunner.FindExecutable("secret-tool")
        ?? throw new SecretStoreUnavailableException(
            "Linux Secret Service support requires 'secret-tool' (the libsecret-tools package) and an unlocked desktop keyring.");

    private static void EnsureSuccess(CommandResult result, string operation)
    {
        if (result.ExitCode == 0)
        {
            return;
        }

        string detail = string.IsNullOrWhiteSpace(result.StandardError)
            ? "the desktop keyring or Secret Service session is unavailable"
            : result.StandardError.Trim();
        throw new SecretStoreUnavailableException(
            $"Unable to {operation} the launcher RSA key through Linux Secret Service: {detail}");
    }
}

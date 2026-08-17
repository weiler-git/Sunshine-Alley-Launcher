using System.Collections.ObjectModel;
using SunshineAlley.Core;
using SunshineAlley.Platform;

namespace SunshineAlley.App.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly CancellationTokenSource _shutdown = new();
    private LauncherRuntime? _runtime;
    private LauncherPreferences? _preferences;
    private ServerItemViewModel? _selectedServer;
    private string _status = "Starting launcher…";
    private string _notice = string.Empty;
    private string _gameDirectory = string.Empty;
    private string _modDataDirectory = string.Empty;
    private string _deviceIdentityNote = string.Empty;
    private bool _useSteam = true;
    private bool _persistent;
    private bool _isBusy = true;
    private bool _isProgressVisible;
    private double _progressValue;
    private Task? _refreshLoop;

    public MainWindowViewModel()
    {
        PlayCommand = new AsyncCommand(
            () => RunOperationAsync(PlayCoreAsync),
            () => CanPlay);
        RefreshCommand = new AsyncCommand(
            () => RunOperationAsync(() => RefreshServersCoreAsync(false)),
            () => !IsBusy && _runtime is not null);
        VerifyCommand = new AsyncCommand(
            () => RunOperationAsync(VerifySelectedCoreAsync),
            () => !IsBusy && SelectedServer?.ModPackId > 0);
    }

    public ObservableCollection<ServerItemViewModel> Servers { get; } = [];
    public AsyncCommand PlayCommand { get; }
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand VerifyCommand { get; }

    public ServerItemViewModel? SelectedServer
    {
        get => _selectedServer;
        set
        {
            if (SetField(ref _selectedServer, value))
            {
                OnPropertyChanged(nameof(CanPlay));
                OnPropertyChanged(nameof(CanManageOptionalMods));
                OnPropertyChanged(nameof(OptionalModsText));
                RaiseCommandStates();
                if (value is not null)
                {
                    _ = SaveSelectedServerSafelyAsync(value.WorldName);
                }
            }
        }
    }

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public string Notice
    {
        get => _notice;
        private set
        {
            if (SetField(ref _notice, value))
            {
                OnPropertyChanged(nameof(HasNotice));
            }
        }
    }

    public bool HasNotice => !string.IsNullOrWhiteSpace(Notice);

    public string GameDirectory
    {
        get => _gameDirectory;
        private set => SetField(ref _gameDirectory, value);
    }

    public string ModDataDirectory
    {
        get => _modDataDirectory;
        private set => SetField(ref _modDataDirectory, value);
    }

    public string DeviceIdentityNote
    {
        get => _deviceIdentityNote;
        private set => SetField(ref _deviceIdentityNote, value);
    }

    public bool UseSteam
    {
        get => _useSteam;
        set
        {
            if (SetField(ref _useSteam, value))
            {
                _ = SavePreferencesSafelyAsync();
            }
        }
    }

    public bool Persistent
    {
        get => _persistent;
        set
        {
            if (SetField(ref _persistent, value))
            {
                _ = SavePreferencesSafelyAsync();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanPlay));
                RaiseCommandStates();
            }
        }
    }

    public bool IsProgressVisible
    {
        get => _isProgressVisible;
        private set => SetField(ref _isProgressVisible, value);
    }

    public double ProgressValue
    {
        get => _progressValue;
        private set => SetField(ref _progressValue, value);
    }

    public bool CanPlay =>
        !IsBusy
        && SelectedServer is not null
        && (SelectedServer.ModPackId <= 0
            || (_runtime?.ModPacks.GetState(SelectedServer.ModPackId).IsVerified ?? false));

    public bool CanManageOptionalMods =>
        SelectedServer?.ModPackId > 0
        && (_runtime?.ModPacks.GetState(SelectedServer.ModPackId).IsVerified ?? false);

    public string OptionalModsText
    {
        get
        {
            if (SelectedServer?.ModPackId is not > 0 || _runtime is null)
            {
                return "Optional mods unavailable";
            }

            IReadOnlyList<OptionalModState> mods = _runtime.ModPacks
                .GetState(SelectedServer.ModPackId)
                .OptionalMods;
            return $"{mods.Count(item => item.Enabled)} of {mods.Count} optional mods enabled";
        }
    }

    public async Task InitializeAsync()
    {
        await RunOperationAsync(async () =>
        {
            _runtime = await LauncherRuntime.CreateAsync(_shutdown.Token);
            _preferences = await _runtime.LoadPreferencesWithDiscoveryAsync(_shutdown.Token);
            GameDirectory = _preferences.GameDirectory;
            ModDataDirectory = _preferences.ModDataDirectory;
            _useSteam = _preferences.UseSteam;
            _persistent = _preferences.Persistent;
            OnPropertyChanged(nameof(UseSteam));
            OnPropertyChanged(nameof(Persistent));

            DeviceIdentity identity = _runtime.DeviceIdentity;
            DeviceIdentityNote = identity.IsFallback
                ? "A generated fallback device ID is in use because no OS machine identifier was available."
                : $"Device identity source: {identity.Source}.";

            await RefreshServersCoreAsync(true);
            if (SelectedServer?.ModPackId > 0)
            {
                await VerifySelectedCoreAsync();
            }

            Status = "Ready.";
        });

        if (_runtime is not null && !_shutdown.IsCancellationRequested)
        {
            _refreshLoop = RefreshLoopAsync(_shutdown.Token);
        }
    }

    public IReadOnlyList<OptionalModState> GetOptionalMods()
    {
        if (_runtime is null || SelectedServer?.ModPackId is not > 0)
        {
            return [];
        }

        return _runtime.ModPacks.GetState(SelectedServer.ModPackId).OptionalMods;
    }

    public async Task SetOptionalModEnabledAsync(string name, bool enabled)
    {
        if (_runtime is null || SelectedServer?.ModPackId is not > 0)
        {
            return;
        }

        await _runtime.ModPacks.SetOptionalEnabledAsync(
            SelectedServer.ModPackId,
            name,
            enabled,
            _shutdown.Token);
        OnPropertyChanged(nameof(OptionalModsText));
        OnPropertyChanged(nameof(CanPlay));
        RaiseCommandStates();
    }

    public Task ReverifySelectedAsync() => RunOperationAsync(VerifySelectedCoreAsync);

    public async Task UpdateDirectoriesAsync(string gameDirectory, string modDataDirectory)
    {
        await RunOperationAsync(async () =>
        {
            if (_runtime is null || _preferences is null)
            {
                return;
            }

            string normalizedGame = Path.GetFullPath(gameDirectory);
            string normalizedData = Path.GetFullPath(modDataDirectory);
            Directory.CreateDirectory(normalizedData);
            var request = new GameLaunchRequest(
                normalizedGame,
                normalizedData,
                LauncherConstants.VanillaModPackId,
                false,
                false);
            GameLaunchDiagnostics diagnostics = await _runtime.GameLauncher.DiagnoseAsync(
                request,
                _shutdown.Token);
            if (diagnostics.GameExecutable is null)
            {
                throw new LauncherException(
                    "The selected game directory does not contain the native Valheim executable for this operating system.");
            }

            GameDirectory = normalizedGame;
            ModDataDirectory = normalizedData;
            _preferences = _preferences with
            {
                GameDirectory = normalizedGame,
                ModDataDirectory = normalizedData
            };
            await _runtime.Settings.SaveAsync(_preferences, _shutdown.Token);
            Status = "Directories saved.";
        });
    }

    public async Task OpenModDataDirectoryAsync()
    {
        if (_runtime is null)
        {
            return;
        }

        Directory.CreateDirectory(ModDataDirectory);
        await _runtime.Shell.OpenFolderAsync(ModDataDirectory, _shutdown.Token);
    }

    private async Task RefreshServersCoreAsync(bool initial)
    {
        if (_runtime is null)
        {
            return;
        }

        Status = "Refreshing launch options…";
        string preferred = initial
            ? _preferences?.SelectedServer ?? string.Empty
            : SelectedServer?.WorldName ?? string.Empty;
        IReadOnlyList<GameServer> servers;
        try
        {
            servers = await _runtime.Servers.RefreshAsync(_shutdown.Token);
            Notice = string.Empty;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            servers =
            [
                new GameServer(
                    "Private World",
                    LauncherConstants.PrivateWorldModPackId,
                    0,
                    DateTime.MinValue,
                    DateTime.UtcNow,
                    true),
                new GameServer(
                    "Vanilla / No mods",
                    LauncherConstants.VanillaModPackId,
                    0,
                    DateTime.MinValue,
                    DateTime.UtcNow,
                    true)
            ];
            Notice = "The online server list is unavailable: " + exception.Message;
        }

        Servers.Clear();
        foreach (GameServer server in servers)
        {
            Servers.Add(new ServerItemViewModel(server));
        }

        SelectedServer = Servers.FirstOrDefault(item => string.Equals(
                item.WorldName,
                preferred,
                StringComparison.Ordinal))
            ?? Servers.FirstOrDefault();
        Status = "Launch options refreshed.";
    }

    private async Task VerifySelectedCoreAsync()
    {
        if (_runtime is null || SelectedServer?.ModPackId is not > 0)
        {
            return;
        }

        var progress = new Progress<LauncherProgress>(UpdateProgress);
        await _runtime.ModPacks.EnsureVerifiedAsync(
            SelectedServer.ModPackId,
            ModDataDirectory,
            _runtime.Servers.IsAdmin,
            progress,
            _shutdown.Token);
        IsProgressVisible = false;
        OnPropertyChanged(nameof(CanPlay));
        OnPropertyChanged(nameof(CanManageOptionalMods));
        OnPropertyChanged(nameof(OptionalModsText));
        RaiseCommandStates();
    }

    private async Task PlayCoreAsync()
    {
        if (_runtime is null || SelectedServer is null)
        {
            return;
        }

        if (SelectedServer.ModPackId > 0)
        {
            await VerifySelectedCoreAsync();
        }

        SteamInstallation? steam = await _runtime.Steam.DiscoverAsync(_shutdown.Token);
        if (!SteamBuildCompatibility.IsCompatible(steam?.BuildId, SelectedServer.SteamBuildId))
        {
            throw new LauncherException(
                $"Valheim build {steam?.BuildId} does not match server build {SelectedServer.SteamBuildId}. Update the game in Steam first.");
        }

        var request = new GameLaunchRequest(
            GameDirectory,
            ModDataDirectory,
            SelectedServer.ModPackId,
            UseSteam,
            Persistent);
        var progress = new Progress<LauncherProgress>(UpdateProgress);
        LaunchResult result = await _runtime.GameLauncher.LaunchAsync(
            request,
            progress,
            _shutdown.Token);
        IsProgressVisible = false;
        Status = $"Valheim exited after {result.Runtime:g}.";
    }

    private async Task RunOperationAsync(Func<Task> operation)
    {
        if (IsBusy && _runtime is not null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await operation();
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            Status = "Launcher is shutting down.";
        }
        catch (Exception exception)
        {
            Status = "Operation failed.";
            Notice = exception.Message;
        }
        finally
        {
            IsBusy = false;
            IsProgressVisible = false;
        }
    }

    private void UpdateProgress(LauncherProgress progress)
    {
        Status = progress.Message;
        IsProgressVisible = progress.Total > 0;
        ProgressValue = progress.Fraction * 100;
    }

    private async Task SaveSelectedServerSafelyAsync(string worldName)
    {
        if (_runtime is null || _preferences is null)
        {
            return;
        }

        try
        {
            _preferences = _preferences with { SelectedServer = worldName };
            await _runtime.Settings.SaveAsync(_preferences, _shutdown.Token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Notice = "Could not save the selected server: " + exception.Message;
        }
    }

    private async Task SavePreferencesSafelyAsync()
    {
        if (_runtime is null || _preferences is null)
        {
            return;
        }

        try
        {
            _preferences = _preferences with { UseSteam = UseSteam, Persistent = Persistent };
            await _runtime.Settings.SaveAsync(_preferences, _shutdown.Token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Notice = "Could not save launcher preferences: " + exception.Message;
        }
    }

    private async Task RefreshLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (IsBusy)
                {
                    continue;
                }

                await RunOperationAsync(() => RefreshServersCoreAsync(false));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void RaiseCommandStates()
    {
        PlayCommand.RaiseCanExecuteChanged();
        RefreshCommand.RaiseCanExecuteChanged();
        VerifyCommand.RaiseCanExecuteChanged();
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        if (_refreshLoop is not null)
        {
            try
            {
                await _refreshLoop;
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (_runtime is not null)
        {
            await _runtime.DisposeAsync();
        }

        _shutdown.Dispose();
    }
}

public sealed class ServerItemViewModel
{
    public ServerItemViewModel(GameServer server) => Server = server;

    public GameServer Server { get; }
    public string WorldName => Server.WorldName;
    public int ModPackId => Server.ModPackId;
    public int SteamBuildId => Server.SteamBuildId;
    public override string ToString() => WorldName;
}

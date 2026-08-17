using System.Collections.ObjectModel;
using SunshineAlley.Core;

namespace SunshineAlley.App.ViewModels;

public sealed class OptionalModsWindowViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _main;
    private string _status = "Changes are synchronized on the next verification pass.";

    public OptionalModsWindowViewModel(MainWindowViewModel main)
    {
        _main = main;
        ModPackTitle = main.SelectedServer is null
            ? "Optional mods"
            : $"Optional mods for modpack {main.SelectedServer.ModPackId}";
        AffectedServers = string.Join(
            ", ",
            main.Servers
                .Where(item => item.ModPackId == main.SelectedServer?.ModPackId)
                .Select(item => item.WorldName));

        foreach (OptionalModState option in main.GetOptionalMods())
        {
            Mods.Add(new OptionalModItemViewModel(
                option.Name,
                option.Enabled,
                SaveAsync));
        }
    }

    public string ModPackTitle { get; }
    public string AffectedServers { get; }
    public ObservableCollection<OptionalModItemViewModel> Mods { get; } = [];

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    private async Task SaveAsync(string name, bool enabled)
    {
        try
        {
            await _main.SetOptionalModEnabledAsync(name, enabled);
            Status = $"{name} will be {(enabled ? "enabled" : "disabled")}.";
        }
        catch (Exception exception)
        {
            Status = "Could not save the change: " + exception.Message;
        }
    }
}

public sealed class OptionalModItemViewModel : ViewModelBase
{
    private readonly Func<string, bool, Task> _save;
    private bool _enabled;

    public OptionalModItemViewModel(
        string name,
        bool enabled,
        Func<string, bool, Task> save)
    {
        Name = name;
        _enabled = enabled;
        _save = save;
    }

    public string Name { get; }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (SetField(ref _enabled, value))
            {
                _ = _save(Name, value);
            }
        }
    }
}

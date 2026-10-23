using System.ComponentModel;
using SNESEmulator.Contracts;
using SNESEmulator.Management;

namespace SNESEmulatorManager;
/// <summary>Long-lived connection identity and unacknowledged drafts survive discovery refreshes.</summary>
public sealed class ManagerViewModel : INotifyPropertyChanged
{
    private InstanceInfo? selected;
    public InstanceInfo? Selected
    {
        get => selected;
        set
        {
            selected = value;
            PropertyChanged?.Invoke(this, new(nameof(Selected)));
        }
    }

    public InstanceInfo? SettingsInstance { get; set; }
    public SettingsInfo? LoadedSettings { get; set; }
    public bool SettingsDirty { get; set; }
    public long Revision { get; set; }
    public List<GameItem> Games { get; set; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;
}

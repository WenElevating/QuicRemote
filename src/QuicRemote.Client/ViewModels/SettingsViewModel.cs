using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuicRemote.Client.Services;

namespace QuicRemote.Client.ViewModels;

/// <summary>
/// ViewModel for client settings
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;

    [ObservableProperty]
    private bool _hardwareAccelerated = true;

    [ObservableProperty]
    private string _selectedScaleMode = "AspectFit";

    [ObservableProperty]
    private bool _showStats = true;

    [ObservableProperty]
    private bool _showShortcutSettings;

    public ObservableCollection<ShortcutKeyViewModel> ShortcutKeys { get; } = new();

    public string[] AvailableScaleModes { get; } = new[] { "AspectFit", "AspectFill", "Fill", "None" };

    public SettingsViewModel(SettingsService settingsService)
    {
        _settingsService = settingsService;
        LoadSettings();
    }

    private void LoadSettings()
    {
        var settings = _settingsService.Settings;
        HardwareAccelerated = settings.HardwareAccelerated;
        SelectedScaleMode = settings.ScaleMode;
        ShowStats = settings.ShowStats;

        // Initialize default shortcuts if needed
        _settingsService.InitializeDefaultShortcuts();

        // Load shortcut keys
        foreach (var shortcut in settings.ShortcutKeys)
        {
            ShortcutKeys.Add(new ShortcutKeyViewModel(
                shortcut.Action,
                GetActionDisplayName(shortcut.Action),
                shortcut.Key,
                shortcut.Ctrl,
                shortcut.Alt,
                shortcut.Shift,
                this
            ));
        }
    }

    private static string GetActionDisplayName(string action)
    {
        return action switch
        {
            "ToggleFullscreen" => "Toggle Fullscreen",
            "ExitFullscreen" => "Exit Fullscreen",
            "Disconnect" => "Disconnect",
            "ToggleStats" => "Toggle Stats",
            "SendCtrlAltDel" => "Send Ctrl+Alt+Del",
            _ => action
        };
    }

    [RelayCommand]
    private void SaveSettings()
    {
        var settings = _settingsService.Settings;
        settings.HardwareAccelerated = HardwareAccelerated;
        settings.ScaleMode = SelectedScaleMode;
        settings.ShowStats = ShowStats;

        foreach (var vm in ShortcutKeys)
        {
            _settingsService.UpdateShortcutKey(vm.Action, vm.Key, vm.Ctrl, vm.Alt, vm.Shift);
        }

        _ = _settingsService.SaveSettingsAsync();
    }

    [RelayCommand]
    private void ResetShortcuts()
    {
        ShortcutKeys.Clear();
        _settingsService.ResetShortcutKeysToDefault();

        foreach (var shortcut in _settingsService.Settings.ShortcutKeys)
        {
            ShortcutKeys.Add(new ShortcutKeyViewModel(
                shortcut.Action,
                GetActionDisplayName(shortcut.Action),
                shortcut.Key,
                shortcut.Ctrl,
                shortcut.Alt,
                shortcut.Shift,
                this
            ));
        }
    }

    partial void OnHardwareAcceleratedChanged(bool value)
    {
        _settingsService.Settings.HardwareAccelerated = value;
        _settingsService.MarkDirty();
    }

    partial void OnSelectedScaleModeChanged(string value)
    {
        _settingsService.UpdateScaleMode(value);
    }

    partial void OnShowStatsChanged(bool value)
    {
        _settingsService.UpdateShowStats(value);
    }
}

/// <summary>
/// ViewModel for a single shortcut key
/// </summary>
public partial class ShortcutKeyViewModel : ObservableObject
{
    [ObservableProperty]
    private string _key;

    [ObservableProperty]
    private bool _ctrl;

    [ObservableProperty]
    private bool _alt;

    [ObservableProperty]
    private bool _shift;

    [ObservableProperty]
    private bool _isEditing;

    public string Action { get; }
    public string DisplayName { get; }
    private readonly SettingsViewModel _parent;

    public string DisplayText
    {
        get
        {
            var parts = new System.Collections.Generic.List<string>();
            if (Ctrl) parts.Add("Ctrl");
            if (Alt) parts.Add("Alt");
            if (Shift) parts.Add("Shift");
            if (!string.IsNullOrEmpty(Key)) parts.Add(Key);
            return string.Join(" + ", parts);
        }
    }

    public ShortcutKeyViewModel(string action, string displayName, string key, bool ctrl, bool alt, bool shift, SettingsViewModel parent)
    {
        Action = action;
        DisplayName = displayName;
        _key = key;
        _ctrl = ctrl;
        _alt = alt;
        _shift = shift;
        _parent = parent;
    }

    [RelayCommand]
    private void StartEdit()
    {
        IsEditing = true;
    }

    [RelayCommand]
    private void EndEdit()
    {
        IsEditing = false;
        _parent.SaveSettingsCommand.Execute(null);
    }

    public void SetKey(string key, bool ctrl, bool alt, bool shift)
    {
        Key = key;
        Ctrl = ctrl;
        Alt = alt;
        Shift = shift;
        OnPropertyChanged(nameof(DisplayText));
    }
}

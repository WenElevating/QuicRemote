using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace QuicRemote.Client.Services;

/// <summary>
/// Connection history entry
/// </summary>
public class ConnectionHistoryEntry
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 4820;
    public string? DeviceId { get; set; }
    public DateTime LastConnected { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Shortcut key configuration
/// </summary>
public class ShortcutKeyConfig
{
    public string Action { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public bool Ctrl { get; set; }
    public bool Alt { get; set; }
    public bool Shift { get; set; }

    public string DisplayText
    {
        get
        {
            var parts = new List<string>();
            if (Ctrl) parts.Add("Ctrl");
            if (Alt) parts.Add("Alt");
            if (Shift) parts.Add("Shift");
            if (!string.IsNullOrEmpty(Key)) parts.Add(Key);
            return string.Join(" + ", parts);
        }
    }
}

/// <summary>
/// Application settings for the Client
/// </summary>
public class ClientSettings
{
    public string Codec { get; set; } = "H264";
    public bool HardwareAccelerated { get; set; } = true;
    public string ScaleMode { get; set; } = "AspectFit";
    public bool ShowStats { get; set; } = true;
    public List<ConnectionHistoryEntry> ConnectionHistory { get; set; } = new();
    public int MaxHistoryEntries { get; set; } = 10;
    public List<ShortcutKeyConfig> ShortcutKeys { get; set; } = new();

    /// <summary>
    /// Gets the default shortcut key configurations
    /// </summary>
    public static List<ShortcutKeyConfig> GetDefaultShortcuts()
    {
        return new List<ShortcutKeyConfig>
        {
            new() { Action = "ToggleFullscreen", Key = "F11", Ctrl = false, Alt = false, Shift = false },
            new() { Action = "ExitFullscreen", Key = "Escape", Ctrl = false, Alt = false, Shift = false },
            new() { Action = "Disconnect", Key = "F12", Ctrl = false, Alt = false, Shift = false },
            new() { Action = "ToggleStats", Key = "S", Ctrl = true, Alt = false, Shift = false },
            new() { Action = "SendCtrlAltDel", Key = "Delete", Ctrl = true, Alt = true, Shift = false },
        };
    }

    /// <summary>
    /// Gets a shortcut configuration by action name
    /// </summary>
    public ShortcutKeyConfig? GetShortcut(string action)
    {
        return ShortcutKeys.FirstOrDefault(s => s.Action == action);
    }
}

/// <summary>
/// Service for persisting application settings
/// </summary>
public class SettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "QuicRemote",
        "client_settings.json"
    );

    private ClientSettings? _settings;
    private bool _dirty;

    public ClientSettings Settings => _settings ??= LoadSettings();

    public SettingsService()
    {
        LoadSettings();
    }

    public ClientSettings LoadSettings()
    {
        if (_settings != null) return _settings;

        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                _settings = JsonSerializer.Deserialize<ClientSettings>(json) ?? new ClientSettings();
            }
            else
            {
                _settings = new ClientSettings();
            }
        }
        catch
        {
            _settings = new ClientSettings();
        }

        return _settings;
    }

    public async Task SaveSettingsAsync()
    {
        if (!_dirty || _settings == null) return;

        try
        {
            var directory = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            await File.WriteAllTextAsync(SettingsPath, json);
            _dirty = false;
        }
        catch
        {
            // Ignore save errors
        }
    }

    public void MarkDirty()
    {
        _dirty = true;
    }

    public void AddConnectionHistory(string host, int port, string? deviceId = null)
    {
        if (_settings == null) return;

        // Remove existing entry with same host:port
        _settings.ConnectionHistory.RemoveAll(h => h.Host == host && h.Port == port);

        // Add new entry at beginning
        _settings.ConnectionHistory.Insert(0, new ConnectionHistoryEntry
        {
            Host = host,
            Port = port,
            DeviceId = deviceId,
            LastConnected = DateTime.UtcNow
        });

        // Trim to max entries
        while (_settings.ConnectionHistory.Count > _settings.MaxHistoryEntries)
        {
            _settings.ConnectionHistory.RemoveAt(_settings.ConnectionHistory.Count - 1);
        }

        MarkDirty();
    }

    public void UpdateScaleMode(string scaleMode)
    {
        if (Settings.ScaleMode != scaleMode)
        {
            Settings.ScaleMode = scaleMode;
            MarkDirty();
        }
    }

    public void UpdateShowStats(bool showStats)
    {
        if (Settings.ShowStats != showStats)
        {
            Settings.ShowStats = showStats;
            MarkDirty();
        }
    }

    public void UpdateShortcutKey(string action, string key, bool ctrl, bool alt, bool shift)
    {
        var existing = Settings.GetShortcut(action);
        if (existing != null)
        {
            existing.Key = key;
            existing.Ctrl = ctrl;
            existing.Alt = alt;
            existing.Shift = shift;
        }
        else
        {
            Settings.ShortcutKeys.Add(new ShortcutKeyConfig
            {
                Action = action,
                Key = key,
                Ctrl = ctrl,
                Alt = alt,
                Shift = shift
            });
        }
        MarkDirty();
    }

    public void ResetShortcutKeysToDefault()
    {
        Settings.ShortcutKeys = ClientSettings.GetDefaultShortcuts();
        MarkDirty();
    }

    public void InitializeDefaultShortcuts()
    {
        if (Settings.ShortcutKeys.Count == 0)
        {
            Settings.ShortcutKeys = ClientSettings.GetDefaultShortcuts();
            MarkDirty();
        }
    }
}

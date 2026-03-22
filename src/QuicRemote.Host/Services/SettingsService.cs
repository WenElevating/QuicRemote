using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace QuicRemote.Host.Services;

/// <summary>
/// Application settings for the Host
/// </summary>
public class HostSettings
{
    public int Port { get; set; } = 4820;
    public int MonitorIndex { get; set; } = 0;
    public string Codec { get; set; } = "H264";
    public int BitrateKbps { get; set; } = 5000;
    public int Framerate { get; set; } = 60;
    public bool LowLatency { get; set; } = true;
    public bool HardwareAccelerated { get; set; } = true;
    public bool StartMinimized { get; set; } = false;
    public bool AutoStart { get; set; } = false;
    public string? Password { get; set; }
}

/// <summary>
/// Service for persisting application settings
/// </summary>
public class SettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "QuicRemote",
        "host_settings.json"
    );

    private HostSettings? _settings;
    private bool _dirty;

    public HostSettings Settings => _settings ??= LoadSettings();

    public SettingsService()
    {
        LoadSettings();
    }

    public HostSettings LoadSettings()
    {
        if (_settings != null) return _settings;

        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                _settings = JsonSerializer.Deserialize<HostSettings>(json) ?? new HostSettings();
            }
            else
            {
                _settings = new HostSettings();
            }
        }
        catch
        {
            _settings = new HostSettings();
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

    public void UpdatePort(int port)
    {
        if (Settings.Port != port)
        {
            Settings.Port = port;
            MarkDirty();
        }
    }

    public void UpdateMonitorIndex(int index)
    {
        if (Settings.MonitorIndex != index)
        {
            Settings.MonitorIndex = index;
            MarkDirty();
        }
    }

    public void UpdateCodec(string codec)
    {
        if (Settings.Codec != codec)
        {
            Settings.Codec = codec;
            MarkDirty();
        }
    }

    public void UpdateBitrate(int bitrateKbps)
    {
        if (Settings.BitrateKbps != bitrateKbps)
        {
            Settings.BitrateKbps = bitrateKbps;
            MarkDirty();
        }
    }

    public void UpdateFramerate(int framerate)
    {
        if (Settings.Framerate != framerate)
        {
            Settings.Framerate = framerate;
            MarkDirty();
        }
    }

    public void UpdatePassword(string? password)
    {
        if (Settings.Password != password)
        {
            Settings.Password = password;
            MarkDirty();
        }
    }

    public void UpdateAutoStart(bool autoStart)
    {
        if (Settings.AutoStart != autoStart)
        {
            Settings.AutoStart = autoStart;
            SetAutoStartRegistry(autoStart);
            MarkDirty();
        }
    }

    public void ApplyAutoStart()
    {
        SetAutoStartRegistry(Settings.AutoStart);
    }

    private static void SetAutoStartRegistry(bool enable)
    {
        const string appName = "QuicRemoteHost";
        var exePath = Environment.ProcessPath;

        if (string.IsNullOrEmpty(exePath)) return;

        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
        if (key == null) return;

        if (enable)
        {
            key.SetValue(appName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(appName, false);
        }
    }

    public static bool IsAutoStartEnabled()
    {
        const string appName = "QuicRemoteHost";

        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
        return key?.GetValue(appName) != null;
    }
}

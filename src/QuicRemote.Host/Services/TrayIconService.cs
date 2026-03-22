using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;

namespace QuicRemote.Host.Services;

/// <summary>
/// Service for managing system tray icon and notifications
/// </summary>
public class TrayIconService : IDisposable
{
    private TaskbarIcon? _notifyIcon;
    private bool _disposed;
    private System.Windows.Controls.MenuItem? _autoStartItem;

    // Cached icon to avoid GDI handle leaks
    private static Icon? _cachedIcon;
    private static IntPtr _cachedIconHandle;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public event EventHandler? ShowWindowRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler? StartStopRequested;
    public event EventHandler<bool>? AutoStartChanged;

    public bool IsRunning { get; set; }
    public int ClientCount { get; set; }
    public bool AutoStartEnabled { get; set; }

    public void Initialize(bool autoStartEnabled = false)
    {
        if (_notifyIcon != null) return;

        AutoStartEnabled = autoStartEnabled;
        _notifyIcon = new TaskbarIcon
        {
            ToolTipText = "QuicRemote Host - Ready",
            Icon = CreateIcon(),
            ContextMenu = CreateContextMenu()
        };

        _notifyIcon.TrayMouseDoubleClick += (s, e) => ShowWindowRequested?.Invoke(this, EventArgs.Empty);
    }

    private System.Windows.Controls.ContextMenu CreateContextMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();

        // Status item (non-clickable)
        var statusItem = new System.Windows.Controls.MenuItem
        {
            Header = "Status: Ready",
            IsEnabled = false,
            FontWeight = FontWeights.Bold
        };
        menu.Items.Add(statusItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        // Show Window
        var showItem = new System.Windows.Controls.MenuItem
        {
            Header = "Show Window"
        };
        showItem.Click += (s, e) => ShowWindowRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(showItem);

        // Start/Stop
        var toggleItem = new System.Windows.Controls.MenuItem
        {
            Header = "Start Sharing"
        };
        toggleItem.Click += (s, e) =>
        {
            StartStopRequested?.Invoke(this, EventArgs.Empty);
            toggleItem.Header = IsRunning ? "Stop Sharing" : "Start Sharing";
        };
        menu.Items.Add(toggleItem);

        // Auto-start option
        _autoStartItem = new System.Windows.Controls.MenuItem
        {
            Header = "Auto-start on Login",
            IsCheckable = true,
            IsChecked = AutoStartEnabled
        };
        _autoStartItem.Click += (s, e) =>
        {
            AutoStartEnabled = _autoStartItem.IsChecked;
            AutoStartChanged?.Invoke(this, AutoStartEnabled);
        };
        menu.Items.Add(_autoStartItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        // Exit
        var exitItem = new System.Windows.Controls.MenuItem
        {
            Header = "Exit"
        };
        exitItem.Click += (s, e) => ExitRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(exitItem);

        return menu;
    }

    public void UpdateStatus(string status, bool isRunning, int clientCount = 0)
    {
        if (_notifyIcon == null) return;

        IsRunning = isRunning;
        ClientCount = clientCount;

        var statusText = isRunning
            ? $"Running - {clientCount} client(s) connected"
            : "Ready";

        _notifyIcon.ToolTipText = $"QuicRemote Host\n{statusText}";

        // Update context menu
        if (_notifyIcon.ContextMenu?.Items[0] is System.Windows.Controls.MenuItem statusItem)
        {
            statusItem.Header = $"Status: {status}";
        }
    }

    public void ShowNotification(string title, string message, BalloonIcon icon = BalloonIcon.Info)
    {
        _notifyIcon?.ShowBalloonTip(title, message, icon);
    }

    public void Show()
    {
        // TaskbarIcon is created on initialization, no need to force create
        _notifyIcon?.ShowBalloonTip("", "", BalloonIcon.None);
    }

    private static Icon CreateIcon()
    {
        // Return cached icon if available
        if (_cachedIcon != null)
        {
            return _cachedIcon;
        }

        // Create a simple icon programmatically
        using var bitmap = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bitmap);

        // Background circle
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        using var brush = new SolidBrush(Color.FromArgb(0, 113, 227)); // Apple blue
        g.FillEllipse(brush, 2, 2, 28, 28);

        // QR text
        using var font = new Font(new FontFamily("Arial"), 10, System.Drawing.FontStyle.Bold);
        using var textBrush = new SolidBrush(Color.White);
        var textSize = g.MeasureString("QR", font);
        g.DrawString("QR", font, textBrush,
            (32 - textSize.Width) / 2,
            (32 - textSize.Height) / 2 - 1);

        // Convert to icon and cache
        _cachedIconHandle = bitmap.GetHicon();
        _cachedIcon = Icon.FromHandle(_cachedIconHandle);

        return _cachedIcon;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _notifyIcon?.Dispose();
        _notifyIcon = null;
    }

    /// <summary>
    /// Cleanup static resources - call when application exits
    /// </summary>
    public static void CleanupStaticResources()
    {
        if (_cachedIconHandle != IntPtr.Zero)
        {
            DestroyIcon(_cachedIconHandle);
            _cachedIconHandle = IntPtr.Zero;
            _cachedIcon = null;
        }
    }
}

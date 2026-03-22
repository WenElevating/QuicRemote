using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using QuicRemote.Host.Resources;

namespace QuicRemote.Host.Services;

/// <summary>
/// Provides localization services for the application
/// </summary>
public class LocalizationService : INotifyPropertyChanged
{
    private static LocalizationService? _instance;
    private CultureInfo _currentCulture;

    public static LocalizationService Instance => _instance ??= new LocalizationService();

    public CultureInfo CurrentCulture
    {
        get => _currentCulture;
        private set
        {
            if (_currentCulture != value)
            {
                _currentCulture = value;
                OnPropertyChanged(nameof(CurrentCulture));
                OnPropertyChanged(nameof(Strings));
                OnLanguageChanged();
            }
        }
    }

    /// <summary>
    /// Gets the resource manager for strings
    /// </summary>
    public System.Resources.ResourceManager ResourceManager => Strings.ResourceManager;

    /// <summary>
    /// Available languages
    /// </summary>
    public IReadOnlyList<LanguageOption> AvailableLanguages { get; } = new List<LanguageOption>
    {
        new("zh-CN", "简体中文"),
        new("en-US", "English")
    };

    /// <summary>
    /// Event raised when language changes
    /// </summary>
    public event EventHandler? LanguageChanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    private LocalizationService()
    {
        // Default to Chinese
        _currentCulture = new CultureInfo("zh-CN");
        ApplyCulture(_currentCulture);
    }

    /// <summary>
    /// Changes the current language
    /// </summary>
    public void ChangeLanguage(string cultureName)
    {
        var culture = new CultureInfo(cultureName);
        CurrentCulture = culture;
        ApplyCulture(culture);
    }

    /// <summary>
    /// Changes the current language
    /// </summary>
    public void ChangeLanguage(CultureInfo culture)
    {
        CurrentCulture = culture;
        ApplyCulture(culture);
    }

    private void ApplyCulture(CultureInfo culture)
    {
        Thread.CurrentThread.CurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
    }

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected virtual void OnLanguageChanged()
    {
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// Represents a language option
/// </summary>
public class LanguageOption
{
    public string CultureName { get; }
    public string DisplayName { get; }

    public LanguageOption(string cultureName, string displayName)
    {
        CultureName = cultureName;
        DisplayName = displayName;
    }
}

using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using QuicRemote.Host.Resources;
using QuicRemote.Host.Services;

namespace QuicRemote.Host;

/// <summary>
/// Markup extension for localizing strings in XAML with dynamic switching support
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public class LocalizeExtension : MarkupExtension, INotifyPropertyChanged
{
    private static readonly LocalizeExtension[] EmptyArray = Array.Empty<LocalizeExtension>();
    private static LocalizeExtension[] _instances = EmptyArray;
    private static readonly object _lock = new();

    private string _key = string.Empty;

    public string Key
    {
        get => _key;
        set
        {
            if (_key != value)
            {
                _key = value;
                OnPropertyChanged(nameof(Value));
            }
        }
    }

    /// <summary>
    /// Gets the localized value
    /// </summary>
    public string Value => Strings.ResourceManager.GetString(Key, CultureInfo.CurrentUICulture) ?? $"#{Key}#";

    public LocalizeExtension()
    {
        RegisterInstance(this);
    }

    public LocalizeExtension(string key) : this()
    {
        _key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        // Return binding to enable dynamic updates
        var binding = new Binding(nameof(Value))
        {
            Source = this,
            Mode = BindingMode.OneWay
        };
        return binding.ProvideValue(serviceProvider);
    }

    private static void RegisterInstance(LocalizeExtension instance)
    {
        lock (_lock)
        {
            var newArray = new LocalizeExtension[_instances.Length + 1];
            Array.Copy(_instances, newArray, _instances.Length);
            newArray[_instances.Length] = instance;
            _instances = newArray;
        }

        // Subscribe to language changes
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
    }

    private static void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (var instance in _instances)
        {
            instance.OnPropertyChanged(nameof(Value));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

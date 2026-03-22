using System.Windows;
using System.Windows.Input;
using QuicRemote.Host.ViewModels;

namespace QuicRemote.Host;

public partial class MainWindow : Window
{
    private bool _closeForReal;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        StateChanged += OnStateChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Add converters to resources if not present
        if (!Resources.Contains("BoolToVisibilityConverter"))
        {
            Resources.Add("BoolToVisibilityConverter", new BoolToVisibilityConverter());
        }
        if (!Resources.Contains("ColorToBrushConverter"))
        {
            Resources.Add("ColorToBrushConverter", new ColorToBrushConverter());
        }
        if (!Resources.Contains("BoolToColorConverter"))
        {
            Resources.Add("BoolToColorConverter", new BoolToColorConverter());
        }

        // Register with App
        (Application.Current as App)?.SetMainWindow(this);

        // Subscribe to view model changes
        if (DataContext is MainViewModel vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;

            // Set initial password if any
            if (!string.IsNullOrEmpty(vm.Password))
            {
                PasswordBox.Password = vm.Password;
            }
        }
    }

    private void OnStateChanged(object? sender, System.EventArgs e)
    {
        // Minimize to tray
        if (WindowState == WindowState.Minimized)
        {
            Hide();
            (Application.Current as App)?.ShowNotification("QuicRemote Host", "Running in background. Double-click tray icon to restore.");
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            (Application.Current as App)?.UpdateTrayStatus(
                vm.StatusText,
                vm.IsRunning,
                vm.ConnectedClients);
        }
    }

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.Password = PasswordBox.Password;
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_closeForReal)
        {
            // Minimize to tray instead of closing
            e.Cancel = true;
            WindowState = WindowState.Minimized;
            return;
        }

        if (DataContext is MainViewModel vm)
        {
            vm.PropertyChanged -= OnViewModelPropertyChanged;
            _ = vm.DisposeAsync();
        }
        base.OnClosing(e);
    }

    public void CloseForReal()
    {
        _closeForReal = true;
        Close();
    }
}

/// <summary>
/// Converts bool to Visibility
/// </summary>
public class BoolToVisibilityConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        return value is bool b && b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        return value is Visibility v && v == Visibility.Visible;
    }
}

/// <summary>
/// Converts color string to SolidColorBrush
/// </summary>
public class ColorToBrushConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is string colorStr)
        {
            try
            {
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorStr);
                return new System.Windows.Media.SolidColorBrush(color);
            }
            catch
            {
                return System.Windows.Media.Brushes.Gray;
            }
        }
        return System.Windows.Media.Brushes.Gray;
    }

    public object ConvertBack(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new System.NotImplementedException();
    }
}

/// <summary>
/// Converts bool to color string based on parameter
/// </summary>
public class BoolToColorConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        var colors = (parameter as string)?.Split('|');
        if (colors == null || colors.Length != 2)
            return "#86868B";

        return value is bool b && b ? colors[0] : colors[1];
    }

    public object ConvertBack(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new System.NotImplementedException();
    }
}

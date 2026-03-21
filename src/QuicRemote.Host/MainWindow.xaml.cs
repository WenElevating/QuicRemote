using System.Windows;
using System.Windows.Input;
using QuicRemote.Host.ViewModels;

namespace QuicRemote.Host;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
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
    }

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    protected override async void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            await vm.DisposeAsync();
        }
        base.OnClosing(e);
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

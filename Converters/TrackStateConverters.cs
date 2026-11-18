using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using CadenceSongSync.Models;

namespace CadenceSongSync.Converters;

public class MissingToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush NormalBrush = new(System.Windows.Media.Color.FromRgb(226, 232, 240)); // Slate 200
    private static readonly SolidColorBrush MissingBrush = new(System.Windows.Media.Color.FromRgb(239, 68, 68));   // Red 500
    private static readonly SolidColorBrush ActiveBrush = new(System.Windows.Media.Color.FromRgb(56, 189, 248));   // Sky 400

    static MissingToBrushConverter()
    {
        NormalBrush.Freeze();
        MissingBrush.Freeze();
        ActiveBrush.Freeze();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isMissing && isMissing)
        {
            return MissingBrush;
        }

        if (parameter?.ToString() == "active" && value is bool isActive && isActive)
        {
            return ActiveBrush;
        }

        return NormalBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class MissingToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isMissing = value is bool b && b;
        bool invert = parameter?.ToString() == "invert";
        return (isMissing ^ invert) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class RepeatModeToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is RepeatMode mode)
        {
            return mode switch
            {
                RepeatMode.RepeatOff => "Off",
                RepeatMode.RepeatAll => "All",
                RepeatMode.RepeatTrack => "1",
                _ => "Off"
            };
        }
        return "Off";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class BooleanToActiveBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush InactiveBrush = new(System.Windows.Media.Color.FromArgb(40, 255, 255, 255));
    private static readonly SolidColorBrush ActiveBrush = new(System.Windows.Media.Color.FromRgb(14, 165, 233)); // Sky blue

    static BooleanToActiveBrushConverter()
    {
        InactiveBrush.Freeze();
        ActiveBrush.Freeze();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (value is bool b && b) ? ActiveBrush : InactiveBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace CadenceSongSync.Views;

public partial class SplashScreenWindow : Window
{
    public SplashScreenWindow()
    {
        InitializeComponent();
        LoadSplashImage();

        // Optional convenience: click anywhere on the splash to close early if desired
        MouseLeftButtonDown += (s, e) =>
        {
            try { Close(); } catch { }
        };
    }

    private void LoadSplashImage()
    {
        // 1. Try loading from embedded WPF pack resource
        try
        {
            var uri = new Uri("pack://application:,,,/assets/images/splash_banner.png", UriKind.Absolute);
            ImgSplash.Source = new BitmapImage(uri);
            return;
        }
        catch { }

        // 2. Try loading from application directory on disk
        try
        {
            string appDir = AppContext.BaseDirectory;
            string diskPath = Path.Combine(appDir, "assets", "images", "splash_banner.png");
            if (File.Exists(diskPath))
            {
                ImgSplash.Source = new BitmapImage(new Uri(diskPath, UriKind.Absolute));
                return;
            }

            if (File.Exists(@"assets\images\splash_banner.png"))
            {
                ImgSplash.Source = new BitmapImage(new Uri(Path.GetFullPath(@"assets\images\splash_banner.png"), UriKind.Absolute));
            }
        }
        catch { }
    }

    public async Task ShowSplashAsync()
    {
        Opacity = 1.0;
        Show();

        // Display for 1.5 seconds
        await Task.Delay(1500);

        // Smooth 1.0-second opacity fade-out before closing
        var tcs = new TaskCompletionSource<bool>();
        var fadeOut = new DoubleAnimation
        {
            From = 1.0,
            To = 0.0,
            Duration = TimeSpan.FromSeconds(1.0),
            FillBehavior = FillBehavior.Stop
        };

        fadeOut.Completed += (s, e) =>
        {
            try
            {
                Close();
            }
            catch { }
            tcs.TrySetResult(true);
        };

        BeginAnimation(OpacityProperty, fadeOut);
        await tcs.Task;
    }
}

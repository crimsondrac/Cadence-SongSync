using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using CadenceSongSync.IconGen;
using CadenceSongSync.Services;
using CadenceSongSync.Views;

namespace CadenceSongSync;

public partial class App : System.Windows.Application
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();

    private const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;

    private TrayIconService? _trayIconService;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Check if automated self-verification requested
        if (e.Args.Contains("--verify") || e.Args.Contains("--test"))
        {
            if (!AttachConsole(ATTACH_PARENT_PROCESS))
            {
                AllocConsole();
            }

            bool passed = await SelfVerificationService.RunAllTestsAsync();
            Environment.Exit(passed ? 0 : 1);
            return;
        }

        // Global Exception Handling
        AppDomain.CurrentDomain.UnhandledException += (s, ev) =>
        {
            if (ev.ExceptionObject is Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CRITICAL] Unhandled Domain Exception: {ex}");
            }
        };

        DispatcherUnhandledException += (s, ev) =>
        {
            System.Diagnostics.Debug.WriteLine($"[CRITICAL] Dispatcher Exception: {ev.Exception}");
            ev.Handled = true; // Prevent app crashing from UI exceptions
        };

        // Ensure multi-size app icon exists on disk
        try
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory, "assets", "images", "app_icon.ico");
            if (!File.Exists(iconPath))
            {
                IconBuilder.GenerateAppIcon(iconPath);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to generate icon: {ex.Message}");
        }

        // Initialize state & tray service
        var appState = AppStateService.Instance;

        // Synchronize Windows startup registration with user setting
        if (appState.Settings.StartWithWindows)
        {
            appState.SettingsService.ApplyStartupRegistration(true);
        }

        // Launch Splash Screen unless "Hide splash banner on startup" is enabled
        if (!appState.Settings.HideSplashBannerOnStartup)
        {
            var splash = new SplashScreenWindow();
            _ = splash.ShowSplashAsync();
        }

        _trayIconService = new TrayIconService();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIconService?.Dispose();
        AppStateService.Instance.SaveSettings();
        base.OnExit(e);
    }
}

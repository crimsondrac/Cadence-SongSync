using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using CadenceSongSync.Models;
using CadenceSongSync.Views;

namespace CadenceSongSync.Services;

public class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _contextMenu;
    private readonly AppStateService _appState;
    private TrayPopupPlayerWindow? _popupWindow;

    private ToolStripMenuItem _playItem = null!;
    private ToolStripMenuItem _nextTrackItem = null!;
    private ToolStripMenuItem _prevTrackItem = null!;
    private ToolStripMenuItem _pauseItem = null!;
    private ToolStripMenuItem _presetsSubMenu = null!;

    public TrayIconService()
    {
        _appState = AppStateService.Instance;

        _contextMenu = new ContextMenuStrip();
        BuildContextMenu();

        _notifyIcon = new NotifyIcon
        {
            Text = "Cadence SongSync",
            Icon = LoadAppIcon(),
            Visible = true,
            ContextMenuStrip = _contextMenu
        };

        _notifyIcon.MouseClick += OnTrayIconMouseClick;
        _contextMenu.Opening += OnContextMenuOpening;

        if (!_appState.Settings.HideSplashBannerOnStartup)
        {
            _notifyIcon.ShowBalloonTip(2000, "Cadence SongSync", "SongSync is active in the Windows system tray.", ToolTipIcon.Info);
        }

        // Keep popup window reference ready
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            _popupWindow = new TrayPopupPlayerWindow();
        });
    }

    private void BuildContextMenu()
    {
        _playItem = new ToolStripMenuItem("Play", null, (s, e) => _appState.AudioPlayer.Play());
        _nextTrackItem = new ToolStripMenuItem("Next Track", null, (s, e) => _appState.AudioPlayer.NextTrack());
        _prevTrackItem = new ToolStripMenuItem("Previous Track", null, (s, e) => _appState.AudioPlayer.PreviousTrack());
        _pauseItem = new ToolStripMenuItem("Pause", null, (s, e) => _appState.AudioPlayer.Pause());

        var loadPlaylistItem = new ToolStripMenuItem("Load Playlist...", null, (s, e) => _appState.OpenLoadPlaylistDialog());
        var editPlaylistItem = new ToolStripMenuItem("Edit Playlist...", null, (s, e) => _appState.ShowEditPlaylistWindow());
        var syncPlaylistItem = new ToolStripMenuItem("Sync Playlist...", null, (s, e) => _appState.TriggerGenericCopyToDrive());

        _presetsSubMenu = new ToolStripMenuItem("Presets");
        var settingsItem = new ToolStripMenuItem("Settings...", null, (s, e) => _appState.ShowSettingsWindow());
        var aboutItem = new ToolStripMenuItem("About...", null, (s, e) => _appState.ShowAboutWindow());
        var exitItem = new ToolStripMenuItem("Exit", null, (s, e) =>
        {
            _notifyIcon.Visible = false;
            _appState.SaveSettings();
            System.Windows.Application.Current.Shutdown();
        });

        _contextMenu.Items.Add(_playItem);
        _contextMenu.Items.Add(_nextTrackItem);
        _contextMenu.Items.Add(_prevTrackItem);
        _contextMenu.Items.Add(_pauseItem);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add(loadPlaylistItem);
        _contextMenu.Items.Add(editPlaylistItem);
        _contextMenu.Items.Add(syncPlaylistItem);
        _contextMenu.Items.Add(_presetsSubMenu);
        _contextMenu.Items.Add(settingsItem);
        _contextMenu.Items.Add(aboutItem);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add(exitItem);
    }

    private void OnContextMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _playItem.Enabled = !_appState.AudioPlayer.IsPlaying && _appState.AudioPlayer.HasPlaylist;
        _pauseItem.Enabled = _appState.AudioPlayer.IsPlaying;
        _nextTrackItem.Enabled = _appState.AudioPlayer.HasPlaylist;
        _prevTrackItem.Enabled = _appState.AudioPlayer.HasPlaylist;

        // Rebuild Presets dynamic submenu
        _presetsSubMenu.DropDownItems.Clear();

        var presets = _appState.Settings.Presets;
        if (presets.Count > 0)
        {
            foreach (var preset in presets)
            {
                var item = new ToolStripMenuItem(preset.Name, null, (s, ev) =>
                {
                    _appState.TriggerPresetSync(preset);
                })
                {
                    ToolTipText = $"{preset.PlaylistFileName} -> {preset.DestinationDisplay}"
                };
                _presetsSubMenu.DropDownItems.Add(item);
            }
            _presetsSubMenu.DropDownItems.Add(new ToolStripSeparator());
        }

        var manageItem = new ToolStripMenuItem("Manage Presets...", null, (s, ev) =>
        {
            _appState.ShowManagePresetsWindow();
        });
        _presetsSubMenu.DropDownItems.Add(manageItem);
    }

    private void OnTrayIconMouseClick(object? sender, System.Windows.Forms.MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (_popupWindow == null || !_popupWindow.IsLoaded)
                {
                    _popupWindow = new TrayPopupPlayerWindow();
                }

                if (_popupWindow.IsVisible)
                {
                    _popupWindow.Hide();
                }
                else
                {
                    _popupWindow.ShowAndPositionAtTray();
                }
            });
        }
    }

    public static Icon LoadAppIcon()
    {
        // 1. Try loading from embedded WPF pack resource
        try
        {
            var uri = new Uri("pack://application:,,,/assets/images/app_icon.ico", UriKind.Absolute);
            var streamInfo = System.Windows.Application.GetResourceStream(uri);
            if (streamInfo != null)
            {
                using var stream = streamInfo.Stream;
                return new Icon(stream);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load icon from WPF resources: {ex.Message}");
        }

        // 2. Try loading from output or workspace directory
        try
        {
            string appDir = AppContext.BaseDirectory;
            string iconPath = Path.Combine(appDir, "assets", "images", "app_icon.ico");
            if (File.Exists(iconPath))
            {
                return new Icon(iconPath);
            }

            if (File.Exists(@"assets\images\app_icon.ico"))
            {
                return new Icon(@"assets\images\app_icon.ico");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load icon from file path: {ex.Message}");
        }

        // 3. Fallback
        return GenerateFallbackTrayIcon();
    }

    public static Icon GenerateFallbackTrayIcon()
    {
        const int size = 32;
        using var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        // Background rounded circle
        using var brush = new LinearGradientBrush(
            new System.Drawing.Point(0, 0),
            new System.Drawing.Point(size, size),
            System.Drawing.Color.FromArgb(14, 165, 233),   // Sky Blue
            System.Drawing.Color.FromArgb(124, 58, 237)   // Violet
        );
        g.FillEllipse(brush, 1, 1, size - 3, size - 3);

        // Music note stem & flag
        using var pen = new System.Drawing.Pen(System.Drawing.Color.White, 2.2f);
        g.DrawLine(pen, 18, 8, 18, 20);
        g.DrawLine(pen, 18, 8, 23, 11);

        // Note head
        using var headBrush = new SolidBrush(System.Drawing.Color.White);
        g.FillEllipse(headBrush, 11, 18, 8, 6);

        // Accent sync dot
        using var dotBrush = new SolidBrush(System.Drawing.Color.FromArgb(52, 211, 153)); // Emerald green
        g.FillEllipse(dotBrush, 22, 19, 5, 5);

        IntPtr hIcon = bmp.GetHicon();
        return Icon.FromHandle(hIcon);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _contextMenu.Dispose();
    }
}

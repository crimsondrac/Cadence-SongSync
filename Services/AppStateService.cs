using System;
using System.IO;
using System.Linq;
using System.Windows;
using CadenceSongSync.Models;
using CadenceSongSync.Views;

namespace CadenceSongSync.Services;

public class AppStateService
{
    private static AppStateService? _instance;
    public static AppStateService Instance => _instance ??= new AppStateService();

    public SettingsService SettingsService { get; }
    public AppSettings Settings { get; }
    public PlaylistParserService PlaylistParser { get; }
    public AudioPlayerService AudioPlayer { get; }
    public DriveSyncService DriveSync { get; }
    public string PlaylistTitle => AudioPlayer.PlaylistTitle;

    public bool IsChildDialogOpen { get; set; }

    public AppStateService()
    {
        SettingsService = new SettingsService();
        Settings = SettingsService.LoadSettings();
        PlaylistParser = new PlaylistParserService();
        AudioPlayer = new AudioPlayerService();
        DriveSync = new DriveSyncService();

        // Restore settings into player
        AudioPlayer.Volume = Settings.Volume;
        AudioPlayer.IsRandom = Settings.IsRandom;
        AudioPlayer.RepeatMode = Settings.RepeatMode;

        // Try restoring last playlist
        if (!string.IsNullOrEmpty(Settings.LastPlaylistPath) && File.Exists(Settings.LastPlaylistPath))
        {
            try
            {
                var playlist = PlaylistParser.LoadPlaylist(Settings.LastPlaylistPath);
                AudioPlayer.CurrentPlaylist = playlist;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load last playlist: {ex.Message}");
            }
        }

        // Save settings when player state changes
        AudioPlayer.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(AudioPlayer.Volume))
            {
                Settings.Volume = AudioPlayer.Volume;
            }
            else if (e.PropertyName == nameof(AudioPlayer.IsRandom))
            {
                Settings.IsRandom = AudioPlayer.IsRandom;
            }
            else if (e.PropertyName == nameof(AudioPlayer.RepeatMode))
            {
                Settings.RepeatMode = AudioPlayer.RepeatMode;
            }
            else if (e.PropertyName == nameof(AudioPlayer.CurrentPlaylist))
            {
                Settings.LastPlaylistPath = AudioPlayer.CurrentPlaylist?.FilePath;
            }
        };
    }

    public void SaveSettings()
    {
        Settings.Volume = AudioPlayer.Volume;
        Settings.IsRandom = AudioPlayer.IsRandom;
        Settings.RepeatMode = AudioPlayer.RepeatMode;
        Settings.LastPlaylistPath = AudioPlayer.CurrentPlaylist?.FilePath;
        SettingsService.SaveSettings(Settings);
    }

    public void OpenLoadPlaylistDialog()
    {
        IsChildDialogOpen = true;
        try
        {
            using var ofd = new System.Windows.Forms.OpenFileDialog
            {
                Title = "Cadence SongSync - Load Playlist",
                Filter = PlaylistParserService.SupportedFileFilter,
                CheckFileExists = true,
                Multiselect = false
            };

            if (ofd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                LoadPlaylist(ofd.FileName);
            }
        }
        finally
        {
            IsChildDialogOpen = false;
        }
    }

    public void LoadPlaylist(string filePath)
    {
        try
        {
            var playlist = PlaylistParser.LoadPlaylist(filePath);
            AudioPlayer.CurrentPlaylist = playlist;
            Settings.LastPlaylistPath = filePath;
            SaveSettings();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Failed to load playlist:\n{ex.Message}",
                "Error Loading Playlist",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    public void ShowEditPlaylistWindow(Playlist? playlistToEdit = null)
    {
        IsChildDialogOpen = true;
        try
        {
            var targetPlaylist = playlistToEdit ?? AudioPlayer.CurrentPlaylist ?? new Playlist();
            var window = new EditPlaylistWindow(targetPlaylist);
            window.ShowDialog();
        }
        finally
        {
            IsChildDialogOpen = false;
        }
    }

    public void ShowManagePresetsWindow()
    {
        IsChildDialogOpen = true;
        try
        {
            var window = new ManagePresetsWindow();
            window.ShowDialog();
        }
        finally
        {
            IsChildDialogOpen = false;
        }
    }

    public void ShowSettingsWindow()
    {
        IsChildDialogOpen = true;
        try
        {
            var window = new SettingsWindow();
            window.ShowDialog();
        }
        finally
        {
            IsChildDialogOpen = false;
        }
    }

    public void ShowAboutWindow()
    {
        IsChildDialogOpen = true;
        try
        {
            var window = new AboutWindow();
            window.ShowDialog();
        }
        finally
        {
            IsChildDialogOpen = false;
        }
    }

    public void TriggerPresetSync(Preset preset)
    {
        // 1. Check/load playlist if specified
        Playlist? playlist = null;
        if (!string.IsNullOrWhiteSpace(preset.PlaylistPath) && File.Exists(preset.PlaylistPath))
        {
            if (AudioPlayer.CurrentPlaylist?.FilePath == preset.PlaylistPath)
            {
                playlist = AudioPlayer.CurrentPlaylist;
            }
            else
            {
                try
                {
                    playlist = PlaylistParser.LoadPlaylist(preset.PlaylistPath);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show(
                        $"Failed to load preset playlist:\n{ex.Message}",
                        "Preset Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }
            }
        }
        else
        {
            playlist = AudioPlayer.CurrentPlaylist;
        }

        if (playlist == null || playlist.Tracks.Count == 0)
        {
            System.Windows.MessageBox.Show(
                "No tracks found to copy. Please load or select a valid playlist first.",
                "Copy to Drive",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        // 2. Smart resolve destination drive
        string? resolvedDest = DriveSync.SmartResolveDestination(preset, out string? updatedLetter);
        if (resolvedDest != null && updatedLetter != null && !string.Equals(updatedLetter, preset.DriveLetter, StringComparison.OrdinalIgnoreCase))
        {
            // Auto update preset letter if moved to a different drive letter with same volume label
            preset.DriveLetter = updatedLetter;
            SaveSettings();
        }

        if (resolvedDest == null)
        {
            // Prompt user with folder browser
            IsChildDialogOpen = true;
            try
            {
                using var fbd = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = $"Select destination drive/folder for preset '{preset.Name}':",
                    UseDescriptionForTitle = true,
                    ShowNewFolderButton = true
                };

                if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    string chosenPath = fbd.SelectedPath;
                    var result = System.Windows.MessageBox.Show(
                        $"Update preset '{preset.Name}' with this new destination?\n\n{chosenPath}",
                        "Update Preset Destination",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        preset.SetDestination(chosenPath);
                        SaveSettings();
                    }

                    resolvedDest = chosenPath;
                }
                else
                {
                    return; // cancelled
                }
            }
            finally
            {
                IsChildDialogOpen = false;
            }
        }

        StartCopyWorkflow(playlist, resolvedDest, preset);
    }

    public void TriggerGenericCopyToDrive()
    {
        var playlist = AudioPlayer.CurrentPlaylist;
        if (playlist == null || playlist.Tracks.Count == 0)
        {
            System.Windows.MessageBox.Show(
                "No playlist is currently loaded. Please load a playlist first.",
                "Copy to Drive",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        IsChildDialogOpen = true;
        try
        {
            using var fbd = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select target drive or music folder to sync playlist:",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };

            if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                StartCopyWorkflow(playlist, fbd.SelectedPath);
            }
        }
        finally
        {
            IsChildDialogOpen = false;
        }
    }

    public void StartCopyWorkflow(Playlist playlist, string destinationDirectory, Preset? preset = null)
    {
        IsChildDialogOpen = true;
        try
        {
            var copyWindow = new CopyProgressWindow(playlist, destinationDirectory, preset);
            copyWindow.ShowDialog();
        }
        finally
        {
            IsChildDialogOpen = false;
        }
    }
}

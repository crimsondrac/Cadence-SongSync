using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CadenceSongSync.Models;
using CadenceSongSync.Services;

namespace CadenceSongSync.Views;

public partial class CopyProgressWindow : Window
{
    private readonly AppStateService _appState;
    private readonly Playlist _playlist;
    private readonly string _destinationDirectory;
    private readonly Preset? _preset;
    private CancellationTokenSource? _cts;
    private bool _isCompleted = false;

    public CopyProgressWindow(Playlist playlist, string destinationDirectory, Preset? preset = null)
    {
        InitializeComponent();
        _appState = AppStateService.Instance;
        _playlist = playlist;
        _destinationDirectory = destinationDirectory;
        _preset = preset;

        TxtDestination.Text = $"Destination: {_destinationDirectory}";
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _cts = new CancellationTokenSource();
        var progress = new Progress<CopyProgressReport>(OnProgressReport);

        var settings = _appState.Settings;

        var filenameMode = _preset != null
            ? (_preset.PrefixTrackNumbers ? FilenameMode.PrefixTrackNumber : FilenameMode.Preserve)
            : settings.FilenameMode;

        var onFileExists = _preset != null
            ? _preset.OnFileExists
            : settings.OnFileExists;

        var playlistExportMode = settings.PlaylistExportMode;

        try
        {
            bool success = await _appState.DriveSync.CopyPlaylistAsync(
                _playlist,
                _destinationDirectory,
                filenameMode,
                settings.FolderStructureMode,
                onFileExists,
                playlistExportMode,
                settings.SanitizeFilenames,
                settings.ClearDestinationFolder,
                progress,
                _cts.Token);

            if (success && !_cts.IsCancellationRequested)
            {
                _isCompleted = true;
                TxtHeader.Text = "Copy Finished!";
                TxtStatusMessage.Text = "All tracks synced successfully.";
                TxtActionLabel.Text = "Done";
                BtnAction.Style = (Style)FindResource("SuccessBtn");
            }
        }
        catch (OperationCanceledException)
        {
            _isCompleted = true;
            TxtHeader.Text = "Copy Cancelled";
            TxtStatusMessage.Text = "Operation cancelled by user.";
            TxtActionLabel.Text = "Close";
            BtnAction.Style = (Style)FindResource("ModernBtn");
        }
        catch (Exception ex)
        {
            _isCompleted = true;
            TxtHeader.Text = "Copy Failed";
            TxtStatusMessage.Text = ex.Message;
            TxtActionLabel.Text = "Close";
            BtnAction.Style = (Style)FindResource("ModernBtn");
        }
    }

    private void OnProgressReport(CopyProgressReport report)
    {
        PbOverall.Value = Math.Clamp(report.Percent, 0, 100);
        TxtPercent.Text = $"{report.Percent:F0}%";
        TxtCurrentFile.Text = $"Copying: {report.CurrentFileName}";
        TxtTrackCount.Text = $"{report.CurrentIndex} / {report.TotalCount} tracks  ({FormatBytes(report.BytesCopied)} / {FormatBytes(report.TotalBytes)})";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }

    private void BtnAction_Click(object sender, RoutedEventArgs e)
    {
        if (_isCompleted)
        {
            Close();
        }
        else
        {
            _cts?.Cancel();
            TxtActionLabel.Text = "Cancelling...";
            BtnAction.IsEnabled = false;
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_isCompleted && _cts != null && !_cts.IsCancellationRequested)
        {
            var res = MessageBox.Show(
                "File copying is in progress. Are you sure you want to cancel?",
                "Cancel Copy",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes)
            {
                _cts.Cancel();
            }
            else
            {
                e.Cancel = true;
            }
        }
    }
}

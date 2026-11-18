using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CadenceSongSync.Models;
using CadenceSongSync.Services;

namespace CadenceSongSync.Views;

public partial class TrayPopupPlayerWindow : Window
{
    private readonly AppStateService _appState;
    private bool _isUserDraggingSlider = false;

    public TrayPopupPlayerWindow()
    {
        InitializeComponent();
        _appState = AppStateService.Instance;
        DataContext = _appState;

        _appState.AudioPlayer.PropertyChanged += AudioPlayer_PropertyChanged;
        UpdatePlayPauseState();
        UpdatePlaylistTitle();
    }

    private void AudioPlayer_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(_appState.AudioPlayer.PlaybackState) ||
            e.PropertyName == nameof(_appState.AudioPlayer.IsPlaying))
        {
            Dispatcher.Invoke(UpdatePlayPauseState);
        }
        else if (e.PropertyName == nameof(_appState.AudioPlayer.CurrentPlaylist) ||
                 e.PropertyName == nameof(_appState.AudioPlayer.PlaylistTitle))
        {
            Dispatcher.Invoke(UpdatePlaylistTitle);
        }
    }

    private void UpdatePlaylistTitle()
    {
        string title = _appState.AudioPlayer.PlaylistTitle;
        TxtPlaylistTitle.Text = string.IsNullOrWhiteSpace(title) ? "No Playlist Loaded" : title;
        TxtPlaylistTitle.ToolTip = TxtPlaylistTitle.Text;
    }

    private void UpdatePlayPauseState()
    {
        TxtPlayPauseIcon.Text = _appState.AudioPlayer.IsPlaying ? "⏸" : "▶";
    }

    public void ShowAndPositionAtTray()
    {
        if (!IsLoaded)
        {
            // Initial render to measure bounds
            Left = -10000;
            Top = -10000;
            Show();
            UpdateLayout();
        }

        var cursorPos = System.Windows.Forms.Cursor.Position;
        var workArea = SystemParameters.WorkArea;

        double winWidth = ActualWidth > 0 ? ActualWidth : Width;
        double winHeight = ActualHeight > 0 ? ActualHeight : Height;

        // Position horizontally centered near cursor, clamped to WorkArea
        double targetLeft = cursorPos.X - (winWidth / 2.0);
        if (targetLeft + winWidth > workArea.Right - 12)
        {
            targetLeft = workArea.Right - winWidth - 12;
        }
        if (targetLeft < workArea.Left + 12)
        {
            targetLeft = workArea.Left + 12;
        }

        // Position vertically: by default position cleanly above the taskbar
        double targetTop = cursorPos.Y - winHeight - 12;
        if (targetTop + winHeight > workArea.Bottom - 12)
        {
            targetTop = workArea.Bottom - winHeight - 12;
        }
        if (targetTop < workArea.Top + 12)
        {
            targetTop = workArea.Top + 12;
        }

        Left = targetLeft;
        Top = targetTop;

        Show();
        Activate();
        Focus();

        UpdatePlaylistTitle();

        // Scroll active item into view if any
        if (_appState.AudioPlayer.CurrentTrack != null)
        {
            LstTracks.ScrollIntoView(_appState.AudioPlayer.CurrentTrack);
        }
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        // Auto-close when focus is lost, except while a child dialog or file copy is active
        if (!_appState.IsChildDialogOpen && !_appState.DriveSync.IsCopying)
        {
            Hide();
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void BtnLoadPlaylist_Click(object sender, RoutedEventArgs e)
    {
        _appState.OpenLoadPlaylistDialog();
    }

    private void BtnEditPlaylist_Click(object sender, RoutedEventArgs e)
    {
        _appState.ShowEditPlaylistWindow();
    }

    private void LstTracks_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (LstTracks.SelectedItem is TrackItem selectedTrack)
        {
            _appState.AudioPlayer.PlayTrack(selectedTrack);
        }
    }

    private void BtnPlayPause_Click(object sender, RoutedEventArgs e)
    {
        _appState.AudioPlayer.TogglePlayPause();
    }

    private void BtnPrev_Click(object sender, RoutedEventArgs e)
    {
        _appState.AudioPlayer.PreviousTrack();
    }

    private void BtnNext_Click(object sender, RoutedEventArgs e)
    {
        _appState.AudioPlayer.NextTrack();
    }

    private void BtnStop_Click(object sender, RoutedEventArgs e)
    {
        _appState.AudioPlayer.Stop();
    }

    private void BtnShuffle_Click(object sender, RoutedEventArgs e)
    {
        _appState.AudioPlayer.IsRandom = !_appState.AudioPlayer.IsRandom;
    }

    private void BtnRepeat_Click(object sender, RoutedEventArgs e)
    {
        _appState.AudioPlayer.CycleRepeatMode();
    }

    private void BtnCopyToDrive_Click(object sender, RoutedEventArgs e)
    {
        _appState.TriggerGenericCopyToDrive();
    }

    private void SliderProgress_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isUserDraggingSlider = true;
    }

    private void SliderProgress_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isUserDraggingSlider)
        {
            _isUserDraggingSlider = false;
            double fraction = SliderProgress.Value;
            var totalDuration = _appState.AudioPlayer.Duration;
            if (totalDuration > TimeSpan.Zero)
            {
                var targetTime = TimeSpan.FromSeconds(fraction * totalDuration.TotalSeconds);
                _appState.AudioPlayer.Seek(targetTime);
            }
        }
    }
}

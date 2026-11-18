using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Threading;
using CadenceSongSync.Models;

namespace CadenceSongSync.Services;

public class AudioPlayerService : INotifyPropertyChanged
{
    private readonly MediaPlayer _mediaPlayer;
    private readonly DispatcherTimer _progressTimer;
    private readonly Random _random = new();

    private Playlist? _currentPlaylist;
    private int _currentTrackIndex = -1;
    private PlaybackState _playbackState = PlaybackState.Stopped;
    private double _volume = 0.8;
    private bool _isRandom = false;
    private RepeatMode _repeatMode = RepeatMode.RepeatOff;
    private TimeSpan _position = TimeSpan.Zero;
    private TimeSpan _duration = TimeSpan.Zero;

    private readonly List<int> _shufflePool = new();
    private readonly List<int> _playHistory = new();

    public AudioPlayerService()
    {
        _mediaPlayer = new MediaPlayer();
        _mediaPlayer.MediaOpened += OnMediaOpened;
        _mediaPlayer.MediaEnded += OnMediaEnded;
        _mediaPlayer.MediaFailed += OnMediaFailed;

        _progressTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _progressTimer.Tick += OnProgressTimerTick;
    }

    public Playlist? CurrentPlaylist
    {
        get => _currentPlaylist;
        set
        {
            if (_currentPlaylist != value)
            {
                if (_currentPlaylist != null)
                {
                    foreach (var t in _currentPlaylist.Tracks)
                    {
                        t.IsActive = false;
                    }
                }

                _currentPlaylist = value;
                _currentTrackIndex = -1;
                ResetShufflePool();
                Stop();
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasPlaylist));
                OnPropertyChanged(nameof(PlaylistTitle));
            }
        }
    }

    public bool HasPlaylist => CurrentPlaylist != null && CurrentPlaylist.Tracks.Count > 0;
    public string PlaylistTitle => CurrentPlaylist?.Title ?? "No Playlist Loaded";

    public int CurrentTrackIndex
    {
        get => _currentTrackIndex;
        private set
        {
            if (_currentTrackIndex != value)
            {
                _currentTrackIndex = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentTrack));
                UpdateTrackActiveStates();
            }
        }
    }

    public TrackItem? CurrentTrack =>
        CurrentPlaylist != null && CurrentTrackIndex >= 0 && CurrentTrackIndex < CurrentPlaylist.Tracks.Count
            ? CurrentPlaylist.Tracks[CurrentTrackIndex]
            : null;

    public PlaybackState PlaybackState
    {
        get => _playbackState;
        private set
        {
            if (_playbackState != value)
            {
                _playbackState = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsPlaying));
                OnPropertyChanged(nameof(IsPaused));
                OnPropertyChanged(nameof(IsStopped));
            }
        }
    }

    public bool IsPlaying => PlaybackState == PlaybackState.Playing;
    public bool IsPaused => PlaybackState == PlaybackState.Paused;
    public bool IsStopped => PlaybackState == PlaybackState.Stopped;

    public double Volume
    {
        get => _volume;
        set
        {
            double clamped = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(_volume - clamped) > 0.001)
            {
                _volume = clamped;
                _mediaPlayer.Volume = _volume;
                OnPropertyChanged();
                OnPropertyChanged(nameof(VolumePercent));
            }
        }
    }

    public int VolumePercent => (int)Math.Round(Volume * 100);

    public bool IsRandom
    {
        get => _isRandom;
        set
        {
            if (_isRandom != value)
            {
                _isRandom = value;
                ResetShufflePool();
                OnPropertyChanged();
            }
        }
    }

    public RepeatMode RepeatMode
    {
        get => _repeatMode;
        set
        {
            if (_repeatMode != value)
            {
                _repeatMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RepeatModeTooltip));
            }
        }
    }

    public string RepeatModeTooltip => RepeatMode switch
    {
        RepeatMode.RepeatOff => "Repeat: Off",
        RepeatMode.RepeatAll => "Repeat: All Tracks",
        RepeatMode.RepeatTrack => "Repeat: Current Track",
        _ => "Repeat"
    };

    public TimeSpan Position
    {
        get => _position;
        set
        {
            if (_position != value)
            {
                _position = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PositionString));
                OnPropertyChanged(nameof(ProgressFraction));
            }
        }
    }

    public TimeSpan Duration
    {
        get => _duration;
        private set
        {
            if (_duration != value)
            {
                _duration = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DurationString));
                OnPropertyChanged(nameof(ProgressFraction));
            }
        }
    }

    public string PositionString => Position.ToString(@"m\:ss");
    public string DurationString => Duration > TimeSpan.Zero ? Duration.ToString(@"m\:ss") : "--:--";

    public double ProgressFraction => Duration > TimeSpan.Zero ? Math.Clamp(Position.TotalSeconds / Duration.TotalSeconds, 0.0, 1.0) : 0.0;

    public void CycleRepeatMode()
    {
        RepeatMode = RepeatMode switch
        {
            RepeatMode.RepeatOff => RepeatMode.RepeatAll,
            RepeatMode.RepeatAll => RepeatMode.RepeatTrack,
            RepeatMode.RepeatTrack => RepeatMode.RepeatOff,
            _ => RepeatMode.RepeatOff
        };
    }

    public void TogglePlayPause()
    {
        if (PlaybackState == PlaybackState.Playing)
        {
            Pause();
        }
        else if (PlaybackState == PlaybackState.Paused)
        {
            Resume();
        }
        else
        {
            if (CurrentTrackIndex >= 0)
            {
                PlayTrackIndex(CurrentTrackIndex);
            }
            else
            {
                PlayFirstAvailableTrack();
            }
        }
    }

    public void Play()
    {
        if (PlaybackState == PlaybackState.Paused)
        {
            Resume();
        }
        else if (CurrentTrackIndex >= 0)
        {
            PlayTrackIndex(CurrentTrackIndex);
        }
        else
        {
            PlayFirstAvailableTrack();
        }
    }

    public void Pause()
    {
        if (PlaybackState == PlaybackState.Playing)
        {
            _mediaPlayer.Pause();
            _progressTimer.Stop();
            PlaybackState = PlaybackState.Paused;
        }
    }

    public void Resume()
    {
        if (PlaybackState == PlaybackState.Paused)
        {
            _mediaPlayer.Play();
            _progressTimer.Start();
            PlaybackState = PlaybackState.Playing;
        }
    }

    public void Stop()
    {
        _progressTimer.Stop();
        try { _mediaPlayer.Stop(); } catch { }
        Position = TimeSpan.Zero;
        Duration = TimeSpan.Zero;
        PlaybackState = PlaybackState.Stopped;
    }

    public void Seek(TimeSpan target)
    {
        if (_mediaPlayer.NaturalDuration.HasTimeSpan)
        {
            _mediaPlayer.Position = target;
            Position = target;
        }
    }

    public void PlayTrack(TrackItem track)
    {
        if (CurrentPlaylist == null) return;
        int index = CurrentPlaylist.Tracks.IndexOf(track);
        if (index >= 0)
        {
            PlayTrackIndex(index);
        }
    }

    public void PlayTrackIndex(int index)
    {
        if (CurrentPlaylist == null || CurrentPlaylist.Tracks.Count == 0) return;
        if (index < 0 || index >= CurrentPlaylist.Tracks.Count) return;

        var track = CurrentPlaylist.Tracks[index];
        if (!File.Exists(track.FilePath))
        {
            track.IsMissing = true;
            // Advance to next valid track
            CurrentTrackIndex = index;
            NextTrack(isAutoAdvance: true);
            return;
        }

        CurrentTrackIndex = index;
        _playHistory.Add(index);
        if (_shufflePool.Contains(index))
        {
            _shufflePool.Remove(index);
        }

        try
        {
            _mediaPlayer.Close();
            _mediaPlayer.Open(new Uri(track.FilePath, UriKind.Absolute));
            _mediaPlayer.Volume = Volume;
            _mediaPlayer.Play();
            PlaybackState = PlaybackState.Playing;
            _progressTimer.Start();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to play {track.FilePath}: {ex.Message}");
            track.IsMissing = true;
            NextTrack(isAutoAdvance: true);
        }
    }

    public void NextTrack(bool isAutoAdvance = false)
    {
        if (CurrentPlaylist == null || CurrentPlaylist.Tracks.Count == 0) return;

        if (IsRandom)
        {
            AdvanceRandom(isAutoAdvance);
        }
        else
        {
            AdvanceSequential(isAutoAdvance);
        }
    }

    public void PreviousTrack()
    {
        if (CurrentPlaylist == null || CurrentPlaylist.Tracks.Count == 0) return;

        // If played more than 3 seconds, rewind current track
        if (Position.TotalSeconds > 3.0)
        {
            Seek(TimeSpan.Zero);
            return;
        }

        // Otherwise go to previous in history or previous sequential index
        if (_playHistory.Count > 1)
        {
            _playHistory.RemoveAt(_playHistory.Count - 1); // remove current
            int prevIdx = _playHistory[^1];
            _playHistory.RemoveAt(_playHistory.Count - 1); // will be re-added in PlayTrackIndex
            PlayTrackIndex(prevIdx);
        }
        else
        {
            int targetIdx = CurrentTrackIndex - 1;
            if (targetIdx < 0) targetIdx = CurrentPlaylist.Tracks.Count - 1;
            PlayTrackIndex(targetIdx);
        }
    }

    private void AdvanceSequential(bool isAutoAdvance)
    {
        if (CurrentPlaylist == null || CurrentPlaylist.Tracks.Count == 0) return;

        int nextIndex = CurrentTrackIndex + 1;
        if (nextIndex >= CurrentPlaylist.Tracks.Count)
        {
            if (RepeatMode == RepeatMode.RepeatAll)
            {
                nextIndex = 0;
            }
            else if (isAutoAdvance && RepeatMode == RepeatMode.RepeatOff)
            {
                Stop();
                return;
            }
            else
            {
                nextIndex = 0;
            }
        }

        PlayTrackIndex(nextIndex);
    }

    private void AdvanceRandom(bool isAutoAdvance)
    {
        if (CurrentPlaylist == null || CurrentPlaylist.Tracks.Count == 0) return;

        if (_shufflePool.Count == 0)
        {
            // Shuffle cycle finished!
            if (isAutoAdvance && RepeatMode == RepeatMode.RepeatOff)
            {
                Stop();
                return;
            }

            ResetShufflePool();
            // Don't replay the exact same track immediately if playlist has multiple tracks
            if (_shufflePool.Count > 1 && CurrentTrackIndex >= 0)
            {
                _shufflePool.Remove(CurrentTrackIndex);
            }
        }

        if (_shufflePool.Count > 0)
        {
            int randIdx = _random.Next(_shufflePool.Count);
            int selectedTrackIndex = _shufflePool[randIdx];
            _shufflePool.RemoveAt(randIdx);
            PlayTrackIndex(selectedTrackIndex);
        }
        else
        {
            Stop();
        }
    }

    private void ResetShufflePool()
    {
        _shufflePool.Clear();
        if (CurrentPlaylist == null) return;

        for (int i = 0; i < CurrentPlaylist.Tracks.Count; i++)
        {
            _shufflePool.Add(i);
        }
    }

    private void PlayFirstAvailableTrack()
    {
        if (CurrentPlaylist == null || CurrentPlaylist.Tracks.Count == 0) return;

        if (IsRandom)
        {
            ResetShufflePool();
            AdvanceRandom(false);
        }
        else
        {
            PlayTrackIndex(0);
        }
    }

    private void UpdateTrackActiveStates()
    {
        if (CurrentPlaylist == null) return;

        for (int i = 0; i < CurrentPlaylist.Tracks.Count; i++)
        {
            CurrentPlaylist.Tracks[i].IsActive = (i == _currentTrackIndex);
        }
    }

    private void OnProgressTimerTick(object? sender, EventArgs e)
    {
        if (_mediaPlayer.NaturalDuration.HasTimeSpan)
        {
            Duration = _mediaPlayer.NaturalDuration.TimeSpan;
            Position = _mediaPlayer.Position;

            // Also update the track's duration if not set
            if (CurrentTrack != null && !CurrentTrack.Duration.HasValue && Duration > TimeSpan.Zero)
            {
                CurrentTrack.Duration = Duration;
            }
        }
    }

    private void OnMediaOpened(object? sender, EventArgs e)
    {
        if (_mediaPlayer.NaturalDuration.HasTimeSpan)
        {
            Duration = _mediaPlayer.NaturalDuration.TimeSpan;
            if (CurrentTrack != null)
            {
                CurrentTrack.Duration = Duration;
            }
        }
    }

    private void OnMediaEnded(object? sender, EventArgs e)
    {
        if (RepeatMode == RepeatMode.RepeatTrack)
        {
            Seek(TimeSpan.Zero);
            _mediaPlayer.Play();
        }
        else
        {
            NextTrack(isAutoAdvance: true);
        }
    }

    private void OnMediaFailed(object? sender, ExceptionEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"Media failed: {e.ErrorException?.Message}");
        if (CurrentTrack != null)
        {
            CurrentTrack.IsMissing = true;
        }
        NextTrack(isAutoAdvance: true);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace CadenceSongSync.Models;

public class Playlist : INotifyPropertyChanged
{
    private string _title = "Untitled Playlist";
    private string? _filePath;
    private string _format = ".m3u8";
    private bool _isDirty;

    public Playlist()
    {
        Tracks = new ObservableCollection<TrackItem>();
        Tracks.CollectionChanged += OnTracksCollectionChanged;
    }

    public string Title
    {
        get => _title;
        set
        {
            if (_title != value)
            {
                _title = value;
                OnPropertyChanged();
            }
        }
    }

    public string? FilePath
    {
        get => _filePath;
        set
        {
            if (_filePath != value)
            {
                _filePath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FileName));
                if (!string.IsNullOrEmpty(value))
                {
                    Format = Path.GetExtension(value).ToLowerInvariant();
                    if (string.IsNullOrWhiteSpace(_title) || _title == "Untitled Playlist")
                    {
                        Title = Path.GetFileNameWithoutExtension(value);
                    }
                }
            }
        }
    }

    public string FileName => string.IsNullOrEmpty(FilePath) ? "Untitled Playlist" : Path.GetFileName(FilePath);

    public string Format
    {
        get => _format;
        set
        {
            if (_format != value)
            {
                _format = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsDirty
    {
        get => _isDirty;
        set
        {
            if (_isDirty != value)
            {
                _isDirty = value;
                OnPropertyChanged();
            }
        }
    }

    public ObservableCollection<TrackItem> Tracks { get; }

    public Playlist Clone()
    {
        var clone = new Playlist
        {
            _title = this.Title,
            _filePath = this.FilePath,
            _format = this.Format
        };

        foreach (var track in this.Tracks)
        {
            clone.Tracks.Add(new TrackItem
            {
                FilePath = track.FilePath,
                Title = track.Title,
                Artist = track.Artist,
                Duration = track.Duration,
                TrackNumber = track.TrackNumber,
                IsMissing = track.IsMissing,
                IsActive = false
            });
        }

        clone.RenumberTracks();
        clone.IsDirty = false;
        return clone;
    }

    public void RenumberTracks()
    {
        for (int i = 0; i < Tracks.Count; i++)
        {
            Tracks[i].TrackNumber = i + 1;
        }
    }

    private void OnTracksCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RenumberTracks();
        IsDirty = true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

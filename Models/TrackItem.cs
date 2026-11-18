using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace CadenceSongSync.Models;

public class TrackItem : INotifyPropertyChanged
{
    private string _filePath = string.Empty;
    private string _title = string.Empty;
    private string _artist = string.Empty;
    private TimeSpan? _duration;
    private bool _isMissing;
    private int _trackNumber;
    private bool _isActive;

    public string FilePath
    {
        get => _filePath;
        set
        {
            if (_filePath != value)
            {
                _filePath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FileName));
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(DirectoryName));
            }
        }
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
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public string Artist
    {
        get => _artist;
        set
        {
            if (_artist != value)
            {
                _artist = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public TimeSpan? Duration
    {
        get => _duration;
        set
        {
            if (_duration != value)
            {
                _duration = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DurationString));
            }
        }
    }

    public bool IsMissing
    {
        get => _isMissing;
        set
        {
            if (_isMissing != value)
            {
                _isMissing = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusTooltip));
            }
        }
    }

    public int TrackNumber
    {
        get => _trackNumber;
        set
        {
            if (_trackNumber != value)
            {
                _trackNumber = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TrackNumberString));
            }
        }
    }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive != value)
            {
                _isActive = value;
                OnPropertyChanged();
            }
        }
    }

    public string FileName => string.IsNullOrEmpty(FilePath) ? "(Untitled)" : Path.GetFileName(FilePath);

    public string DirectoryName
    {
        get
        {
            try
            {
                return string.IsNullOrEmpty(FilePath) ? string.Empty : (Path.GetDirectoryName(FilePath) ?? string.Empty);
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Title))
            {
                if (!string.IsNullOrWhiteSpace(Artist))
                    return $"{Artist} - {Title}";
                return Title;
            }
            return FileName;
        }
    }

    public string TrackNumberString => TrackNumber > 0 ? $"{TrackNumber:D2}" : "--";

    public string DurationString => Duration.HasValue ? Duration.Value.ToString(@"m\:ss") : "--:--";

    public string StatusTooltip => IsMissing ? $"File not found at: {FilePath}" : FilePath;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

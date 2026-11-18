using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace CadenceSongSync.Models;

public class Preset : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString();
    private string _name = "New Preset";
    private string _playlistPath = string.Empty;
    private string _driveLetter = string.Empty;
    private string _volumeLabel = string.Empty;
    private string _relativeSubfolder = string.Empty;
    private ExistingFileAction _onFileExists = ExistingFileAction.Skip;
    private bool _prefixTrackNumbers = false;

    public string Id
    {
        get => _id;
        set { _id = value; OnPropertyChanged(); }
    }

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    public string PlaylistPath
    {
        get => _playlistPath;
        set
        {
            _playlistPath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PlaylistFileName));
        }
    }

    public string DriveLetter
    {
        get => _driveLetter;
        set
        {
            _driveLetter = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DestinationDisplay));
        }
    }

    public string VolumeLabel
    {
        get => _volumeLabel;
        set
        {
            _volumeLabel = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DestinationDisplay));
        }
    }

    public string RelativeSubfolder
    {
        get => _relativeSubfolder;
        set
        {
            _relativeSubfolder = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DestinationDisplay));
        }
    }

    public ExistingFileAction OnFileExists
    {
        get => _onFileExists;
        set
        {
            _onFileExists = value;
            OnPropertyChanged();
        }
    }

    public bool PrefixTrackNumbers
    {
        get => _prefixTrackNumbers;
        set
        {
            _prefixTrackNumbers = value;
            OnPropertyChanged();
        }
    }

    public string PlaylistFileName => string.IsNullOrEmpty(PlaylistPath) ? "(None)" : Path.GetFileName(PlaylistPath);

    public string DestinationDisplay
    {
        get
        {
            string labelPart = string.IsNullOrWhiteSpace(VolumeLabel) ? "" : $" [{VolumeLabel}]";
            string folderPart = string.IsNullOrWhiteSpace(RelativeSubfolder) ? "" : $"\\{RelativeSubfolder.TrimStart('\\')}";
            return $"{DriveLetter}{labelPart}{folderPart}";
        }
    }

    public void SetDestination(string fullPath)
    {
        try
        {
            string root = Path.GetPathRoot(fullPath) ?? fullPath;
            DriveLetter = root;
            var driveInfo = new DriveInfo(root);
            if (driveInfo.IsReady)
            {
                VolumeLabel = driveInfo.VolumeLabel;
            }
            else
            {
                VolumeLabel = string.Empty;
            }

            string relative = fullPath.Substring(root.Length).TrimStart('\\', '/');
            RelativeSubfolder = relative;
        }
        catch
        {
            DriveLetter = fullPath;
            VolumeLabel = string.Empty;
            RelativeSubfolder = string.Empty;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

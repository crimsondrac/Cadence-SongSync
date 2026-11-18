using System.Collections.Generic;

namespace CadenceSongSync.Models;

public class AppSettings
{
    public string? LastPlaylistPath { get; set; }
    public double Volume { get; set; } = 0.8;
    public bool IsRandom { get; set; } = false;
    public RepeatMode RepeatMode { get; set; } = RepeatMode.RepeatOff;
    public FilenameMode FilenameMode { get; set; } = FilenameMode.Preserve;
    public FolderStructureMode FolderStructureMode { get; set; } = FolderStructureMode.PreserveImmediateFolder;
    public ExistingFileAction OnFileExists { get; set; } = ExistingFileAction.Skip;
    public PlaylistExportMode PlaylistExportMode { get; set; } = PlaylistExportMode.GenerateM3U8;
    public bool StartWithWindows { get; set; } = true;
    public bool HideSplashBannerOnStartup { get; set; } = false;
    public bool PlaylistEditorOpenLastFolder { get; set; } = true;
    public string? PlaylistEditorCustomStartingFolder { get; set; }
    public string? LastBrowsedFolder { get; set; }
    public bool ClearDestinationFolder { get; set; } = false;
    public bool SanitizeFilenames { get; set; } = true;
    public bool CopyM3U8ToDestination { get; set; } = true; // Kept for backward compatibility
    public List<Preset> Presets { get; set; } = new();
}

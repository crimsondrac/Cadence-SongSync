namespace CadenceSongSync.Models;

public enum RepeatMode
{
    RepeatOff = 0,
    RepeatAll = 1,
    RepeatTrack = 2
}

public enum FilenameMode
{
    Preserve = 0,
    PrefixTrackNumber = 1
}

public enum FolderStructureMode
{
    PreserveImmediateFolder = 0, // e.g. <destination>\My Songs\Song.mp3
    PreserveRelativePath = 1,    // e.g. <destination>\Artist\Album\Song.mp3
    Flat = 2                     // <destination>\Song.mp3
}

public enum ExistingFileAction
{
    Skip = 0,
    Overwrite = 1
}

public enum PlaylistExportMode
{
    GenerateM3U8 = 0,     // Generate new synced .m3u8 with relative paths
    CopyOriginalAsIs = 1, // Copy original playlist file as-is
    None = 2              // Do not export playlist
}

public enum PlaybackState
{
    Stopped = 0,
    Playing = 1,
    Paused = 2
}

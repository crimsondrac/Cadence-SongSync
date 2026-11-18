using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CadenceSongSync.Models;

namespace CadenceSongSync.Services;

public class CopyProgressReport
{
    public int CurrentIndex { get; set; }
    public int TotalCount { get; set; }
    public string CurrentFileName { get; set; } = string.Empty;
    public double Percent { get; set; }
    public long BytesCopied { get; set; }
    public long TotalBytes { get; set; }
    public bool IsComplete { get; set; }
    public bool IsCancelled { get; set; }
    public string? ErrorMessage { get; set; }
}

public class DriveSyncService
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public bool IsCopying { get; private set; }

    public string? SmartResolveDestination(Preset preset, out string? updatedDriveLetter)
    {
        updatedDriveLetter = null;

        // 1. Check if DriveLetter exists and matches VolumeLabel
        if (!string.IsNullOrWhiteSpace(preset.DriveLetter))
        {
            try
            {
                var drive = new DriveInfo(preset.DriveLetter);
                if (drive.IsReady)
                {
                    if (string.IsNullOrWhiteSpace(preset.VolumeLabel) ||
                        string.Equals(drive.VolumeLabel, preset.VolumeLabel, StringComparison.OrdinalIgnoreCase))
                    {
                        updatedDriveLetter = drive.RootDirectory.FullName;
                        return Path.Combine(drive.RootDirectory.FullName, preset.RelativeSubfolder ?? string.Empty);
                    }
                }
            }
            catch { }
        }

        // 2. If not found at DriveLetter, scan all ready drives for VolumeLabel
        if (!string.IsNullOrWhiteSpace(preset.VolumeLabel))
        {
            try
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (drive.IsReady && string.Equals(drive.VolumeLabel, preset.VolumeLabel, StringComparison.OrdinalIgnoreCase))
                        {
                            updatedDriveLetter = drive.RootDirectory.FullName;
                            return Path.Combine(drive.RootDirectory.FullName, preset.RelativeSubfolder ?? string.Empty);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        // 3. Not found, caller must prompt user
        return null;
    }

    public async Task<bool> CopyPlaylistAsync(
        Playlist playlist,
        string destinationDirectory,
        FilenameMode filenameMode,
        FolderStructureMode folderMode,
        ExistingFileAction onFileExists,
        PlaylistExportMode playlistExportMode,
        bool sanitizeNames,
        bool clearDestination,
        IProgress<CopyProgressReport> progress,
        CancellationToken cancellationToken)
    {
        IsCopying = true;
        try
        {
            return await Task.Run(() => DoCopy(
                playlist,
                destinationDirectory,
                filenameMode,
                folderMode,
                onFileExists,
                playlistExportMode,
                sanitizeNames,
                clearDestination,
                progress,
                cancellationToken));
        }
        finally
        {
            IsCopying = false;
        }
    }

    // Overload for backward compatibility
    public Task<bool> CopyPlaylistAsync(
        Playlist playlist,
        string destinationDirectory,
        FilenameMode filenameMode,
        FolderStructureMode folderMode,
        bool sanitizeNames,
        bool clearDestination,
        bool copyM3u8,
        IProgress<CopyProgressReport> progress,
        CancellationToken cancellationToken)
    {
        return CopyPlaylistAsync(
            playlist,
            destinationDirectory,
            filenameMode,
            folderMode,
            ExistingFileAction.Skip,
            copyM3u8 ? PlaylistExportMode.GenerateM3U8 : PlaylistExportMode.None,
            sanitizeNames,
            clearDestination,
            progress,
            cancellationToken);
    }

    private bool DoCopy(
        Playlist playlist,
        string destinationDirectory,
        FilenameMode filenameMode,
        FolderStructureMode folderMode,
        ExistingFileAction onFileExists,
        PlaylistExportMode playlistExportMode,
        bool sanitizeNames,
        bool clearDestination,
        IProgress<CopyProgressReport> progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destinationDirectory);

        if (clearDestination)
        {
            try
            {
                var dirInfo = new DirectoryInfo(destinationDirectory);
                foreach (var file in dirInfo.GetFiles())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try { file.Delete(); } catch { }
                }
                foreach (var subDir in dirInfo.GetDirectories())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try { subDir.Delete(true); } catch { }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to clear destination: {ex.Message}");
            }
        }

        var validTracks = playlist.Tracks.Where(t => !t.IsMissing && File.Exists(t.FilePath)).ToList();
        int totalCount = validTracks.Count;
        if (totalCount == 0)
        {
            progress.Report(new CopyProgressReport
            {
                CurrentIndex = 0,
                TotalCount = 0,
                Percent = 100,
                IsComplete = true
            });
            return true;
        }

        long totalBytes = 0;
        foreach (var t in validTracks)
        {
            try { totalBytes += new FileInfo(t.FilePath).Length; } catch { }
        }

        long cumulativeBytesCopied = 0;
        // Track unique filenames written during the current sync session only
        var sessionWrittenFilesByFolder = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var copiedFilesForM3U = new List<(TrackItem track, string relativePath)>();

        int padDigits = Math.Max(2, totalCount.ToString().Length);
        byte[] buffer = new byte[128 * 1024]; // 128 KB buffer

        for (int i = 0; i < totalCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var track = validTracks[i];
            string sourcePath = track.FilePath;
            string originalExt = Path.GetExtension(sourcePath);
            string baseName = Path.GetFileNameWithoutExtension(sourcePath);

            long trackSize = 0;
            try { trackSize = new FileInfo(sourcePath).Length; } catch { }

            string targetFileName;
            if (filenameMode == FilenameMode.PrefixTrackNumber)
            {
                string prefix = (i + 1).ToString($"D{padDigits}");
                targetFileName = $"{prefix} - {baseName}{originalExt}";
            }
            else
            {
                targetFileName = $"{baseName}{originalExt}";
            }

            if (sanitizeNames)
            {
                targetFileName = SanitizeFileName(targetFileName);
            }

            // Determine subfolder based on folderStructureMode
            string subFolder = GetDestinationSubFolder(sourcePath, playlist, folderMode, sanitizeNames);
            string targetFolder = string.IsNullOrEmpty(subFolder)
                ? destinationDirectory
                : Path.Combine(destinationDirectory, subFolder);

            if (!Directory.Exists(targetFolder))
            {
                Directory.CreateDirectory(targetFolder);
            }

            // Avoid collisions between two DIFFERENT tracks in the same playlist
            if (!sessionWrittenFilesByFolder.TryGetValue(targetFolder, out var sessionNames))
            {
                sessionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                sessionWrittenFilesByFolder[targetFolder] = sessionNames;
            }

            if (sessionNames.Contains(targetFileName))
            {
                targetFileName = EnsureUniqueFileName(targetFileName, sessionNames);
            }
            sessionNames.Add(targetFileName);

            string destinationPath = Path.Combine(targetFolder, targetFileName);

            // Relative path from destination root (for m3u8 playlist and progress readout)
            string relativeDisplayPath = string.IsNullOrEmpty(subFolder)
                ? targetFileName
                : Path.Combine(subFolder, targetFileName);

            copiedFilesForM3U.Add((track, relativeDisplayPath));

            // Check if file already exists in destination
            if (File.Exists(destinationPath) && onFileExists == ExistingFileAction.Skip)
            {
                // Skip copying file! Never create duplicate copies (Song (1).mp3)
                cumulativeBytesCopied += trackSize;
                progress.Report(new CopyProgressReport
                {
                    CurrentIndex = i + 1,
                    TotalCount = totalCount,
                    CurrentFileName = $"{relativeDisplayPath} (Skipped - already exists)",
                    Percent = totalBytes > 0 ? Math.Min(100.0, (double)cumulativeBytesCopied / totalBytes * 100.0) : ((double)(i + 1) / totalCount * 100.0),
                    BytesCopied = cumulativeBytesCopied,
                    TotalBytes = totalBytes
                });
                continue;
            }

            progress.Report(new CopyProgressReport
            {
                CurrentIndex = i + 1,
                TotalCount = totalCount,
                CurrentFileName = relativeDisplayPath,
                Percent = totalBytes > 0 ? (double)cumulativeBytesCopied / totalBytes * 100.0 : ((double)i / totalCount * 100.0),
                BytesCopied = cumulativeBytesCopied,
                TotalBytes = totalBytes
            });

            // Perform stream copy for responsive cancellation and byte progress
            using (var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var destStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                int bytesRead;
                while ((bytesRead = sourceStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    destStream.Write(buffer, 0, bytesRead);
                    cumulativeBytesCopied += bytesRead;

                    progress.Report(new CopyProgressReport
                    {
                        CurrentIndex = i + 1,
                        TotalCount = totalCount,
                        CurrentFileName = relativeDisplayPath,
                        Percent = totalBytes > 0 ? Math.Min(100.0, (double)cumulativeBytesCopied / totalBytes * 100.0) : ((double)i / totalCount * 100.0),
                        BytesCopied = cumulativeBytesCopied,
                        TotalBytes = totalBytes
                    });
                }
            }
        }

        // Handle playlist export mode
        if (playlistExportMode == PlaylistExportMode.GenerateM3U8)
        {
            try
            {
                string playlistName = sanitizeNames ? SanitizeFileName($"{playlist.Title}.m3u8") : $"{playlist.Title}.m3u8";
                string destM3u8Path = Path.Combine(destinationDirectory, playlistName);

                var sb = new StringBuilder();
                sb.AppendLine("#EXTM3U");
                foreach (var item in copiedFilesForM3U)
                {
                    int seconds = item.track.Duration.HasValue ? (int)Math.Round(item.track.Duration.Value.TotalSeconds) : -1;
                    sb.AppendLine($"#EXTINF:{seconds},{item.track.DisplayName}");
                    sb.AppendLine(item.relativePath);
                }
                File.WriteAllText(destM3u8Path, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to write destination m3u8: {ex.Message}");
            }
        }
        else if (playlistExportMode == PlaylistExportMode.CopyOriginalAsIs)
        {
            try
            {
                if (!string.IsNullOrEmpty(playlist.FilePath) && File.Exists(playlist.FilePath))
                {
                    // Copy original playlist as-is if track names were not prefixed
                    if (filenameMode != FilenameMode.PrefixTrackNumber)
                    {
                        string origFileName = Path.GetFileName(playlist.FilePath);
                        if (sanitizeNames)
                        {
                            origFileName = SanitizeFileName(origFileName);
                        }
                        string destPlaylistPath = Path.Combine(destinationDirectory, origFileName);
                        File.Copy(playlist.FilePath, destPlaylistPath, overwrite: true);
                    }
                    else
                    {
                        // Fallback to generating new .m3u8 because original playlist points to un-prefixed track names
                        string playlistName = sanitizeNames ? SanitizeFileName($"{playlist.Title}.m3u8") : $"{playlist.Title}.m3u8";
                        string destM3u8Path = Path.Combine(destinationDirectory, playlistName);

                        var sb = new StringBuilder();
                        sb.AppendLine("#EXTM3U");
                        foreach (var item in copiedFilesForM3U)
                        {
                            int seconds = item.track.Duration.HasValue ? (int)Math.Round(item.track.Duration.Value.TotalSeconds) : -1;
                            sb.AppendLine($"#EXTINF:{seconds},{item.track.DisplayName}");
                            sb.AppendLine(item.relativePath);
                        }
                        File.WriteAllText(destM3u8Path, sb.ToString(), new UTF8Encoding(false));
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to copy original playlist: {ex.Message}");
            }
        }

        progress.Report(new CopyProgressReport
        {
            CurrentIndex = totalCount,
            TotalCount = totalCount,
            CurrentFileName = "Completed",
            Percent = 100,
            BytesCopied = cumulativeBytesCopied,
            TotalBytes = totalBytes,
            IsComplete = true
        });

        return true;
    }

    public static string GetDestinationSubFolder(string filePath, Playlist playlist, FolderStructureMode mode, bool sanitize)
    {
        if (mode == FolderStructureMode.Flat)
            return string.Empty;

        string subFolder = string.Empty;

        if (mode == FolderStructureMode.PreserveImmediateFolder)
        {
            try
            {
                string? dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir))
                {
                    var dirInfo = new DirectoryInfo(dir);
                    if (dirInfo.Parent != null)
                    {
                        subFolder = dirInfo.Name;
                    }
                }
            }
            catch { }
        }
        else if (mode == FolderStructureMode.PreserveRelativePath)
        {
            try
            {
                string? trackDir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(playlist.FilePath))
                {
                    string? playlistDir = Path.GetDirectoryName(playlist.FilePath);
                    if (!string.IsNullOrEmpty(playlistDir) &&
                        !string.IsNullOrEmpty(trackDir) &&
                        trackDir.StartsWith(playlistDir, StringComparison.OrdinalIgnoreCase))
                    {
                        string rel = Path.GetRelativePath(playlistDir, trackDir);
                        if (rel != "." && !string.IsNullOrWhiteSpace(rel))
                        {
                            subFolder = rel;
                        }
                    }
                }

                if (string.IsNullOrEmpty(subFolder) && !string.IsNullOrEmpty(trackDir))
                {
                    var dirInfo = new DirectoryInfo(trackDir);
                    if (dirInfo.Parent != null)
                    {
                        subFolder = dirInfo.Name;
                    }
                }
            }
            catch { }
        }

        if (!string.IsNullOrEmpty(subFolder) && sanitize)
        {
            var segments = subFolder.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            var cleanSegments = segments.Select(SanitizeFileName);
            subFolder = Path.Combine(cleanSegments.ToArray());
        }

        return subFolder;
    }

    public static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "track";

        string ext = Path.GetExtension(fileName);
        string name = Path.GetFileNameWithoutExtension(fileName);

        // Replace invalid filename characters
        char[] invalidChars = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            if (invalidChars.Contains(c) || c < 32)
            {
                sb.Append('_');
            }
            else
            {
                sb.Append(c);
            }
        }

        string cleanName = sb.ToString().Trim(' ', '.');
        if (string.IsNullOrWhiteSpace(cleanName))
        {
            cleanName = "track";
        }

        // Check Windows reserved names
        if (ReservedNames.Contains(cleanName))
        {
            cleanName = "_" + cleanName;
        }

        // Sanitize extension as well
        var extSb = new StringBuilder(ext.Length);
        foreach (char c in ext)
        {
            if (invalidChars.Contains(c) || c < 32)
            {
                extSb.Append('_');
            }
            else
            {
                extSb.Append(c);
            }
        }

        return cleanName + extSb.ToString();
    }

    private static string EnsureUniqueFileName(string fileName, HashSet<string> existingNames)
    {
        if (!existingNames.Contains(fileName))
            return fileName;

        string ext = Path.GetExtension(fileName);
        string baseName = Path.GetFileNameWithoutExtension(fileName);

        int counter = 1;
        string candidate;
        do
        {
            candidate = $"{baseName} ({counter}){ext}";
            counter++;
        } while (existingNames.Contains(candidate));

        return candidate;
    }
}

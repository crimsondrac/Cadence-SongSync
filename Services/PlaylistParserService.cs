using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Linq;
using CadenceSongSync.Models;

namespace CadenceSongSync.Services;

public class PlaylistParserService
{
    public const string SupportedFileFilter = 
        "Supported Playlists (*.m3u;*.m3u8;*.pls;*.wpl;*.xspf)|*.m3u;*.m3u8;*.pls;*.wpl;*.xspf|" +
        "M3U / M3U8 Playlists (*.m3u;*.m3u8)|*.m3u;*.m3u8|" +
        "PLS Playlists (*.pls)|*.pls|" +
        "WPL Playlists (*.wpl)|*.wpl|" +
        "XSPF Playlists (*.xspf)|*.xspf|" +
        "All Files (*.*)|*.*";

    public static readonly string[] SupportedAudioExtensions =
    {
        ".mp3", ".wav", ".wma", ".flac", ".m4a", ".aac", ".ogg"
    };

    public Playlist LoadPlaylist(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Playlist file not found.", filePath);
        }

        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        var playlist = new Playlist
        {
            FilePath = filePath,
            Title = Path.GetFileNameWithoutExtension(filePath),
            Format = ext
        };

        string directory = Path.GetDirectoryName(filePath) ?? string.Empty;
        List<TrackItem> tracks = ext switch
        {
            ".m3u" or ".m3u8" => ParseM3U(filePath, directory),
            ".pls" => ParsePLS(filePath, directory),
            ".wpl" => ParseWPL(filePath, directory),
            ".xspf" => ParseXSPF(filePath, directory),
            _ => ParseM3U(filePath, directory)
        };

        foreach (var track in tracks)
        {
            playlist.Tracks.Add(track);
        }

        playlist.RenumberTracks();
        playlist.IsDirty = false;
        return playlist;
    }

    public void SavePlaylist(Playlist playlist, string targetFilePath)
    {
        string targetDir = Path.GetDirectoryName(targetFilePath) ?? string.Empty;
        if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        var sb = new StringBuilder();
        sb.AppendLine("#EXTM3U");

        string? targetRoot = null;
        try { targetRoot = Path.GetPathRoot(targetFilePath); } catch { }

        for (int i = 0; i < playlist.Tracks.Count; i++)
        {
            var track = playlist.Tracks[i];
            int seconds = track.Duration.HasValue ? (int)Math.Round(track.Duration.Value.TotalSeconds) : -1;
            string title = track.DisplayName;
            sb.AppendLine($"#EXTINF:{seconds},{title}");

            string finalPath = track.FilePath;
            try
            {
                string? trackRoot = Path.GetPathRoot(track.FilePath);
                if (!string.IsNullOrEmpty(targetRoot) &&
                    !string.IsNullOrEmpty(trackRoot) &&
                    targetRoot.Equals(trackRoot, StringComparison.OrdinalIgnoreCase))
                {
                    finalPath = Path.GetRelativePath(targetDir, track.FilePath);
                }
            }
            catch
            {
                finalPath = track.FilePath;
            }

            sb.AppendLine(finalPath);
        }

        File.WriteAllText(targetFilePath, sb.ToString(), new UTF8Encoding(false));
        playlist.FilePath = targetFilePath;
        playlist.Format = ".m3u8";
        playlist.IsDirty = false;
    }

    private List<TrackItem> ParseM3U(string filePath, string playlistDir)
    {
        var tracks = new List<TrackItem>();
        string[] lines = File.ReadAllLines(filePath, DetectEncoding(filePath));

        int? pendingDurationSeconds = null;
        string? pendingTitle = null;
        string? pendingArtist = null;

        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
            {
                // Format: #EXTINF:seconds,Artist - Title or #EXTINF:seconds,Title
                string info = line.Substring(8).Trim();
                int commaIdx = info.IndexOf(',');
                if (commaIdx >= 0)
                {
                    string secStr = info.Substring(0, commaIdx).Trim();
                    if (double.TryParse(secStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double secs) && secs >= 0)
                    {
                        pendingDurationSeconds = (int)secs;
                    }

                    string fullTitle = info.Substring(commaIdx + 1).Trim();
                    int dashIdx = fullTitle.IndexOf(" - ", StringComparison.Ordinal);
                    if (dashIdx > 0)
                    {
                        pendingArtist = fullTitle.Substring(0, dashIdx).Trim();
                        pendingTitle = fullTitle.Substring(dashIdx + 3).Trim();
                    }
                    else
                    {
                        pendingTitle = fullTitle;
                    }
                }
                continue;
            }

            if (line.StartsWith("#", StringComparison.Ordinal))
            {
                // Comment or directive, ignore
                continue;
            }

            // Path line
            string resolved = ResolvePath(line, playlistDir);
            bool isMissing = !File.Exists(resolved);

            var track = new TrackItem
            {
                FilePath = resolved,
                Title = pendingTitle ?? Path.GetFileNameWithoutExtension(resolved),
                Artist = pendingArtist ?? string.Empty,
                Duration = pendingDurationSeconds.HasValue ? TimeSpan.FromSeconds(pendingDurationSeconds.Value) : null,
                IsMissing = isMissing
            };

            tracks.Add(track);

            pendingDurationSeconds = null;
            pendingTitle = null;
            pendingArtist = null;
        }

        return tracks;
    }

    private List<TrackItem> ParsePLS(string filePath, string playlistDir)
    {
        var tracks = new List<TrackItem>();
        string[] lines = File.ReadAllLines(filePath, DetectEncoding(filePath));

        var files = new Dictionary<int, string>();
        var titles = new Dictionary<int, string>();
        var lengths = new Dictionary<int, int>();

        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("[", StringComparison.Ordinal) || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith(";", StringComparison.Ordinal))
                continue;

            int eqIdx = line.IndexOf('=');
            if (eqIdx <= 0) continue;

            string key = line.Substring(0, eqIdx).Trim();
            string val = line.Substring(eqIdx + 1).Trim();

            if (key.StartsWith("File", StringComparison.OrdinalIgnoreCase) && int.TryParse(key.Substring(4), out int fileIndex))
            {
                files[fileIndex] = val;
            }
            else if (key.StartsWith("Title", StringComparison.OrdinalIgnoreCase) && int.TryParse(key.Substring(5), out int titleIndex))
            {
                titles[titleIndex] = val;
            }
            else if (key.StartsWith("Length", StringComparison.OrdinalIgnoreCase) && int.TryParse(key.Substring(6), out int lenIndex))
            {
                if (int.TryParse(val, out int sec))
                    lengths[lenIndex] = sec;
            }
        }

        for (int i = 1; i <= Math.Max(files.Count, 5000); i++)
        {
            if (!files.TryGetValue(i, out string? rawPath))
                continue;

            string resolved = ResolvePath(rawPath, playlistDir);
            bool isMissing = !File.Exists(resolved);

            string title = titles.TryGetValue(i, out var t) ? t : Path.GetFileNameWithoutExtension(resolved);
            string artist = string.Empty;
            int dashIdx = title.IndexOf(" - ", StringComparison.Ordinal);
            if (dashIdx > 0)
            {
                artist = title.Substring(0, dashIdx).Trim();
                title = title.Substring(dashIdx + 3).Trim();
            }

            TimeSpan? duration = null;
            if (lengths.TryGetValue(i, out int sec) && sec > 0)
            {
                duration = TimeSpan.FromSeconds(sec);
            }

            tracks.Add(new TrackItem
            {
                FilePath = resolved,
                Title = title,
                Artist = artist,
                Duration = duration,
                IsMissing = isMissing
            });
        }

        return tracks;
    }

    private List<TrackItem> ParseWPL(string filePath, string playlistDir)
    {
        var tracks = new List<TrackItem>();
        try
        {
            var doc = XDocument.Load(filePath);
            var mediaElements = doc.Descendants("media");

            foreach (var elem in mediaElements)
            {
                string? src = (string?)elem.Attribute("src");
                if (string.IsNullOrWhiteSpace(src))
                    continue;

                string resolved = ResolvePath(src, playlistDir);
                bool isMissing = !File.Exists(resolved);

                string? trackTitle = (string?)elem.Attribute("trackTitle") ?? (string?)elem.Attribute("track_title");
                string? trackArtist = (string?)elem.Attribute("trackArtist") ?? (string?)elem.Attribute("artist");

                tracks.Add(new TrackItem
                {
                    FilePath = resolved,
                    Title = !string.IsNullOrWhiteSpace(trackTitle) ? trackTitle : Path.GetFileNameWithoutExtension(resolved),
                    Artist = trackArtist ?? string.Empty,
                    IsMissing = isMissing
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to parse WPL: {ex.Message}");
        }

        return tracks;
    }

    private List<TrackItem> ParseXSPF(string filePath, string playlistDir)
    {
        var tracks = new List<TrackItem>();
        try
        {
            var doc = XDocument.Load(filePath);
            XNamespace ns = doc.Root?.Name.Namespace ?? XNamespace.None;

            var trackElements = doc.Descendants(ns + "track");
            foreach (var elem in trackElements)
            {
                string? location = elem.Element(ns + "location")?.Value?.Trim();
                if (string.IsNullOrWhiteSpace(location))
                    continue;

                string resolved = ResolvePath(location, playlistDir);
                bool isMissing = !File.Exists(resolved);

                string title = elem.Element(ns + "title")?.Value?.Trim() ?? Path.GetFileNameWithoutExtension(resolved);
                string artist = elem.Element(ns + "creator")?.Value?.Trim() ?? string.Empty;

                TimeSpan? duration = null;
                string? durStr = elem.Element(ns + "duration")?.Value?.Trim();
                if (double.TryParse(durStr, out double ms) && ms > 0)
                {
                    duration = TimeSpan.FromMilliseconds(ms);
                }

                tracks.Add(new TrackItem
                {
                    FilePath = resolved,
                    Title = title,
                    Artist = artist,
                    Duration = duration,
                    IsMissing = isMissing
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to parse XSPF: {ex.Message}");
        }

        return tracks;
    }

    public static string ResolvePath(string rawPath, string playlistDir)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            return string.Empty;

        string path = rawPath.Trim();

        // Handle file:/// or file:// URIs
        if (path.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (Uri.TryCreate(path, UriKind.Absolute, out var uri))
                {
                    path = uri.LocalPath;
                }
                else
                {
                    path = Uri.UnescapeDataString(path.Substring(5).TrimStart('/'));
                }
            }
            catch
            {
                path = path.Substring(5).TrimStart('/');
            }
        }
        else if (path.Contains('%'))
        {
            try
            {
                path = Uri.UnescapeDataString(path);
            }
            catch { }
        }

        // Standardize directory separators
        path = path.Replace('/', '\\');

        string fullPath;
        try
        {
            if (Path.IsPathRooted(path))
            {
                fullPath = Path.GetFullPath(path);
            }
            else
            {
                fullPath = Path.GetFullPath(Path.Combine(playlistDir, path));
            }
        }
        catch
        {
            fullPath = path;
        }

        // 1. Direct match
        if (File.Exists(fullPath))
            return fullPath;

        // 2. Unicode normalization (FormC - Precomposed, FormD - Decomposed)
        string formC = fullPath.Normalize(NormalizationForm.FormC);
        if (File.Exists(formC))
            return formC;

        string formD = fullPath.Normalize(NormalizationForm.FormD);
        if (File.Exists(formD))
            return formD;

        // 3. Fallback: inspect directory for matching filename with umlauts/accents
        try
        {
            string? dir = Path.GetDirectoryName(fullPath);
            string targetName = Path.GetFileName(fullPath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                string targetNameNorm = targetName.Normalize(NormalizationForm.FormC);
                foreach (string candidate in Directory.GetFiles(dir))
                {
                    string candidateName = Path.GetFileName(candidate);
                    if (string.Equals(candidateName, targetName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(candidateName.Normalize(NormalizationForm.FormC), targetNameNorm, StringComparison.OrdinalIgnoreCase))
                    {
                        return candidate;
                    }
                }
            }
        }
        catch { }

        return fullPath;
    }

    private static Encoding DetectEncoding(string filename)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(filename);
        }
        catch
        {
            return Encoding.UTF8;
        }

        if (bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf) return Encoding.UTF8;
        if (bytes.Length >= 2 && bytes[0] == 0xff && bytes[1] == 0xfe) return Encoding.Unicode;
        if (bytes.Length >= 2 && bytes[0] == 0xfe && bytes[1] == 0xff) return Encoding.BigEndianUnicode;
        if (bytes.Length >= 4 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0xfe && bytes[3] == 0xff) return Encoding.UTF32;

        string ext = Path.GetExtension(filename).ToLowerInvariant();
        if (ext == ".m3u8" || ext == ".xspf")
        {
            return Encoding.UTF8;
        }

        // Check if file is valid UTF-8
        if (IsValidUtf8(bytes))
        {
            return Encoding.UTF8;
        }

        // If not valid UTF-8, fall back to Windows-1252 / ANSI to support umlauts (ä, ö, ü, etc.)
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1252);
        }
        catch
        {
            return Encoding.Latin1;
        }
    }

    private static bool IsValidUtf8(byte[] bytes)
    {
        int length = bytes.Length;
        int i = 0;

        while (i < length)
        {
            byte b = bytes[i];
            if (b < 0x80)
            {
                i++;
                continue;
            }

            int sequenceLength;

            if ((b & 0xE0) == 0xC0)
            {
                sequenceLength = 2;
                if (b < 0xC2) return false;
            }
            else if ((b & 0xF0) == 0xE0)
            {
                sequenceLength = 3;
            }
            else if ((b & 0xF8) == 0xF0)
            {
                sequenceLength = 4;
                if (b > 0xF4) return false;
            }
            else
            {
                return false;
            }

            if (i + sequenceLength > length)
                return false;

            for (int j = 1; j < sequenceLength; j++)
            {
                if ((bytes[i + j] & 0xC0) != 0x80)
                    return false;
            }

            i += sequenceLength;
        }

        return true;
    }
}

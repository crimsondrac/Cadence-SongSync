using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CadenceSongSync.Models;

namespace CadenceSongSync.Services;

public static class SelfVerificationService
{
    public static async Task<bool> RunAllTestsAsync()
    {
        string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "verification_result.txt");
        using var logWriter = new StreamWriter(logPath, false);
        void Log(string msg)
        {
            Console.WriteLine(msg);
            logWriter.WriteLine(msg);
            logWriter.Flush();
        }

        Log("=================================================");
        Log("Cadence SongSync - Automated Self-Verification");
        Log("=================================================");

        string testDir = Path.Combine(Path.GetTempPath(), "SongSyncVerification_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir);

        try
        {
            TestM3UParserAndWriter(testDir);
            Log("M3U/M3U8 Parser & Writer: PASSED");

            TestPLSParser(testDir);
            Log("PLS Parser: PASSED");

            TestWPLParser(testDir);
            Log("WPL XML Parser: PASSED");

            TestXSPFParser(testDir);
            Log("XSPF XML Parser: PASSED");

            TestSanitization();
            Log("FAT32/exFAT Sanitization: PASSED");

            TestPresetMetadata();
            Log("Preset Metadata & Drive Resolution: PASSED");

            await TestDriveCopyAsync(testDir);
            Log("Drive Copier (Preserve, Prefix, & Folder Structure modes): PASSED");

            TestAppAssetsAndIcons();
            Log("App & Tray Icons & Splash Banner: PASSED");

            TestInternationalCharactersWithUmlauts(testDir);
            Log("International Characters & Umlauts Recognition: PASSED");

            TestPlaylistDirtyState(testDir);
            Log("Playlist Unsaved Changes & Clean State: PASSED");

            Log("\n[SUCCESS] All self-tests passed successfully!");
            return true;
        }
        catch (Exception ex)
        {
            Log($"\n[FAILURE] Test error: {ex}");
            return false;
        }
        finally
        {
            try { Directory.Delete(testDir, true); } catch { }
        }
    }

    private static void TestM3UParserAndWriter(string testDir)
    {
        Console.Write("Testing M3U/M3U8 Parser & Writer... ");
        string song1 = Path.Combine(testDir, "Rock.mp3");
        string song2 = Path.Combine(testDir, "Sub", "Jazz.flac");
        Directory.CreateDirectory(Path.GetDirectoryName(song2)!);
        File.WriteAllText(song1, "dummy audio 1");
        File.WriteAllText(song2, "dummy audio 2");

        string m3uPath = Path.Combine(testDir, "playlist.m3u8");
        string content = "#EXTM3U\n#EXTINF:210,Band - Rock Song\nRock.mp3\n#EXTINF:180,Jazz Title\nSub\\Jazz.flac\n#EXTINF:100,Missing\nfile:///" + testDir.Replace('\\', '/') + "/None.wav\n";
        File.WriteAllText(m3uPath, content);

        var parser = new PlaylistParserService();
        var pl = parser.LoadPlaylist(m3uPath);

        Assert(pl.Tracks.Count == 3, "Track count should be 3");
        Assert(pl.Tracks[0].Title == "Rock Song" && pl.Tracks[0].Artist == "Band", "Track 0 title/artist mismatch");
        Assert(pl.Tracks[0].IsMissing == false, "Track 0 should exist");
        Assert(pl.Tracks[1].Title == "Jazz Title", "Track 1 title mismatch");
        Assert(pl.Tracks[1].IsMissing == false, "Track 1 should exist");
        Assert(pl.Tracks[2].IsMissing == true, "Track 2 should be flagged missing");

        // Test writing back out
        string outPath = Path.Combine(testDir, "out.m3u8");
        parser.SavePlaylist(pl, outPath);
        Assert(File.Exists(outPath), "Saved playlist file should exist");
        string saved = File.ReadAllText(outPath);
        Assert(saved.Contains("#EXTM3U") && saved.Contains("Rock.mp3"), "Saved m3u8 should contain EXTM3U and Rock.mp3");

        Console.WriteLine("PASSED");
    }

    private static void TestPLSParser(string testDir)
    {
        Console.Write("Testing PLS Parser... ");
        string song1 = Path.Combine(testDir, "PlsTrack.mp3");
        File.WriteAllText(song1, "pls data");

        string plsPath = Path.Combine(testDir, "test.pls");
        string content = "[playlist]\nFile1=PlsTrack.mp3\nTitle1=Artist - Title One\nLength1=240\nFile2=Missing.mp3\nTitle2=Title Two\nNumberOfEntries=2\nVersion=2";
        File.WriteAllText(plsPath, content);

        var parser = new PlaylistParserService();
        var pl = parser.LoadPlaylist(plsPath);

        Assert(pl.Tracks.Count == 2, "PLS track count should be 2");
        Assert(pl.Tracks[0].Title == "Title One" && pl.Tracks[0].Artist == "Artist", "PLS track 0 title/artist mismatch");
        Assert(pl.Tracks[0].IsMissing == false, "PLS track 0 should exist");
        Assert(pl.Tracks[1].IsMissing == true, "PLS track 1 should be missing");

        Console.WriteLine("PASSED");
    }

    private static void TestWPLParser(string testDir)
    {
        Console.Write("Testing WPL XML Parser... ");
        string song1 = Path.Combine(testDir, "WplTrack.mp3");
        File.WriteAllText(song1, "wpl data");

        string wplPath = Path.Combine(testDir, "test.wpl");
        string content = "<?wpl version=\"1.0\"?><smil><body><seq><media src=\"WplTrack.mp3\" trackTitle=\"WPL Song\" trackArtist=\"WPL Band\" /><media src=\"Missing.mp3\" /></seq></body></smil>";
        File.WriteAllText(wplPath, content);

        var parser = new PlaylistParserService();
        var pl = parser.LoadPlaylist(wplPath);

        Assert(pl.Tracks.Count == 2, "WPL track count should be 2");
        Assert(pl.Tracks[0].Title == "WPL Song" && pl.Tracks[0].Artist == "WPL Band", "WPL track 0 title mismatch");
        Assert(pl.Tracks[0].IsMissing == false, "WPL track 0 should exist");
        Assert(pl.Tracks[1].IsMissing == true, "WPL track 1 should be missing");

        Console.WriteLine("PASSED");
    }

    private static void TestXSPFParser(string testDir)
    {
        Console.Write("Testing XSPF XML Parser... ");
        string song1 = Path.Combine(testDir, "XspfTrack.m4a");
        File.WriteAllText(song1, "xspf data");

        string xspfPath = Path.Combine(testDir, "test.xspf");
        string content = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><playlist version=\"1\" xmlns=\"http://xspf.org/ns/0/\"><trackList><track><location>file:///" + song1.Replace('\\', '/') + "</location><title>XSPF Song</title><creator>XSPF Artist</creator><duration>185000</duration></track></trackList></playlist>";
        File.WriteAllText(xspfPath, content);

        var parser = new PlaylistParserService();
        var pl = parser.LoadPlaylist(xspfPath);

        Assert(pl.Tracks.Count == 1, "XSPF track count should be 1");
        Assert(pl.Tracks[0].Title == "XSPF Song" && pl.Tracks[0].Artist == "XSPF Artist", "XSPF track mismatch");
        Assert(pl.Tracks[0].IsMissing == false, "XSPF track should exist");
        Assert(pl.Tracks[0].Duration == TimeSpan.FromMilliseconds(185000), "XSPF duration mismatch");

        Console.WriteLine("PASSED");
    }

    private static void TestSanitization()
    {
        Console.Write("Testing FAT32/exFAT Filename Sanitization... ");
        string dirty = "AC/DC: Who Made Who? *Deluxe* <2020> | Live.mp3";
        string clean = DriveSyncService.SanitizeFileName(dirty);
        Assert(!clean.Contains('/') && !clean.Contains(':') && !clean.Contains('?') && !clean.Contains('*') && !clean.Contains('<') && !clean.Contains('>') && !clean.Contains('|'), "Clean name has illegal characters");

        string trailing = "SongTitle... .mp3";
        string cleanTrailing = DriveSyncService.SanitizeFileName(trailing);
        Assert(cleanTrailing.EndsWith(".mp3"), "Should preserve extension");
        Assert(!Path.GetFileNameWithoutExtension(cleanTrailing).EndsWith("."), "Should not have trailing dot in base name");

        string reserved = "CON.mp3";
        string cleanReserved = DriveSyncService.SanitizeFileName(reserved);
        Assert(cleanReserved.StartsWith("_CON"), "Reserved device name CON should be prefixed");

        Console.WriteLine("PASSED");
    }

    private static void TestPresetMetadata()
    {
        Console.Write("Testing Preset Metadata & Drive Resolution... ");
        var preset = new Preset { Name = "Workout USB" };
        preset.SetDestination("C:\\Music\\Workout");
        Assert(preset.DriveLetter == "C:\\", "Drive letter should be C:\\");
        Assert(preset.RelativeSubfolder == "Music\\Workout", "Relative subfolder mismatch");
        Console.WriteLine("PASSED");
    }

    private static async Task TestDriveCopyAsync(string testDir)
    {
        Console.Write("Testing Async Drive Copier (Preserve, Prefix, & Folder Structure modes)... ");
        string srcA = Path.Combine(testDir, "FolderA");
        string srcB = Path.Combine(testDir, "FolderB");
        string destPreserve = Path.Combine(testDir, "DestPreserve");
        string destPrefix = Path.Combine(testDir, "DestPrefix");
        string destFolders = Path.Combine(testDir, "DestFolders");

        Directory.CreateDirectory(srcA);
        Directory.CreateDirectory(srcB);

        string f1 = Path.Combine(srcA, "Intro.mp3");
        string f2 = Path.Combine(srcB, "Intro.mp3");
        File.WriteAllText(f1, "Sample audio 1");
        File.WriteAllText(f2, "Sample audio 2");

        var pl = new Playlist { Title = "SyncTest" };
        pl.Tracks.Add(new TrackItem { FilePath = f1, Title = "Intro 1" });
        pl.Tracks.Add(new TrackItem { FilePath = f2, Title = "Intro 2" });

        var sync = new DriveSyncService();

        // 1. Preserve mode with intra-playlist collision (Flat)
        bool res1 = await sync.CopyPlaylistAsync(
            pl,
            destPreserve,
            FilenameMode.Preserve,
            FolderStructureMode.Flat,
            ExistingFileAction.Skip,
            PlaylistExportMode.GenerateM3U8,
            sanitizeNames: true,
            clearDestination: false,
            new Progress<CopyProgressReport>(),
            CancellationToken.None);

        Assert(res1, "Copy should succeed in preserve mode");
        Assert(File.Exists(Path.Combine(destPreserve, "Intro.mp3")), "Intro.mp3 should exist");
        Assert(File.Exists(Path.Combine(destPreserve, "Intro (1).mp3")), "Intro (1).mp3 intra-playlist collision rename should exist");
        Assert(File.Exists(Path.Combine(destPreserve, "SyncTest.m3u8")), "Destination m3u8 should exist");

        // 2. Prefix mode (Flat)
        bool res2 = await sync.CopyPlaylistAsync(
            pl,
            destPrefix,
            FilenameMode.PrefixTrackNumber,
            FolderStructureMode.Flat,
            ExistingFileAction.Skip,
            PlaylistExportMode.GenerateM3U8,
            sanitizeNames: true,
            clearDestination: false,
            new Progress<CopyProgressReport>(),
            CancellationToken.None);

        Assert(res2, "Copy should succeed in prefix mode");
        Assert(File.Exists(Path.Combine(destPrefix, "01 - Intro.mp3")), "01 - Intro.mp3 should exist");
        Assert(File.Exists(Path.Combine(destPrefix, "02 - Intro.mp3")), "02 - Intro.mp3 should exist");

        // 3. Preserve Immediate Folder Structure mode (e.g. "My Songs" subfolder recreation)
        string srcMySongs = Path.Combine(testDir, "My Songs");
        string srcWorkout = Path.Combine(testDir, "Workout Beats");
        Directory.CreateDirectory(srcMySongs);
        Directory.CreateDirectory(srcWorkout);

        string songMySongs = Path.Combine(srcMySongs, "FavoriteTrack.mp3");
        string songWorkout = Path.Combine(srcWorkout, "FastTempo.wav");
        File.WriteAllText(songMySongs, "audio my songs");
        File.WriteAllText(songWorkout, "audio workout");

        var folderPlaylist = new Playlist { Title = "FolderSyncTest" };
        folderPlaylist.Tracks.Add(new TrackItem { FilePath = songMySongs, Title = "Favorite Track" });
        folderPlaylist.Tracks.Add(new TrackItem { FilePath = songWorkout, Title = "Fast Tempo" });

        bool res3 = await sync.CopyPlaylistAsync(
            folderPlaylist,
            destFolders,
            FilenameMode.Preserve,
            FolderStructureMode.PreserveImmediateFolder,
            ExistingFileAction.Skip,
            PlaylistExportMode.GenerateM3U8,
            sanitizeNames: true,
            clearDestination: false,
            new Progress<CopyProgressReport>(),
            CancellationToken.None);

        Assert(res3, "Copy should succeed in folder structure mode");
        Assert(File.Exists(Path.Combine(destFolders, "My Songs", "FavoriteTrack.mp3")), "Subfolder 'My Songs/FavoriteTrack.mp3' must exist");
        Assert(File.Exists(Path.Combine(destFolders, "Workout Beats", "FastTempo.wav")), "Subfolder 'Workout Beats/FastTempo.wav' must exist");

        string destM3u8Path = Path.Combine(destFolders, "FolderSyncTest.m3u8");
        Assert(File.Exists(destM3u8Path), "Destination m3u8 must exist");
        string destM3u8Text = File.ReadAllText(destM3u8Path);
        Assert(destM3u8Text.Contains(@"My Songs\FavoriteTrack.mp3"), "m3u8 should reference subfolder My Songs\\FavoriteTrack.mp3");
        Assert(destM3u8Text.Contains(@"Workout Beats\FastTempo.wav"), "m3u8 should reference subfolder Workout Beats\\FastTempo.wav");

        // 4. Test ExistingFileAction.Skip (never create duplicate copies like Intro (1).mp3 on re-sync)
        string destNoDupes = Path.Combine(testDir, "DestNoDupes");
        Directory.CreateDirectory(destNoDupes);
        File.WriteAllText(Path.Combine(destNoDupes, "Intro.mp3"), "Existing file on drive");

        var singleTrackPl = new Playlist { Title = "NoDupeSync" };
        singleTrackPl.Tracks.Add(new TrackItem { FilePath = f1, Title = "Intro" });

        bool res4 = await sync.CopyPlaylistAsync(
            singleTrackPl,
            destNoDupes,
            FilenameMode.Preserve,
            FolderStructureMode.Flat,
            ExistingFileAction.Skip,
            PlaylistExportMode.GenerateM3U8,
            sanitizeNames: true,
            clearDestination: false,
            new Progress<CopyProgressReport>(),
            CancellationToken.None);

        Assert(res4, "Sync should succeed with Skip existing");
        Assert(File.Exists(Path.Combine(destNoDupes, "Intro.mp3")), "Intro.mp3 should still exist");
        Assert(!File.Exists(Path.Combine(destNoDupes, "Intro (1).mp3")), "Intro (1).mp3 must NEVER be created on re-sync!");
        Assert(File.ReadAllText(Path.Combine(destNoDupes, "Intro.mp3")) == "Existing file on drive", "Existing file should not have been overwritten when Skip was set");

        // 5. Test PlaylistExportMode.CopyOriginalAsIs
        string originalPlPath = Path.Combine(testDir, "OriginalList.m3u");
        File.WriteAllText(originalPlPath, "#EXTM3U\nIntro.mp3\n");
        singleTrackPl.FilePath = originalPlPath;

        string destCopyAsIs = Path.Combine(testDir, "DestCopyAsIs");
        Directory.CreateDirectory(destCopyAsIs);

        bool res5 = await sync.CopyPlaylistAsync(
            singleTrackPl,
            destCopyAsIs,
            FilenameMode.Preserve,
            FolderStructureMode.Flat,
            ExistingFileAction.Skip,
            PlaylistExportMode.CopyOriginalAsIs,
            sanitizeNames: true,
            clearDestination: false,
            new Progress<CopyProgressReport>(),
            CancellationToken.None);

        Assert(res5, "Sync should succeed with CopyOriginalAsIs");
        Assert(File.Exists(Path.Combine(destCopyAsIs, "OriginalList.m3u")), "Original playlist file must be copied as-is");

        Console.WriteLine("PASSED");
    }

    private static void TestAppAssetsAndIcons()
    {
        Console.Write("Testing App & Tray Icons & Splash Banner... ");

        string iconPath = Path.Combine(AppContext.BaseDirectory, "assets", "images", "app_icon.ico");
        if (!File.Exists(iconPath))
        {
            iconPath = Path.Combine(Directory.GetCurrentDirectory(), "assets", "images", "app_icon.ico");
        }
        Assert(File.Exists(iconPath), $"app_icon.ico must exist at {iconPath}");

        using (var icon = TrayIconService.LoadAppIcon())
        {
            Assert(icon != null, "TrayIconService.LoadAppIcon must return non-null Icon");
        }

        string splashPath = Path.Combine(AppContext.BaseDirectory, "assets", "images", "splash_banner.png");
        if (!File.Exists(splashPath))
        {
            splashPath = Path.Combine(Directory.GetCurrentDirectory(), "assets", "images", "splash_banner.png");
        }
        Assert(File.Exists(splashPath), $"splash_banner.png must exist at {splashPath}");

        Console.WriteLine("PASSED");
    }

    private static void TestInternationalCharactersWithUmlauts(string testDir)
    {
        Console.Write("Testing International Characters & Umlauts Recognition... ");

        // Create audio files on disk with German umlauts, French accents, Spanish tildes
        string songUmlaut = Path.Combine(testDir, "01 - Schöne Musik - Hören & Träumen.mp3");
        string songAccent = Path.Combine(testDir, "02 - Café Français.flac");
        string songSpanish = Path.Combine(testDir, "03 - Canción Mañana.wav");

        File.WriteAllText(songUmlaut, "dummy audio umlauts");
        File.WriteAllText(songAccent, "dummy audio accents");
        File.WriteAllText(songSpanish, "dummy audio spanish");

        // 1. Test UTF-8 encoded .m3u8
        string m3u8Path = Path.Combine(testDir, "umlauts_utf8.m3u8");
        string contentUtf8 = "#EXTM3U\n#EXTINF:200,Schöne Musik - Hören & Träumen\n01 - Schöne Musik - Hören & Träumen.mp3\n#EXTINF:150,Café Français\n02 - Café Français.flac\n#EXTINF:180,Canción Mañana\n03 - Canción Mañana.wav\n";
        File.WriteAllText(m3u8Path, contentUtf8, Encoding.UTF8);

        var parser = new PlaylistParserService();
        var plUtf8 = parser.LoadPlaylist(m3u8Path);
        Assert(plUtf8.Tracks.Count == 3, "UTF-8 playlist track count should be 3");
        Assert(!plUtf8.Tracks[0].IsMissing, "UTF-8 umlaut track 0 should NOT be missing");
        Assert(!plUtf8.Tracks[1].IsMissing, "UTF-8 accented track 1 should NOT be missing");
        Assert(!plUtf8.Tracks[2].IsMissing, "UTF-8 spanish track 2 should NOT be missing");

        // 2. Test ANSI / Windows-1252 encoded .m3u without BOM
        string m3uAnsiPath = Path.Combine(testDir, "umlauts_ansi.m3u");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var ansiEncoding = Encoding.GetEncoding(1252);
        File.WriteAllText(m3uAnsiPath, contentUtf8, ansiEncoding);

        var plAnsi = parser.LoadPlaylist(m3uAnsiPath);
        Assert(plAnsi.Tracks.Count == 3, "ANSI playlist track count should be 3");
        Assert(!plAnsi.Tracks[0].IsMissing, "ANSI umlaut track 0 should NOT be missing");
        Assert(!plAnsi.Tracks[1].IsMissing, "ANSI accented track 1 should NOT be missing");
        Assert(!plAnsi.Tracks[2].IsMissing, "ANSI spanish track 2 should NOT be missing");

        Console.WriteLine("PASSED");
    }

    private static void TestPlaylistDirtyState(string testDir)
    {
        Console.Write("Testing Playlist Unsaved Changes & Clean State... ");

        string song1 = Path.Combine(testDir, "Song1.mp3");
        string song2 = Path.Combine(testDir, "Song2.mp3");
        File.WriteAllText(song1, "audio1");
        File.WriteAllText(song2, "audio2");

        string plPath = Path.Combine(testDir, "DirtyCheck.m3u8");
        File.WriteAllText(plPath, "#EXTM3U\n#EXTINF:100,Song 1\nSong1.mp3\n#EXTINF:120,Song 2\nSong2.mp3\n");

        var parser = new PlaylistParserService();
        var pl = parser.LoadPlaylist(plPath);
        Assert(!pl.IsDirty, "Newly loaded playlist must have IsDirty == false");

        // Clone for editor
        var editorCopy = pl.Clone();
        Assert(!editorCopy.IsDirty, "Editor working clone must have IsDirty == false on open");
        Assert(editorCopy.Tracks.Count == 2, "Editor working clone should have 2 tracks");

        // Simulate deleting a track in editor
        editorCopy.Tracks.RemoveAt(0);
        Assert(editorCopy.IsDirty, "Removing track in editor must set editorCopy.IsDirty == true");
        Assert(editorCopy.Tracks.Count == 1, "Editor copy should now have 1 track");

        // Verify the original playlist is UNTOUCHED (no caching of removed tracks!)
        Assert(!pl.IsDirty, "Original playlist must remain IsDirty == false when editor changes are not saved");
        Assert(pl.Tracks.Count == 2, "Original playlist must still have all 2 tracks (no leaked deletions!)");

        // Re-cloning editor copy shows original state
        var reloadedEditorCopy = pl.Clone();
        Assert(!reloadedEditorCopy.IsDirty, "Reopening editor must start with IsDirty == false");
        Assert(reloadedEditorCopy.Tracks.Count == 2, "Reopening editor must display all 2 tracks intact");

        Console.WriteLine("PASSED");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception("Assertion Failed: " + message);
        }
    }
}

# Cadence SongSync (SongSync)

**Cadence SongSync** is a lightweight, zero-bloat Windows desktop application built in C# (.NET 8, WPF + Windows Forms) designed for seamless audio playback and smart music playlist syncing to external drives, USB sticks, and car stereos. It lives primarily in the Windows System Tray with no taskbar clutter.

---

## Key Features

### 1. System Tray & Compact Popup Player
- **System Tray Resident**: Lives in the Windows notification area with zero taskbar footprint when closed (`ShowInTaskbar="False"`, `ShutdownMode="OnExplicitShutdown"`).
- **Left-Click Popup**: Opens a borderless player window cleanly positioned relative to the taskbar using DPI-aware `SystemParameters.WorkArea`. Auto-closes on `Deactivated` unless a dialog or copy operation is active.
- **Popup UI**:
  - Currently loaded playlist title and track counter.
  - Interactive track list highlighting the active playing track and clearly flagging missing files in red without crashing. Double-click any track to play instantly.
  - Full playback controls: Play/Pause, Previous, Next, Stop, Random Play toggle (shuffle without repeats), Repeat Mode cycle button (Repeat Off / Repeat All / Repeat Track), Volume slider (0-100%), and interactive scrubber slider.
  - "Copy Playlist to Drive" button and quick "Load" / "Edit" playlist shortcuts.
- **Right-Click Native Context Menu**:
  - Play / Pause (dynamically reflects state)
  - Next Track
  - Load Playlist...
  - Edit Playlist...
  - Copy to Drive...
  - Presets (dynamic submenu listing configured presets + "Manage Presets...")
  - Settings...
  - Exit

### 2. Multi-Format Playlist Parser & Writer
- **Read Formats**: `.m3u`, `.m3u8`, `.pls`, `.wpl`, and `.xspf`.
- **Path Resolution**: Handles relative paths against playlist folder, absolute Windows paths, and `file:///` URI-encoded paths (`Uri.UnescapeDataString`). Missing files are gracefully flagged in red.
- **Write Support**: Saves new or edited playlists in UTF-8 `.m3u8` format with `#EXTM3U` and `#EXTINF` metadata. Automatically uses relative paths when tracks reside on the same drive, and absolute paths otherwise.

### 3. Zero-Dependency Audio Playback
- Zero external dependencies: uses native `System.Windows.Media.MediaPlayer` for smooth playback of `.mp3`, `.wav`, `.wma`, `.flac`, and `.m4a`.
- **Random Play (Shuffle)**: Shuffles playback queue without repeats until the entire cycle completes.
- **Repeat Modes**:
  - **Repeat Off**: Plays through playlist/cycle and stops.
  - **Repeat All**: Loops back to the start or initiates a new shuffle cycle upon completion.
  - **Repeat Track**: Continuously loops the active song.
- Automatic skip over missing/corrupted tracks without crashing or freezing.

### 4. Split-Pane Edit Playlist Window
- **Left Panel (File Browser)**:
  - Drive dropdown populated safely via `DriveInfo.GetDrives()` (gracefully skips empty card readers / optical drives).
  - Lazy-loaded hierarchical folder tree view (`TreeView`).
  - Audio file list filtered to supported audio extensions (`.mp3`, `.wav`, `.wma`, `.flac`, `.m4a`, `.aac`, `.ogg`).
- **Right Panel (Playlist Tracks)**:
  - Editable playlist title.
  - Ordered track list with track number, title, duration, path, and missing status.
- **Interactions**:
  - Multi-select drag-and-drop from the left file list into the right playlist (at drop position or end).
  - Drag-and-drop track reordering within the playlist.
  - Deletion via the `Delete` key or by dragging tracks onto an on-screen **Trash** icon target.
- **Toolbar Actions**:
  - **Create New**: Clears playlist for a new creation.
  - **Save**: Overwrites existing `.m3u8` or prompts Save As.
  - **Save As**: Saves to standard `.m3u8`.
  - **Delete Playlist**: Prompts for confirmation and deletes the playlist file from disk.
  - **Close**: Checks `IsDirty` and prompts Save / Don't Save / Cancel before closing.

### 5. Presets & Smart Destination Drive Resolution
- **Manage Presets Window**: Manage presets consisting of Preset Name, Playlist File Path, and Destination Folder. Automatically captures and stores:
  - `DriveLetter` (e.g. `E:\`)
  - `VolumeLabel` (e.g. `SANDISK32`)
  - `RelativeSubfolder` (e.g. `Music\Workouts`)
- **Smart Drive Resolution**:
  1. Checks if `DriveLetter` exists, is ready, and matches `VolumeLabel`.
  2. If drive letter shifted (common with USB flash drives and SD cards), scans all ready drives for a matching `VolumeLabel` and appends `RelativeSubfolder`.
  3. If not found, prompts user with a folder browser and offers to update the preset with the new destination.

### 6. Background Destination Drive Copier
- **Async Execution**: File copying runs on background thread (`Task.Run`) with `CancellationTokenSource`, guaranteeing audio playback never stutters or drops frames.
- **Non-blocking Progress UI**: Real-time progress bar, track counter, MB/GB byte readout, current filename, and responsive Cancel button.
- **Folder Structure Modes**:
  - **Recreate source folder (Default)**: Automatically recreates the containing folder on the target drive (e.g., a song residing in `...\My Songs\Track.mp3` copies to `<destination>\My Songs\Track.mp3`). Subfolders are created on-the-fly if they don't exist.
  - **Recreate relative hierarchy**: Preserves full nested subfolder paths (e.g., `<destination>\Artist\Album\Track.mp3`).
  - **Flat**: Copies all songs directly to the destination root folder without subfolders.
- **Customizable File Settings**:
  - **Preserve original filenames**: Auto-renames duplicates with `(1)`, `(2)`.
  - **Prefix track numbers**: Prepend zero-padded numbers (`001 - Track.mp3`) so hardware players and car stereos play in playlist order.
  - **FAT32/exFAT Sanitization**: Strips illegal characters (`\ / : * ? " < > |`), control characters, trailing dots/spaces, and reserved Windows device names (`CON`, `PRN`, `AUX`, `NUL`).
  - **Clear destination folder**: Optional cleanup before sync.
  - **Write .m3u8**: Creates a destination `.m3u8` playlist with relative paths that properly reference any subfolders.

### 7. Zero Bloat Storage
- No album art fetching, no lyrics, no web scraping, no background network calls, and no bloated database.
- Settings and presets are persisted cleanly to `%AppData%\CadenceSongSync\settings.json`.

---

## Build & Run Instructions

### Prerequisites
- Windows 10 / 11
- .NET 8 SDK (`net8.0-windows`)

### Build
```powershell
dotnet build -c Release
```

### Run
```powershell
.\bin\Release\net8.0-windows\CadenceSongSync.exe
```

### Self-Verification Test Suite
Run the built-in automated verification tests:
```powershell
.\bin\Release\net8.0-windows\CadenceSongSync.exe --verify
```

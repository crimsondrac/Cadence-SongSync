using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CadenceSongSync.Models;
using CadenceSongSync.Services;

namespace CadenceSongSync.Views;

public class FolderItem
{
    public string Header { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public ObservableCollection<FolderItem?> Children { get; set; } = new();
    public bool IsLoaded { get; set; }
}

public class AudioFileEntry
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public long Length { get; set; }
    public string FormattedSize
    {
        get
        {
            if (Length < 1024 * 1024)
                return $"{Length / 1024.0:F1} KB";
            return $"{Length / (1024.0 * 1024.0):F1} MB";
        }
    }
}

public partial class EditPlaylistWindow : Window
{
    private readonly AppStateService _appState;
    private readonly Playlist _originalPlaylist;
    private Playlist _playlist;
    private string _originalTitle;
    private bool _isLoadedAndReady;

    private System.Windows.Point _leftDragStartPoint;
    private bool _leftIsMouseDownOnSelected;
    private AudioFileEntry? _leftPotentialSingleSelect;

    private System.Windows.Point _rightDragStartPoint;
    private bool _rightIsMouseDownOnSelected;
    private TrackItem? _rightPotentialSingleSelect;
    private List<TrackItem>? _draggedTracks;

    public EditPlaylistWindow(Playlist playlist)
    {
        InitializeComponent();
        _appState = AppStateService.Instance;

        // Work on a detached clone so in-progress changes are isolated
        // and cannot pollute AudioPlayer.CurrentPlaylist unless explicitly saved
        _originalPlaylist = playlist;
        _playlist = playlist.Clone();
        _playlist.IsDirty = false;
        _originalTitle = _playlist.Title;

        DataContext = _playlist;

        _playlist.PropertyChanged += Playlist_PropertyChanged;
        UpdateHeaderStats();
        PopulateDrives();

        Loaded += (s, e) =>
        {
            _isLoadedAndReady = true;
            _playlist.IsDirty = false;
            UpdateHeaderStats();
        };
    }

    private void Playlist_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Playlist.IsDirty))
        {
            Dispatcher.Invoke(() =>
            {
                TxtDirtyIndicator.Visibility = _playlist.IsDirty ? Visibility.Visible : Visibility.Collapsed;
            });
        }
        else if (e.PropertyName == nameof(Playlist.Title) || e.PropertyName == nameof(Playlist.Tracks))
        {
            Dispatcher.Invoke(UpdateHeaderStats);
        }
    }

    private void UpdateHeaderStats()
    {
        TxtPlaylistStats.Text = $"{_playlist.Tracks.Count} tracks";
        TxtDirtyIndicator.Visibility = _playlist.IsDirty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TxtPlaylistTitle_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isLoadedAndReady) return;
        if (!string.Equals(_playlist.Title, _originalTitle, StringComparison.Ordinal))
        {
            _playlist.IsDirty = true;
        }
    }

    #region Left Panel: Drives, Folders, Audio Files

    private void PopulateDrives()
    {
        CmbDrives.Items.Clear();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.IsReady)
                {
                    string label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? drive.DriveType.ToString() : drive.VolumeLabel;
                    long freeGb = drive.AvailableFreeSpace / (1024 * 1024 * 1024);
                    CmbDrives.Items.Add(new ComboBoxItem
                    {
                        Content = $"{drive.Name} [{label}] ({freeGb} GB free)",
                        Tag = drive.RootDirectory.FullName
                    });
                }
            }
            catch { /* Skip unready / card reader drives */ }
        }

        string? initialFolder = null;
        if (_appState.Settings.PlaylistEditorOpenLastFolder &&
            !string.IsNullOrWhiteSpace(_appState.Settings.LastBrowsedFolder) &&
            Directory.Exists(_appState.Settings.LastBrowsedFolder))
        {
            initialFolder = _appState.Settings.LastBrowsedFolder;
        }
        else if (!string.IsNullOrWhiteSpace(_appState.Settings.PlaylistEditorCustomStartingFolder) &&
                 Directory.Exists(_appState.Settings.PlaylistEditorCustomStartingFolder))
        {
            initialFolder = _appState.Settings.PlaylistEditorCustomStartingFolder;
        }

        if (initialFolder != null)
        {
            string? driveRoot = Path.GetPathRoot(initialFolder);
            ComboBoxItem? matchingItem = null;

            if (!string.IsNullOrEmpty(driveRoot))
            {
                foreach (ComboBoxItem item in CmbDrives.Items)
                {
                    if (item.Tag is string tag && string.Equals(tag.TrimEnd('\\'), driveRoot.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    {
                        matchingItem = item;
                        break;
                    }
                }
            }

            if (matchingItem != null)
            {
                CmbDrives.SelectedItem = matchingItem;
                NavigateToFolder(initialFolder);
                return;
            }
        }

        if (CmbDrives.Items.Count > 0)
        {
            CmbDrives.SelectedIndex = 0;
        }
    }

    private void CmbDrives_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbDrives.SelectedItem is ComboBoxItem item && item.Tag is string rootPath)
        {
            LoadDriveRoot(rootPath);
        }
    }

    private void LoadDriveRoot(string rootPath)
    {
        TreeFolders.Items.Clear();
        LstAudioFiles.Items.Clear();
        TxtFileCount.Text = "0 files";

        var rootItem = new FolderItem
        {
            Header = rootPath,
            FullPath = rootPath
        };
        rootItem.Children.Add(null); // Dummy child for expansion arrow
        TreeFolders.Items.Add(rootItem);
    }

    private void PopulateFolderChildren(FolderItem folder)
    {
        if (folder.IsLoaded) return;
        folder.Children.Clear();
        try
        {
            var dir = new DirectoryInfo(folder.FullPath);
            foreach (var sub in dir.GetDirectories())
            {
                if ((sub.Attributes & FileAttributes.Hidden) != 0 ||
                    (sub.Attributes & FileAttributes.System) != 0)
                    continue;

                var subItem = new FolderItem
                {
                    Header = sub.Name,
                    FullPath = sub.FullName
                };
                subItem.Children.Add(null); // Dummy child
                folder.Children.Add(subItem);
            }
        }
        catch { /* Ignore access denied */ }
        folder.IsLoaded = true;
    }

    private void NavigateToFolder(string targetFolder)
    {
        try
        {
            if (TreeFolders.Items.Count == 0 || !(TreeFolders.Items[0] is FolderItem rootItem))
                return;

            string? driveRoot = Path.GetPathRoot(targetFolder);
            if (string.IsNullOrEmpty(driveRoot)) return;

            if (string.Equals(targetFolder.TrimEnd('\\'), driveRoot.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                LoadFolderAudioFiles(targetFolder);
                return;
            }

            string rel = Path.GetRelativePath(driveRoot, targetFolder);
            string[] parts = rel.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);

            FolderItem current = rootItem;
            foreach (string part in parts)
            {
                PopulateFolderChildren(current);
                var next = current.Children.FirstOrDefault(c => c != null && string.Equals(c.Header, part, StringComparison.OrdinalIgnoreCase));
                if (next == null) break;
                current = next;
            }

            PopulateFolderChildren(current);
            LoadFolderAudioFiles(current.FullPath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to navigate to starting folder: {ex.Message}");
        }
    }

    private void TreeItem_Expanded(object sender, RoutedEventArgs e)
    {
        if (sender is TreeViewItem tvi && tvi.DataContext is FolderItem folder && !folder.IsLoaded)
        {
            PopulateFolderChildren(folder);
        }
    }

    private void TreeFolders_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is FolderItem folder)
        {
            LoadFolderAudioFiles(folder.FullPath);
            _appState.Settings.LastBrowsedFolder = folder.FullPath;
        }
    }

    private void LoadFolderAudioFiles(string folderPath)
    {
        LstAudioFiles.Items.Clear();
        try
        {
            var dir = new DirectoryInfo(folderPath);
            var supported = new HashSet<string>(PlaylistParserService.SupportedAudioExtensions, StringComparer.OrdinalIgnoreCase);

            var files = dir.GetFiles()
                .Where(f => supported.Contains(f.Extension))
                .OrderBy(f => f.Name)
                .ToList();

            foreach (var f in files)
            {
                LstAudioFiles.Items.Add(new AudioFileEntry
                {
                    Name = f.Name,
                    FullPath = f.FullName,
                    Length = f.Length
                });
            }

            TxtFileCount.Text = $"{files.Count} files";
        }
        catch (Exception ex)
        {
            TxtFileCount.Text = "Access denied";
            System.Diagnostics.Debug.WriteLine($"Failed to load folder files: {ex.Message}");
        }
    }

    #endregion

    #region Drag and Drop: Left -> Right

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T parent)
                return parent;
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    private void LstAudioFiles_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _leftDragStartPoint = e.GetPosition(this);
        _leftIsMouseDownOnSelected = false;
        _leftPotentialSingleSelect = null;

        var hit = VisualTreeHelper.HitTest(LstAudioFiles, e.GetPosition(LstAudioFiles));
        if (hit != null)
        {
            var item = FindVisualParent<System.Windows.Controls.ListViewItem>(hit.VisualHit);
            if (item != null && item.DataContext is AudioFileEntry entry)
            {
                if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0)
                {
                    return;
                }

                if (LstAudioFiles.SelectedItems.Contains(entry) && LstAudioFiles.SelectedItems.Count > 1)
                {
                    // Prevent WPF from collapsing multi-selection before drag starts
                    _leftIsMouseDownOnSelected = true;
                    _leftPotentialSingleSelect = entry;
                    e.Handled = true;
                }
            }
        }
    }

    private void LstAudioFiles_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_leftIsMouseDownOnSelected && _leftPotentialSingleSelect != null)
        {
            LstAudioFiles.SelectedItems.Clear();
            LstAudioFiles.SelectedItem = _leftPotentialSingleSelect;
        }
        _leftIsMouseDownOnSelected = false;
        _leftPotentialSingleSelect = null;
    }

    private void LstAudioFiles_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && LstAudioFiles.SelectedItems.Count > 0)
        {
            System.Windows.Point mousePos = e.GetPosition(this);
            Vector diff = _leftDragStartPoint - mousePos;

            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                _leftIsMouseDownOnSelected = false;
                _leftPotentialSingleSelect = null;

                var selectedFiles = LstAudioFiles.SelectedItems.OfType<AudioFileEntry>()
                    .Select(f => f.FullPath)
                    .ToArray();

                if (selectedFiles.Length > 0)
                {
                    var data = new System.Windows.DataObject(System.Windows.DataFormats.FileDrop, selectedFiles);
                    System.Windows.DragDrop.DoDragDrop(LstAudioFiles, data, System.Windows.DragDropEffects.Copy);
                }
            }
        }
    }

    #endregion

    #region Right Panel: Reorder, Drop, Delete, Trash

    private void LstPlaylistTracks_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _rightDragStartPoint = e.GetPosition(this);
        _rightIsMouseDownOnSelected = false;
        _rightPotentialSingleSelect = null;

        var hit = VisualTreeHelper.HitTest(LstPlaylistTracks, e.GetPosition(LstPlaylistTracks));
        if (hit != null)
        {
            var item = FindVisualParent<System.Windows.Controls.ListViewItem>(hit.VisualHit);
            if (item != null && item.DataContext is TrackItem track)
            {
                if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0)
                {
                    return;
                }

                if (LstPlaylistTracks.SelectedItems.Contains(track) && LstPlaylistTracks.SelectedItems.Count > 1)
                {
                    // Prevent WPF from collapsing multi-selection before drag starts
                    _rightIsMouseDownOnSelected = true;
                    _rightPotentialSingleSelect = track;
                    e.Handled = true;
                }
            }
        }
    }

    private void LstPlaylistTracks_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_rightIsMouseDownOnSelected && _rightPotentialSingleSelect != null)
        {
            LstPlaylistTracks.SelectedItems.Clear();
            LstPlaylistTracks.SelectedItem = _rightPotentialSingleSelect;
        }
        _rightIsMouseDownOnSelected = false;
        _rightPotentialSingleSelect = null;
    }

    private void LstPlaylistTracks_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && LstPlaylistTracks.SelectedItems.Count > 0)
        {
            System.Windows.Point mousePos = e.GetPosition(this);
            Vector diff = _rightDragStartPoint - mousePos;

            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                _rightIsMouseDownOnSelected = false;
                _rightPotentialSingleSelect = null;

                _draggedTracks = LstPlaylistTracks.SelectedItems.OfType<TrackItem>().ToList();
                var data = new System.Windows.DataObject("SongSyncTracks", _draggedTracks);
                System.Windows.DragDrop.DoDragDrop(LstPlaylistTracks, data, System.Windows.DragDropEffects.Move | System.Windows.DragDropEffects.Copy);
            }
        }
    }

    private void LstPlaylistTracks_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (_draggedTracks != null && _draggedTracks.Count > 0)
        {
            e.Effects = System.Windows.DragDropEffects.Move;
            e.Handled = true;
        }
        else if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            e.Effects = System.Windows.DragDropEffects.Copy;
            e.Handled = true;
        }
        else
        {
            e.Effects = System.Windows.DragDropEffects.None;
        }
    }

    private void LstPlaylistTracks_Drop(object sender, System.Windows.DragEventArgs e)
    {
        // 1. Check if reordering existing tracks
        if (_draggedTracks != null && _draggedTracks.Count > 0)
        {
            int targetIndex = GetDropTargetIndex(e);
            var tracksToMove = _draggedTracks.ToList();

            TrackItem? targetTrack = (targetIndex >= 0 && targetIndex < _playlist.Tracks.Count)
                ? _playlist.Tracks[targetIndex]
                : null;

            foreach (var t in tracksToMove)
            {
                _playlist.Tracks.Remove(t);
            }

            int insertIndex = targetTrack != null ? _playlist.Tracks.IndexOf(targetTrack) : _playlist.Tracks.Count;
            if (insertIndex < 0) insertIndex = _playlist.Tracks.Count;

            for (int i = 0; i < tracksToMove.Count; i++)
            {
                _playlist.Tracks.Insert(insertIndex + i, tracksToMove[i]);
            }

            _playlist.RenumberTracks();
            _playlist.IsDirty = true;
            _draggedTracks = null;

            // Re-select moved tracks
            LstPlaylistTracks.SelectedItems.Clear();
            foreach (var t in tracksToMove)
            {
                LstPlaylistTracks.SelectedItems.Add(t);
            }

            UpdateHeaderStats();
            e.Handled = true;
            return;
        }

        // 2. Check if external or left-panel files dropped
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                int insertIndex = GetDropTargetIndex(e);
                if (insertIndex < 0 || insertIndex > _playlist.Tracks.Count)
                {
                    insertIndex = _playlist.Tracks.Count;
                }

                var supported = new HashSet<string>(PlaylistParserService.SupportedAudioExtensions, StringComparer.OrdinalIgnoreCase);
                var newTracks = new List<TrackItem>();

                foreach (string file in files)
                {
                    if (Directory.Exists(file))
                    {
                        try
                        {
                            var dirFiles = Directory.GetFiles(file, "*.*", SearchOption.AllDirectories)
                                .Where(f => supported.Contains(Path.GetExtension(f)))
                                .OrderBy(f => f);

                            foreach (var df in dirFiles)
                            {
                                var track = CreateTrackFromFile(df);
                                _playlist.Tracks.Insert(insertIndex++, track);
                                newTracks.Add(track);
                            }
                        }
                        catch { }
                    }
                    else if (File.Exists(file) && supported.Contains(Path.GetExtension(file)))
                    {
                        var track = CreateTrackFromFile(file);
                        _playlist.Tracks.Insert(insertIndex++, track);
                        newTracks.Add(track);
                    }
                }

                if (newTracks.Count > 0)
                {
                    _playlist.RenumberTracks();
                    _playlist.IsDirty = true;

                    // Select newly added tracks
                    LstPlaylistTracks.SelectedItems.Clear();
                    foreach (var t in newTracks)
                    {
                        LstPlaylistTracks.SelectedItems.Add(t);
                    }

                    UpdateHeaderStats();
                }

                e.Handled = true;
            }
        }
    }

    private TrackItem CreateTrackFromFile(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        string artist = string.Empty;
        string title = name;

        int dashIdx = name.IndexOf(" - ", StringComparison.Ordinal);
        if (dashIdx > 0)
        {
            artist = name.Substring(0, dashIdx).Trim();
            title = name.Substring(dashIdx + 3).Trim();
        }

        return new TrackItem
        {
            FilePath = path,
            Title = title,
            Artist = artist,
            IsMissing = !File.Exists(path)
        };
    }

    private int GetDropTargetIndex(System.Windows.DragEventArgs e)
    {
        var hit = VisualTreeHelper.HitTest(LstPlaylistTracks, e.GetPosition(LstPlaylistTracks));
        if (hit != null)
        {
            var item = FindVisualParent<System.Windows.Controls.ListViewItem>(hit.VisualHit);
            if (item != null)
            {
                int index = LstPlaylistTracks.ItemContainerGenerator.IndexFromContainer(item);
                if (index >= 0)
                {
                    System.Windows.Point posInItem = e.GetPosition(item);
                    if (posInItem.Y > item.ActualHeight / 2.0)
                    {
                        return index + 1;
                    }
                    return index;
                }
            }
        }
        return _playlist.Tracks.Count;
    }

    private void LstPlaylistTracks_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            DeleteSelectedTracks();
            e.Handled = true;
        }
    }

    private void DeleteSelectedTracks()
    {
        var selected = LstPlaylistTracks.SelectedItems.OfType<TrackItem>().ToList();
        if (selected.Count == 0) return;

        foreach (var t in selected)
        {
            _playlist.Tracks.Remove(t);
        }

        _playlist.RenumberTracks();
        _playlist.IsDirty = true;
        UpdateHeaderStats();
    }

    private void BorderTrash_DragEnter(object sender, System.Windows.DragEventArgs e)
    {
        BorderTrash.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(80, 20, 20));
        TxtTrashLabel.Text = "Release to Delete!";
    }

    private void BorderTrash_DragLeave(object sender, System.Windows.DragEventArgs e)
    {
        BorderTrash.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 26, 30));
        TxtTrashLabel.Text = "Drop here to Remove";
    }

    private void BorderTrash_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (_draggedTracks != null && _draggedTracks.Count > 0)
        {
            e.Effects = System.Windows.DragDropEffects.Move;
            e.Handled = true;
        }
        else
        {
            e.Effects = System.Windows.DragDropEffects.None;
        }
    }

    private void BorderTrash_Drop(object sender, System.Windows.DragEventArgs e)
    {
        BorderTrash.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 26, 30));
        TxtTrashLabel.Text = "Drop here to Remove";

        if (_draggedTracks != null && _draggedTracks.Count > 0)
        {
            foreach (var t in _draggedTracks)
            {
                _playlist.Tracks.Remove(t);
            }
            _playlist.RenumberTracks();
            _playlist.IsDirty = true;
            _draggedTracks = null;
            UpdateHeaderStats();
            e.Handled = true;
        }
        else
        {
            DeleteSelectedTracks();
        }
    }

    #endregion

    #region Toolbar Actions: New, Save, Save As, Delete, Close

    private void BtnNew_Click(object sender, RoutedEventArgs e)
    {
        if (_playlist.IsDirty)
        {
            var result = System.Windows.MessageBox.Show(
                "You have unsaved changes in the current playlist. Do you want to save them first?",
                "Unsaved Changes",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                if (!SaveCurrentPlaylist()) return;
            }
            else if (result == MessageBoxResult.Cancel)
            {
                return;
            }
        }

        _isLoadedAndReady = false;
        _playlist = new Playlist { Title = "Untitled Playlist" };
        _originalTitle = _playlist.Title;
        _playlist.PropertyChanged += Playlist_PropertyChanged;
        DataContext = _playlist;
        LstPlaylistTracks.ItemsSource = _playlist.Tracks;
        TxtPlaylistTitle.Text = _playlist.Title;
        _playlist.IsDirty = false;
        UpdateHeaderStats();
        _isLoadedAndReady = true;
        TxtStatus.Text = "Started new playlist.";
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentPlaylist();
    }

    private bool SaveCurrentPlaylist()
    {
        if (string.IsNullOrWhiteSpace(_playlist.FilePath) || _playlist.Format != ".m3u8")
        {
            return SavePlaylistAs();
        }

        try
        {
            _appState.PlaylistParser.SavePlaylist(_playlist, _playlist.FilePath);
            _playlist.IsDirty = false;
            _originalTitle = _playlist.Title;
            _appState.AudioPlayer.CurrentPlaylist = _playlist.Clone();
            TxtStatus.Text = $"Saved successfully to {_playlist.FilePath}";
            return true;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Failed to save playlist:\n{ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void BtnSaveAs_Click(object sender, RoutedEventArgs e)
    {
        SavePlaylistAs();
    }

    private bool SavePlaylistAs()
    {
        using var sfd = new System.Windows.Forms.SaveFileDialog
        {
            Title = "Save Playlist As",
            Filter = "M3U8 Playlist (*.m3u8)|*.m3u8",
            DefaultExt = "m3u8",
            FileName = $"{_playlist.Title}.m3u8"
        };

        if (sfd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            try
            {
                _appState.PlaylistParser.SavePlaylist(_playlist, sfd.FileName);
                _playlist.FilePath = sfd.FileName;
                _playlist.Format = ".m3u8";
                _playlist.IsDirty = false;
                _originalTitle = _playlist.Title;
                _appState.AudioPlayer.CurrentPlaylist = _playlist.Clone();
                TxtStatus.Text = $"Saved as {sfd.FileName}";
                return true;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to save playlist:\n{ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        return false;
    }

    private void BtnDeletePlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_playlist.FilePath) || !File.Exists(_playlist.FilePath))
        {
            System.Windows.MessageBox.Show("This playlist has not been saved to disk yet.", "Delete Playlist", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = System.Windows.MessageBox.Show(
            $"Are you sure you want to permanently delete the playlist file:\n\n{_playlist.FilePath}\n\n(Audio files will NOT be deleted)",
            "Confirm Delete Playlist",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                File.Delete(_playlist.FilePath);
                if (_appState.AudioPlayer.CurrentPlaylist?.FilePath == _playlist.FilePath)
                {
                    _appState.AudioPlayer.CurrentPlaylist = null;
                }

                _isLoadedAndReady = false;
                _playlist = new Playlist { Title = "Untitled Playlist" };
                _originalTitle = _playlist.Title;
                _playlist.PropertyChanged += Playlist_PropertyChanged;
                DataContext = _playlist;
                LstPlaylistTracks.ItemsSource = _playlist.Tracks;
                TxtPlaylistTitle.Text = _playlist.Title;
                _playlist.IsDirty = false;
                UpdateHeaderStats();
                _isLoadedAndReady = true;
                TxtStatus.Text = "Playlist file deleted.";
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to delete playlist file:\n{ex.Message}", "Delete Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_playlist.IsDirty)
        {
            var result = System.Windows.MessageBox.Show(
                "You have unsaved changes in this playlist.\n\nDo you want to save before closing?",
                "Unsaved Changes",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                if (!SaveCurrentPlaylist())
                {
                    e.Cancel = true; // cancel closing if save failed or cancelled
                }
            }
            else if (result == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
            }
        }
    }

    #endregion
}

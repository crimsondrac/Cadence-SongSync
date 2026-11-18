using System.IO;
using System.Windows;
using CadenceSongSync.Models;
using CadenceSongSync.Services;

namespace CadenceSongSync.Views;

public partial class SettingsWindow : Window
{
    private readonly AppStateService _appState;

    public SettingsWindow()
    {
        InitializeComponent();
        _appState = AppStateService.Instance;

        LoadSettings();
    }

    private void LoadSettings()
    {
        var s = _appState.Settings;

        switch (s.FolderStructureMode)
        {
            case FolderStructureMode.PreserveImmediateFolder:
                RbFolderImmediate.IsChecked = true;
                break;
            case FolderStructureMode.PreserveRelativePath:
                RbFolderRelative.IsChecked = true;
                break;
            case FolderStructureMode.Flat:
                RbFolderFlat.IsChecked = true;
                break;
        }

        switch (s.PlaylistExportMode)
        {
            case PlaylistExportMode.GenerateM3U8:
                RbExportGenerateM3U8.IsChecked = true;
                break;
            case PlaylistExportMode.CopyOriginalAsIs:
                RbExportCopyOriginal.IsChecked = true;
                break;
            case PlaylistExportMode.None:
                RbExportNone.IsChecked = true;
                break;
        }

        if (s.FilenameMode == FilenameMode.PrefixTrackNumber)
        {
            RbPrefixNumber.IsChecked = true;
        }
        else
        {
            RbPreserve.IsChecked = true;
        }

        ChkSanitize.IsChecked = s.SanitizeFilenames;
        ChkClearDestination.IsChecked = s.ClearDestinationFolder;

        ChkStartWithWindows.IsChecked = s.StartWithWindows;
        ChkHideSplashBanner.IsChecked = s.HideSplashBannerOnStartup;

        ChkEditorOpenLastFolder.IsChecked = s.PlaylistEditorOpenLastFolder;
        TxtEditorStartingFolder.Text = s.PlaylistEditorCustomStartingFolder ?? string.Empty;
    }

    private void BtnBrowseStartingFolder_Click(object sender, RoutedEventArgs e)
    {
        using var fbd = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select Default Starting Folder for Playlist Editor",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        if (!string.IsNullOrWhiteSpace(TxtEditorStartingFolder.Text) && Directory.Exists(TxtEditorStartingFolder.Text))
        {
            fbd.InitialDirectory = TxtEditorStartingFolder.Text;
        }

        if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            TxtEditorStartingFolder.Text = fbd.SelectedPath;
        }
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var s = _appState.Settings;

        if (RbFolderImmediate.IsChecked == true)
            s.FolderStructureMode = FolderStructureMode.PreserveImmediateFolder;
        else if (RbFolderRelative.IsChecked == true)
            s.FolderStructureMode = FolderStructureMode.PreserveRelativePath;
        else
            s.FolderStructureMode = FolderStructureMode.Flat;

        if (RbExportGenerateM3U8.IsChecked == true)
        {
            s.PlaylistExportMode = PlaylistExportMode.GenerateM3U8;
            s.CopyM3U8ToDestination = true;
        }
        else if (RbExportCopyOriginal.IsChecked == true)
        {
            s.PlaylistExportMode = PlaylistExportMode.CopyOriginalAsIs;
            s.CopyM3U8ToDestination = false;
        }
        else
        {
            s.PlaylistExportMode = PlaylistExportMode.None;
            s.CopyM3U8ToDestination = false;
        }

        s.FilenameMode = (RbPrefixNumber.IsChecked == true) ? FilenameMode.PrefixTrackNumber : FilenameMode.Preserve;
        s.SanitizeFilenames = ChkSanitize.IsChecked == true;
        s.ClearDestinationFolder = ChkClearDestination.IsChecked == true;

        s.StartWithWindows = ChkStartWithWindows.IsChecked == true;
        s.HideSplashBannerOnStartup = ChkHideSplashBanner.IsChecked == true;
        _appState.SettingsService.ApplyStartupRegistration(s.StartWithWindows);

        s.PlaylistEditorOpenLastFolder = ChkEditorOpenLastFolder.IsChecked == true;
        s.PlaylistEditorCustomStartingFolder = string.IsNullOrWhiteSpace(TxtEditorStartingFolder.Text)
            ? null
            : TxtEditorStartingFolder.Text.Trim();

        _appState.SaveSettings();
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

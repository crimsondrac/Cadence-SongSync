using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CadenceSongSync.Models;
using CadenceSongSync.Services;

namespace CadenceSongSync.Views;

public partial class ManagePresetsWindow : Window
{
    private readonly AppStateService _appState;
    private Preset? _selectedPreset;

    public ManagePresetsWindow()
    {
        InitializeComponent();
        _appState = AppStateService.Instance;

        RefreshPresetList();
        if (_appState.Settings.Presets.Count > 0)
        {
            LstPresets.SelectedIndex = 0;
        }
        else
        {
            _selectedPreset = null;
            PnlPresetDetails.IsEnabled = false;
            ClearPresetForm();
        }
    }

    private void RefreshPresetList()
    {
        LstPresets.ItemsSource = null;
        LstPresets.ItemsSource = _appState.Settings.Presets;
    }

    private void LstPresets_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LstPresets.SelectedItem is Preset preset)
        {
            _selectedPreset = preset;
            LoadPresetIntoView(preset);
            PnlPresetDetails.IsEnabled = true;
        }
        else if (_selectedPreset == null || !_appState.Settings.Presets.Contains(_selectedPreset))
        {
            PnlPresetDetails.IsEnabled = _selectedPreset != null;
            if (_selectedPreset == null)
            {
                ClearPresetForm();
            }
        }
    }

    private void LoadPresetIntoView(Preset preset)
    {
        TxtName.Text = preset.Name;
        TxtPlaylistPath.Text = preset.PlaylistPath;
        TxtDestinationPreview.Text = preset.DestinationDisplay;
        TxtMetaDriveLetter.Text = string.IsNullOrEmpty(preset.DriveLetter) ? "(None)" : preset.DriveLetter;
        TxtMetaVolumeLabel.Text = string.IsNullOrEmpty(preset.VolumeLabel) ? "(None)" : preset.VolumeLabel;
        TxtMetaSubfolder.Text = string.IsNullOrEmpty(preset.RelativeSubfolder) ? "(Root)" : preset.RelativeSubfolder;

        if (preset.OnFileExists == ExistingFileAction.Overwrite)
        {
            RbExistsOverwrite.IsChecked = true;
        }
        else
        {
            RbExistsSkip.IsChecked = true;
        }

        ChkPrefixTracks.IsChecked = preset.PrefixTrackNumbers;
    }

    private void ClearPresetForm()
    {
        TxtName.Text = string.Empty;
        TxtPlaylistPath.Text = string.Empty;
        TxtDestinationPreview.Text = string.Empty;
        TxtMetaDriveLetter.Text = "(None)";
        TxtMetaVolumeLabel.Text = "(None)";
        TxtMetaSubfolder.Text = "(Root)";
        RbExistsSkip.IsChecked = true;
        ChkPrefixTracks.IsChecked = false;
    }

    private void BtnAddPreset_Click(object sender, RoutedEventArgs e)
    {
        // Start a draft preset without saving to settings until Save Preset is clicked
        _selectedPreset = new Preset
        {
            Name = $"Preset {_appState.Settings.Presets.Count + 1}",
            OnFileExists = ExistingFileAction.Skip,
            PrefixTrackNumbers = false
        };

        LstPresets.SelectedIndex = -1;
        LoadPresetIntoView(_selectedPreset);
        PnlPresetDetails.IsEnabled = true;
        TxtName.Focus();
        TxtName.SelectAll();
    }

    private void BtnDeletePreset_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPreset == null) return;

        if (_appState.Settings.Presets.Contains(_selectedPreset))
        {
            var result = MessageBox.Show(
                $"Delete preset '{_selectedPreset.Name}'?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _appState.Settings.Presets.Remove(_selectedPreset);
                _appState.SaveSettings();
                RefreshPresetList();
                if (_appState.Settings.Presets.Count > 0)
                {
                    LstPresets.SelectedIndex = 0;
                }
                else
                {
                    _selectedPreset = null;
                    PnlPresetDetails.IsEnabled = false;
                    ClearPresetForm();
                }
            }
        }
        else
        {
            // Draft preset cancelled
            _selectedPreset = null;
            PnlPresetDetails.IsEnabled = false;
            ClearPresetForm();
        }
    }

    private void BtnBrowsePlaylist_Click(object sender, RoutedEventArgs e)
    {
        using var ofd = new System.Windows.Forms.OpenFileDialog
        {
            Title = "Select Playlist for Preset",
            Filter = PlaylistParserService.SupportedFileFilter,
            CheckFileExists = true
        };

        if (ofd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            TxtPlaylistPath.Text = ofd.FileName;
        }
    }

    private void BtnBrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPreset == null) return;

        using var fbd = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select target drive / folder for preset:",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            string chosenPath = fbd.SelectedPath;
            _selectedPreset.SetDestination(chosenPath);

            // Update destination UI fields ONLY, preserving current TxtName and TxtPlaylistPath
            TxtDestinationPreview.Text = _selectedPreset.DestinationDisplay;
            TxtMetaDriveLetter.Text = string.IsNullOrEmpty(_selectedPreset.DriveLetter) ? "(None)" : _selectedPreset.DriveLetter;
            TxtMetaVolumeLabel.Text = string.IsNullOrEmpty(_selectedPreset.VolumeLabel) ? "(None)" : _selectedPreset.VolumeLabel;
            TxtMetaSubfolder.Text = string.IsNullOrEmpty(_selectedPreset.RelativeSubfolder) ? "(Root)" : _selectedPreset.RelativeSubfolder;
        }
    }

    private void BtnSavePreset_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPreset == null) return;

        _selectedPreset.Name = string.IsNullOrWhiteSpace(TxtName.Text) ? "Untitled Preset" : TxtName.Text.Trim();
        _selectedPreset.PlaylistPath = TxtPlaylistPath.Text.Trim();
        _selectedPreset.OnFileExists = (RbExistsOverwrite.IsChecked == true) ? ExistingFileAction.Overwrite : ExistingFileAction.Skip;
        _selectedPreset.PrefixTrackNumbers = ChkPrefixTracks.IsChecked == true;

        if (!_appState.Settings.Presets.Contains(_selectedPreset))
        {
            _appState.Settings.Presets.Add(_selectedPreset);
        }

        _appState.SaveSettings();
        RefreshPresetList();
        LstPresets.SelectedItem = _selectedPreset;

        MessageBox.Show("Preset saved successfully.", "Preset Saved", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnSyncNow_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPreset == null) return;

        BtnSavePreset_Click(sender, e);
        _appState.TriggerPresetSync(_selectedPreset);
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

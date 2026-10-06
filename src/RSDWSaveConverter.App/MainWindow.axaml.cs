using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using RSDWSaveConverter.Core;
using RSDWSaveConverter.Core.Wgs;

namespace RSDWSaveConverter.App;

public partial class MainWindow : Window
{
    private LoadedSave? _loadedSave;

    public MainWindow()
    {
        InitializeComponent();
        WireEvents();
        UpdateActions();

        Opened += async (_, _) => await RefreshProfilesAsync();
    }

    private void WireEvents()
    {
        ChooseSaveButton.Click += async (_, _) => await ChooseSaveAsync();
        ExportPayloadButton.Click += async (_, _) => await ExportPayloadAsync();
        RefreshProfilesButton.Click += async (_, _) => await RefreshProfilesAsync();
        ChooseProfileButton.Click += async (_, _) => await ChooseProfileAsync();
        ProfilePicker.SelectionChanged += (_, _) => PopulateDestinations();
        DestinationPicker.SelectionChanged += (_, _) => UpdateActions();
        ImportButton.Click += async (_, _) => await ImportAsync();

        DropZone.AddHandler(DragDrop.DragOverEvent, HandleDragOver);
        DropZone.AddHandler(DragDrop.DropEvent, HandleDrop);
    }

    private void HandleDragOver(object? sender, DragEventArgs e)
    {
        var files = e.Data.GetFiles();
        var hasValidFile = files != null && files.Any(f =>
        {
            var path = f.TryGetLocalPath() ?? f.Path?.LocalPath;
            return path != null && IsSupportedFile(path);
        });

        e.DragEffects = hasValidFile ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void HandleDrop(object? sender, DragEventArgs e)
    {
        var files = e.Data.GetFiles();
        var valid = files?.FirstOrDefault(f =>
        {
            var path = f.TryGetLocalPath() ?? f.Path?.LocalPath;
            return path != null && IsSupportedFile(path);
        });

        if (valid != null)
        {
            var path = valid.TryGetLocalPath() ?? valid.Path?.LocalPath;
            if (path != null)
            {
                await LoadSaveAsync(path);
            }
        }
    }

    private static bool IsSupportedFile(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".sav", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".xav", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".json", StringComparison.OrdinalIgnoreCase);
    }

    private async Task ChooseSaveAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a Dragonwilds save",
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("Dragonwilds saves (*.sav; *.xav; *.json)")
                {
                    Patterns = new[] { "*.sav", "*.xav", "*.json" }
                },
                new("World saves (*.sav; *.xav)")
                {
                    Patterns = new[] { "*.sav", "*.xav" }
                },
                new("Character saves (*.json)")
                {
                    Patterns = new[] { "*.json" }
                },
                new("All files (*.*)")
                {
                    Patterns = new[] { "*.*" }
                }
            }
        });

        var file = files.FirstOrDefault();
        if (file != null)
        {
            var path = file.TryGetLocalPath() ?? file.Path.LocalPath;
            if (path != null)
            {
                await LoadSaveAsync(path);
            }
        }
    }

    private async Task LoadSaveAsync(string path)
    {
        _loadedSave = null;
        FileNameText.Text = "No save selected";
        FileDetailsText.Text = "";
        PopulateDestinations();

        await RunBusyAsync("Reading save...", async () =>
        {
            var fallback = Path.GetFileNameWithoutExtension(path);
            if (Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                var data = await File.ReadAllBytesAsync(path);
                var metadata = DragonwildsCharacterCodec.ReadMetadata(data, fallback);
                _loadedSave = new LoadedSave(
                    SaveKind.Character,
                    metadata.CharacterName,
                    Path.GetFileName(path),
                    data);
            }
            else
            {
                var raw = await Task.Run(() => DragonwildsSaveCodec.ReadRawSave(path));
                var metadata = DragonwildsSaveCodec.ReadMetadata(raw, fallback);
                _loadedSave = new LoadedSave(
                    SaveKind.World,
                    metadata.WorldName,
                    Path.GetFileName(path),
                    raw);
            }

            FileNameText.Text = _loadedSave.DisplayName;
            FileDetailsText.Text = $"{_loadedSave.Kind} save  |  {FormatByteSize(_loadedSave.Data.Length)}  |  {_loadedSave.FileName}";
            StatusText.Text = $"{_loadedSave.Kind} save ready";
        });

        PopulateDestinations();
        UpdateActions();
    }

    private async Task ExportPayloadAsync()
    {
        if (_loadedSave is not { Kind: SaveKind.World } loadedSave)
        {
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Game Pass payload",
            SuggestedFileName = DragonwildsSaveCodec.MakeSafeSlotName(loadedSave.DisplayName) + ".xav",
            DefaultExtension = "xav",
            FileTypeChoices = new List<FilePickerFileType>
            {
                new("Dragonwilds Game Pass payload (*.xav)")
                {
                    Patterns = new[] { "*.xav" }
                }
            }
        });

        if (file == null)
        {
            return;
        }

        var destinationPath = file.TryGetLocalPath() ?? file.Path.LocalPath;
        if (destinationPath == null)
        {
            return;
        }

        await RunBusyAsync("Converting save...", async () =>
        {
            var wrapped = await Task.Run(() => DragonwildsSaveCodec.Wrap(loadedSave.Data));
            await File.WriteAllBytesAsync(destinationPath, wrapped);
            StatusText.Text = $"Exported {Path.GetFileName(destinationPath)}";
        });
    }

    private async Task RefreshProfilesAsync()
    {
        await RunBusyAsync("Finding Game Pass profiles...", async () =>
        {
            var profiles = await Task.Run(WgsProfile.Discover);
            var choices = profiles.Select(profile => new ProfileChoice(profile)).ToList();
            ProfilePicker.ItemsSource = choices;
            ProfilePicker.SelectedIndex = choices.Count > 0 ? 0 : -1;

            ProfileStatusText.Text = profiles.Count == 0
                ? "Not detected"
                : profiles.Count == 1 ? "Detected" : $"{profiles.Count} detected";
            StatusText.Text = profiles.Count == 0
                ? "Choose the WGS profile folder manually"
                : "Game Pass profile ready";
        });

        PopulateDestinations();
    }

    private async Task ChooseProfileAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose the WGS profile folder containing containers.index",
            AllowMultiple = false
        });

        var folder = folders.FirstOrDefault();
        if (folder == null)
        {
            return;
        }

        var path = folder.TryGetLocalPath() ?? folder.Path.LocalPath;
        if (path == null)
        {
            return;
        }

        try
        {
            var profile = WgsProfile.Load(path);
            var choices = new List<ProfileChoice> { new(profile) };
            ProfilePicker.ItemsSource = choices;
            ProfilePicker.SelectedIndex = 0;
            ProfileStatusText.Text = "Selected";
            StatusText.Text = "Game Pass profile ready";
            PopulateDestinations();
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(exception);
        }
    }

    private void PopulateDestinations()
    {
        var profile = (ProfilePicker.SelectedItem as ProfileChoice)?.Profile;
        var choices = (_loadedSave?.Kind, profile) switch
        {
            (SaveKind.World, not null) => profile.WorldSlots
                .Select(DestinationChoice.FromWorld)
                .ToList(),
            (SaveKind.Character, not null) => profile.CharacterSlots
                .Select(DestinationChoice.FromCharacter)
                .ToList(),
            _ => []
        };

        DestinationPicker.ItemsSource = null;
        DestinationPicker.ItemsSource = choices;
        DestinationLabel.Text = _loadedSave?.Kind switch
        {
            SaveKind.World => "Replace an existing world",
            SaveKind.Character => "Replace an existing character",
            _ => "Choose a save to list compatible slots"
        };
        ImportButton.Content = _loadedSave?.Kind switch
        {
            SaveKind.World => "Import World",
            SaveKind.Character => "Import Character",
            _ => "Import Save"
        };

        if (_loadedSave is not null)
        {
            var matchingIndex = choices.FindIndex(choice =>
                choice.DisplayName.Equals(_loadedSave.DisplayName, StringComparison.OrdinalIgnoreCase)
                || choice.SlotName.Equals(_loadedSave.DisplayName, StringComparison.OrdinalIgnoreCase));
            if (matchingIndex >= 0)
            {
                DestinationPicker.SelectedIndex = matchingIndex;
            }
            else if (choices.Count > 0)
            {
                DestinationPicker.SelectedIndex = 0;
            }
        }

        UpdateActions();
    }

    private async Task ImportAsync()
    {
        if (_loadedSave is not { } loadedSave
            || ProfilePicker.SelectedItem is not ProfileChoice profileChoice
            || DestinationPicker.SelectedItem is not DestinationChoice destination
            || destination.Kind != loadedSave.Kind)
        {
            return;
        }

        var saveType = loadedSave.Kind.ToString().ToLowerInvariant();
        var confirmed = await MessageDialog.ShowConfirmAsync(
            this,
            $"Import Game Pass {saveType}",
            $"Replace '{destination.DisplayName}' with '{loadedSave.DisplayName}'?\n\n"
            + "A complete WGS backup will be created first. The existing in-game backup slot will remain unchanged.");

        if (!confirmed)
        {
            return;
        }

        await RunBusyAsync($"Importing {saveType}...", async () =>
        {
            var result = await Task.Run(() => loadedSave.Kind switch
            {
                SaveKind.World => new WgsImporter().ReplaceWorld(
                    profileChoice.Profile.Path,
                    destination.FileName,
                    loadedSave.Data),
                SaveKind.Character => new WgsImporter().ReplaceCharacter(
                    profileChoice.Profile.Path,
                    destination.FileName,
                    loadedSave.Data),
                _ => throw new InvalidOperationException("Unsupported save type.")
            });

            StatusText.Text = $"Imported {result.SaveName}";

            await MessageDialog.ShowInfoAsync(
                this,
                "Import complete",
                $"{result.SaveName} is ready in Game Pass.\n\nBackup:\n{result.BackupPath}\n\n"
                + "Launch Dragonwilds and choose the local save if Xbox asks which version to use.");
        });

        await RefreshProfilesAsync();
    }

    private async Task RunBusyAsync(string status, Func<Task> action)
    {
        SetBusy(true, status);
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            StatusText.Text = "Operation failed";
            await ShowErrorAsync(exception);
        }
        finally
        {
            SetBusy(false, StatusText.Text ?? "Ready");
        }
    }

    private void SetBusy(bool busy, string status)
    {
        StatusText.Text = status;
        ProgressBar.IsVisible = busy;
        ChooseSaveButton.IsEnabled = !busy;
        ChooseProfileButton.IsEnabled = !busy;
        RefreshProfilesButton.IsEnabled = !busy;
        ProfilePicker.IsEnabled = !busy;
        DestinationPicker.IsEnabled = !busy;

        if (busy)
        {
            ExportPayloadButton.IsEnabled = false;
            ImportButton.IsEnabled = false;
        }
        else
        {
            UpdateActions();
        }
    }

    private void UpdateActions()
    {
        ExportPayloadButton.IsEnabled = _loadedSave is { Kind: SaveKind.World };
        ImportButton.IsEnabled = _loadedSave is not null
            && ProfilePicker.SelectedItem is ProfileChoice
            && DestinationPicker.SelectedItem is DestinationChoice destination
            && destination.Kind == _loadedSave.Kind;
    }

    private static string FormatByteSize(int length) => length >= 1024 * 1024
        ? $"{length / 1024d / 1024d:N1} MB"
        : $"{length / 1024d:N1} KB";

    private async Task ShowErrorAsync(Exception exception)
    {
        await MessageDialog.ShowInfoAsync(this, "RSDW Save Converter Error", exception.Message);
    }

    private sealed record ProfileChoice(WgsProfile Profile)
    {
        public override string ToString() =>
            $"{Profile.WorldSlots.Count} world(s), {Profile.CharacterSlots.Count} character(s)  |  {new DirectoryInfo(Profile.Path).Name}";
    }

    private enum SaveKind
    {
        World,
        Character
    }

    private sealed record LoadedSave(
        SaveKind Kind,
        string DisplayName,
        string FileName,
        byte[] Data);

    private sealed record DestinationChoice(
        SaveKind Kind,
        string SlotName,
        string DisplayName,
        string FileName)
    {
        public static DestinationChoice FromWorld(WgsWorldSlot slot) => new(
            SaveKind.World,
            slot.SlotName,
            slot.DisplayName,
            slot.ActiveEntry.FileName);

        public static DestinationChoice FromCharacter(WgsCharacterSlot slot) => new(
            SaveKind.Character,
            slot.SlotName,
            slot.DisplayName,
            slot.ActiveEntry.FileName);

        public override string ToString() => DisplayName.Equals(SlotName, StringComparison.OrdinalIgnoreCase)
            ? DisplayName
            : $"{DisplayName} ({SlotName})";
    }
}

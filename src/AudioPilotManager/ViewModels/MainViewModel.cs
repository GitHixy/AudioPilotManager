using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Windows.Input;
using AudioPilotManager.Audio;
using AudioPilotManager.Mvvm;
using AudioPilotManager.Services;
using AudioPilotManager.Themes;

namespace AudioPilotManager.ViewModels;

public sealed record NavItem(string Title, string Glyph);

public sealed class MainViewModel : ObservableObject
{
    public const string PatreonUrl = "https://www.patreon.com/GitHixy";
    public const string RepoUrl = "https://github.com/GitHixy/AudioPilotManager";

    private readonly SettingsService _settingsService;
    private AudioDevice? _selectedMixerDevice;
    private bool _suppressMixerSelection;
    private int _selectedPage;
    private string _newProfileName = "";

    public MainViewModel(AudioService audio, SettingsService settings)
    {
        Audio = audio;
        _settingsService = settings;
        Updates = new UpdateViewModel(settings);

        Nav = new[]
        {
            new NavItem("Mixer", ""),
            new NavItem("Devices", ""),
            new NavItem("Profiles", ""),
            new NavItem("Settings", ""),
        };

        Hotkeys = new ObservableCollection<HotkeyItem>
        {
            new(this, "Mute microphone", "Toggle the default microphone on and off", Glyphs.Microphone,
                () => S.MicMuteHotkey, v => S.MicMuteHotkey = v),
            new(this, "Mute speakers", "Toggle the default output on and off", Glyphs.Volume,
                () => S.OutputMuteHotkey, v => S.OutputMuteHotkey = v),
            new(this, "Switch output", "Cycle the default output between your devices", Glyphs.Headphones,
                () => S.CycleOutputHotkey, v => S.CycleOutputHotkey = v),
            new(this, "Show / hide", "Bring Audio Pilot Manager up, or tuck it away", "",
                () => S.ToggleWindowHotkey, v => S.ToggleWindowHotkey = v),
        };

        foreach (var p in S.Profiles) Profiles.Add(new ProfileViewModel(this, p));
        foreach (var h in S.HiddenApps) HiddenApps.Add(h);

        audio.AppHidden += (_, key) => OnAppHidden(key);
        audio.DevicesChanged += (_, _) => RebuildMixerDevices();
        audio.DefaultsChanged += (_, _) => OnDefaultsChanged();
        audio.ShowInMixerRequested += (_, d) =>
        {
            SelectedMixerDevice = d;
            SelectedPage = 0;
        };
        RebuildMixerDevices();

        SaveProfileCommand = new RelayCommand(SaveProfile, () => !string.IsNullOrWhiteSpace(NewProfileName));
        ToggleMicMuteCommand = new RelayCommand(ToggleMicMute);
        ToggleOutputMuteCommand = new RelayCommand(ToggleOutputMute);
        CycleOutputCommand = new RelayCommand(CycleOutput);
        OpenPatreonCommand = new RelayCommand(() => OpenUrl(PatreonUrl));
        OpenRepoCommand = new RelayCommand(() => OpenUrl(RepoUrl));
        OpenIssuesCommand = new RelayCommand(() => OpenUrl(RepoUrl + "/issues"));
        OpenSettingsFolderCommand = new RelayCommand(() => OpenFolder(SettingsService.Directory));
        OpenLogsCommand = new RelayCommand(() => OpenFolder(Log.Directory));
        OpenSoundPanelCommand = new RelayCommand(() => Launch("control.exe", "mmsys.cpl"));
        ForgetAppVolumesCommand = new RelayCommand(() =>
        {
            S.AppMemory.Clear();
            Save();
            Osd("", "Remembered app volumes cleared");
        });
        UnhideAppCommand = new RelayCommand(p =>
        {
            if (p is not string key) return;
            S.HiddenApps.RemoveAll(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
            HiddenApps.Remove(key);
            Save();
            Audio.ApplyVisibility();
        });
        DismissTipsCommand = new RelayCommand(() => ShowTips = false);
        SuggestNameCommand = new RelayCommand(p => NewProfileName = p as string ?? "");
        GoToPageCommand = new RelayCommand(p =>
        {
            if (p is string s && int.TryParse(s, out var i)) SelectedPage = i;
        });
    }

    public AudioService Audio { get; }
    public UpdateViewModel Updates { get; }
    private AppSettings S => _settingsService.Current;

    public event Action<string, string>? OsdRequested;
    public event EventHandler? HotkeysChanged;
    public event EventHandler? WindowOptionsChanged;

    public string Version { get; } = "v" + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0");

    // ---------------------------------------------------------------- navigation --

    public IReadOnlyList<NavItem> Nav { get; }

    public int SelectedPage
    {
        get => _selectedPage;
        set => Set(ref _selectedPage, Math.Clamp(value, 0, 3));
    }

    // ---------------------------------------------------------------- mixer ------

    public ObservableCollection<AudioDevice> Outputs => Audio.Outputs;
    public ObservableCollection<AudioDevice> Inputs => Audio.Inputs;
    public ObservableCollection<AudioDevice> MixerDevices { get; } = new();

    public AudioDevice? SelectedMixerDevice
    {
        get => _selectedMixerDevice;
        set
        {
            if (_suppressMixerSelection || value is null && MixerDevices.Count > 0) return;
            if (!Set(ref _selectedMixerDevice, value)) return;
            Audio.MeteredDevice = value;
            if (value is not null)
            {
                // Picking the default output means "follow the default", so the mixer tracks device switches.
                S.MixerDeviceId = value.IsOutput && value.IsDefault ? null : value.Id;
                Save();
            }

            OnPropertyChanged(nameof(FollowsDefault));
        }
    }

    public bool FollowsDefault => S.MixerDeviceId is null;

    private void RebuildMixerDevices()
    {
        _suppressMixerSelection = true;
        MixerDevices.Clear();
        foreach (var d in Audio.Outputs) MixerDevices.Add(d);
        foreach (var d in Audio.Inputs) MixerDevices.Add(d);
        _suppressMixerSelection = false;

        var wanted = S.MixerDeviceId is { } id ? MixerDevices.FirstOrDefault(d => d.Id == id) : null;
        SelectMixer(wanted ?? Audio.DefaultOutput ?? MixerDevices.FirstOrDefault());
        OnPropertyChanged(nameof(HasDevices));
        RaiseDefaults();
    }

    private void SelectMixer(AudioDevice? device)
    {
        // Set the field directly so an automatic switch doesn't overwrite the user's choice.
        _selectedMixerDevice = device;
        Audio.MeteredDevice = device;
        OnPropertyChanged(nameof(SelectedMixerDevice));
        OnPropertyChanged(nameof(FollowsDefault));
    }

    private void OnDefaultsChanged()
    {
        if (FollowsDefault && Audio.DefaultOutput is { } d && d != _selectedMixerDevice)
            SelectMixer(d);
        RaiseDefaults();
        DefaultsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseDefaults()
    {
        OnPropertyChanged(nameof(DefaultOutput));
        OnPropertyChanged(nameof(DefaultInput));
        OnPropertyChanged(nameof(QuickOutput));
    }

    public AudioDevice? DefaultOutput => Audio.DefaultOutput;
    public AudioDevice? DefaultInput => Audio.DefaultInput;

    /// <summary>The quick flyout's output picker: choosing a device makes it the Windows default.</summary>
    public AudioDevice? QuickOutput
    {
        get => Audio.DefaultOutput;
        set
        {
            if (value is null || value.IsDefault) return;
            Audio.SetDefault(value, communications: false);
            RaiseDefaults();
        }
    }

    public event EventHandler? DefaultsChanged;

    public bool HasDevices => MixerDevices.Count > 0;

    public bool ShowTips
    {
        get => S.ShowFirstRunTips;
        set
        {
            S.ShowFirstRunTips = value;
            Save();
            OnPropertyChanged();
        }
    }

    // ---------------------------------------------------------------- quick actions --

    public ICommand ToggleMicMuteCommand { get; }
    public ICommand ToggleOutputMuteCommand { get; }
    public ICommand CycleOutputCommand { get; }

    public void ToggleMicMute()
    {
        if (Audio.DefaultInput is not { } mic)
        {
            Osd(Glyphs.MicrophoneOff, "No microphone found");
            return;
        }

        mic.IsMuted = !mic.IsMuted;
        Osd(mic.IsMuted ? Glyphs.MicrophoneOff : Glyphs.Microphone, mic.IsMuted ? "Microphone muted" : "Microphone on");
    }

    public void ToggleOutputMute()
    {
        if (Audio.DefaultOutput is not { } output) return;
        output.IsMuted = !output.IsMuted;
        Osd(output.VolumeGlyph, output.IsMuted ? $"{output.Name} muted" : $"{output.Name} · {output.Volume:0}%");
    }

    public void CycleOutput()
    {
        if (Audio.Outputs.Count < 2)
        {
            Osd(Glyphs.Speakers, "Only one output device is connected");
            return;
        }

        var next = Audio.CycleDefaultOutput();
        if (next is not null) Osd(next.Glyph, $"Now playing on {next.Name}");
    }

    public void Osd(string glyph, string text)
    {
        if (S.ShowOsd) OsdRequested?.Invoke(glyph, text);
    }

    // ---------------------------------------------------------------- profiles ---

    public ObservableCollection<ProfileViewModel> Profiles { get; } = new();

    public bool HasProfiles => Profiles.Count > 0;

    public string NewProfileName
    {
        get => _newProfileName;
        set => Set(ref _newProfileName, value);
    }

    public ICommand SaveProfileCommand { get; }
    public ICommand SuggestNameCommand { get; }

    public IReadOnlyList<string> ProfileSuggestions { get; } = new[] { "Gaming", "Meeting", "Music", "Streaming", "Night" };

    private void SaveProfile()
    {
        var name = NewProfileName.Trim();
        if (name.Length == 0) return;
        if (name.Length > 40) name = name[..40];

        var existing = Profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase));
        if (existing is not null)
        {
            existing.Update();
        }
        else
        {
            var model = Capture(name);
            S.Profiles.Add(model);
            Profiles.Add(new ProfileViewModel(this, model));
            OnPropertyChanged(nameof(HasProfiles));
            Save();
        }

        NewProfileName = "";
        Osd("", $"Saved profile “{name}”");
    }

    internal MixProfile Capture(string name)
    {
        var p = new MixProfile { Name = name, Saved = DateTime.Now, DefaultOutputId = Audio.DefaultOutput?.Id };
        foreach (var d in Audio.AllDevices)
        {
            p.Devices[d.Id] = new AppMemory { Volume = (float)(d.Volume / 100), Muted = d.IsMuted };
            foreach (var app in d.AllApps.Where(a => !a.IsSystem && !a.Key.StartsWith("pid:", StringComparison.Ordinal)))
                p.Apps[app.Key] = new AppMemory { Volume = (float)(app.Volume / 100), Muted = app.IsMuted && !app.IsSoloMuted };
        }

        return p;
    }

    internal void Apply(MixProfile p)
    {
        if (p.DefaultOutputId is { } id && Audio.Outputs.FirstOrDefault(d => d.Id == id) is { IsDefault: false } target)
            Audio.SetDefault(target, communications: false);

        foreach (var d in Audio.AllDevices)
        {
            if (!p.Devices.TryGetValue(d.Id, out var m)) continue;
            d.Volume = Math.Round(m.Volume * 100);
            d.IsMuted = m.Muted;
        }

        foreach (var d in Audio.AllDevices)
        foreach (var app in d.AllApps)
        {
            if (!p.Apps.TryGetValue(app.Key, out var m)) continue;
            app.Volume = Math.Round(m.Volume * 100);
            app.SetMuted(m.Muted, fromUser: true);
        }

        // Apps that aren't running yet pick their level up when they start.
        foreach (var (key, m) in p.Apps)
            S.AppMemory[key] = new AppMemory { Volume = m.Volume, Muted = m.Muted };
        Save();
        Osd("", $"Profile “{p.Name}” applied");
    }

    internal void Delete(ProfileViewModel vm)
    {
        S.Profiles.Remove(vm.Model);
        Profiles.Remove(vm);
        OnPropertyChanged(nameof(HasProfiles));
        Save();
    }

    internal void Replace(ProfileViewModel vm, MixProfile updated)
    {
        var i = S.Profiles.IndexOf(vm.Model);
        if (i >= 0) S.Profiles[i] = updated;
        Save();
    }

    // ---------------------------------------------------------------- settings ---

    public IReadOnlyList<AccentOption> Accents => ThemeService.Accents;

    public string Theme
    {
        get => S.Theme;
        set
        {
            if (S.Theme == value || value is null) return;
            S.Theme = value;
            ApplyTheme();
            OnPropertyChanged();
        }
    }

    public string Accent
    {
        get => S.Accent;
        set
        {
            if (S.Accent == value || value is null) return;
            S.Accent = value;
            ApplyTheme();
            OnPropertyChanged();
        }
    }

    public bool UseBackdrop
    {
        get => S.UseBackdrop;
        set
        {
            S.UseBackdrop = value;
            ApplyTheme();
            OnPropertyChanged();
        }
    }

    public bool BackdropSupported => ThemeService.BackdropSupported;

    private void ApplyTheme()
    {
        ThemeService.Apply(S.Theme, S.Accent, S.UseBackdrop);
        Save();
    }

    public bool StartWithWindows
    {
        get => StartupService.IsEnabled;
        set
        {
            StartupService.SetEnabled(value);
            OnPropertyChanged();
        }
    }

    public bool StartMinimized
    {
        get => S.StartMinimized;
        set => SetSetting(v => S.StartMinimized = v, value);
    }

    public bool CloseToTray
    {
        get => S.CloseToTray;
        set => SetSetting(v => S.CloseToTray = v, value);
    }

    public bool AlwaysOnTop
    {
        get => S.AlwaysOnTop;
        set
        {
            SetSetting(v => S.AlwaysOnTop = v, value);
            WindowOptionsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool RememberAppVolumes
    {
        get => S.RememberAppVolumes;
        set => SetSetting(v => S.RememberAppVolumes = v, value);
    }

    public bool ShowSystemSounds
    {
        get => S.ShowSystemSounds;
        set
        {
            SetSetting(v => S.ShowSystemSounds = v, value);
            Audio.ApplyVisibility();
        }
    }

    public bool LiveMicMeter
    {
        get => S.LiveMicMeter;
        set
        {
            SetSetting(v => S.LiveMicMeter = v, value);
            Audio.UpdateMonitoring();
        }
    }

    public bool ShowOsd
    {
        get => S.ShowOsd;
        set => SetSetting(v => S.ShowOsd = v, value);
    }

    public bool HotkeysEnabled
    {
        get => S.HotkeysEnabled;
        set
        {
            SetSetting(v => S.HotkeysEnabled = v, value);
            HotkeysChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ObservableCollection<HotkeyItem> Hotkeys { get; }

    internal void OnHotkeyEdited() => HotkeysChanged?.Invoke(this, EventArgs.Empty);

    public ObservableCollection<string> HiddenApps { get; } = new();

    public ICommand UnhideAppCommand { get; }
    public ICommand ForgetAppVolumesCommand { get; }
    public ICommand DismissTipsCommand { get; }
    public ICommand GoToPageCommand { get; }

    internal void OnAppHidden(string key)
    {
        if (!HiddenApps.Contains(key)) HiddenApps.Add(key);
    }

    private void SetSetting(Action<bool> set, bool value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        set(value);
        Save();
        OnPropertyChanged(name);
    }

    internal void Save() => _settingsService.Save();

    // ---------------------------------------------------------------- links ------

    public ICommand OpenPatreonCommand { get; }
    public ICommand OpenRepoCommand { get; }
    public ICommand OpenIssuesCommand { get; }
    public ICommand OpenSettingsFolderCommand { get; }
    public ICommand OpenLogsCommand { get; }
    public ICommand OpenSoundPanelCommand { get; }

    private static void OpenUrl(string url)
    {
        // Only our own fixed https links are ever opened.
        if (!url.StartsWith("https://", StringComparison.Ordinal)) return;
        Launch(url, null);
    }

    private static void OpenFolder(string path)
    {
        try
        {
            System.IO.Directory.CreateDirectory(path);
            Launch("explorer.exe", $"\"{path}\"");
        }
        catch (Exception ex)
        {
            Log.Error("Could not open folder", ex);
        }
    }

    private static void Launch(string file, string? args)
    {
        try
        {
            Process.Start(new ProcessStartInfo(file) { Arguments = args ?? "", UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"Could not open {file}", ex);
        }
    }
}

public sealed class ProfileViewModel : ObservableObject
{
    private readonly MainViewModel _owner;

    public ProfileViewModel(MainViewModel owner, MixProfile model)
    {
        _owner = owner;
        Model = model;
        ApplyCommand = new RelayCommand(() => _owner.Apply(Model));
        UpdateCommand = new RelayCommand(Update);
        DeleteCommand = new RelayCommand(() => _owner.Delete(this));
    }

    public MixProfile Model { get; private set; }

    public string Name => Model.Name;

    public string Summary
    {
        get
        {
            var apps = Model.Apps.Count;
            var devices = Model.Devices.Count;
            return $"{apps} app{(apps == 1 ? "" : "s")} · {devices} device{(devices == 1 ? "" : "s")} · saved {Model.Saved:d MMM, HH:mm}";
        }
    }

    public ICommand ApplyCommand { get; }
    public ICommand UpdateCommand { get; }
    public ICommand DeleteCommand { get; }

    internal void Update()
    {
        var updated = _owner.Capture(Model.Name);
        _owner.Replace(this, updated);
        Model = updated;
        OnPropertyChanged(nameof(Summary));
        _owner.Osd("", $"Profile “{Name}” updated");
    }
}

public sealed class HotkeyItem : ObservableObject
{
    private readonly MainViewModel _owner;
    private readonly Func<HotkeyBinding> _get;
    private readonly Action<HotkeyBinding> _set;
    private string? _status;

    public HotkeyItem(MainViewModel owner, string title, string description, string glyph, Func<HotkeyBinding> get, Action<HotkeyBinding> set)
    {
        _owner = owner;
        Title = title;
        Description = description;
        Glyph = glyph;
        _get = get;
        _set = set;
    }

    public string Title { get; }
    public string Description { get; }
    public string Glyph { get; }

    public HotkeyBinding Binding
    {
        get => _get();
        set
        {
            if (value is null) return;
            _set(value);
            _owner.Save();
            OnPropertyChanged();
            _owner.OnHotkeyEdited();
        }
    }

    /// <summary>Shown when Windows refused the shortcut (another app owns it).</summary>
    public string? Status
    {
        get => _status;
        set => Set(ref _status, value);
    }
}

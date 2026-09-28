using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Input;
using AudioPilotManager.Interop;
using AudioPilotManager.Mvvm;

namespace AudioPilotManager.Audio;

/// <summary>An active playback or recording device, its channels, and the apps using it.</summary>
public sealed class AudioDevice : ObservableObject, IDisposable
{
    private static Guid _context = AudioContext.Id;

    private readonly IMMDevice _device;
    private readonly IAudioEndpointVolume? _endpoint;
    private readonly IAudioMeterInformation? _meter;
    private readonly IAudioSessionManager2? _sessionManager;
    private readonly SessionNotifier? _notifier;
    private readonly Dictionary<string, AudioSession> _sessions = new();
    private readonly Dictionary<string, bool> _soloRestore = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastUserChange = DateTime.MinValue;

    private string _name = "";
    private string _description = "";
    private EndpointFormFactor _formFactor = EndpointFormFactor.Unknown;
    private double _volume;
    private bool _isMuted;
    private double _level;
    private bool _isDefault;
    private bool _isDefaultComms;
    private bool _showChannels;
    private string? _soloKey;
    private bool _disposed;

    internal AudioDevice(AudioService service, IMMDevice device, string id, EDataFlow flow)
    {
        Service = service;
        _device = device;
        Id = id;
        IsOutput = flow == EDataFlow.Render;

        _endpoint = Activate<IAudioEndpointVolume>(CoreAudioIids.AudioEndpointVolume);
        _meter = Activate<IAudioMeterInformation>(CoreAudioIids.AudioMeterInformation);
        _sessionManager = Activate<IAudioSessionManager2>(CoreAudioIids.AudioSessionManager2);

        ReadProperties();
        CreateChannels();
        SyncState(force: true);

        if (_sessionManager is not null)
        {
            // Windows only raises session notifications after the enumerator was requested once.
            RefreshSessions();
            try
            {
                _notifier = new SessionNotifier(service.OnSessionCreated);
                if (_sessionManager.RegisterSessionNotification(_notifier) != 0)
                    _notifier = null;
            }
            catch
            {
                _notifier = null;
            }
        }

        ToggleMuteCommand = new RelayCommand(() => IsMuted = !IsMuted);
        SetDefaultCommand = new RelayCommand(() => Service.SetDefault(this, communications: false));
        SetDefaultCommsCommand = new RelayCommand(() => Service.SetDefault(this, communications: true));
        ToggleChannelsCommand = new RelayCommand(() => ShowChannels = !ShowChannels);
        ResetBalanceCommand = new RelayCommand(() => Balance = 0);
        ShowInMixerCommand = new RelayCommand(() => Service.RequestShowInMixer(this));
    }

    internal AudioService Service { get; }

    public string Id { get; }
    public bool IsOutput { get; }
    public bool IsInput => !IsOutput;

    public string Name
    {
        get => _name;
        private set
        {
            if (!Set(ref _name, value)) return;
            foreach (var app in Apps) app.RefreshDeviceName();
        }
    }

    /// <summary>The hardware behind the endpoint, e.g. "Realtek(R) Audio".</summary>
    public string Description
    {
        get => _description;
        private set => Set(ref _description, value);
    }

    public string Glyph => _formFactor switch
    {
        EndpointFormFactor.Headphones => Glyphs.Headphones,
        EndpointFormFactor.Headset or EndpointFormFactor.Handset => Glyphs.Headset,
        EndpointFormFactor.DigitalAudioDisplayDevice => Glyphs.Monitor,
        EndpointFormFactor.Microphone => Glyphs.Microphone,
        _ => IsOutput ? Glyphs.Speakers : Glyphs.Microphone,
    };

    public string VolumeGlyph => IsInput
        ? (IsMuted ? Glyphs.MicrophoneOff : Glyphs.Microphone)
        : IsMuted || Volume <= 0 ? Glyphs.Mute
        : Volume < 34 ? Glyphs.Volume1
        : Volume < 67 ? Glyphs.Volume2
        : Glyphs.Volume3;

    public string KindLabel => IsOutput ? "Output" : "Input";

    public double Volume
    {
        get => _volume;
        set
        {
            value = Math.Clamp(Math.Round(value), 0, 100);
            if (!Set(ref _volume, value) || _endpoint is null) return;
            _lastUserChange = DateTime.UtcNow;
            _endpoint.SetMasterVolumeLevelScalar((float)(value / 100), ref _context);
            if (IsMuted && value > 0 && IsOutput) IsMuted = false;
            OnPropertyChanged(nameof(VolumeGlyph));
            SyncChannels();
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (!Set(ref _isMuted, value) || _endpoint is null) return;
            _lastUserChange = DateTime.UtcNow;
            _endpoint.SetMute(value, ref _context);
            OnPropertyChanged(nameof(VolumeGlyph));
        }
    }

    public double Level
    {
        get => _level;
        private set => Set(ref _level, value);
    }

    public bool IsDefault
    {
        get => _isDefault;
        internal set => Set(ref _isDefault, value);
    }

    public bool IsDefaultComms
    {
        get => _isDefaultComms;
        internal set => Set(ref _isDefaultComms, value);
    }

    public bool ShowChannels
    {
        get => _showChannels;
        set => Set(ref _showChannels, value);
    }

    public ObservableCollection<ChannelVolume> Channels { get; } = new();

    public bool HasChannels => Channels.Count > 1;

    public bool HasBalance => Channels.Count == 2;

    /// <summary>-100 (left) .. 100 (right), for stereo devices.</summary>
    public double Balance
    {
        get
        {
            if (!HasBalance) return 0;
            double l = Channels[0].Volume, r = Channels[1].Volume;
            if (l <= 0 && r <= 0) return 0;
            return Math.Round(l >= r ? -(1 - r / l) * 100 : (1 - l / r) * 100);
        }
        set
        {
            if (!HasBalance || _endpoint is null) return;
            value = Math.Clamp(Math.Round(value), -100, 100);
            if (Math.Abs(value) <= 2) value = 0;   // gentle snap to centre
            var master = Math.Max(Channels[0].Volume, Channels[1].Volume) / 100;
            if (master <= 0) master = Volume / 100;
            var b = value / 100;
            var left = master * (b > 0 ? 1 - b : 1);
            var right = master * (b < 0 ? 1 + b : 1);
            _lastUserChange = DateTime.UtcNow;
            _endpoint.SetChannelVolumeLevelScalar(0, (float)left, ref _context);
            _endpoint.SetChannelVolumeLevelScalar(1, (float)right, ref _context);
            SyncChannels();
            OnPropertyChanged();
        }
    }

    public ObservableCollection<AudioApp> Apps { get; } = new();

    public bool HasApps => Apps.Count > 0;

    public ICommand ToggleMuteCommand { get; }
    public ICommand SetDefaultCommand { get; }
    public ICommand SetDefaultCommsCommand { get; }
    public ICommand ToggleChannelsCommand { get; }
    public ICommand ResetBalanceCommand { get; }
    public ICommand ShowInMixerCommand { get; }

    private T? Activate<T>(Guid iid) where T : class
    {
        try
        {
            return _device.Activate(ref iid, CoreAudioIids.ClsCtxAll, IntPtr.Zero, out var obj) == 0 ? obj as T : null;
        }
        catch
        {
            return null;
        }
    }

    internal void ReadProperties()
    {
        if (_device.OpenPropertyStore(0 /* STGM_READ */, out var store) != 0 || store is null)
        {
            Name = "Audio device";
            return;
        }

        try
        {
            var shortName = ReadString(store, PropertyKeys.DeviceDescription);
            var interfaceName = ReadString(store, PropertyKeys.DeviceInterfaceFriendlyName);
            Name = shortName ?? ReadString(store, PropertyKeys.DeviceFriendlyName) ?? "Audio device";
            Description = interfaceName ?? "";
            var key = PropertyKeys.AudioEndpointFormFactor;
            if (store.GetValue(ref key, out var pv) == 0)
            {
                if (pv.AsUInt() is { } ff) _formFactor = (EndpointFormFactor)ff;
                Ole32.PropVariantClear(ref pv);
            }

            OnPropertyChanged(nameof(Glyph));
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    private static string? ReadString(IPropertyStore store, PropertyKey key)
    {
        if (store.GetValue(ref key, out var pv) != 0) return null;
        try
        {
            var s = pv.AsString();
            return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        }
        finally
        {
            Ole32.PropVariantClear(ref pv);
        }
    }

    private void CreateChannels()
    {
        if (_endpoint is null || _endpoint.GetChannelCount(out var count) != 0) return;
        var names = ChannelVolume.NamesFor((int)count);
        for (uint i = 0; i < count && i < 16; i++)
            Channels.Add(new ChannelVolume(this, i, names[i]));
        OnPropertyChanged(nameof(HasChannels));
        OnPropertyChanged(nameof(HasBalance));
    }

    internal float ReadChannel(uint index) =>
        _endpoint is not null && _endpoint.GetChannelVolumeLevelScalar(index, out var v) == 0 ? v : 0f;

    internal void WriteChannel(uint index, float value)
    {
        if (_endpoint is null) return;
        _lastUserChange = DateTime.UtcNow;
        _endpoint.SetChannelVolumeLevelScalar(index, Math.Clamp(value, 0f, 1f), ref _context);
        // Windows derives the master volume from the loudest channel.
        if (_endpoint.GetMasterVolumeLevelScalar(out var master) == 0)
        {
            _volume = Math.Round(master * 100);
            OnPropertyChanged(nameof(Volume));
            OnPropertyChanged(nameof(VolumeGlyph));
        }

        OnPropertyChanged(nameof(Balance));
    }

    private void SyncChannels()
    {
        foreach (var c in Channels) c.Sync();
        OnPropertyChanged(nameof(Balance));
    }

    /// <summary>Reads volume, mute and channels back from Windows.</summary>
    internal void SyncState(bool force = false)
    {
        if (_endpoint is null) return;
        if (!force && (DateTime.UtcNow - _lastUserChange).TotalMilliseconds < 500) return;

        if (_endpoint.GetMasterVolumeLevelScalar(out var v) == 0)
        {
            var nv = Math.Round(v * 100);
            if (Math.Abs(nv - _volume) >= 1 || force)
            {
                _volume = nv;
                OnPropertyChanged(nameof(Volume));
                OnPropertyChanged(nameof(VolumeGlyph));
            }
        }

        if (_endpoint.GetMute(out var m) == 0 && (m != _isMuted || force))
        {
            _isMuted = m;
            OnPropertyChanged(nameof(IsMuted));
            OnPropertyChanged(nameof(VolumeGlyph));
        }

        var balanceBefore = Balance;
        foreach (var c in Channels) c.Sync();
        if (Math.Abs(balanceBefore - Balance) >= 1) OnPropertyChanged(nameof(Balance));
    }

    private CaptureMonitor? _monitor;

    /// <summary>Inputs only: keeps a silent capture stream open so the level meter has a signal.</summary>
    internal void SetMonitoring(bool on)
    {
        if (IsOutput || _disposed) return;
        if (on && _monitor is null)
        {
            _monitor = CaptureMonitor.Start(_device);
        }
        else if (!on && _monitor is not null)
        {
            _monitor.Dispose();
            _monitor = null;
        }
    }

    internal void UpdateMeter(bool includeApps)
    {
        _monitor?.Drain();
        if (_meter is not null && _meter.GetPeakValue(out var peak) == 0)
        {
            var next = Meter.Smooth(_level, Meter.ToLevel(peak));
            if (Math.Abs(next - _level) > 0.002 || (next == 0 && _level != 0))
                Level = next;
        }

        if (!includeApps) return;
        foreach (var app in Apps) app.UpdateMeter();
    }

    /// <summary>Diffs the device's live sessions against what we show: new apps appear, closed ones disappear.</summary>
    internal void RefreshSessions()
    {
        if (_sessionManager is null || _disposed) return;

        IAudioSessionEnumerator? enumerator = null;
        var seen = new HashSet<string>();
        try
        {
            if (_sessionManager.GetSessionEnumerator(out enumerator) != 0 || enumerator is null) return;
            if (enumerator.GetCount(out var count) != 0) return;

            for (var i = 0; i < count; i++)
            {
                if (enumerator.GetSession(i, out var control) != 0 || control is null) continue;
                try
                {
                    TrackSession(control, seen);
                }
                catch (Exception ex)
                {
                    Services.Log.Warn($"Skipped a session on {Name}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Services.Log.Warn($"Session refresh failed on {Name}: {ex.Message}");
            return;
        }
        finally
        {
            if (enumerator is not null) Marshal.ReleaseComObject(enumerator);
        }

        // Anything we didn't see this time has ended.
        foreach (var gone in _sessions.Keys.Where(k => !seen.Contains(k)).ToList())
            DropSession(gone);

        foreach (var app in Apps) app.Sync();
    }

    private void TrackSession(IAudioSessionControl2 control, HashSet<string> seen)
    {
        if (control.GetSessionInstanceIdentifier(out var instanceId) != 0 || string.IsNullOrEmpty(instanceId)) return;
        if (control.GetState(out var state) != 0 || state == AudioSessionState.Expired) return;

        var isSystem = control.IsSystemSoundsSession() == HResult.S_OK;
        control.GetProcessId(out var pid);
        // Our own microphone level monitor is not an app the user cares about.
        if (pid == (uint)Environment.ProcessId) return;
        if (!isSystem && pid != 0 && !ProcessInfo.IsAlive(pid)) return;

        seen.Add(instanceId);
        if (_sessions.ContainsKey(instanceId)) return;

        var session = new AudioSession(control, instanceId, pid, isSystem);
        _sessions[instanceId] = session;

        string key, name;
        string? path = null;
        if (isSystem)
        {
            key = AudioService.SystemSoundsKey;
            name = "System sounds";
        }
        else
        {
            (path, name) = ProcessInfo.Identify(pid);
            key = path is not null ? System.IO.Path.GetFileName(path).ToLowerInvariant() : $"pid:{pid}";
        }

        var app = Apps.FirstOrDefault(a => a.Key == key);
        var isNew = app is null;
        app ??= new AudioApp(this, key, name, path, isSystem);
        app.AddSession(session);

        if (isNew)
        {
            Service.OnAppAppeared(this, app);
            if (_soloKey is not null && !app.IsMuted)
            {
                _soloRestore[app.Key] = false;
                app.IsSoloMuted = true;
                app.SetMuted(true, fromUser: false);
            }

            if (Service.ShouldShow(app))
                InsertSorted(app);
            else
                _hidden.Add(app);
        }
    }

    // Apps the user hid (or system sounds when turned off) are tracked but not listed.
    private readonly List<AudioApp> _hidden = new();

    internal IEnumerable<AudioApp> AllApps => Apps.Concat(_hidden);

    private void InsertSorted(AudioApp app)
    {
        var index = 0;
        while (index < Apps.Count && Compare(Apps[index], app) <= 0) index++;
        Apps.Insert(index, app);
        OnPropertyChanged(nameof(HasApps));
    }

    private static int Compare(AudioApp a, AudioApp b)
    {
        // System sounds always sit at the end.
        if (a.IsSystem != b.IsSystem) return a.IsSystem ? 1 : -1;
        return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>Re-evaluates hidden apps after a visibility setting changes.</summary>
    internal void ApplyVisibility()
    {
        foreach (var app in Apps.Where(a => !Service.ShouldShow(a)).ToList())
        {
            Apps.Remove(app);
            _hidden.Add(app);
        }

        foreach (var app in _hidden.Where(Service.ShouldShow).ToList())
        {
            _hidden.Remove(app);
            InsertSorted(app);
        }

        OnPropertyChanged(nameof(HasApps));
    }

    private void DropSession(string instanceId)
    {
        if (!_sessions.Remove(instanceId, out var session)) return;
        var app = AllApps.FirstOrDefault(a => a.Sessions.Contains(session));
        if (app is null || !app.RemoveSession(instanceId) || !app.IsEmpty) return;

        Apps.Remove(app);
        _hidden.Remove(app);
        _soloRestore.Remove(app.Key);
        if (_soloKey == app.Key) EndSolo();
        if (!app.IsSystem && session.ProcessId != 0) ProcessInfo.Forget(session.ProcessId);
        OnPropertyChanged(nameof(HasApps));
        Service.OnAppDisappeared(this, app);
    }

    /// <summary>Solo: silence every other app on this device; press again to put things back.</summary>
    internal void ToggleSolo(AudioApp app)
    {
        if (_soloKey == app.Key)
        {
            EndSolo();
            return;
        }

        if (_soloKey is not null) EndSolo();

        _soloKey = app.Key;
        app.IsSoloed = true;
        foreach (var other in AllApps)
        {
            _soloRestore[other.Key] = other.IsMuted;
            if (other == app)
            {
                if (other.IsMuted) other.SetMuted(false, fromUser: false);
            }
            else if (!other.IsMuted)
            {
                other.IsSoloMuted = true;
                other.SetMuted(true, fromUser: false);
            }
        }
    }

    private void EndSolo()
    {
        foreach (var app in AllApps)
        {
            app.IsSoloed = false;
            if (_soloRestore.TryGetValue(app.Key, out var wasMuted) && (app.IsSoloMuted || app.Key == _soloKey))
                app.SetMuted(wasMuted, fromUser: false);
            app.IsSoloMuted = false;
        }

        _soloRestore.Clear();
        _soloKey = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _monitor?.Dispose();
        _monitor = null;
        _disposed = true;
        try
        {
            if (_notifier is not null) _sessionManager?.UnregisterSessionNotification(_notifier);
        }
        catch
        {
            // The device may already be gone.
        }

        foreach (var session in _sessions.Values.Where(s => !s.IsSystem && s.ProcessId != 0))
            ProcessInfo.Forget(session.ProcessId);
        _sessions.Clear();
        Apps.Clear();
        _hidden.Clear();

        Release(_sessionManager);
        Release(_meter);
        Release(_endpoint);
        Release(_device);
    }

    private static void Release(object? com)
    {
        try
        {
            if (com is not null && Marshal.IsComObject(com)) Marshal.ReleaseComObject(com);
        }
        catch
        {
            // ignored
        }
    }

    private sealed class SessionNotifier : IAudioSessionNotification
    {
        private readonly Action _onCreated;

        public SessionNotifier(Action onCreated) => _onCreated = onCreated;

        public int OnSessionCreated(IAudioSessionControl2 newSession)
        {
            _onCreated();
            return 0;
        }
    }
}

public sealed class ChannelVolume : ObservableObject
{
    private readonly AudioDevice _device;
    private double _volume;

    internal ChannelVolume(AudioDevice device, uint index, string name)
    {
        _device = device;
        Index = index;
        Name = name;
        _volume = Math.Round(device.ReadChannel(index) * 100);
    }

    public uint Index { get; }
    public string Name { get; }

    public double Volume
    {
        get => _volume;
        set
        {
            value = Math.Clamp(Math.Round(value), 0, 100);
            if (Set(ref _volume, value))
                _device.WriteChannel(Index, (float)(value / 100));
        }
    }

    internal void Sync()
    {
        var v = Math.Round(_device.ReadChannel(Index) * 100);
        if (Math.Abs(v - _volume) >= 1)
        {
            _volume = v;
            OnPropertyChanged(nameof(Volume));
        }
    }

    internal static string[] NamesFor(int count)
    {
        string[] known = count switch
        {
            1 => new[] { "Mono" },
            2 => new[] { "Left", "Right" },
            4 => new[] { "Front left", "Front right", "Rear left", "Rear right" },
            6 => new[] { "Front left", "Front right", "Center", "Subwoofer", "Rear left", "Rear right" },
            8 => new[] { "Front left", "Front right", "Center", "Subwoofer", "Rear left", "Rear right", "Side left", "Side right" },
            _ => Array.Empty<string>(),
        };
        var names = new string[Math.Max(count, 0)];
        for (var i = 0; i < names.Length; i++)
            names[i] = i < known.Length ? known[i] : $"Channel {i + 1}";
        return names;
    }
}

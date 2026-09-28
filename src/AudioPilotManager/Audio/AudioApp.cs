using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using AudioPilotManager.Interop;
using AudioPilotManager.Mvvm;

namespace AudioPilotManager.Audio;

/// <summary>
/// An application as the mixer shows it: every audio session one executable has on one device,
/// controlled together (browsers and games often open several).
/// </summary>
public sealed class AudioApp : ObservableObject
{
    private readonly List<AudioSession> _sessions = new();
    private readonly AudioDevice _owner;
    private DateTime _lastUserChange = DateTime.MinValue;

    private string _name;
    private double _volume = 100;
    private bool _isMuted;
    private double _level;
    private bool _isActive;
    private bool _isSoloed;

    internal AudioApp(AudioDevice owner, string key, string name, string? exePath, bool isSystem)
    {
        _owner = owner;
        Key = key;
        _name = name;
        ExePath = exePath;
        IsSystem = isSystem;
        Icon = isSystem ? null : ProcessInfo.GetIcon(exePath);

        ToggleMuteCommand = new RelayCommand(() => SetMuted(!IsMuted, fromUser: true));
        SoloCommand = new RelayCommand(() => _owner.ToggleSolo(this));
        HideCommand = new RelayCommand(() => _owner.Service.HideApp(Key));
        ResetCommand = new RelayCommand(() => Volume = 100);
        NudgeCommand = new RelayCommand(p =>
        {
            if (p is string s && double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d))
                Volume = Math.Clamp(Volume + d, 0, 100);
        });
    }

    public string Key { get; }
    public string? ExePath { get; }
    public bool IsSystem { get; }
    public ImageSource? Icon { get; }
    public bool HasIcon => Icon is not null;
    public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name[..1].ToUpperInvariant();
    public string DeviceName => _owner.Name;

    public string Name
    {
        get => _name;
        private set
        {
            if (Set(ref _name, value))
                OnPropertyChanged(nameof(Initial));
        }
    }

    public string Tooltip => ExePath is null ? Name : $"{Name}\n{ExePath}";

    /// <summary>0..100, as shown on the fader.</summary>
    public double Volume
    {
        get => _volume;
        set
        {
            value = Math.Clamp(Math.Round(value), 0, 100);
            if (!Set(ref _volume, value)) return;
            _lastUserChange = DateTime.UtcNow;
            foreach (var s in _sessions) s.Volume = (float)(value / 100);
            // Moving a muted app's fader is a clear sign the user wants to hear it.
            if (IsMuted && !IsSoloMuted && value > 0) SetMuted(false, fromUser: true);
            _owner.Service.RememberApp(this);
            OnPropertyChanged(nameof(VolumeGlyph));
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set => SetMuted(value, fromUser: true);
    }

    /// <summary>True while this app is silenced because another app is soloed.</summary>
    internal bool IsSoloMuted { get; set; }

    public double Level
    {
        get => _level;
        private set => Set(ref _level, value);
    }

    /// <summary>At least one session is currently producing sound.</summary>
    public bool IsActive
    {
        get => _isActive;
        private set => Set(ref _isActive, value);
    }

    public bool IsSoloed
    {
        get => _isSoloed;
        internal set => Set(ref _isSoloed, value);
    }

    public string VolumeGlyph => IsMuted || Volume <= 0 ? Glyphs.Mute
        : Volume < 34 ? Glyphs.Volume1
        : Volume < 67 ? Glyphs.Volume2
        : Glyphs.Volume3;

    public ICommand ToggleMuteCommand { get; }
    public ICommand SoloCommand { get; }
    public ICommand HideCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand NudgeCommand { get; }

    internal IReadOnlyList<AudioSession> Sessions => _sessions;

    internal void SetMuted(bool muted, bool fromUser)
    {
        if (fromUser && IsSoloMuted)
            IsSoloMuted = false;
        _lastUserChange = DateTime.UtcNow;
        foreach (var s in _sessions) s.Muted = muted;
        if (Set(ref _isMuted, muted, nameof(IsMuted)))
            OnPropertyChanged(nameof(VolumeGlyph));
        if (fromUser)
            _owner.Service.RememberApp(this);
    }

    /// <summary>Applies a stored volume without it counting as a fresh user change.</summary>
    internal void Apply(float volume, bool muted)
    {
        _volume = Math.Round(Math.Clamp(volume, 0f, 1f) * 100);
        foreach (var s in _sessions)
        {
            s.Volume = volume;
            s.Muted = muted;
        }

        _isMuted = muted;
        OnPropertyChanged(nameof(Volume));
        OnPropertyChanged(nameof(IsMuted));
        OnPropertyChanged(nameof(VolumeGlyph));
    }

    internal void AddSession(AudioSession session)
    {
        var first = _sessions.Count == 0;
        _sessions.Add(session);
        if (first)
        {
            _volume = Math.Round(session.Volume * 100);
            _isMuted = session.Muted;
        }
        else
        {
            // New streams from the same app follow the app's fader.
            session.Volume = (float)(_volume / 100);
            session.Muted = _isMuted;
        }

        if (IsSystem) return;
        var display = session.DisplayName;
        if (!string.IsNullOrWhiteSpace(display) && display.Length <= 48 && _sessions.Count == 1 && ExePath is null)
            Name = display;
    }

    internal bool RemoveSession(string instanceId) => _sessions.RemoveAll(s => s.InstanceId == instanceId) > 0;

    internal bool IsEmpty => _sessions.Count == 0;

    /// <summary>Picks up changes made elsewhere (Windows mixer, the app itself, another tool).</summary>
    internal void Sync()
    {
        if (_sessions.Count == 0) return;
        IsActive = _sessions.Any(s => s.State == AudioSessionState.Active);
        if ((DateTime.UtcNow - _lastUserChange).TotalMilliseconds < 500) return;

        var head = _sessions[0];
        var v = Math.Round(head.Volume * 100);
        var m = head.Muted;
        var changed = false;
        if (Math.Abs(v - _volume) >= 1)
        {
            _volume = v;
            OnPropertyChanged(nameof(Volume));
            changed = true;
        }

        if (m != _isMuted)
        {
            _isMuted = m;
            OnPropertyChanged(nameof(IsMuted));
            if (!m) IsSoloMuted = false;
            changed = true;
        }

        if (changed)
        {
            OnPropertyChanged(nameof(VolumeGlyph));
            _owner.Service.RememberApp(this);
        }
    }

    internal void UpdateMeter()
    {
        float peak = 0;
        foreach (var s in _sessions)
            peak = Math.Max(peak, s.Peak);
        var next = Meter.Smooth(_level, Meter.ToLevel(peak));
        if (Math.Abs(next - _level) > 0.002 || (next == 0 && _level != 0))
            Level = next;
    }

    internal void RefreshDeviceName() => OnPropertyChanged(nameof(DeviceName));
}

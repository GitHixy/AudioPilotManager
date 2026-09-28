using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using AudioPilotManager.Interop;
using AudioPilotManager.Services;

namespace AudioPilotManager.Audio;

/// <summary>
/// Owns every Core Audio object. It keeps the device and app lists live from three sources:
/// Windows' device notifications, session-created notifications, and a light poll that
/// catches everything else (apps closing, changes made in other mixers). All work happens on
/// the UI thread; notifications only schedule it.
/// </summary>
public sealed class AudioService : IDisposable
{
    public const string SystemSoundsKey = "system-sounds";

    private readonly Dispatcher _dispatcher;
    private readonly SettingsService _settings;
    private readonly DispatcherTimer _pollTimer;
    private readonly DispatcherTimer _meterTimer;
    private IMMDeviceEnumerator? _enumerator;
    private DeviceNotifier? _deviceNotifier;
    private bool _deviceRefreshQueued;
    private bool _sessionRefreshQueued;
    private bool _meteringEnabled;
    private bool _disposed;

    public AudioService(SettingsService settings, Dispatcher dispatcher)
    {
        _settings = settings;
        _dispatcher = dispatcher;
        _pollTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(500) };
        _pollTimer.Tick += (_, _) => Poll();
        _meterTimer = new DispatcherTimer(DispatcherPriority.Render, dispatcher) { Interval = TimeSpan.FromMilliseconds(33) };
        _meterTimer.Tick += (_, _) => UpdateMeters();
    }

    public ObservableCollection<AudioDevice> Outputs { get; } = new();
    public ObservableCollection<AudioDevice> Inputs { get; } = new();

    public IEnumerable<AudioDevice> AllDevices => Outputs.Concat(Inputs);

    public AudioDevice? DefaultOutput => Outputs.FirstOrDefault(d => d.IsDefault);
    public AudioDevice? DefaultInput => Inputs.FirstOrDefault(d => d.IsDefault);

    /// <summary>The device whose apps get per-frame meters (the one on screen).</summary>
    public AudioDevice? MeteredDevice { get; set; }

    public event EventHandler? DevicesChanged;
    public event EventHandler? DefaultsChanged;
    public event EventHandler<AudioDevice>? ShowInMixerRequested;

    public void Start()
    {
        _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        _deviceNotifier = new DeviceNotifier(this);
        if (_enumerator.RegisterEndpointNotificationCallback(_deviceNotifier) != 0)
            Log.Warn("Device notifications unavailable; relying on polling.");
        RefreshDevices();
        _pollTimer.Start();
        Log.Info($"Audio started: {Outputs.Count} output(s), {Inputs.Count} input(s).");
    }

    /// <summary>Meters only run while there is a visible window to show them.</summary>
    public void SetMetering(bool enabled)
    {
        _meteringEnabled = enabled;
        _pollTimer.Interval = TimeSpan.FromMilliseconds(enabled ? 500 : 1500);
        if (enabled)
        {
            _meterTimer.Start();
        }
        else
        {
            _meterTimer.Stop();
        }

        UpdateMonitoring();
    }

    /// <summary>Microphone meters need a live stream; keep one only while a meter is on screen.</summary>
    public void UpdateMonitoring()
    {
        var on = _meteringEnabled && _settings.Current.LiveMicMeter && !_disposed;
        foreach (var d in Inputs) d.SetMonitoring(on);
    }

    public bool IsMetering => _meteringEnabled;

    // ---------------------------------------------------------------- devices ----

    private void QueueDeviceRefresh()
    {
        if (_deviceRefreshQueued || _disposed) return;
        _deviceRefreshQueued = true;
        // A short delay lets Windows finish a burst of notifications (one per role) first.
        _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(async () =>
        {
            await System.Threading.Tasks.Task.Delay(120);
            _deviceRefreshQueued = false;
            if (!_disposed) RefreshDevices();
        }));
    }

    internal void OnSessionCreated()
    {
        if (_sessionRefreshQueued || _disposed) return;
        _sessionRefreshQueued = true;
        _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(async () =>
        {
            // Give the new session a moment to settle its state and volume.
            await System.Threading.Tasks.Task.Delay(80);
            _sessionRefreshQueued = false;
            if (_disposed) return;
            foreach (var d in AllDevices.ToList()) d.RefreshSessions();
        }));
    }

    public void RefreshDevices()
    {
        if (_enumerator is null) return;
        var changed = false;
        changed |= SyncList(Outputs, EDataFlow.Render);
        changed |= SyncList(Inputs, EDataFlow.Capture);
        UpdateDefaults();
        if (changed)
        {
            UpdateMonitoring();
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool SyncList(ObservableCollection<AudioDevice> list, EDataFlow flow)
    {
        var found = new List<(string Id, IMMDevice Device)>();
        if (_enumerator!.EnumAudioEndpoints(flow, DeviceState.Active, out var collection) == 0 && collection is not null)
        {
            try
            {
                collection.GetCount(out var count);
                for (uint i = 0; i < count; i++)
                {
                    if (collection.Item(i, out var dev) == 0 && dev is not null && dev.GetId(out var id) == 0)
                        found.Add((id, dev));
                }
            }
            finally
            {
                Marshal.ReleaseComObject(collection);
            }
        }

        var changed = false;
        foreach (var gone in list.Where(d => found.All(f => f.Id != d.Id)).ToList())
        {
            list.Remove(gone);
            gone.Dispose();
            changed = true;
        }

        foreach (var (id, dev) in found)
        {
            if (list.Any(d => d.Id == id))
                continue;
            try
            {
                var device = new AudioDevice(this, dev, id, flow);
                var index = 0;
                while (index < list.Count && string.Compare(list[index].Name, device.Name, StringComparison.CurrentCultureIgnoreCase) < 0) index++;
                list.Insert(index, device);
                changed = true;
            }
            catch (Exception ex)
            {
                Log.Error($"Could not open device {id}", ex);
            }
        }

        return changed;
    }

    private void UpdateDefaults()
    {
        var render = DefaultId(EDataFlow.Render, ERole.Multimedia);
        var renderComms = DefaultId(EDataFlow.Render, ERole.Communications);
        var capture = DefaultId(EDataFlow.Capture, ERole.Multimedia);
        var captureComms = DefaultId(EDataFlow.Capture, ERole.Communications);
        var changed = false;
        foreach (var d in Outputs)
        {
            changed |= d.IsDefault != (d.Id == render);
            d.IsDefault = d.Id == render;
            d.IsDefaultComms = d.Id == renderComms;
        }

        foreach (var d in Inputs)
        {
            changed |= d.IsDefault != (d.Id == capture);
            d.IsDefault = d.Id == capture;
            d.IsDefaultComms = d.Id == captureComms;
        }

        if (changed)
        {
            Log.Info($"Defaults: output '{DefaultOutput?.Name}', input '{DefaultInput?.Name}'.");
            DefaultsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private string? DefaultId(EDataFlow flow, ERole role)
    {
        if (_enumerator is null || _enumerator.GetDefaultAudioEndpoint(flow, role, out var dev) != 0 || dev is null)
            return null;
        try
        {
            return dev.GetId(out var id) == 0 ? id : null;
        }
        finally
        {
            Marshal.ReleaseComObject(dev);
        }
    }

    /// <summary>Makes a device the Windows default (for everything, or for calls only).</summary>
    public bool SetDefault(AudioDevice device, bool communications)
    {
        IPolicyConfig? policy = null;
        Log.Info($"Setting default {(communications ? "communications " : "")}device: {device.Name}");
        try
        {
            policy = (IPolicyConfig)new PolicyConfigComObject();
            var ok = true;
            if (communications)
            {
                ok &= policy.SetDefaultEndpoint(device.Id, ERole.Communications) == 0;
            }
            else
            {
                ok &= policy.SetDefaultEndpoint(device.Id, ERole.Console) == 0;
                ok &= policy.SetDefaultEndpoint(device.Id, ERole.Multimedia) == 0;
                ok &= policy.SetDefaultEndpoint(device.Id, ERole.Communications) == 0;
            }

            UpdateDefaults();
            return ok;
        }
        catch (Exception ex)
        {
            Log.Error("Could not change the default device", ex);
            return false;
        }
        finally
        {
            if (policy is not null) Marshal.ReleaseComObject(policy);
        }
    }

    /// <summary>Switches the default output to the next device in the list. Returns the new default.</summary>
    public AudioDevice? CycleDefaultOutput()
    {
        if (Outputs.Count == 0) return null;
        var current = DefaultOutput;
        var index = current is null ? -1 : Outputs.IndexOf(current);
        var next = Outputs[(index + 1) % Outputs.Count];
        return SetDefault(next, communications: false) ? next : null;
    }

    internal void RequestShowInMixer(AudioDevice device) => ShowInMixerRequested?.Invoke(this, device);

    // ---------------------------------------------------------------- polling ----

    private void Poll()
    {
        foreach (var d in AllDevices.ToList())
        {
            d.SyncState();
            d.RefreshSessions();
        }
    }

    private void UpdateMeters()
    {
        foreach (var d in Outputs) d.UpdateMeter(includeApps: d == MeteredDevice);
        foreach (var d in Inputs) d.UpdateMeter(includeApps: d == MeteredDevice);
    }

    // ---------------------------------------------------------------- apps -------

    public event EventHandler<AudioApp>? AppAppeared;

    internal void OnAppAppeared(AudioDevice device, AudioApp app)
    {
        var s = _settings.Current;
        if (s.RememberAppVolumes && !app.IsSystem && s.AppMemory.TryGetValue(app.Key, out var memory))
        {
            var current = (float)(app.Volume / 100);
            if (Math.Abs(current - memory.Volume) > 0.005f || app.IsMuted != memory.Muted)
                app.Apply(memory.Volume, memory.Muted);
        }

        AppAppeared?.Invoke(this, app);
    }

    internal void OnAppDisappeared(AudioDevice device, AudioApp app)
    {
    }

    internal void RememberApp(AudioApp app)
    {
        var s = _settings.Current;
        if (!s.RememberAppVolumes || app.IsSystem || app.Key.StartsWith("pid:", StringComparison.Ordinal) || app.IsSoloMuted)
            return;
        if (!s.AppMemory.TryGetValue(app.Key, out var memory))
        {
            // Keep the memory from growing forever.
            if (s.AppMemory.Count >= 500) s.AppMemory.Remove(s.AppMemory.Keys.First());
            s.AppMemory[app.Key] = memory = new AppMemory();
        }

        var v = (float)(app.Volume / 100);
        if (Math.Abs(memory.Volume - v) < 0.001f && memory.Muted == app.IsMuted) return;
        memory.Volume = v;
        memory.Muted = app.IsMuted;
        _settings.Save();
    }

    internal bool ShouldShow(AudioApp app)
    {
        var s = _settings.Current;
        if (app.IsSystem) return s.ShowSystemSounds;
        return !s.HiddenApps.Contains(app.Key, StringComparer.OrdinalIgnoreCase);
    }

    internal void HideApp(string key)
    {
        var s = _settings.Current;
        if (!s.HiddenApps.Contains(key, StringComparer.OrdinalIgnoreCase))
            s.HiddenApps.Add(key);
        _settings.Save();
        ApplyVisibility();
        AppHidden?.Invoke(this, key);
    }

    public event EventHandler<string>? AppHidden;

    public void ApplyVisibility()
    {
        foreach (var d in AllDevices) d.ApplyVisibility();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pollTimer.Stop();
        _meterTimer.Stop();
        try
        {
            if (_enumerator is not null && _deviceNotifier is not null)
                _enumerator.UnregisterEndpointNotificationCallback(_deviceNotifier);
        }
        catch
        {
            // ignored
        }

        foreach (var d in AllDevices.ToList()) d.Dispose();
        Outputs.Clear();
        Inputs.Clear();
        if (_enumerator is not null) Marshal.ReleaseComObject(_enumerator);
        _enumerator = null;
        ProcessInfo.Clear();
    }

    /// <summary>Receives device events on a Windows audio thread and hands them to the UI thread.</summary>
    private sealed class DeviceNotifier : IMMNotificationClient
    {
        private readonly AudioService _owner;

        public DeviceNotifier(AudioService owner) => _owner = owner;

        public void OnDeviceStateChanged(string deviceId, uint newState) => Queue();

        public void OnDeviceAdded(string deviceId) => Queue();

        public void OnDeviceRemoved(string deviceId) => Queue();

        public void OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? defaultDeviceId) => Queue();

        public void OnPropertyValueChanged(string deviceId, PropertyKey key)
        {
            // Only renames matter to us.
            if (key.FormatId == PropertyKeys.DeviceFriendlyName.FormatId || key.FormatId == PropertyKeys.DeviceInterfaceFriendlyName.FormatId)
            {
                _owner._dispatcher.BeginInvoke(new Action(() =>
                {
                    foreach (var d in _owner.AllDevices.Where(d => d.Id == deviceId))
                        d.ReadProperties();
                }));
            }
        }

        private void Queue() => _owner._dispatcher.BeginInvoke(new Action(_owner.QueueDeviceRefresh));
    }
}

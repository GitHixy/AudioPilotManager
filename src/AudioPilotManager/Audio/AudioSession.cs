using System;
using AudioPilotManager.Interop;

namespace AudioPilotManager.Audio;

/// <summary>One WASAPI audio session. An app can own several (one per stream or per process).</summary>
internal sealed class AudioSession
{
    private static Guid _context = AudioContext.Id;

    private readonly IAudioSessionControl2 _control;
    private readonly ISimpleAudioVolume? _volume;
    private readonly IAudioMeterInformation? _meter;

    public AudioSession(IAudioSessionControl2 control, string instanceId, uint pid, bool isSystem)
    {
        _control = control;
        _volume = control as ISimpleAudioVolume;
        _meter = control as IAudioMeterInformation;
        InstanceId = instanceId;
        ProcessId = pid;
        IsSystem = isSystem;
    }

    public string InstanceId { get; }
    public uint ProcessId { get; }
    public bool IsSystem { get; }

    /// <summary>Set when a call fails in a way that means the session is gone.</summary>
    public bool IsDead { get; private set; }

    public string? DisplayName =>
        _control.GetDisplayName(out var name) == 0 ? ProcessInfo.ResolveIndirect(name) : null;

    public AudioSessionState State
    {
        get
        {
            if (IsDead) return AudioSessionState.Expired;
            return Check(_control.GetState(out var state)) ? state : AudioSessionState.Expired;
        }
    }

    public float Volume
    {
        get => _volume is not null && Check(_volume.GetMasterVolume(out var v)) ? v : 1f;
        set
        {
            if (_volume is not null)
                Check(_volume.SetMasterVolume(Math.Clamp(value, 0f, 1f), ref _context));
        }
    }

    public bool Muted
    {
        get => _volume is not null && Check(_volume.GetMute(out var m)) && m;
        set
        {
            if (_volume is not null)
                Check(_volume.SetMute(value, ref _context));
        }
    }

    public float Peak => _meter is not null && !IsDead && _meter.GetPeakValue(out var p) == 0 ? p : 0f;

    private bool Check(int hr)
    {
        if (hr >= 0) return true;
        if (hr == HResult.AUDCLNT_E_DEVICE_INVALIDATED || hr == unchecked((int)0x800706BA) /* RPC server unavailable */)
            IsDead = true;
        return false;
    }
}

internal static class AudioContext
{
    /// <summary>Event context stamped on every change this app makes.</summary>
    public static readonly Guid Id = new("5a0e2f55-7a1d-4c4b-9f1e-a0d1f0c0a9e1");
}

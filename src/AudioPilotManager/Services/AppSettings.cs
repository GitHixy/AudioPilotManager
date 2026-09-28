using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;
using System.Windows.Input;

namespace AudioPilotManager.Services;

public sealed class AppSettings
{
    public int Version { get; set; } = 1;

    // Appearance
    public string Theme { get; set; } = "System";   // System | Dark | Light
    public string Accent { get; set; } = "Aurora";
    public bool UseBackdrop { get; set; } = true;     // Mica on Windows 11

    // Behaviour
    public bool StartMinimized { get; set; } = true;  // when launched by Windows at sign-in
    public bool CloseToTray { get; set; } = true;
    public bool AlwaysOnTop { get; set; }
    public bool RememberAppVolumes { get; set; } = true;
    public bool ShowSystemSounds { get; set; } = true;
    public bool ShowOsd { get; set; } = true;
    public bool LiveMicMeter { get; set; } = true;    // listen to inputs while a meter is visible
    public bool HotkeysEnabled { get; set; } = true;
    public bool ShowFirstRunTips { get; set; } = true;

    public HotkeyBinding MicMuteHotkey { get; set; } = new(HotkeyBinding.Control | HotkeyBinding.Alt, 0x4D);       // Ctrl+Alt+M
    public HotkeyBinding OutputMuteHotkey { get; set; } = new(HotkeyBinding.Control | HotkeyBinding.Alt, 0x4E);    // Ctrl+Alt+N
    public HotkeyBinding CycleOutputHotkey { get; set; } = new(HotkeyBinding.Control | HotkeyBinding.Alt, 0x4F);   // Ctrl+Alt+O
    public HotkeyBinding ToggleWindowHotkey { get; set; } = new(HotkeyBinding.Control | HotkeyBinding.Alt, 0x56);  // Ctrl+Alt+V

    // Mixer
    public string? MixerDeviceId { get; set; }        // null = follow the default output
    public List<string> HiddenApps { get; set; } = new();
    public Dictionary<string, AppMemory> AppMemory { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<MixProfile> Profiles { get; set; } = new();

    // Window
    public double WindowWidth { get; set; } = 1040;
    public double WindowHeight { get; set; } = 680;
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }

    /// <summary>Clamps everything that came from disk into sane ranges.</summary>
    public void Sanitize()
    {
        if (Theme is not ("System" or "Dark" or "Light")) Theme = "System";
        if (string.IsNullOrWhiteSpace(Accent) || Accent.Length > 32) Accent = "Aurora";
        HiddenApps ??= new();
        AppMemory = new Dictionary<string, AppMemory>(AppMemory ?? new(), StringComparer.OrdinalIgnoreCase);
        Profiles ??= new();
        foreach (var m in AppMemory.Values) m.Sanitize();
        Profiles.RemoveAll(p => p is null || string.IsNullOrWhiteSpace(p.Name));
        foreach (var p in Profiles) p.Sanitize();
        MicMuteHotkey ??= new();
        OutputMuteHotkey ??= new();
        CycleOutputHotkey ??= new();
        ToggleWindowHotkey ??= new();
        WindowWidth = Math.Clamp(double.IsFinite(WindowWidth) ? WindowWidth : 1040, 640, 4000);
        WindowHeight = Math.Clamp(double.IsFinite(WindowHeight) ? WindowHeight : 680, 460, 3000);
        if (WindowLeft is { } l && !double.IsFinite(l)) WindowLeft = null;
        if (WindowTop is { } t && !double.IsFinite(t)) WindowTop = null;
    }
}

public sealed class AppMemory
{
    public float Volume { get; set; } = 1f;
    public bool Muted { get; set; }

    public void Sanitize() => Volume = float.IsFinite(Volume) ? Math.Clamp(Volume, 0f, 1f) : 1f;
}

public sealed class MixProfile
{
    public string Name { get; set; } = "";
    public DateTime Saved { get; set; } = DateTime.Now;
    public Dictionary<string, AppMemory> Apps { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, AppMemory> Devices { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? DefaultOutputId { get; set; }

    public void Sanitize()
    {
        Name = Name.Trim();
        if (Name.Length > 40) Name = Name[..40];
        Apps = new Dictionary<string, AppMemory>(Apps ?? new(), StringComparer.OrdinalIgnoreCase);
        Devices = new Dictionary<string, AppMemory>(Devices ?? new(), StringComparer.OrdinalIgnoreCase);
        foreach (var a in Apps.Values) a.Sanitize();
        foreach (var d in Devices.Values) d.Sanitize();
    }
}

public sealed class HotkeyBinding
{
    public const uint Alt = 0x1, Control = 0x2, Shift = 0x4, Win = 0x8;

    public HotkeyBinding()
    {
    }

    public HotkeyBinding(uint modifiers, uint key)
    {
        Modifiers = modifiers;
        Key = key;
    }

    public uint Modifiers { get; set; }
    public uint Key { get; set; }

    [JsonIgnore] public bool IsEmpty => Key == 0;

    public override string ToString()
    {
        if (IsEmpty) return "Not set";
        var sb = new StringBuilder();
        if ((Modifiers & Control) != 0) sb.Append("Ctrl + ");
        if ((Modifiers & Alt) != 0) sb.Append("Alt + ");
        if ((Modifiers & Shift) != 0) sb.Append("Shift + ");
        if ((Modifiers & Win) != 0) sb.Append("Win + ");
        sb.Append(KeyName(KeyInterop.KeyFromVirtualKey((int)Key)));
        return sb.ToString();
    }

    private static string KeyName(Key key) => key switch
    {
        >= System.Windows.Input.Key.D0 and <= System.Windows.Input.Key.D9 => ((int)(key - System.Windows.Input.Key.D0)).ToString(),
        System.Windows.Input.Key.Up => "↑",
        System.Windows.Input.Key.Down => "↓",
        System.Windows.Input.Key.Left => "←",
        System.Windows.Input.Key.Right => "→",
        System.Windows.Input.Key.OemPlus => "+",
        System.Windows.Input.Key.OemMinus => "-",
        _ => key.ToString(),
    };
}

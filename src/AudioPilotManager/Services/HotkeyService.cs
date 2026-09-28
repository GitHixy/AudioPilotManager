using System;
using System.Collections.Generic;
using System.Windows.Interop;
using AudioPilotManager.Interop;

namespace AudioPilotManager.Services;

/// <summary>System-wide shortcuts via RegisterHotKey on a private message-only window.</summary>
public sealed class HotkeyService : IDisposable
{
    private const uint MOD_NOREPEAT = 0x4000;

    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = new();
    private int _nextId = 0x5A00;

    public HotkeyService()
    {
        var p = new HwndSourceParameters("AudioPilotManager.Hotkeys")
        {
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE
            WindowStyle = 0,
        };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
    }

    /// <summary>Registers a shortcut. Returns false if another app already owns it.</summary>
    public bool Register(HotkeyBinding binding, Action action)
    {
        if (binding.IsEmpty) return true;
        var id = _nextId++;
        if (!Native.RegisterHotKey(_source.Handle, id, binding.Modifiers | MOD_NOREPEAT, binding.Key))
        {
            Log.Warn($"Hotkey {binding} is already in use by another application.");
            return false;
        }

        _actions[id] = action;
        return true;
    }

    public void UnregisterAll()
    {
        foreach (var id in _actions.Keys)
            Native.UnregisterHotKey(_source.Handle, id);
        _actions.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log.Error("Hotkey action failed", ex);
            }
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}

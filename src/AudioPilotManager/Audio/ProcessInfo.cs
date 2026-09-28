using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AudioPilotManager.Interop;

namespace AudioPilotManager.Audio;

/// <summary>Who is behind an audio session: executable path, friendly name, icon, and whether it's still running.</summary>
internal static class ProcessInfo
{
    private static readonly Dictionary<uint, IntPtr> Handles = new();
    private static readonly Dictionary<uint, (string? Path, string Name)> Identities = new();
    private static readonly Dictionary<string, ImageSource?> Icons = new(StringComparer.OrdinalIgnoreCase);

    public static bool IsAlive(uint pid)
    {
        if (pid == 0) return true;
        if (!Handles.TryGetValue(pid, out var handle))
        {
            handle = Native.OpenProcess(Native.SYNCHRONIZE | Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (handle == IntPtr.Zero)
            {
                // We can't open it (e.g. a protected process). Assume it's alive; the session's
                // own state tells us when it's gone.
                return true;
            }

            Handles[pid] = handle;
        }

        if (Native.WaitForSingleObject(handle, 0) == Native.WAIT_OBJECT_0)
        {
            Forget(pid);
            return false;
        }

        return true;
    }

    public static void Forget(uint pid)
    {
        if (Handles.Remove(pid, out var handle))
            Native.CloseHandle(handle);
        Identities.Remove(pid);
    }

    public static (string? Path, string Name) Identify(uint pid)
    {
        if (Identities.TryGetValue(pid, out var cached))
            return cached;

        string? path = null;
        var handle = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle != IntPtr.Zero)
        {
            try
            {
                var sb = new StringBuilder(1024);
                uint size = (uint)sb.Capacity;
                if (Native.QueryFullProcessImageName(handle, 0, sb, ref size))
                    path = sb.ToString();
            }
            finally
            {
                Native.CloseHandle(handle);
            }
        }

        string name;
        if (path is not null)
        {
            name = FriendlyName(path);
        }
        else
        {
            try
            {
                using var p = Process.GetProcessById((int)pid);
                name = p.ProcessName;
            }
            catch
            {
                name = $"Process {pid}";
            }
        }

        var result = (path, name);
        Identities[pid] = result;
        return result;
    }

    private static string FriendlyName(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            var description = info.FileDescription?.Trim();
            if (!string.IsNullOrEmpty(description) && description.Length <= 48 && !description.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return description;
            var product = info.ProductName?.Trim();
            if (!string.IsNullOrEmpty(product) && product.Length <= 48)
                return product;
        }
        catch
        {
            // Fall through to the file name.
        }

        var file = Path.GetFileNameWithoutExtension(path);
        return file.Length > 0 ? char.ToUpperInvariant(file[0]) + file[1..] : path;
    }

    public static ImageSource? GetIcon(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return null;
        if (Icons.TryGetValue(path, out var icon))
            return icon;

        icon = null;
        try
        {
            var handles = new IntPtr[1];
            var count = Native.PrivateExtractIcons(path, 0, 64, 64, handles, null, 1, 0);
            if (count > 0 && count != uint.MaxValue && handles[0] != IntPtr.Zero)
            {
                try
                {
                    var source = Imaging.CreateBitmapSourceFromHIcon(handles[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze();
                    icon = source;
                }
                finally
                {
                    Native.DestroyIcon(handles[0]);
                }
            }
        }
        catch
        {
            icon = null;
        }

        Icons[path] = icon;
        return icon;
    }

    /// <summary>Resolves "@dll,-id" style resource strings used for some session display names.</summary>
    public static string? ResolveIndirect(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        if (!text.StartsWith('@'))
            return text;
        var sb = new StringBuilder(512);
        return Native.SHLoadIndirectString(text, sb, sb.Capacity, IntPtr.Zero) == 0 ? sb.ToString() : null;
    }

    public static void Clear()
    {
        foreach (var handle in Handles.Values)
            Native.CloseHandle(handle);
        Handles.Clear();
        Identities.Clear();
    }
}

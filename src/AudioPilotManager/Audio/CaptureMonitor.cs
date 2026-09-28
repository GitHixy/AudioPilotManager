using System;
using System.Runtime.InteropServices;
using AudioPilotManager.Interop;

namespace AudioPilotManager.Audio;

/// <summary>
/// Windows only measures a microphone while something is recording from it, so an idle mic's
/// meter always reads zero. This opens a shared-mode capture stream purely so the endpoint meter
/// has a signal, the same thing the Windows Sound settings page does. Samples are released
/// unread straight away: nothing is stored, processed or sent anywhere. It only runs while a
/// window showing the meter is open (and can be switched off in Settings).
/// </summary>
internal sealed class CaptureMonitor : IDisposable
{
    private const int SharedMode = 0;
    private const long BufferDuration = 2_000_000; // 200 ms in 100-ns units

    private IAudioClient? _client;
    private IAudioCaptureClient? _capture;

    private CaptureMonitor()
    {
    }

    public static CaptureMonitor? Start(IMMDevice device)
    {
        var monitor = new CaptureMonitor();
        try
        {
            var iid = CoreAudioIids.AudioClient;
            if (device.Activate(ref iid, CoreAudioIids.ClsCtxAll, IntPtr.Zero, out var obj) != 0 || obj is not IAudioClient client)
                return null;
            monitor._client = client;

            if (client.GetMixFormat(out var format) != 0 || format == IntPtr.Zero)
            {
                monitor.Dispose();
                return null;
            }

            try
            {
                if (client.Initialize(SharedMode, 0, BufferDuration, 0, format, IntPtr.Zero) != 0)
                {
                    monitor.Dispose();
                    return null;
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(format);
            }

            var captureIid = CoreAudioIids.AudioCaptureClient;
            if (client.GetService(ref captureIid, out var svc) == 0)
                monitor._capture = svc as IAudioCaptureClient;

            if (client.Start() != 0)
            {
                monitor.Dispose();
                return null;
            }

            return monitor;
        }
        catch (Exception ex)
        {
            Services.Log.Warn($"Microphone level monitor unavailable: {ex.Message}");
            monitor.Dispose();
            return null;
        }
    }

    /// <summary>Hands captured packets straight back so the stream never overflows.</summary>
    public void Drain()
    {
        if (_capture is null) return;
        try
        {
            for (var i = 0; i < 64 && _capture.GetNextPacketSize(out var frames) == 0 && frames > 0; i++)
            {
                if (_capture.GetBuffer(out _, out var got, out _, out _, out _) != 0) break;
                _capture.ReleaseBuffer(got);
            }
        }
        catch
        {
            // The device went away; the owner disposes us on the next refresh.
        }
    }

    public void Dispose()
    {
        try
        {
            _client?.Stop();
        }
        catch
        {
            // ignored
        }

        if (_capture is not null) Marshal.ReleaseComObject(_capture);
        if (_client is not null) Marshal.ReleaseComObject(_client);
        _capture = null;
        _client = null;
    }
}

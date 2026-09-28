using System;

namespace AudioPilotManager.Audio;

internal static class Meter
{
    private const double FloorDb = -54;

    /// <summary>Maps a linear peak (0..1) onto a dB scale, which is how loudness is actually heard.</summary>
    public static double ToLevel(float peak)
    {
        if (peak <= 0.0001f) return 0;
        var db = 20 * Math.Log10(peak);
        return Math.Clamp((db - FloorDb) / -FloorDb, 0, 1);
    }

    /// <summary>Instant attack, smooth release.</summary>
    public static double Smooth(double shown, double target)
    {
        if (target >= shown) return target;
        var next = shown - Math.Max(0.018, (shown - target) * 0.22);
        return next < target ? target : next;
    }
}

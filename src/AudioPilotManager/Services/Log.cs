using System;
using System.IO;

namespace AudioPilotManager.Services;

/// <summary>
/// A tiny local log file (%LocalAppData%\AudioPilotManager\logs). Nothing is ever sent anywhere;
/// it exists so a user can attach it to a bug report if they choose to.
/// </summary>
public static class Log
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new();

    public static string Directory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AudioPilotManager", "logs");

    public static string FilePath { get; } = Path.Combine(Directory, "audiopilot.log");

    public static void Info(string message) => Write("INFO ", message);

    public static void Warn(string message) => Write("WARN ", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(Directory);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxBytes)
                    File.Copy(FilePath, FilePath + ".old", overwrite: true);
                if (info.Exists && info.Length > MaxBytes)
                    File.WriteAllText(FilePath, string.Empty);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }
}

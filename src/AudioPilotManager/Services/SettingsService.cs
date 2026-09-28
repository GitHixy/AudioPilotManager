using System;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;

namespace AudioPilotManager.Services;

/// <summary>Loads and saves <see cref="AppSettings"/> as JSON in %AppData%\AudioPilotManager.</summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly DispatcherTimer _debounce;

    public SettingsService()
    {
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            SaveNow();
        };
    }

    public static string Directory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioPilotManager");

    public static string FilePath { get; } = Path.Combine(Directory, "settings.json");

    public AppSettings Current { get; private set; } = new();

    public void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var info = new FileInfo(FilePath);
                // A settings file this big was not written by us; don't trust it.
                if (info.Length < 4 * 1024 * 1024)
                    Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            Log.Error("Settings could not be read; starting with defaults", ex);
            TryBackupBrokenFile();
            Current = new AppSettings();
        }

        Current.Sanitize();
    }

    /// <summary>Saves shortly after the last change, so dragging a slider doesn't hammer the disk.</summary>
    public void Save()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    public void SaveNow()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Current, Options));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error("Settings could not be saved", ex);
        }
    }

    public void Flush()
    {
        if (_debounce.IsEnabled)
        {
            _debounce.Stop();
            SaveNow();
        }
    }

    private static void TryBackupBrokenFile()
    {
        try
        {
            File.Copy(FilePath, FilePath + ".broken", overwrite: true);
        }
        catch
        {
            // ignored
        }
    }
}

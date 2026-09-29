using System;
using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Threading;
using AudioPilotManager.Mvvm;
using AudioPilotManager.Services;

namespace AudioPilotManager.ViewModels;

/// <summary>The Updates card in Settings, plus the once-a-day background check.</summary>
public sealed class UpdateViewModel : ObservableObject
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromHours(24);

    private readonly SettingsService _settings;
    private readonly DispatcherTimer _timer;
    private UpdateInfo? _available;
    private bool _isBusy;
    private double _progress;
    private string _status = "";
    private bool _hasError;

    public UpdateViewModel(SettingsService settings)
    {
        _settings = settings;
        CheckNowCommand = new RelayCommand(() => _ = CheckAsync(manual: true), () => !IsBusy);
        InstallCommand = new RelayCommand(() => _ = InstallAsync(), () => !IsBusy && _available is { AssetUrl.Length: > 0 });
        ViewReleaseCommand = new RelayCommand(() =>
        {
            var url = _available?.PageUrl ?? MainViewModel.RepoUrl + "/releases";
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Error("Could not open the release page", ex);
            }
        });

        UpdateStatusFromLastCheck();

        // First look shortly after start-up (not during it), then re-check once a day while running.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _timer.Tick += (_, _) =>
        {
            _timer.Interval = TimeSpan.FromHours(1);
            if (S.CheckForUpdates && !IsBusy && (S.LastUpdateCheck is not { } last || DateTime.Now - last >= CheckEvery))
                _ = CheckAsync(manual: false);
        };
        _timer.Start();
    }

    private AppSettings S => _settings.Current;

    /// <summary>Raised once per new version found by the background check.</summary>
    public event Action<UpdateInfo>? UpdateFound;

    /// <summary>The update is downloaded and ready: the app should exit so it can be applied.</summary>
    public event EventHandler? ExitRequested;

    public string CurrentVersion { get; } = "v" + UpdateService.CurrentVersion.ToString(3);

    public string PackageKind => UpdateService.IsInstalled ? "installed" : "portable";

    public bool CheckForUpdates
    {
        get => S.CheckForUpdates;
        set
        {
            if (S.CheckForUpdates == value) return;
            S.CheckForUpdates = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    public bool IsAvailable => _available is not null;

    public string AvailableVersion => _available is null ? "" : "v" + _available.Version.ToString(3);

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value)) CommandManager.InvalidateRequerySuggested();
        }
    }

    public double Progress
    {
        get => _progress;
        private set => Set(ref _progress, value);
    }

    public bool IsDownloading => IsBusy && IsAvailable;

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public bool HasError
    {
        get => _hasError;
        private set => Set(ref _hasError, value);
    }

    public ICommand CheckNowCommand { get; }
    public ICommand InstallCommand { get; }
    public ICommand ViewReleaseCommand { get; }

    public async System.Threading.Tasks.Task CheckAsync(bool manual)
    {
        if (IsBusy) return;
        IsBusy = true;
        HasError = false;
        Status = "Checking for updates…";
        try
        {
            var update = await UpdateService.CheckAsync();
            S.LastUpdateCheck = DateTime.Now;
            _settings.Save();
            SetAvailable(update);

            if (update is null)
            {
                UpdateStatusFromLastCheck();
            }
            else
            {
                Status = update.AssetUrl.Length > 0
                    ? $"Version {update.Version.ToString(3)} is available."
                    : $"Version {update.Version.ToString(3)} is available, but has no {PackageKind} package for this PC yet. You can get it from the release page.";

                var key = update.Version.ToString(3);
                if (!manual && !string.Equals(S.NotifiedVersion, key, StringComparison.Ordinal))
                {
                    S.NotifiedVersion = key;
                    _settings.Save();
                    UpdateFound?.Invoke(update);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Update check failed: {ex.GetType().Name}: {ex.Message}");
            HasError = manual;
            if (manual)
                Status = "Couldn't reach GitHub. Check your connection and try again.";
            else
                UpdateStatusFromLastCheck();
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsDownloading));
        }
    }

    private async System.Threading.Tasks.Task InstallAsync()
    {
        if (_available is not { AssetUrl.Length: > 0 } update || IsBusy) return;
        IsBusy = true;
        HasError = false;
        Progress = 0;
        OnPropertyChanged(nameof(IsDownloading));
        Status = $"Downloading version {update.Version.ToString(3)}…";
        try
        {
            var progress = new Progress<double>(p => Progress = p);
            await UpdateService.PrepareAsync(update, progress);
            Status = "Installing… Audio Pilot Manager will restart by itself.";
            ExitRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Log.Error("Update could not be installed", ex);
            HasError = true;
            Status = ex is System.IO.InvalidDataException or InvalidOperationException
                ? ex.Message
                : ex is UnauthorizedAccessException
                    ? "Audio Pilot Manager can't replace itself in this folder. Download the update from the release page instead."
                    : "The download failed. Check your connection and try again.";
            IsBusy = false;
            OnPropertyChanged(nameof(IsDownloading));
        }
    }

    private void SetAvailable(UpdateInfo? update)
    {
        _available = update;
        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(AvailableVersion));
        CommandManager.InvalidateRequerySuggested();
    }

    private void UpdateStatusFromLastCheck()
    {
        if (_available is not null) return;
        Status = S.LastUpdateCheck is { } last
            ? $"You're up to date. Last checked {Describe(last)}."
            : "Not checked yet.";
    }

    private static string Describe(DateTime when)
    {
        var today = DateTime.Today;
        if (when.Date == today) return $"today at {when:t}";
        if (when.Date == today.AddDays(-1)) return $"yesterday at {when:t}";
        return when.ToString("d");
    }
}

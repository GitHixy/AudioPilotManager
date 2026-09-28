using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using AudioPilotManager.Audio;
using AudioPilotManager.Services;
using AudioPilotManager.Themes;
using AudioPilotManager.ViewModels;
using AudioPilotManager.Views;

namespace AudioPilotManager;

public partial class App : Application
{
    private const string MutexName = @"Local\AudioPilotManager.SingleInstance";
    private const string ShowEventName = @"Local\AudioPilotManager.Show";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private SettingsService? _settings;
    private AudioService? _audio;
    private HotkeyService? _hotkeys;
    private TrayService? _tray;
    private OsdWindow? _osd;
    private FlyoutWindow? _flyout;
    private MainViewModel? _vm;
    private MainWindow? _window;
    private bool _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // One instance only: a second launch just brings the first one forward.
        _mutex = new Mutex(true, MutexName, out var isFirst);
        if (!isFirst)
        {
            _mutex.Dispose();
            _mutex = null;
            try
            {
                using var signal = EventWaitHandle.OpenExisting(ShowEventName);
                signal.Set();
            }
            catch
            {
                // The first instance is still starting up; nothing else to do.
            }

            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Error("Unhandled exception", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        Log.Info($"Starting Audio Pilot Manager {typeof(App).Assembly.GetName().Version} on Windows {Environment.OSVersion.Version}");

        _settings = new SettingsService();
        _settings.Load();
        StartupService.RepairPathIfEnabled();
        ThemeService.Apply(_settings.Current.Theme, _settings.Current.Accent, _settings.Current.UseBackdrop);
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        _audio = new AudioService(_settings, Dispatcher);
        try
        {
            _audio.Start();
        }
        catch (Exception ex)
        {
            Log.Error("Windows audio could not be started", ex);
            MessageBox.Show("Audio Pilot Manager couldn't connect to Windows audio.\n\n" + ex.Message,
                "Audio Pilot Manager", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        _vm = new MainViewModel(_audio, _settings);
        _osd = new OsdWindow();
        _vm.OsdRequested += (glyph, text) => _osd.Show(glyph, text);
        _vm.HotkeysChanged += (_, _) => RegisterHotkeys();

        _window = new MainWindow(_vm, _settings);
        _window.CloseRequested += OnWindowCloseRequested;
        _window.VisibilityChanged += (_, _) => UpdateMetering();

        _flyout = new FlyoutWindow(_vm);
        _flyout.OpenMainRequested += (_, _) => ShowMainWindow();
        _flyout.VisibilityChanged += (_, _) => UpdateMetering();

        _tray = new TrayService(_vm);
        _tray.LeftClick += (_, _) => ToggleFlyout();
        _tray.OpenRequested += (_, _) => ShowMainWindow();
        _tray.ExitRequested += (_, _) => ExitApp();

        _hotkeys = new HotkeyService();
        RegisterHotkeys();

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var listener = new Thread(() =>
        {
            while (_showEvent.WaitOne())
            {
                if (_exiting) return;
                Dispatcher.BeginInvoke(ShowMainWindow);
            }
        }) { IsBackground = true, Name = "Single instance listener" };
        listener.Start();

        var minimized = e.Args.Any(a => string.Equals(a, StartupService.MinimizedArg, StringComparison.OrdinalIgnoreCase))
                        && _settings.Current.StartMinimized;
        if (minimized)
        {
            _window.PrepareHidden();
        }
        else
        {
            ShowMainWindow();
        }

        UpdateMetering();

#if DEBUG
        // Developer aids for checking the transient windows without a tray click or hotkey.
        if (e.Args.Contains("--debug-flyout")) Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => _flyout.ShowNearTray());
        if (e.Args.Contains("--debug-osd")) Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => _osd.Show(Glyphs.MicrophoneOff, "Microphone muted"));
#endif
    }

    private void RegisterHotkeys()
    {
        if (_hotkeys is null || _vm is null || _settings is null) return;
        _hotkeys.UnregisterAll();
        foreach (var item in _vm.Hotkeys) item.Status = null;
        if (!_settings.Current.HotkeysEnabled) return;

        Register(0, _vm.ToggleMicMute);
        Register(1, _vm.ToggleOutputMute);
        Register(2, _vm.CycleOutput);
        Register(3, ToggleMainWindow);

        void Register(int index, Action action)
        {
            var item = _vm.Hotkeys[index];
            if (!_hotkeys.Register(item.Binding, action))
                item.Status = "Already used by another app";
        }
    }

    private void UpdateMetering()
    {
        var visible = (_window?.IsVisible == true && _window.WindowState != WindowState.Minimized) || _flyout?.IsVisible == true;
        _audio?.SetMetering(visible);
    }

    private void ShowMainWindow()
    {
        if (_window is null) return;
        _flyout?.Hide();
        _window.Reveal();
        UpdateMetering();
    }

    private void ToggleMainWindow()
    {
        if (_window is null) return;
        if (_window.IsVisible && _window.WindowState != WindowState.Minimized && _window.IsActive)
            _window.Hide();
        else
            ShowMainWindow();
        UpdateMetering();
    }

    private void ToggleFlyout()
    {
        if (_flyout is null) return;
        if (_flyout.IsVisible || _flyout.RecentlyClosed)
            _flyout.Hide();
        else
            _flyout.ShowNearTray();
    }

    private void OnWindowCloseRequested(object? sender, EventArgs e)
    {
        if (_settings?.Current.CloseToTray == true && !_exiting)
        {
            _window?.Hide();
            _tray?.ShowHintOnce();
            UpdateMetering();
        }
        else
        {
            ExitApp();
        }
    }

    private void ExitApp()
    {
        if (_exiting) return;
        _exiting = true;
        _window?.SavePlacement();
        _settings?.Flush();
        Shutdown();
    }

    private void OnUserPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        // Follow Windows when it switches between light and dark.
        if (e.Category == Microsoft.Win32.UserPreferenceCategory.General && _settings?.Current.Theme == "System")
            Dispatcher.BeginInvoke(() => ThemeService.Apply(_settings.Current.Theme, _settings.Current.Accent, _settings.Current.UseBackdrop));
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled UI exception", e.Exception);
        // A glitch in one control shouldn't take the mixer down.
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _exiting = true;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        try
        {
            _hotkeys?.Dispose();
            _tray?.Dispose();
            _audio?.Dispose();
            _settings?.Flush();
            _showEvent?.Set();
            _showEvent?.Dispose();
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error("Error during shutdown", ex);
        }

        Log.Info("Exited.");
        base.OnExit(e);
    }
}

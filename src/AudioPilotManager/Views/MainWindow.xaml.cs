using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using AudioPilotManager.Interop;
using AudioPilotManager.Services;
using AudioPilotManager.Themes;
using AudioPilotManager.ViewModels;

namespace AudioPilotManager.Views;

public partial class MainWindow : Window
{
    private readonly SettingsService _settings;
    private readonly MainViewModel _vm;

    public MainWindow(MainViewModel vm, SettingsService settings)
    {
        _vm = vm;
        _settings = settings;
        InitializeComponent();
        DataContext = vm;

        RestorePlacement();
        Topmost = settings.Current.AlwaysOnTop;
        UpdatePinGlyph();
        vm.WindowOptionsChanged += (_, _) =>
        {
            Topmost = _settings.Current.AlwaysOnTop;
            UpdatePinGlyph();
        };

        SourceInitialized += (_, _) => ThemeService.ApplyToWindow(this);
        StateChanged += (_, _) => OnStateChanged();
        IsVisibleChanged += (_, _) => VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? CloseRequested;
    public event EventHandler? VisibilityChanged;

    /// <summary>Creates the window handle without showing anything (start minimised to tray).</summary>
    public void PrepareHidden() => new WindowInteropHelper(this).EnsureHandle();

    public void Reveal()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero) Native.SetForegroundWindow(hwnd);
    }

    private void OnStateChanged()
    {
        // With a custom chrome, a maximised window overhangs the screen by the resize border.
        Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        MaxButton.Content = WindowState == WindowState.Maximized ? "" : "";
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Closing is decided by the app (tray or exit), never by the window alone.
        e.Cancel = true;
        SavePlacement();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RestorePlacement()
    {
        var s = _settings.Current;
        Width = s.WindowWidth;
        Height = s.WindowHeight;
        if (s.WindowLeft is { } left && s.WindowTop is { } top)
        {
            // Only restore a position that's still on a screen.
            var virtualScreen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            if (virtualScreen.IntersectsWith(new Rect(left + 40, top + 10, Math.Max(100, Width - 80), 40)))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = left;
                Top = top;
            }
        }
    }

    public void SavePlacement()
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
        if (bounds.IsEmpty || double.IsNaN(bounds.Left) || bounds.Width < 100) return;
        var s = _settings.Current;
        s.WindowLeft = bounds.Left;
        s.WindowTop = bounds.Top;
        s.WindowWidth = bounds.Width;
        s.WindowHeight = bounds.Height;
        _settings.Save();
    }

    private void UpdatePinGlyph()
    {
        PinGlyph.Text = Topmost ? "" : "";
        PinButton.ToolTip = Topmost ? "Stop keeping on top" : "Keep on top";
    }

    private void OnPinClick(object sender, RoutedEventArgs e) => _vm.AlwaysOnTop = !_vm.AlwaysOnTop;

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}

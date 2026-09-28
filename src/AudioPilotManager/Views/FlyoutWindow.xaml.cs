using System;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using AudioPilotManager.Interop;
using AudioPilotManager.Themes;
using AudioPilotManager.ViewModels;

namespace AudioPilotManager.Views;

/// <summary>The quick mixer that pops up from the tray icon, like the Windows volume flyout.</summary>
public partial class FlyoutWindow : Window
{
    private DateTime _hiddenAt = DateTime.MinValue;

    public FlyoutWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
            Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, ex | Native.WS_EX_TOOLWINDOW);
            ThemeService.ApplyToWindow(this, transient: true);
        };
        Deactivated += (_, _) => Hide();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) _hiddenAt = DateTime.UtcNow;
            VisibilityChanged?.Invoke(this, EventArgs.Empty);
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape) Hide();
        };
    }

    public event EventHandler? OpenMainRequested;
    public event EventHandler? VisibilityChanged;

    /// <summary>
    /// Clicking the tray icon while the flyout is open first deactivates (hides) it, then
    /// delivers the click. This stops that click from immediately reopening it.
    /// </summary>
    public bool RecentlyClosed => (DateTime.UtcNow - _hiddenAt).TotalMilliseconds < 250;

    public void ShowNearTray()
    {
        Show();
        UpdateLayout();
        PositionNearTray();
        Activate();
        Native.SetForegroundWindow(new WindowInteropHelper(this).Handle);

        var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(220);
        Slide.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
            new System.Windows.Media.Animation.DoubleAnimation(18, 0, duration) { EasingFunction = ease });
        Panel.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, duration));
    }

    private void PositionNearTray()
    {
        // Work in the coordinates of the screen the cursor (and so the tray icon) is on.
        var cursor = Control.MousePosition;
        var screen = Screen.FromPoint(cursor);
        var source = PresentationSource.FromVisual(this);
        var toDip = source?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
        var work = screen.WorkingArea;
        var bounds = screen.Bounds;
        var topLeft = toDip.Transform(new Point(work.Left, work.Top));
        var bottomRight = toDip.Transform(new Point(work.Right, work.Bottom));
        const double gap = 12;

        // The taskbar is on whichever edge the working area is missing.
        if (work.Top > bounds.Top)
        {
            Top = topLeft.Y + gap;
            Left = bottomRight.X - ActualWidth - gap;
        }
        else if (work.Left > bounds.Left)
        {
            Left = topLeft.X + gap;
            Top = bottomRight.Y - ActualHeight - gap;
        }
        else
        {
            Left = bottomRight.X - ActualWidth - gap;
            Top = bottomRight.Y - ActualHeight - gap;
        }
    }

    /// <summary>
    /// Changing the Windows default is a real system change, so it only happens when the
    /// person actually picked from the list, never when the list re-syncs itself.
    /// </summary>
    private void OnOutputPicked(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var combo = (System.Windows.Controls.ComboBox)sender;
        if (!IsVisible || !(combo.IsDropDownOpen || combo.IsKeyboardFocusWithin)) return;
        if (e.AddedItems.Count == 1 && e.AddedItems[0] is Audio.AudioDevice device && DataContext is MainViewModel vm)
            vm.QuickOutput = device;
    }

    private void OnOpenMainClick(object sender, RoutedEventArgs e)
    {
        Hide();
        OpenMainRequested?.Invoke(this, EventArgs.Empty);
    }
}

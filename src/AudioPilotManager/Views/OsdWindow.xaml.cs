using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AudioPilotManager.Interop;

namespace AudioPilotManager.Views;

/// <summary>
/// A small pill near the bottom of the screen that confirms what a shortcut did. It never
/// takes focus and clicks pass straight through it, so it can't interrupt a game.
/// </summary>
public partial class OsdWindow : Window
{
    private readonly DispatcherTimer _hideTimer;

    public OsdWindow()
    {
        InitializeComponent();
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1700) };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            Fade(0, () => Hide());
        };

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
            Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, ex | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW | Native.WS_EX_TRANSPARENT);
        };
    }

    public void Show(string glyph, string message)
    {
        GlyphText.Text = glyph;
        MessageText.Text = message;
        if (!IsVisible)
        {
            Opacity = 0;
            Show();
        }

        UpdateLayout();
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Bottom - ActualHeight - 48;
        Fade(1, null);
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void Fade(double to, Action? done)
    {
        var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(to > 0 ? 140 : 260))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        if (done is not null)
            anim.Completed += (_, _) =>
            {
                if (!_hideTimer.IsEnabled) done();
            };
        BeginAnimation(OpacityProperty, anim);
    }
}

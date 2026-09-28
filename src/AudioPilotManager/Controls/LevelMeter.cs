using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AudioPilotManager.Controls;

/// <summary>
/// A lightweight audio level bar with a peak-hold tick. Drawn directly in OnRender, so dozens
/// of them updating at 30 fps cost next to nothing.
/// </summary>
public sealed class LevelMeter : FrameworkElement
{
    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(double), typeof(LevelMeter),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, OnLevelChanged));

    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        nameof(Orientation), typeof(Orientation), typeof(LevelMeter),
        new FrameworkPropertyMetadata(Orientation.Horizontal, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(LevelMeter),
        new FrameworkPropertyMetadata(Brushes.DeepSkyBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
        nameof(Track), typeof(Brush), typeof(LevelMeter),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsDimmedProperty = DependencyProperty.Register(
        nameof(IsDimmed), typeof(bool), typeof(LevelMeter),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    private double _peak;
    private DateTime _peakTime;
    private Brush? _verticalSource;
    private Brush? _verticalFill;

    /// <summary>Horizontal gradients (left = quiet) rotated to run bottom-to-top; cached per brush.</summary>
    private Brush VerticalFill()
    {
        var fill = Fill;
        if (ReferenceEquals(fill, _verticalSource) && _verticalFill is not null) return _verticalFill;
        _verticalSource = fill;
        if (fill is LinearGradientBrush lg)
        {
            var clone = lg.Clone();
            clone.StartPoint = new Point(0.5, 1);
            clone.EndPoint = new Point(0.5, 0);
            clone.Freeze();
            _verticalFill = clone;
        }
        else
        {
            _verticalFill = fill;
        }

        return _verticalFill;
    }

    public double Level
    {
        get => (double)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>Brush for the lit part. It is laid out across the whole meter, so a gradient maps to loudness.</summary>
    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public Brush? Track
    {
        get => (Brush?)GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    /// <summary>Muted sources still show their signal, but faded.</summary>
    public bool IsDimmed
    {
        get => (bool)GetValue(IsDimmedProperty);
        set => SetValue(IsDimmedProperty, value);
    }

    private static void OnLevelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var m = (LevelMeter)d;
        var level = (double)e.NewValue;
        var now = DateTime.UtcNow;
        if (level >= m._peak)
        {
            m._peak = level;
            m._peakTime = now;
        }
        else if ((now - m._peakTime).TotalMilliseconds > 900)
        {
            m._peak = Math.Max(level, m._peak - 0.02);
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        var vertical = Orientation == Orientation.Vertical;
        var radius = (vertical ? w : h) / 2;
        var full = new Rect(0, 0, w, h);
        if (Track is not null)
            dc.DrawRoundedRectangle(Track, null, full, radius, radius);

        var level = Math.Clamp(Level, 0, 1);
        if (level <= 0.001 && _peak <= 0.01) return;

        // Lay the brush out over the full length so colours map to loudness, not to bar length.
        var fill = vertical ? VerticalFill() : Fill;

        dc.PushOpacity(IsDimmed ? 0.35 : 1);
        dc.PushClip(new RectangleGeometry(full, radius, radius));
        var lit = vertical ? new Rect(0, h * (1 - level), w, h * level) : new Rect(0, 0, w * level, h);
        dc.PushClip(new RectangleGeometry(lit));
        dc.DrawRectangle(fill, null, full);
        dc.Pop();

        // Peak hold tick.
        if (_peak > 0.02)
        {
            var tick = vertical ? new Rect(0, Math.Max(0, h * (1 - _peak) - 1), w, 2) : new Rect(Math.Min(w - 2, w * _peak - 1), 0, 2, h);
            dc.PushClip(new RectangleGeometry(tick));
            dc.DrawRectangle(fill, null, full);
            dc.Pop();
        }

        dc.Pop();
        dc.Pop();
    }
}

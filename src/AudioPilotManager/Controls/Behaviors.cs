using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AudioPilotManager.Controls;

/// <summary>Attached state that templates can trigger on.</summary>
public static class Ui
{
    public static readonly DependencyProperty IsMutedProperty = DependencyProperty.RegisterAttached(
        "IsMuted", typeof(bool), typeof(Ui), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static bool GetIsMuted(DependencyObject o) => (bool)o.GetValue(IsMutedProperty);

    public static void SetIsMuted(DependencyObject o, bool value) => o.SetValue(IsMutedProperty, value);

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.RegisterAttached(
        "Glyph", typeof(string), typeof(Ui), new PropertyMetadata(null));

    public static string? GetGlyph(DependencyObject o) => (string?)o.GetValue(GlyphProperty);

    public static void SetGlyph(DependencyObject o, string? value) => o.SetValue(GlyphProperty, value);
}

/// <summary>Left-click a button to open its ContextMenu underneath it (a "more" menu).</summary>
public static class MenuButton
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(MenuButton), new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject o) => (bool)o.GetValue(EnabledProperty);

    public static void SetEnabled(DependencyObject o, bool value) => o.SetValue(EnabledProperty, value);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ButtonBase b) return;
        b.Click -= OnClick;
        if ((bool)e.NewValue) b.Click += OnClick;
    }

    private static void OnClick(object sender, RoutedEventArgs e)
    {
        var b = (ButtonBase)sender;
        if (b.ContextMenu is not { } menu) return;
        menu.DataContext = b.DataContext;
        menu.PlacementTarget = b;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }
}

/// <summary>Scroll the mouse wheel over a slider to nudge it; hold Shift for fine steps.</summary>
public static class SliderWheel
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(SliderWheel), new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject o) => (bool)o.GetValue(EnabledProperty);

    public static void SetEnabled(DependencyObject o, bool value) => o.SetValue(EnabledProperty, value);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not RangeBase slider) return;
        slider.PreviewMouseWheel -= OnWheel;
        if ((bool)e.NewValue) slider.PreviewMouseWheel += OnWheel;
    }

    private static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        var slider = (RangeBase)sender;
        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 1 : 2;
        slider.Value = Math.Clamp(slider.Value + (e.Delta > 0 ? step : -step), slider.Minimum, slider.Maximum);
        e.Handled = true;
    }
}

/// <summary>Fades and slides an element in when it is first loaded (used for mixer strips and pages).</summary>
public static class Entrance
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(Entrance), new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject o) => (bool)o.GetValue(EnabledProperty);

    public static void SetEnabled(DependencyObject o, bool value) => o.SetValue(EnabledProperty, value);

    public static readonly DependencyProperty OffsetProperty = DependencyProperty.RegisterAttached(
        "Offset", typeof(double), typeof(Entrance), new PropertyMetadata(14.0));

    public static double GetOffset(DependencyObject o) => (double)o.GetValue(OffsetProperty);

    public static void SetOffset(DependencyObject o, double value) => o.SetValue(OffsetProperty, value);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement fe && (bool)e.NewValue)
            fe.Loaded += OnLoaded;
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        var fe = (FrameworkElement)sender;
        if (SystemParameters.ClientAreaAnimation == false) return;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(320);
        var translate = new TranslateTransform(0, GetOffset(fe));
        fe.RenderTransform = translate;
        fe.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(GetOffset(fe), 0, duration) { EasingFunction = ease });
    }
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var b = value is true;
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Visible != Invert;
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        (value is null || value is string { Length: 0 }) != Invert ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Two-way binds a RadioButton to a string/enum property: checked when value equals the parameter.</summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? parameter?.ToString() ?? Binding.DoNothing : Binding.DoNothing;
}

public sealed class PercentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double d ? $"{Math.Round(d):0}" : "0";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class BalanceConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var d = value is double v ? Math.Round(v) : 0;
        return d == 0 ? "Centre" : d < 0 ? $"L {-d:0}" : $"R {d:0}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class NegateThickness : IValueConverter
{
    public static NegateThickness Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Thickness t ? new Thickness(-t.Left, -t.Top, -t.Right, -t.Bottom) : new Thickness();

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class NonZeroToVisibility : IValueConverter
{
    public static NonZeroToVisibility Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int n && n > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class InitialConverter : IValueConverter
{
    public static InitialConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string { Length: > 0 } s ? s.Trim()[..1].ToUpper(culture) : "?";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Multiplies a number by the parameter (used to derive sizes).</summary>
public sealed class ScaleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double d && double.TryParse(parameter?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? d * f : 0.0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Finds the visual ancestor of a type (used by the title bar drag area).</summary>
internal static class VisualTreeHelpers
{
    public static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d is not null and not T)
            d = VisualTreeHelper.GetParent(d);
        return d as T;
    }
}

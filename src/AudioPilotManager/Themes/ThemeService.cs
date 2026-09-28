using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using AudioPilotManager.Interop;
using Microsoft.Win32;

namespace AudioPilotManager.Themes;

public sealed record AccentOption(string Name, Color Start, Color End)
{
    public Brush Preview { get; } = Freeze(new LinearGradientBrush(Start, End, 45));

    private static Brush Freeze(Brush b)
    {
        b.Freeze();
        return b;
    }
}

/// <summary>
/// Builds every colour the UI uses from (theme, accent) and swaps them into the application
/// resources, so switching is instant and all windows follow. XAML only ever uses
/// DynamicResource keys from here.
/// </summary>
public static class ThemeService
{
    public static IReadOnlyList<AccentOption> Accents { get; } = new[]
    {
        new AccentOption("Aurora", Hex("#8B5CF6"), Hex("#22D3EE")),
        new AccentOption("Ocean", Hex("#3B82F6"), Hex("#06B6D4")),
        new AccentOption("Emerald", Hex("#10B981"), Hex("#A3E635")),
        new AccentOption("Sunset", Hex("#F97316"), Hex("#F43F5E")),
        new AccentOption("Blossom", Hex("#EC4899"), Hex("#A855F7")),
        new AccentOption("Gold", Hex("#F59E0B"), Hex("#FACC15")),
        new AccentOption("Mono", Hex("#94A3B8"), Hex("#E2E8F0")),
    };

    public static bool IsDark { get; private set; } = true;

    /// <summary>Mica needs the Windows 11 22H2 backdrop API.</summary>
    public static bool BackdropSupported => Environment.OSVersion.Version.Build >= 22621;

    public static bool BackdropActive { get; private set; }

    public static event EventHandler? Changed;

    public static void Apply(string theme, string accentName, bool useBackdrop)
    {
        IsDark = theme switch
        {
            "Dark" => true,
            "Light" => false,
            _ => SystemUsesDarkTheme(),
        };
        BackdropActive = useBackdrop && BackdropSupported;

        var accent = Accents.FirstOrDefault(a => a.Name == accentName) ?? Accents[0];
        var r = Application.Current.Resources;

        if (IsDark)
        {
            Set(r, "WindowBackgroundBrush", BackdropActive ? Hex("#38101218") : Hex("#FF111319"));
            Set(r, "SidebarBrush", BackdropActive ? Hex("#26000000") : Hex("#FF0C0E13"));
            Set(r, "CardBrush", Hex("#0FFFFFFF"));
            Set(r, "CardHoverBrush", Hex("#17FFFFFF"));
            Set(r, "CardBorderBrush", Hex("#1CFFFFFF"));
            Set(r, "ControlBrush", Hex("#24FFFFFF"));
            Set(r, "ControlHoverBrush", Hex("#33FFFFFF"));
            Set(r, "ControlPressedBrush", Hex("#1AFFFFFF"));
            Set(r, "DividerBrush", Hex("#14FFFFFF"));
            Set(r, "PopupBrush", Hex("#FF1C1F29"));
            Set(r, "PopupBorderBrush", Hex("#FF2E3240"));
            Set(r, "TextPrimaryBrush", Hex("#FFF2F4FA"));
            Set(r, "TextSecondaryBrush", Hex("#FFA6ACC0"));
            Set(r, "TextTertiaryBrush", Hex("#FF6E748A"));
            Set(r, "ThumbBrush", Hex("#FFFFFFFF"));
            Set(r, "OsdBrush", Hex("#F01A1C24"));
            Set(r, "FlyoutBrush", BackdropActive ? Hex("#C8181A22") : Hex("#FF16181F"));
        }
        else
        {
            Set(r, "WindowBackgroundBrush", BackdropActive ? Hex("#40F6F7FB") : Hex("#FFF3F4F8"));
            Set(r, "SidebarBrush", BackdropActive ? Hex("#14FFFFFF") : Hex("#FFEBEDF3"));
            Set(r, "CardBrush", Hex("#B8FFFFFF"));
            Set(r, "CardHoverBrush", Hex("#E6FFFFFF"));
            Set(r, "CardBorderBrush", Hex("#14000000"));
            Set(r, "ControlBrush", Hex("#1A0B1020"));
            Set(r, "ControlHoverBrush", Hex("#260B1020"));
            Set(r, "ControlPressedBrush", Hex("#120B1020"));
            Set(r, "DividerBrush", Hex("#120B1020"));
            Set(r, "PopupBrush", Hex("#FFFCFCFE"));
            Set(r, "PopupBorderBrush", Hex("#FFDDE0E8"));
            Set(r, "TextPrimaryBrush", Hex("#FF151823"));
            Set(r, "TextSecondaryBrush", Hex("#FF545A6E"));
            Set(r, "TextTertiaryBrush", Hex("#FF8C92A5"));
            Set(r, "ThumbBrush", Hex("#FFFFFFFF"));
            Set(r, "OsdBrush", Hex("#F5FCFCFE"));
            Set(r, "FlyoutBrush", BackdropActive ? Hex("#D0F7F8FB") : Hex("#FFF7F8FB"));
        }

        var mid = Mix(accent.Start, accent.End, 0.5);
        Set(r, "AccentBrush", IsDark ? Mix(mid, Colors.White, 0.1) : Mix(accent.Start, Colors.Black, 0.08));
        Set(r, "AccentStartBrush", accent.Start);
        Set(r, "AccentEndBrush", accent.End);
        Set(r, "AccentSoftBrush", WithAlpha(mid, IsDark ? (byte)0x33 : (byte)0x26));
        Set(r, "AccentForegroundBrush", Luminance(mid) > 0.62 ? Hex("#FF10121A") : Colors.White);
        SetBrush(r, "AccentGradientBrush", new LinearGradientBrush(accent.Start, accent.End, new Point(0, 0.5), new Point(1, 0.5)));
        SetBrush(r, "AccentGradientDiagonalBrush", new LinearGradientBrush(accent.Start, accent.End, 45));
        SetBrush(r, "AccentGradientVerticalBrush", new LinearGradientBrush(accent.Start, accent.End, new Point(0.5, 1), new Point(0.5, 0)));
        SetBrush(r, "AccentGlowBrush", new RadialGradientBrush(WithAlpha(mid, 0x70), WithAlpha(mid, 0x00)));
        r["AccentColor"] = mid;

        Set(r, "DangerBrush", Hex(IsDark ? "#FFFF5C72" : "#FFE11D48"));
        Set(r, "DangerSoftBrush", Hex(IsDark ? "#33FF5C72" : "#1FE11D48"));
        Set(r, "SuccessBrush", Hex(IsDark ? "#FF34D399" : "#FF059669"));
        Set(r, "WarningBrush", Hex(IsDark ? "#FFFBBF24" : "#FFD97706"));

        // Meters: accent gradient that runs hot near the top.
        var meter = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        meter.GradientStops.Add(new GradientStop(accent.Start, 0));
        meter.GradientStops.Add(new GradientStop(accent.End, 0.78));
        meter.GradientStops.Add(new GradientStop(Hex("#FFFBBF24"), 0.9));
        meter.GradientStops.Add(new GradientStop(Hex("#FFFF5C72"), 1));
        SetBrush(r, "MeterBrush", meter);

        foreach (Window w in Application.Current.Windows)
            ApplyToWindow(w);
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Dark title-bar hint, rounded corners and Mica for one window.</summary>
    public static void ApplyToWindow(Window window, bool transient = false)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        var dark = IsDark ? 1 : 0;
        Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        var corner = Native.DWMWCP_ROUND;
        Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

        // Our title bars are custom. With the frame extended into the client area, DWM would
        // otherwise paint its own caption buttons underneath ours; they only exist with WS_SYSMENU.
        var style = Native.GetWindowLong(hwnd, Native.GWL_STYLE);
        if ((style & Native.WS_SYSMENU) != 0)
            Native.SetWindowLong(hwnd, Native.GWL_STYLE, style & ~Native.WS_SYSMENU);

        if (window.AllowsTransparency || !BackdropSupported) return;

        var source = HwndSource.FromHwnd(hwnd);
        if (BackdropActive)
        {
            if (source?.CompositionTarget is not null) source.CompositionTarget.BackgroundColor = Colors.Transparent;
            var margins = new Native.Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            Native.DwmExtendFrameIntoClientArea(hwnd, ref margins);
            var type = transient ? Native.DWMSBT_TRANSIENTWINDOW : Native.DWMSBT_MAINWINDOW;
            Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_SYSTEMBACKDROP_TYPE, ref type, sizeof(int));
        }
        else
        {
            var none = Native.DWMSBT_NONE;
            Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_SYSTEMBACKDROP_TYPE, ref none, sizeof(int));
            var margins = new Native.Margins();
            Native.DwmExtendFrameIntoClientArea(hwnd, ref margins);
            if (source?.CompositionTarget is not null)
                source.CompositionTarget.BackgroundColor = IsDark ? Hex("#FF111319") : Hex("#FFF3F4F8");
        }
    }

    public static bool SystemUsesDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is not int v || v == 0;
        }
        catch
        {
            return true;
        }
    }

    private static void Set(ResourceDictionary r, string key, Color color) => SetBrush(r, key, new SolidColorBrush(color));

    private static void SetBrush(ResourceDictionary r, string key, Brush brush)
    {
        brush.Freeze();
        r[key] = brush;
    }

    public static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        (byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    private static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;
}

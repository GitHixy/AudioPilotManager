using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using AudioPilotManager.Services;

namespace AudioPilotManager.Controls;

/// <summary>
/// Click, then press a shortcut. Needs at least one of Ctrl / Alt / Win so it can't swallow
/// ordinary typing system-wide. Esc cancels, Backspace or Delete clears.
/// </summary>
public sealed class HotkeyBox : TextBox
{
    public static readonly DependencyProperty HotkeyProperty = DependencyProperty.Register(
        nameof(Hotkey), typeof(HotkeyBinding), typeof(HotkeyBox),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((HotkeyBox)d).ShowCurrent()));

    public HotkeyBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        IsUndoEnabled = false;
        Cursor = Cursors.Hand;
        ContextMenu = null;
        GotKeyboardFocus += (_, _) => Text = "Press a shortcut…";
        LostKeyboardFocus += (_, _) => ShowCurrent();
    }

    public HotkeyBinding? Hotkey
    {
        get => (HotkeyBinding?)GetValue(HotkeyProperty);
        set => SetValue(HotkeyProperty, value);
    }

    private void ShowCurrent() => Text = Hotkey?.ToString() ?? "Not set";

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        switch (key)
        {
            case Key.Escape:
                Keyboard.ClearFocus();
                MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                return;
            case Key.Back or Key.Delete when Keyboard.Modifiers == ModifierKeys.None:
                Hotkey = new HotkeyBinding();
                MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                return;
            case Key.Tab when Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift:
                e.Handled = false;
                return;
            case Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin:
                Text = Describe(Keyboard.Modifiers) + "…";
                return;
        }

        var mods = Keyboard.Modifiers;
        var win = Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin);
        if ((mods & (ModifierKeys.Control | ModifierKeys.Alt)) == 0 && !win)
        {
            Text = "Use Ctrl, Alt or Win with a key";
            return;
        }

        uint m = 0;
        if (mods.HasFlag(ModifierKeys.Control)) m |= HotkeyBinding.Control;
        if (mods.HasFlag(ModifierKeys.Alt)) m |= HotkeyBinding.Alt;
        if (mods.HasFlag(ModifierKeys.Shift)) m |= HotkeyBinding.Shift;
        if (win) m |= HotkeyBinding.Win;

        Hotkey = new HotkeyBinding(m, (uint)KeyInterop.VirtualKeyFromKey(key));
        GetBindingExpression(HotkeyProperty)?.UpdateSource();
        MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
    }

    private static string Describe(ModifierKeys mods)
    {
        var s = "";
        if (mods.HasFlag(ModifierKeys.Control)) s += "Ctrl + ";
        if (mods.HasFlag(ModifierKeys.Alt)) s += "Alt + ";
        if (mods.HasFlag(ModifierKeys.Shift)) s += "Shift + ";
        return s;
    }
}

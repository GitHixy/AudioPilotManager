using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using AudioPilotManager.Themes;
using AudioPilotManager.ViewModels;

namespace AudioPilotManager.Services;

/// <summary>The notification-area icon: left click opens the quick mixer, right click the menu.</summary>
public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly MainViewModel _vm;
    private readonly ContextMenuStrip _menu;
    private bool _hintShown;

    public TrayService(MainViewModel vm)
    {
        _vm = vm;
        _menu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true, Padding = new Padding(4) };
        _menu.Opening += (_, _) => BuildMenu();

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "Audio Pilot Manager",
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) LeftClick?.Invoke(this, EventArgs.Empty);
        };
        _icon.MouseMove += (_, _) => UpdateTooltip();
    }

    public event EventHandler? LeftClick;
    public event EventHandler? OpenRequested;
    public event EventHandler? ExitRequested;

    private static Icon LoadIcon()
    {
        try
        {
            var info = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
            if (info is not null)
            {
                using var stream = info.Stream;
                return new Icon(stream, SystemInformation.SmallIconSize);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Tray icon could not be loaded", ex);
        }

        return SystemIcons.Application;
    }

    private void UpdateTooltip()
    {
        var output = _vm.DefaultOutput;
        var text = output is null ? "Audio Pilot Manager" : $"Audio Pilot Manager\n{output.Name}: {(output.IsMuted ? "muted" : $"{output.Volume:0}%")}";
        if (_vm.DefaultInput is { IsMuted: true }) text += "\nMicrophone muted";
        _icon.Text = text.Length > 127 ? text[..127] : text;
    }

    /// <summary>Tells the user once that closing the window keeps the app in the tray.</summary>
    public void ShowHintOnce()
    {
        if (_hintShown) return;
        _hintShown = true;
        _icon.ShowBalloonTip(3000, "Still here", "Audio Pilot Manager keeps running in the notification area. Right-click the icon to exit.", ToolTipIcon.None);
    }

    private void BuildMenu()
    {
        _menu.Items.Clear();
        _menu.Renderer = new FlatRenderer(ThemeService.IsDark);
        _menu.BackColor = ThemeService.IsDark ? Color.FromArgb(28, 31, 41) : Color.FromArgb(252, 252, 254);
        _menu.ForeColor = ThemeService.IsDark ? Color.FromArgb(242, 244, 250) : Color.FromArgb(21, 24, 35);
        _menu.Font = new Font("Segoe UI", 9.5f);

        Add("Open Audio Pilot Manager", () => OpenRequested?.Invoke(this, EventArgs.Empty)).Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        _menu.Items.Add(new ToolStripSeparator());

        var mic = _vm.DefaultInput;
        var micItem = Add("Mute microphone", _vm.ToggleMicMute);
        micItem.Checked = mic?.IsMuted == true;
        micItem.Enabled = mic is not null;

        var output = _vm.DefaultOutput;
        var outItem = Add("Mute speakers", _vm.ToggleOutputMute);
        outItem.Checked = output?.IsMuted == true;
        outItem.Enabled = output is not null;

        // Output devices: pick one to make it the default.
        if (_vm.Outputs.Count > 1)
        {
            var devices = new ToolStripMenuItem("Output device");
            foreach (var d in _vm.Outputs.ToList())
            {
                var item = new ToolStripMenuItem(d.Name) { Checked = d.IsDefault };
                item.Click += (_, _) => _vm.QuickOutput = d;
                devices.DropDownItems.Add(item);
            }

            Style(devices);
            _menu.Items.Add(devices);
        }

        if (_vm.Profiles.Count > 0)
        {
            var profiles = new ToolStripMenuItem("Apply profile");
            foreach (var p in _vm.Profiles.ToList())
            {
                var item = new ToolStripMenuItem(p.Name);
                item.Click += (_, _) => p.ApplyCommand.Execute(null);
                profiles.DropDownItems.Add(item);
            }

            Style(profiles);
            _menu.Items.Add(profiles);
        }

        _menu.Items.Add(new ToolStripSeparator());
        Add("♥  Support on Patreon", () => _vm.OpenPatreonCommand.Execute(null));
        Add("Exit", () => ExitRequested?.Invoke(this, EventArgs.Empty));
    }

    private void Style(ToolStripMenuItem parent)
    {
        parent.DropDown.BackColor = _menu.BackColor;
        parent.DropDown.ForeColor = _menu.ForeColor;
        parent.DropDown.Renderer = _menu.Renderer;
        foreach (ToolStripItem child in parent.DropDownItems) child.ForeColor = _menu.ForeColor;
    }

    private ToolStripMenuItem Add(string text, Action action)
    {
        var item = new ToolStripMenuItem(text) { ForeColor = _menu.ForeColor, Padding = new Padding(4, 3, 4, 3) };
        item.Click += (_, _) =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log.Error("Tray action failed", ex);
            }
        };
        _menu.Items.Add(item);
        return item;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
    }

    /// <summary>Flat, theme-matched menu instead of the dated default look.</summary>
    private sealed class FlatRenderer : ToolStripProfessionalRenderer
    {
        private readonly bool _dark;

        public FlatRenderer(bool dark) : base(new Colors(dark))
        {
            _dark = dark;
            RoundedEdges = false;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled
                ? (_dark ? Color.FromArgb(242, 244, 250) : Color.FromArgb(21, 24, 35))
                : Color.FromArgb(120, 125, 140);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = _dark ? Color.FromArgb(166, 172, 192) : Color.FromArgb(84, 90, 110);
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var r = e.ImageRectangle;
            using var pen = new Pen(_dark ? Color.FromArgb(34, 211, 238) : Color.FromArgb(124, 58, 237), 2f);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.DrawLines(pen, new[]
            {
                new PointF(r.Left + r.Width * 0.2f, r.Top + r.Height * 0.52f),
                new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.74f),
                new PointF(r.Left + r.Width * 0.8f, r.Top + r.Height * 0.3f),
            });
        }
    }

    private sealed class Colors : ProfessionalColorTable
    {
        private readonly Color _bg, _hover, _border, _separator;

        public Colors(bool dark)
        {
            _bg = dark ? Color.FromArgb(28, 31, 41) : Color.FromArgb(252, 252, 254);
            _hover = dark ? Color.FromArgb(46, 50, 64) : Color.FromArgb(234, 236, 243);
            _border = dark ? Color.FromArgb(46, 50, 64) : Color.FromArgb(221, 224, 232);
            _separator = dark ? Color.FromArgb(46, 50, 64) : Color.FromArgb(228, 230, 237);
            UseSystemColors = false;
        }

        public override Color ToolStripDropDownBackground => _bg;
        public override Color ImageMarginGradientBegin => _bg;
        public override Color ImageMarginGradientMiddle => _bg;
        public override Color ImageMarginGradientEnd => _bg;
        public override Color MenuBorder => _border;
        public override Color MenuItemBorder => _hover;
        public override Color MenuItemSelected => _hover;
        public override Color MenuItemSelectedGradientBegin => _hover;
        public override Color MenuItemSelectedGradientEnd => _hover;
        public override Color MenuItemPressedGradientBegin => _hover;
        public override Color MenuItemPressedGradientEnd => _hover;
        public override Color CheckBackground => _bg;
        public override Color CheckSelectedBackground => _hover;
        public override Color CheckPressedBackground => _hover;
        public override Color SeparatorDark => _separator;
        public override Color SeparatorLight => _separator;
    }
}

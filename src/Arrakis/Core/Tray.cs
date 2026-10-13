using System;
using Arrakis.Modules;

namespace Arrakis.Core
{
    /// <summary>The icon in the notification area, with a right-click menu.</summary>
    public sealed class Tray : IDisposable
    {
        private readonly System.Windows.Forms.NotifyIcon _icon;

        public Tray()
        {
            _icon = new System.Windows.Forms.NotifyIcon
            {
                Text = "Arrakis",
                Visible = true
            };
            try { _icon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath); }
            catch { _icon.Icon = System.Drawing.SystemIcons.Application; }

            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Open Arrakis", null, (s, e) => App.Instance.ShowHub());
            menu.Items.Add("Command bar", null, (s, e) => CommandBar.Toggle());
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("Quick controls", null, (s, e) => App.Instance.Controls.Toggle());
            menu.Items.Add("New note", null, (s, e) => App.Instance.Notes.Open(null, true));
            menu.Items.Add("Clipboard", null, (s, e) => App.Instance.Clipboard.ShowPanel());
            var shot = new System.Windows.Forms.ToolStripMenuItem("Screenshot");
            shot.DropDownItems.Add("Area", null, (s, e) => App.Instance.Capture.StartCapture());
            shot.DropDownItems.Add("Window", null, (s, e) => App.Instance.Capture.Capture(Arrakis.Modules.CaptureMode.Window));
            shot.DropDownItems.Add("Full screen", null, (s, e) => App.Instance.Capture.Capture(Arrakis.Modules.CaptureMode.FullScreen));
            shot.DropDownItems.Add("All screens", null, (s, e) => App.Instance.Capture.Capture(Arrakis.Modules.CaptureMode.AllScreens));
            shot.DropDownItems.Add("Last area again", null, (s, e) => App.Instance.Capture.Capture(Arrakis.Modules.CaptureMode.LastRegion));
            shot.DropDownItems.Add("Area in 5 seconds", null, (s, e) => App.Instance.Capture.CaptureAfter(5, Arrakis.Modules.CaptureMode.Region));
            menu.Items.Add(shot);
            menu.Items.Add("Start or pause focus", null, (s, e) => App.Instance.Focus.ToggleFromHotkey());
            menu.Items.Add("Edit widgets", null, (s, e) => App.Instance.Widgets.ToggleEditMode());
            menu.Items.Add("Run a speed test", null, (s, e) => App.Instance.Network.RunSpeedTestFromAnywhere());
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("Show Windows taskbar", null, (s, e) =>
            {
                Settings.Current.Dock.HideTaskbar = false;
                Settings.Save();
                TaskbarManager.Restore();
            });
            menu.Items.Add("Exit Arrakis", null, (s, e) => App.Instance.Quit());
            _icon.ContextMenuStrip = menu;
            _icon.BalloonTipClicked += (s, e) => { var a = _onClick; _onClick = null; a?.Invoke(); };
            _icon.BalloonTipClosed += (s, e) => _onClick = null;
            _icon.MouseClick += (s, e) =>
            {
                if (e.Button == System.Windows.Forms.MouseButtons.Left) App.Instance.ShowHub();
            };
        }

        private Action _onClick;
        private System.Windows.Forms.ToolStripMenuItem _update;

        public void Notify(string title, string text) => Notify(title, text, null);

        /// <summary>Shows a notification. If onClick is given, clicking the notification runs it.</summary>
        public void Notify(string title, string text, Action onClick)
        {
            _onClick = onClick;
            try { _icon.ShowBalloonTip(5000, title, text, System.Windows.Forms.ToolTipIcon.None); }
            catch { }
        }

        /// <summary>Adds (or updates) an "Install update" item at the top of the menu.</summary>
        public void ShowUpdateItem(string version)
        {
            var menu = _icon.ContextMenuStrip;
            if (_update == null)
            {
                _update = new System.Windows.Forms.ToolStripMenuItem();
                _update.Click += (s, e) => _ = App.Instance.InstallUpdate();
                menu.Items.Insert(0, _update);
                menu.Items.Insert(1, new System.Windows.Forms.ToolStripSeparator());
            }
            _update.Text = "Install update " + version;
        }

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
        }
    }
}

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
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("Clipboard", null, (s, e) => App.Instance.Clipboard.ShowPanel());
            menu.Items.Add("Take a screenshot", null, (s, e) => App.Instance.Capture.StartCapture());
            menu.Items.Add("Start or pause focus", null, (s, e) => App.Instance.Focus.ToggleFromHotkey());
            menu.Items.Add("Edit widgets", null, (s, e) => App.Instance.Widgets.ToggleEditMode());
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("Show Windows taskbar", null, (s, e) =>
            {
                Settings.Current.Dock.HideTaskbar = false;
                Settings.Save();
                TaskbarManager.Restore();
            });
            menu.Items.Add("Exit Arrakis", null, (s, e) => App.Instance.Quit());
            _icon.ContextMenuStrip = menu;
            _icon.MouseClick += (s, e) =>
            {
                if (e.Button == System.Windows.Forms.MouseButtons.Left) App.Instance.ShowHub();
            };
        }

        public void Notify(string title, string text)
        {
            try { _icon.ShowBalloonTip(5000, title, text, System.Windows.Forms.ToolTipIcon.None); }
            catch { }
        }

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
        }
    }
}

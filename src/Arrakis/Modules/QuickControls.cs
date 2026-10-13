using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Arrakis.Core;
using Windows.Devices.Radios;

namespace Arrakis.Modules
{
    /// <summary>Volume per app, brightness and the Windows switches people reach for most, in one panel.</summary>
    public class QuickControlsModule : ModuleBase
    {
        public override string Id => "controls";
        public override string Name => "Quick controls";
        public override string Description => "Volume for each app, brightness for every screen, and Wi-Fi, Bluetooth, dark mode and more in one panel.";
        public override string IconData => Icons.Sliders;
        public override string ShortcutText => S.Hotkey;
        public override bool IsEnabled { get => S.Enabled; set => S.Enabled = value; }

        private ControlsSettings S => Settings.Current.Controls;
        private QuickControlsPanel _panel;

        protected override void OnStart()
        {
            if (!Hotkeys.Register("controls", S.Hotkey, Toggle))
                App.Instance.Tray.Notify("Quick controls shortcut unavailable", S.Hotkey + " is used by another app. Choose another in Arrakis > Quick controls.");
        }

        protected override void OnStop()
        {
            Hotkeys.Unregister("controls");
            _panel?.Close();
            _panel = null;
        }

        public void Toggle()
        {
            if (_panel != null) { _panel.Close(); _panel = null; return; }
            _panel = new QuickControlsPanel();
            _panel.Closed += (s, e) => _panel = null;
            _panel.Show();
            _panel.Activate();
        }

        public override UIElement BuildPage()
        {
            var hotkey = UI.TextInput(S.Hotkey, 160);
            var status = UI.Text("", 12, Theme.Muted);
            var apply = FlatButton.Secondary("Use this shortcut", () =>
            {
                string old = S.Hotkey;
                S.Hotkey = hotkey.Text.Trim();
                if (IsRunning)
                {
                    Hotkeys.Unregister("controls");
                    if (Hotkeys.Register("controls", S.Hotkey, Toggle)) { status.Text = "Saved."; Settings.Save(); }
                    else { status.Text = S.Hotkey + " is taken. Try another."; S.Hotkey = old; Hotkeys.Register("controls", old, Toggle); }
                }
                else { Settings.Save(); status.Text = "Saved."; }
            });

            var apps = new ToggleSwitch(S.ShowAppVolumes, "Show volume for each app");
            apps.Toggled += on => { S.ShowAppVolumes = on; Settings.Save(); };
            var bright = new ToggleSwitch(S.ShowBrightness, "Show brightness");
            bright.Toggled += on => { S.ShowBrightness = on; Settings.Save(); };

            var live = new Border();
            live.Child = UI.Text("Loading...", 13, Theme.Muted);
            live.Loaded += async (s, e) => live.Child = await QuickControlsPanel.BuildBody(false, null);

            return UI.V(16,
                UI.Card(UI.V(10,
                    UI.Text("Everything you'd normally dig through Settings for", 18, Theme.Text, FontWeights.SemiBold, true),
                    UI.Text("Press " + S.Hotkey + " anywhere, or right-click the Arrakis icon > Quick controls. Click outside the panel to close it.", 14, Theme.Muted),
                    UI.H(10, FlatButton.Primary("Open quick controls", Toggle))), 22),
                UI.Label("Try it here"),
                UI.Card(live, 20),
                UI.Label("Settings"),
                UI.Card(UI.V(4,
                    UI.SettingRow("Shortcut", null, UI.V(4, UI.H(8, hotkey, apply), status)),
                    UI.SettingRow("Volume for each app", "Lists every app that's making sound, with its own slider and mute button.", apps),
                    UI.SettingRow("Brightness", "Laptop screens, and external monitors that support DDC/CI (most from the last ten years).", bright)), 8));
        }
    }

    /// <summary>The pop-up panel. Opens bottom right, closes when you click away.</summary>
    public class QuickControlsPanel : Window
    {
        private readonly Border _host = new Border();
        private bool _closing;

        public QuickControlsPanel()
        {
            Title = "Quick controls";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            Width = 440;
            SizeToContent = SizeToContent.Height;
            FontFamily = Theme.Font;
            WindowStartupLocation = WindowStartupLocation.Manual;

            _host.Child = UI.Text("Loading...", 13, Theme.Muted);
            Content = new Border
            {
                Background = Theme.Popup,
                BorderBrush = Theme.GlassBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(26),
                Padding = new Thickness(18),
                Margin = new Thickness(20),
                Effect = Theme.Shadow(50, 0.5, 14),
                Child = _host
            };
            SourceInitialized += (s, e) => WindowHelper.MakeToolWindow(this, false);
            Loaded += async (s, e) =>
            {
                Place();
                _host.Child = await BuildBody(true, SafeClose);
                Place();
            };
            SizeChanged += (s, e) => Place();
            Deactivated += (s, e) => SafeClose();
            KeyDown += (s, e) => { if (e.Key == Key.Escape) SafeClose(); };
        }

        private void Place()
        {
            var work = SystemParameters.WorkArea;
            Left = work.Right - ActualWidth + 8;
            Top = Math.Max(work.Top, work.Bottom - ActualHeight + 8);
        }

        private void SafeClose()
        {
            if (_closing) return;
            _closing = true;
            try { Close(); } catch { }
        }

        /// <summary>Builds the controls. Used by the pop-up and by the module page.</summary>
        public static async Task<UIElement> BuildBody(bool popup, Action close)
        {
            var st = Settings.Current.Controls;
            var root = UI.V(14);

            // Header
            if (popup)
            {
                var head = new DockPanel();
                var gear = FlatButton.IconButton(Icons.Settings, "Quick controls settings", () => { close?.Invoke(); App.Instance.ShowHub("controls"); }, 34);
                DockPanel.SetDock(gear, System.Windows.Controls.Dock.Right);
                head.Children.Add(gear);
                head.Children.Add(UI.Text("Quick controls", 18, Theme.Text, FontWeights.Bold, true));
                root.Children.Add(head);
            }

            // ---------- Switch tiles ----------
            var tiles = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3 };
            bool? wifi = null, bt = null;
            try
            {
                var tw = SystemToggles.GetRadio(RadioKind.WiFi);
                var tb = SystemToggles.GetRadio(RadioKind.Bluetooth);
                await Task.WhenAll(tw, tb);
                wifi = tw.Result; bt = tb.Result;
            }
            catch { }

            Tile(tiles, Icons.Wifi, "Wi-Fi", wifi, async on => await SystemToggles.SetRadio(RadioKind.WiFi, on), "ms-settings:network-wifi");
            Tile(tiles, Icons.Bluetooth, "Bluetooth", bt, async on => await SystemToggles.SetRadio(RadioKind.Bluetooth, on), "ms-settings:bluetooth");
            Tile(tiles, Icons.BellOff, "Do not disturb", SystemToggles.DoNotDisturb, on => { SystemToggles.DoNotDisturb = on; return Task.FromResult(true); }, "ms-settings:notifications");
            Tile(tiles, Icons.Contrast, "Dark mode", SystemToggles.DarkMode, on => { SystemToggles.DarkMode = on; return Task.FromResult(true); }, "ms-settings:colors");
            Tile(tiles, Icons.Moon, "Night light", SystemToggles.NightLight, on => Task.FromResult(SystemToggles.SetNightLight(on)), "ms-settings:nightlight");
            Tile(tiles, Icons.Mic, "Microphone", Audio.HasMicrophone ? !Audio.GetMicMute() : (bool?)null, on => { Audio.SetMicMute(!on); return Task.FromResult(true); }, "ms-settings:privacy-microphone");
            root.Children.Add(tiles);

            // ---------- Brightness ----------
            if (st.ShowBrightness)
            {
                List<Display> displays = new List<Display>();
                try { displays = await Task.Run(Brightness.Find); } catch { }
                if (displays.Count > 0)
                {
                    var list = UI.V(10);
                    foreach (var d in displays)
                    {
                        var disp = d;
                        var pct = UI.Text(disp.Brightness + "%", 13, Theme.Muted);
                        pct.Width = 40;
                        var slider = new MiniSlider(0, 100, disp.Brightness, Theme.Amber, popup ? 250 : 320) { Step = 1 };
                        var pending = 0;
                        var throttle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
                        throttle.Tick += (s, e) => { throttle.Stop(); int v = pending; _ = Task.Run(() => Brightness.Set(disp, v)); };
                        slider.ValueChanged += v => { pending = (int)v; pct.Text = (int)v + "%"; throttle.Stop(); throttle.Start(); };
                        list.Children.Add(Row(Icons.Sun, displays.Count > 1 ? disp.Name : "Brightness", slider, pct, null));
                    }
                    root.Children.Add(Section("Brightness", list));
                }
            }

            // ---------- Sound ----------
            var sound = UI.V(10);
            {
                var pct = UI.Text((int)Math.Round(Audio.GetVolume() * 100) + "%", 13, Theme.Muted);
                pct.Width = 40;
                var slider = new MiniSlider(0, 100, Math.Round(Audio.GetVolume() * 100), Theme.Amber, popup ? 250 : 320);
                slider.ValueChanged += v => { Audio.SetVolume((float)(v / 100.0)); pct.Text = (int)v + "%"; };
                FlatButton muteBtn = null;
                muteBtn = FlatButton.IconButton(Audio.GetMute() ? Icons.Mute : Icons.Speaker, "Mute or unmute", () =>
                {
                    bool m = !Audio.GetMute();
                    Audio.SetMute(m);
                    muteBtn.Content = UI.GridIcon(m ? Icons.Mute : Icons.Speaker, 18, m ? Theme.Red : Theme.Text);
                }, 34);
                muteBtn.Content = UI.GridIcon(Audio.GetMute() ? Icons.Mute : Icons.Speaker, 18, Audio.GetMute() ? Theme.Red : Theme.Text);
                sound.Children.Add(Row(Icons.Speaker, "Volume", slider, pct, muteBtn));
            }

            var devices = Audio.OutputDevices();
            if (devices.Count > 1)
            {
                var combo = new ComboBox { MinWidth = 200, Height = 34, ItemsSource = devices.Select(d => d.Name).ToList(), SelectedIndex = Math.Max(0, devices.FindIndex(d => d.IsDefault)) };
                System.Windows.Automation.AutomationProperties.SetName(combo, "Output device");
                combo.SelectionChanged += (s, e) =>
                {
                    if (combo.SelectedIndex >= 0) Audio.SetDefaultOutput(devices[combo.SelectedIndex].Id);
                };
                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 0) };
                var lbl = UI.Text("Play sound on", 13, Theme.Muted);
                lbl.VerticalAlignment = VerticalAlignment.Center;
                lbl.Width = 110;
                DockPanel.SetDock(lbl, System.Windows.Controls.Dock.Left);
                row.Children.Add(lbl);
                row.Children.Add(combo);
                sound.Children.Add(row);
            }

            if (st.ShowAppVolumes)
            {
                var sessions = Audio.Sessions();
                foreach (var se in sessions)
                {
                    var sess = se;
                    var pct = UI.Text((int)Math.Round(sess.Volume * 100) + "%", 13, Theme.Muted);
                    pct.Width = 40;
                    var slider = new MiniSlider(0, 100, Math.Round(sess.Volume * 100), Theme.Teal, popup ? 210 : 280);
                    slider.ValueChanged += v => { Audio.SetSessionVolume(sess, (float)(v / 100.0)); pct.Text = (int)v + "%"; };
                    FlatButton m = null;
                    m = FlatButton.IconButton(sess.Muted ? Icons.Mute : Icons.Speaker, "Mute " + sess.Name, () =>
                    {
                        Audio.SetSessionMute(sess, !sess.Muted);
                        m.Content = UI.GridIcon(sess.Muted ? Icons.Mute : Icons.Speaker, 16, sess.Muted ? Theme.Red : Theme.Muted);
                    }, 30);
                    m.Content = UI.GridIcon(sess.Muted ? Icons.Mute : Icons.Speaker, 16, sess.Muted ? Theme.Red : Theme.Muted);
                    FrameworkElement icon;
                    var img = string.IsNullOrEmpty(sess.Path) ? null : IconCache.ForFile(sess.Path);
                    if (img != null) icon = new Image { Source = img, Width = 20, Height = 20 };
                    else icon = UI.GridIcon(Icons.Music, 18, Theme.Muted);
                    sound.Children.Add(Row(icon, sess.Name, slider, pct, m));
                }
                if (sessions.Count == 0) sound.Children.Add(UI.Text("Apps making sound will show up here with their own volume.", 12, Theme.Muted));
            }
            root.Children.Add(Section("Sound", sound));

            // ---------- Power mode ----------
            var modes = new WrapPanel();
            int cur = PowerModes.Current();
            var chips = new List<Chip>();
            for (int i = 0; i < PowerModes.Modes.Length; i++)
            {
                int idx = i;
                var chip = new Chip(PowerModes.Modes[i].name, i == cur) { Margin = new Thickness(0, 0, 8, 0) };
                chip.Click += (s, e) =>
                {
                    if (!PowerModes.Set(idx)) return;
                    for (int j = 0; j < chips.Count; j++) chips[j].Selected = j == idx;
                };
                chips.Add(chip);
                modes.Children.Add(chip);
            }
            root.Children.Add(Section("Power mode", modes));

            return root;
        }

        private static UIElement Section(string title, UIElement body) => UI.V(8, UI.Label(title), body);

        /// <summary>icon (string path data or element) · label · slider · value · optional button</summary>
        private static UIElement Row(object icon, string label, UIElement slider, UIElement value, UIElement button)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var top = UI.H(8);
            if (icon is string data) top.Children.Add(UI.GridIcon(data, 16, Theme.Muted));
            else if (icon is UIElement el) top.Children.Add(el);
            var name = UI.Text(label, 13, Theme.Text, FontWeights.SemiBold);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.MaxWidth = 300;
            top.Children.Add(name);
            var line = UI.H(10, slider, value);
            if (button != null) line.Children.Add(button);
            var v = UI.V(4, top, line);
            g.Children.Add(v);
            return g;
        }

        /// <summary>A square switch. on == null means it isn't available here, and clicking opens the Settings page instead.</summary>
        private static void Tile(Panel parent, string icon, string name, bool? on, Func<bool, Task<bool>> set, string settingsUri)
        {
            bool state = on ?? false;
            var label = UI.Text(name, 13, Theme.Text, FontWeights.SemiBold);
            var sub = UI.Text(on == null ? "Open settings" : state ? "On" : "Off", 11, Theme.Muted);
            var ic = new ContentControl();
            var b = new FlatButton
            {
                Padding = new Thickness(12, 12, 12, 10),
                Margin = new Thickness(0, 0, 8, 8),
                CornerRadius = new CornerRadius(16),
                BorderThickness = new Thickness(1),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = UI.V(6, ic, label, sub)
            };
            void Paint()
            {
                b.Background = state && on != null ? Theme.AmberTint : Theme.Subtle;
                b.BorderBrush = state && on != null ? Theme.Amber : Theme.Line;
                ic.Content = UI.GridIcon(icon, 20, state && on != null ? Theme.Amber : Theme.Muted);
                sub.Text = on == null ? "Open settings" : state ? "On" : "Off";
            }
            Paint();
            FlatButton.SetA11yName(b, name);
            b.Click += async (s, e) =>
            {
                if (on == null) { Shell.Open(settingsUri); return; }
                bool want = !state;
                b.IsEnabled = false;
                bool ok = await set(want);
                b.IsEnabled = true;
                if (ok) { state = want; Paint(); }
                else Shell.Open(settingsUri);
            };
            b.MouseRightButtonUp += (s, e) => { Shell.Open(settingsUri); e.Handled = true; };
            b.ToolTip = "Right-click for Windows settings";
            parent.Children.Add(b);
        }
    }
}

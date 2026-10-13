using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Arrakis.Core;
using Arrakis.Modules;

namespace Arrakis
{
    /// <summary>The main Arrakis window: a sidebar of modules and a page for each.</summary>
    public class HubWindow : Window
    {
        private readonly StackPanel _nav;
        private readonly ContentControl _page;
        private readonly ScrollViewer _scroll;
        private readonly Dictionary<string, FlatButton> _navButtons = new Dictionary<string, FlatButton>();
        private string _current = "home";

        public HubWindow()
        {
            Title = "Arrakis";
            Width = 1180;
            Height = 800;
            MinWidth = 900;
            MinHeight = 600;
            bool glass = Glass.Available;
            Theme.GlassActive = glass;
            Background = glass ? Brushes.Transparent : Theme.Bg;
            Foreground = Theme.Text;
            FontFamily = Theme.Font;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            try { Icon = IconCache.AppIcon(); } catch { }

            _nav = new StackPanel();
            var logo = UI.H(10, new Image { Source = IconCache.AppIcon(), Width = 30, Height = 30 }, UI.Text("Arrakis", 20, Theme.Text, FontWeights.Bold, true));
            logo.Margin = new Thickness(10, 0, 0, 22);
            _nav.Children.Add(logo);
            AddNav("home", "Home", Icons.Home);
            foreach (var m in App.Instance.Modules) AddNav(m.Id, m.Name, m.IconData);

            var settingsBtn = NavButton("settings", "Settings", Icons.Settings);
            var side = new DockPanel { Width = 240, Background = glass ? Theme.GlassSide : Theme.Side };
            DockPanel.SetDock(settingsBtn, System.Windows.Controls.Dock.Bottom);
            settingsBtn.Margin = new Thickness(14, 0, 14, 20);
            side.Children.Add(settingsBtn);
            _nav.Margin = new Thickness(14, 24, 14, 0);
            side.Children.Add(_nav);

            _page = new ContentControl();
            _scroll = new ScrollViewer
            {
                Content = new Border { Padding = new Thickness(40, 36, 40, 40), Child = _page },
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            SmoothScroll.Enable(_scroll);
            var root = new DockPanel { Background = glass ? Theme.GlassTint : null };
            DockPanel.SetDock(side, System.Windows.Controls.Dock.Left);
            root.Children.Add(side);
            root.Children.Add(new Border { Width = 1, Background = Theme.Line });
            DockPanel.SetDock(root.Children[1], System.Windows.Controls.Dock.Left);
            root.Children.Add(_scroll);
            Content = root;

            SourceInitialized += (s, e) =>
            {
                var h = new WindowInteropHelper(this).Handle;
                Native.UseDarkTitleBar(h);
                Native.RoundCorners(h);
                if (glass && !Glass.Apply(this))
                {
                    // Couldn't get the glass effect: fall back to the solid look.
                    Theme.GlassActive = false;
                    Background = Theme.Bg;
                    root.Background = null;
                    side.Background = Theme.Side;
                    Navigate(_current);
                }
            };
            Navigate("home");
        }

        private void AddNav(string id, string name, string icon) => _nav.Children.Add(NavButton(id, name, icon));

        private FlatButton NavButton(string id, string name, string icon)
        {
            var b = new FlatButton
            {
                Content = UI.H(12, UI.GridIcon(icon, 18, Theme.Text), UI.Text(name, 14, Theme.Text, FontWeights.Medium)),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(12, 0, 12, 0),
                MinHeight = 44,
                Background = Brushes.Transparent,
                Margin = new Thickness(0, 0, 0, 4)
            };
            b.Click += (s, e) => Navigate(id);
            _navButtons[id] = b;
            return b;
        }

        public void Navigate(string id)
        {
            _current = id;
            foreach (var kv in _navButtons) kv.Value.Background = kv.Key == id ? Theme.AmberTint : Brushes.Transparent;
            UIElement body;
            string title, subtitle;
            var module = App.Instance.Modules.FirstOrDefault(m => m.Id == id);
            if (id == "home")
            {
                title = Greeting();
                subtitle = "Turn on only the modules you want. Everything runs from one small icon by the clock.";
                body = Home();
            }
            else if (id == "settings")
            {
                title = "Settings";
                subtitle = "Arrakis " + Updates.CurrentText;
                body = SettingsPage();
            }
            else if (module != null)
            {
                title = module.Name;
                subtitle = module.Description;
                body = ModulePage(module);
            }
            else return;

            var header = UI.V(6, UI.Text(title, 30, Theme.Text, FontWeights.SemiBold, true), UI.Text(subtitle, 15, Theme.Muted));
            header.Margin = new Thickness(0, 0, 0, 24);
            var page = new StackPanel { MaxWidth = 1100 };
            page.Children.Add(header);
            page.Children.Add(body);
            _page.Content = page;
            _scroll.ScrollToTop();
        }

        private static string Greeting()
        {
            int h = DateTime.Now.Hour;
            string part = h < 12 ? "Good morning" : h < 18 ? "Good afternoon" : "Good evening";
            return part;
        }

        private UIElement ModulePage(IModule m)
        {
            var toggle = new ToggleSwitch(m.IsEnabled, "Turn " + m.Name + " on or off");
            var state = UI.Text(m.IsEnabled ? "On" : "Off", 14, Theme.Muted);
            toggle.Toggled += on =>
            {
                App.Instance.SetModuleEnabled(m, on);
                state.Text = on ? "On" : "Off";
            };
            var bar = UI.Card(UI.SettingRow(m.Name + " is " + (m.IsEnabled ? "on" : "off"), "Shortcut: " + m.ShortcutText, UI.H(10, state, toggle)), 8);
            toggle.Toggled += on =>
            {
                if (bar.Child is Grid g && g.Children[0] is StackPanel sp && sp.Children[0] is TextBlock tb) tb.Text = m.Name + " is " + (on ? "on" : "off");
            };
            UIElement page;
            try { page = m.BuildPage(); }
            catch (Exception ex)
            {
                Log.Write("Page failed for " + m.Name + ": " + ex);
                page = UI.Text("Something went wrong showing this page. Details are in the log file.", 14, Theme.Muted);
            }
            return UI.V(16, bar, page);
        }

        private UIElement Home()
        {
            var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4 };
            foreach (var m in App.Instance.Modules) grid.Children.Add(ModuleCard(m));
            grid.Children.Add(ArrakisCard());
            return grid;
        }

        private UIElement ModuleCard(IModule m)
        {
            bool amber = Array.IndexOf(App.Instance.Modules.Select(x => x.Id).ToArray(), m.Id) % 2 == 0;
            var iconTile = new Border
            {
                Width = 44,
                Height = 44,
                CornerRadius = new CornerRadius(13),
                Background = amber ? Theme.AmberTint : Theme.TealTint,
                Child = UI.GridIcon(m.IconData, 22, amber ? Theme.Amber : Theme.Teal)
            };
            var toggle = new ToggleSwitch(m.IsEnabled, "Turn " + m.Name + " on or off");
            var top = new DockPanel();
            DockPanel.SetDock(toggle, System.Windows.Controls.Dock.Right);
            top.Children.Add(toggle);
            top.Children.Add(iconTile);
            var name = UI.Text(m.Name, 17, Theme.Text, FontWeights.Bold);
            var desc = UI.Text(m.Description, 13, Theme.Muted);
            desc.LineHeight = 19;
            var key = UI.Kbd(m.ShortcutText);
            key.HorizontalAlignment = HorizontalAlignment.Left;
            var body = new DockPanel();
            DockPanel.SetDock(top, System.Windows.Controls.Dock.Top);
            DockPanel.SetDock(key, System.Windows.Controls.Dock.Bottom);
            body.Children.Add(top);
            body.Children.Add(key);
            var text = UI.V(4, name, desc);
            text.Margin = new Thickness(0, 14, 0, 14);
            body.Children.Add(text);

            var card = new Border
            {
                Background = Theme.Panel,
                BorderBrush = Theme.Line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(20),
                Margin = new Thickness(0, 0, 16, 16),
                MinHeight = 200,
                Child = body,
                Cursor = Cursors.Hand,
                Opacity = m.IsEnabled ? 1 : 0.62
            };
            toggle.Toggled += on => { App.Instance.SetModuleEnabled(m, on); card.Opacity = on ? 1 : 0.62; };
            card.MouseLeftButtonUp += (s, e) =>
            {
                if (e.OriginalSource is DependencyObject d && IsIn(d, toggle)) return;
                Navigate(m.Id);
            };
            return card;
        }

        private static bool IsIn(DependencyObject d, DependencyObject target)
        {
            while (d != null)
            {
                if (d == target) return true;
                d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return false;
        }

        private UIElement ArrakisCard()
        {
            var mem = UI.Text("", 22, Theme.Text, FontWeights.SemiBold, true);
            var ver = UI.Text("Version " + Updates.CurrentText, 13, Theme.Text);
            var check = FlatButton.Secondary("Check for updates");
            check.BorderBrush = Theme.Brush(Color.FromArgb(0x66, 0xFF, 0xB5, 0x47));
            check.Foreground = Theme.Amber;
            bool ready = false;
            check.Click += async (s, e) =>
            {
                check.IsEnabled = false;
                if (!ready)
                {
                    check.Content = "Checking...";
                    ready = await Updates.Check();
                    check.Content = ready ? "Install " + Updates.LatestVersion : Updates.LatestVersion == null ? "Couldn't check" : "Up to date";
                    check.IsEnabled = true;
                    return;
                }
                bool ok = await App.Instance.InstallUpdate(t => check.Content = t);
                if (!ok) { check.Content = "Try again"; check.IsEnabled = true; }
            };
            void Refresh()
            {
                using var p = Process.GetCurrentProcess();
                mem.Text = (p.WorkingSet64 / (1024 * 1024)) + " MB";
            }
            Refresh();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (s, e) => Refresh();
            var body = new DockPanel();
            DockPanel.SetDock(check, System.Windows.Controls.Dock.Bottom);
            body.Children.Add(check);
            body.Children.Add(UI.V(8, UI.Label("Arrakis itself").WithColor(Theme.Amber), UI.V(0, mem, UI.Text("memory in use", 12, Theme.Muted)), ver));
            var card = new Border
            {
                Background = Theme.Warm,
                BorderBrush = Theme.Brush(Color.FromArgb(0x40, 0xFF, 0xB5, 0x47)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(20),
                Margin = new Thickness(0, 0, 16, 16),
                MinHeight = 200,
                Child = body
            };
            card.Loaded += (s, e) => timer.Start();
            card.Unloaded += (s, e) => timer.Stop();
            return card;
        }

        private UIElement SettingsPage()
        {
            var st = Settings.Current;
            var startup = new ToggleSwitch(st.StartWithWindows, "Start with Windows");
            startup.Toggled += on => { st.StartWithWindows = on; Settings.Save(); AutoStart.Apply(on); };

            // Appearance
            var restartNote = UI.Text("", 12, Theme.Amber, FontWeights.SemiBold);
            var restartBtn = FlatButton.Primary("Restart Arrakis now", () => App.Instance.Restart());
            restartBtn.Visibility = Visibility.Collapsed;
            restartBtn.HorizontalAlignment = HorizontalAlignment.Left;
            void NeedsRestart() { restartNote.Text = "Restart Arrakis to see the change."; restartBtn.Visibility = Visibility.Visible; }
            string ThemeLabel(string v) => v == "Light" ? "Light" : v == "System" ? "Match Windows" : "Dark";
            var theme = UI.Combo(new[] { "Dark", "Light", "Match Windows" }, ThemeLabel(st.Theme), v =>
            {
                st.Theme = v == "Match Windows" ? "System" : v;
                Settings.Save();
                NeedsRestart();
            });
            var glassToggle = new ToggleSwitch(st.GlassWindow, "Glass window");
            glassToggle.IsEnabled = Environment.OSVersion.Version.Build >= 22621;
            glassToggle.Toggled += on =>
            {
                st.GlassWindow = on;
                Settings.Save();
                // Reopen the window so the change shows straight away.
                Dispatcher.BeginInvoke(new Action(() => { var page = _current; Close(); App.Instance.ShowHub(page); }));
            };
            var accents = new WrapPanel();
            var accentButtons = new List<FlatButton>();
            foreach (var name in Theme.AccentNames)
            {
                var b = new FlatButton
                {
                    Width = 32, Height = 32, MinHeight = 32, Padding = new Thickness(0), CornerRadius = new CornerRadius(16),
                    Background = Theme.Brush(Theme.AccentFor(name)), BorderThickness = new Thickness(3),
                    BorderBrush = st.Accent == name ? Theme.Text : Brushes.Transparent, Margin = new Thickness(0, 0, 8, 0), ToolTip = name
                };
                FlatButton.SetA11yName(b, name + " accent");
                string n = name;
                b.Click += (s, e) =>
                {
                    st.Accent = n;
                    Settings.Save();
                    foreach (var x in accentButtons) x.BorderBrush = (string)x.ToolTip == n ? Theme.Text : Brushes.Transparent;
                    NeedsRestart();
                };
                accentButtons.Add(b);
                accents.Children.Add(b);
            }

            // Command bar
            var cmdOn = new ToggleSwitch(st.CommandBar, "Command bar");
            cmdOn.Toggled += on => { st.CommandBar = on; Settings.Save(); App.Instance.ApplyCommandBar(); };
            var cmdKey = UI.TextInput(st.CommandBarHotkey, 150);
            var cmdStatus = UI.Text("", 12, Theme.Muted);
            var cmdApply = FlatButton.Secondary("Apply", () =>
            {
                string v = cmdKey.Text.Trim();
                if (!Hotkeys.TryParse(v, out _, out _)) { cmdStatus.Text = "Try something like Alt+Space."; return; }
                st.CommandBarHotkey = v;
                Settings.Save();
                App.Instance.ApplyCommandBar();
                cmdStatus.Text = "Saved.";
            });
            cmdApply.MinHeight = 36;
            var tryIt = FlatButton.Secondary("Try it", () => CommandBar.Toggle());
            tryIt.MinHeight = 36;

            // Updates
            var updates = new ToggleSwitch(st.CheckForUpdates, "Check for updates");
            updates.Toggled += on => { st.CheckForUpdates = on; Settings.Save(); };
            var auto = new ToggleSwitch(st.AutoUpdate, "Install updates automatically");
            auto.Toggled += on => { st.AutoUpdate = on; Settings.Save(); };
            var updStatus = UI.Text("You're on " + Updates.CurrentText + ".", 12, Theme.Muted);
            var updBtn = FlatButton.Secondary("Check now");
            updBtn.MinHeight = 36;
            bool updReady = false;
            updBtn.Click += async (s, e) =>
            {
                updBtn.IsEnabled = false;
                if (!updReady)
                {
                    updStatus.Text = "Checking...";
                    updReady = await Updates.Check();
                    if (updReady)
                    {
                        updStatus.Text = "Arrakis " + Updates.LatestVersion + " is available. Click to download and install it. Arrakis reopens by itself.";
                        updBtn.Content = "Install " + Updates.LatestVersion;
                    }
                    else updStatus.Text = Updates.LatestVersion == null ? "Couldn't reach GitHub." : "You're up to date (" + Updates.CurrentText + ").";
                    updBtn.IsEnabled = true;
                    return;
                }
                bool ok = await App.Instance.InstallUpdate(t => updStatus.Text = t);
                if (!ok) { updBtn.Content = "Try again"; updBtn.IsEnabled = true; }
            };

            // Backup
            var exportBtn = FlatButton.Secondary("Export...", () =>
            {
                var dlg = new Microsoft.Win32.SaveFileDialog { FileName = "Arrakis backup " + DateTime.Now.ToString("yyyy-MM-dd") + ".zip", Filter = "Arrakis backup (*.zip)|*.zip" };
                if (dlg.ShowDialog(this) != true) return;
                try { Backup.Export(dlg.FileName); MessageBox.Show(this, "Saved your settings to\n" + dlg.FileName, "Arrakis"); }
                catch (Exception ex) { MessageBox.Show(this, "Couldn't export: " + ex.Message, "Arrakis"); }
            });
            var importBtn = FlatButton.Secondary("Import...", () =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Arrakis backup (*.zip)|*.zip" };
                if (dlg.ShowDialog(this) != true) return;
                if (MessageBox.Show(this, "This replaces your current Arrakis settings, clipboard history and stats with the backup, then restarts Arrakis.", "Import backup", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
                try
                {
                    foreach (var m in App.Instance.Modules) if (m.IsRunning) m.Stop();
                    Backup.Import(dlg.FileName);
                    App.Instance.Restart();
                }
                catch (Exception ex) { MessageBox.Show(this, "Couldn't import: " + ex.Message, "Arrakis"); }
            });

            var folder = FlatButton.Secondary("Open settings folder", () => Shell.Open(Settings.Folder));
            var site = FlatButton.Secondary("Website and user guide", () => Shell.Open("https://bhxuk.github.io/arrakis/"));
            var issues = FlatButton.Secondary("Report a problem", () => Shell.Open("https://github.com/bhxuk/arrakis/issues"));
            var taskbar = FlatButton.Secondary("Show the Windows taskbar now", () => App.Instance.Dock.PutTaskbarBack());
            var quit = FlatButton.Secondary("Exit Arrakis", () => App.Instance.Quit());

            return UI.V(16,
                UI.Card(UI.V(0,
                    UI.SettingRow("Start with Windows", "Arrakis starts quietly in the background when you sign in.", startup)), 8),
                UI.Label("Appearance"),
                UI.Card(UI.V(0,
                    UI.SettingRow("Theme", "Dark, light, or follow Windows.", theme),
                    UI.Divider(),
                    UI.SettingRow("Accent colour", "Used for buttons, switches and highlights.", accents),
                    UI.Divider(),
                    UI.SettingRow("Glass window", Environment.OSVersion.Version.Build >= 22621 ? "Your wallpaper shows softly through this window, like frosted glass." : "Needs Windows 11 (version 22H2 or later).", glassToggle),
                    UI.V(8, restartNote, restartBtn)), 8),
                UI.Label("Command bar"),
                UI.Card(UI.V(0,
                    UI.SettingRow("Command bar", "Search apps, files, Windows settings, your clipboard and Arrakis actions from one box. Type a sum to work it out.", cmdOn),
                    UI.Divider(),
                    UI.SettingRow("Shortcut", null, UI.V(4, UI.H(8, cmdKey, cmdApply, tryIt), cmdStatus))), 8),
                UI.Label("Updates"),
                UI.Card(UI.V(0,
                    UI.SettingRow("Check for updates", "Looks for a newer version on GitHub when Arrakis starts. Nothing else is sent.", updates),
                    UI.Divider(),
                    UI.SettingRow("Install updates automatically", "Downloads and installs new versions quietly, then reopens Arrakis. Never during a focus session.", auto),
                    UI.Divider(),
                    UI.SettingRow("Update now", null, UI.V(4, updBtn, updStatus))), 8),
                UI.Label("Backup"),
                UI.Card(UI.V(0,
                    UI.SettingRow("Move to a new PC", "Export everything (settings, widgets, clipboard, snippets, notes, speed tests, focus and battery history) into one file, then import it on the other PC.", UI.H(8, exportBtn, importBtn))), 8),
                UI.Label("Help"),
                UI.Card(UI.V(0,
                    UI.SettingRow("Taskbar gone and won't come back?", "This brings it straight back. You can also run Start > Arrakis > Restore Windows taskbar.", taskbar),
                    UI.Divider(),
                    UI.SettingRow("Your data", "Settings, clipboard history and stats are stored only on this PC, in your AppData folder.", folder),
                    UI.Divider(),
                    UI.SettingRow("Guide", null, site),
                    UI.Divider(),
                    UI.SettingRow("Something not working?", "Open an issue on GitHub. The log file in the settings folder helps.", issues),
                    UI.Divider(),
                    UI.SettingRow("Exit", "Closes Arrakis completely and gives the taskbar back.", quit)), 8),
                UI.Text("Arrakis " + Updates.CurrentText + " · Made by bhxuk · MIT licence", 12, Theme.Muted));
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Closing the window just hides it; Arrakis keeps running in the tray.
            e.Cancel = true;
            Hide();
            base.OnClosing(e);
        }
    }

    public static class TextExtensions
    {
        public static TextBlock WithColor(this TextBlock t, Brush b)
        {
            t.Foreground = b;
            return t;
        }
    }
}

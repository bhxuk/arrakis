using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Arrakis.Modules;

namespace Arrakis.Core
{
    public class CommandItem
    {
        public string Title = "";
        public string Subtitle = "";
        public string Kind = "";          // App, File, Folder, Action, Setting, Clipboard, Maths, Web
        public string Icon = Icons.Search;
        public ImageSource Image;
        public Action Run;
        public Action RunAlt;             // Ctrl+Enter
        public string Path;
        public double Score;
    }

    /// <summary>Indexes apps, files and settings for the command bar.</summary>
    public static class CommandIndex
    {
        private static List<CommandItem> _apps = new List<CommandItem>();
        private static List<CommandItem> _files = new List<CommandItem>();
        private static DateTime _built = DateTime.MinValue;
        private static bool _building;

        public static IReadOnlyList<CommandItem> Apps => _apps;
        public static IReadOnlyList<CommandItem> Files => _files;

        public static void RefreshIfStale()
        {
            if (_building || (DateTime.Now - _built).TotalMinutes < 10) return;
            _building = true;
            Task.Run(() =>
            {
                try
                {
                    var apps = BuildApps();
                    var files = BuildFiles();
                    Application.Current.Dispatcher.Invoke(() => { _apps = apps; _files = files; _built = DateTime.Now; });
                }
                catch (Exception ex) { Log.Write("Command index failed: " + ex.Message); }
                finally { _building = false; }
            });
        }

        private static List<CommandItem> BuildApps()
        {
            var list = new List<CommandItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Every app Windows knows about, including Store apps, via the Apps folder.
            try
            {
                var shellType = Type.GetTypeFromProgID("Shell.Application");
                dynamic shell = Activator.CreateInstance(shellType);
                dynamic folder = shell.NameSpace("shell:AppsFolder");
                foreach (dynamic item in folder.Items())
                {
                    string name = item.Name;
                    string id = item.Path;
                    if (string.IsNullOrWhiteSpace(name) || !seen.Add(name)) continue;
                    if (name.IndexOf("uninstall", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    string target = id;
                    list.Add(new CommandItem
                    {
                        Title = name,
                        Subtitle = "App",
                        Kind = "App",
                        Icon = Icons.Dock,
                        Path = id,
                        Run = () => Shell.Open("explorer.exe", "shell:AppsFolder\\" + target)
                    });
                }
            }
            catch (Exception ex) { Log.Write("Apps folder failed: " + ex.Message); }
            // Start menu shortcuts as a fallback (and for icons).
            foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), Environment.GetFolderPath(Environment.SpecialFolder.StartMenu) })
            {
                try
                {
                    foreach (var f in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
                    {
                        string name = System.IO.Path.GetFileNameWithoutExtension(f);
                        if (name.IndexOf("uninstall", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        var existing = list.FirstOrDefault(i => i.Title.Equals(name, StringComparison.OrdinalIgnoreCase));
                        if (existing != null) { existing.Path = f; continue; }
                        if (!seen.Add(name)) continue;
                        string path = f;
                        list.Add(new CommandItem { Title = name, Subtitle = "App", Kind = "App", Icon = Icons.Dock, Path = f, Run = () => Shell.Open(path) });
                    }
                }
                catch { }
            }
            return list;
        }

        private static List<CommandItem> BuildFiles()
        {
            var list = new List<CommandItem>();
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var roots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                System.IO.Path.Combine(home, "Downloads"),
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
            };
            foreach (var root in roots.Where(Directory.Exists))
            {
                try { Walk(root, 0, list); } catch { }
                if (list.Count > 6000) break;
            }
            // Recently opened files.
            try
            {
                string recent = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
                foreach (var f in new DirectoryInfo(recent).GetFiles("*.lnk").OrderByDescending(f => f.LastWriteTime).Take(150))
                {
                    string name = System.IO.Path.GetFileNameWithoutExtension(f.Name);
                    string path = f.FullName;
                    list.Add(new CommandItem { Title = name, Subtitle = "Recent", Kind = "File", Icon = Icons.Clock, Path = path, Run = () => Shell.Open(path), Score = 0.2 });
                }
            }
            catch { }
            return list;
        }

        private static void Walk(string dir, int depth, List<CommandItem> list)
        {
            if (depth > 2 || list.Count > 6000) return;
            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                var name = System.IO.Path.GetFileName(sub);
                if (name.StartsWith(".") || name.Equals("node_modules", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    if ((File.GetAttributes(sub) & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint)) != 0) continue;
                }
                catch { continue; }
                string path = sub;
                list.Add(new CommandItem { Title = name, Subtitle = Shorten(sub), Kind = "Folder", Icon = Icons.Folder, Path = sub, Run = () => Shell.Open(path) });
                try { Walk(sub, depth + 1, list); } catch { }
            }
            foreach (var f in Directory.EnumerateFiles(dir))
            {
                var name = System.IO.Path.GetFileName(f);
                if (name.StartsWith(".") || name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                string path = f;
                list.Add(new CommandItem
                {
                    Title = name,
                    Subtitle = Shorten(System.IO.Path.GetDirectoryName(f)),
                    Kind = "File",
                    Icon = Icons.Note,
                    Path = f,
                    Run = () => Shell.Open(path),
                    RunAlt = () => Process.Start("explorer.exe", "/select,\"" + path + "\"")
                });
            }
        }

        private static string Shorten(string path)
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return path.StartsWith(home, StringComparison.OrdinalIgnoreCase) ? "~" + path.Substring(home.Length) : path;
        }

        public static readonly (string name, string uri)[] WindowsSettings =
        {
            ("Display", "ms-settings:display"), ("Sound", "ms-settings:sound"), ("Bluetooth", "ms-settings:bluetooth"),
            ("Wi-Fi", "ms-settings:network-wifi"), ("Network", "ms-settings:network"), ("Apps", "ms-settings:appsfeatures"),
            ("Default apps", "ms-settings:defaultapps"), ("Startup apps", "ms-settings:startupapps"), ("Power and battery", "ms-settings:powersleep"),
            ("Windows Update", "ms-settings:windowsupdate"), ("Personalisation", "ms-settings:personalization"), ("Background", "ms-settings:personalization-background"),
            ("Colours", "ms-settings:personalization-colors"), ("Mouse", "ms-settings:mousetouchpad"), ("Keyboard", "ms-settings:keyboard"),
            ("Notifications", "ms-settings:notifications"), ("Storage", "ms-settings:storagesense"), ("Printers", "ms-settings:printers"),
            ("Date and time", "ms-settings:dateandtime"), ("Language", "ms-settings:regionlanguage"), ("Accounts", "ms-settings:yourinfo"),
            ("Privacy", "ms-settings:privacy"), ("About this PC", "ms-settings:about"), ("Night light", "ms-settings:nightlight"),
            ("Task Manager", "taskmgr.exe"), ("Control Panel", "control.exe"), ("File Explorer", "explorer.exe")
        };
    }

    /// <summary>Search apps, files, settings, the clipboard and sums from one box.</summary>
    public class CommandBar : Window
    {
        private static CommandBar _open;
        private readonly TextBox _box;
        private readonly StackPanel _list;
        private readonly TextBlock _empty;
        private List<CommandItem> _results = new List<CommandItem>();
        private int _sel;
        private bool _closing;

        public static void Toggle()
        {
            if (_open != null) { _open.SafeClose(); return; }
            CommandIndex.RefreshIfStale();
            _open = new CommandBar();
            _open.Closed += (s, e) => _open = null;
            _open.Show();
            _open.Activate();
        }

        private CommandBar()
        {
            Title = "Arrakis";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            Width = 720;
            SizeToContent = SizeToContent.Height;
            FontFamily = Theme.Font;

            _box = new TextBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Theme.Text,
                CaretBrush = Theme.Amber,
                FontSize = 22,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            System.Windows.Automation.AutomationProperties.SetName(_box, "Search");
            var hint = UI.Text("Search apps, files, settings, clipboard, or type a sum", 22, Theme.Muted);
            hint.IsHitTestVisible = false;
            hint.VerticalAlignment = VerticalAlignment.Center;
            var boxGrid = new Grid();
            boxGrid.Children.Add(hint);
            boxGrid.Children.Add(_box);
            _box.TextChanged += (s, e) => { hint.Visibility = _box.Text.Length > 0 ? Visibility.Collapsed : Visibility.Visible; _sel = 0; Search(); };
            var searchRow = new DockPanel { Height = 56 };
            var icon = UI.GridIcon(Icons.Search, 22, Theme.Muted);
            icon.Margin = new Thickness(4, 0, 14, 0);
            DockPanel.SetDock(icon, System.Windows.Controls.Dock.Left);
            searchRow.Children.Add(icon);
            searchRow.Children.Add(boxGrid);

            _list = new StackPanel();
            _empty = UI.Text("", 13, Theme.Muted);
            _empty.Margin = new Thickness(12, 8, 12, 8);
            var scroll = new ScrollViewer { Content = _list, MaxHeight = 460, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };

            var footer = new WrapPanel { Margin = new Thickness(6, 4, 6, 0) };
            foreach (var (k, t) in new[] { ("Enter", "open"), ("Ctrl + Enter", "show in folder"), ("Up / Down", "move"), ("Esc", "close") })
                footer.Children.Add(UI.H(6, UI.Kbd(k), UI.Text(t, 12, Theme.Muted)).WithMargin(new Thickness(0, 0, 16, 0)));

            Content = new Border
            {
                Margin = new Thickness(24),
                Background = Theme.Popup,
                BorderBrush = Theme.GlassBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(24),
                Padding = new Thickness(18, 10, 18, 14),
                Effect = Theme.Shadow(50, 0.5, 14),
                Child = UI.V(8, searchRow, UI.Divider(), scroll, _empty, footer)
            };

            SourceInitialized += (s, e) => WindowHelper.MakeToolWindow(this, false);
            Loaded += (s, e) =>
            {
                var wa = SystemParameters.WorkArea;
                Left = wa.Left + (wa.Width - ActualWidth) / 2;
                Top = wa.Top + wa.Height * 0.18;
                _box.Focus();
                Search();
            };
            Deactivated += (s, e) => SafeClose();
            PreviewKeyDown += OnKey;
        }

        private void SafeClose()
        {
            if (_closing) return;
            _closing = true;
            Close();
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            switch (e.Key)
            {
                case Key.Escape: SafeClose(); e.Handled = true; break;
                case Key.Down: Move(1); e.Handled = true; break;
                case Key.Up: Move(-1); e.Handled = true; break;
                case Key.Tab: Move((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? -1 : 1); e.Handled = true; break;
                case Key.Enter:
                    if (_results.Count > 0) Launch(_results[_sel], ctrl);
                    e.Handled = true;
                    break;
            }
        }

        private void Move(int d)
        {
            if (_results.Count == 0) return;
            _sel = Math.Max(0, Math.Min(_results.Count - 1, _sel + d));
            Render();
            if (_list.Children[_sel] is FrameworkElement fe) fe.BringIntoView();
        }

        private void Launch(CommandItem item, bool alt)
        {
            _closing = true;
            Hide();
            try
            {
                if (alt && item.RunAlt != null) item.RunAlt();
                else if (alt && !string.IsNullOrEmpty(item.Path) && (File.Exists(item.Path) || Directory.Exists(item.Path)))
                    Process.Start("explorer.exe", "/select,\"" + item.Path + "\"");
                else item.Run?.Invoke();
            }
            catch (Exception ex) { Log.Write("Command failed: " + ex.Message); }
            Close();
        }

        private static readonly Regex MathsRx = new Regex(@"^[\d\s\.\,\+\-\*\/\(\)%x×÷\^]+$", RegexOptions.Compiled);

        private void Search()
        {
            string q = _box.Text.Trim();
            var results = new List<CommandItem>();

            // Sums
            if (q.Length > 0 && MathsRx.IsMatch(q) && q.Any(char.IsDigit) && q.Any(c => "+-*/x×÷%^(".Contains(c)))
            {
                try
                {
                    string expr = q.Replace("x", "*").Replace("×", "*").Replace("÷", "/").Replace(",", "");
                    expr = Regex.Replace(expr, @"(\d+(\.\d+)?)\s*%", "($1/100)");
                    if (expr.Contains('^')) throw new InvalidOperationException();
                    var value = new DataTable().Compute(expr, null);
                    double d = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    string text = Math.Round(d, 10).ToString("#,0.##########", CultureInfo.CurrentCulture);
                    string raw = Math.Round(d, 10).ToString(CultureInfo.InvariantCulture);
                    results.Add(new CommandItem { Title = "= " + text, Subtitle = "Press Enter to copy", Kind = "Maths", Icon = Icons.Plus, Run = () => System.Windows.Clipboard.SetText(raw), Score = 100 });
                }
                catch { }
            }

            if (q.Length == 0)
            {
                // Suggestions when the box is empty.
                results.AddRange(Actions().Take(6));
                _results = results;
                _empty.Text = "Type to search. Apps, files and folders in your Desktop, Documents, Downloads, Pictures, Music and Videos are included.";
                Render();
                return;
            }

            void Add(IEnumerable<CommandItem> source, double weight, int max)
            {
                var scored = new List<CommandItem>();
                foreach (var item in source)
                {
                    double s = Score(item.Title, q);
                    if (s <= 0) continue;
                    scored.Add(new CommandItem
                    {
                        Title = item.Title, Subtitle = item.Subtitle, Kind = item.Kind, Icon = item.Icon, Image = item.Image,
                        Run = item.Run, RunAlt = item.RunAlt, Path = item.Path, Score = s * weight + item.Score
                    });
                }
                results.AddRange(scored.OrderByDescending(i => i.Score).Take(max));
            }

            Add(CommandIndex.Apps, 3.0, 6);
            Add(Actions(), 2.6, 5);
            Add(CommandIndex.WindowsSettings.Select(w =>
            {
                string uri = w.uri;
                return new CommandItem { Title = w.name, Subtitle = uri.StartsWith("ms-settings") ? "Windows settings" : "Windows tool", Kind = "Setting", Icon = Icons.Settings, Run = () => Shell.Open(uri) };
            }), 2.2, 4);
            Add(CommandIndex.Files, 1.6, 8);
            if (App.Instance.Clipboard.IsRunning)
            {
                var clips = App.Instance.Clipboard.Items.Where(i => i.Kind != "Image" && (i.Text ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).Take(3);
                foreach (var c in clips)
                {
                    var item = c;
                    string first = (c.Text ?? "").Trim().Replace("\r", "").Split('\n')[0];
                    if (first.Length > 80) first = first.Substring(0, 80) + "...";
                    results.Add(new CommandItem { Title = first, Subtitle = "Clipboard · press Enter to copy", Kind = "Clipboard", Icon = Icons.Clipboard, Run = () => App.Instance.Clipboard.SetClipboard(item, false), Score = 1.5 });
                }
            }
            if (App.Instance.Notes.IsRunning)
            {
                foreach (var n in NoteStore.Search(q).Where(n => !string.IsNullOrWhiteSpace(n.Text)).Take(3))
                {
                    var note = n;
                    results.Add(new CommandItem { Title = note.Title, Subtitle = "Quick note · " + note.Updated.ToString("d MMM"), Kind = "Note", Icon = Icons.NotePad, Run = () => App.Instance.Notes.Open(note.Id), Score = 1.8 });
                }
            }
            string web = q;
            results.Add(new CommandItem { Title = "Search the web for \"" + q + "\"", Subtitle = "Opens in your browser", Kind = "Web", Icon = Icons.Link, Run = () => Shell.Open("https://www.google.com/search?q=" + Uri.EscapeDataString(web)), Score = 0.1 });

            _results = results.OrderByDescending(r => r.Score).ToList();
            _empty.Text = CommandIndex.Apps.Count == 0 ? "Still finding your apps and files..." : "";
            Render();
        }

        /// <summary>0 when there's no match. Higher for matches at the start of the name or of a word.</summary>
        private static double Score(string title, string q)
        {
            if (string.IsNullOrEmpty(title)) return 0;
            string t = title.ToLowerInvariant(), s = q.ToLowerInvariant();
            if (t == s) return 10;
            if (t.StartsWith(s)) return 8 - Math.Min(3, (t.Length - s.Length) / 20.0);
            int idx = t.IndexOf(s, StringComparison.Ordinal);
            if (idx > 0 && !char.IsLetterOrDigit(t[idx - 1])) return 6;
            if (idx > 0) return 4;
            // Initials, e.g. "vsc" for Visual Studio Code.
            var initials = new string(Regex.Split(t, @"[^a-z0-9]+").Where(w => w.Length > 0).Select(w => w[0]).ToArray());
            if (s.Length >= 2 && initials.StartsWith(s)) return 5;
            return 0;
        }

        private static IEnumerable<CommandItem> Actions()
        {
            var app = App.Instance;
            yield return new CommandItem { Title = "Take a screenshot", Subtitle = "Arrakis · Capture", Kind = "Action", Icon = Icons.Capture, Run = () => DelayRun(() => app.Capture.StartCapture()) };
            yield return new CommandItem { Title = "Screenshot a window", Subtitle = "Arrakis · Capture", Kind = "Action", Icon = Icons.Capture, Run = () => DelayRun(() => app.Capture.Capture(Arrakis.Modules.CaptureMode.Window)) };
            yield return new CommandItem { Title = "Open the clipboard", Subtitle = "Arrakis · Clipboard", Kind = "Action", Icon = Icons.Clipboard, Run = () => DelayRun(() => app.Clipboard.ShowPanel()) };
            yield return new CommandItem { Title = app.Focus.IsActive ? "Pause or resume focus" : "Start focus", Subtitle = "Arrakis · Focus", Kind = "Action", Icon = Icons.Focus, Run = () => app.Focus.ToggleFromHotkey() };
            if (app.Focus.IsRunning && app.Focus.Current == Arrakis.Modules.FocusModule.Phase.Idle)
                foreach (int mins in new[] { 25, 50, 90 })
                {
                    int m = mins;
                    yield return new CommandItem { Title = "Focus for " + m + " minutes", Subtitle = "Arrakis · Focus", Kind = "Action", Icon = Icons.Focus, Run = () => app.Focus.StartFocus(null, m) };
                }
            for (int i = 0; i < Arrakis.Modules.PowerModes.Modes.Length; i++)
            {
                int index = i;
                var mode = Arrakis.Modules.PowerModes.Modes[i];
                yield return new CommandItem { Title = "Power mode: " + mode.name, Subtitle = "Windows · " + mode.desc, Kind = "Action", Icon = mode.icon, Run = () => Arrakis.Modules.PowerModes.Set(index) };
            }
            if (app.Widgets.IsRunning)
                foreach (var name in Settings.Current.Widgets.Layouts.Keys.ToList())
                {
                    string n = name;
                    yield return new CommandItem { Title = "Widget layout: " + n, Subtitle = "Arrakis · Widgets", Kind = "Action", Icon = Icons.Widgets, Run = () => app.Widgets.LoadLayout(n) };
                }
            yield return new CommandItem { Title = "Quick controls", Subtitle = "Arrakis · Volume, brightness, Wi-Fi, Bluetooth", Kind = "Action", Icon = Icons.Sliders, Run = () => DelayRun(() => app.Controls.Toggle()) };
            yield return new CommandItem { Title = "New note", Subtitle = "Arrakis · Quick notes", Kind = "Action", Icon = Icons.NotePad, Run = () => app.Notes.Open(null, true) };
            yield return new CommandItem { Title = "Run a speed test", Subtitle = "Arrakis · Network", Kind = "Action", Icon = Icons.Gauge, Run = () => app.Network.RunSpeedTestFromAnywhere() };
            yield return new CommandItem { Title = SystemToggles.DarkMode ? "Turn dark mode off" : "Turn dark mode on", Subtitle = "Windows", Kind = "Action", Icon = Icons.Contrast, Run = () => SystemToggles.DarkMode = !SystemToggles.DarkMode };
            yield return new CommandItem { Title = SystemToggles.DoNotDisturb ? "Turn do not disturb off" : "Turn do not disturb on", Subtitle = "Windows · notifications", Kind = "Action", Icon = Icons.BellOff, Run = () => SystemToggles.DoNotDisturb = !SystemToggles.DoNotDisturb };
            yield return new CommandItem { Title = Audio.GetMute() ? "Unmute sound" : "Mute sound", Subtitle = "Windows · volume", Kind = "Action", Icon = Icons.Mute, Run = () => Audio.SetMute(!Audio.GetMute()) };
            yield return new CommandItem { Title = "Edit widgets", Subtitle = "Arrakis · Widgets", Kind = "Action", Icon = Icons.Widgets, Run = () => app.Widgets.ToggleEditMode() };
            yield return new CommandItem { Title = "Open Arrakis", Subtitle = "Arrakis", Kind = "Action", Icon = Icons.Home, Run = () => app.ShowHub() };
            foreach (var m in app.Modules)
            {
                var mod = m;
                yield return new CommandItem { Title = mod.Name + " settings", Subtitle = "Arrakis", Kind = "Action", Icon = mod.IconData, Run = () => app.ShowHub(mod.Id) };
            }
            yield return new CommandItem { Title = "Arrakis settings", Subtitle = "Arrakis", Kind = "Action", Icon = Icons.Settings, Run = () => app.ShowHub("settings") };
        }

        private static void DelayRun(Action a)
        {
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            t.Tick += (s, e) => { t.Stop(); a(); };
            t.Start();
        }

        private void Render()
        {
            _list.Children.Clear();
            for (int i = 0; i < _results.Count; i++) _list.Children.Add(Row(_results[i], i == _sel, i));
            _empty.Visibility = string.IsNullOrEmpty(_empty.Text) ? Visibility.Collapsed : Visibility.Visible;
        }

        private UIElement Row(CommandItem item, bool selected, int index)
        {
            FrameworkElement icon;
            ImageSource img = item.Image;
            if (img == null && item.Kind == "App" && !string.IsNullOrEmpty(item.Path) && File.Exists(item.Path)) img = IconCache.ForFile(item.Path);
            if (img != null) icon = new Image { Source = img, Width = 28, Height = 28 };
            else icon = UI.GridIcon(item.Icon, 18, selected ? Theme.Amber : Theme.Muted);
            var iconBox = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(11), Background = Theme.Subtle, Child = icon };
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            icon.VerticalAlignment = VerticalAlignment.Center;
            var title = UI.Text(item.Title, 15, Theme.Text, FontWeights.SemiBold);
            title.TextWrapping = TextWrapping.NoWrap;
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            var sub = UI.Text(item.Subtitle, 12, Theme.Muted);
            sub.TextWrapping = TextWrapping.NoWrap;
            sub.TextTrimming = TextTrimming.CharacterEllipsis;
            var text = UI.V(1, title, sub);
            text.VerticalAlignment = VerticalAlignment.Center;
            var kind = UI.Text(item.Kind, 11, Theme.Muted, FontWeights.SemiBold);
            kind.VerticalAlignment = VerticalAlignment.Center;
            var dock = new DockPanel();
            DockPanel.SetDock(iconBox, System.Windows.Controls.Dock.Left);
            DockPanel.SetDock(kind, System.Windows.Controls.Dock.Right);
            iconBox.Margin = new Thickness(0, 0, 14, 0);
            dock.Children.Add(iconBox);
            dock.Children.Add(kind);
            dock.Children.Add(text);
            var row = new Border
            {
                Padding = new Thickness(10, 8, 12, 8),
                CornerRadius = new CornerRadius(14),
                Background = selected ? Theme.AmberTint : Brushes.Transparent,
                Child = dock,
                Cursor = Cursors.Hand
            };
            row.MouseLeftButtonUp += (s, e) => Launch(item, (Keyboard.Modifiers & ModifierKeys.Control) != 0);
            row.MouseEnter += (s, e) => { if (_sel != index) row.Background = Theme.Subtle; };
            row.MouseLeave += (s, e) => { if (_sel != index) row.Background = Brushes.Transparent; };
            return row;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using Arrakis.Core;

namespace Arrakis.Modules
{
    /// <summary>The newer widgets: agenda, to-do, quick launch, world clocks, photos, stocks and headlines.</summary>
    public partial class WidgetWindow
    {
        private static readonly HttpClient Http = CreateHttp();
        private static readonly Dictionary<string, (DateTime at, List<(string title, string link)> items)> NewsCache = new Dictionary<string, (DateTime, List<(string, string)>)>();
        private static readonly Dictionary<string, (DateTime at, List<(string sym, double close, double change)> rows)> StockCache = new Dictionary<string, (DateTime, List<(string, double, double)>)>();
        private static readonly Random Rnd = new Random();
        private string _photo;

        private static HttpClient CreateHttp()
        {
            var h = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            h.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Arrakis/1.2");
            return h;
        }

        /// <summary>Called once a minute.</summary>
        public void SlowTick(int minutes)
        {
            if (Item.Kind == "Agenda" && minutes % 5 == 0) Rebuild();
            if (Item.Kind == "Photo" && minutes % 5 == 0) { _photo = null; Rebuild(); }
            if ((Item.Kind == "News" || Item.Kind == "Stocks") && minutes % 15 == 0) Rebuild();
        }

        private void AddKindMenu(ContextMenu menu)
        {
            void Add(string header, Action a)
            {
                var mi = new MenuItem { Header = header };
                mi.Click += (s, e) => a();
                menu.Items.Add(mi);
            }
            switch (Item.Kind)
            {
                case "Agenda":
                    Add("Refresh calendar", async () => { await CalendarFeed.Refresh(); Rebuild(); });
                    Add("Calendar settings...", () => App.Instance.ShowHub("widgets"));
                    break;
                case "Todo":
                    Add("Clear finished items", () => { Item.List.RemoveAll(x => x.StartsWith("[x]")); Settings.Save(); Rebuild(); });
                    break;
                case "Launch":
                    Add("Add an app or file...", () =>
                    {
                        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Apps and files|*.*", Title = "Add to quick launch" };
                        if (dlg.ShowDialog() == true) { Item.List.Add(dlg.FileName); Settings.Save(); Rebuild(); }
                    });
                    Add("Add a folder...", () =>
                    {
                        var dlg = new System.Windows.Forms.FolderBrowserDialog();
                        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) { Item.List.Add(dlg.SelectedPath); Settings.Save(); Rebuild(); }
                    });
                    Add("Clear all", () => { Item.List.Clear(); Settings.Save(); Rebuild(); });
                    break;
                case "WorldClock":
                    Add("Choose cities...", EditClocks);
                    break;
                case "Photo":
                    Add("Next photo", () => { _photo = null; Rebuild(); });
                    Add("Choose folder...", () =>
                    {
                        var dlg = new System.Windows.Forms.FolderBrowserDialog { SelectedPath = Item.Source };
                        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) { Item.Source = dlg.SelectedPath; _photo = null; Settings.Save(); Rebuild(); }
                    });
                    break;
                case "Stocks":
                    Add("Edit symbols...", () => EditText("Stocks", "Symbols, separated by commas. Use Stooq codes, for example AAPL.US, MSFT.US, VOD.UK or ^UKX for the FTSE 100.",
                        string.Join(", ", Item.List), v => { Item.List = v.Split(',').Select(x => x.Trim().ToUpperInvariant()).Where(x => x.Length > 0).Take(8).ToList(); StockCache.Clear(); }));
                    break;
                case "News":
                    Add("Change feed...", () => EditText("Headlines", "Paste the address of any RSS or Atom news feed.", Item.Source, v => { Item.Source = v.Trim(); NewsCache.Clear(); }));
                    break;
            }
            if (menu.Items.Count > 0) menu.Items.Add(new Separator());
        }

        private void EditText(string title, string prompt, string value, Action<string> save)
        {
            var dlg = new Window
            {
                Title = title,
                Width = 440,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = Theme.Bg,
                ResizeMode = ResizeMode.NoResize,
                FontFamily = Theme.Font
            };
            var box = UI.TextInput(value, 380);
            var ok = FlatButton.Primary("Save", () => { save(box.Text); Settings.Save(); Rebuild(); dlg.Close(); });
            dlg.Content = new Border { Padding = new Thickness(24), Child = UI.V(12, UI.Text(prompt, 14, Theme.Text), box, ok) };
            dlg.SourceInitialized += (s, e) => Native.UseDarkTitleBar(new WindowInteropHelper(dlg).Handle);
            dlg.ShowDialog();
        }

        // ---------------- Agenda ----------------
        private UIElement AgendaBody()
        {
            var v = UI.V(10);
            v.Width = 280;
            var head = new DockPanel();
            var today = UI.Label(DateTime.Today.ToString("ddd d MMM"));
            DockPanel.SetDock(today, System.Windows.Controls.Dock.Right);
            head.Children.Add(today);
            head.Children.Add(UI.Text("Agenda", 15, Theme.Text, FontWeights.SemiBold));
            v.Children.Add(head);
            if (!CalendarFeed.IsSet)
            {
                v.Children.Add(UI.Text("Connect your calendar in Arrakis > Widgets to see your events here.", 13, Theme.Muted));
                return v;
            }
            var events = CalendarFeed.Upcoming(6).ToList();
            if (events.Count == 0) v.Children.Add(UI.Text(CalendarFeed.LastError != null ? "Couldn't read your calendar." : "Nothing coming up.", 13, Theme.Muted));
            DateTime? lastDay = null;
            foreach (var ev in events)
            {
                if (lastDay != ev.Start.Date)
                {
                    lastDay = ev.Start.Date;
                    string label = ev.Start.Date == DateTime.Today ? "Today" : ev.Start.Date == DateTime.Today.AddDays(1) ? "Tomorrow" : ev.Start.ToString("dddd d MMM");
                    v.Children.Add(UI.Label(label).WithMargin(new Thickness(0, 4, 0, 0)));
                }
                bool now = ev.Start <= DateTime.Now && ev.End > DateTime.Now;
                var bar = new Border { Width = 3, CornerRadius = new CornerRadius(2), Background = now ? Theme.Amber : Theme.Teal, Margin = new Thickness(0, 2, 10, 2) };
                var title = UI.Text(ev.Title, 14, Theme.Text, FontWeights.SemiBold);
                title.TextWrapping = TextWrapping.NoWrap;
                title.TextTrimming = TextTrimming.CharacterEllipsis;
                string when = ev.AllDay ? "All day" : ev.Start.ToString("HH:mm") + " to " + ev.End.ToString("HH:mm");
                if (now) when = "Now · until " + ev.End.ToString("HH:mm");
                else if (!ev.AllDay && (ev.Start - DateTime.Now).TotalMinutes is double m && m > 0 && m < 60) when = "In " + (int)Math.Ceiling(m) + " min · " + ev.Start.ToString("HH:mm");
                var meta = UI.Text(when + (string.IsNullOrEmpty(ev.Location) ? "" : " · " + ev.Location), 12, now ? Theme.Amber : Theme.Muted);
                meta.TextWrapping = TextWrapping.NoWrap;
                meta.TextTrimming = TextTrimming.CharacterEllipsis;
                var row = new DockPanel();
                DockPanel.SetDock(bar, System.Windows.Controls.Dock.Left);
                row.Children.Add(bar);
                row.Children.Add(UI.V(1, title, meta));
                v.Children.Add(row);
            }
            return v;
        }

        // ---------------- To-do ----------------
        private UIElement TodoBody()
        {
            var v = UI.V(6);
            v.Width = 260;
            int left = Item.List.Count(x => !x.StartsWith("[x]"));
            var head = new DockPanel();
            var count = UI.Label(left + " left");
            DockPanel.SetDock(count, System.Windows.Controls.Dock.Right);
            head.Children.Add(count);
            head.Children.Add(UI.Text("To-do", 15, Theme.Text, FontWeights.SemiBold));
            v.Children.Add(head);
            for (int i = 0; i < Item.List.Count; i++)
            {
                int index = i;
                string raw = Item.List[i];
                bool done = raw.StartsWith("[x]");
                string text = raw.Length > 3 && raw[0] == '[' ? raw.Substring(4) : raw;
                var check = new FlatButton
                {
                    Width = 22, Height = 22, MinHeight = 22, Padding = new Thickness(0), CornerRadius = new CornerRadius(7),
                    Background = done ? Theme.Amber : Brushes.Transparent, BorderBrush = done ? Theme.Amber : Theme.Muted, BorderThickness = new Thickness(1.5),
                    Content = done ? UI.GridIcon(Icons.Check, 12, Theme.Dark, 3) : null
                };
                FlatButton.SetA11yName(check, done ? "Mark as not done" : "Mark as done");
                check.Click += (s, e) =>
                {
                    Item.List[index] = (done ? "[ ] " : "[x] ") + text;
                    Settings.Save();
                    Rebuild();
                };
                var label = UI.Text(text, 14, done ? Theme.Muted : Theme.Text);
                label.TextDecorations = done ? TextDecorations.Strikethrough : null;
                label.VerticalAlignment = VerticalAlignment.Center;
                var del = FlatButton.IconButton(Icons.Close, "Remove", () => { Item.List.RemoveAt(index); Settings.Save(); Rebuild(); }, 24);
                del.Background = Brushes.Transparent;
                del.Opacity = 0.5;
                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                DockPanel.SetDock(check, System.Windows.Controls.Dock.Left);
                DockPanel.SetDock(del, System.Windows.Controls.Dock.Right);
                check.Margin = new Thickness(0, 0, 10, 0);
                row.Children.Add(check);
                row.Children.Add(del);
                row.Children.Add(label);
                v.Children.Add(row);
            }
            var add = new TextBox
            {
                Background = Theme.Subtle,
                BorderThickness = new Thickness(0),
                Foreground = Theme.Text,
                CaretBrush = Theme.Text,
                FontSize = 13,
                Padding = new Thickness(10, 7, 10, 7),
                Margin = new Thickness(0, 6, 0, 0)
            };
            var hint = UI.Text("Add a task and press Enter", 13, Theme.Muted);
            hint.IsHitTestVisible = false;
            hint.Margin = new Thickness(11, 13, 0, 0);
            var addGrid = new Grid();
            addGrid.Children.Add(add);
            addGrid.Children.Add(hint);
            add.TextChanged += (s, e) => hint.Visibility = add.Text.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
            add.PreviewMouseLeftButtonDown += (s, e) => { Activate(); add.Focus(); };
            add.KeyDown += (s, e) =>
            {
                if (e.Key != Key.Enter || string.IsNullOrWhiteSpace(add.Text)) return;
                Item.List.Add("[ ] " + add.Text.Trim());
                Settings.Save();
                Rebuild();
            };
            v.Children.Add(addGrid);
            return v;
        }

        // ---------------- Quick launch ----------------
        private UIElement LaunchBody()
        {
            AllowDrop = true;
            Drop -= OnLaunchDrop;
            Drop += OnLaunchDrop;
            var v = UI.V(10);
            v.Children.Add(UI.Text("Quick launch", 15, Theme.Text, FontWeights.SemiBold));
            var wrap = new WrapPanel { Width = 300 };
            foreach (var path in Item.List.ToList())
            {
                string p = path;
                bool isDir = Directory.Exists(p);
                FrameworkElement icon;
                var img = isDir ? null : IconCache.ForFile(p);
                if (img != null) icon = new Image { Source = img, Width = 32, Height = 32 };
                else icon = UI.GridIcon(isDir ? Icons.Folder : Icons.Note, 26, Theme.Amber);
                icon.HorizontalAlignment = HorizontalAlignment.Center;
                string name = Path.GetFileNameWithoutExtension(p.TrimEnd('\\'));
                if (string.IsNullOrEmpty(name)) name = p;
                var label = UI.Text(name, 11, Theme.Text);
                label.TextAlignment = TextAlignment.Center;
                label.TextTrimming = TextTrimming.CharacterEllipsis;
                label.TextWrapping = TextWrapping.NoWrap;
                label.Width = 64;
                var tile = new FlatButton
                {
                    Width = 72, Height = 72, MinHeight = 72, Padding = new Thickness(4), CornerRadius = new CornerRadius(14),
                    Background = Theme.Subtle, Content = UI.V(6, icon, label), Margin = new Thickness(0, 0, 0, 0), ToolTip = p
                };
                tile.Click += (s, e) => Shell.Open(p);
                tile.MouseRightButtonUp += (s, e) =>
                {
                    var m = new ContextMenu();
                    var rm = new MenuItem { Header = "Remove from quick launch" };
                    rm.Click += (a, b) => { Item.List.Remove(p); Settings.Save(); Rebuild(); };
                    m.Items.Add(rm);
                    m.IsOpen = true;
                    e.Handled = true;
                };
                wrap.Children.Add(new Border { Padding = new Thickness(0, 0, 4, 4), Child = tile });
            }
            v.Children.Add(wrap);
            if (Item.List.Count == 0) v.Children.Add(UI.Text("Drag apps, files or folders here, or right-click to add.", 13, Theme.Muted));
            return v;
        }

        private void OnLaunchDrop(object sender, DragEventArgs e)
        {
            if (Item.Kind != "Launch" || !(e.Data.GetData(DataFormats.FileDrop) is string[] files)) return;
            foreach (var f in files) if (!Item.List.Contains(f, StringComparer.OrdinalIgnoreCase)) Item.List.Add(f);
            Settings.Save();
            Rebuild();
        }

        // ---------------- World clocks ----------------
        public static readonly (string city, string tz)[] Cities =
        {
            ("London", "GMT Standard Time"), ("Dublin", "GMT Standard Time"), ("Paris", "Romance Standard Time"), ("Berlin", "W. Europe Standard Time"),
            ("Athens", "GTB Standard Time"), ("Dubai", "Arabian Standard Time"), ("Mumbai", "India Standard Time"), ("Karachi", "Pakistan Standard Time"),
            ("Singapore", "Singapore Standard Time"), ("Hong Kong", "China Standard Time"), ("Tokyo", "Tokyo Standard Time"), ("Sydney", "AUS Eastern Standard Time"),
            ("Auckland", "New Zealand Standard Time"), ("New York", "Eastern Standard Time"), ("Toronto", "Eastern Standard Time"), ("Chicago", "Central Standard Time"),
            ("Denver", "Mountain Standard Time"), ("Los Angeles", "Pacific Standard Time"), ("São Paulo", "E. South America Standard Time"), ("Lagos", "W. Central Africa Standard Time"),
            ("Johannesburg", "South Africa Standard Time"), ("Georgetown", "SA Western Standard Time"), ("Port of Spain", "SA Western Standard Time"), ("Kingston", "SA Pacific Standard Time")
        };

        private UIElement WorldClockBody()
        {
            var v = UI.V(8);
            v.Width = 260;
            v.Children.Add(UI.Text("World clocks", 15, Theme.Text, FontWeights.SemiBold));
            var rows = new List<(TextBlock time, TextBlock diff, TimeZoneInfo tz)>();
            foreach (var entry in Item.List)
            {
                string tzId = entry, label = entry;
                int bar = entry.IndexOf('|');
                if (bar > 0) { label = entry.Substring(0, bar); tzId = entry.Substring(bar + 1); }
                else label = Cities.FirstOrDefault(c => c.tz == tzId).city ?? tzId;
                TimeZoneInfo tz;
                try { tz = TimeZoneInfo.FindSystemTimeZoneById(tzId); } catch { continue; }
                var name = UI.Text(label, 14, Theme.Text, FontWeights.SemiBold);
                var diff = UI.Text("", 12, Theme.Muted);
                var time = UI.Text("", 22, Theme.Text, FontWeights.SemiBold, true);
                time.VerticalAlignment = VerticalAlignment.Center;
                var row = new DockPanel();
                DockPanel.SetDock(time, System.Windows.Controls.Dock.Right);
                row.Children.Add(time);
                row.Children.Add(UI.V(0, name, diff));
                v.Children.Add(row);
                rows.Add((time, diff, tz));
            }
            if (rows.Count == 0) v.Children.Add(UI.Text("Right-click to choose cities.", 13, Theme.Muted));
            _tick = () =>
            {
                foreach (var (time, diff, tz) in rows)
                {
                    var t = TimeZoneInfo.ConvertTime(DateTime.Now, tz);
                    time.Text = t.ToString("HH:mm");
                    double hours = (tz.GetUtcOffset(DateTime.UtcNow) - TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow)).TotalHours;
                    string day = t.Date > DateTime.Today ? "Tomorrow" : t.Date < DateTime.Today ? "Yesterday" : "Today";
                    diff.Text = day + ", " + (hours == 0 ? "same time" : (hours > 0 ? "+" : "") + hours.ToString("0.#") + " h");
                }
            };
            return v;
        }

        private void EditClocks()
        {
            var dlg = new Window
            {
                Title = "World clocks",
                Width = 380,
                Height = 560,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = Theme.Bg,
                FontFamily = Theme.Font
            };
            var list = UI.V(2);
            var checks = new List<(CheckBox box, string value)>();
            foreach (var (city, tz) in Cities)
            {
                string value = city + "|" + tz;
                var cb = new CheckBox
                {
                    Content = city,
                    Foreground = Theme.Text,
                    FontSize = 14,
                    Margin = new Thickness(0, 4, 0, 4),
                    IsChecked = Item.List.Contains(value) || Item.List.Contains(tz) && Cities.First(c => c.tz == tz).city == city
                };
                checks.Add((cb, value));
                list.Children.Add(cb);
            }
            var ok = FlatButton.Primary("Save", () =>
            {
                Item.List = checks.Where(c => c.box.IsChecked == true).Select(c => c.value).Take(6).ToList();
                Settings.Save();
                Rebuild();
                dlg.Close();
            });
            var dock = new DockPanel { Margin = new Thickness(20) };
            var top = UI.Text("Pick up to six cities.", 14, Theme.Text, FontWeights.SemiBold);
            DockPanel.SetDock(top, System.Windows.Controls.Dock.Top);
            DockPanel.SetDock(ok, System.Windows.Controls.Dock.Bottom);
            ok.Margin = new Thickness(0, 12, 0, 0);
            dock.Children.Add(top);
            dock.Children.Add(ok);
            dock.Children.Add(new ScrollViewer { Content = list, Margin = new Thickness(0, 12, 0, 0) });
            dlg.Content = dock;
            dlg.SourceInitialized += (s, e) => Native.UseDarkTitleBar(new WindowInteropHelper(dlg).Handle);
            dlg.ShowDialog();
        }

        // ---------------- Photo frame ----------------
        private UIElement PhotoBody()
        {
            var frame = new Border { Width = 300, Height = 200, CornerRadius = new CornerRadius(14), Background = Theme.Subtle, ClipToBounds = true, Cursor = Cursors.Hand };
            try
            {
                if (_photo == null || !File.Exists(_photo))
                {
                    var exts = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp" };
                    var files = Directory.Exists(Item.Source)
                        ? Directory.EnumerateFiles(Item.Source, "*.*", SearchOption.AllDirectories).Where(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant())).Take(2000).ToList()
                        : new List<string>();
                    _photo = files.Count > 0 ? files[Rnd.Next(files.Count)] : null;
                }
                if (_photo != null)
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.DecodePixelWidth = 640;
                    bi.UriSource = new Uri(_photo);
                    bi.EndInit();
                    frame.Background = new ImageBrush(bi) { Stretch = Stretch.UniformToFill };
                    frame.ToolTip = Path.GetFileName(_photo) + "\nClick to open";
                    string path = _photo;
                    frame.MouseLeftButtonUp += (s, e) => Shell.Open(path);
                    _frame.Padding = new Thickness(8);
                    return frame;
                }
            }
            catch (Exception ex) { Log.Write("Photo widget: " + ex.Message); }
            frame.Child = UI.Text("No photos found. Right-click to choose a folder.", 13, Theme.Muted).WithMargin(new Thickness(16));
            return frame;
        }

        // ---------------- Stocks ----------------
        private UIElement StocksBody()
        {
            var v = UI.V(8);
            v.Width = 260;
            var head = new DockPanel();
            var note = UI.Label("Today");
            DockPanel.SetDock(note, System.Windows.Controls.Dock.Right);
            head.Children.Add(note);
            head.Children.Add(UI.Text("Stocks", 15, Theme.Text, FontWeights.SemiBold));
            v.Children.Add(head);
            string key = string.Join(",", Item.List).ToLowerInvariant();
            if (Item.List.Count == 0) { v.Children.Add(UI.Text("Right-click to add symbols.", 13, Theme.Muted)); return v; }
            if (StockCache.TryGetValue(key, out var cached) && (DateTime.Now - cached.at).TotalMinutes < 15)
            {
                foreach (var (sym, close, change) in cached.rows)
                {
                    var up = change >= 0;
                    var price = UI.Text(close.ToString("#,0.00", CultureInfo.CurrentCulture), 14, Theme.Text, FontWeights.SemiBold, true);
                    var pct = UI.Text((up ? "+" : "") + change.ToString("0.00") + "%", 12, up ? Theme.Green : Theme.Red, FontWeights.SemiBold);
                    pct.TextAlignment = TextAlignment.Right;
                    var right = UI.V(0, price, pct);
                    right.HorizontalAlignment = HorizontalAlignment.Right;
                    var row = new DockPanel();
                    DockPanel.SetDock(right, System.Windows.Controls.Dock.Right);
                    row.Children.Add(right);
                    var name = UI.Text(sym.Replace(".US", "").Replace(".UK", ""), 14, Theme.Text, FontWeights.SemiBold);
                    name.VerticalAlignment = VerticalAlignment.Center;
                    row.Children.Add(name);
                    v.Children.Add(row);
                }
                if (cached.rows.Count == 0) v.Children.Add(UI.Text("Couldn't get prices right now.", 13, Theme.Muted));
                v.Children.Add(UI.Text("Prices from Stooq, may be delayed.", 11, Theme.Muted));
                return v;
            }
            v.Children.Add(UI.Text("Loading prices...", 13, Theme.Muted));
            _ = LoadStocks(key);
            return v;
        }

        private async Task LoadStocks(string key)
        {
            var rows = new List<(string, double, double)>();
            try
            {
                string url = "https://stooq.com/q/l/?s=" + Uri.EscapeDataString(key) + "&f=sd2t2ohlc&h&e=csv";
                string csv = await Http.GetStringAsync(url);
                foreach (var line in csv.Split('\n').Skip(1))
                {
                    var c = line.Trim().Split(',');
                    if (c.Length < 7) continue;
                    if (!double.TryParse(c[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double open)) continue;
                    if (!double.TryParse(c[6], NumberStyles.Float, CultureInfo.InvariantCulture, out double close)) continue;
                    rows.Add((c[0], close, open != 0 ? (close - open) / open * 100 : 0));
                }
            }
            catch (Exception ex) { Log.Write("Stocks: " + ex.Message); }
            StockCache[key] = (DateTime.Now, rows);
            await Dispatcher.InvokeAsync(Rebuild);
        }

        // ---------------- Headlines ----------------
        private UIElement NewsBody()
        {
            var v = UI.V(8);
            v.Width = 300;
            v.Children.Add(UI.Text("Headlines", 15, Theme.Text, FontWeights.SemiBold));
            if (string.IsNullOrWhiteSpace(Item.Source)) { v.Children.Add(UI.Text("Right-click to choose a news feed.", 13, Theme.Muted)); return v; }
            if (NewsCache.TryGetValue(Item.Source, out var cached) && (DateTime.Now - cached.at).TotalMinutes < 15)
            {
                foreach (var (title, link) in cached.items.Take(5))
                {
                    var t = UI.Text(title, 13, Theme.Text, FontWeights.Medium);
                    t.MaxHeight = 40;
                    t.TextTrimming = TextTrimming.WordEllipsis;
                    var row = new Border { Padding = new Thickness(0, 2, 0, 2), Child = t, Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = title };
                    string l = link;
                    row.MouseLeftButtonUp += (s, e) => { if (!string.IsNullOrEmpty(l)) Shell.Open(l); };
                    row.MouseEnter += (s, e) => t.Foreground = Theme.Amber;
                    row.MouseLeave += (s, e) => t.Foreground = Theme.Text;
                    v.Children.Add(row);
                }
                if (cached.items.Count == 0) v.Children.Add(UI.Text("Couldn't read that feed.", 13, Theme.Muted));
                return v;
            }
            v.Children.Add(UI.Text("Loading headlines...", 13, Theme.Muted));
            _ = LoadNews(Item.Source);
            return v;
        }

        private async Task LoadNews(string url)
        {
            var items = new List<(string, string)>();
            try
            {
                var xml = XDocument.Parse(await Http.GetStringAsync(url));
                XNamespace atom = "http://www.w3.org/2005/Atom";
                foreach (var it in xml.Descendants("item").Take(10))
                {
                    string t = ((string)it.Element("title") ?? "").Trim();
                    string l = ((string)it.Element("link") ?? "").Trim();
                    items.Add((t, l));
                }
                if (items.Count == 0)
                    foreach (var it in xml.Descendants(atom + "entry").Take(10))
                    {
                        string t = ((string)it.Element(atom + "title") ?? "").Trim();
                        string l = (string)it.Element(atom + "link")?.Attribute("href") ?? "";
                        items.Add((t, l));
                    }
            }
            catch (Exception ex) { Log.Write("News: " + ex.Message); }
            NewsCache[url] = (DateTime.Now, items);
            await Dispatcher.InvokeAsync(Rebuild);
        }
    }
}

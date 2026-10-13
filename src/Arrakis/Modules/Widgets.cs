using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Arrakis.Core;

namespace Arrakis.Modules
{
    public class WidgetsModule : ModuleBase
    {
        public override string Id => "widgets";
        public override string Name => "Widgets";
        public override string Description => "Weather, calendar, agenda, to-dos, world clocks, photos, stocks, news and more on your desktop.";
        public override string IconData => Icons.Widgets;
        public override string ShortcutText => "Ctrl+Alt+W";
        public override bool IsEnabled { get => Settings.Current.Widgets.Enabled; set => Settings.Current.Widgets.Enabled = value; }

        public static readonly (string kind, string name, string size, string icon)[] Catalog =
        {
            ("Clock", "Clock", "Wide", Icons.Clock),
            ("Weather", "Weather", "Medium", Icons.Cloud),
            ("Calendar", "Calendar", "Medium", Icons.Calendar),
            ("System", "System rings", "Wide", Icons.Rings),
            ("Battery", "Battery", "Small", Icons.Battery),
            ("NowPlaying", "Now playing", "Medium", Icons.Music),
            ("Notes", "Notes", "Small", Icons.Note),
            ("Countdown", "Countdown", "Small", Icons.Timer),
            ("Agenda", "Agenda", "Medium", Icons.Calendar),
            ("Todo", "To-do list", "Medium", Icons.Check),
            ("Launch", "Quick launch", "Wide", Icons.Dock),
            ("WorldClock", "World clocks", "Medium", Icons.Clock),
            ("Photo", "Photo frame", "Medium", Icons.Wallpaper),
            ("Stocks", "Stocks", "Medium", "M4 19h16M6 15l4-4 3 3 5-6M14 8h4v4"),
            ("News", "Headlines", "Medium", Icons.Lines)
        };

        private readonly List<WidgetWindow> _windows = new List<WidgetWindow>();
        private DispatcherTimer _fast;
        private DispatcherTimer _weather;
        private DispatcherTimer _slow;
        private readonly HashSet<string> _alerted = new HashSet<string>();
        private bool _dimmed;
        public bool EditMode { get; private set; }
        public event Action ListChanged;

        private WidgetSettings S => Settings.Current.Widgets;

        protected override void OnStart()
        {
            Seed();
            foreach (var item in S.Items) Open(item);
            _fast = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _fast.Tick += (s, e) => { foreach (var w in _windows) w.Tick(); };
            _fast.Start();
            _weather = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
            _weather.Tick += async (s, e) => await Weather.Refresh();
            _weather.Start();
            Weather.Updated += OnWeather;
            MediaService.Instance.Changed += OnMedia;
            CalendarFeed.Updated += OnCalendar;
            _ = Weather.Refresh();
            _ = CalendarFeed.Refresh();
            // Calendar, headlines, stocks and photos refresh on a slower beat; meeting alerts are checked every minute.
            int minutes = 0;
            _slow = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            _slow.Tick += async (s, e) =>
            {
                minutes++;
                CheckMeetings();
                foreach (var w in _windows) w.SlowTick(minutes);
                if (minutes % 15 == 0) await CalendarFeed.Refresh();
            };
            _slow.Start();
            Hotkeys.Register("widgets", "Ctrl+Alt+W", ToggleEditMode);
        }

        protected override void OnStop()
        {
            _fast?.Stop();
            _weather?.Stop();
            _slow?.Stop();
            CalendarFeed.Updated -= OnCalendar;
            Weather.Updated -= OnWeather;
            MediaService.Instance.Changed -= OnMedia;
            Hotkeys.Unregister("widgets");
            foreach (var w in _windows.ToList()) w.Close();
            _windows.Clear();
            EditMode = false;
        }

        private void OnWeather() => Application.Current.Dispatcher.BeginInvoke(new Action(() => { foreach (var w in _windows) w.Rebuild(); }));
        private void OnCalendar() => Application.Current.Dispatcher.BeginInvoke(new Action(() => { foreach (var w in _windows.Where(x => x.Item.Kind == "Agenda" || x.Item.Kind == "Calendar")) w.Rebuild(); }));

        private void CheckMeetings()
        {
            if (!S.MeetingAlerts || !CalendarFeed.IsSet) return;
            foreach (var ev in CalendarFeed.Upcoming(10))
            {
                if (ev.AllDay) continue;
                double mins = (ev.Start - DateTime.Now).TotalMinutes;
                if (mins > 0 && mins <= 10 && _alerted.Add(ev.Uid))
                    App.Instance.Tray.Notify(ev.Title + " in " + Math.Max(1, (int)Math.Round(mins)) + " min", ev.Start.ToString("HH:mm") + (string.IsNullOrEmpty(ev.Location) ? "" : " · " + ev.Location));
            }
        }

        private void OnMedia() { foreach (var w in _windows.Where(x => x.Item.Kind == "NowPlaying")) w.Rebuild(); }

        private void Seed()
        {
            if (S.Seeded) return;
            foreach (var k in new[] { "Clock", "Weather", "Calendar", "System", "NowPlaying", "Notes" })
                S.Items.Add(new WidgetInstance { Kind = k, Text = k == "Notes" ? "Double-click to type a note" : "" });
            S.Seeded = true;
            Settings.Save();
        }

        private void Open(WidgetInstance item)
        {
            if (double.IsNaN(item.Left) || double.IsNaN(item.Top)) AutoPlace(item);
            var w = new WidgetWindow(this, item);
            w.Opacity = _dimmed ? 0.3 : 1;
            _windows.Add(w);
            w.Closed += (s, e) => _windows.Remove(w);
            w.Show();
        }

        private void AutoPlace(WidgetInstance item)
        {
            double screenW = SystemParameters.WorkArea.Width;
            int index = S.Items.IndexOf(item);
            bool right = item.Kind == "NowPlaying" || item.Kind == "Notes" || item.Kind == "Countdown" || item.Kind == "Battery";
            double y = 76;
            foreach (var other in S.Items.Take(Math.Max(0, index)))
            {
                bool otherRight = other.Kind == "NowPlaying" || other.Kind == "Notes" || other.Kind == "Countdown" || other.Kind == "Battery";
                if (otherRight == right) y += EstimatedHeight(other.Kind) + 16;
            }
            item.Left = right ? screenW - 32 - 300 : 32;
            item.Top = y;
        }

        private static double EstimatedHeight(string kind) => kind switch
        {
            "Clock" => 120,
            "Weather" => 170,
            "Calendar" => 250,
            "System" => 120,
            "NowPlaying" => 160,
            "Battery" => 110,
            _ => 130
        };

        public void Add(string kind)
        {
            var item = new WidgetInstance { Kind = kind };
            if (kind == "Countdown") { item.Text = "Weekend"; item.Date = NextSaturday(); }
            if (kind == "Notes") item.Text = "Double-click to type a note";
            if (kind == "Todo") item.List = new List<string> { "[ ] Try the to-do widget", "[x] Install Arrakis" };
            if (kind == "WorldClock") item.List = new List<string> { "Eastern Standard Time", "Pacific Standard Time", "Tokyo Standard Time" };
            if (kind == "Stocks") item.List = new List<string> { "AAPL.US", "MSFT.US", "^UKX" };
            if (kind == "News") item.Source = "https://feeds.bbci.co.uk/news/rss.xml";
            if (kind == "Photo") item.Source = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            S.Items.Add(item);
            Settings.Save();
            if (!IsRunning) App.Instance.SetModuleEnabled(this, true);
            else Open(item);
            ListChanged?.Invoke();
        }

        private static DateTime NextSaturday()
        {
            var d = DateTime.Today.AddDays(1);
            while (d.DayOfWeek != DayOfWeek.Saturday) d = d.AddDays(1);
            return d;
        }

        public void Remove(WidgetInstance item)
        {
            S.Items.Remove(item);
            Settings.Save();
            _windows.FirstOrDefault(w => w.Item == item)?.Close();
            ListChanged?.Invoke();
        }

        public void ToggleEditMode()
        {
            EditMode = !EditMode;
            foreach (var w in _windows) w.Rebuild();
        }

        public void SetDimmed(bool dim)
        {
            _dimmed = dim;
            foreach (var w in _windows) w.Opacity = dim ? 0.3 : 1;
        }

        public void RestyleAll()
        {
            foreach (var w in _windows) w.Rebuild();
        }

        /// <summary>Saves where everything is, so you can switch between setups like Work and Gaming.</summary>
        public void SaveLayout(string name)
        {
            var copy = System.Text.Json.JsonSerializer.Deserialize<List<WidgetInstance>>(System.Text.Json.JsonSerializer.Serialize(S.Items, Settings.Json), Settings.Json);
            S.Layouts[name] = copy;
            Settings.Save();
        }

        public void LoadLayout(string name)
        {
            if (!S.Layouts.TryGetValue(name, out var layout)) return;
            var copy = System.Text.Json.JsonSerializer.Deserialize<List<WidgetInstance>>(System.Text.Json.JsonSerializer.Serialize(layout, Settings.Json), Settings.Json);
            foreach (var w in _windows.ToList()) w.Close();
            _windows.Clear();
            S.Items = copy;
            Settings.Save();
            if (IsRunning) foreach (var item in S.Items) Open(item);
            ListChanged?.Invoke();
        }

        public override UIElement BuildPage()
        {
            var gallery = new UniformGrid4();
            foreach (var c in Catalog)
            {
                var tile = new FlatButton
                {
                    Background = Theme.Subtle,
                    BorderBrush = Theme.Line,
                    CornerRadius = new CornerRadius(18),
                    Padding = new Thickness(12),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Stretch,
                    Margin = new Thickness(0, 0, 12, 12)
                };
                var isAmber = Array.IndexOf(Catalog, c) % 2 == 0;
                var thumb = new Border
                {
                    Height = 64,
                    CornerRadius = new CornerRadius(12),
                    Background = isAmber ? Theme.AmberTint : Theme.TealTint,
                    Child = UI.GridIcon(c.icon, 28, isAmber ? Theme.Amber : Theme.Teal, 1.7)
                };
                tile.Content = UI.V(10, thumb, UI.V(2, UI.Text(c.name, 13, Theme.Text, FontWeights.Bold), UI.Text(c.size + " · click to add", 11, Theme.Muted)));
                string kind = c.kind;
                tile.Click += (s, e) => Add(kind);
                FlatButton.SetA11yName(tile, "Add " + c.name + " widget");
                gallery.Children.Add(tile);
            }

            var styleRow = UI.H(8);
            var chips = new List<Chip>();
            foreach (var st in new[] { "Glass", "Solid", "Outline" })
            {
                var chip = new Chip(st, S.Style == st);
                chip.Click += (s, e) =>
                {
                    S.Style = st;
                    Settings.Save();
                    foreach (var ch in chips) ch.Selected = (string)ch.Content == st;
                    RestyleAll();
                };
                chips.Add(chip);
                styleRow.Children.Add(chip);
            }

            var locked = new ToggleSwitch(S.Locked, "Lock widgets");
            locked.Toggled += on => { S.Locked = on; Settings.Save(); };

            var city = UI.TextInput(S.City, 200);
            var cityStatus = UI.Text(double.IsNaN(S.Latitude) ? "No city set yet." : "Using " + S.City, 12, Theme.Muted);
            var find = FlatButton.Secondary("Set city");
            find.MinHeight = 36;
            find.Click += async (s, e) =>
            {
                cityStatus.Text = "Looking up...";
                var r = await Weather.Geocode(city.Text.Trim());
                if (r == null) { cityStatus.Text = "Couldn't find that place. Try a nearby town."; return; }
                S.City = r.Value.name;
                S.Latitude = r.Value.lat;
                S.Longitude = r.Value.lon;
                Settings.Save();
                city.Text = S.City;
                cityStatus.Text = "Using " + S.City;
                await Weather.Refresh();
                App.Instance.Wallpaper.Reapply();
            };
            var fahrenheit = new ToggleSwitch(S.Fahrenheit, "Fahrenheit");
            fahrenheit.Toggled += async on => { S.Fahrenheit = on; Settings.Save(); await Weather.Refresh(); };

            var calUrl = UI.TextInput(Settings.Current.CalendarUrl, 300);
            var calStatus = UI.Text(CalendarFeed.IsSet ? (CalendarFeed.LastError == null ? CalendarFeed.Events.Count + " events in the next month." : "Couldn't read it: " + CalendarFeed.LastError) : "Not connected.", 12, Theme.Muted);
            var calSave = FlatButton.Secondary("Connect", async () =>
            {
                Settings.Current.CalendarUrl = calUrl.Text.Trim();
                Settings.Save();
                calStatus.Text = "Reading your calendar...";
                await CalendarFeed.Refresh();
                calStatus.Text = !CalendarFeed.IsSet ? "Not connected." : CalendarFeed.LastError == null ? "Connected. " + CalendarFeed.Events.Count + " events in the next month." : "Couldn't read it: " + CalendarFeed.LastError;
            });
            calSave.MinHeight = 36;
            var alerts = new ToggleSwitch(S.MeetingAlerts, "Meeting alerts");
            alerts.Toggled += on => { S.MeetingAlerts = on; Settings.Save(); };
            var snap = new ToggleSwitch(S.SnapToGrid, "Snap to grid");
            snap.Toggled += on => { S.SnapToGrid = on; Settings.Save(); };

            var layoutName = UI.TextInput("", 200);
            var layoutList = UI.V(4);
            void RenderLayouts()
            {
                layoutList.Children.Clear();
                foreach (var name in S.Layouts.Keys.ToList())
                {
                    string n = name;
                    var load = FlatButton.Secondary("Switch to", () => LoadLayout(n));
                    load.MinHeight = 34;
                    var del = FlatButton.IconButton(Icons.Trash, "Delete layout", () => { S.Layouts.Remove(n); Settings.Save(); RenderLayouts(); }, 34);
                    layoutList.Children.Add(UI.SettingRow(n, S.Layouts[n].Count + " widgets", UI.H(8, load, del)));
                }
                if (S.Layouts.Count == 0) layoutList.Children.Add(UI.Text("No saved layouts yet.", 13, Theme.Muted));
            }
            RenderLayouts();
            var saveLayout = FlatButton.Secondary("Save current layout", () =>
            {
                string n = string.IsNullOrWhiteSpace(layoutName.Text) ? "Layout " + (S.Layouts.Count + 1) : layoutName.Text.Trim();
                SaveLayout(n);
                layoutName.Text = "";
                RenderLayouts();
            });
            saveLayout.MinHeight = 36;

            var list = UI.V(4);
            void RenderList()
            {
                list.Children.Clear();
                foreach (var item in S.Items.ToList())
                {
                    var name = Catalog.FirstOrDefault(c => c.kind == item.Kind).name ?? item.Kind;
                    var remove = FlatButton.Secondary("Remove", () => { Remove(item); RenderList(); });
                    remove.MinHeight = 34;
                    list.Children.Add(UI.SettingRow(name, item.Kind == "Countdown" ? item.Text + (item.Date.HasValue ? " · " + item.Date.Value.ToString("d MMM yyyy") : "") : null, remove));
                }
                if (S.Items.Count == 0) list.Children.Add(UI.Text("No widgets yet. Pick some above.", 13, Theme.Muted));
            }
            RenderList();
            Action onList = RenderList;
            ListChanged += onList;
            list.Unloaded += (s, e) => ListChanged -= onList;

            return UI.V(16,
                UI.Label("Add widgets"),
                gallery,
                UI.Card(UI.V(0,
                    UI.SettingRow("Style", "Glass, Solid or Outline.", styleRow),
                    UI.Divider(),
                    UI.SettingRow("Lock widgets", "Stops them being dragged by accident. Press Ctrl+Alt+W to show remove buttons.", locked),
                    UI.Divider(),
                    UI.SettingRow("Weather city", null, UI.V(6, UI.H(8, city, find), cityStatus)),
                    UI.Divider(),
                    UI.SettingRow("Use Fahrenheit", null, fahrenheit),
                    UI.Divider(),
                    UI.SettingRow("Snap to grid", "Widgets line up neatly when you let go of them.", snap)), 8),
                UI.Label("Calendar"),
                UI.Card(UI.V(0,
                    UI.SettingRow("Calendar link", "Paste your calendar's .ics link. In Google Calendar it's Settings > your calendar > Secret address in iCal format. In Outlook, publish your calendar and copy the ICS link.", UI.V(4, UI.H(8, calUrl, calSave), calStatus)),
                    UI.Divider(),
                    UI.SettingRow("Meeting alerts", "A heads-up 10 minutes before each event.", alerts)), 8),
                UI.Label("Layouts"),
                UI.Card(UI.V(10,
                    UI.Text("Save where your widgets are, then switch between setups like Work and Gaming.", 13, Theme.Muted),
                    UI.H(8, layoutName, saveLayout),
                    layoutList), 16),
                UI.Label("On your desktop"),
                UI.Card(list, 12));
        }
    }

    /// <summary>A wrap panel that lays tiles out four to a row.</summary>
    public class UniformGrid4 : System.Windows.Controls.Primitives.UniformGrid
    {
        public UniformGrid4() { Columns = 4; }
    }

    public partial class WidgetWindow : Window
    {
        private readonly WidgetsModule _module;
        public WidgetInstance Item { get; }
        private readonly Border _frame;
        private Action _tick;

        private WidgetSettings S => Settings.Current.Widgets;

        public WidgetWindow(WidgetsModule module, WidgetInstance item)
        {
            _module = module;
            Item = item;
            Title = "Arrakis widget";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            ShowActivated = false;
            FontFamily = Theme.Font;
            Left = item.Left;
            Top = item.Top;

            _frame = new Border { Margin = new Thickness(10), Padding = new Thickness(18, 16, 18, 16) };
            Content = _frame;

            SourceInitialized += (s, e) =>
            {
                WindowHelper.MakeToolWindow(this, false);
                var src = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
                src?.AddHook(KeepAtBottom);
                Native.SetWindowPos(new WindowInteropHelper(this).Handle, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            };
            MouseLeftButtonDown += (s, e) =>
            {
                if (S.Locked) return;
                if (e.OriginalSource is TextBox || IsInsideButton(e.OriginalSource as DependencyObject)) return;
                try { DragMove(); } catch { }
                if (S.SnapToGrid)
                {
                    Left = Math.Round(Left / 16) * 16;
                    Top = Math.Round(Top / 16) * 16;
                }
                Item.Left = Left;
                Item.Top = Top;
                Settings.Save();
            };
            MouseRightButtonUp += (s, e) => ShowMenu();
            Rebuild();
        }

        private static bool IsInsideButton(DependencyObject d)
        {
            while (d != null)
            {
                if (d is System.Windows.Controls.Primitives.ButtonBase || d is TextBox) return true;
                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return false;
        }

        private IntPtr KeepAtBottom(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_WINDOWPOSCHANGING)
            {
                var pos = Marshal.PtrToStructure<Native.WINDOWPOS>(lParam);
                pos.hwndInsertAfter = Native.HWND_BOTTOM;
                pos.flags &= ~Native.SWP_NOZORDER;
                Marshal.StructureToPtr(pos, lParam, false);
            }
            return IntPtr.Zero;
        }

        private void ShowMenu()
        {
            var menu = new ContextMenu();
            if (Item.Kind == "Countdown")
            {
                var edit = new MenuItem { Header = "Edit countdown..." };
                edit.Click += (s, e) => EditCountdown();
                menu.Items.Add(edit);
            }
            AddKindMenu(menu);
            var size = new MenuItem { Header = "Size" };
            foreach (var (code, label) in new[] { ("S", "Small"), ("M", "Medium"), ("L", "Large") })
            {
                var mi = new MenuItem { Header = label, IsChecked = (Item.Size ?? "M") == code };
                mi.Click += (s, e) => { Item.Size = code; Settings.Save(); Rebuild(); };
                size.Items.Add(mi);
            }
            menu.Items.Add(size);
            var lockItem = new MenuItem { Header = S.Locked ? "Unlock widgets" : "Lock widgets" };
            lockItem.Click += (s, e) => { S.Locked = !S.Locked; Settings.Save(); };
            menu.Items.Add(lockItem);
            var style = new MenuItem { Header = "Style" };
            foreach (var st in new[] { "Glass", "Solid", "Outline" })
            {
                var mi = new MenuItem { Header = st, IsChecked = S.Style == st };
                mi.Click += (s, e) => { S.Style = st; Settings.Save(); _module.RestyleAll(); };
                style.Items.Add(mi);
            }
            menu.Items.Add(style);
            var remove = new MenuItem { Header = "Remove this widget" };
            remove.Click += (s, e) => _module.Remove(Item);
            menu.Items.Add(remove);
            menu.Items.Add(new Separator());
            var open = new MenuItem { Header = "Widget settings..." };
            open.Click += (s, e) => App.Instance.ShowHub("widgets");
            menu.Items.Add(open);
            menu.IsOpen = true;
        }

        private void EditCountdown()
        {
            var dlg = new Window
            {
                Title = "Edit countdown",
                Width = 360,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = Theme.Bg,
                ResizeMode = ResizeMode.NoResize,
                FontFamily = Theme.Font
            };
            var name = UI.TextInput(Item.Text, 300);
            var date = new DatePicker { SelectedDate = Item.Date ?? DateTime.Today.AddDays(7), Height = 36, FontSize = 14 };
            var save = FlatButton.Primary("Save", () =>
            {
                Item.Text = string.IsNullOrWhiteSpace(name.Text) ? "Countdown" : name.Text.Trim();
                Item.Date = date.SelectedDate ?? DateTime.Today;
                Settings.Save();
                Rebuild();
                dlg.Close();
            });
            dlg.Content = new Border
            {
                Padding = new Thickness(24),
                Child = UI.V(12, UI.Text("What are you counting down to?", 14, Theme.Text, FontWeights.SemiBold), name, date, save)
            };
            dlg.SourceInitialized += (s, e) => Native.UseDarkTitleBar(new WindowInteropHelper(dlg).Handle);
            dlg.ShowDialog();
        }

        public void Tick() => _tick?.Invoke();

        public void Rebuild()
        {
            _tick = null;
            ApplyFrameStyle();
            double scale = (Item.Size ?? "M") == "S" ? 0.84 : Item.Size == "L" ? 1.2 : 1.0;
            _frame.LayoutTransform = Math.Abs(scale - 1) < 0.01 ? Transform.Identity : new ScaleTransform(scale, scale);
            UIElement body = Item.Kind switch
            {
                "Clock" => Clock(),
                "Weather" => WeatherBody(),
                "Calendar" => CalendarBody(),
                "System" => SystemBody(),
                "Battery" => BatteryBody(),
                "NowPlaying" => NowPlaying(),
                "Notes" => Notes(),
                "Countdown" => Countdown(),
                "Agenda" => AgendaBody(),
                "Todo" => TodoBody(),
                "Launch" => LaunchBody(),
                "WorldClock" => WorldClockBody(),
                "Photo" => PhotoBody(),
                "Stocks" => StocksBody(),
                "News" => NewsBody(),
                _ => UI.Text("Unknown widget")
            };
            if (_module.EditMode)
            {
                var g = new Grid();
                g.Children.Add(body);
                var x = new FlatButton
                {
                    Content = UI.GridIcon(Icons.Close, 12, Brushes.White, 2.6),
                    Width = 28,
                    Height = 28,
                    MinHeight = 28,
                    Padding = new Thickness(0),
                    CornerRadius = new CornerRadius(14),
                    Background = Theme.Red,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(-30, -28, 0, 0)
                };
                FlatButton.SetA11yName(x, "Remove widget");
                x.Click += (s, e) => _module.Remove(Item);
                g.Children.Add(x);
                body = g;
                _frame.BorderBrush = Theme.Amber;
            }
            _frame.Child = body;
            _tick?.Invoke();
        }

        private void ApplyFrameStyle()
        {
            _frame.CornerRadius = new CornerRadius(22);
            _frame.BorderThickness = new Thickness(1);
            if (Item.Kind == "Notes")
            {
                _frame.Background = Theme.Brush("#FFE7A8");
                _frame.BorderBrush = Brushes.Transparent;
                _frame.Effect = Theme.Shadow(24, 0.3, 8);
                return;
            }
            switch (S.Style)
            {
                case "Solid":
                    _frame.Background = Theme.Panel;
                    _frame.BorderBrush = Theme.Line;
                    _frame.Effect = Theme.Shadow(24, 0.3, 8);
                    break;
                case "Outline":
                    _frame.Background = Theme.Brush(Color.FromArgb(0x40, 0, 0, 0));
                    _frame.BorderBrush = Theme.Brush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
                    _frame.BorderThickness = new Thickness(1.5);
                    _frame.Effect = null;
                    break;
                default:
                    _frame.Background = Theme.Glass;
                    _frame.BorderBrush = Theme.GlassBorder;
                    _frame.Effect = Theme.Shadow(30, 0.35, 10);
                    break;
            }
        }

        // ---------- Widget bodies ----------
        private UIElement Clock()
        {
            var time = UI.Text("", 56, Theme.Text, FontWeights.SemiBold, true);
            var date = UI.Text("", 16, Theme.Muted);
            _tick = () =>
            {
                time.Text = DateTime.Now.ToString("HH:mm");
                date.Text = DateTime.Now.ToString("dddd d MMMM");
            };
            var v = UI.V(4, time, date);
            v.Width = 264;
            return v;
        }

        private UIElement WeatherBody()
        {
            var w = Weather.Latest;
            var stack = UI.V(8);
            stack.Width = 264;
            if (double.IsNaN(S.Latitude))
            {
                stack.Children.Add(UI.Label("Weather"));
                stack.Children.Add(UI.Text("Set your city in Arrakis > Widgets to see the weather here.", 14, Theme.Text));
                return stack;
            }
            if (w == null)
            {
                stack.Children.Add(UI.Label(S.City));
                stack.Children.Add(UI.Text("Loading...", 14, Theme.Muted));
                return stack;
            }
            string unit = "°";
            var top = new DockPanel();
            var icon = UI.GridIcon(Weather.IconFor(w.Code), 46, Theme.Text, 1.5);
            DockPanel.SetDock(icon, System.Windows.Controls.Dock.Right);
            top.Children.Add(icon);
            top.Children.Add(UI.V(4,
                UI.Label(ShortCity(w.City)),
                UI.Text(Math.Round(w.Temp) + unit, 44, Theme.Text, FontWeights.SemiBold, true),
                UI.Text(Weather.Describe(w.Code) + " · H " + Math.Round(w.High) + unit + " L " + Math.Round(w.Low) + unit, 13, Theme.Muted)));
            stack.Children.Add(top);
            var hours = new System.Windows.Controls.Primitives.UniformGrid { Columns = Math.Max(1, w.Hours.Length), Margin = new Thickness(0, 6, 0, 0) };
            foreach (var (time, temp) in w.Hours)
            {
                var t = UI.Text(time, 12, Theme.Muted);
                t.TextAlignment = TextAlignment.Center;
                var v = UI.Text(Math.Round(temp) + unit, 14, Theme.Text, FontWeights.SemiBold, true);
                v.TextAlignment = TextAlignment.Center;
                hours.Children.Add(UI.V(4, t, v));
            }
            stack.Children.Add(hours);
            return stack;
        }

        private static string ShortCity(string c)
        {
            if (string.IsNullOrEmpty(c)) return "";
            int comma = c.IndexOf(',');
            return comma > 0 ? c.Substring(0, comma) : c;
        }

        private UIElement CalendarBody()
        {
            var today = DateTime.Today;
            var first = new DateTime(today.Year, today.Month, 1);
            var fdow = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
            int offset = ((int)first.DayOfWeek - (int)fdow + 7) % 7;
            int days = DateTime.DaysInMonth(today.Year, today.Month);

            var header = new DockPanel();
            var dayLbl = UI.Label(today.ToString("ddd d"));
            DockPanel.SetDock(dayLbl, System.Windows.Controls.Dock.Right);
            header.Children.Add(dayLbl);
            header.Children.Add(UI.Text(today.ToString("MMMM yyyy"), 15, Theme.Text, FontWeights.SemiBold));

            var names = new System.Windows.Controls.Primitives.UniformGrid { Columns = 7, Margin = new Thickness(0, 10, 0, 4) };
            for (int i = 0; i < 7; i++)
            {
                var dow = (DayOfWeek)(((int)fdow + i) % 7);
                var t = UI.Text(CultureInfo.CurrentCulture.DateTimeFormat.GetShortestDayName(dow).Substring(0, 1), 11, Theme.Muted, FontWeights.SemiBold);
                t.TextAlignment = TextAlignment.Center;
                names.Children.Add(t);
            }
            var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 7 };
            for (int i = 0; i < offset; i++) grid.Children.Add(new Border());
            for (int d = 1; d <= days; d++)
            {
                bool isToday = d == today.Day;
                var t = UI.Text(d.ToString(), 12, isToday ? Theme.Dark : d < today.Day ? Theme.Muted : Theme.Text, FontWeights.SemiBold);
                t.TextAlignment = TextAlignment.Center;
                t.VerticalAlignment = VerticalAlignment.Center;
                var dayDate = new DateTime(today.Year, today.Month, d);
                bool hasEvents = CalendarFeed.IsSet && CalendarFeed.On(dayDate).Any();
                var cell = new Grid();
                cell.Children.Add(t);
                if (hasEvents)
                    cell.Children.Add(new Border { Width = 4, Height = 4, CornerRadius = new CornerRadius(2), Background = isToday ? Theme.Dark : Theme.Teal, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 2) });
                grid.Children.Add(new Border
                {
                    Height = 26,
                    CornerRadius = new CornerRadius(8),
                    Background = isToday ? Theme.Amber : Brushes.Transparent,
                    Child = cell,
                    Margin = new Thickness(1),
                    ToolTip = hasEvents ? string.Join("\n", CalendarFeed.On(dayDate).Take(5).Select(ev => (ev.AllDay ? "All day" : ev.Start.ToString("HH:mm")) + "  " + ev.Title)) : null
                });
            }
            var v = UI.V(0, header, names, grid);
            v.Width = 264;
            return v;
        }

        private UIElement SystemBody()
        {
            var cpu = new Ring(58, 5, Theme.Teal, "0%");
            var mem = new Ring(58, 5, Theme.Amber, "0%");
            var third = new Ring(58, 5, Theme.Green, "0%");
            var thirdLabel = UI.Label("Battery");
            UIElement Col(Ring r, TextBlock lbl)
            {
                lbl.HorizontalAlignment = HorizontalAlignment.Center;
                var v = UI.V(6, r, lbl);
                v.HorizontalAlignment = HorizontalAlignment.Center;
                return v;
            }
            var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Width = 264 };
            grid.Children.Add(Col(cpu, UI.Label("CPU")));
            grid.Children.Add(Col(mem, UI.Label("Memory")));
            grid.Children.Add(Col(third, thirdLabel));
            SystemStats.CpuPercent();
            _tick = () =>
            {
                double c = SystemStats.CpuPercent();
                double m = SystemStats.MemoryPercent();
                cpu.SetValue(c / 100); cpu.Center.Text = Math.Round(c) + "%";
                mem.SetValue(m / 100); mem.Center.Text = Math.Round(m) + "%";
                var p = Power.Read();
                if (p.HasBattery)
                {
                    thirdLabel.Text = "BATTERY";
                    third.SetValue(p.Percent / 100.0); third.Center.Text = p.Percent + "%";
                }
                else
                {
                    double d = SystemStats.DiskPercent();
                    thirdLabel.Text = "DISK";
                    third.SetValue(d / 100); third.Center.Text = Math.Round(d) + "%";
                }
            };
            return grid;
        }

        private UIElement BatteryBody()
        {
            var ring = new Ring(64, 7, Theme.Green, "");
            var pct = UI.Text("", 28, Theme.Text, FontWeights.SemiBold, true);
            var sub = UI.Text("", 13, Theme.Muted);
            _tick = () =>
            {
                var p = Power.Read();
                if (!p.HasBattery) { pct.Text = "--"; sub.Text = "No battery"; ring.SetValue(0); return; }
                ring.SetValue(p.Percent / 100.0);
                pct.Text = p.Percent + "%";
                sub.Text = p.OnMains ? (p.Percent >= 100 ? "Fully charged" : "Charging") : p.SecondsLeft > 0 ? Power.FormatTime(p.SecondsLeft) + " left" : "On battery";
            };
            var text = UI.V(2, UI.Label("Battery"), pct, sub);
            text.VerticalAlignment = VerticalAlignment.Center;
            var row = UI.H(16, ring, text);
            row.Width = 224;
            return row;
        }

        private UIElement NowPlaying()
        {
            var m = MediaService.Instance;
            var art = new Border { Width = 60, Height = 60, CornerRadius = new CornerRadius(14), Background = Theme.Off };
            if (m.Art != null) art.Background = new ImageBrush(m.Art) { Stretch = Stretch.UniformToFill };
            else art.Child = UI.GridIcon(Icons.Music, 26, Theme.Muted);
            var title = UI.Text(m.HasMedia ? m.Title : "Nothing playing", 16, Theme.Text, FontWeights.Bold);
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            title.TextWrapping = TextWrapping.NoWrap;
            var artist = UI.Text(m.HasMedia ? m.Artist : "Play something in Spotify, a browser or Media Player", 13, Theme.Muted);
            artist.TextTrimming = TextTrimming.CharacterEllipsis;
            var info = UI.V(3, UI.Label(m.HasMedia ? (string.IsNullOrEmpty(m.AppName) ? "Now playing" : m.AppName) : "Now playing"), title, artist);
            info.Width = 172;
            info.VerticalAlignment = VerticalAlignment.Center;
            var top = UI.H(12, art, info);

            var prev = FlatButton.IconButton(Icons.Prev, "Previous", () => m.Previous());
            var play = FlatButton.IconButton(m.IsPlaying ? Icons.Pause : Icons.Play, m.IsPlaying ? "Pause" : "Play", () => m.PlayPause());
            play.Background = Theme.Text;
            play.Content = UI.GridIcon(m.IsPlaying ? Icons.Pause : Icons.Play, 16, Theme.Bg, 2.4);
            play.CornerRadius = new CornerRadius(22);
            var next = FlatButton.IconButton(Icons.Next, "Next", () => m.Next());
            prev.Background = next.Background = Brushes.Transparent;
            var controls = UI.H(18, prev, play, next);
            controls.HorizontalAlignment = HorizontalAlignment.Center;
            var v = UI.V(12, top, controls);
            v.Width = 248;
            return v;
        }

        private UIElement Notes()
        {
            var label = UI.Text("NOTE", 11, Theme.Brush("#6B5310"), FontWeights.Bold);
            var box = new TextBox
            {
                Text = Item.Text,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Theme.Brush("#2A2108"),
                CaretBrush = Theme.Brush("#2A2108"),
                FontSize = 15,
                FontWeight = FontWeights.Medium,
                MinHeight = 80,
                Width = 220,
                IsReadOnly = true,
                Cursor = Cursors.Arrow
            };
            box.MouseDoubleClick += (s, e) =>
            {
                box.IsReadOnly = false;
                box.Cursor = Cursors.IBeam;
                Activate();
                box.Focus();
                box.SelectAll();
            };
            box.LostKeyboardFocus += (s, e) =>
            {
                box.IsReadOnly = true;
                box.Cursor = Cursors.Arrow;
                Item.Text = box.Text;
                Settings.Save();
            };
            return UI.V(6, label, box);
        }

        private UIElement Countdown()
        {
            var target = Item.Date ?? DateTime.Today;
            int days = Math.Max(0, (int)(target.Date - DateTime.Today).TotalDays);
            var row = UI.H(8,
                UI.Text(days.ToString(), 40, Theme.Teal, FontWeights.SemiBold, true),
                UI.Text(days == 1 ? "day until" : days == 0 ? "It's today!" : "days until", 15, Theme.Muted));
            if (row.Children[1] is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
            var v = UI.V(4, UI.Label("Countdown"), row, UI.Text(string.IsNullOrEmpty(Item.Text) ? "Right-click to set" : Item.Text, 16, Theme.Text, FontWeights.SemiBold));
            v.Width = 220;
            return v;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Arrakis.Core;

namespace Arrakis.Modules
{
    public class DockModule : ModuleBase
    {
        public override string Id => "dock";
        public override string Name => "Dock";
        public override string Description => "A smooth dock for your apps with previews, folder stacks, five styles, six themes and three positions.";
        public override string IconData => Icons.Dock;
        public override string ShortcutText => "Always on";
        public override bool IsEnabled { get => Settings.Current.Dock.Enabled; set => Settings.Current.Dock.Enabled = value; }

        private readonly List<DockWindow> _windows = new List<DockWindow>();
        private DispatcherTimer _taskbarTimer;
        private DockSettings D => Settings.Current.Dock;

        protected override void OnStart()
        {
            if (string.IsNullOrEmpty(D.HoverEffect)) { D.HoverEffect = D.Magnify ? "Magnify" : "None"; Settings.Save(); }
            SeedPins();
            OpenWindows();
            ApplyTaskbar();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaysChanged;
        }

        protected override void OnStop()
        {
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaysChanged;
            _taskbarTimer?.Stop();
            TaskbarManager.Restore();
            CloseWindows();
        }

        private void OnDisplaysChanged(object sender, EventArgs e)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(async () =>
            {
                if (!IsRunning) return;
                // Screens were added, removed or resized: start from what Windows has, then apply again.
                TaskbarManager.Restore();
                CloseWindows();
                await Task.Delay(800);
                if (!IsRunning) return;
                OpenWindows();
                ApplyTaskbar();
            }));
        }

        private void OpenWindows()
        {
            foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            {
                if (!screen.Primary && !D.AllMonitors) continue;
                var w = new DockWindow(screen.DeviceName, screen.Primary);
                _windows.Add(w);
                w.Show();
            }
        }

        private void CloseWindows()
        {
            foreach (var w in _windows.ToList()) w.Close();
            _windows.Clear();
        }

        /// <summary>Hides the taskbar on screens that have a dock (if chosen), and keeps space for the dock (if chosen).</summary>
        public void ApplyTaskbar()
        {
            _taskbarTimer?.Stop();
            if (!IsRunning) { TaskbarManager.Restore(); return; }
            var screens = _windows.Select(w => new DockScreen { Monitor = w.Monitor, Edge = D.Position, ReservePx = D.ReserveSpace ? w.ReservePx : 0 }).ToList();
            if (D.HideTaskbar || D.ReserveSpace)
            {
                TaskbarManager.Apply(screens, D.HideTaskbar);
                _taskbarTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                _taskbarTimer.Tick += (s, e) => TaskbarManager.Enforce();
                _taskbarTimer.Start();
            }
            else TaskbarManager.Restore();
            foreach (var w in _windows) w.Reposition();
        }

        /// <summary>Brings the Windows taskbar back on every screen and turns off "Hide the Windows taskbar".</summary>
        public void PutTaskbarBack()
        {
            D.HideTaskbar = false;
            Settings.Save();
            if (IsRunning) ApplyTaskbar(); else TaskbarManager.Restore();
            TaskbarManager.EmergencyRestore();
            foreach (var w in _windows) w.Reposition();
        }

        private void ReopenWindows()
        {
            if (!IsRunning) return;
            CloseWindows();
            OpenWindows();
            ApplyTaskbar();
        }

        public void Refresh() { foreach (var w in _windows) w.Rebuild(true); }

        /// <summary>Applies look and position changes to every dock.</summary>
        public void ApplyLook()
        {
            foreach (var w in _windows) w.ApplyStyle();
            ApplyTaskbar();
        }

        private static void SeedPins()
        {
            var d = Settings.Current.Dock;
            if (!d.FoldersSeeded)
            {
                string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (Directory.Exists(downloads)) d.Folders.Add(downloads);
                d.FoldersSeeded = true;
                Settings.Save();
            }
            if (d.PinnedSeeded) return;
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var candidates = new[]
            {
                Path.Combine(win, "explorer.exe"),
                Path.Combine(pf86, @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(pf, @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(win, @"System32\notepad.exe")
            };
            foreach (var c in candidates) if (File.Exists(c)) d.Pinned.Add(c);
            d.PinnedSeeded = true;
            Settings.Save();
        }

        // ---------------------------------------------------------------- settings page
        public static readonly (string id, string name, string desc)[] Styles =
        {
            ("Floating", "Floating", "A rounded dock that floats just above the edge"),
            ("Pill", "Minimal pill", "No background until you hover"),
            ("Islands", "Islands", "Pinned, open and folders in separate groups"),
            ("Edge", "Edge bar", "Full width, flush to the edge, icons centred"),
            ("Compact", "Compact bar", "A slim full-width bar like the classic taskbar")
        };

        public static readonly (string id, string name)[] Themes =
        {
            ("Auto", "Match Arrakis"), ("Dark", "Dark glass"), ("Light", "Light glass"), ("Solid", "Solid"), ("Clear", "Clear"), ("Accent", "Accent")
        };

        public static readonly (string id, string name, string desc)[] Effects =
        {
            ("Magnify", "Magnify", "Icons grow and spread apart as you glide past"),
            ("Zoom", "Zoom", "The icon under the mouse grows in place"),
            ("Lift", "Lift", "Icons rise a little"),
            ("None", "None", "Nothing moves")
        };

        private static WrapPanel ChipRow(IEnumerable<(string id, string name)> options, string current, Action<string> pick)
        {
            var row = new WrapPanel();
            var chips = new List<Chip>();
            foreach (var (id, name) in options)
            {
                string v = id;
                var c = new Chip(name, id == current) { Margin = new Thickness(0, 0, 8, 8), Tag = id };
                c.Click += (s, e) => { foreach (var x in chips) x.Selected = (string)x.Tag == v; pick(v); };
                chips.Add(c);
                row.Children.Add(c);
            }
            return row;
        }

        public override UIElement BuildPage()
        {
            var d = D;
            if (string.IsNullOrEmpty(d.HoverEffect)) d.HoverEffect = d.Magnify ? "Magnify" : "None";

            var styleDesc = UI.Text(Styles.FirstOrDefault(x => x.id == d.Style).desc ?? "", 12, Theme.Muted);
            var style = ChipRow(Styles.Select(x => (x.id, x.name)), d.Style, v => { d.Style = v; styleDesc.Text = Styles.First(x => x.id == v).desc; Settings.Save(); ApplyLook(); });
            var position = ChipRow(new[] { ("Bottom", "Bottom"), ("Left", "Left"), ("Right", "Right") }, d.Position, v => { d.Position = v; Settings.Save(); ApplyLook(); });
            var theme = ChipRow(Themes, d.Theme, v => { d.Theme = v; Settings.Save(); ApplyLook(); });
            var opacityText = UI.Text(d.Opacity + "%", 13, Theme.Muted);
            var opacity = new MiniSlider(20, 100, d.Opacity, null, 200) { Step = 5 };
            opacity.ValueChanged += v => { d.Opacity = (int)v; opacityText.Text = (int)v + "%"; Settings.Save(); foreach (var w in _windows) w.ApplyColours(); };
            var size = new MiniSlider(32, 72, d.IconSize, null, 200) { Step = 2 };
            var sizeText = UI.Text(d.IconSize + " px", 13, Theme.Muted);
            size.ValueChanged += v => { d.IconSize = (int)v; sizeText.Text = (int)v + " px"; Settings.Save(); ApplyLook(); };
            var divider = new ToggleSwitch(d.ShowDivider, "Dividers");
            divider.Toggled += on => { d.ShowDivider = on; Settings.Save(); Refresh(); };

            var effectDesc = UI.Text(Effects.FirstOrDefault(x => x.id == d.HoverEffect).desc ?? "", 12, Theme.Muted);
            var effect = ChipRow(Effects.Select(x => (x.id, x.name)), d.HoverEffect, v => { d.HoverEffect = v; d.Magnify = v != "None"; effectDesc.Text = Effects.First(x => x.id == v).desc; Settings.Save(); ApplyLook(); });
            var amountText = UI.Text(d.MagnifyAmount + "%", 13, Theme.Muted);
            var amount = new MiniSlider(20, 100, d.MagnifyAmount, null, 200) { Step = 5 };
            amount.ValueChanged += v => { d.MagnifyAmount = (int)v; amountText.Text = (int)v + "%"; Settings.Save(); ApplyLook(); };
            var anims = new ToggleSwitch(d.Animations, "Smooth animations");
            anims.Toggled += on => { d.Animations = on; Settings.Save(); };
            var autohide = new ToggleSwitch(d.AutoHideWhenMaximised, "Get out of the way");
            autohide.Toggled += on => { d.AutoHideWhenMaximised = on; Settings.Save(); };

            var taskbar = new ToggleSwitch(d.HideTaskbar, "Hide the Windows taskbar");
            taskbar.Toggled += on => { d.HideTaskbar = on; Settings.Save(); ApplyTaskbar(); };
            var reserve = new ToggleSwitch(d.ReserveSpace, "Keep windows clear of the dock");
            reserve.Toggled += on => { d.ReserveSpace = on; Settings.Save(); ApplyTaskbar(); };
            var putBack = FlatButton.Secondary("Put the taskbar back", () => { PutTaskbarBack(); taskbar.IsOn = false; });
            var monitors = new ToggleSwitch(d.AllMonitors, "Dock on every screen");
            monitors.Toggled += on => { d.AllMonitors = on; Settings.Save(); ReopenWindows(); };

            var live = new ToggleSwitch(d.ShowLiveActivities, "Live activities");
            live.Toggled += on => { d.ShowLiveActivities = on; Settings.Save(); Refresh(); };
            var previews = new ToggleSwitch(d.Previews, "Window previews");
            previews.Toggled += on => { d.Previews = on; Settings.Save(); };
            var counts = new ToggleSwitch(d.WindowCounts, "Window counts");
            counts.Toggled += on => { d.WindowCounts = on; Settings.Save(); Refresh(); };

            // Pinned apps
            var pinned = UI.V(6);
            void RenderPins()
            {
                pinned.Children.Clear();
                foreach (var p in d.Pinned.ToList())
                {
                    var img = new Image { Source = IconCache.ForFile(p), Width = 24, Height = 24 };
                    var name = UI.Text(WindowTracker.FriendlyName(p), 14);
                    name.VerticalAlignment = VerticalAlignment.Center;
                    var remove = FlatButton.IconButton(Icons.Close, "Unpin " + WindowTracker.FriendlyName(p), () => { d.Pinned.Remove(p); Settings.Save(); Refresh(); RenderPins(); }, 36);
                    var row = new DockPanel { LastChildFill = true };
                    DockPanel.SetDock(remove, System.Windows.Controls.Dock.Right);
                    row.Children.Add(remove);
                    row.Children.Add(UI.H(12, img, name));
                    pinned.Children.Add(row);
                }
                if (d.Pinned.Count == 0) pinned.Children.Add(UI.Text("Nothing pinned yet. Right-click an open app in the dock and choose Pin.", 13, Theme.Muted));
            }
            RenderPins();
            var addBtn = FlatButton.Secondary("Pin a program...", () =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Programs (*.exe)|*.exe", Title = "Choose a program to pin" };
                if (dlg.ShowDialog() != true) return;
                d.Pinned.Add(dlg.FileName);
                Settings.Save();
                Refresh();
                RenderPins();
            });
            addBtn.HorizontalAlignment = HorizontalAlignment.Left;

            var folders = UI.V(6);
            void RenderFolders()
            {
                folders.Children.Clear();
                foreach (var f in d.Folders.ToList())
                {
                    var name = UI.Text(Path.GetFileName(f.TrimEnd('\\')) is string n && n.Length > 0 ? n : f, 14);
                    name.VerticalAlignment = VerticalAlignment.Center;
                    var remove = FlatButton.IconButton(Icons.Close, "Unpin folder", () => { d.Folders.Remove(f); Settings.Save(); Refresh(); RenderFolders(); }, 36);
                    var row = new DockPanel { LastChildFill = true };
                    DockPanel.SetDock(remove, System.Windows.Controls.Dock.Right);
                    row.Children.Add(remove);
                    row.Children.Add(UI.H(12, UI.GridIcon(Icons.Folder, 20, Theme.Amber), name, UI.Text(f, 12, Theme.Muted)));
                    folders.Children.Add(row);
                }
                if (d.Folders.Count == 0) folders.Children.Add(UI.Text("No folders yet. Drag a folder onto the dock, or add one here.", 13, Theme.Muted));
            }
            RenderFolders();
            var addFolder = FlatButton.Secondary("Pin a folder...", () =>
            {
                var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = "Choose a folder to pin to the dock" };
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                d.Folders.Add(dlg.SelectedPath);
                Settings.Save();
                Refresh();
                RenderFolders();
            });
            addFolder.HorizontalAlignment = HorizontalAlignment.Left;

            UIElement Block(string title, string desc, UIElement control) => UI.V(8, UI.Text(title, 15, Theme.Text, FontWeights.SemiBold), control, desc == null ? (UIElement)new Border() : UI.Text(desc, 12, Theme.Muted));

            return UI.V(16,
                UI.Label("Look"),
                UI.Card(UI.V(16,
                    UI.V(8, UI.Text("Style", 15, Theme.Text, FontWeights.SemiBold), style, styleDesc),
                    Block("Position", null, position),
                    Block("Theme", "Match Arrakis follows your light or dark setting and accent colour.", theme),
                    UI.Divider(),
                    UI.SettingRow("See-through", "How much of your wallpaper shows through the dock.", UI.H(10, opacity, opacityText)),
                    UI.SettingRow("Icon size", null, UI.H(10, size, sizeText)),
                    UI.SettingRow("Dividers", "A thin line between pinned apps, open apps and folders.", divider)), 20),
                UI.Label("Motion"),
                UI.Card(UI.V(16,
                    UI.V(8, UI.Text("When the mouse is over an icon", 15, Theme.Text, FontWeights.SemiBold), effect, effectDesc),
                    UI.Divider(),
                    UI.SettingRow("How much", "For Magnify and Zoom.", UI.H(10, amount, amountText)),
                    UI.SettingRow("Smooth animations", "Turn off and everything changes instantly, with no movement at all.", anims),
                    UI.SettingRow("Get out of the way", "Slide the dock away when a window is maximised. Move the mouse to the edge of the screen to bring it back.", autohide)), 20),
                UI.Label("Taskbar and screens"),
                UI.Card(UI.V(4,
                    UI.SettingRow("Hide the Windows taskbar", "Use the dock instead. It's hidden only on screens that have a dock, and comes back whenever Arrakis closes.", taskbar),
                    UI.SettingRow("Put the taskbar back", "Shows the Windows taskbar again on every screen, straight away.", putBack),
                    UI.SettingRow("A dock on every screen", "Each screen gets its own dock with the windows that are on it. With the taskbar hidden, it's hidden on those screens too.", monitors),
                    UI.SettingRow("Keep windows clear of the dock", "Maximised windows stop at the dock instead of going underneath it.", reserve)), 8),
                UI.Label("Features"),
                UI.Card(UI.V(4,
                    UI.SettingRow("Live activities", "Show what's playing and your focus timer next to the dock.", live),
                    UI.SettingRow("Window previews", "Rest the mouse on an open app to see its windows. Click one to switch to it.", previews),
                    UI.SettingRow("Window counts", "Show how many windows an app has open under its icon.", counts)), 8),
                UI.Label("Pinned apps"),
                UI.Card(UI.V(12, pinned, addBtn, UI.Text("Tip: drag a program or shortcut onto the dock to pin it.", 12, Theme.Muted))),
                UI.Label("Folder stacks"),
                UI.Card(UI.V(12, folders, addFolder, UI.Text("Click a folder in the dock to see its newest files.", 12, Theme.Muted))));
        }
    }

    internal class DockItem
    {
        public string Key;
        public string Path;
        public string Name;
        public bool Pinned;
        public List<AppWindow> Windows = new List<AppWindow>();
        public Border Tile;
        public Border Dot;
        public TextBlock Count;
        public bool IsFolder;
    }

    /// <summary>Colours for the dock's themes.</summary>
    internal static class DockLook
    {
        private static DockSettings S => Settings.Current.Dock;

        public static bool LightSurface => S.Theme == "Light" || ((S.Theme == "Auto" || S.Theme == "Solid") && Theme.IsLight);

        public static Brush Background(bool hovered = true)
        {
            byte a = (byte)Math.Round(255 * Math.Max(20, Math.Min(100, S.Opacity)) / 100.0);
            Color c;
            switch (S.Theme)
            {
                case "Dark": c = Color.FromArgb(a, 0x12, 0x14, 0x1A); break;
                case "Light": c = Color.FromArgb(a, 0xF8, 0xF7, 0xF4); break;
                case "Solid": c = Theme.PanelColor; break;
                case "Clear": c = Color.FromArgb((byte)(a / 4), 0, 0, 0); break;
                case "Accent":
                    var acc = Theme.AccentColor;
                    c = Color.FromArgb(a, (byte)(0x14 + (acc.R - 0x14) * 0.32), (byte)(0x16 + (acc.G - 0x16) * 0.32), (byte)(0x1C + (acc.B - 0x1C) * 0.32));
                    break;
                default: c = Theme.IsLight ? Color.FromArgb(a, 0xF8, 0xF7, 0xF4) : Color.FromArgb(a, 0x12, 0x14, 0x1A); break;
            }
            if (S.Style == "Pill" && !hovered) c = Color.FromArgb(1, 0, 0, 0);
            return Theme.Brush(c);
        }

        public static Brush Border => Theme.Brush(S.Theme == "Clear" ? Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF) : LightSurface ? Color.FromArgb(0x24, 0, 0, 0) : Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF));

        public static Brush Dot => Theme.Brush(LightSurface ? Color.FromArgb(0xC0, 0x20, 0x22, 0x28) : Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF));

        public static Brush DividerBrush => Theme.Brush(LightSurface ? Color.FromArgb(0x30, 0, 0, 0) : Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));

        public static System.Windows.Media.Effects.Effect Shadow => S.Theme == "Clear" || S.Style == "Pill" || S.Style == "Edge" || S.Style == "Compact" ? null : Theme.Shadow(22, 0.3, 6);
    }

    /// <summary>One visible icon slot, with its animation state.</summary>
    internal sealed class DockTile
    {
        public FrameworkElement Slot;
        public Border Tile;
        public ScaleTransform Scale;
        public TranslateTransform Lift;
        public double Center;     // along the dock, in un-magnified coordinates
        public double Cur;        // 0..1, how "hovered" it is right now
        public DockIsland Island;
    }

    internal sealed class DockIsland
    {
        public Grid Root;
        public Border Bg;
        public StackPanel Items;
        public double BaseLength;
        public readonly List<DockTile> Tiles = new List<DockTile>();
    }

    public class DockWindow : Window
    {
        private readonly Grid _root;
        private readonly Border _edge;
        private readonly StackPanel _row;
        private readonly StackPanel _live;
        private readonly TranslateTransform _slide = new TranslateTransform();
        private readonly DispatcherTimer _timer;
        private readonly DispatcherTimer _edgePoll;
        private readonly List<DockItem> _dockItems = new List<DockItem>();
        private readonly List<DockIsland> _islands = new List<DockIsland>();
        private readonly List<DockTile> _tiles = new List<DockTile>();
        private string _signature = "";
        private bool _hidden;
        private int _lastActive;
        private readonly string _device;
        private readonly bool _primary;
        private Native.RECT _full;
        private double _scale = 1;
        private DispatcherTimer _hoverTimer;
        private FrameworkElement _hoverTile;
        private bool _rendering;
        private bool _hovering;
        private TimeSpan _lastFrame;
        private bool _scanning;

        public IntPtr Monitor { get; private set; }
        public int ReservePx => (int)Math.Round(BarThickness * _scale);

        private DockSettings S => Settings.Current.Dock;
        private bool Vertical => S.Position == "Left" || S.Position == "Right";
        private bool FullLength => S.Style == "Edge" || S.Style == "Compact";
        private double BaseSize => S.Style == "Compact" ? Math.Min(S.IconSize, 34) : S.IconSize;
        private double Amount => Math.Max(0.2, Math.Min(1.0, S.MagnifyAmount / 100.0));

        /// <summary>How far the bar itself sticks out from the edge (without magnified icons).</summary>
        private double BarThickness => BaseSize + (S.Style == "Compact" ? 10 : 16) + 9 + (FullLength ? 0 : 10);

        /// <summary>Room for icons that grow out of the bar.</summary>
        private double Headroom => S.HoverEffect switch
        {
            "Magnify" => BaseSize * Amount + 12,
            "Zoom" => BaseSize * Amount * 0.55 + 12,
            "Lift" => BaseSize * 0.4 + 12,
            _ => 8
        };

        [System.Runtime.InteropServices.DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        public DockWindow(string device, bool primary)
        {
            _device = device;
            _primary = primary;
            Title = "Arrakis Dock";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            ShowActivated = false;
            FontFamily = Theme.Font;
            UseLayoutRounding = true;

            _root = new Grid();
            // A nearly invisible strip along the edge catches the mouse when the dock is hidden.
            _edge = new Border { Background = Theme.Brush(Color.FromArgb(1, 0, 0, 0)) };
            _edge.MouseEnter += (s, e) => SetHidden(false);
            _root.Children.Add(_edge);

            _live = new StackPanel();
            _row = new StackPanel { RenderTransform = _slide };
            _root.Children.Add(_row);
            Content = _root;

            ComputeMonitor();

            SourceInitialized += (s, e) => { WindowHelper.MakeToolWindow(this, true); Reposition(); };
            Loaded += (s, e) => ApplyStyle();

            MediaService.Instance.Changed += RebuildLive;
            App.Instance.Focus.Changed += RebuildLive;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _timer.Tick += async (s, e) => { await RefreshWindowsAsync(); CheckAutoHide(); };
            _timer.Start();
            _edgePoll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _edgePoll.Tick += (s, e) => { if (_hidden && IsMouseAtEdge()) SetHidden(false); };
            Closed += (s, e) =>
            {
                _timer.Stop();
                _edgePoll.Stop();
                StopRendering();
                MediaService.Instance.Changed -= RebuildLive;
                App.Instance.Focus.Changed -= RebuildLive;
            };
        }

        private void ComputeMonitor()
        {
            var screen = System.Windows.Forms.Screen.AllScreens.FirstOrDefault(sc => sc.DeviceName == _device) ?? System.Windows.Forms.Screen.PrimaryScreen;
            var b = screen.Bounds;
            _full = new Native.RECT { Left = b.Left, Top = b.Top, Right = b.Right, Bottom = b.Bottom };
            Monitor = Native.MonitorFromPoint(new Native.POINT { X = b.Left + b.Width / 2, Y = b.Top + b.Height / 2 }, Native.MONITOR_DEFAULTTONEAREST);
            _scale = 1;
            try { if (GetDpiForMonitor(Monitor, 0, out uint dx, out _) == 0) _scale = dx / 96.0; } catch { }
        }

        // ---------------------------------------------------------------- placement
        public void Reposition()
        {
            ComputeMonitor();
            var area = TaskbarManager.BaseArea(Monitor);
            double thickness = BarThickness + Headroom;
            double monLen = Vertical ? _full.Height / _scale : _full.Width / _scale;
            double length = monLen;
            if (!FullLength)
            {
                _row.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double content = Vertical ? _row.DesiredSize.Height : _row.DesiredSize.Width;
                double grow = S.HoverEffect == "Magnify" ? BaseSize * Amount * 3 : BaseSize;
                length = Math.Min(monLen, Math.Max(300, content + grow + 80));
            }
            int t = (int)Math.Round(thickness * _scale), l = (int)Math.Round(length * _scale);
            int x, y, w, h;
            switch (S.Position)
            {
                case "Left":
                    w = t; h = l; x = area.Left; y = _full.Top + (_full.Height - h) / 2; break;
                case "Right":
                    w = t; h = l; x = area.Right - t; y = _full.Top + (_full.Height - h) / 2; break;
                default:
                    w = l; h = t; x = _full.Left + (_full.Width - w) / 2; y = area.Bottom - t; break;
            }
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero) Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, x, y, w, h, Native.SWP_NOACTIVATE);
        }

        private void LayoutForEdge()
        {
            bool v = Vertical;
            // Full-length bars: the row runs across the dock so its one island stretches the whole length.
            _row.Orientation = FullLength ? (v ? Orientation.Horizontal : Orientation.Vertical) : (v ? Orientation.Vertical : Orientation.Horizontal);
            _live.Orientation = v ? Orientation.Vertical : Orientation.Horizontal;
            double m = FullLength ? 0 : 10;
            switch (S.Position)
            {
                case "Left":
                    _row.HorizontalAlignment = HorizontalAlignment.Left;
                    _row.VerticalAlignment = FullLength ? VerticalAlignment.Stretch : VerticalAlignment.Center;
                    _row.Margin = new Thickness(m, 0, 0, 0);
                    _edge.Width = 3; _edge.Height = double.NaN;
                    _edge.HorizontalAlignment = HorizontalAlignment.Left; _edge.VerticalAlignment = VerticalAlignment.Stretch;
                    break;
                case "Right":
                    _row.HorizontalAlignment = HorizontalAlignment.Right;
                    _row.VerticalAlignment = FullLength ? VerticalAlignment.Stretch : VerticalAlignment.Center;
                    _row.Margin = new Thickness(0, 0, m, 0);
                    _edge.Width = 3; _edge.Height = double.NaN;
                    _edge.HorizontalAlignment = HorizontalAlignment.Right; _edge.VerticalAlignment = VerticalAlignment.Stretch;
                    break;
                default:
                    _row.HorizontalAlignment = FullLength ? HorizontalAlignment.Stretch : HorizontalAlignment.Center;
                    _row.VerticalAlignment = VerticalAlignment.Bottom;
                    _row.Margin = new Thickness(0, 0, 0, m);
                    _edge.Height = 3; _edge.Width = double.NaN;
                    _edge.VerticalAlignment = VerticalAlignment.Bottom; _edge.HorizontalAlignment = HorizontalAlignment.Stretch;
                    break;
            }
        }

        public void ApplyStyle()
        {
            LayoutForEdge();
            _slide.BeginAnimation(TranslateTransform.XProperty, null);
            _slide.BeginAnimation(TranslateTransform.YProperty, null);
            _slide.X = _slide.Y = 0;
            _hidden = false;
            _signature = "";
            Rebuild(true);
            Reposition();
        }

        public void ApplyColours()
        {
            foreach (var isl in _islands)
            {
                isl.Bg.Background = DockLook.Background(_hovering);
                isl.Bg.BorderBrush = DockLook.Border;
            }
            foreach (var item in _dockItems) if (item.Dot != null) UpdateDot(item);
        }

        // ---------------------------------------------------------------- building
        private async Task RefreshWindowsAsync()
        {
            // Never scan windows mid-animation: it's the one thing that could cause a stutter.
            if (_rendering || _scanning) return;
            _scanning = true;
            try
            {
                var mon = Monitor;
                bool perScreen = S.AllMonitors;
                var windows = await Task.Run(() =>
                {
                    try
                    {
                        var all = WindowTracker.GetAppWindows();
                        return perScreen ? all.Where(w => w.Monitor == mon).ToList() : all;
                    }
                    catch { return null; }
                });
                if (windows != null && !_rendering) Apply(windows, false);
            }
            finally { _scanning = false; }
        }

        public void Rebuild(bool force)
        {
            List<AppWindow> windows;
            try
            {
                windows = WindowTracker.GetAppWindows();
                if (S.AllMonitors && Monitor != IntPtr.Zero) windows = windows.Where(w => w.Monitor == Monitor).ToList();
            }
            catch { return; }
            Apply(windows, force);
        }

        private void Apply(List<AppWindow> windows, bool force)
        {
            var items = new List<DockItem>();
            foreach (var p in S.Pinned)
                items.Add(new DockItem { Key = p.ToLowerInvariant(), Path = p, Name = WindowTracker.FriendlyName(p), Pinned = true });
            foreach (var f in S.Folders)
                items.Add(new DockItem { Key = "folder:" + f.ToLowerInvariant(), Path = f, Name = Path.GetFileName(f.TrimEnd('\\')), Pinned = true, IsFolder = true });
            foreach (var w in windows)
            {
                var existing = items.FirstOrDefault(i => i.Key == w.Key);
                if (existing == null)
                {
                    existing = new DockItem { Key = w.Key, Path = w.Path, Name = w.Key.StartsWith("uwp:") ? w.Title : WindowTracker.FriendlyName(w.Path) };
                    items.Add(existing);
                }
                existing.Windows.Add(w);
            }

            string sig = string.Join("|", items.Select(i => i.Key)) + "#" + S.IconSize + S.Style + S.Position + S.Theme + S.WindowCounts + S.ShowDivider + S.HoverEffect + S.MagnifyAmount + S.ShowLiveActivities;
            if (!force && sig == _signature)
            {
                for (int i = 0; i < items.Count && i < _dockItems.Count; i++)
                {
                    _dockItems[i].Windows = items[i].Windows;
                    UpdateDot(_dockItems[i]);
                }
                return;
            }
            _signature = sig;
            Build(items);
            Reposition();
        }

        private void Build(List<DockItem> items)
        {
            StopRendering();
            _row.Children.Clear();
            _islands.Clear();
            _tiles.Clear();
            _dockItems.Clear();
            _live.Children.Clear();
            if (_live.Parent is Panel lp) lp.Children.Remove(_live);

            var pinnedApps = items.Where(i => i.Pinned && !i.IsFolder).ToList();
            var folders = items.Where(i => i.IsFolder).ToList();
            var running = items.Where(i => !i.Pinned).ToList();
            // Keep _dockItems in the same order as `items` so window lists line up on refresh.
            _dockItems.AddRange(items);

            if (S.Style == "Islands")
            {
                var a = NewIsland(); AddStart(a); foreach (var i in pinnedApps) AddTile(a, i);
                if (running.Count > 0) { var b = NewIsland(); foreach (var i in running) AddTile(b, i); }
                if (folders.Count > 0) { var c = NewIsland(); foreach (var i in folders) AddTile(c, i); }
            }
            else
            {
                var isl = NewIsland();
                AddStart(isl);
                foreach (var i in pinnedApps) AddTile(isl, i);
                if (running.Count > 0)
                {
                    if (S.ShowDivider && pinnedApps.Count > 0) AddDivider(isl);
                    foreach (var i in running) AddTile(isl, i);
                }
                if (folders.Count > 0)
                {
                    if (S.ShowDivider) AddDivider(isl);
                    foreach (var i in folders) AddTile(isl, i);
                }
            }

            foreach (var isl in _islands)
            {
                double pos = 0;
                foreach (UIElement child in isl.Items.Children)
                {
                    double ext = ExtentOf(child);
                    var t = isl.Tiles.FirstOrDefault(x => x.Slot == child);
                    if (t != null) t.Center = pos + ext / 2;
                    pos += ext;
                }
                isl.BaseLength = pos;
            }

            // Live activities: inside the bar for full-length styles, next to it otherwise.
            if (FullLength && _islands.Count > 0)
            {
                var isl = _islands[0];
                isl.Root.Children.Remove(isl.Items);
                var dp = new DockPanel { LastChildFill = true };
                var dock = Vertical ? System.Windows.Controls.Dock.Bottom : System.Windows.Controls.Dock.Right;
                DockPanel.SetDock(_live, dock);
                dp.Children.Add(_live);
                dp.Children.Add(isl.Items);
                isl.Items.HorizontalAlignment = Vertical ? HorizontalAlignment.Stretch : (S.Style == "Compact" ? HorizontalAlignment.Left : HorizontalAlignment.Center);
                isl.Items.VerticalAlignment = Vertical ? (S.Style == "Compact" ? VerticalAlignment.Top : VerticalAlignment.Center) : VerticalAlignment.Bottom;
                isl.Root.Children.Add(dp);
            }
            else _row.Children.Add(_live);
            RebuildLive();
        }

        private double ExtentOf(UIElement child)
        {
            if (child is FrameworkElement fe && "divider".Equals(fe.Tag)) return 13;
            return BaseSize + 8;
        }

        private DockIsland NewIsland()
        {
            var isl = new DockIsland();
            double pad = S.Style == "Compact" ? 6 : 10;
            double edgePad = S.Style == "Compact" ? 4 : 6;
            var padding = S.Position switch
            {
                "Left" => new Thickness(edgePad, pad, pad, pad),
                "Right" => new Thickness(pad, pad, edgePad, pad),
                _ => new Thickness(pad, pad, pad, edgePad)
            };
            double radius = S.Style switch { "Edge" => 0, "Compact" => 0, "Pill" => 999, _ => 24 };
            var border = S.Style == "Edge" || S.Style == "Compact"
                ? S.Position switch { "Left" => new Thickness(0, 0, 1, 0), "Right" => new Thickness(1, 0, 0, 0), _ => new Thickness(0, 1, 0, 0) }
                : new Thickness(S.Style == "Pill" ? 0 : 1);
            isl.Bg = new Border
            {
                Background = DockLook.Background(false),
                BorderBrush = DockLook.Border,
                BorderThickness = border,
                CornerRadius = new CornerRadius(radius),
                Effect = DockLook.Shadow,
                SnapsToDevicePixels = true
            };
            isl.Items = new StackPanel
            {
                Orientation = Vertical ? Orientation.Vertical : Orientation.Horizontal,
                Margin = padding,
                HorizontalAlignment = Vertical ? HorizontalAlignment.Stretch : HorizontalAlignment.Center,
                VerticalAlignment = Vertical ? VerticalAlignment.Center : VerticalAlignment.Bottom
            };
            isl.Root = new Grid
            {
                Margin = _islands.Count == 0 ? new Thickness(0) : (Vertical ? new Thickness(0, 10, 0, 0) : new Thickness(10, 0, 0, 0)),
                VerticalAlignment = Vertical ? VerticalAlignment.Stretch : VerticalAlignment.Bottom,
                HorizontalAlignment = Vertical ? (S.Position == "Left" ? HorizontalAlignment.Left : HorizontalAlignment.Right) : HorizontalAlignment.Stretch
            };
            isl.Root.Children.Add(isl.Bg);
            isl.Root.Children.Add(isl.Items);
            isl.Root.MouseEnter += (s, e) => StartRendering();
            isl.Root.AllowDrop = true;
            isl.Root.DragOver += (s, e) =>
            {
                e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None;
                e.Handled = true;
            };
            isl.Root.Drop += (s, e) => OnDrop(e);
            _islands.Add(isl);
            _row.Children.Add(isl.Root);
            return isl;
        }

        private void AddDivider(DockIsland isl)
        {
            var line = new Border
            {
                Background = DockLook.DividerBrush,
                Tag = "divider",
                Width = Vertical ? BaseSize * 0.7 : 1,
                Height = Vertical ? 1 : BaseSize * 0.7,
                Margin = Vertical ? new Thickness(0, 6, 0, 6) : new Thickness(6, 0, 6, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = Vertical ? VerticalAlignment.Center : VerticalAlignment.Center
            };
            isl.Items.Children.Add(line);
        }

        /// <summary>Wraps a tile and its running dot in a slot that can grow along the dock.</summary>
        private DockTile Slot(DockIsland isl, Border tile, Border dot)
        {
            double size = BaseSize;
            var scale = new ScaleTransform(1, 1);
            var lift = new TranslateTransform();
            var group = new TransformGroup();
            group.Children.Add(scale);
            group.Children.Add(lift);
            tile.RenderTransform = group;
            tile.RenderTransformOrigin = S.Position switch { "Left" => new Point(0, 0.5), "Right" => new Point(1, 0.5), _ => new Point(0.5, 1) };

            var slot = new StackPanel { Tag = "tile" };
            switch (S.Position)
            {
                case "Left":
                    slot.Orientation = Orientation.Horizontal;
                    slot.Height = size + 8;
                    slot.Margin = new Thickness(0);
                    dot.Margin = new Thickness(0, 0, 5, 0);
                    dot.VerticalAlignment = VerticalAlignment.Center;
                    tile.VerticalAlignment = VerticalAlignment.Center;
                    slot.Children.Add(dot);
                    slot.Children.Add(tile);
                    break;
                case "Right":
                    slot.Orientation = Orientation.Horizontal;
                    slot.Height = size + 8;
                    slot.HorizontalAlignment = HorizontalAlignment.Right;
                    dot.Margin = new Thickness(5, 0, 0, 0);
                    dot.VerticalAlignment = VerticalAlignment.Center;
                    tile.VerticalAlignment = VerticalAlignment.Center;
                    slot.Children.Add(tile);
                    slot.Children.Add(dot);
                    break;
                default:
                    slot.Orientation = Orientation.Vertical;
                    slot.Width = size + 8;
                    slot.VerticalAlignment = VerticalAlignment.Bottom;
                    dot.Margin = new Thickness(0, 5, 0, 0);
                    dot.HorizontalAlignment = HorizontalAlignment.Center;
                    tile.HorizontalAlignment = HorizontalAlignment.Center;
                    slot.Children.Add(tile);
                    slot.Children.Add(dot);
                    break;
            }
            var t = new DockTile { Slot = slot, Tile = tile, Scale = scale, Lift = lift, Island = isl };
            isl.Items.Children.Add(slot);
            isl.Tiles.Add(t);
            _tiles.Add(t);
            return t;
        }

        private void AddStart(DockIsland isl)
        {
            double size = BaseSize;
            var img = new Image { Source = IconCache.AppIcon(), Width = size * 0.82, Height = size * 0.82 };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            var tile = new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(size * 0.27),
                Child = img,
                Background = Brushes.Transparent,
                ToolTip = "Start (right-click for Arrakis)",
                Cursor = Cursors.Hand
            };
            var dot = new Border { Width = 4, Height = 4, CornerRadius = new CornerRadius(2) };
            tile.MouseLeftButtonUp += (s, e) => Native.PressWinKey();
            tile.MouseRightButtonUp += (s, e) => { App.Instance.ShowHub(); e.Handled = true; };
            Slot(isl, tile, dot);
        }

        private void AddTile(DockIsland isl, DockItem item)
        {
            double size = BaseSize;
            ImageSource icon = item.IsFolder ? null : IconCache.ForFile(item.Path);
            UIElement content;
            if (icon != null)
            {
                var img = new Image { Source = icon, Width = size * 0.78, Height = size * 0.78 };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                content = img;
            }
            else if (item.IsFolder)
            {
                var g = UI.GridIcon(Icons.Folder, size * 0.5, Theme.Amber, 1.8);
                g.HorizontalAlignment = HorizontalAlignment.Center;
                g.VerticalAlignment = VerticalAlignment.Center;
                content = g;
            }
            else
            {
                var letter = UI.Text(string.IsNullOrEmpty(item.Name) ? "?" : item.Name.Substring(0, 1).ToUpperInvariant(), size * 0.42, Theme.Text, FontWeights.Bold, true);
                letter.HorizontalAlignment = HorizontalAlignment.Center;
                letter.VerticalAlignment = VerticalAlignment.Center;
                content = letter;
            }
            var tile = new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(size * 0.27),
                Background = icon != null ? Brushes.Transparent : item.IsFolder ? Theme.AmberTint : Theme.Off,
                Child = content,
                ToolTip = item.IsFolder ? item.Name + " (folder)" : item.Name,
                Cursor = Cursors.Hand
            };
            var count = new TextBlock { FontSize = 9.5, FontWeight = FontWeights.Bold, Foreground = DockLook.LightSurface ? Brushes.White : Theme.Bg, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Visibility = Visibility.Collapsed };
            var dot = new Border { Width = 4, Height = 4, CornerRadius = new CornerRadius(2), Child = count };
            item.Tile = tile;
            item.Dot = dot;
            item.Count = count;
            UpdateDot(item);
            tile.MouseLeftButtonUp += (s, e) =>
            {
                HidePreview();
                if (item.IsFolder) FolderStack.Show(item.Path, tile);
                else ActivateItem(item);
            };
            tile.MouseRightButtonUp += (s, e) => { HidePreview(); ShowMenu(item, tile); e.Handled = true; };
            tile.MouseEnter += (s, e) => StartHover(item, tile);
            tile.MouseLeave += (s, e) => { if (_hoverTile == tile) _hoverTimer?.Stop(); };
            Slot(isl, tile, dot);
        }

        private void UpdateDot(DockItem item)
        {
            if (item.Dot == null) return;
            int n = item.Windows.Count;
            item.Dot.Background = n > 0 ? DockLook.Dot : Brushes.Transparent;
            if (S.WindowCounts && n > 1)
            {
                item.Count.Text = n.ToString();
                item.Dot.Width = double.NaN;
                item.Dot.Height = 13;
                item.Dot.Padding = new Thickness(4, 0, 4, 0);
                item.Dot.CornerRadius = new CornerRadius(6.5);
                item.Count.Visibility = Visibility.Visible;
            }
            else
            {
                item.Dot.Width = 4;
                item.Dot.Height = 4;
                item.Dot.Padding = new Thickness(0);
                item.Dot.CornerRadius = new CornerRadius(2);
                item.Count.Visibility = Visibility.Collapsed;
            }
        }

        // ---------------------------------------------------------------- hover animation
        // Each frame, every icon eases towards how "hovered" it should be. Icons grow with render transforms
        // (drawn by the graphics card), so nothing else on the dock has to be measured or redrawn.
        private void StartRendering()
        {
            if (_rendering || S.HoverEffect == "None" && S.Style != "Pill") return;
            _rendering = true;
            _lastFrame = TimeSpan.Zero;
            CompositionTarget.Rendering += OnFrame;
        }

        private void StopRendering()
        {
            if (!_rendering) return;
            _rendering = false;
            CompositionTarget.Rendering -= OnFrame;
        }

        private void OnFrame(object sender, EventArgs e)
        {
            var now = (e as RenderingEventArgs)?.RenderingTime ?? TimeSpan.Zero;
            if (now == _lastFrame) return;   // WPF can call twice per frame
            double dt = _lastFrame == TimeSpan.Zero ? 1 / 60.0 : Math.Min(0.05, (now - _lastFrame).TotalSeconds);
            _lastFrame = now;
            double ease = S.Animations ? 1 - Math.Exp(-dt * 16) : 1;

            Native.GetCursorPos(out var cur);
            var screen = new Point(cur.X, cur.Y);
            string effect = S.HoverEffect;
            double size = BaseSize;
            double range = effect == "Zoom" ? size * 1.3 : size * 2.4;
            bool anyHover = false;
            bool settled = true;

            foreach (var isl in _islands)
            {
                Point p;
                try { p = isl.Items.PointFromScreen(screen); } catch { continue; }
                double along = Vertical ? p.Y : p.X;
                double across = Vertical ? p.X : p.Y;
                double len = Vertical ? isl.Items.ActualHeight : isl.Items.ActualWidth;
                double thick = Vertical ? isl.Items.ActualWidth : isl.Items.ActualHeight;
                // A band from the edge of the screen out past the grown icons, so the dock doesn't flicker between icons.
                double outward = S.Position == "Left" ? across - thick : S.Position == "Right" ? -across : -across;
                double inward = S.Position == "Left" ? -across : S.Position == "Right" ? across - thick : across - thick;
                bool inBand = outward <= (_hovering ? Headroom : 4) && inward <= 30 && along >= -size * 0.6 && along <= len + size * 0.6 && !_hidden;
                if (inBand) anyHover = true;
                // Map the mouse into un-magnified coordinates by its fraction along the dock.
                double baseAlong = len > 0 ? (along < 0 ? along : along > len ? isl.BaseLength + (along - len) : along / len * isl.BaseLength) : along;

                foreach (var t in isl.Tiles)
                {
                    double target = 0;
                    if (inBand && effect != "None")
                    {
                        double d = Math.Abs(baseAlong - t.Center);
                        target = d >= range ? 0 : (Math.Cos(d / range * Math.PI) + 1) / 2;
                    }
                    t.Cur += (target - t.Cur) * ease;
                    if (Math.Abs(target - t.Cur) < 0.002) t.Cur = target;
                    else settled = false;
                    ApplyTile(t, effect, size);
                }
            }

            if (anyHover != _hovering)
            {
                _hovering = anyHover;
                if (S.Style == "Pill") foreach (var isl in _islands) isl.Bg.Background = DockLook.Background(_hovering);
                if (!_hovering) HidePreviewSoon();
            }
            if (!anyHover && settled) StopRendering();
        }

        private void ApplyTile(DockTile t, string effect, double size)
        {
            double f = t.Cur;
            double scale = 1, lift = 0, slot = size + 8;
            switch (effect)
            {
                case "Magnify": scale = 1 + Amount * f; slot = size * (1 + Amount * f) + 8; break;
                case "Zoom": scale = 1 + Amount * 0.55 * f; break;
                case "Lift": scale = 1 + 0.08 * f; lift = size * 0.28 * f; break;
            }
            t.Scale.ScaleX = t.Scale.ScaleY = scale;
            switch (S.Position)
            {
                case "Left": t.Lift.X = lift; break;
                case "Right": t.Lift.X = -lift; break;
                default: t.Lift.Y = -lift; break;
            }
            if (Vertical) { if (Math.Abs(t.Slot.Height - slot) > 0.05) t.Slot.Height = slot; }
            else if (Math.Abs(t.Slot.Width - slot) > 0.05) t.Slot.Width = slot;
        }

        private void HidePreviewSoon()
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            timer.Tick += (s, e) => { timer.Stop(); if (!_hovering) WindowPreview.CloseIfNotHovered(); };
            timer.Start();
        }

        // ---------------------------------------------------------------- actions
        private void ActivateItem(DockItem item)
        {
            if (item.Windows.Count == 0)
            {
                if (!string.IsNullOrEmpty(item.Path) && !item.Key.StartsWith("uwp:")) Shell.Open(item.Path);
                return;
            }
            IntPtr fg = Native.GetForegroundWindow();
            if (item.Windows.Count == 1)
            {
                var h = item.Windows[0].Handle;
                if (h == fg && !Native.IsIconic(h)) Native.ShowWindow(h, Native.SW_MINIMIZE);
                else Native.ForceForeground(h);
                return;
            }
            int idx = item.Windows.FindIndex(w => w.Handle == fg);
            int next = idx >= 0 ? (idx + 1) % item.Windows.Count : (_lastActive % item.Windows.Count);
            _lastActive = next;
            Native.ForceForeground(item.Windows[next].Handle);
        }

        private System.Windows.Controls.Primitives.PlacementMode MenuPlacement => S.Position switch
        {
            "Left" => System.Windows.Controls.Primitives.PlacementMode.Right,
            "Right" => System.Windows.Controls.Primitives.PlacementMode.Left,
            _ => System.Windows.Controls.Primitives.PlacementMode.Top
        };

        private void ShowMenu(DockItem item, FrameworkElement target)
        {
            var menu = new ContextMenu();
            if (item.IsFolder)
            {
                var open = new MenuItem { Header = "Open folder" };
                open.Click += (s, e) => Shell.Open(item.Path);
                var unpin = new MenuItem { Header = "Unpin from dock" };
                unpin.Click += (s, e) =>
                {
                    S.Folders.RemoveAll(f => string.Equals(f, item.Path, StringComparison.OrdinalIgnoreCase));
                    Settings.Save();
                    App.Instance.Dock.Refresh();
                };
                menu.Items.Add(open);
                menu.Items.Add(unpin);
            }
            else
            {
                foreach (var w in item.Windows)
                {
                    var wi = new MenuItem { Header = w.Title.Length > 60 ? w.Title.Substring(0, 57) + "..." : w.Title };
                    var hw = w.Handle;
                    wi.Click += (s, e) => Native.ForceForeground(hw);
                    menu.Items.Add(wi);
                }
                if (item.Windows.Count > 0) menu.Items.Add(new Separator());
                if (!item.Key.StartsWith("uwp:") && !string.IsNullOrEmpty(item.Path))
                {
                    var open = new MenuItem { Header = item.Windows.Count > 0 ? "Open a new window" : "Open" };
                    open.Click += (s, e) => Shell.Open(item.Path);
                    menu.Items.Add(open);
                    var pin = new MenuItem { Header = item.Pinned ? "Unpin from dock" : "Pin to dock" };
                    pin.Click += (s, e) =>
                    {
                        if (item.Pinned) S.Pinned.RemoveAll(p => string.Equals(p, item.Path, StringComparison.OrdinalIgnoreCase));
                        else S.Pinned.Add(item.Path);
                        Settings.Save();
                        App.Instance.Dock.Refresh();
                    };
                    menu.Items.Add(pin);
                }
                if (item.Windows.Count > 0)
                {
                    var close = new MenuItem { Header = item.Windows.Count > 1 ? "Close all windows" : "Close window" };
                    close.Click += (s, e) => { foreach (var w in item.Windows) WindowTracker.Close(w.Handle); };
                    menu.Items.Add(close);
                }
            }
            menu.Items.Add(new Separator());
            var settings = new MenuItem { Header = "Dock settings..." };
            settings.Click += (s, e) => App.Instance.ShowHub("dock");
            menu.Items.Add(settings);
            menu.PlacementTarget = target;
            menu.Placement = MenuPlacement;
            menu.IsOpen = true;
        }

        private void StartHover(DockItem item, FrameworkElement tile)
        {
            _hoverTile = tile;
            if (!S.Previews || item.IsFolder || item.Windows.Count == 0) return;
            _hoverTimer?.Stop();
            _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            _hoverTimer.Tick += (s, e) =>
            {
                _hoverTimer.Stop();
                if (_hoverTile == tile && tile.IsMouseOver && item.Windows.Count > 0) WindowPreview.Show(item.Windows, tile);
            };
            _hoverTimer.Start();
        }

        private static void HidePreview() => WindowPreview.CloseCurrent();

        private void OnDrop(DragEventArgs e)
        {
            if (!(e.Data.GetData(DataFormats.FileDrop) is string[] files)) return;
            bool changed = false;
            foreach (var f in files)
            {
                if (Directory.Exists(f))
                {
                    if (!S.Folders.Contains(f, StringComparer.OrdinalIgnoreCase)) { S.Folders.Add(f); changed = true; }
                }
                else if (File.Exists(f))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if ((ext == ".exe" || ext == ".lnk" || ext == ".url" || ext == ".bat" || ext == ".cmd") && !S.Pinned.Contains(f, StringComparer.OrdinalIgnoreCase))
                    {
                        S.Pinned.Add(f);
                        changed = true;
                    }
                }
            }
            if (changed)
            {
                Settings.Save();
                App.Instance.Dock.Refresh();
            }
            e.Handled = true;
        }

        // ---------------------------------------------------------------- live activities
        private void RebuildLive()
        {
            _live.Children.Clear();
            if (!S.ShowLiveActivities || !_primary) { _live.Visibility = Visibility.Collapsed; return; }
            var media = MediaService.Instance;
            if (media.HasMedia) _live.Children.Add(Vertical ? MusicTile(media) : MusicCapsule(media));
            var focus = App.Instance.Focus;
            if (focus.IsActive) _live.Children.Add(Vertical ? FocusTile(focus) : FocusCapsule(focus));
            _live.Visibility = _live.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            _live.Margin = FullLength
                ? (Vertical ? new Thickness(0, 0, 0, 10) : new Thickness(0, 0, 10, 0))
                : S.Position switch { "Left" => new Thickness(0, 10, 0, 0), "Right" => new Thickness(0, 10, 0, 0), _ => new Thickness(12, 0, 0, 6) };
            _live.VerticalAlignment = Vertical ? VerticalAlignment.Top : VerticalAlignment.Center;
            _live.HorizontalAlignment = S.Position == "Left" ? HorizontalAlignment.Left : S.Position == "Right" ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        }

        private Border Capsule(UIElement content, bool square = false)
        {
            return new Border
            {
                Background = DockLook.Background(true),
                BorderBrush = DockLook.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(square ? 16 : 20),
                Padding = square ? new Thickness(8) : new Thickness(8, 6, 14, 6),
                Margin = Vertical ? new Thickness(0, 0, 0, 8) : new Thickness(0, 0, 8, 0),
                Child = content,
                Cursor = Cursors.Hand,
                Effect = FullLength ? null : Theme.Shadow(18, 0.28, 5)
            };
        }

        private Border MusicCapsule(MediaService m)
        {
            var art = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(9), Background = Theme.Off, ClipToBounds = true };
            if (m.Art != null) art.Background = new ImageBrush(m.Art) { Stretch = Stretch.UniformToFill };
            string title = m.Title.Length > 26 ? m.Title.Substring(0, 24) + "..." : m.Title;
            var text = UI.V(0, UI.Text(title, 13, Theme.Text, FontWeights.Bold), UI.Text(m.IsPlaying ? "Playing" : "Paused", 11, Theme.Muted));
            text.VerticalAlignment = VerticalAlignment.Center;
            var bars = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
            foreach (var h in new[] { 9.0, 16.0, 12.0 })
                bars.Children.Add(new Border { Width = 3, Height = m.IsPlaying ? h : 4, CornerRadius = new CornerRadius(1.5), Background = Theme.Amber, Margin = new Thickness(1.5, 0, 1.5, 0), VerticalAlignment = VerticalAlignment.Bottom });
            var cap = Capsule(UI.H(10, art, text, bars));
            cap.ToolTip = (m.Artist ?? "") + (string.IsNullOrEmpty(m.AppName) ? "" : " · " + m.AppName) + "\nClick to play or pause";
            cap.MouseLeftButtonUp += (s, e) => m.PlayPause();
            return cap;
        }

        private Border MusicTile(MediaService m)
        {
            var art = new Border { Width = BaseSize * 0.8, Height = BaseSize * 0.8, CornerRadius = new CornerRadius(10), Background = Theme.Off, ClipToBounds = true };
            if (m.Art != null) art.Background = new ImageBrush(m.Art) { Stretch = Stretch.UniformToFill };
            else art.Child = UI.GridIcon(Icons.Music, 18, Theme.Amber);
            var cap = Capsule(art, true);
            cap.ToolTip = m.Title + (string.IsNullOrEmpty(m.Artist) ? "" : "\n" + m.Artist) + "\nClick to play or pause";
            cap.MouseLeftButtonUp += (s, e) => m.PlayPause();
            return cap;
        }

        private Border FocusCapsule(FocusModule f)
        {
            var ring = new Ring(26, 3, Theme.Amber);
            ring.SetValue(f.Fraction);
            var text = UI.V(0, UI.Text(f.RemainingText, 14, Theme.Text, FontWeights.SemiBold, true), UI.Text(f.PhaseLabel, 11, Theme.Muted));
            text.VerticalAlignment = VerticalAlignment.Center;
            var cap = Capsule(UI.H(10, ring, text));
            cap.ToolTip = "Click to pause or resume";
            cap.MouseLeftButtonUp += (s, e) => f.ToggleFromHotkey();
            return cap;
        }

        private Border FocusTile(FocusModule f)
        {
            var ring = new Ring(BaseSize * 0.8, 3, Theme.Amber, f.RemainingText.Split(':')[0], 11);
            ring.SetValue(f.Fraction);
            var cap = Capsule(ring, true);
            cap.ToolTip = f.PhaseLabel + ", " + f.RemainingText + " left. Click to pause or resume";
            cap.MouseLeftButtonUp += (s, e) => f.ToggleFromHotkey();
            return cap;
        }

        // ---------------------------------------------------------------- auto-hide
        private void CheckAutoHide()
        {
            bool hide = false;
            var focus = App.Instance.Focus;
            if (focus.IsActive && focus.IsFocusPhase && Settings.Current.Focus.HideDock) hide = true;
            if (!hide && S.AutoHideWhenMaximised && !S.ReserveSpace)
            {
                IntPtr fg = Native.GetForegroundWindow();
                IntPtr me = new WindowInteropHelper(this).Handle;
                if (fg != IntPtr.Zero && fg != me && Native.MonitorFromWindow(fg, Native.MONITOR_DEFAULTTONEAREST) == Monitor)
                {
                    bool covers = false;
                    if (Native.IsZoomed(fg)) covers = true;
                    else if (Native.GetWindowRect(fg, out var r))
                    {
                        var sb = new System.Text.StringBuilder(64);
                        Native.GetClassName(fg, sb, sb.Capacity);
                        string cls = sb.ToString();
                        bool desktop = cls == "Progman" || cls == "WorkerW";
                        if (!desktop && r.Left <= _full.Left && r.Top <= _full.Top && r.Right >= _full.Right && r.Bottom >= _full.Bottom) covers = true;
                    }
                    hide = covers;
                }
            }
            if (hide && (_hovering || IsMouseOver)) hide = false;
            if (hide && !_hidden && IsMouseAtEdge()) hide = false;
            SetHidden(hide);
        }

        private bool IsMouseAtEdge()
        {
            Native.GetCursorPos(out var pt);
            if (pt.X < _full.Left || pt.X >= _full.Right || pt.Y < _full.Top || pt.Y >= _full.Bottom) return false;
            return S.Position switch
            {
                "Left" => pt.X <= _full.Left + 2,
                "Right" => pt.X >= _full.Right - 3,
                _ => pt.Y >= _full.Bottom - 3
            };
        }

        private void SetHidden(bool hide)
        {
            if (hide == _hidden) return;
            _hidden = hide;
            if (hide) _edgePoll.Start(); else _edgePoll.Stop();
            double dist = (BarThickness + Headroom) + 10;
            var prop = Vertical ? TranslateTransform.XProperty : TranslateTransform.YProperty;
            double to = !hide ? 0 : S.Position == "Left" ? -dist : dist;
            if (!S.Animations)
            {
                _slide.BeginAnimation(prop, null);
                if (Vertical) _slide.X = to; else _slide.Y = to;
                return;
            }
            var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(hide ? 220 : 180))
            {
                EasingFunction = new CubicEase { EasingMode = hide ? EasingMode.EaseIn : EasingMode.EaseOut }
            };
            _slide.BeginAnimation(prop, anim);
        }
    }
}

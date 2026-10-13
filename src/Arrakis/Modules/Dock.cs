using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        public override string Description => "A floating dock with your apps, plus live music and focus timer.";
        public override string IconData => Icons.Dock;
        public override string ShortcutText => "Always on";
        public override bool IsEnabled { get => Settings.Current.Dock.Enabled; set => Settings.Current.Dock.Enabled = value; }

        private DockWindow _window;
        private DispatcherTimer _taskbarTimer;

        protected override void OnStart()
        {
            SeedPins();
            _window = new DockWindow();
            _window.Show();
            ApplyTaskbar();
        }

        protected override void OnStop()
        {
            _taskbarTimer?.Stop();
            TaskbarManager.Restore();
            _window?.Close();
            _window = null;
        }

        public void ApplyTaskbar()
        {
            _taskbarTimer?.Stop();
            if (IsRunning || _window != null)
            {
                if (Settings.Current.Dock.HideTaskbar)
                {
                    TaskbarManager.Hide();
                    _taskbarTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                    _taskbarTimer.Tick += (s, e) => TaskbarManager.Enforce();
                    _taskbarTimer.Start();
                }
                else TaskbarManager.Restore();
            }
            _window?.Reposition();
        }

        public void Refresh() => _window?.Rebuild(true);

        private static void SeedPins()
        {
            var d = Settings.Current.Dock;
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

        public override UIElement BuildPage()
        {
            var d = Settings.Current.Dock;
            var style = UI.Combo(new[] { "Floating glass", "Minimal pill", "Edge bar" }, StyleLabel(d.Style), v =>
            {
                d.Style = v == "Minimal pill" ? "Pill" : v == "Edge bar" ? "Edge" : "Floating";
                Settings.Save();
                _window?.ApplyStyle();
            });

            var taskbar = new ToggleSwitch(d.HideTaskbar, "Hide the Windows taskbar");
            taskbar.Toggled += on => { d.HideTaskbar = on; Settings.Save(); ApplyTaskbar(); };

            var autohide = new ToggleSwitch(d.AutoHideWhenMaximised, "Auto-hide");
            autohide.Toggled += on => { d.AutoHideWhenMaximised = on; Settings.Save(); };

            var magnify = new ToggleSwitch(d.Magnify, "Magnify");
            magnify.Toggled += on => { d.Magnify = on; Settings.Save(); };

            var live = new ToggleSwitch(d.ShowLiveActivities, "Live activities");
            live.Toggled += on => { d.ShowLiveActivities = on; Settings.Save(); _window?.Rebuild(true); };

            var size = new MiniSlider(36, 64, d.IconSize, null, 200) { Step = 2 };
            size.ValueChanged += v => { d.IconSize = (int)v; Settings.Save(); _window?.Rebuild(true); };

            var pinned = UI.V(6);
            void RenderPins()
            {
                pinned.Children.Clear();
                foreach (var p in d.Pinned.ToList())
                {
                    var img = new Image { Source = IconCache.ForFile(p), Width = 24, Height = 24 };
                    var name = UI.Text(WindowTracker.FriendlyName(p), 14);
                    name.VerticalAlignment = VerticalAlignment.Center;
                    var remove = FlatButton.IconButton(Icons.Close, "Unpin " + WindowTracker.FriendlyName(p), () =>
                    {
                        d.Pinned.Remove(p);
                        Settings.Save();
                        Refresh();
                        RenderPins();
                    }, 36);
                    var row = new DockPanel { LastChildFill = true };
                    DockPanel.SetDock(remove, System.Windows.Controls.Dock.Right);
                    row.Children.Add(remove);
                    var left = UI.H(12, img, name);
                    row.Children.Add(left);
                    pinned.Children.Add(row);
                }
                if (d.Pinned.Count == 0) pinned.Children.Add(UI.Text("Nothing pinned yet. Right-click an open app in the dock and choose Pin.", 13, Theme.Muted));
            }
            RenderPins();

            var addBtn = FlatButton.Secondary("Pin a program...", () =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Programs (*.exe)|*.exe", Title = "Choose a program to pin" };
                if (dlg.ShowDialog() == true)
                {
                    d.Pinned.Add(dlg.FileName);
                    Settings.Save();
                    Refresh();
                    RenderPins();
                }
            });
            addBtn.HorizontalAlignment = HorizontalAlignment.Left;

            return UI.V(16,
                UI.Card(UI.V(0,
                    UI.SettingRow("Style", "Floating glass magnifies on hover. Minimal pill only shows a background when you hover. Edge bar runs the full width like the classic taskbar.", style),
                    UI.Divider(),
                    UI.SettingRow("Hide the Windows taskbar", "Use the dock instead of the taskbar. The taskbar always comes back when Arrakis exits, or from Start > Arrakis > Restore Windows taskbar.", taskbar),
                    UI.Divider(),
                    UI.SettingRow("Get out of the way", "Slide the dock away when a window is maximised. Move the mouse to the bottom edge to bring it back.", autohide),
                    UI.Divider(),
                    UI.SettingRow("Magnify on hover", null, magnify),
                    UI.Divider(),
                    UI.SettingRow("Live activities", "Show what's playing and your focus timer next to the dock.", live),
                    UI.Divider(),
                    UI.SettingRow("Icon size", null, size)), 8),
                UI.Label("Pinned apps"),
                UI.Card(UI.V(12, pinned, addBtn)));
        }

        private static string StyleLabel(string s) => s == "Pill" ? "Minimal pill" : s == "Edge" ? "Edge bar" : "Floating glass";
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
    }

    public class DockWindow : Window
    {
        private readonly Grid _root;
        private readonly StackPanel _row;
        private readonly Border _panel;
        private readonly StackPanel _items;
        private readonly StackPanel _live;
        private readonly TranslateTransform _slide = new TranslateTransform();
        private readonly DispatcherTimer _timer;
        private readonly List<DockItem> _dockItems = new List<DockItem>();
        private string _signature = "";
        private bool _hidden;
        private int _lastActive;
        private const double WindowHeight = 170;

        private DockSettings S => Settings.Current.Dock;

        public DockWindow()
        {
            Title = "Arrakis Dock";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            ShowActivated = false;
            FontFamily = Theme.Font;

            _root = new Grid();
            // A nearly invisible strip along the bottom edge catches the mouse when the dock is hidden.
            var edge = new Border { Height = 3, VerticalAlignment = VerticalAlignment.Bottom, Background = Theme.Brush(Color.FromArgb(1, 0, 0, 0)) };
            edge.MouseEnter += (s, e) => SetHidden(false);
            _root.Children.Add(edge);

            _items = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
            _panel = new Border { Child = _items, VerticalAlignment = VerticalAlignment.Bottom, Padding = new Thickness(10, 10, 10, 6) };
            _live = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(12, 0, 0, 8) };
            _row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 10),
                RenderTransform = _slide
            };
            _row.Children.Add(_panel);
            _row.Children.Add(_live);
            _root.Children.Add(_row);
            Content = _root;

            _items.MouseMove += (s, e) => Magnify(e.GetPosition(_items).X);
            _items.MouseLeave += (s, e) => ResetSizes();
            _panel.MouseEnter += (s, e) => { if (S.Style == "Pill") _panel.Background = Theme.Glass; };
            _panel.MouseLeave += (s, e) => { if (S.Style == "Pill") _panel.Background = Theme.Brush(Color.FromArgb(0x01, 0, 0, 0)); };

            SourceInitialized += (s, e) => WindowHelper.MakeToolWindow(this, true);
            Loaded += (s, e) => { Reposition(); ApplyStyle(); Rebuild(true); };

            MediaService.Instance.Changed += RebuildLive;
            App.Instance.Focus.Changed += RebuildLive;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _timer.Tick += (s, e) => { Rebuild(false); CheckAutoHide(); };
            _timer.Start();
            Closed += (s, e) =>
            {
                _timer.Stop();
                MediaService.Instance.Changed -= RebuildLive;
                App.Instance.Focus.Changed -= RebuildLive;
            };
        }

        public void Reposition()
        {
            var mon = Native.GetPrimaryMonitor();
            double scale = WindowHelper.DpiScale(this);
            var full = mon.rcMonitor;
            var work = TaskbarManager.IsHidden ? full : mon.rcWork;
            Left = full.Left / scale;
            Width = full.Width / scale;
            Height = WindowHeight;
            Top = work.Bottom / scale - WindowHeight;
        }

        public void ApplyStyle()
        {
            switch (S.Style)
            {
                case "Pill":
                    _panel.Background = Theme.Brush(Color.FromArgb(0x01, 0, 0, 0));
                    _panel.BorderThickness = new Thickness(0);
                    _panel.CornerRadius = new CornerRadius(30);
                    _panel.Effect = null;
                    _row.Margin = new Thickness(0, 0, 0, 10);
                    break;
                case "Edge":
                    _panel.Background = Theme.Brush(Color.FromArgb(0xEE, 0x12, 0x14, 0x1A));
                    _panel.BorderBrush = Theme.GlassBorder;
                    _panel.BorderThickness = new Thickness(0, 1, 0, 0);
                    _panel.CornerRadius = new CornerRadius(0);
                    _panel.Effect = null;
                    _row.Margin = new Thickness(0);
                    break;
                default:
                    _panel.Background = Theme.Glass;
                    _panel.BorderBrush = Theme.GlassBorder;
                    _panel.BorderThickness = new Thickness(1);
                    _panel.CornerRadius = new CornerRadius(24);
                    _panel.Effect = Theme.Shadow(30, 0.35, 8);
                    _row.Margin = new Thickness(0, 0, 0, 10);
                    break;
            }
            if (S.Style == "Edge")
            {
                _row.HorizontalAlignment = HorizontalAlignment.Stretch;
                _items.HorizontalAlignment = HorizontalAlignment.Center;
                _panel.Width = Width;
                _row.Children.Remove(_live);
                _items.Children.Remove(_live);
            }
            else
            {
                _row.HorizontalAlignment = HorizontalAlignment.Center;
                _panel.Width = double.NaN;
                _items.Children.Remove(_live);
                _live.Margin = new Thickness(12, 0, 0, 8);
                if (!_row.Children.Contains(_live)) _row.Children.Add(_live);
            }
            _signature = "";
            Rebuild(true);
        }

        /// <summary>Rebuilds the icons when the set of apps changes, otherwise just updates the running dots.</summary>
        public void Rebuild(bool force)
        {
            List<AppWindow> windows;
            try { windows = WindowTracker.GetAppWindows(); }
            catch { return; }

            var items = new List<DockItem>();
            foreach (var p in S.Pinned)
                items.Add(new DockItem { Key = p.ToLowerInvariant(), Path = p, Name = WindowTracker.FriendlyName(p), Pinned = true });
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

            string sig = string.Join("|", items.Select(i => i.Key)) + "#" + S.IconSize + S.Style;
            if (!force && sig == _signature)
            {
                // Same apps: refresh window lists and the running dots.
                for (int i = 0; i < items.Count && i < _dockItems.Count; i++)
                {
                    _dockItems[i].Windows = items[i].Windows;
                    _dockItems[i].Dot.Background = items[i].Windows.Count > 0 ? Theme.Brush(Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF)) : Brushes.Transparent;
                }
                return;
            }
            _signature = sig;
            _dockItems.Clear();
            _items.Children.Clear();
            _items.Children.Add(BuildStartTile());
            foreach (var item in items)
            {
                _items.Children.Add(BuildTile(item));
                _dockItems.Add(item);
            }
            if (S.Style == "Edge" && S.ShowLiveActivities)
            {
                _items.Children.Remove(_live);
                _live.Margin = new Thickness(16, 0, 0, 4);
                _items.Children.Add(_live);
            }
            RebuildLive();
        }

        private FrameworkElement BuildStartTile()
        {
            double size = S.IconSize;
            var img = new Image { Source = IconCache.AppIcon(), Width = size * 0.82, Height = size * 0.82 };
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
            var dot = new Border { Width = 4, Height = 4, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 5, 0, 0) };
            var stack = new StackPanel { Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Bottom, Tag = "tile" };
            stack.Children.Add(tile);
            stack.Children.Add(dot);
            tile.MouseLeftButtonUp += (s, e) => Native.PressWinKey();
            tile.MouseRightButtonUp += (s, e) => { App.Instance.ShowHub(); e.Handled = true; };
            tile.Tag = "start";
            return stack;
        }

        private FrameworkElement BuildTile(DockItem item)
        {
            double size = S.IconSize;
            ImageSource icon = IconCache.ForFile(item.Path);
            UIElement content;
            if (icon != null)
                content = new Image { Source = icon, Width = size * 0.78, Height = size * 0.78 };
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
                Background = icon != null ? Brushes.Transparent : Theme.Off,
                Child = content,
                ToolTip = item.Name,
                Cursor = Cursors.Hand
            };
            RenderOptions.SetBitmapScalingMode(tile, BitmapScalingMode.HighQuality);
            var dot = new Border
            {
                Width = 4,
                Height = 4,
                CornerRadius = new CornerRadius(2),
                Margin = new Thickness(0, 5, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Background = item.Windows.Count > 0 ? Theme.Brush(Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF)) : Brushes.Transparent
            };
            item.Tile = tile;
            item.Dot = dot;
            var stack = new StackPanel { Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Bottom, Tag = "tile" };
            stack.Children.Add(tile);
            stack.Children.Add(dot);
            tile.MouseLeftButtonUp += (s, e) => Activate(item);
            tile.MouseRightButtonUp += (s, e) => { ShowMenu(item, tile); e.Handled = true; };
            return stack;
        }

        private void Activate(DockItem item)
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
            // Several windows: cycle through them.
            int idx = item.Windows.FindIndex(w => w.Handle == fg);
            int next = idx >= 0 ? (idx + 1) % item.Windows.Count : (_lastActive % item.Windows.Count);
            _lastActive = next;
            Native.ForceForeground(item.Windows[next].Handle);
        }

        private void ShowMenu(DockItem item, FrameworkElement target)
        {
            var menu = new ContextMenu();
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
                    Rebuild(true);
                };
                menu.Items.Add(pin);
            }
            if (item.Windows.Count > 0)
            {
                var close = new MenuItem { Header = item.Windows.Count > 1 ? "Close all windows" : "Close window" };
                close.Click += (s, e) => { foreach (var w in item.Windows) WindowTracker.Close(w.Handle); };
                menu.Items.Add(close);
            }
            menu.PlacementTarget = target;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
            menu.IsOpen = true;
        }

        private void Magnify(double mouseX)
        {
            if (!S.Magnify) return;
            double baseSize = S.IconSize;
            double range = baseSize * 2.6;
            foreach (UIElement child in _items.Children)
            {
                if (!(child is StackPanel sp) || !"tile".Equals(sp.Tag) || sp.Children.Count == 0 || !(sp.Children[0] is Border tile)) continue;
                var center = sp.TranslatePoint(new Point(sp.ActualWidth / 2, 0), _items).X;
                double d = Math.Abs(mouseX - center);
                double f = d >= range ? 0 : (Math.Cos(d / range * Math.PI) + 1) / 2;
                double size = baseSize + baseSize * 0.6 * f;
                tile.Width = size;
                tile.Height = size;
                tile.CornerRadius = new CornerRadius(size * 0.27);
                if (tile.Child is FrameworkElement fe)
                {
                    fe.Width = size * 0.78;
                    fe.Height = size * 0.78;
                }
            }
        }

        private void ResetSizes()
        {
            double size = S.IconSize;
            foreach (UIElement child in _items.Children)
            {
                if (!(child is StackPanel sp) || !"tile".Equals(sp.Tag) || sp.Children.Count == 0 || !(sp.Children[0] is Border tile)) continue;
                tile.Width = size;
                tile.Height = size;
                tile.CornerRadius = new CornerRadius(size * 0.27);
                if (tile.Child is FrameworkElement fe)
                {
                    fe.Width = size * 0.78;
                    fe.Height = size * 0.78;
                }
            }
        }

        private void RebuildLive()
        {
            _live.Children.Clear();
            if (!S.ShowLiveActivities) return;
            var media = MediaService.Instance;
            if (media.HasMedia) _live.Children.Add(MusicCapsule(media));
            var focus = App.Instance.Focus;
            if (focus.IsActive) _live.Children.Add(FocusCapsule(focus));
            _live.Visibility = _live.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private Border Capsule(UIElement content)
        {
            return new Border
            {
                Background = Theme.Glass,
                BorderBrush = Theme.GlassBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(8, 6, 14, 6),
                Margin = new Thickness(0, 0, 8, 0),
                Child = content,
                Cursor = Cursors.Hand,
                Effect = Theme.Shadow(20, 0.3, 6)
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
            var row = UI.H(10, art, text, bars);
            var cap = Capsule(row);
            cap.ToolTip = (m.Artist ?? "") + (string.IsNullOrEmpty(m.AppName) ? "" : " · " + m.AppName) + "\nClick to play or pause";
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

        private void CheckAutoHide()
        {
            bool hide = false;
            var focus = App.Instance.Focus;
            if (focus.IsActive && focus.IsFocusPhase && Settings.Current.Focus.HideDock) hide = true;
            if (!hide && S.AutoHideWhenMaximised)
            {
                IntPtr fg = Native.GetForegroundWindow();
                IntPtr me = new WindowInteropHelper(this).Handle;
                if (fg != IntPtr.Zero && fg != me)
                {
                    bool covers = false;
                    if (Native.IsZoomed(fg)) covers = true;
                    else if (Native.GetWindowRect(fg, out var r))
                    {
                        var mon = Native.GetPrimaryMonitor().rcMonitor;
                        var sb = new System.Text.StringBuilder(64);
                        Native.GetClassName(fg, sb, sb.Capacity);
                        string cls = sb.ToString();
                        bool desktop = cls == "Progman" || cls == "WorkerW";
                        if (!desktop && r.Left <= mon.Left && r.Top <= mon.Top && r.Right >= mon.Right && r.Bottom >= mon.Bottom) covers = true;
                    }
                    hide = covers;
                }
            }
            if (hide && IsMouseOver && _row.IsMouseOver) hide = false;
            if (hide && _hidden == false && IsMouseNearBottom()) hide = false;
            SetHidden(hide);
        }

        private bool IsMouseNearBottom()
        {
            Native.GetCursorPos(out var pt);
            var mon = Native.GetPrimaryMonitor().rcMonitor;
            return pt.Y >= mon.Bottom - 4 && pt.X >= mon.Left && pt.X <= mon.Right;
        }

        private void SetHidden(bool hide)
        {
            if (hide == _hidden) return;
            _hidden = hide;
            var anim = new DoubleAnimation(hide ? WindowHeight : 0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            _slide.BeginAnimation(TranslateTransform.YProperty, anim);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Arrakis.Core;
using Path = System.Windows.Shapes.Path;

namespace Arrakis.Modules
{
    public enum CaptureMode { Region, Window, FullScreen, AllScreens, LastRegion }

    /// <summary>A finished capture, before it goes to the editor, the clipboard or a file.</summary>
    public class CaptureResult
    {
        public BitmapSource Image;
        public string Title = "";
    }

    public class CaptureModule : ModuleBase
    {
        public override string Id => "capture";
        public override string Name => "Capture";
        public override string Description => "Screenshots of an area, a window or the whole screen, with a full editor for arrows, blur, steps and effects.";
        public override string IconData => Icons.Capture;
        public override string ShortcutText => Settings.Current.Capture.Hotkey;
        public override bool IsEnabled { get => Settings.Current.Capture.Enabled; set => Settings.Current.Capture.Enabled = value; }

        private CaptureSettings S => Settings.Current.Capture;
        private bool _busy;

        protected override void OnStart() => RegisterKeys();

        protected override void OnStop()
        {
            foreach (var n in new[] { "capture", "capture-window", "capture-full", "prt", "prt-alt", "prt-ctrl", "prt-shift" })
                Hotkeys.Unregister(n);
        }

        private void RegisterKeys()
        {
            var failed = new List<string>();
            if (!Hotkeys.Register("capture", S.Hotkey, StartCapture)) failed.Add(S.Hotkey);
            if (!Hotkeys.Register("capture-window", S.WindowHotkey, () => Capture(CaptureMode.Window))) failed.Add(S.WindowHotkey);
            if (!Hotkeys.Register("capture-full", S.FullHotkey, () => Capture(CaptureMode.FullScreen))) failed.Add(S.FullHotkey);
            if (S.UsePrintScreen)
            {
                Hotkeys.Register("prt", "PrintScreen", StartCapture);
                Hotkeys.Register("prt-alt", "Alt+PrintScreen", () => Capture(CaptureMode.Window));
                Hotkeys.Register("prt-ctrl", "Ctrl+PrintScreen", () => Capture(CaptureMode.FullScreen));
                Hotkeys.Register("prt-shift", "Shift+PrintScreen", () => Capture(CaptureMode.LastRegion));
            }
            else
            {
                foreach (var n in new[] { "prt", "prt-alt", "prt-ctrl", "prt-shift" }) Hotkeys.Unregister(n);
            }
            if (failed.Count > 0)
                App.Instance.Tray.Notify("Some screenshot shortcuts are taken", string.Join(", ", failed) + " is used by another app. Choose different ones in Arrakis > Capture.");
        }

        /// <summary>Drag to pick an area (the default).</summary>
        public void StartCapture() => Capture(CaptureMode.Region);

        public void Capture(CaptureMode mode)
        {
            if (_busy) return;
            _busy = true;
            try
            {
                Native.GetCursorPos(out var pt);
                switch (mode)
                {
                    case CaptureMode.AllScreens:
                    {
                        var r = Grabber.VirtualScreen();
                        Done(new CaptureResult { Image = Grabber.Grab(r, S.IncludeCursor), Title = "All screens" });
                        return;
                    }
                    case CaptureMode.FullScreen:
                    {
                        var r = Native.GetMonitorAt(pt).rcMonitor;
                        Done(new CaptureResult { Image = Grabber.Grab(r, S.IncludeCursor), Title = "Screen" });
                        return;
                    }
                    case CaptureMode.LastRegion:
                    {
                        if (S.LastW < 2 || S.LastH < 2) { _busy = false; Capture(CaptureMode.Region); return; }
                        var r = new Native.RECT { Left = S.LastX, Top = S.LastY, Right = S.LastX + S.LastW, Bottom = S.LastY + S.LastH };
                        Done(new CaptureResult { Image = Grabber.Grab(r, S.IncludeCursor), Title = "Region" });
                        return;
                    }
                }

                var mon = Native.GetMonitorAt(pt).rcMonitor;
                var windows = Grabber.TopLevelWindows();
                var shot = Grabber.Grab(mon, S.IncludeCursor);
                var overlay = new CaptureOverlay(shot, mon, windows, mode == CaptureMode.Window, S.ShowMagnifier);
                overlay.Closed += (s, e) => { if (!overlay.HasResult) _busy = false; };
                overlay.Captured += (result, screenRect) =>
                {
                    S.LastX = screenRect.Left;
                    S.LastY = screenRect.Top;
                    S.LastW = screenRect.Width;
                    S.LastH = screenRect.Height;
                    Settings.Save();
                    Done(result);
                };
                overlay.Show();
                overlay.Activate();
            }
            catch (Exception ex)
            {
                _busy = false;
                Log.Write("Capture failed: " + ex);
            }
        }

        /// <summary>Counts down so you can open a menu or hover something first.</summary>
        public void CaptureAfter(int seconds, CaptureMode mode)
        {
            var pill = new CountdownWindow(seconds);
            pill.Finished += async () =>
            {
                await Task.Delay(180);   // let the countdown disappear before grabbing the screen
                Capture(mode);
            };
            pill.Show();
        }

        private void Done(CaptureResult r)
        {
            _busy = false;
            if (r?.Image == null) return;
            try
            {
                switch (S.AfterCapture)
                {
                    case "Clipboard":
                        System.Windows.Clipboard.SetImage(r.Image);
                        App.Instance.Tray.Notify("Screenshot copied", r.Image.PixelWidth + " × " + r.Image.PixelHeight + ". Paste it anywhere.");
                        break;
                    case "Save":
                    {
                        var path = CaptureImaging.QuickSave(r.Image, r.Title);
                        App.Instance.Tray.Notify("Screenshot saved", path);
                        break;
                    }
                    case "ClipboardAndSave":
                    {
                        System.Windows.Clipboard.SetImage(r.Image);
                        var path = CaptureImaging.QuickSave(r.Image, r.Title);
                        App.Instance.Tray.Notify("Screenshot copied and saved", path);
                        break;
                    }
                    default:
                        OpenEditor(r);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Write("After capture failed: " + ex.Message);
                OpenEditor(r);
            }
        }

        public void OpenEditor(CaptureResult r)
        {
            var editor = new CaptureEditor(r.Image, r.Title);
            editor.Show();
            editor.Activate();
        }

        public void OpenFile(string path)
        {
            try
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.UriSource = new Uri(path);
                img.EndInit();
                img.Freeze();
                OpenEditor(new CaptureResult { Image = CaptureImaging.ToBgra(img), Title = System.IO.Path.GetFileNameWithoutExtension(path) });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Couldn't open that image.\n\n" + ex.Message, "Arrakis", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ---------------- settings page ----------------
        public override UIElement BuildPage()
        {
            void Later(Action a)
            {
                if (!IsRunning) App.Instance.SetModuleEnabled(this, true);
                App.Instance.Hub?.Hide();
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
                t.Tick += (s, e) => { t.Stop(); a(); };
                t.Start();
            }

            FlatButton Mode(string text, string icon, Action a)
            {
                var b = new FlatButton
                {
                    Content = UI.V(8, UI.GridIcon(icon, 22, Theme.Amber).Centered(), UI.Text(text, 13, Theme.Text, FontWeights.SemiBold).Centered()),
                    MinHeight = 86,
                    Padding = new Thickness(10),
                    CornerRadius = new CornerRadius(16),
                    Background = Theme.Subtle,
                    BorderBrush = Theme.Line,
                    Margin = new Thickness(0, 0, 10, 10)
                };
                b.Click += (s, e) => a();
                return b;
            }

            var modes = new System.Windows.Controls.Primitives.UniformGrid { Columns = 6 };
            modes.Children.Add(Mode("Area", Icons.Capture, () => Later(StartCapture)));
            modes.Children.Add(Mode("Window", Icons.Box, () => Later(() => Capture(CaptureMode.Window))));
            modes.Children.Add(Mode("Full screen", Icons.Wallpaper, () => Later(() => Capture(CaptureMode.FullScreen))));
            modes.Children.Add(Mode("All screens", Icons.Widgets, () => Later(() => Capture(CaptureMode.AllScreens))));
            modes.Children.Add(Mode("Last area", Icons.Refresh, () => Later(() => Capture(CaptureMode.LastRegion))));
            modes.Children.Add(Mode("In 5 seconds", Icons.Timer, () => { App.Instance.Hub?.Hide(); CaptureAfter(5, CaptureMode.Region); }));

            var openFile = FlatButton.Secondary("Edit an image file...", () =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files|*.*" };
                if (dlg.ShowDialog() == true) OpenFile(dlg.FileName);
            });
            var fromClip = FlatButton.Secondary("Edit image on clipboard", () =>
            {
                try
                {
                    if (System.Windows.Clipboard.ContainsImage()) OpenEditor(new CaptureResult { Image = CaptureImaging.ToBgra(System.Windows.Clipboard.GetImage()), Title = "Clipboard" });
                    else MessageBox.Show("There's no image on the clipboard right now.", "Arrakis");
                }
                catch { }
            });

            // Shortcuts
            UIElement KeyRow(string label, Func<string> get, Action<string> set, string name, Action action)
            {
                var box = UI.TextInput(get(), 150);
                var status = UI.Text("", 12, Theme.Muted);
                var apply = FlatButton.Secondary("Apply", () =>
                {
                    string v = box.Text.Trim();
                    if (!Hotkeys.TryParse(v, out _, out _)) { status.Text = "Try something like Ctrl+Alt+S."; return; }
                    set(v);
                    Settings.Save();
                    status.Text = Hotkeys.Register(name, v, action) ? "Saved." : "Another app is using that.";
                });
                apply.MinHeight = 36;
                return UI.SettingRow(label, null, UI.V(4, UI.H(8, box, apply), status));
            }

            var prt = new ToggleSwitch(S.UsePrintScreen, "Use Print Screen keys");
            prt.Toggled += on => { S.UsePrintScreen = on; Settings.Save(); if (IsRunning) RegisterKeys(); };
            var cursor = new ToggleSwitch(S.IncludeCursor, "Include mouse pointer");
            cursor.Toggled += on => { S.IncludeCursor = on; Settings.Save(); };
            var magnifier = new ToggleSwitch(S.ShowMagnifier, "Show magnifier");
            magnifier.Toggled += on => { S.ShowMagnifier = on; Settings.Save(); };
            var blur = new ToggleSwitch(S.AutoBlurEmails, "Auto-blur emails");
            blur.Toggled += on => { S.AutoBlurEmails = on; Settings.Save(); };

            string AfterLabel(string v) => v == "Clipboard" ? "Copy to clipboard" : v == "Save" ? "Save to folder" : v == "ClipboardAndSave" ? "Copy and save" : "Open the editor";
            var after = UI.Combo(new[] { "Open the editor", "Copy to clipboard", "Save to folder", "Copy and save" }, AfterLabel(S.AfterCapture), v =>
            {
                S.AfterCapture = v == "Copy to clipboard" ? "Clipboard" : v == "Save to folder" ? "Save" : v == "Copy and save" ? "ClipboardAndSave" : "Editor";
                Settings.Save();
            });

            var format = UI.Combo(new[] { "PNG", "JPG", "BMP", "GIF", "TIFF" }, S.Format.ToUpperInvariant(), v => { S.Format = v.ToLowerInvariant(); Settings.Save(); });
            var quality = new MiniSlider(50, 100, S.JpegQuality, null, 180) { Step = 5 };
            var qText = UI.Text(S.JpegQuality + "%", 13, Theme.Muted);
            quality.ValueChanged += v => { S.JpegQuality = (int)v; qText.Text = (int)v + "%"; Settings.Save(); };
            var pattern = UI.TextInput(S.FilePattern, 260);
            var preview = UI.Text("", 12, Theme.Muted);
            void ShowPreview() => preview.Text = "Example: " + CaptureImaging.FileNameFromPattern(pattern.Text, "Notepad") + "." + S.Format;
            ShowPreview();
            pattern.TextChanged += (s, e) => ShowPreview();
            pattern.LostFocus += (s, e) => { S.FilePattern = string.IsNullOrWhiteSpace(pattern.Text) ? "Screenshot {yyyy-MM-dd HH.mm.ss}" : pattern.Text; Settings.Save(); };
            var folder = UI.Text(CaptureImaging.SaveFolder(), 12, Theme.Muted);
            var choose = FlatButton.Secondary("Change...", () =>
            {
                var dlg = new System.Windows.Forms.FolderBrowserDialog { SelectedPath = CaptureImaging.SaveFolder(), Description = "Where should screenshots be saved?" };
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    S.SaveFolder = dlg.SelectedPath;
                    Settings.Save();
                    folder.Text = S.SaveFolder;
                }
            });
            choose.MinHeight = 36;
            var openFolder = FlatButton.Secondary("Open folder", () => { Directory.CreateDirectory(CaptureImaging.SaveFolder()); Shell.Open(CaptureImaging.SaveFolder()); });
            openFolder.MinHeight = 36;

            return UI.V(16,
                UI.Label("Take a screenshot"),
                modes,
                UI.H(10, openFile, fromClip),
                UI.Label("Recent"),
                RecentStrip(),
                UI.Label("Shortcuts"),
                UI.Card(UI.V(0,
                    KeyRow("Area", () => S.Hotkey, v => S.Hotkey = v, "capture", StartCapture),
                    UI.Divider(),
                    KeyRow("Window", () => S.WindowHotkey, v => S.WindowHotkey = v, "capture-window", () => Capture(CaptureMode.Window)),
                    UI.Divider(),
                    KeyRow("Full screen", () => S.FullHotkey, v => S.FullHotkey = v, "capture-full", () => Capture(CaptureMode.FullScreen)),
                    UI.Divider(),
                    UI.SettingRow("Use the Print Screen key", "Print Screen for an area, Alt for a window, Ctrl for full screen, Shift for the last area. Turn off Windows' own Print Screen shortcut first (Settings > Accessibility > Keyboard).", prt)), 8),
                UI.Label("Capturing"),
                UI.Card(UI.V(0,
                    UI.SettingRow("After a capture", "Open the editor, or skip it and copy or save straight away.", after),
                    UI.Divider(),
                    UI.SettingRow("Include the mouse pointer", null, cursor),
                    UI.Divider(),
                    UI.SettingRow("Magnifier", "A zoomed view with exact pixel position and colour while you pick an area. Press C to copy the colour.", magnifier),
                    UI.Divider(),
                    UI.SettingRow("Auto-blur email addresses", "Reads the screenshot on your PC (nothing is uploaded) and blurs any emails it finds.", blur)), 8),
                UI.Label("Saving"),
                UI.Card(UI.V(0,
                    UI.SettingRow("Folder", null, UI.V(4, UI.H(8, choose, openFolder), folder)),
                    UI.Divider(),
                    UI.SettingRow("File name", "Use {yyyy-MM-dd HH.mm.ss} style dates and {title} for the window name.", UI.V(4, pattern, preview)),
                    UI.Divider(),
                    UI.SettingRow("Format", null, format),
                    UI.Divider(),
                    UI.SettingRow("JPG quality", null, UI.H(10, quality, qText))), 8));
        }

        private UIElement RecentStrip()
        {
            var row = new WrapPanel();
            try
            {
                string dir = CaptureImaging.SaveFolder();
                var files = Directory.Exists(dir)
                    ? new DirectoryInfo(dir).GetFiles().Where(f => new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" }.Contains(f.Extension.ToLowerInvariant()))
                        .OrderByDescending(f => f.LastWriteTime).Take(6).ToList()
                    : new List<FileInfo>();
                foreach (var f in files)
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.DecodePixelHeight = 180;
                    bi.UriSource = new Uri(f.FullName);
                    bi.EndInit();
                    var thumb = new Border { Width = 168, Height = 100, CornerRadius = new CornerRadius(10), Background = new ImageBrush(bi) { Stretch = Stretch.UniformToFill } };
                    var name = UI.Text(f.LastWriteTime.ToString("d MMM, HH:mm"), 12, Theme.Muted);
                    var b = new FlatButton
                    {
                        Content = UI.V(6, thumb, name),
                        Padding = new Thickness(6),
                        Background = Theme.Subtle,
                        CornerRadius = new CornerRadius(14),
                        Margin = new Thickness(0, 0, 10, 10),
                        ToolTip = f.Name + "\nClick to edit"
                    };
                    string path = f.FullName;
                    b.Click += (s, e) => OpenFile(path);
                    row.Children.Add(b);
                }
            }
            catch { }
            if (row.Children.Count == 0) return UI.Text("Screenshots you save will show up here.", 13, Theme.Muted);
            return row;
        }
    }

    /// <summary>Screen grabbing, cursor drawing and window detection.</summary>
    public static class Grabber
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct CURSORINFO { public int cbSize; public int flags; public IntPtr hCursor; public Native.POINT ptScreenPos; }

        [StructLayout(LayoutKind.Sequential)]
        private struct ICONINFO { public bool fIcon; public int xHotspot; public int yHotspot; public IntPtr hbmMask; public IntPtr hbmColor; }

        [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CURSORINFO pci);
        [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);
        [DllImport("user32.dll")] private static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr hIcon, int cx, int cy, int step, IntPtr brush, int flags);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out Native.RECT rect, int size);

        public static Native.RECT VirtualScreen()
        {
            int x = GetSystemMetrics(76), y = GetSystemMetrics(77), w = GetSystemMetrics(78), h = GetSystemMetrics(79);
            return new Native.RECT { Left = x, Top = y, Right = x + w, Bottom = y + h };
        }

        public static BitmapSource Grab(Native.RECT r, bool cursor)
        {
            using var bmp = new System.Drawing.Bitmap(Math.Max(1, r.Width), Math.Max(1, r.Height), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(r.Left, r.Top, 0, 0, new System.Drawing.Size(r.Width, r.Height), System.Drawing.CopyPixelOperation.SourceCopy);
                if (cursor) DrawCursor(g, r);
            }
            var hbm = bmp.GetHbitmap();
            try
            {
                var src = CaptureImaging.ToBgra(FromHBitmap(hbm));
                return src;
            }
            finally { DeleteObject(hbm); }
        }

        private static BitmapSource FromHBitmap(IntPtr hbm)
        {
            var s = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(hbm, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            s.Freeze();
            return s;
        }

        private static void DrawCursor(System.Drawing.Graphics g, Native.RECT r)
        {
            try
            {
                var ci = new CURSORINFO { cbSize = Marshal.SizeOf(typeof(CURSORINFO)) };
                if (!GetCursorInfo(ref ci) || (ci.flags & 1) == 0) return;
                int hx = 0, hy = 0;
                if (GetIconInfo(ci.hCursor, out var info))
                {
                    hx = info.xHotspot;
                    hy = info.yHotspot;
                    if (info.hbmMask != IntPtr.Zero) DeleteObject(info.hbmMask);
                    if (info.hbmColor != IntPtr.Zero) DeleteObject(info.hbmColor);
                }
                IntPtr hdc = g.GetHdc();
                try { DrawIconEx(hdc, ci.ptScreenPos.X - r.Left - hx, ci.ptScreenPos.Y - r.Top - hy, ci.hCursor, 0, 0, 0, IntPtr.Zero, 3); }
                finally { g.ReleaseHdc(hdc); }
            }
            catch { }
        }

        public class WindowInfo
        {
            public Native.RECT Rect;   // screen pixels
            public string Title;
        }

        /// <summary>Visible app windows, front to back, with their real (shadow-free) edges.</summary>
        public static List<WindowInfo> TopLevelWindows()
        {
            var list = new List<WindowInfo>();
            uint own = (uint)Environment.ProcessId;
            Native.EnumWindows((h, l) =>
            {
                try
                {
                    if (!Native.IsWindowVisible(h) || Native.IsIconic(h) || Native.IsCloaked(h)) return true;
                    Native.GetWindowThreadProcessId(h, out uint pid);
                    if (pid == own) return true;
                    Native.RECT rect;
                    if (DwmGetWindowAttribute(h, 9, out rect, Marshal.SizeOf(typeof(Native.RECT))) != 0)
                        Native.GetWindowRect(h, out rect);
                    if (rect.Width < 24 || rect.Height < 24) return true;
                    int len = Native.GetWindowTextLength(h);
                    var sb = new StringBuilder(len + 1);
                    Native.GetWindowText(h, sb, sb.Capacity);
                    var cls = new StringBuilder(64);
                    Native.GetClassName(h, cls, cls.Capacity);
                    string c = cls.ToString();
                    if (c == "Progman" || c == "WorkerW") return true;
                    list.Add(new WindowInfo { Rect = rect, Title = sb.ToString() });
                }
                catch { }
                return true;
            }, IntPtr.Zero);
            return list;
        }
    }

    /// <summary>Full-screen, frozen copy of one monitor. Drag for an area, or hover and click a window.</summary>
    public class CaptureOverlay : Window
    {
        private readonly BitmapSource _shot;
        private readonly Native.RECT _mon;
        private readonly List<Grabber.WindowInfo> _windows;
        private readonly bool _magnifier;
        private readonly byte[] _pixels;
        private readonly Grid _root;
        private readonly Path _dim;
        private readonly Rectangle _sel;
        private readonly Border _size;
        private readonly TextBlock _sizeText;
        private readonly Border _hint;
        private readonly TextBlock _hintText;
        private readonly Canvas _layer;
        private readonly Border _loupe;
        private readonly Image _loupeImg;
        private readonly TextBlock _loupeText;
        private readonly Border _loupeSwatch;
        private readonly Line _crossH, _crossV;
        private bool _windowMode;
        private Point _start;
        private bool _dragging;
        private Grabber.WindowInfo _hover;
        private string _hoverColour = "#000000";

        public bool HasResult { get; private set; }
        public event Action<CaptureResult, Native.RECT> Captured;

        private double Scale => _shot.PixelWidth / Math.Max(1, _root.ActualWidth);

        public CaptureOverlay(BitmapSource shot, Native.RECT mon, List<Grabber.WindowInfo> windows, bool windowMode, bool magnifier)
        {
            _shot = shot;
            _mon = mon;
            _windows = windows;
            _windowMode = windowMode;
            _magnifier = magnifier;
            _pixels = new byte[shot.PixelWidth * shot.PixelHeight * 4];
            shot.CopyPixels(_pixels, shot.PixelWidth * 4, 0);

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            ShowInTaskbar = false;
            Cursor = Cursors.Cross;
            Background = Brushes.Black;
            FontFamily = Theme.Font;

            _root = new Grid();
            var img = new Image { Source = shot, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
            _root.Children.Add(img);
            _dim = new Path { Fill = Theme.Brush(Color.FromArgb(0x7A, 0, 0, 0)), IsHitTestVisible = false };
            _root.Children.Add(_dim);
            _sel = new Rectangle { Stroke = Theme.Amber, StrokeThickness = 2, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            _root.Children.Add(_sel);

            _layer = new Canvas { IsHitTestVisible = false };
            _sizeText = UI.Text("", 12, Theme.Text, FontWeights.SemiBold);
            _size = new Border { Background = Theme.Glass, CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 4, 8, 4), Child = _sizeText, Visibility = Visibility.Collapsed };
            _layer.Children.Add(_size);

            // Magnifier
            _loupeImg = new Image { Width = 136, Height = 136, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(_loupeImg, BitmapScalingMode.NearestNeighbor);
            _crossH = new Line { X1 = 0, X2 = 136, Y1 = 68, Y2 = 68, Stroke = Theme.Brush(Color.FromArgb(0xB0, 0xFF, 0xB5, 0x47)), StrokeThickness = 8, Opacity = 0.35 };
            _crossV = new Line { Y1 = 0, Y2 = 136, X1 = 68, X2 = 68, Stroke = Theme.Brush(Color.FromArgb(0xB0, 0xFF, 0xB5, 0x47)), StrokeThickness = 8, Opacity = 0.35 };
            var centre = new Rectangle { Width = 9, Height = 9, Stroke = Brushes.White, StrokeThickness = 1.5, Margin = new Thickness(63.5, 63.5, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            var zoom = new Grid { Width = 136, Height = 136, ClipToBounds = true };
            zoom.Children.Add(_loupeImg);
            zoom.Children.Add(_crossH);
            zoom.Children.Add(_crossV);
            zoom.Children.Add(centre);
            _loupeText = UI.Text("", 12, Theme.Text, FontWeights.SemiBold);
            _loupeSwatch = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(4), BorderBrush = Theme.GlassBorder, BorderThickness = new Thickness(1) };
            var info = UI.H(8, _loupeSwatch, _loupeText);
            info.Margin = new Thickness(4, 8, 4, 2);
            _loupe = new Border
            {
                Background = Theme.Popup,
                BorderBrush = Theme.GlassBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(6),
                Child = UI.V(0, new Border { CornerRadius = new CornerRadius(9), ClipToBounds = true, Child = zoom }, info),
                Visibility = Visibility.Collapsed,
                Effect = Theme.Shadow(20, 0.45, 6)
            };
            _layer.Children.Add(_loupe);
            _root.Children.Add(_layer);

            _hintText = UI.Text("", 14, Theme.Text, FontWeights.SemiBold);
            _hint = new Border
            {
                Background = Theme.Glass,
                BorderBrush = Theme.GlassBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(22),
                Padding = new Thickness(18, 10, 18, 10),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 22, 0, 0),
                IsHitTestVisible = false,
                Child = _hintText
            };
            _root.Children.Add(_hint);
            Content = _root;
            UpdateHint();

            SourceInitialized += (s, e) =>
            {
                var h = new WindowInteropHelper(this).Handle;
                Native.SetWindowPos(h, Native.HWND_TOPMOST, _mon.Left, _mon.Top, _mon.Width, _mon.Height, Native.SWP_SHOWWINDOW);
            };
            Loaded += (s, e) =>
            {
                UpdateDim(Rect.Empty);
                Activate();
                Focus();
                Native.GetCursorPos(out var p);
                var pos = new Point((p.X - _mon.Left) / Scale, (p.Y - _mon.Top) / Scale);
                OnHover(pos);
            };
            MouseLeftButtonDown += (s, e) =>
            {
                _start = e.GetPosition(_root);
                _dragging = true;
                CaptureMouse();
            };
            MouseMove += (s, e) =>
            {
                var p = e.GetPosition(_root);
                if (_dragging && (p - _start).Length > 4) ShowSelection(new Rect(_start, p));
                else if (!_dragging) OnHover(p);
                UpdateLoupe(p);
            };
            MouseLeftButtonUp += (s, e) =>
            {
                if (!_dragging) return;
                _dragging = false;
                ReleaseMouseCapture();
                var p = e.GetPosition(_root);
                var r = new Rect(_start, p);
                if (r.Width >= 4 && r.Height >= 4) { Finish(r, "Region"); return; }
                // A click: the window under the mouse, or the whole screen.
                if (_hover != null) FinishWindow(_hover);
                else Finish(new Rect(0, 0, _root.ActualWidth, _root.ActualHeight), "Screen");
            };
            MouseRightButtonUp += (s, e) => Close();
            KeyDown += OnKey;
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape: Close(); break;
                case Key.Enter: Finish(new Rect(0, 0, _root.ActualWidth, _root.ActualHeight), "Screen"); break;
                case Key.Space:
                    _windowMode = !_windowMode;
                    UpdateHint();
                    Native.GetCursorPos(out var p);
                    OnHover(new Point((p.X - _mon.Left) / Scale, (p.Y - _mon.Top) / Scale));
                    break;
                case Key.C:
                    try { System.Windows.Clipboard.SetText(_hoverColour); _hintText.Text = "Copied " + _hoverColour; } catch { }
                    break;
                case Key.Left: Nudge(-1, 0); break;
                case Key.Right: Nudge(1, 0); break;
                case Key.Up: Nudge(0, -1); break;
                case Key.Down: Nudge(0, 1); break;
            }
            e.Handled = true;
        }

        private void Nudge(int dx, int dy)
        {
            Native.GetCursorPos(out var p);
            System.Windows.Forms.Cursor.Position = new System.Drawing.Point(p.X + dx, p.Y + dy);
        }

        private void UpdateHint()
        {
            _hintText.Text = _windowMode
                ? "Click a window   ·   Space: drag an area instead   ·   Enter: whole screen   ·   Esc: cancel"
                : "Drag an area or click a window   ·   Space: windows only   ·   Enter: whole screen   ·   Esc: cancel";
        }

        private void OnHover(Point p)
        {
            _hover = null;
            double s = Scale;
            int sx = (int)(p.X * s) + _mon.Left, sy = (int)(p.Y * s) + _mon.Top;
            foreach (var w in _windows)
            {
                if (sx >= w.Rect.Left && sx < w.Rect.Right && sy >= w.Rect.Top && sy < w.Rect.Bottom) { _hover = w; break; }
            }
            if (_hover == null && _windowMode) { UpdateDim(Rect.Empty); _sel.Visibility = Visibility.Collapsed; _size.Visibility = Visibility.Collapsed; return; }
            if (_hover == null) { UpdateDim(Rect.Empty); _sel.Visibility = Visibility.Collapsed; _size.Visibility = Visibility.Collapsed; return; }
            var r = new Rect((_hover.Rect.Left - _mon.Left) / s, (_hover.Rect.Top - _mon.Top) / s, _hover.Rect.Width / s, _hover.Rect.Height / s);
            r.Intersect(new Rect(0, 0, _root.ActualWidth, _root.ActualHeight));
            if (r.IsEmpty) return;
            ShowSelection(r, _hover.Title);
        }

        private void ShowSelection(Rect r, string label = null)
        {
            _sel.Margin = new Thickness(r.X, r.Y, 0, 0);
            _sel.Width = r.Width;
            _sel.Height = r.Height;
            _sel.Visibility = Visibility.Visible;
            double s = Scale;
            _sizeText.Text = (label != null && label.Length > 0 ? (label.Length > 40 ? label.Substring(0, 40) + "..." : label) + "   " : "") + (int)Math.Round(r.Width * s) + " × " + (int)Math.Round(r.Height * s);
            Canvas.SetLeft(_size, r.X);
            Canvas.SetTop(_size, r.Y >= 34 ? r.Y - 32 : r.Y + 6);
            _size.Visibility = Visibility.Visible;
            UpdateDim(r);
        }

        private void UpdateLoupe(Point p)
        {
            double s = Scale;
            int px = (int)(p.X * s), py = (int)(p.Y * s);
            px = Math.Max(0, Math.Min(_shot.PixelWidth - 1, px));
            py = Math.Max(0, Math.Min(_shot.PixelHeight - 1, py));
            int i = (py * _shot.PixelWidth + px) * 4;
            byte b = _pixels[i], g = _pixels[i + 1], r = _pixels[i + 2];
            _hoverColour = "#" + r.ToString("X2") + g.ToString("X2") + b.ToString("X2");
            if (!_magnifier) return;
            const int n = 17;
            int x0 = px - n / 2, y0 = py - n / 2;
            int cx = Math.Max(0, Math.Min(_shot.PixelWidth - n, x0));
            int cy = Math.Max(0, Math.Min(_shot.PixelHeight - n, y0));
            if (_shot.PixelWidth >= n && _shot.PixelHeight >= n)
            {
                try { _loupeImg.Source = new CroppedBitmap(_shot, new Int32Rect(cx, cy, n, n)); } catch { }
            }
            _loupeText.Text = px + ", " + py + "   " + _hoverColour + "   C to copy";
            _loupeSwatch.Background = new SolidColorBrush(Color.FromRgb(r, g, b));
            double lx = p.X + 22, ly = p.Y + 22;
            if (lx + 160 > _root.ActualWidth) lx = p.X - 170;
            if (ly + 190 > _root.ActualHeight) ly = p.Y - 200;
            Canvas.SetLeft(_loupe, lx);
            Canvas.SetTop(_loupe, ly);
            _loupe.Visibility = Visibility.Visible;
        }

        private void UpdateDim(Rect hole)
        {
            var full = new RectangleGeometry(new Rect(0, 0, Math.Max(1, _root.ActualWidth), Math.Max(1, _root.ActualHeight)));
            _dim.Data = hole.IsEmpty ? (Geometry)full : new CombinedGeometry(GeometryCombineMode.Exclude, full, new RectangleGeometry(hole));
        }

        private void FinishWindow(Grabber.WindowInfo w)
        {
            double s = Scale;
            var r = new Rect((w.Rect.Left - _mon.Left) / s, (w.Rect.Top - _mon.Top) / s, w.Rect.Width / s, w.Rect.Height / s);
            r.Intersect(new Rect(0, 0, _root.ActualWidth, _root.ActualHeight));
            Finish(r, string.IsNullOrWhiteSpace(w.Title) ? "Window" : w.Title);
        }

        private void Finish(Rect r, string title)
        {
            double s = Scale;
            int x = (int)Math.Round(r.X * s), y = (int)Math.Round(r.Y * s);
            int w = (int)Math.Round(r.Width * s), h = (int)Math.Round(r.Height * s);
            x = Math.Max(0, Math.Min(_shot.PixelWidth - 1, x));
            y = Math.Max(0, Math.Min(_shot.PixelHeight - 1, y));
            w = Math.Max(1, Math.Min(_shot.PixelWidth - x, w));
            h = Math.Max(1, Math.Min(_shot.PixelHeight - y, h));
            var crop = new CroppedBitmap(_shot, new Int32Rect(x, y, w, h));
            var copy = new WriteableBitmap(crop);
            copy.Freeze();
            HasResult = true;
            var screenRect = new Native.RECT { Left = _mon.Left + x, Top = _mon.Top + y, Right = _mon.Left + x + w, Bottom = _mon.Top + y + h };
            Close();
            Captured?.Invoke(new CaptureResult { Image = copy, Title = title }, screenRect);
        }
    }

    /// <summary>A small "3, 2, 1" pill before a delayed capture.</summary>
    public class CountdownWindow : Window
    {
        public event Action Finished;

        public CountdownWindow(int seconds)
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            var num = UI.Text(seconds.ToString(), 18, Theme.Amber, FontWeights.Bold, true);
            Content = new Border
            {
                Margin = new Thickness(16),
                Background = Theme.Glass,
                BorderBrush = Theme.GlassBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(22),
                Padding = new Thickness(16, 8, 18, 8),
                Effect = Theme.Shadow(20, 0.4, 6),
                Child = UI.H(10, UI.GridIcon(Icons.Capture, 18, Theme.Text), UI.Text("Screenshot in", 14, Theme.Text, FontWeights.SemiBold), num)
            };
            Loaded += (s, e) =>
            {
                var wa = SystemParameters.WorkArea;
                Left = wa.Left + (wa.Width - ActualWidth) / 2;
                Top = wa.Top + 8;
            };
            SourceInitialized += (s, e) => WindowHelper.MakeToolWindow(this, true);
            int left = seconds;
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            t.Tick += (s, e) =>
            {
                left--;
                if (left <= 0)
                {
                    t.Stop();
                    Close();
                    Finished?.Invoke();
                    return;
                }
                num.Text = left.ToString();
            };
            t.Start();
        }
    }

    public static class CenterExtensions
    {
        public static T Centered<T>(this T e) where T : FrameworkElement
        {
            e.HorizontalAlignment = HorizontalAlignment.Center;
            return e;
        }
    }
}

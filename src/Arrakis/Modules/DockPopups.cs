using System;
using System.Collections.Generic;
using System.IO;
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
    /// <summary>Live thumbnails of an app's windows, shown above its dock icon.</summary>
    public class WindowPreview : Window
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct DWM_THUMBNAIL_PROPERTIES
        {
            public int dwFlags;
            public Native.RECT rcDestination;
            public Native.RECT rcSource;
            public byte opacity;
            public bool fVisible;
            public bool fSourceClientAreaOnly;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PSIZE { public int x, y; }

        [DllImport("dwmapi.dll")] private static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);
        [DllImport("dwmapi.dll")] private static extern int DwmUnregisterThumbnail(IntPtr thumb);
        [DllImport("dwmapi.dll")] private static extern int DwmUpdateThumbnailProperties(IntPtr thumb, ref DWM_THUMBNAIL_PROPERTIES props);
        [DllImport("dwmapi.dll")] private static extern int DwmQueryThumbnailSourceSize(IntPtr thumb, out PSIZE size);

        private const int RectDestination = 0x1, Opacity = 0x4, Visible = 0x8;
        private const double ThumbW = 240, ThumbH = 140;

        private static WindowPreview _current;
        private readonly List<AppWindow> _windows;
        private readonly FrameworkElement _anchor;
        private readonly List<(Border slot, IntPtr src)> _slots = new List<(Border, IntPtr)>();
        private readonly List<IntPtr> _thumbs = new List<IntPtr>();
        private readonly DispatcherTimer _watch;
        private readonly Grid _root;

        public static void Show(List<AppWindow> windows, FrameworkElement anchor)
        {
            CloseCurrent();
            _current = new WindowPreview(windows.Take(6).ToList(), anchor);
            _current.Show();
        }

        public static void CloseCurrent()
        {
            try { _current?.Close(); } catch { }
            _current = null;
        }

        private WindowPreview(List<AppWindow> windows, FrameworkElement anchor)
        {
            _windows = windows;
            _anchor = anchor;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            Background = Theme.Brush(Theme.PanelColor);
            FontFamily = Theme.Font;

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var w in windows)
            {
                var win = w;
                var slot = new Border { Width = ThumbW, Height = ThumbH, CornerRadius = new CornerRadius(8), Background = Theme.Subtle };
                var title = UI.Text(w.Title, 12, Theme.Text, FontWeights.SemiBold);
                title.TextWrapping = TextWrapping.NoWrap;
                title.TextTrimming = TextTrimming.CharacterEllipsis;
                title.VerticalAlignment = VerticalAlignment.Center;
                var close = FlatButton.IconButton(Icons.Close, "Close this window", () =>
                {
                    WindowTracker.Close(win.Handle);
                    CloseCurrent();
                }, 26);
                close.Background = Brushes.Transparent;
                var head = new DockPanel { Margin = new Thickness(2, 0, 0, 8) };
                DockPanel.SetDock(close, System.Windows.Controls.Dock.Right);
                head.Children.Add(close);
                head.Children.Add(title);
                var cell = new Border
                {
                    Width = ThumbW + 16,
                    Padding = new Thickness(8),
                    CornerRadius = new CornerRadius(12),
                    Background = Brushes.Transparent,
                    Child = UI.V(0, head, slot),
                    Cursor = Cursors.Hand,
                    Margin = new Thickness(2)
                };
                cell.MouseEnter += (s, e) => cell.Background = Theme.Subtle;
                cell.MouseLeave += (s, e) => cell.Background = Brushes.Transparent;
                cell.MouseLeftButtonUp += (s, e) =>
                {
                    if (e.OriginalSource is DependencyObject d && IsInside(d, close)) return;
                    Native.ForceForeground(win.Handle);
                    CloseCurrent();
                };
                _slots.Add((slot, w.Handle));
                row.Children.Add(cell);
            }
            _root = new Grid { Margin = new Thickness(8) };
            _root.Children.Add(row);
            Content = _root;

            SourceInitialized += (s, e) =>
            {
                var h = new WindowInteropHelper(this).Handle;
                WindowHelper.MakeToolWindow(this, true);
                Native.RoundCorners(h);
            };
            Loaded += (s, e) => { Place(); Dispatcher.BeginInvoke(new Action(Register), DispatcherPriority.Loaded); };
            _watch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
            _watch.Tick += (s, e) =>
            {
                if (!IsMouseOver && !_anchor.IsMouseOver) CloseCurrent();
            };
            _watch.Start();
            Closed += (s, e) =>
            {
                _watch.Stop();
                foreach (var t in _thumbs) DwmUnregisterThumbnail(t);
                _thumbs.Clear();
            };
        }

        private static bool IsInside(DependencyObject d, DependencyObject target)
        {
            while (d != null)
            {
                if (d == target) return true;
                d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return false;
        }

        private void Place()
        {
            try
            {
                double scale = WindowHelper.DpiScale(_anchor);
                var topLeft = _anchor.PointToScreen(new Point(0, 0));
                double cx = topLeft.X + _anchor.ActualWidth * scale / 2;
                var hwnd = new WindowInteropHelper(this).Handle;
                double myScale = WindowHelper.DpiScale(this);
                int w = (int)Math.Round(ActualWidth * myScale), h = (int)Math.Round(ActualHeight * myScale);
                int x = (int)Math.Round(cx - w / 2.0), y = (int)Math.Round(topLeft.Y - h - 14 * scale);
                var mon = Native.GetMonitorAt(new Native.POINT { X = (int)cx, Y = (int)topLeft.Y }).rcWork;
                x = Math.Max(mon.Left + 8, Math.Min(mon.Right - w - 8, x));
                y = Math.Max(mon.Top + 8, y);
                Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, x, y, w, h, Native.SWP_NOACTIVATE);
            }
            catch (Exception ex) { Log.Write("Preview placement failed: " + ex.Message); }
        }

        private void Register()
        {
            var me = new WindowInteropHelper(this).Handle;
            double scale = WindowHelper.DpiScale(this);
            foreach (var (slot, src) in _slots)
            {
                if (DwmRegisterThumbnail(me, src, out IntPtr thumb) != 0) continue;
                _thumbs.Add(thumb);
                var p = slot.TranslatePoint(new Point(0, 0), this);
                double sw = slot.ActualWidth, sh = slot.ActualHeight;
                if (DwmQueryThumbnailSourceSize(thumb, out var size) == 0 && size.x > 0 && size.y > 0)
                {
                    double ratio = Math.Min(sw / size.x, sh / size.y);
                    double tw = size.x * ratio, th = size.y * ratio;
                    p.X += (sw - tw) / 2;
                    p.Y += (sh - th) / 2;
                    sw = tw;
                    sh = th;
                }
                var props = new DWM_THUMBNAIL_PROPERTIES
                {
                    dwFlags = RectDestination | Opacity | Visible,
                    opacity = 255,
                    fVisible = true,
                    rcDestination = new Native.RECT
                    {
                        Left = (int)Math.Round(p.X * scale),
                        Top = (int)Math.Round(p.Y * scale),
                        Right = (int)Math.Round((p.X + sw) * scale),
                        Bottom = (int)Math.Round((p.Y + sh) * scale)
                    }
                };
                DwmUpdateThumbnailProperties(thumb, ref props);
            }
        }
    }

    /// <summary>The newest files in a pinned folder, fanned out above its dock icon.</summary>
    public class FolderStack : Window
    {
        private static FolderStack _current;
        private bool _closing;

        public static void Show(string folder, FrameworkElement anchor)
        {
            try { _current?.Close(); } catch { }
            if (!Directory.Exists(folder))
            {
                MessageBox.Show("That folder isn't there any more:\n" + folder, "Arrakis");
                return;
            }
            _current = new FolderStack(folder, anchor);
            _current.Closed += (s, e) => _current = null;
            _current.Show();
            _current.Activate();
        }

        private FolderStack(string folder, FrameworkElement anchor)
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            FontFamily = Theme.Font;

            var list = new StackPanel();
            List<FileSystemInfo> entries;
            try
            {
                entries = new DirectoryInfo(folder).EnumerateFileSystemInfos()
                    .Where(f => (f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0 && !f.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => f.LastWriteTime).Take(12).ToList();
            }
            catch { entries = new List<FileSystemInfo>(); }

            foreach (var entry in entries)
            {
                string path = entry.FullName;
                bool isDir = entry is DirectoryInfo;
                FrameworkElement icon;
                var img = isDir ? null : IconCache.ForFile(path);
                if (img != null) icon = new Image { Source = img, Width = 26, Height = 26 };
                else icon = UI.GridIcon(isDir ? Icons.Folder : Icons.Note, 20, Theme.Amber);
                var iconBox = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(9), Background = Theme.Subtle, Child = icon };
                icon.HorizontalAlignment = HorizontalAlignment.Center;
                icon.VerticalAlignment = VerticalAlignment.Center;
                var name = UI.Text(entry.Name, 13, Theme.Text, FontWeights.SemiBold);
                name.TextWrapping = TextWrapping.NoWrap;
                name.TextTrimming = TextTrimming.CharacterEllipsis;
                name.MaxWidth = 260;
                var meta = UI.Text(ClipboardPanel.Ago(entry.LastWriteTime) + (isDir ? " · folder" : " · " + Size(((FileInfo)entry).Length)), 11, Theme.Muted);
                var text = UI.V(1, name, meta);
                text.VerticalAlignment = VerticalAlignment.Center;
                var row = new Border
                {
                    Padding = new Thickness(8),
                    CornerRadius = new CornerRadius(12),
                    Background = Brushes.Transparent,
                    Cursor = Cursors.Hand,
                    Child = UI.H(12, iconBox, text),
                    ToolTip = path
                };
                row.MouseEnter += (s, e) => row.Background = Theme.Subtle;
                row.MouseLeave += (s, e) => row.Background = Brushes.Transparent;
                row.MouseLeftButtonUp += (s, e) => { Shell.Open(path); SafeClose(); };
                row.MouseRightButtonUp += (s, e) =>
                {
                    System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\"");
                    SafeClose();
                };
                list.Children.Add(row);
            }
            if (entries.Count == 0) list.Children.Add(UI.Text("This folder is empty.", 13, Theme.Muted).WithMargin(new Thickness(8)));

            var title = UI.Text(System.IO.Path.GetFileName(folder.TrimEnd('\\')), 16, Theme.Text, FontWeights.Bold);
            title.VerticalAlignment = VerticalAlignment.Center;
            var open = FlatButton.Secondary("Open folder", () => { Shell.Open(folder); SafeClose(); });
            open.MinHeight = 34;
            open.Height = 34;
            var head = new DockPanel { Margin = new Thickness(8, 4, 4, 8) };
            DockPanel.SetDock(open, System.Windows.Controls.Dock.Right);
            head.Children.Add(open);
            head.Children.Add(title);

            Content = new Border
            {
                Margin = new Thickness(16),
                Background = Theme.Popup,
                BorderBrush = Theme.GlassBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(10),
                Width = 360,
                Effect = Theme.Shadow(34, 0.45, 10),
                Child = UI.V(0, head, list, UI.Text("Click to open · right-click to show in folder", 11, Theme.Muted).WithMargin(new Thickness(8, 6, 8, 2)))
            };

            SourceInitialized += (s, e) => WindowHelper.MakeToolWindow(this, false);
            Loaded += (s, e) =>
            {
                try
                {
                    double scale = WindowHelper.DpiScale(anchor);
                    var p = anchor.PointToScreen(new Point(0, 0));
                    double cx = p.X / scale + anchor.ActualWidth / 2;
                    Left = cx - ActualWidth / 2;
                    Top = p.Y / scale - ActualHeight - 4;
                    var wa = SystemParameters.WorkArea;
                    Left = Math.Max(wa.Left + 4, Math.Min(wa.Right - ActualWidth - 4, Left));
                    Top = Math.Max(wa.Top + 4, Top);
                }
                catch { }
            };
            Deactivated += (s, e) => SafeClose();
            KeyDown += (s, e) => { if (e.Key == Key.Escape) SafeClose(); };
        }

        private void SafeClose()
        {
            if (_closing) return;
            _closing = true;
            Close();
        }

        private static string Size(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0") + " KB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / 1024.0 / 1024).ToString("0.0") + " MB";
            return (bytes / 1024.0 / 1024 / 1024).ToString("0.0") + " GB";
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Arrakis.Core
{
    /// <summary>Start Arrakis with Windows (current user only, no admin needed).</summary>
    public static class AutoStart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static void Apply(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
                if (enabled)
                    key.SetValue("Arrakis", "\"" + Environment.ProcessPath + "\" --background");
                else if (key.GetValue("Arrakis") != null)
                    key.DeleteValue("Arrakis");
            }
            catch (Exception ex) { Log.Write("Startup registry failed: " + ex.Message); }
        }
    }

    /// <summary>One screen that has a dock on it.</summary>
    public struct DockScreen
    {
        public IntPtr Monitor;
        public string Edge;        // Bottom, Left, Right
        public int ReservePx;      // 0 = windows may go under the dock
    }

    /// <summary>
    /// Hides and restores the Windows taskbar, and manages each screen's work area (the space maximised windows use).
    /// Windows only frees the taskbar's space on the main screen by itself, so every screen is handled separately here.
    /// The taskbar is only hidden on screens that have a dock, so no screen is ever left without one.
    /// </summary>
    public static class TaskbarManager
    {
        private static readonly Dictionary<IntPtr, Native.RECT> Original = new Dictionary<IntPtr, Native.RECT>();
        private static readonly HashSet<IntPtr> HiddenTrays = new HashSet<IntPtr>();
        private static List<DockScreen> _docks = new List<DockScreen>();
        private static bool _hide;
        private static bool _hidden;

        public static bool IsHidden => _hidden;

        public static bool IsHiddenOn(IntPtr monitor) => _hidden && _docks.Exists(d => d.Monitor == monitor);

        /// <summary>The area a dock should sit in on this screen: the whole screen if its taskbar is hidden,
        /// otherwise the space next to the taskbar. Never includes space the dock itself reserved.</summary>
        public static Native.RECT BaseArea(IntPtr monitor)
        {
            var mi = Info(monitor);
            if (IsHiddenOn(monitor)) return mi.rcMonitor;
            return Original.TryGetValue(monitor, out var r) ? r : mi.rcWork;
        }

        /// <summary>Applies the taskbar and work-area settings for these docks. Safe to call often.</summary>
        public static void Apply(List<DockScreen> docks, bool hideTaskbar)
        {
            _docks = docks ?? new List<DockScreen>();
            _hide = hideTaskbar;
            try
            {
                // 1. Taskbars: hide the ones on docked screens, show the rest.
                foreach (var h in TaskbarWindows())
                {
                    var mon = Native.MonitorFromWindow(h, Native.MONITOR_DEFAULTTONEAREST);
                    bool shouldHide = _hide && _docks.Exists(d => d.Monitor == mon);
                    if (shouldHide)
                    {
                        if (!Original.ContainsKey(mon)) Original[mon] = Info(mon).rcWork;
                        if (Native.IsWindowVisible(h)) Native.ShowWindow(h, Native.SW_HIDE);
                        HiddenTrays.Add(h);
                    }
                    else if (HiddenTrays.Contains(h))
                    {
                        Native.ShowWindow(h, Native.SW_SHOW);
                        HiddenTrays.Remove(h);
                    }
                }
                _hidden = _hide && HiddenTrays.Count > 0;

                // 2. Work areas, screen by screen.
                foreach (var mon in Monitors())
                {
                    var mi = Info(mon);
                    var dock = _docks.Find(d => d.Monitor == mon);
                    bool docked = dock.Monitor == mon && mon != IntPtr.Zero;
                    bool hiddenHere = _hide && docked;
                    bool reserve = docked && dock.ReservePx > 0;
                    if (!hiddenHere && !reserve)
                    {
                        // Nothing to change here: put back what Windows had, if we changed it.
                        if (Original.TryGetValue(mon, out var orig))
                        {
                            if (!Same(mi.rcWork, orig)) SetWork(orig);
                            Original.Remove(mon);
                        }
                        continue;
                    }
                    if (!Original.ContainsKey(mon)) Original[mon] = mi.rcWork;
                    var want = hiddenHere ? mi.rcMonitor : Original[mon];
                    if (reserve)
                    {
                        switch (dock.Edge)
                        {
                            case "Left": want.Left += dock.ReservePx; break;
                            case "Right": want.Right -= dock.ReservePx; break;
                            default: want.Bottom -= dock.ReservePx; break;
                        }
                    }
                    if (!Same(mi.rcWork, want)) SetWork(want);
                }
            }
            catch (Exception ex) { Log.Write("Taskbar apply failed: " + ex.Message); }
        }

        /// <summary>Called on a timer: Explorer sometimes shows the taskbar or resets work areas (for example after it restarts).</summary>
        public static void Enforce()
        {
            if (_docks.Count == 0) return;
            Apply(_docks, _hide);
        }

        /// <summary>Puts every taskbar and work area back the way Windows had them.</summary>
        public static void Restore()
        {
            try
            {
                foreach (var h in TaskbarWindows()) if (!Native.IsWindowVisible(h)) Native.ShowWindow(h, Native.SW_SHOW);
                HiddenTrays.Clear();
                foreach (var kv in Original) SetWork(kv.Value);
                Original.Clear();
                _docks = new List<DockScreen>();
                _hidden = false;
                _hide = false;
            }
            catch (Exception ex) { Log.Write("Restore taskbar failed: " + ex.Message); }
        }

        /// <summary>Used by the "Restore Windows taskbar" shortcut. Works even if Arrakis crashed, on every screen.</summary>
        public static void EmergencyRestore()
        {
            var trays = TaskbarWindows();
            foreach (var h in trays) Native.ShowWindow(h, Native.SW_SHOW);
            foreach (var mon in Monitors())
            {
                var mi = Info(mon);
                var work = mi.rcMonitor;
                foreach (var h in trays)
                {
                    if (Native.MonitorFromWindow(h, Native.MONITOR_DEFAULTTONEAREST) != mon) continue;
                    if (!Native.GetWindowRect(h, out var t)) continue;
                    if (t.Width >= t.Height && t.Top > work.Top) work.Bottom = Math.Min(work.Bottom, t.Top);
                    else if (t.Width >= t.Height) work.Top = Math.Max(work.Top, t.Bottom);
                    else if (t.Left > work.Left) work.Right = Math.Min(work.Right, t.Left);
                    else work.Left = Math.Max(work.Left, t.Right);
                }
                SetWork(work);
            }
        }

        // ---------- helpers ----------
        private static bool Same(Native.RECT a, Native.RECT b) => a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;

        private static void SetWork(Native.RECT r) => Native.SystemParametersInfo(Native.SPI_SETWORKAREA, 0, ref r, Native.SPIF_SENDCHANGE);

        private static Native.MONITORINFO Info(IntPtr mon)
        {
            var mi = new Native.MONITORINFO { cbSize = Marshal.SizeOf(typeof(Native.MONITORINFO)) };
            Native.GetMonitorInfo(mon, ref mi);
            return mi;
        }

        private static List<IntPtr> TaskbarWindows()
        {
            var list = new List<IntPtr>();
            Native.EnumWindows((h, l) =>
            {
                var sb = new StringBuilder(64);
                Native.GetClassName(h, sb, sb.Capacity);
                string cls = sb.ToString();
                if (cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd") list.Add(h);
                return true;
            }, IntPtr.Zero);
            return list;
        }

        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr rect, IntPtr data);

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);

        public static List<IntPtr> Monitors()
        {
            var list = new List<IntPtr>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (h, dc, r, d) => { list.Add(h); return true; }, IntPtr.Zero);
            return list;
        }
    }

    /// <summary>Gets icons for programs and windows.</summary>
    public static class IconCache
    {
        private static readonly Dictionary<string, ImageSource> Cache = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);

        public static ImageSource ForFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (Cache.TryGetValue(path, out var cached)) return cached;
            ImageSource result = null;
            try
            {
                if (File.Exists(path))
                {
                    using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                    if (icon != null)
                    {
                        using var bmp = icon.ToBitmap();
                        result = FromBitmap(bmp);
                    }
                }
            }
            catch { }
            Cache[path] = result;
            return result;
        }

        public static BitmapSource FromBitmap(System.Drawing.Bitmap bmp)
        {
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            ms.Position = 0;
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.StreamSource = ms;
            img.EndInit();
            img.Freeze();
            return img;
        }

        public static ImageSource AppIcon()
        {
            try
            {
                var uri = new Uri("pack://application:,,,/Assets/arrakis.ico", UriKind.Absolute);
                var decoder = BitmapDecoder.Create(uri, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                BitmapFrame best = decoder.Frames[0];
                foreach (var f in decoder.Frames) if (f.PixelWidth > best.PixelWidth) best = f;
                return best;
            }
            catch { return null; }
        }
    }

    public static class Shell
    {
        public static void Open(string target, string args = null)
        {
            try
            {
                var psi = new ProcessStartInfo(target) { UseShellExecute = true };
                if (!string.IsNullOrEmpty(args)) psi.Arguments = args;
                Process.Start(psi);
            }
            catch (Exception ex) { Log.Write("Open failed for " + target + ": " + ex.Message); }
        }
    }

    public static class WindowHelper
    {
        /// <summary>Makes a window invisible to Alt+Tab and the taskbar.</summary>
        public static void MakeToolWindow(Window w, bool noActivate)
        {
            var h = new WindowInteropHelper(w).EnsureHandle();
            long add = Native.WS_EX_TOOLWINDOW;
            if (noActivate) add |= Native.WS_EX_NOACTIVATE;
            Native.AddExStyle(h, add);
            Native.RemoveExStyle(h, Native.WS_EX_APPWINDOW);
        }

        public static double DpiScale(Visual v)
        {
            try { return VisualTreeHelper.GetDpi(v).DpiScaleX; } catch { return 1.0; }
        }

        /// <summary>Work area of the monitor under the mouse, in WPF units (DIPs) for the given window.</summary>
        public static Rect WorkAreaDips(Window w, bool full = false)
        {
            Native.GetCursorPos(out var pt);
            var mi = Native.GetPrimaryMonitor();
            var r = full ? mi.rcMonitor : mi.rcWork;
            double s = DpiScale(w);
            return new Rect(r.Left / s, r.Top / s, r.Width / s, r.Height / s);
        }
    }
}

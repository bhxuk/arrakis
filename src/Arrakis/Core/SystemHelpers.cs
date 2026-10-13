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
        public int ReservePx;      // kept for compatibility; space is now reserved by the dock itself as an app bar
    }

    /// <summary>
    /// Hides and restores the Windows taskbar.
    ///
    /// How it works, and why: Arrakis switches on Windows' own "Automatically hide the taskbar" setting, once.
    /// Windows then frees the taskbar's space on every screen by itself, so nothing here ever changes a screen's
    /// work area or broadcasts settings changes (doing that on a timer fights Explorer and can freeze the PC).
    /// On screens with a dock the taskbar is also hidden outright; on other screens it just auto-hides, so it's
    /// still there when you push the mouse to the edge. Your original setting is saved and put back on exit.
    /// </summary>
    public static class TaskbarManager
    {
        private static readonly HashSet<IntPtr> HiddenTrays = new HashSet<IntPtr>();
        private static List<DockScreen> _docks = new List<DockScreen>();
        private static bool _hide;
        private static bool _hidden;
        private static readonly Queue<DateTime> Rehides = new Queue<DateTime>();
        private static bool _gaveUp;

        /// <summary>Raised once if Windows keeps bringing the taskbar back, and Arrakis stops trying.</summary>
        public static event Action GaveUp;

        public static bool IsHidden => _hidden;

        public static bool IsHiddenOn(IntPtr monitor) => _hidden && _docks.Exists(d => d.Monitor == monitor);

        /// <summary>The area a dock should sit in on this screen.</summary>
        public static Native.RECT BaseArea(IntPtr monitor)
        {
            var mi = Info(monitor);
            return IsHiddenOn(monitor) ? mi.rcMonitor : mi.rcWork;
        }

        public static void Apply(List<DockScreen> docks, bool hideTaskbar)
        {
            _docks = docks ?? new List<DockScreen>();
            _hide = hideTaskbar;
            try
            {
                if (_hide)
                {
                    var st = Settings.Current.Dock;
                    if (st.TaskbarAutoHideBefore < 0)
                    {
                        st.TaskbarAutoHideBefore = AutoHide ? 1 : 0;
                        Settings.Save();
                    }
                    if (!AutoHide) AutoHide = true;
                    _gaveUp = false;
                    Rehides.Clear();
                    SyncTrays();
                    _hidden = true;
                }
                else
                {
                    // Not hiding: make sure every taskbar is showing (this also cleans up after a crash) and
                    // put the auto-hide setting back if Arrakis changed it.
                    Restore();
                }
            }
            catch (Exception ex) { Log.Write("Taskbar apply failed: " + ex.Message); }
        }

        /// <summary>Hides taskbars on docked screens and shows the others. Returns how many it had to hide.</summary>
        private static int SyncTrays()
        {
            int hid = 0;
            foreach (var h in TaskbarWindows())
            {
                var mon = Native.MonitorFromWindow(h, Native.MONITOR_DEFAULTTONEAREST);
                bool docked = _docks.Exists(d => d.Monitor == mon);
                if (docked)
                {
                    if (Native.IsWindowVisible(h)) { Native.ShowWindow(h, Native.SW_HIDE); hid++; }
                    HiddenTrays.Add(h);
                }
                else if (HiddenTrays.Remove(h) && !Native.IsWindowVisible(h)) Native.ShowWindow(h, Native.SW_SHOW);
            }
            return hid;
        }

        /// <summary>Called every few seconds. If Explorer has brought a taskbar back (for example after it restarted),
        /// hide it again, but give up if that keeps happening, rather than fight Windows.</summary>
        public static void Enforce()
        {
            if (!_hide || _gaveUp) return;
            try
            {
                int hid = SyncTrays();
                if (hid == 0) return;
                var now = DateTime.Now;
                Rehides.Enqueue(now);
                while (Rehides.Count > 0 && (now - Rehides.Peek()).TotalSeconds > 60) Rehides.Dequeue();
                if (Rehides.Count >= 5)
                {
                    _gaveUp = true;
                    Log.Write("Windows keeps showing the taskbar again; Arrakis has stopped hiding it until next start.");
                    foreach (var h in TaskbarWindows()) if (!Native.IsWindowVisible(h)) Native.ShowWindow(h, Native.SW_SHOW);
                    HiddenTrays.Clear();
                    _hidden = false;
                    GaveUp?.Invoke();
                }
            }
            catch (Exception ex) { Log.Write("Taskbar check failed: " + ex.Message); }
        }

        /// <summary>Shows every taskbar and puts "Automatically hide the taskbar" back how it was.</summary>
        public static void Restore()
        {
            try
            {
                foreach (var h in TaskbarWindows()) if (!Native.IsWindowVisible(h)) Native.ShowWindow(h, Native.SW_SHOW);
                HiddenTrays.Clear();
                var st = Settings.Current.Dock;
                if (st.TaskbarAutoHideBefore >= 0)
                {
                    bool was = st.TaskbarAutoHideBefore == 1;
                    if (AutoHide != was) AutoHide = was;
                    st.TaskbarAutoHideBefore = -1;
                    Settings.Save();
                }
                _docks = new List<DockScreen>();
                _hidden = false;
                _hide = false;
            }
            catch (Exception ex) { Log.Write("Restore taskbar failed: " + ex.Message); }
        }

        /// <summary>Used by Start > Arrakis > Restore Windows taskbar. Works even if Arrakis isn't running.</summary>
        public static void EmergencyRestore()
        {
            try { Settings.Load(); } catch { }
            var trays = TaskbarWindows();
            foreach (var h in trays) Native.ShowWindow(h, Native.SW_SHOW);
            var st = Settings.Current.Dock;
            bool wantAutoHide = st.TaskbarAutoHideBefore == 1;
            // Toggling the setting makes Explorer recalculate every screen's space from scratch,
            // which also clears anything left over from older versions of Arrakis.
            AutoHide = !wantAutoHide;
            System.Threading.Thread.Sleep(300);
            AutoHide = wantAutoHide;
            st.TaskbarAutoHideBefore = -1;
            st.HideTaskbar = false;
            try { Settings.Save(); } catch { }
        }

        // ---------- Windows' "Automatically hide the taskbar" ----------
        [StructLayout(LayoutKind.Sequential)]
        public struct APPBARDATA
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uCallbackMessage;
            public uint uEdge;
            public Native.RECT rc;
            public IntPtr lParam;
        }

        [DllImport("shell32.dll")]
        public static extern UIntPtr SHAppBarMessage(uint msg, ref APPBARDATA data);

        private const uint ABM_GETSTATE = 4, ABM_SETSTATE = 10;

        public static bool AutoHide
        {
            get
            {
                var d = new APPBARDATA { cbSize = (uint)Marshal.SizeOf<APPBARDATA>(), hWnd = Native.FindWindow("Shell_TrayWnd", null) };
                return ((ulong)SHAppBarMessage(ABM_GETSTATE, ref d) & 1) != 0;
            }
            set
            {
                var d = new APPBARDATA { cbSize = (uint)Marshal.SizeOf<APPBARDATA>(), hWnd = Native.FindWindow("Shell_TrayWnd", null), lParam = new IntPtr(value ? 1 : 2) };
                SHAppBarMessage(ABM_SETSTATE, ref d);
            }
        }

        // ---------- helpers ----------
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

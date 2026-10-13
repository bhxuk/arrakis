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

    /// <summary>Hides and restores the Windows taskbar for the "replace the taskbar" option.</summary>
    public static class TaskbarManager
    {
        private static bool _hidden;
        private static Native.RECT _savedWorkArea;
        private static bool _haveSaved;

        public static bool IsHidden => _hidden;

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

        public static void Hide()
        {
            try
            {
                var primary = Native.GetPrimaryMonitor();
                if (!_haveSaved)
                {
                    _savedWorkArea = primary.rcWork;
                    _haveSaved = true;
                }
                foreach (var h in TaskbarWindows())
                {
                    if (Native.IsWindowVisible(h)) Native.ShowWindow(h, Native.SW_HIDE);
                }
                var full = primary.rcMonitor;
                Native.SystemParametersInfo(Native.SPI_SETWORKAREA, 0, ref full, Native.SPIF_SENDCHANGE);
                _hidden = true;
            }
            catch (Exception ex) { Log.Write("Hide taskbar failed: " + ex.Message); }
        }

        /// <summary>Called on a timer: Explorer sometimes shows the taskbar again (for example after it restarts).</summary>
        public static void Enforce()
        {
            if (!_hidden) return;
            foreach (var h in TaskbarWindows())
            {
                if (Native.IsWindowVisible(h)) { Hide(); break; }
            }
        }

        public static void Restore()
        {
            try
            {
                foreach (var h in TaskbarWindows()) Native.ShowWindow(h, Native.SW_SHOW);
                if (_haveSaved)
                {
                    var r = _savedWorkArea;
                    Native.SystemParametersInfo(Native.SPI_SETWORKAREA, 0, ref r, Native.SPIF_SENDCHANGE);
                }
                _hidden = false;
            }
            catch (Exception ex) { Log.Write("Restore taskbar failed: " + ex.Message); }
        }

        /// <summary>Used by the "Restore Windows taskbar" shortcut. Works even if Arrakis crashed.</summary>
        public static void EmergencyRestore()
        {
            foreach (var h in TaskbarWindows()) Native.ShowWindow(h, Native.SW_SHOW);
            // Asking Explorer to recalculate the work area: showing the taskbar triggers this,
            // and a broadcast setting change nudges any windows that cached the old size.
            var primary = Native.GetPrimaryMonitor();
            var work = primary.rcMonitor;
            var tb = Native.FindWindow("Shell_TrayWnd", null);
            if (tb != IntPtr.Zero && Native.GetWindowRect(tb, out var t))
            {
                if (t.Width >= t.Height && t.Top > work.Top) work.Bottom = t.Top;
                else if (t.Width >= t.Height) work.Top = t.Bottom;
                else if (t.Left > work.Left) work.Right = t.Left;
                else work.Left = t.Right;
                Native.SystemParametersInfo(Native.SPI_SETWORKAREA, 0, ref work, Native.SPIF_SENDCHANGE);
            }
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

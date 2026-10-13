using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Arrakis.Core;

namespace Arrakis.Modules
{
    public class AppWindow
    {
        public IntPtr Handle;
        public string Path;
        public string Title;
        public string Key;
        public IntPtr Monitor;
    }

    /// <summary>Finds the open app windows, like the taskbar does.</summary>
    public static class WindowTracker
    {
        private static readonly uint OwnPid = (uint)Environment.ProcessId;
        private static readonly Dictionary<uint, string> PathCache = new Dictionary<uint, string>();

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private const uint WM_CLOSE = 0x0010;

        public static List<AppWindow> GetAppWindows()
        {
            var list = new List<AppWindow>();
            Native.EnumWindows((h, l) =>
            {
                try
                {
                    if (!Native.IsWindowVisible(h)) return true;
                    if (Native.GetWindow(h, Native.GW_OWNER) != IntPtr.Zero) return true;
                    long ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
                    if ((ex & Native.WS_EX_TOOLWINDOW) != 0 && (ex & Native.WS_EX_APPWINDOW) == 0) return true;
                    if ((ex & Native.WS_EX_NOACTIVATE) != 0) return true;
                    int len = Native.GetWindowTextLength(h);
                    if (len == 0) return true;
                    if (Native.IsCloaked(h)) return true;
                    Native.GetWindowThreadProcessId(h, out uint pid);
                    if (pid == OwnPid) return true;
                    var sb = new StringBuilder(len + 1);
                    Native.GetWindowText(h, sb, sb.Capacity);
                    string title = sb.ToString();
                    if (title == "Program Manager") return true;
                    if (!PathCache.TryGetValue(pid, out string path))
                    {
                        path = Native.GetProcessPath(pid) ?? "";
                        PathCache[pid] = path;
                    }
                    string file = System.IO.Path.GetFileName(path).ToLowerInvariant();
                    string key = file == "applicationframehost.exe" ? "uwp:" + title : path.ToLowerInvariant();
                    list.Add(new AppWindow { Handle = h, Path = path, Title = title, Key = key, Monitor = Native.MonitorFromWindow(h, Native.MONITOR_DEFAULTTONEAREST) });
                }
                catch { }
                return true;
            }, IntPtr.Zero);
            if (PathCache.Count > 500) PathCache.Clear();
            return list;
        }

        public static string FriendlyName(string path)
        {
            if (string.IsNullOrEmpty(path)) return "App";
            try
            {
                if (File.Exists(path))
                {
                    var info = FileVersionInfo.GetVersionInfo(path);
                    if (!string.IsNullOrWhiteSpace(info.FileDescription)) return info.FileDescription.Trim();
                }
            }
            catch { }
            return System.IO.Path.GetFileNameWithoutExtension(path);
        }

        public static void Close(IntPtr h) => PostMessage(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }
}

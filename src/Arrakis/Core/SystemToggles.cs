using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Arrakis.Core
{
    /// <summary>One screen whose brightness can be changed: the built-in laptop panel, or an external monitor over DDC/CI.</summary>
    public sealed class Display
    {
        public string Name;
        public bool BuiltIn;
        public int Brightness;
        internal IntPtr Physical;
        internal uint Min, Max = 100;
    }

    public static class Brightness
    {
        private static List<Display> _cache;

        /// <summary>Finds screens that support brightness. External monitors are slow to answer, so call this off the UI thread.</summary>
        private static DateTime _found;
        private static readonly object Gate = new object();

        public static List<Display> Find()
        {
            lock (Gate)
            {
                // Reuse a recent scan so the panel and the settings page share the same monitor handles.
                if (_cache != null && (DateTime.Now - _found).TotalSeconds < 60) return _cache;
                Release();
                _cache = Scan();
                _found = DateTime.Now;
                return _cache;
            }
        }

        private static List<Display> Scan()
        {
            var list = new List<Display>();
            // Laptop screen
            try
            {
                using var s = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
                foreach (ManagementObject o in s.Get())
                {
                    list.Add(new Display { Name = "Built-in screen", BuiltIn = true, Brightness = Convert.ToInt32(o["CurrentBrightness"]) });
                    break;
                }
            }
            catch { }
            // External monitors
            try
            {
                var monitors = new List<IntPtr>();
                EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr dc, ref Rect r, IntPtr d) => { monitors.Add(h); return true; }, IntPtr.Zero);
                int n = 1;
                foreach (var hmon in monitors)
                {
                    if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hmon, out uint count) || count == 0) continue;
                    var phys = new PhysicalMonitor[count];
                    if (!GetPhysicalMonitorsFromHMONITOR(hmon, count, phys)) continue;
                    foreach (var p in phys)
                    {
                        if (GetMonitorBrightness(p.Handle, out uint min, out uint cur, out uint max) && max > min)
                        {
                            string name = string.IsNullOrWhiteSpace(p.Description) || p.Description.StartsWith("Generic") ? "Monitor " + n : p.Description;
                            list.Add(new Display { Name = name, Physical = p.Handle, Min = min, Max = max, Brightness = (int)Math.Round(100.0 * (cur - min) / (max - min)) });
                            n++;
                        }
                        else DestroyPhysicalMonitor(p.Handle);
                    }
                }
            }
            catch (Exception ex) { Log.Write("Monitor brightness: " + ex.Message); }
            return list;
        }

        public static void Set(Display d, int percent)
        {
            percent = Math.Max(0, Math.Min(100, percent));
            d.Brightness = percent;
            try
            {
                if (d.BuiltIn)
                {
                    using var s = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
                    foreach (ManagementObject o in s.Get())
                    {
                        o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)percent });
                        break;
                    }
                }
                else SetMonitorBrightness(d.Physical, (uint)(d.Min + (d.Max - d.Min) * percent / 100.0));
            }
            catch (Exception ex) { Log.Write("Set brightness: " + ex.Message); }
        }

        private static void Release()
        {
            if (_cache == null) return;
            foreach (var d in _cache) if (!d.BuiltIn && d.Physical != IntPtr.Zero) DestroyPhysicalMonitor(d.Physical);
            _cache = null;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect { public int L, T, R, B; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PhysicalMonitor
        {
            public IntPtr Handle;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        }

        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref Rect rect, IntPtr data);

        [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
        [DllImport("dxva2.dll")] private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint count);
        [DllImport("dxva2.dll", CharSet = CharSet.Unicode)] private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint count, [Out] PhysicalMonitor[] monitors);
        [DllImport("dxva2.dll")] private static extern bool GetMonitorBrightness(IntPtr monitor, out uint min, out uint current, out uint max);
        [DllImport("dxva2.dll")] private static extern bool SetMonitorBrightness(IntPtr monitor, uint value);
        [DllImport("dxva2.dll")] private static extern bool DestroyPhysicalMonitor(IntPtr monitor);
    }

    /// <summary>Windows switches: dark mode, Night light, Do not disturb, Wi-Fi and Bluetooth.</summary>
    public static class SystemToggles
    {
        private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        // ---------- Dark mode ----------
        public static bool DarkMode
        {
            get
            {
                try { using var k = Registry.CurrentUser.OpenSubKey(Personalize); return k?.GetValue("AppsUseLightTheme") is int v && v == 0; }
                catch { return false; }
            }
            set
            {
                try
                {
                    using var k = Registry.CurrentUser.CreateSubKey(Personalize);
                    k.SetValue("AppsUseLightTheme", value ? 0 : 1, RegistryValueKind.DWord);
                    k.SetValue("SystemUsesLightTheme", value ? 0 : 1, RegistryValueKind.DWord);
                    SendMessageTimeout(new IntPtr(0xFFFF), 0x001A, IntPtr.Zero, "ImmersiveColorSet", 0x0002, 200, out _);
                }
                catch (Exception ex) { Log.Write("Dark mode: " + ex.Message); }
            }
        }

        // ---------- Do not disturb ----------
        // The same switch Focus uses: turns app notification pop-ups off.
        public static bool DoNotDisturb
        {
            get
            {
                try { using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\PushNotifications"); return k?.GetValue("ToastEnabled") is int v && v == 0; }
                catch { return false; }
            }
            set
            {
                try
                {
                    using var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\PushNotifications");
                    k.SetValue("ToastEnabled", value ? 0 : 1, RegistryValueKind.DWord);
                }
                catch (Exception ex) { Log.Write("Do not disturb: " + ex.Message); }
            }
        }

        // ---------- Night light ----------
        private const string NightKey = @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\default$windows.data.bluelightreduction.bluelightreductionstate\windows.data.bluelightreduction.bluelightreductionstate";

        /// <summary>Null when Night light's state can't be read on this PC.</summary>
        public static bool? NightLight
        {
            get
            {
                try
                {
                    using var k = Registry.CurrentUser.OpenSubKey(NightKey);
                    if (!(k?.GetValue("Data") is byte[] d) || d.Length < 24) return null;
                    if (d[18] == 0x15) return true;
                    if (d[18] == 0x13) return false;
                    return null;
                }
                catch { return null; }
            }
        }

        /// <summary>Turns Night light on or off. Returns false if Windows stores it in a way we don't recognise.</summary>
        public static bool SetNightLight(bool on)
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(NightKey, true);
                if (!(k?.GetValue("Data") is byte[] d) || d.Length < 26) return false;
                bool isOn = d[18] == 0x15;
                if (d[18] != 0x15 && d[18] != 0x13) return false;
                if (isOn == on) return true;
                byte[] n;
                if (on)
                {
                    n = new byte[d.Length + 2];
                    Array.Copy(d, 0, n, 0, 23);
                    Array.Copy(d, 23, n, 25, d.Length - 23);
                    n[18] = 0x15;
                    n[23] = 0x10;
                    n[24] = 0x00;
                }
                else
                {
                    n = new byte[d.Length - 2];
                    Array.Copy(d, 0, n, 0, 23);
                    Array.Copy(d, 25, n, 23, d.Length - 25);
                    n[18] = 0x13;
                }
                // Bump the change timestamp so Windows notices.
                for (int i = 10; i < 15; i++)
                {
                    if (n[i] != 0xFF) { n[i]++; break; }
                }
                k.SetValue("Data", n, RegistryValueKind.Binary);
                return true;
            }
            catch (Exception ex) { Log.Write("Night light: " + ex.Message); return false; }
        }

        // ---------- Radios ----------
        public static async Task<bool?> GetRadio(Windows.Devices.Radios.RadioKind kind)
        {
            try
            {
                var radio = (await Windows.Devices.Radios.Radio.GetRadiosAsync()).FirstOrDefault(r => r.Kind == kind);
                if (radio == null) return null;
                return radio.State == Windows.Devices.Radios.RadioState.On;
            }
            catch { return null; }
        }

        public static async Task<bool> SetRadio(Windows.Devices.Radios.RadioKind kind, bool on)
        {
            try
            {
                var access = await Windows.Devices.Radios.Radio.RequestAccessAsync();
                if (access != Windows.Devices.Radios.RadioAccessStatus.Allowed) return false;
                var radio = (await Windows.Devices.Radios.Radio.GetRadiosAsync()).FirstOrDefault(r => r.Kind == kind);
                if (radio == null) return false;
                var result = await radio.SetStateAsync(on ? Windows.Devices.Radios.RadioState.On : Windows.Devices.Radios.RadioState.Off);
                return result == Windows.Devices.Radios.RadioAccessStatus.Allowed;
            }
            catch (Exception ex) { Log.Write("Radio " + kind + ": " + ex.Message); return false; }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);
    }
}

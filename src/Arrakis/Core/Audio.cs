using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Arrakis.Core
{
    /// <summary>An app that is playing (or can play) sound, with its own volume.</summary>
    public sealed class AudioSession
    {
        public int ProcessId;
        public string Name;
        public string Path;
        public float Volume;
        public bool Muted;
        internal object Control;
    }

    public sealed class AudioDevice
    {
        public string Id;
        public string Name;
        public bool IsDefault;
    }

    /// <summary>Windows Core Audio: master volume, per-app volume, the microphone and the output device.</summary>
    public static class Audio
    {
        // ---------- Public API ----------
        public static float GetVolume() => Try(() => { var v = Endpoint(0); v.GetMasterVolumeLevelScalar(out float f); return f; }, 0f);
        public static void SetVolume(float level) => Try(() => { Endpoint(0).SetMasterVolumeLevelScalar(Clamp(level), IntPtr.Zero); return true; }, false);
        public static bool GetMute() => Try(() => { Endpoint(0).GetMute(out bool m); return m; }, false);
        public static void SetMute(bool mute) => Try(() => { Endpoint(0).SetMute(mute, IntPtr.Zero); return true; }, false);

        public static bool HasMicrophone => Try(() => Endpoint(1) != null, false);
        public static bool GetMicMute() => Try(() => { Endpoint(1).GetMute(out bool m); return m; }, false);
        public static void SetMicMute(bool mute) => Try(() => { Endpoint(1).SetMute(mute, IntPtr.Zero); return true; }, false);

        /// <summary>Apps with an audio session on the default output, one row per app.</summary>
        public static List<AudioSession> Sessions()
        {
            var list = new List<AudioSession>();
            try
            {
                var device = DefaultDevice(0);
                var iid = typeof(IAudioSessionManager2).GUID;
                device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out object o);
                var manager = (IAudioSessionManager2)o;
                manager.GetSessionEnumerator(out IAudioSessionEnumerator e);
                e.GetCount(out int count);
                var seen = new HashSet<int>();
                for (int i = 0; i < count; i++)
                {
                    e.GetSession(i, out IAudioSessionControl2 ctl);
                    if (ctl == null) continue;
                    if (ctl.IsSystemSoundsSession() == 0) continue;
                    ctl.GetState(out int state);
                    if (state == 2) continue;   // expired
                    ctl.GetProcessId(out uint pid);
                    if (pid == 0 || !seen.Add((int)pid)) continue;
                    var s = new AudioSession { ProcessId = (int)pid, Control = ctl };
                    try
                    {
                        using var p = Process.GetProcessById((int)pid);
                        s.Name = p.ProcessName;
                        try { s.Path = p.MainModule?.FileName; } catch { }
                        try
                        {
                            if (!string.IsNullOrEmpty(s.Path))
                            {
                                var info = FileVersionInfo.GetVersionInfo(s.Path);
                                if (!string.IsNullOrWhiteSpace(info.FileDescription)) s.Name = info.FileDescription;
                            }
                        }
                        catch { }
                    }
                    catch { continue; }
                    var vol = (ISimpleAudioVolume)ctl;
                    vol.GetMasterVolume(out s.Volume);
                    vol.GetMute(out s.Muted);
                    list.Add(s);
                }
            }
            catch (Exception ex) { Log.Write("Audio sessions: " + ex.Message); }
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            return list;
        }

        public static void SetSessionVolume(AudioSession s, float level)
        {
            try { ((ISimpleAudioVolume)s.Control).SetMasterVolume(Clamp(level), IntPtr.Zero); s.Volume = level; } catch { }
        }

        public static void SetSessionMute(AudioSession s, bool mute)
        {
            try { ((ISimpleAudioVolume)s.Control).SetMute(mute, IntPtr.Zero); s.Muted = mute; } catch { }
        }

        /// <summary>Speakers, headphones and other outputs that are plugged in.</summary>
        public static List<AudioDevice> OutputDevices()
        {
            var list = new List<AudioDevice>();
            try
            {
                var en = Enumerator();
                string def = null;
                try { en.GetDefaultAudioEndpoint(0, 1, out IMMDevice d); d.GetId(out def); } catch { }
                en.EnumAudioEndpoints(0, 1 /* active */, out IMMDeviceCollection col);
                col.GetCount(out uint n);
                for (uint i = 0; i < n; i++)
                {
                    col.Item(i, out IMMDevice dev);
                    dev.GetId(out string id);
                    list.Add(new AudioDevice { Id = id, Name = FriendlyName(dev) ?? "Audio device", IsDefault = id == def });
                }
            }
            catch (Exception ex) { Log.Write("Audio devices: " + ex.Message); }
            return list;
        }

        /// <summary>Makes a device the default for everything (music, calls and system sounds).</summary>
        public static bool SetDefaultOutput(string id)
        {
            try
            {
                var policy = (IPolicyConfig)new PolicyConfigClient();
                for (int role = 0; role < 3; role++) Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(id, role));
                return true;
            }
            catch (Exception ex) { Log.Write("Set output device: " + ex.Message); return false; }
        }

        // ---------- Plumbing ----------
        private const int ClsCtxAll = 23;

        private static float Clamp(float f) => Math.Max(0f, Math.Min(1f, f));

        private static T Try<T>(Func<T> f, T fallback)
        {
            try { return f(); } catch { return fallback; }
        }

        private static IMMDeviceEnumerator Enumerator() => (IMMDeviceEnumerator)new MMDeviceEnumerator();

        private static IMMDevice DefaultDevice(int flow)
        {
            Marshal.ThrowExceptionForHR(Enumerator().GetDefaultAudioEndpoint(flow, 1, out IMMDevice d));
            return d;
        }

        private static IAudioEndpointVolume Endpoint(int flow)
        {
            var dev = DefaultDevice(flow);
            var iid = typeof(IAudioEndpointVolume).GUID;
            dev.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out object o);
            return (IAudioEndpointVolume)o;
        }

        private static string FriendlyName(IMMDevice dev)
        {
            try
            {
                dev.OpenPropertyStore(0, out IPropertyStore store);
                var key = new PropertyKey { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };
                store.GetValue(ref key, out PropVariant v);
                string s = v.vt == 31 ? Marshal.PtrToStringUni(v.p) : null;
                PropVariantClear(ref v);
                return s;
            }
            catch { return null; }
        }

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref PropVariant pvar);

        [StructLayout(LayoutKind.Sequential)]
        private struct PropertyKey { public Guid fmtid; public int pid; }

        [StructLayout(LayoutKind.Sequential)]
        private struct PropVariant
        {
            public ushort vt;
            public ushort r1, r2, r3;
            public IntPtr p;
            public IntPtr p2;
        }

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumerator { }

        [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
        private class PolicyConfigClient { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
            [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
            [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
            [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
        }

        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceCollection
        {
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int Item(uint index, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
            [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore properties);
            [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetState(out int state);
        }

        [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int GetAt(uint index, out PropertyKey key);
            [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
            [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
            [PreserveSig] int Commit();
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int GetChannelCount(out uint count);
            [PreserveSig] int SetMasterVolumeLevel(float levelDb, IntPtr context);
            [PreserveSig] int SetMasterVolumeLevelScalar(float level, IntPtr context);
            [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
            [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
            [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, IntPtr context);
            [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, IntPtr context);
            [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
            [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, IntPtr context);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
            [PreserveSig] int GetVolumeStepInfo(out uint step, out uint stepCount);
            [PreserveSig] int VolumeStepUp(IntPtr context);
            [PreserveSig] int VolumeStepDown(IntPtr context);
            [PreserveSig] int QueryHardwareSupport(out uint mask);
            [PreserveSig] int GetVolumeRange(out float min, out float max, out float increment);
        }

        [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionManager2
        {
            [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, uint flags, out IntPtr control);
            [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, uint flags, out IntPtr volume);
            [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);
            [PreserveSig] int RegisterSessionNotification(IntPtr notify);
            [PreserveSig] int UnregisterSessionNotification(IntPtr notify);
            [PreserveSig] int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionId, IntPtr notify);
            [PreserveSig] int UnregisterDuckNotification(IntPtr notify);
        }

        [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionEnumerator
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int GetSession(int index, out IAudioSessionControl2 session);
        }

        [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionControl2
        {
            // IAudioSessionControl
            [PreserveSig] int GetState(out int state);
            [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr context);
            [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
            [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, IntPtr context);
            [PreserveSig] int GetGroupingParam(out Guid grouping);
            [PreserveSig] int SetGroupingParam(ref Guid grouping, IntPtr context);
            [PreserveSig] int RegisterAudioSessionNotification(IntPtr notify);
            [PreserveSig] int UnregisterAudioSessionNotification(IntPtr notify);
            // IAudioSessionControl2
            [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetProcessId(out uint pid);
            [PreserveSig] int IsSystemSoundsSession();
            [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
        }

        [ComImport, Guid("87CE5498-68D6-44E5-9215-6F4BC0E1F4A4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISimpleAudioVolume
        {
            [PreserveSig] int SetMasterVolume(float level, IntPtr context);
            [PreserveSig] int GetMasterVolume(out float level);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, IntPtr context);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }

        [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPolicyConfig
        {
            [PreserveSig] int GetMixFormat(IntPtr a, IntPtr b);
            [PreserveSig] int GetDeviceFormat(IntPtr a, int b, IntPtr c);
            [PreserveSig] int ResetDeviceFormat(IntPtr a);
            [PreserveSig] int SetDeviceFormat(IntPtr a, IntPtr b, IntPtr c);
            [PreserveSig] int GetProcessingPeriod(IntPtr a, int b, IntPtr c, IntPtr d);
            [PreserveSig] int SetProcessingPeriod(IntPtr a, IntPtr b);
            [PreserveSig] int GetShareMode(IntPtr a, IntPtr b);
            [PreserveSig] int SetShareMode(IntPtr a, IntPtr b);
            [PreserveSig] int GetPropertyValue(IntPtr a, IntPtr b, IntPtr c);
            [PreserveSig] int SetPropertyValue(IntPtr a, IntPtr b, IntPtr c);
            [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);
            [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int visible);
        }
    }
}

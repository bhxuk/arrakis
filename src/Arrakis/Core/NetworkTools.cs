using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Arrakis.Core
{
    // ======================================================================
    //  Ping, every few seconds, shared by the Network page and the widget
    // ======================================================================
    public static class NetPing
    {
        public sealed class Sample { public DateTime Time; public int? Ms; }

        public static readonly List<Sample> Samples = new List<Sample>();
        public static event Action Updated;
        /// <summary>Raised with true when the connection drops, false when it comes back.</summary>
        public static event Action<bool, TimeSpan> ConnectionChanged;

        private static DispatcherTimer _timer;
        private static int _users;
        private static int _fails;
        private static bool _down;
        private static DateTime _downSince;
        private static bool _busy;

        public static string Host => string.IsNullOrWhiteSpace(Settings.Current.Network.PingHost) ? "1.1.1.1" : Settings.Current.Network.PingHost.Trim();

        public static void Acquire()
        {
            _users++;
            if (_timer != null) return;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _timer.Tick += async (s, e) => await Tick();
            _timer.Start();
            _ = Tick();
        }

        public static void Release()
        {
            _users = Math.Max(0, _users - 1);
            if (_users == 0 && _timer != null) { _timer.Stop(); _timer = null; }
        }

        public static int? Latest => Samples.Count == 0 ? null : Samples[^1].Ms;

        /// <summary>Median, jitter and loss over the last few minutes.</summary>
        public static (int? median, int? jitter, double loss) Stats(int count = 60)
        {
            var recent = Samples.Skip(Math.Max(0, Samples.Count - count)).ToList();
            if (recent.Count == 0) return (null, null, 0);
            var ok = recent.Where(x => x.Ms.HasValue).Select(x => x.Ms.Value).ToList();
            double loss = 100.0 * (recent.Count - ok.Count) / recent.Count;
            if (ok.Count == 0) return (null, null, loss);
            var sorted = ok.OrderBy(x => x).ToList();
            int median = sorted[sorted.Count / 2];
            int jitter = ok.Count < 2 ? 0 : (int)Math.Round(ok.Zip(ok.Skip(1), (a, b) => Math.Abs(a - b)).Average());
            return (median, jitter, loss);
        }

        private static async Task Tick()
        {
            if (_busy) return;
            _busy = true;
            int? ms = null;
            try
            {
                using var p = new Ping();
                var r = await p.SendPingAsync(Host, 2000);
                if (r.Status == IPStatus.Success) ms = (int)r.RoundtripTime;
            }
            catch { }
            _busy = false;
            Samples.Add(new Sample { Time = DateTime.Now, Ms = ms });
            if (Samples.Count > 200) Samples.RemoveRange(0, Samples.Count - 200);

            if (ms == null) _fails++; else _fails = 0;
            if (!_down && _fails >= 3) { _down = true; _downSince = DateTime.Now.AddSeconds(-9); ConnectionChanged?.Invoke(true, TimeSpan.Zero); }
            else if (_down && ms != null) { _down = false; ConnectionChanged?.Invoke(false, DateTime.Now - _downSince); }
            Updated?.Invoke();
        }
    }

    // ======================================================================
    //  What you're connected to
    // ======================================================================
    public sealed class ConnectionInfo
    {
        public bool Connected;
        public string Kind = "None";        // Wi-Fi, Ethernet, None
        public string AdapterName = "";
        public long LinkMbps;
        public string LocalIp = "";
        public string Gateway = "";
        public string Dns = "";
        public string Mac = "";
        public int PrefixLength = 24;
        // Wi-Fi only
        public string Ssid = "";
        public int Signal = -1;
        public int Channel;
        public string Band = "";
        public int RxMbps, TxMbps;
        public string Standard = "";
        public int SameChannelNetworks = -1;
        public int NetworksNearby = -1;
        public bool FiveGhzSeen;
    }

    public static class NetInfo
    {
        public static ConnectionInfo Current()
        {
            var info = new ConnectionInfo();
            try
            {
                var nic = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                    .Select(n => (n, p: n.GetIPProperties()))
                    .Where(x => x.p.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)))
                    .OrderByDescending(x => x.n.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? 2 : x.n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 1 : 0)
                    .FirstOrDefault();
                if (nic.n == null) return info;
                info.Connected = true;
                info.AdapterName = nic.n.Description;
                info.Kind = nic.n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "Wi-Fi" : "Ethernet";
                info.LinkMbps = nic.n.Speed > 0 ? nic.n.Speed / 1_000_000 : 0;
                var v4 = nic.p.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
                if (v4 != null) { info.LocalIp = v4.Address.ToString(); info.PrefixLength = v4.PrefixLength; }
                info.Gateway = nic.p.GatewayAddresses.First(g => g.Address.AddressFamily == AddressFamily.InterNetwork).Address.ToString();
                info.Dns = string.Join(", ", nic.p.DnsAddresses.Where(d => d.AddressFamily == AddressFamily.InterNetwork).Select(d => d.ToString()).Take(2));
                info.Mac = string.Join(":", nic.n.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2")));
                if (info.Kind == "Wi-Fi") Wlan.Fill(info);
            }
            catch (Exception ex) { Log.Write("Network info: " + ex.Message); }
            return info;
        }

        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };

        public static async Task<string> PublicIp()
        {
            try { return (await Http.GetStringAsync("https://api.ipify.org")).Trim(); }
            catch { return null; }
        }
    }

    /// <summary>Windows' Native Wi-Fi API: the network you're on, signal, channel and what's nearby.</summary>
    internal static class Wlan
    {
        public static void Fill(ConnectionInfo info)
        {
            IntPtr handle = IntPtr.Zero, list = IntPtr.Zero;
            try
            {
                if (WlanOpenHandle(2, IntPtr.Zero, out _, out handle) != 0) return;
                if (WlanEnumInterfaces(handle, IntPtr.Zero, out list) != 0) return;
                int count = Marshal.ReadInt32(list);
                int size = Marshal.SizeOf<InterfaceInfo>();
                for (int i = 0; i < count; i++)
                {
                    var ii = Marshal.PtrToStructure<InterfaceInfo>(list + 8 + i * size);
                    if (ii.State != 1) continue;   // connected
                    var guid = ii.Guid;
                    if (WlanQueryInterface(handle, ref guid, 7 /* current_connection */, IntPtr.Zero, out _, out IntPtr data, out _) == 0)
                    {
                        var c = Marshal.PtrToStructure<ConnectionAttributes>(data);
                        info.Ssid = Encoding.UTF8.GetString(c.Assoc.Ssid.Bytes, 0, (int)Math.Min(32, c.Assoc.Ssid.Length));
                        info.Signal = (int)c.Assoc.SignalQuality;
                        info.RxMbps = (int)(c.Assoc.RxRate / 1000);
                        info.TxMbps = (int)(c.Assoc.TxRate / 1000);
                        info.Standard = c.Assoc.PhyType switch { 4 => "802.11a", 5 => "802.11b", 6 => "802.11g", 7 => "Wi-Fi 4", 8 => "Wi-Fi 5", 10 => "Wi-Fi 6", 11 => "Wi-Fi 7", _ => "" };
                        WlanFreeMemory(data);
                    }
                    if (WlanQueryInterface(handle, ref guid, 8 /* channel_number */, IntPtr.Zero, out _, out IntPtr ch, out _) == 0)
                    {
                        info.Channel = Marshal.ReadInt32(ch);
                        WlanFreeMemory(ch);
                    }
                    // What else is around, and how crowded our channel is.
                    if (WlanGetNetworkBssList(handle, ref guid, IntPtr.Zero, 0 /* any */, false, IntPtr.Zero, out IntPtr bss) == 0)
                    {
                        int n = Marshal.ReadInt32(bss + 4);
                        int esize = Marshal.SizeOf<BssEntry>();
                        var freqs = new List<uint>();
                        uint myFreq = 0;
                        var ssids = new HashSet<string>();
                        for (int j = 0; j < n; j++)
                        {
                            var e = Marshal.PtrToStructure<BssEntry>(bss + 8 + j * esize);
                            string ssid = Encoding.UTF8.GetString(e.Ssid.Bytes, 0, (int)Math.Min(32, e.Ssid.Length));
                            freqs.Add(e.ChCenterFrequency);
                            ssids.Add(ssid + "|" + (e.ChCenterFrequency / 1000));
                            if (ssid == info.Ssid && e.ChCenterFrequency >= 4900000) info.FiveGhzSeen = true;
                            if (ssid == info.Ssid && FreqToChannel(e.ChCenterFrequency) == info.Channel) myFreq = e.ChCenterFrequency;
                        }
                        info.NetworksNearby = ssids.Count;
                        if (myFreq > 0) info.SameChannelNetworks = Math.Max(0, freqs.Count(f => f == myFreq) - 1);
                        WlanFreeMemory(bss);
                    }
                    info.Band = info.Channel > 0 ? (info.Channel <= 14 ? "2.4 GHz" : "5 GHz") : "";
                    break;
                }
            }
            catch (Exception ex) { Log.Write("Wi-Fi info: " + ex.Message); }
            finally
            {
                if (list != IntPtr.Zero) WlanFreeMemory(list);
                if (handle != IntPtr.Zero) WlanCloseHandle(handle, IntPtr.Zero);
            }
        }

        private static int FreqToChannel(uint khz)
        {
            uint mhz = khz / 1000;
            if (mhz == 2484) return 14;
            if (mhz >= 2412 && mhz <= 2472) return (int)(mhz - 2407) / 5;
            if (mhz >= 5000 && mhz <= 5900) return (int)(mhz - 5000) / 5;
            return 0;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct InterfaceInfo
        {
            public Guid Guid;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Description;
            public int State;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Dot11Ssid
        {
            public uint Length;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Bytes;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AssociationAttributes
        {
            public Dot11Ssid Ssid;
            public int BssType;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)] public byte[] Bssid;
            public int PhyType;
            public uint PhyIndex;
            public uint SignalQuality;
            public uint RxRate;
            public uint TxRate;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SecurityAttributes
        {
            public int SecurityEnabled;
            public int OneXEnabled;
            public int AuthAlgorithm;
            public int CipherAlgorithm;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ConnectionAttributes
        {
            public int State;
            public int Mode;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string ProfileName;
            public AssociationAttributes Assoc;
            public SecurityAttributes Security;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BssEntry
        {
            public Dot11Ssid Ssid;
            public uint PhyId;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)] public byte[] Bssid;
            public int BssType;
            public int PhyType;
            public int Rssi;
            public uint LinkQuality;
            public byte InRegDomain;
            public ushort BeaconPeriod;
            public ulong Timestamp;
            public ulong HostTimestamp;
            public ushort Capability;
            public uint ChCenterFrequency;
            public uint RateSetLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 126)] public ushort[] Rates;
            public uint IeOffset;
            public uint IeSize;
        }

        [DllImport("wlanapi.dll")] private static extern int WlanOpenHandle(uint version, IntPtr reserved, out uint negotiated, out IntPtr handle);
        [DllImport("wlanapi.dll")] private static extern int WlanCloseHandle(IntPtr handle, IntPtr reserved);
        [DllImport("wlanapi.dll")] private static extern int WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr list);
        [DllImport("wlanapi.dll")] private static extern int WlanQueryInterface(IntPtr handle, ref Guid iface, int opCode, IntPtr reserved, out int dataSize, out IntPtr data, out int valueType);
        [DllImport("wlanapi.dll")] private static extern int WlanGetNetworkBssList(IntPtr handle, ref Guid iface, IntPtr ssid, int bssType, bool securityEnabled, IntPtr reserved, out IntPtr list);
        [DllImport("wlanapi.dll")] private static extern void WlanFreeMemory(IntPtr memory);
    }

    // ======================================================================
    //  Speed test (Cloudflare's public speed test servers, no account)
    // ======================================================================
    public sealed class SpeedResult
    {
        public DateTime Time { get; set; } = DateTime.Now;
        public double DownMbps { get; set; }
        public double UpMbps { get; set; }
        public int PingMs { get; set; }
        public int JitterMs { get; set; }
        public string Network { get; set; } = "";
    }

    public static class SpeedTest
    {
        private const string Base = "https://speed.cloudflare.com/";

        /// <summary>progress(stage, current Mbps or ms, 0..1)</summary>
        public static async Task<SpeedResult> Run(Action<string, double, double> progress, CancellationToken ct)
        {
            var result = new SpeedResult();
            using var http = new HttpClient(new SocketsHttpHandler { MaxConnectionsPerServer = 8, PooledConnectionLifetime = TimeSpan.FromMinutes(1) }) { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Arrakis/" + Updates.CurrentText);

            // Latency: small requests, take the median.
            var times = new List<double>();
            for (int i = 0; i < 12 && !ct.IsCancellationRequested; i++)
            {
                var sw = Stopwatch.StartNew();
                using (var r = await http.GetAsync(Base + "__down?bytes=0", HttpCompletionOption.ResponseHeadersRead, ct)) { }
                sw.Stop();
                if (i > 1) times.Add(sw.Elapsed.TotalMilliseconds);   // skip the first two (connection setup)
                progress("Ping", sw.Elapsed.TotalMilliseconds, (i + 1) / 12.0 * 0.1);
            }
            if (times.Count > 0)
            {
                var sorted = times.OrderBy(x => x).ToList();
                result.PingMs = (int)Math.Round(sorted[sorted.Count / 2]);
                result.JitterMs = times.Count < 2 ? 0 : (int)Math.Round(times.Zip(times.Skip(1), (a, b) => Math.Abs(a - b)).Average());
            }

            // Download: several parallel streams for about 8 seconds.
            result.DownMbps = await Measure(async (token, add) =>
            {
                while (!token.IsCancellationRequested)
                {
                    using var resp = await http.GetAsync(Base + "__down?bytes=25000000", HttpCompletionOption.ResponseHeadersRead, token);
                    using var stream = await resp.Content.ReadAsStreamAsync(token);
                    var buf = new byte[81920];
                    int n;
                    while ((n = await stream.ReadAsync(buf, 0, buf.Length, token)) > 0) add(n);
                }
            }, 6, TimeSpan.FromSeconds(8), _ => { }, 0.1, 0.6, progress, ct);

            // Upload: parallel POSTs for about 6 seconds.
            var payload = new byte[4_000_000];
            new Random(1).NextBytes(payload);
            result.UpMbps = await Measure(async (token, add) =>
            {
                while (!token.IsCancellationRequested)
                {
                    var content = new ProgressContent(payload, add);
                    using var resp = await http.PostAsync(Base + "__up", content, token);
                }
            }, 4, TimeSpan.FromSeconds(6), _ => { }, 0.7, 0.3, progress, ct);

            progress("Done", 0, 1);
            return result;
        }

        private static async Task<double> Measure(Func<CancellationToken, Action<int>, Task> worker, int streams, TimeSpan duration,
            Action<double> live, double startFraction, double share, Action<string, double, double> progress, CancellationToken outer)
        {
            long bytes = 0;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(outer);
            var sw = Stopwatch.StartNew();
            void Add(int n) => Interlocked.Add(ref bytes, n);
            var tasks = Enumerable.Range(0, streams).Select(_ => Task.Run(async () => { try { await worker(cts.Token, Add); } catch { } })).ToList();

            // Ignore the first second (TCP ramp-up), then measure.
            await Task.Delay(1000, outer).ContinueWith(_ => { });
            long startBytes = Interlocked.Read(ref bytes);
            var mark = sw.Elapsed;
            double mbps = 0;
            while (sw.Elapsed < duration && !outer.IsCancellationRequested)
            {
                await Task.Delay(250).ConfigureAwait(true);
                double secs = (sw.Elapsed - mark).TotalSeconds;
                if (secs > 0) mbps = (Interlocked.Read(ref bytes) - startBytes) * 8 / secs / 1_000_000;
                live(mbps);
                string stage = startFraction < 0.5 ? "Download" : "Upload";
                progress(stage, mbps, startFraction + share * Math.Min(1, sw.Elapsed.TotalSeconds / duration.TotalSeconds));
            }
            cts.Cancel();
            try { await Task.WhenAll(tasks); } catch { }
            return Math.Round(mbps, 1);
        }

        /// <summary>Upload body that reports bytes as they're sent.</summary>
        private sealed class ProgressContent : HttpContent
        {
            private readonly byte[] _data;
            private readonly Action<int> _add;
            public ProgressContent(byte[] data, Action<int> add) { _data = data; _add = add; Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream"); }
            protected override async Task SerializeToStreamAsync(System.IO.Stream stream, TransportContext context)
            {
                const int chunk = 65536;
                for (int i = 0; i < _data.Length; i += chunk)
                {
                    int n = Math.Min(chunk, _data.Length - i);
                    await stream.WriteAsync(_data, i, n);
                    _add(n);
                }
            }
            protected override bool TryComputeLength(out long length) { length = _data.Length; return true; }
        }
    }

    // ======================================================================
    //  Devices on your network
    // ======================================================================
    public sealed class LanDevice
    {
        public string Ip;
        public string Name;
        public string Mac;
        public bool IsThisPc;
        public bool IsRouter;
        public bool PrivateMac;
        public int? Ms;
    }

    public static class LanScan
    {
        /// <summary>Pings every address on your local network (up to 254), then reads Windows' address table to find who answered.</summary>
        public static async Task<List<LanDevice>> Run(ConnectionInfo info, Action<double> progress)
        {
            var found = new Dictionary<string, LanDevice>();
            if (!info.Connected || !IPAddress.TryParse(info.LocalIp, out var local)) return new List<LanDevice>();
            var b = local.GetAddressBytes();
            int prefix = Math.Max(24, info.PrefixLength);   // stay within a /24 so a scan is quick and polite
            uint mask = prefix >= 32 ? 0xFFFFFFFF : ~((1u << (32 - prefix)) - 1);
            uint ip = (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
            uint net = ip & mask, broadcast = net | ~mask;
            var targets = new List<IPAddress>();
            for (uint a = net + 1; a < broadcast; a++) targets.Add(new IPAddress(new[] { (byte)(a >> 24), (byte)(a >> 16), (byte)(a >> 8), (byte)a }));

            var times = new Dictionary<string, int>();
            int done = 0;
            using var gate = new SemaphoreSlim(48);
            await Task.WhenAll(targets.Select(async t =>
            {
                await gate.WaitAsync();
                try
                {
                    using var p = new Ping();
                    var r = await p.SendPingAsync(t, 700);
                    if (r.Status == IPStatus.Success) lock (times) times[t.ToString()] = (int)r.RoundtripTime;
                }
                catch { }
                finally
                {
                    gate.Release();
                    int d = Interlocked.Increment(ref done);
                    progress(d / (double)targets.Count * 0.8);
                }
            }));

            // Devices that don't answer pings still show up in the ARP table once we've tried them.
            foreach (var (addr, mac) in ArpTable())
            {
                if (!targets.Any(t => t.ToString() == addr)) continue;
                found[addr] = new LanDevice { Ip = addr, Mac = mac };
            }
            foreach (var kv in times)
                if (!found.ContainsKey(kv.Key)) found[kv.Key] = new LanDevice { Ip = kv.Key };
            foreach (var kv in times) found[kv.Key].Ms = kv.Value;
            if (!found.ContainsKey(info.LocalIp)) found[info.LocalIp] = new LanDevice { Ip = info.LocalIp, Mac = info.Mac };

            var list = found.Values.ToList();
            int named = 0;
            await Task.WhenAll(list.Select(async dev =>
            {
                dev.IsThisPc = dev.Ip == info.LocalIp;
                dev.IsRouter = dev.Ip == info.Gateway;
                if (dev.IsThisPc) dev.Name = Environment.MachineName;
                if (!string.IsNullOrEmpty(dev.Mac) && dev.Mac.Length >= 2 && (Convert.ToByte(dev.Mac.Substring(0, 2), 16) & 0x02) != 0) dev.PrivateMac = true;
                if (dev.Name == null)
                {
                    try
                    {
                        var lookup = Dns.GetHostEntryAsync(dev.Ip);
                        if (await Task.WhenAny(lookup, Task.Delay(1500)) == lookup && !string.IsNullOrEmpty(lookup.Result.HostName) && lookup.Result.HostName != dev.Ip)
                            dev.Name = lookup.Result.HostName.Split('.')[0];
                    }
                    catch { }
                }
                progress(0.8 + 0.2 * Interlocked.Increment(ref named) / list.Count);
            }));
            return list.OrderBy(d => d.IsThisPc ? 1 : 2).ThenBy(d => d.IsRouter ? 0 : 1).ThenBy(d => Version.TryParse(d.Ip, out var v) ? v : new Version()).ToList();
        }

        private static List<(string ip, string mac)> ArpTable()
        {
            var list = new List<(string, string)>();
            int size = 0;
            GetIpNetTable(IntPtr.Zero, ref size, false);
            if (size <= 0) return list;
            IntPtr buf = Marshal.AllocHGlobal(size);
            try
            {
                if (GetIpNetTable(buf, ref size, true) != 0) return list;
                int n = Marshal.ReadInt32(buf);
                int rowSize = Marshal.SizeOf<IpNetRow>();
                for (int i = 0; i < n; i++)
                {
                    var row = Marshal.PtrToStructure<IpNetRow>(buf + 4 + i * rowSize);
                    if (row.Type == 2 /* invalid */ || row.PhysAddrLen != 6) continue;
                    var ip = new IPAddress(BitConverter.GetBytes(row.Addr)).ToString();
                    var mac = string.Join(":", row.PhysAddr.Take(6).Select(x => x.ToString("X2")));
                    if (mac == "FF:FF:FF:FF:FF:FF" || mac == "00:00:00:00:00:00") continue;
                    list.Add((ip, mac));
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
            return list;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IpNetRow
        {
            public int Index;
            public int PhysAddrLen;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] PhysAddr;
            public uint Addr;
            public int Type;
        }

        [DllImport("iphlpapi.dll")] private static extern int GetIpNetTable(IntPtr table, ref int size, bool sort);
    }
}

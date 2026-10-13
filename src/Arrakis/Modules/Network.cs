using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Arrakis.Core;

namespace Arrakis.Modules
{
    public class SpeedHistory
    {
        public List<SpeedResult> Results { get; set; } = new List<SpeedResult>();
    }

    /// <summary>Your connection at a glance: speed test, live ping, what's slowing Wi-Fi down, and who else is on the network.</summary>
    public class NetworkModule : ModuleBase
    {
        public override string Id => "network";
        public override string Name => "Network";
        public override string Description => "Speed test, a live ping graph, tips for slow Wi-Fi, the devices on your network and alerts when the internet drops.";
        public override string IconData => Icons.Wifi;
        public override string ShortcutText => "Speed test";
        public override bool IsEnabled { get => S.Enabled; set => S.Enabled = value; }

        private NetworkSettings S => Settings.Current.Network;
        private CancellationTokenSource _test;
        public bool StartTestWhenShown { get; set; }

        protected override void OnStart()
        {
            NetPing.Acquire();
            NetPing.ConnectionChanged += OnConnectionChanged;
        }

        protected override void OnStop()
        {
            NetPing.ConnectionChanged -= OnConnectionChanged;
            NetPing.Release();
            _test?.Cancel();
        }

        private void OnConnectionChanged(bool down, TimeSpan downFor)
        {
            if (!S.DropAlerts) return;
            if (down) App.Instance.Tray.Notify("Internet connection lost", "Arrakis will tell you when it's back.");
            else App.Instance.Tray.Notify("Back online", "You were offline for " + Friendly(downFor) + ".");
        }

        private static string Friendly(TimeSpan t) => t.TotalMinutes >= 1 ? (int)t.TotalMinutes + " min " + t.Seconds + " s" : Math.Max(1, (int)t.TotalSeconds) + " seconds";

        /// <summary>Opens the Network page and starts a speed test.</summary>
        public void RunSpeedTestFromAnywhere()
        {
            if (!IsRunning) App.Instance.SetModuleEnabled(this, true);
            StartTestWhenShown = true;
            App.Instance.ShowHub(Id);
        }

        public override UIElement BuildPage()
        {
            var root = UI.V(16);
            var info = new ConnectionInfo();

            // ---------- Connection ----------
            var connTitle = UI.Text("Checking your connection...", 20, Theme.Text, FontWeights.SemiBold, true);
            var connSub = UI.Text("", 14, Theme.Muted);
            var signal = new Ring(84, 8, Theme.Teal, "", 16);
            var details = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(0, 16, 0, 0) };
            var publicIp = UI.Text("Hidden", 14, Theme.Text, FontWeights.SemiBold);
            var showIp = FlatButton.Secondary("Show");
            showIp.MinHeight = 28;
            showIp.Padding = new Thickness(10, 2, 10, 2);
            showIp.Click += async (s, e) => { showIp.IsEnabled = false; publicIp.Text = await NetInfo.PublicIp() ?? "Couldn't check"; showIp.Visibility = Visibility.Collapsed; };
            UIElement Fact(string label, UIElement value) => new Border { Padding = new Thickness(0, 0, 12, 12), Child = UI.V(2, UI.Label(label), value) };
            var signalHost = new Grid { Width = 84, Height = 84, Margin = new Thickness(0, 0, 20, 0) };
            signalHost.Children.Add(signal);
            var head = new DockPanel();
            DockPanel.SetDock(signalHost, System.Windows.Controls.Dock.Left);
            head.Children.Add(signalHost);
            head.Children.Add(UI.V(4, connTitle, connSub));
            head.VerticalAlignment = VerticalAlignment.Center;
            root.Children.Add(UI.Card(UI.V(0, head, details), 22));

            // ---------- Speed test ----------
            var down = UI.Text("-", 34, Theme.Text, FontWeights.SemiBold, true);
            var up = UI.Text("-", 34, Theme.Text, FontWeights.SemiBold, true);
            var ping = UI.Text("-", 34, Theme.Text, FontWeights.SemiBold, true);
            var stage = UI.Text("Takes about 20 seconds and uses roughly 100 to 300 MB on a fast connection.", 13, Theme.Muted);
            var bar = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = Theme.Amber, HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            var track = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = Theme.Subtle, Child = bar, Margin = new Thickness(0, 10, 0, 0) };
            UIElement Gauge(string label, TextBlock value, string unit) => UI.V(2, UI.Label(label), UI.H(6, value, UI.Text(unit, 14, Theme.Muted).WithMargin(new Thickness(0, 14, 0, 0))));
            var gauges = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3 };
            gauges.Children.Add(Gauge("Download", down, "Mbps"));
            gauges.Children.Add(Gauge("Upload", up, "Mbps"));
            gauges.Children.Add(Gauge("Ping", ping, "ms"));
            var history = UI.V(4);
            var runBtn = FlatButton.Primary("Run speed test");
            void FillHistory()
            {
                history.Children.Clear();
                var hist = Settings.LoadFile<SpeedHistory>("speedtests.json").Results.OrderByDescending(r => r.Time).Take(5).ToList();
                foreach (var r in hist)
                {
                    var row = new DockPanel();
                    var nums = UI.Text(r.DownMbps.ToString("0.#") + " down  ·  " + r.UpMbps.ToString("0.#") + " up  ·  " + r.PingMs + " ms", 13, Theme.Text);
                    DockPanel.SetDock(nums, System.Windows.Controls.Dock.Right);
                    row.Children.Add(nums);
                    row.Children.Add(UI.Text(r.Time.ToString("ddd d MMM, HH:mm") + (string.IsNullOrEmpty(r.Network) ? "" : "  ·  " + r.Network), 13, Theme.Muted));
                    history.Children.Add(row);
                }
                if (hist.Count == 0) history.Children.Add(UI.Text("Your results will be kept here so you can compare.", 13, Theme.Muted));
            }
            FillHistory();
            async void RunTest()
            {
                if (_test != null) { _test.Cancel(); return; }
                _test = new CancellationTokenSource();
                runBtn.Content = "Stop";
                down.Text = up.Text = ping.Text = "-";
                try
                {
                    var r = await SpeedTest.Run((st, val, frac) =>
                    {
                        root.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (st == "Ping") { ping.Text = ((int)val).ToString(); stage.Text = "Measuring ping..."; }
                            else if (st == "Download") { down.Text = val.ToString(val < 10 ? "0.0" : "0"); stage.Text = "Measuring download..."; }
                            else if (st == "Upload") { up.Text = val.ToString(val < 10 ? "0.0" : "0"); stage.Text = "Measuring upload..."; }
                            if (frac > 0) bar.Width = Math.Max(0, track.ActualWidth * Math.Min(1, frac));
                        }));
                    }, _test.Token);
                    down.Text = r.DownMbps.ToString(r.DownMbps < 10 ? "0.0" : "0");
                    up.Text = r.UpMbps.ToString(r.UpMbps < 10 ? "0.0" : "0");
                    ping.Text = r.PingMs.ToString();
                    r.Network = info.Kind == "Wi-Fi" ? info.Ssid : info.Kind;
                    stage.Text = "Jitter " + r.JitterMs + " ms. " + Verdict(r);
                    bar.Width = track.ActualWidth;
                    var hist = Settings.LoadFile<SpeedHistory>("speedtests.json");
                    hist.Results.Add(r);
                    if (hist.Results.Count > 50) hist.Results.RemoveAt(0);
                    Settings.SaveFile("speedtests.json", hist);
                    FillHistory();
                }
                catch (OperationCanceledException) { stage.Text = "Stopped."; }
                catch (Exception ex) { stage.Text = "Couldn't finish the test: " + ex.Message; }
                finally { _test?.Dispose(); _test = null; runBtn.Content = "Run again"; }
            }
            runBtn.Click += (s, e) => RunTest();
            var testHead = new DockPanel();
            DockPanel.SetDock(runBtn, System.Windows.Controls.Dock.Right);
            testHead.Children.Add(runBtn);
            testHead.Children.Add(UI.Text("Speed test", 18, Theme.Text, FontWeights.SemiBold, true));
            root.Children.Add(UI.Card(UI.V(14, testHead, gauges, UI.V(4, track, stage), UI.Divider(), UI.Label("Recent tests"), history), 22));

            // ---------- Live ping ----------
            var chart = new Canvas { Height = 110, ClipToBounds = true };
            var line = new Polyline { Stroke = Theme.Teal, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
            chart.Children.Add(line);
            var pingNow = UI.Text("-", 22, Theme.Text, FontWeights.SemiBold, true);
            var pingStats = UI.Text("", 13, Theme.Muted);
            void DrawPing()
            {
                var pts = NetPing.Samples.Skip(Math.Max(0, NetPing.Samples.Count - 60)).ToList();
                line.Points.Clear();
                foreach (var dot in chart.Children.OfType<Ellipse>().ToList()) chart.Children.Remove(dot);
                double w = chart.ActualWidth > 0 ? chart.ActualWidth : 600, h = chart.Height;
                int max = Math.Max(50, pts.Where(p => p.Ms.HasValue).Select(p => p.Ms.Value).DefaultIfEmpty(0).Max() + 10);
                for (int i = 0; i < pts.Count; i++)
                {
                    double x = pts.Count == 1 ? w : i * w / 59.0;
                    if (pts[i].Ms.HasValue) line.Points.Add(new Point(x, h - 6 - (h - 12) * pts[i].Ms.Value / max));
                    else
                    {
                        var miss = new Ellipse { Width = 6, Height = 6, Fill = Theme.Red };
                        Canvas.SetLeft(miss, x - 3);
                        Canvas.SetTop(miss, h - 8);
                        chart.Children.Add(miss);
                    }
                }
                var (median, jitter, loss) = NetPing.Stats();
                pingNow.Text = NetPing.Latest.HasValue ? NetPing.Latest + " ms" : NetPing.Samples.Count == 0 ? "-" : "No reply";
                pingStats.Text = median.HasValue ? "Typical " + median + " ms  ·  jitter " + jitter + " ms  ·  " + loss.ToString("0.#") + "% lost  ·  to " + NetPing.Host : "Pinging " + NetPing.Host + " every 3 seconds";
            }
            NetPing.Acquire();
            NetPing.Updated += DrawPing;
            chart.Unloaded += (s, e) => { NetPing.Updated -= DrawPing; NetPing.Release(); };
            chart.SizeChanged += (s, e) => DrawPing();
            var pingHead = new DockPanel();
            DockPanel.SetDock(pingNow, System.Windows.Controls.Dock.Right);
            pingHead.Children.Add(pingNow);
            pingHead.Children.Add(UI.Text("Live ping", 18, Theme.Text, FontWeights.SemiBold, true));
            root.Children.Add(UI.Card(UI.V(10, pingHead, chart, pingStats), 22));

            // ---------- Tips ----------
            var tips = UI.V(12);
            root.Children.Add(UI.Label("What could be slowing things down"));
            root.Children.Add(UI.Card(tips, 18));

            // ---------- Devices ----------
            var devList = UI.V(4);
            var scanStatus = UI.Text("Finds phones, TVs, speakers and anything else on the same network as this PC.", 13, Theme.Muted);
            var scanBtn = FlatButton.Secondary("Scan my network");
            scanBtn.Click += async (s, e) =>
            {
                scanBtn.IsEnabled = false;
                devList.Children.Clear();
                scanStatus.Text = "Scanning...";
                var devices = await LanScan.Run(info, p => root.Dispatcher.BeginInvoke(new Action(() => scanStatus.Text = "Scanning... " + (int)(p * 100) + "%")));
                foreach (var d in devices)
                {
                    string name = d.IsThisPc ? Environment.MachineName + " (this PC)" : d.IsRouter ? (d.Name ?? "Your router") + " (router)" : d.Name ?? (d.PrivateMac ? "Phone or tablet (private address)" : "Unknown device");
                    var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
                    var right = UI.Text(d.Ip + (string.IsNullOrEmpty(d.Mac) ? "" : "   " + d.Mac), 12, Theme.Muted);
                    right.VerticalAlignment = VerticalAlignment.Center;
                    DockPanel.SetDock(right, System.Windows.Controls.Dock.Right);
                    row.Children.Add(right);
                    var icon = UI.GridIcon(d.IsRouter ? Icons.Network : d.IsThisPc ? Icons.Devices : Icons.Wifi, 18, d.IsThisPc || d.IsRouter ? Theme.Amber : Theme.Muted);
                    row.Children.Add(UI.H(10, icon, UI.Text(name, 14, Theme.Text, FontWeights.SemiBold)));
                    devList.Children.Add(row);
                }
                scanStatus.Text = devices.Count + (devices.Count == 1 ? " device" : " devices") + " found at " + DateTime.Now.ToString("HH:mm") + ". Devices that are asleep may not show up.";
                scanBtn.Content = "Scan again";
                scanBtn.IsEnabled = true;
            };
            var devHead = new DockPanel();
            DockPanel.SetDock(scanBtn, System.Windows.Controls.Dock.Right);
            devHead.Children.Add(scanBtn);
            devHead.Children.Add(UI.V(4, UI.Text("Devices on your network", 18, Theme.Text, FontWeights.SemiBold, true), scanStatus));
            root.Children.Add(UI.Card(UI.V(12, devHead, devList), 22));

            // ---------- Settings ----------
            var host = UI.TextInput(S.PingHost, 180);
            host.LostFocus += (s, e) => { S.PingHost = string.IsNullOrWhiteSpace(host.Text) ? "1.1.1.1" : host.Text.Trim(); Settings.Save(); };
            var alerts = new ToggleSwitch(S.DropAlerts, "Tell me when the internet drops");
            alerts.Toggled += on => { S.DropAlerts = on; Settings.Save(); };
            root.Children.Add(UI.Label("Settings"));
            root.Children.Add(UI.Card(UI.V(4,
                UI.SettingRow("Tell me when the internet drops", "A notification when the connection stops answering, and another when it's back, with how long it was out.", alerts),
                UI.SettingRow("Ping", "The address used for the live ping and drop alerts. 1.1.1.1 is Cloudflare; your router's address tests just your Wi-Fi.", host),
                UI.SettingRow("What goes online", "The speed test uses Cloudflare's public speed test. Your public IP is only looked up when you click Show.", UI.Text("", 1))), 8));

            // Fill in connection details off the UI thread.
            root.Loaded += async (s, e) =>
            {
                info = await Task.Run(NetInfo.Current);
                if (!info.Connected)
                {
                    connTitle.Text = "Not connected";
                    connSub.Text = "Arrakis can't see a network connection right now.";
                }
                else if (info.Kind == "Wi-Fi")
                {
                    connTitle.Text = string.IsNullOrEmpty(info.Ssid) ? "Wi-Fi" : info.Ssid;
                    connSub.Text = string.Join("  ·  ", new[] { "Wi-Fi", info.Band, info.Standard, info.Channel > 0 ? "channel " + info.Channel : "" }.Where(x => !string.IsNullOrEmpty(x)));
                    if (info.Signal >= 0) { signal.SetValue(info.Signal / 100.0); signal.Center.Text = info.Signal + "%"; }
                }
                else
                {
                    connTitle.Text = "Wired connection";
                    connSub.Text = "Ethernet  ·  " + (info.LinkMbps >= 1000 ? (info.LinkMbps / 1000.0).ToString("0.#") + " Gbps" : info.LinkMbps + " Mbps") + " link";
                    signal.SetValue(1);
                    signal.Center.Text = "Wired";
                }
                details.Children.Clear();
                if (info.Connected)
                {
                    if (info.Kind == "Wi-Fi" && info.RxMbps > 0) details.Children.Add(Fact("Link speed", UI.Text(info.RxMbps + " Mbps down, " + info.TxMbps + " up", 14, Theme.Text, FontWeights.SemiBold)));
                    details.Children.Add(Fact("This PC", UI.Text(info.LocalIp, 14, Theme.Text, FontWeights.SemiBold)));
                    details.Children.Add(Fact("Router", UI.Text(info.Gateway, 14, Theme.Text, FontWeights.SemiBold)));
                    details.Children.Add(Fact("DNS", UI.Text(string.IsNullOrEmpty(info.Dns) ? "-" : info.Dns, 14, Theme.Text, FontWeights.SemiBold)));
                    details.Children.Add(Fact("Public IP", UI.H(8, publicIp, showIp)));
                    details.Children.Add(Fact("Adapter", Trim(UI.Text(info.AdapterName, 13, Theme.Text))));
                }
                FillTips(tips, info);
                if (StartTestWhenShown) { StartTestWhenShown = false; RunTest(); }
            };
            return root;
        }

        private static TextBlock Trim(TextBlock t) { t.TextTrimming = TextTrimming.CharacterEllipsis; t.MaxWidth = 220; return t; }

        private static string Verdict(SpeedResult r)
        {
            if (r.DownMbps >= 100) return "Plenty for 4K streaming, big downloads and video calls at the same time.";
            if (r.DownMbps >= 25) return "Good for 4K streaming and video calls.";
            if (r.DownMbps >= 10) return "Fine for HD streaming and video calls, slower for big downloads.";
            return "On the slow side. HD video may buffer.";
        }

        private static void FillTips(StackPanel list, ConnectionInfo info)
        {
            var tips = new List<(bool good, string title, string text)>();
            var (median, jitter, loss) = NetPing.Stats();
            if (!info.Connected) tips.Add((false, "Not connected", "Check Wi-Fi is on and you're near your router, or that the cable is plugged in."));
            if (info.Kind == "Wi-Fi")
            {
                if (info.Signal >= 0 && info.Signal < 40) tips.Add((false, "Weak signal (" + info.Signal + "%)", "Walls, floors and distance cut Wi-Fi speed sharply. Move closer to the router, raise it off the floor, or add a mesh point or extender."));
                else if (info.Signal >= 40 && info.Signal < 65) tips.Add((false, "Signal is only fair (" + info.Signal + "%)", "You'll get more speed nearer the router or with nothing big in between, like a fridge, mirror or fish tank."));
                else if (info.Signal >= 65) tips.Add((true, "Strong signal (" + info.Signal + "%)", "Distance from the router isn't holding you back."));
                if (info.Band == "2.4 GHz" && info.FiveGhzSeen) tips.Add((false, "You're on 2.4 GHz, but 5 GHz is available", "Your router also broadcasts on 5 GHz, which is usually much faster at close range. Forget the 2.4 GHz network or pick the 5 GHz one."));
                else if (info.Band == "2.4 GHz") tips.Add((false, "You're on 2.4 GHz", "It reaches further but is slower and busier. If your router supports 5 GHz, turn it on and connect to it."));
                if (info.SameChannelNetworks >= 4) tips.Add((false, info.SameChannelNetworks + " other networks on your channel", "Neighbours' Wi-Fi on the same channel takes turns with yours. Set your router's channel to auto, or pick a quieter one."));
                else if (info.SameChannelNetworks >= 0) tips.Add((true, "Your channel is fairly quiet", info.NetworksNearby > 0 ? info.NetworksNearby + " networks nearby, but few share your channel." : "Not much else is competing with your Wi-Fi."));
                if (info.RxMbps > 0 && info.RxMbps < 100) tips.Add((false, "Slow link to the router (" + info.RxMbps + " Mbps)", "This is the most your PC and router can talk at right now, so internet speed can't beat it. A better signal or 5 GHz will raise it."));
            }
            else if (info.Kind == "Ethernet")
            {
                if (info.LinkMbps > 0 && info.LinkMbps <= 100) tips.Add((false, "Cable link is only " + info.LinkMbps + " Mbps", "That usually means an old or damaged cable, or a 100 Mbps port. Try another cable (Cat 5e or better) or another port on the router."));
                else tips.Add((true, "Wired at full speed", "Cables are the fastest, steadiest way to connect."));
            }
            if (median.HasValue)
            {
                if (median > 80) tips.Add((false, "High ping (" + median + " ms)", "Something on your network may be using a lot of bandwidth, like uploads, backups or game downloads. Restarting the router can help too."));
                if (jitter > 30) tips.Add((false, "Unsteady connection (jitter " + jitter + " ms)", "Ping is jumping around, which makes calls and games stutter. A cable, or getting closer to the router, usually fixes it."));
                if (loss >= 3) tips.Add((false, loss.ToString("0") + "% of pings lost", "Some traffic isn't getting through. If this keeps happening, restart your router and check for loose cables."));
                if (median <= 80 && jitter <= 30 && loss < 3) tips.Add((true, "Steady ping", "Typical " + median + " ms with little variation. Good for calls and games."));
            }
            list.Children.Clear();
            foreach (var (good, title, text) in tips.OrderBy(t => t.good))
            {
                var icon = new Border
                {
                    Width = 34, Height = 34, CornerRadius = new CornerRadius(10),
                    Background = good ? Theme.Brush(Color.FromArgb(0x30, Theme.GreenColor.R, Theme.GreenColor.G, Theme.GreenColor.B)) : Theme.AmberTint,
                    Child = UI.GridIcon(good ? Icons.Check : "M12 8v5M12 16v.01M12 3l9 16H3z", 18, good ? Theme.Green : Theme.Amber),
                    Margin = new Thickness(0, 0, 14, 0),
                    VerticalAlignment = VerticalAlignment.Top
                };
                var row = new DockPanel();
                DockPanel.SetDock(icon, System.Windows.Controls.Dock.Left);
                row.Children.Add(icon);
                row.Children.Add(UI.V(2, UI.Text(title, 14, Theme.Text, FontWeights.SemiBold), UI.Text(text, 13, Theme.Muted)));
                list.Children.Add(row);
            }
            if (tips.Count == 0) list.Children.Add(UI.Text("Give it a minute to gather some ping readings.", 13, Theme.Muted));
        }
    }
}

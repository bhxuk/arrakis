using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Arrakis.Core;

namespace Arrakis.Modules
{
    public class BatterySample
    {
        public DateTime Time { get; set; }
        public int Percent { get; set; }
        public bool OnMains { get; set; }
    }

    public class BatteryHistory
    {
        public List<BatterySample> Samples { get; set; } = new List<BatterySample>();
        public Dictionary<string, double> HealthByMonth { get; set; } = new Dictionary<string, double>();
    }

    public class BatteryInfo
    {
        public double DesignWh;
        public double FullWh;
        public int Cycles = -1;
        public double Health => DesignWh > 0 && FullWh > 0 ? Math.Min(100, 100.0 * FullWh / DesignWh) : -1;
    }

    public class BatteryModule : ModuleBase
    {
        public override string Id => "battery";
        public override string Name => "Battery";
        public override string Description => "Battery health, charging tips, low-battery alerts, power modes and a report you can save.";
        public override string IconData => Icons.Battery;
        public override string ShortcutText => "Laptops";
        public override bool IsEnabled { get => Settings.Current.Battery.Enabled; set => Settings.Current.Battery.Enabled = value; }

        private DispatcherTimer _timer;
        private DateTime _lastSample = DateTime.MinValue;
        private bool _reminded;
        private bool _lowAlerted;
        private BatteryHistory _history = new BatteryHistory();
        private BatterySettings S => Settings.Current.Battery;

        protected override void OnStart()
        {
            _history = Settings.LoadFile<BatteryHistory>("battery.json");
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _timer.Tick += (s, e) => Tick();
            _timer.Start();
            Tick();
        }

        protected override void OnStop() => _timer?.Stop();

        private void Tick()
        {
            var p = Power.Read();
            if (!p.HasBattery) return;

            // Low battery
            if (p.OnMains || p.Percent > S.LowPercent + 5) _lowAlerted = false;
            if (S.LowAlert && !p.OnMains && !_lowAlerted && p.Percent <= S.LowPercent)
            {
                _lowAlerted = true;
                _ = LowBatteryAlert(p);
            }

            // Unplug reminder
            if (!p.OnMains) _reminded = false;
            else if (S.UnplugReminder && !_reminded && p.Percent >= S.ReminderPercent && p.Percent < 100 + 1)
            {
                _reminded = true;
                App.Instance.Tray.Notify("Battery at " + p.Percent + "%", "You can unplug now. Keeping it below 100% most days slows down wear.");
            }

            if ((DateTime.Now - _lastSample).TotalMinutes >= 10)
            {
                _lastSample = DateTime.Now;
                _history.Samples.Add(new BatterySample { Time = DateTime.Now, Percent = p.Percent, OnMains = p.OnMains });
                _history.Samples.RemoveAll(x => x.Time < DateTime.Now.AddDays(-14));
                string month = DateTime.Today.ToString("yyyy-MM");
                if (!_history.HealthByMonth.ContainsKey(month))
                {
                    var info = ReadInfo();
                    if (info.Health > 0) _history.HealthByMonth[month] = Math.Round(info.Health, 1);
                }
                Settings.SaveFile("battery.json", _history);
            }
        }

        /// <summary>Design and full-charge capacity and cycle count come from the battery driver via WMI.</summary>
        public static BatteryInfo ReadInfo()
        {
            var info = new BatteryInfo();
            try
            {
                using (var s = new ManagementObjectSearcher(@"root\WMI", "SELECT DesignedCapacity FROM BatteryStaticData"))
                    foreach (ManagementObject o in s.Get()) info.DesignWh += Convert.ToDouble(o["DesignedCapacity"]) / 1000.0;
            }
            catch { }
            try
            {
                using (var s = new ManagementObjectSearcher(@"root\WMI", "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity"))
                    foreach (ManagementObject o in s.Get()) info.FullWh += Convert.ToDouble(o["FullChargedCapacity"]) / 1000.0;
            }
            catch { }
            try
            {
                using (var s = new ManagementObjectSearcher(@"root\WMI", "SELECT CycleCount FROM BatteryCycleCount"))
                    foreach (ManagementObject o in s.Get())
                    {
                        int c = Convert.ToInt32(o["CycleCount"]);
                        if (c > 0) info.Cycles = Math.Max(info.Cycles, c);
                    }
            }
            catch { }
            return info;
        }

        public override UIElement BuildPage()
        {
            var p = Power.Read();
            if (!p.HasBattery)
            {
                return UI.V(16,
                    UI.Card(UI.V(8,
                        UI.Text("No battery found", 20, Theme.Text, FontWeights.SemiBold, true),
                        UI.Text("Battery health and charging tips are for laptops and tablets. You can still switch power modes below.", 14, Theme.Muted)), 24),
                    UI.Label("Power mode"),
                    PowerModes.Build());
            }
            var info = ReadInfo();

            Border Tile(UIElement content) => UI.Card(content, 20);
            UIElement Kpi(string label, string value, string sub) =>
                UI.V(6, UI.Label(label), UI.Text(value, 28, Theme.Text, FontWeights.SemiBold, true), UI.Text(sub, 12, Theme.Muted));

            var ring = new Ring(64, 7, info.Health >= 80 ? Theme.Green : info.Health >= 60 ? Theme.Amber : Theme.Red);
            ring.SetValue(Math.Max(0, info.Health) / 100.0);
            string healthWord = info.Health < 0 ? "Not reported" : info.Health >= 80 ? "Good" : info.Health >= 60 ? "Fair" : "Worn";
            var healthTile = Tile(UI.H(16, ring, Kpi("Health", info.Health < 0 ? "--" : Math.Round(info.Health) + "%", healthWord)));
            var capTile = Tile(Kpi("Holds when full", info.FullWh > 0 ? info.FullWh.ToString("0.0") + " Wh" : "--", info.DesignWh > 0 ? "of " + info.DesignWh.ToString("0.0") + " Wh when new" : "Not reported by this laptop"));
            var cycTile = Tile(Kpi("Charge cycles", info.Cycles > 0 ? info.Cycles.ToString() : "--", info.Cycles > 0 ? "Most batteries are rated for 500 to 1,000" : "Not reported by this laptop"));
            string now = p.OnMains ? (p.Percent >= 100 ? "Plugged in, full" : "Charging") : p.SecondsLeft > 0 ? "On battery · about " + Power.FormatTime(p.SecondsLeft) + " left" : "On battery";
            var nowTile = Tile(Kpi("Right now", p.Percent + "%", now));

            var kpis = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4 };
            foreach (var t in new[] { healthTile, capTile, cycTile, nowTile }) { t.Margin = new Thickness(0, 0, 12, 0); kpis.Children.Add(t); }

            // Reminder
            var remind = new ToggleSwitch(S.UnplugReminder, "Unplug reminder");
            remind.Toggled += on => { S.UnplugReminder = on; Settings.Save(); };
            var pct = UI.Text(S.ReminderPercent + "%", 14, Theme.Amber, FontWeights.SemiBold, true);
            var slider = new MiniSlider(60, 95, S.ReminderPercent, null, 220) { Step = 5 };
            slider.ValueChanged += v => { S.ReminderPercent = (int)v; pct.Text = (int)v + "%"; Settings.Save(); };

            var health = UI.V(6);
            foreach (var kv in _history.HealthByMonth.OrderBy(k => k.Key).TakeLast(6))
                health.Children.Add(UI.SettingRow(DateTime.ParseExact(kv.Key, "yyyy-MM", null).ToString("MMMM yyyy"), null, UI.Text(kv.Value.ToString("0.0") + "%", 14, Theme.Text, FontWeights.SemiBold)));
            if (health.Children.Count == 0) health.Children.Add(UI.Text("Arrakis records your battery's health once a month, so you can see how it changes over time.", 13, Theme.Muted));

            var low = new ToggleSwitch(S.LowAlert, "Low battery alert");
            low.Toggled += on => { S.LowAlert = on; Settings.Save(); };
            var lowPct = UI.Text(S.LowPercent + "%", 14, Theme.Amber, FontWeights.SemiBold, true);
            var lowSlider = new MiniSlider(5, 50, S.LowPercent, null, 220) { Step = 5 };
            lowSlider.ValueChanged += v => { S.LowPercent = (int)v; lowPct.Text = (int)v + "%"; Settings.Save(); };

            var report = FlatButton.Secondary("Save a health report...", () => SaveReport(info));
            report.HorizontalAlignment = HorizontalAlignment.Left;

            return UI.V(16,
                kpis,
                UI.Label("Power mode"),
                PowerModes.Build(),
                UI.Label("Charge level, last 7 days"),
                UI.Card(Chart(), 20),
                UI.Label("Tips for your battery"),
                UI.Card(Tips(info), 16),
                UI.Card(UI.V(0,
                    UI.SettingRow("Unplug reminder", "Get a nudge when charging reaches your limit. Keeping it below 100% most days slows down wear.", remind),
                    UI.Divider(),
                    UI.SettingRow("Remind me at", null, UI.H(12, slider, pct)),
                    UI.Divider(),
                    UI.SettingRow("Low battery alert", "A warning when you're running low, with the apps using the most power right then.", low),
                    UI.Divider(),
                    UI.SettingRow("Warn me at", null, UI.H(12, lowSlider, lowPct))), 8),
                UI.Label("Health over time"),
                UI.Card(health, 16),
                UI.Card(UI.SettingRow("Health report", "A one-page summary of your battery that you can print or save as a PDF. Handy when selling a laptop or making a warranty claim.", report), 8));
        }

        /// <summary>The apps using the most processor time over one second.</summary>
        private static async System.Threading.Tasks.Task<List<(string name, double pct)>> TopApps(int count)
        {
            var first = new Dictionary<int, TimeSpan>();
            foreach (var p in System.Diagnostics.Process.GetProcesses())
                try { first[p.Id] = p.TotalProcessorTime; } catch { } finally { p.Dispose(); }
            await System.Threading.Tasks.Task.Delay(1000);
            var usage = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in System.Diagnostics.Process.GetProcesses())
            {
                try
                {
                    if (!first.TryGetValue(p.Id, out var before) || p.Id == 0) continue;
                    double ms = (p.TotalProcessorTime - before).TotalMilliseconds;
                    string name = p.ProcessName;
                    try { var path = p.MainModule?.FileName; if (path != null) name = WindowTracker.FriendlyName(path); } catch { }
                    usage[name] = (usage.TryGetValue(name, out var u) ? u : 0) + ms;
                }
                catch { }
                finally { p.Dispose(); }
            }
            double total = Environment.ProcessorCount * 1000.0;
            return usage.Where(kv => kv.Key != "Idle" && kv.Key != "System Idle Process").OrderByDescending(kv => kv.Value).Take(count).Select(kv => (kv.Key, 100 * kv.Value / total)).ToList();
        }

        private async System.Threading.Tasks.Task LowBatteryAlert(PowerState p)
        {
            string apps = "";
            try
            {
                var top = await TopApps(3);
                if (top.Count > 0) apps = "Using the most power: " + string.Join(", ", top.Select(t => t.name)) + ".";
            }
            catch { }
            App.Instance.Tray.Notify("Battery at " + p.Percent + "%" + (p.SecondsLeft > 0 ? ", about " + Power.FormatTime(p.SecondsLeft) + " left" : ""), "Plug in soon. " + apps);
        }

        /// <summary>Looks at the last two weeks of charging and suggests habits that help the battery last.</summary>
        private UIElement Tips(BatteryInfo info)
        {
            var tips = new List<(string title, string text, bool good)>();
            var samples = _history.Samples.OrderBy(x => x.Time).ToList();
            if (samples.Count < 12)
            {
                tips.Add(("Getting to know your battery", "Tips appear after a day or two of use, once Arrakis has seen how you charge.", true));
            }
            else
            {
                // Nights spent plugged in at (almost) full charge.
                var nights = samples.Where(x => x.OnMains && x.Percent >= 95 && (x.Time.Hour >= 0 && x.Time.Hour < 6)).Select(x => x.Time.Date).Distinct().Count();
                if (nights >= 3)
                    tips.Add(("Charged to full overnight " + nights + " times", "Sitting at 100% for hours wears batteries faster. Try unplugging before bed, or turn on your laptop's charge limit (often called Smart Charging or Battery Care) if it has one.", false));
                int deep = samples.Count(x => !x.OnMains && x.Percent <= 10);
                if (deep >= 2)
                    tips.Add(("Ran very low " + deep + " times", "Letting it drop under 10% often is hard on a battery. Plugging in around 20% helps.", false));
                double inRange = samples.Count(x => x.Percent >= 20 && x.Percent <= 80) * 100.0 / samples.Count;
                if (inRange >= 60)
                    tips.Add(("Mostly between 20% and 80%", "That's the sweet spot. You spent " + Math.Round(inRange) + "% of the last two weeks in it.", true));
                else
                    tips.Add(("Spending a lot of time at the extremes", "Only " + Math.Round(inRange) + "% of the last two weeks was between 20% and 80%, the range batteries like best.", false));
                double plugged = samples.Count(x => x.OnMains) * 100.0 / samples.Count;
                if (plugged > 85)
                    tips.Add(("Plugged in almost all the time", "If this laptop mostly lives on a desk, a charge limit of around 80% will help it last for years.", false));
            }
            if (info.Health > 0 && info.Health < 70)
                tips.Add(("Battery is getting worn", "It holds " + Math.Round(info.Health) + "% of its original charge. A replacement would bring back the battery life you had when it was new.", false));
            if (info.Cycles > 600)
                tips.Add((info.Cycles + " charge cycles", "Most laptop batteries are rated for 500 to 1,000 cycles, so expect it to wear more noticeably from here.", false));

            var list = UI.V(12);
            foreach (var (title, text, good) in tips)
            {
                var icon = new Border
                {
                    Width = 34, Height = 34, CornerRadius = new CornerRadius(10),
                    Background = good ? Theme.Brush(Color.FromArgb(0x30, Theme.GreenColor.R, Theme.GreenColor.G, Theme.GreenColor.B)) : Theme.AmberTint,
                    Child = UI.GridIcon(good ? Icons.Check : "M12 8v5M12 16v.01M12 3l9 16H3z", 18, good ? Theme.Green : Theme.Amber)
                };
                var row = new DockPanel();
                DockPanel.SetDock(icon, System.Windows.Controls.Dock.Left);
                icon.Margin = new Thickness(0, 0, 14, 0);
                icon.VerticalAlignment = VerticalAlignment.Top;
                row.Children.Add(icon);
                row.Children.Add(UI.V(2, UI.Text(title, 14, Theme.Text, FontWeights.SemiBold), UI.Text(text, 13, Theme.Muted)));
                list.Children.Add(row);
            }
            return list;
        }

        private void SaveReport(BatteryInfo info)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                FileName = "Battery report " + DateTime.Now.ToString("yyyy-MM-dd") + ".html",
                Filter = "Web page (*.html)|*.html",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };
            if (dlg.ShowDialog() != true) return;
            var p = Power.Read();
            var since = DateTime.Now.AddDays(-7);
            var pts = _history.Samples.Where(x => x.Time >= since).OrderBy(x => x.Time).ToList();
            var line = string.Join(" ", pts.Select(x => ((x.Time - since).TotalSeconds / TimeSpan.FromDays(7).TotalSeconds * 700).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "," + (200 - x.Percent * 2).ToString(System.Globalization.CultureInfo.InvariantCulture)));
            string Esc(string t) => System.Net.WebUtility.HtmlEncode(t ?? "");
            var healthRows = string.Join("", _history.HealthByMonth.OrderBy(k => k.Key).Select(kv => "<tr><td>" + Esc(DateTime.ParseExact(kv.Key, "yyyy-MM", null).ToString("MMMM yyyy")) + "</td><td>" + kv.Value.ToString("0.0") + "%</td></tr>"));
            string model = "";
            try
            {
                using var s = new System.Management.ManagementObjectSearcher("SELECT Manufacturer, Model FROM Win32_ComputerSystem");
                foreach (System.Management.ManagementObject o in s.Get()) model = (o["Manufacturer"] + " " + o["Model"]).Trim();
            }
            catch { }
            string html = "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>Battery report</title><style>" +
                "body{font-family:Segoe UI,system-ui,sans-serif;max-width:760px;margin:40px auto;color:#17191f;padding:0 20px}h1{font-size:28px;margin:0}" +
                ".muted{color:#5b606b}.grid{display:grid;grid-template-columns:repeat(4,1fr);gap:12px;margin:24px 0}.k{border:1px solid #ddd;border-radius:12px;padding:14px}" +
                ".k b{display:block;font-size:22px;margin-top:4px}table{border-collapse:collapse;width:100%}td{padding:8px 0;border-bottom:1px solid #eee}" +
                "svg{border:1px solid #eee;border-radius:12px;width:100%;height:auto}@media print{body{margin:0}}</style></head><body>" +
                "<h1>Battery report</h1><p class=\"muted\">" + Esc(Environment.MachineName) + (model.Length > 0 ? " · " + Esc(model) : "") + " · " + DateTime.Now.ToString("d MMMM yyyy, HH:mm") + "</p>" +
                "<div class=\"grid\"><div class=\"k\">Health<b>" + (info.Health > 0 ? Math.Round(info.Health) + "%" : "Not reported") + "</b></div>" +
                "<div class=\"k\">Holds when full<b>" + (info.FullWh > 0 ? info.FullWh.ToString("0.0") + " Wh" : "-") + "</b></div>" +
                "<div class=\"k\">When new<b>" + (info.DesignWh > 0 ? info.DesignWh.ToString("0.0") + " Wh" : "-") + "</b></div>" +
                "<div class=\"k\">Charge cycles<b>" + (info.Cycles > 0 ? info.Cycles.ToString() : "-") + "</b></div></div>" +
                "<h2>Charge level, last 7 days</h2><svg viewBox=\"0 0 700 200\"><line x1=\"0\" y1=\"40\" x2=\"700\" y2=\"40\" stroke=\"#ffb547\" stroke-dasharray=\"5 5\"/><polyline fill=\"none\" stroke=\"#1fa392\" stroke-width=\"2.5\" points=\"" + line + "\"/></svg>" +
                "<h2>Health over time</h2><table>" + (healthRows.Length > 0 ? healthRows : "<tr><td class=\"muted\">Recorded monthly from when Arrakis was installed.</td></tr>") + "</table>" +
                "<p class=\"muted\" style=\"margin-top:32px\">Right now: " + p.Percent + "% · " + (p.OnMains ? "plugged in" : "on battery") + ". Made with Arrakis.</p></body></html>";
            System.IO.File.WriteAllText(dlg.FileName, html);
            Shell.Open(dlg.FileName);
        }

        private UIElement Chart()
        {
            const double W = 900, H = 200;
            var canvas = new Canvas { Width = W, Height = H };
            var since = DateTime.Now.AddDays(-7);
            var pts = _history.Samples.Where(s => s.Time >= since).OrderBy(s => s.Time).ToList();
            double X(DateTime t) => (t - since).TotalSeconds / TimeSpan.FromDays(7).TotalSeconds * W;
            double Y(double pct) => H - pct / 100.0 * H;

            canvas.Children.Add(new Line { X1 = 0, X2 = W, Y1 = Y(50), Y2 = Y(50), Stroke = Theme.Line, StrokeThickness = 1 });
            var limit = new Line { X1 = 0, X2 = W, Y1 = Y(S.ReminderPercent), Y2 = Y(S.ReminderPercent), Stroke = Theme.Amber, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 4 } };
            canvas.Children.Add(limit);

            if (pts.Count >= 2)
            {
                var line = new Polyline { Stroke = Theme.Teal, StrokeThickness = 2.5, StrokeLineJoin = PenLineJoin.Round };
                var area = new Polygon { Fill = Theme.Brush(Color.FromArgb(0x1F, 0x5E, 0xD3, 0xC3)) };
                area.Points.Add(new Point(X(pts[0].Time), H));
                foreach (var s in pts)
                {
                    var pt = new Point(X(s.Time), Y(s.Percent));
                    line.Points.Add(pt);
                    area.Points.Add(pt);
                }
                area.Points.Add(new Point(X(pts[pts.Count - 1].Time), H));
                canvas.Children.Add(area);
                canvas.Children.Add(line);
            }
            else
            {
                var t = UI.Text("Arrakis checks your battery every 10 minutes. The chart fills in over the next few days.", 14, Theme.Muted);
                Canvas.SetLeft(t, 0);
                Canvas.SetTop(t, H / 2 - 10);
                canvas.Children.Add(t);
            }

            var labels = new System.Windows.Controls.Primitives.UniformGrid { Columns = 7, Margin = new Thickness(0, 8, 0, 0) };
            for (int i = 6; i >= 0; i--)
            {
                var d = DateTime.Today.AddDays(-i);
                var t = UI.Text(i == 0 ? "Today" : d.ToString("ddd"), 12, Theme.Muted);
                t.TextAlignment = TextAlignment.Center;
                labels.Children.Add(t);
            }
            var axis = UI.V(0, UI.Text("100%", 11, Theme.Muted), new Border { Height = 72 }, UI.Text("50%", 11, Theme.Muted), new Border { Height = 72 }, UI.Text("0%", 11, Theme.Muted));
            axis.Margin = new Thickness(0, -6, 10, 0);
            var view = new Viewbox { Child = canvas, Stretch = Stretch.Fill, Height = H };
            var right = UI.V(0, view, labels);
            var dock = new DockPanel();
            DockPanel.SetDock(axis, System.Windows.Controls.Dock.Left);
            dock.Children.Add(axis);
            dock.Children.Add(right);
            return dock;
        }
    }
}

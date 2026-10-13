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
        public override string Description => "Battery wear, charge history and an unplug-at-80% nudge.";
        public override string IconData => Icons.Battery;
        public override string ShortcutText => "Laptops";
        public override bool IsEnabled { get => Settings.Current.Battery.Enabled; set => Settings.Current.Battery.Enabled = value; }

        private DispatcherTimer _timer;
        private DateTime _lastSample = DateTime.MinValue;
        private bool _reminded;
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
                return UI.Card(UI.V(8,
                    UI.Text("No battery found", 20, Theme.Text, FontWeights.SemiBold, true),
                    UI.Text("This module is for laptops and tablets. On a desktop PC you can turn it off on the Home page.", 14, Theme.Muted)), 24);
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

            return UI.V(16,
                kpis,
                UI.Label("Charge level, last 7 days"),
                UI.Card(Chart(), 20),
                UI.Card(UI.V(0,
                    UI.SettingRow("Unplug reminder", "Get a nudge when charging reaches your limit. Keeping it below 100% most days slows down wear.", remind),
                    UI.Divider(),
                    UI.SettingRow("Remind me at", null, UI.H(12, slider, pct))), 8),
                UI.Label("Health over time"),
                UI.Card(health, 16));
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

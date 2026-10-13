using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Arrakis.Core;
using Microsoft.Win32;

namespace Arrakis.Modules
{
    public class FocusLog
    {
        public Dictionary<string, int> MinutesByDay { get; set; } = new Dictionary<string, int>();
    }

    public class FocusModule : ModuleBase
    {
        public override string Id => "focus";
        public override string Name => "Focus";
        public override string Description => "A timer pill that silences notifications while you work.";
        public override string IconData => Icons.Focus;
        public override string ShortcutText => Settings.Current.Focus.Hotkey;
        public override bool IsEnabled { get => Settings.Current.Focus.Enabled; set => Settings.Current.Focus.Enabled = value; }

        public enum Phase { Idle, Focus, Break }

        public Phase Current { get; private set; } = Phase.Idle;
        public bool Paused { get; private set; }
        public int Session { get; private set; } = 1;
        public TimeSpan Remaining { get; private set; }
        public TimeSpan Total { get; private set; }

        public bool IsActive => Current != Phase.Idle;
        public bool IsFocusPhase => Current == Phase.Focus;
        public double Fraction => Total.TotalSeconds > 0 ? Remaining.TotalSeconds / Total.TotalSeconds : 0;
        public string RemainingText => ((int)Remaining.TotalMinutes).ToString("00") + ":" + Remaining.Seconds.ToString("00");
        public string PhaseLabel => Current == Phase.Break ? "Break" : Paused ? "Paused" : S.Label;

        public event Action Changed;

        private FocusSettings S => Settings.Current.Focus;
        private readonly DispatcherTimer _timer;
        private FocusPill _pill;
        private readonly AmbientPlayer _ambient = new AmbientPlayer();
        private DateTime _focusStartedAt;
        private TimeSpan _focusedThisSession;
        private static string FlagPath => Path.Combine(Settings.Folder, "notifications-silenced.flag");

        public FocusModule()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (s, e) => Tick();
        }

        protected override void OnStart()
        {
            // If Arrakis was closed mid-session last time, give notifications back.
            if (File.Exists(FlagPath)) SetNotifications(true);
            Hotkeys.Register("focus", S.Hotkey, ToggleFromHotkey);
        }

        protected override void OnStop()
        {
            End();
            Hotkeys.Unregister("focus");
        }

        public void ToggleFromHotkey()
        {
            if (!IsRunning) return;
            if (Current == Phase.Idle) StartFocus();
            else TogglePause();
        }

        public void StartFocus()
        {
            Current = Phase.Focus;
            Paused = false;
            Total = Remaining = TimeSpan.FromMinutes(Math.Max(1, S.FocusMinutes));
            _focusStartedAt = DateTime.Now;
            _focusedThisSession = TimeSpan.Zero;
            EnterFocusEffects();
            _timer.Start();
            ShowPill();
            Raise();
        }

        public void TogglePause()
        {
            if (Current == Phase.Idle) return;
            Paused = !Paused;
            if (Paused) { _timer.Stop(); _ambient.Stop(); }
            else { _timer.Start(); if (Current == Phase.Focus) _ambient.Play(S.Sound, S.Volume); }
            Raise();
        }

        public void AddFiveMinutes()
        {
            if (Current == Phase.Idle) return;
            Remaining += TimeSpan.FromMinutes(5);
            Total += TimeSpan.FromMinutes(5);
            Raise();
        }

        public void End()
        {
            if (Current == Phase.Focus) LogMinutes(_focusedThisSession);
            _timer.Stop();
            Current = Phase.Idle;
            Paused = false;
            Session = 1;
            ExitFocusEffects();
            _pill?.Close();
            _pill = null;
            Raise();
        }

        public void Skip()
        {
            if (Current == Phase.Focus) FinishFocus(false);
            else if (Current == Phase.Break) FinishBreak();
        }

        private void Tick()
        {
            if (Paused || Current == Phase.Idle) return;
            Remaining -= TimeSpan.FromSeconds(1);
            if (Current == Phase.Focus) _focusedThisSession += TimeSpan.FromSeconds(1);
            if (Remaining <= TimeSpan.Zero)
            {
                if (Current == Phase.Focus) FinishFocus(true);
                else FinishBreak();
            }
            Raise();
        }

        private void FinishFocus(bool chime)
        {
            LogMinutes(_focusedThisSession);
            _focusedThisSession = TimeSpan.Zero;
            if (chime) SystemSounds.Asterisk.Play();
            ExitFocusEffects();
            bool longBreak = Session >= S.SessionsPerRound;
            Current = Phase.Break;
            Total = Remaining = TimeSpan.FromMinutes(Math.Max(1, longBreak ? S.LongBreakMinutes : S.BreakMinutes));
            App.Instance.Tray.Notify("Nice work", longBreak ? "Round complete. Take a longer break." : "Time for a short break.");
            Raise();
        }

        private void FinishBreak()
        {
            SystemSounds.Asterisk.Play();
            if (Session >= S.SessionsPerRound)
            {
                App.Instance.Tray.Notify("Break's over", "That was a full round. Start another whenever you're ready.");
                End();
                return;
            }
            Session++;
            Current = Phase.Focus;
            Total = Remaining = TimeSpan.FromMinutes(Math.Max(1, S.FocusMinutes));
            _focusedThisSession = TimeSpan.Zero;
            EnterFocusEffects();
            App.Instance.Tray.Notify("Back to it", "Session " + Session + " of " + S.SessionsPerRound + " has started.");
            Raise();
        }

        private void EnterFocusEffects()
        {
            if (S.SilenceNotifications) SetNotifications(false);
            if (S.DimWidgets) App.Instance.Widgets.SetDimmed(true);
            _ambient.Play(S.Sound, S.Volume);
        }

        private void ExitFocusEffects()
        {
            if (File.Exists(FlagPath)) SetNotifications(true);
            App.Instance.Widgets.SetDimmed(false);
            _ambient.Stop();
        }

        private static void SetNotifications(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\PushNotifications");
                if (!enabled)
                {
                    object prev = key.GetValue("ToastEnabled");
                    if (prev is int p && p == 0) return;   // already off, leave it alone
                    key.SetValue("ToastEnabled", 0, RegistryValueKind.DWord);
                    File.WriteAllText(FlagPath, DateTime.Now.ToString("o"));
                }
                else
                {
                    key.SetValue("ToastEnabled", 1, RegistryValueKind.DWord);
                    if (File.Exists(FlagPath)) File.Delete(FlagPath);
                }
            }
            catch (Exception ex) { Log.Write("Notification toggle failed: " + ex.Message); }
        }

        private void ShowPill()
        {
            if (_pill == null)
            {
                _pill = new FocusPill(this);
                _pill.Show();
            }
        }

        private void Raise() => Changed?.Invoke();

        // ---------- Stats ----------
        private static void LogMinutes(TimeSpan span)
        {
            int minutes = (int)Math.Round(span.TotalMinutes);
            if (minutes < 1) return;
            var log = Settings.LoadFile<FocusLog>("focus.json");
            string key = DateTime.Today.ToString("yyyy-MM-dd");
            log.MinutesByDay.TryGetValue(key, out int existing);
            log.MinutesByDay[key] = existing + minutes;
            // Keep a year of history.
            foreach (var old in log.MinutesByDay.Keys.Where(k => string.CompareOrdinal(k, DateTime.Today.AddDays(-366).ToString("yyyy-MM-dd")) < 0).ToList())
                log.MinutesByDay.Remove(old);
            Settings.SaveFile("focus.json", log);
        }

        public static (DateTime day, int minutes)[] LastSevenDays()
        {
            var log = Settings.LoadFile<FocusLog>("focus.json");
            var result = new (DateTime, int)[7];
            for (int i = 0; i < 7; i++)
            {
                var d = DateTime.Today.AddDays(i - 6);
                log.MinutesByDay.TryGetValue(d.ToString("yyyy-MM-dd"), out int m);
                result[i] = (d, m);
            }
            return result;
        }

        public static int Streak()
        {
            var log = Settings.LoadFile<FocusLog>("focus.json");
            int streak = 0;
            var d = DateTime.Today;
            if (!log.MinutesByDay.ContainsKey(d.ToString("yyyy-MM-dd"))) d = d.AddDays(-1);
            while (log.MinutesByDay.TryGetValue(d.ToString("yyyy-MM-dd"), out int m) && m > 0)
            {
                streak++;
                d = d.AddDays(-1);
            }
            return streak;
        }

        // ---------- Settings page ----------
        public override UIElement BuildPage()
        {
            var ring = new Ring(200, 14, Theme.Amber, "", 44);
            var phase = UI.Label("Ready");
            var title = UI.Text(S.Label, 26, Theme.Text, FontWeights.SemiBold, true);
            var startBtn = FlatButton.Primary("Start focus");
            var pauseBtn = FlatButton.Secondary("Pause");
            var plusBtn = FlatButton.Secondary("+5 min", AddFiveMinutes);
            var endBtn = FlatButton.Secondary("End session", End);
            startBtn.Click += (s, e) => { if (!IsRunning) App.Instance.SetModuleEnabled(this, true); StartFocus(); };
            pauseBtn.Click += (s, e) => TogglePause();

            void Render()
            {
                if (IsActive)
                {
                    ring.SetValue(Fraction);
                    ring.Center.Text = RemainingText;
                    phase.Text = (Current == Phase.Break ? "BREAK" : "SESSION " + Session + " OF " + S.SessionsPerRound) + (Paused ? " · PAUSED" : "");
                    startBtn.Visibility = Visibility.Collapsed;
                    pauseBtn.Visibility = plusBtn.Visibility = endBtn.Visibility = Visibility.Visible;
                    pauseBtn.Content = Paused ? "Resume" : "Pause";
                }
                else
                {
                    ring.SetValue(1);
                    ring.Center.Text = S.FocusMinutes.ToString("00") + ":00";
                    phase.Text = "READY";
                    startBtn.Visibility = Visibility.Visible;
                    pauseBtn.Visibility = plusBtn.Visibility = endBtn.Visibility = Visibility.Collapsed;
                }
                title.Text = S.Label;
            }
            Render();
            Action handler = Render;
            Changed += handler;
            ring.Unloaded += (s, e) => Changed -= handler;
            ring.Loaded += (s, e) => { Changed -= handler; Changed += handler; Render(); };

            var buttons = UI.H(10, startBtn, pauseBtn, plusBtn, endBtn);
            var info = UI.V(14, phase, title, buttons,
                UI.Text("Shortcut: " + S.Hotkey + " starts, pauses and resumes.", 13, Theme.Muted));
            info.VerticalAlignment = VerticalAlignment.Center;
            var top = UI.H(36, ring, info);

            // Ambient sound chips
            var soundRow = new WrapPanel();
            var chips = new List<Chip>();
            foreach (var name in AmbientPlayer.Sounds)
            {
                var chip = new Chip(name, name == S.Sound) { Margin = new Thickness(0, 0, 8, 8) };
                chip.Click += (s, e) =>
                {
                    S.Sound = name;
                    Settings.Save();
                    foreach (var c in chips) c.Selected = (string)c.Content == name;
                    if (IsActive && IsFocusPhase && !Paused) _ambient.Play(S.Sound, S.Volume);
                };
                chips.Add(chip);
                soundRow.Children.Add(chip);
            }
            var volume = new MiniSlider(0, 1, S.Volume, Theme.Teal, 220) { Step = 0.05 };
            volume.ValueChanged += v => { S.Volume = v; _ambient.Volume = v; Settings.Save(); };

            var label = UI.TextInput(S.Label, 200);
            label.LostFocus += (s, e) => { S.Label = string.IsNullOrWhiteSpace(label.Text) ? "Deep work" : label.Text.Trim(); Settings.Save(); Render(); Raise(); };

            MiniSlider Minutes(int min, int max, int value, Action<int> set)
            {
                var sl = new MiniSlider(min, max, value, null, 200);
                sl.ValueChanged += v => { set((int)v); Settings.Save(); Render(); };
                return sl;
            }
            TextBlock ValueText(Func<string> get)
            {
                var t = UI.Text(get(), 13, Theme.Muted);
                t.Width = 60;
                t.TextAlignment = TextAlignment.Right;
                return t;
            }

            var focusVal = ValueText(() => S.FocusMinutes + " min");
            var breakVal = ValueText(() => S.BreakMinutes + " min");
            var longVal = ValueText(() => S.LongBreakMinutes + " min");
            var roundVal = ValueText(() => S.SessionsPerRound + " sessions");
            var focusSl = Minutes(5, 90, S.FocusMinutes, v => { S.FocusMinutes = v; focusVal.Text = v + " min"; });
            var breakSl = Minutes(1, 30, S.BreakMinutes, v => { S.BreakMinutes = v; breakVal.Text = v + " min"; });
            var longSl = Minutes(5, 60, S.LongBreakMinutes, v => { S.LongBreakMinutes = v; longVal.Text = v + " min"; });
            var roundSl = Minutes(1, 8, S.SessionsPerRound, v => { S.SessionsPerRound = v; roundVal.Text = v + " sessions"; });

            ToggleSwitch Toggle(bool value, string name, Action<bool> set)
            {
                var t = new ToggleSwitch(value, name);
                t.Toggled += on => { set(on); Settings.Save(); };
                return t;
            }

            var settingsCard = UI.Card(UI.V(0,
                UI.SettingRow("Session name", "Shown on the pill at the top of your screen.", label),
                UI.Divider(),
                UI.SettingRow("Focus length", null, UI.H(8, focusSl, focusVal)),
                UI.Divider(),
                UI.SettingRow("Short break", null, UI.H(8, breakSl, breakVal)),
                UI.Divider(),
                UI.SettingRow("Long break", "After a full round.", UI.H(8, longSl, longVal)),
                UI.Divider(),
                UI.SettingRow("Sessions per round", null, UI.H(8, roundSl, roundVal)),
                UI.Divider(),
                UI.SettingRow("Silence notifications", "Turns off app pop-ups while you focus, and back on afterwards.", Toggle(S.SilenceNotifications, "Silence notifications", v => S.SilenceNotifications = v)),
                UI.Divider(),
                UI.SettingRow("Hide the dock", null, Toggle(S.HideDock, "Hide the dock", v => S.HideDock = v)),
                UI.Divider(),
                UI.SettingRow("Dim widgets", null, Toggle(S.DimWidgets, "Dim widgets", v => S.DimWidgets = v))), 8);

            return UI.V(16,
                UI.Card(top, 28),
                UI.Label("Ambient sound"),
                UI.Card(UI.V(10, soundRow, UI.H(10, UI.GridIcon(Icons.Volume, 18, Theme.Muted), volume))),
                UI.Label("Last 7 days"),
                UI.Card(BuildChart()),
                UI.Label("Settings"),
                settingsCard);
        }

        private UIElement BuildChart()
        {
            var days = LastSevenDays();
            int total = days.Sum(d => d.minutes);
            int max = Math.Max(30, days.Max(d => d.minutes));
            var grid = new Grid { Height = 150 };
            for (int i = 0; i < 7; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < 7; i++)
            {
                var (day, m) = days[i];
                bool today = i == 6;
                var col = new DockPanel { Margin = new Thickness(8, 0, 8, 0) };
                var name = UI.Text(today ? "Today" : day.ToString("ddd"), 12, today ? Theme.Amber : Theme.Muted, FontWeights.SemiBold);
                name.HorizontalAlignment = HorizontalAlignment.Center;
                DockPanel.SetDock(name, System.Windows.Controls.Dock.Bottom);
                var val = UI.Text(m > 0 ? m + "m" : "0", 12, Theme.Muted);
                val.HorizontalAlignment = HorizontalAlignment.Center;
                DockPanel.SetDock(val, System.Windows.Controls.Dock.Top);
                var bar = new Border
                {
                    Height = Math.Max(4, 96.0 * m / max),
                    CornerRadius = new CornerRadius(8),
                    Background = today ? Theme.Amber : Theme.Brush(Color.FromArgb(0x8C, 0x5E, 0xD3, 0xC3)),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    MaxWidth = 64,
                    Margin = new Thickness(0, 4, 0, 6)
                };
                col.Children.Add(name);
                col.Children.Add(val);
                col.Children.Add(bar);
                Grid.SetColumn(col, i);
                grid.Children.Add(col);
            }
            int streak = Streak();
            var summary = UI.V(8,
                UI.Text((total / 60) + " h " + (total % 60) + " min", 28, Theme.Text, FontWeights.SemiBold, true),
                UI.Text("focused this week", 13, Theme.Muted),
                UI.H(6, UI.GridIcon(Icons.Flame, 18, Theme.Amber), UI.Text(streak + (streak == 1 ? " day streak" : " day streak"), 14, Theme.Amber, FontWeights.SemiBold)));
            summary.Width = 200;
            summary.VerticalAlignment = VerticalAlignment.Bottom;
            var dock = new DockPanel();
            DockPanel.SetDock(summary, System.Windows.Controls.Dock.Left);
            dock.Children.Add(summary);
            dock.Children.Add(grid);
            return dock;
        }
    }

    /// <summary>The little pill at the top of the screen while a session runs.</summary>
    public class FocusPill : Window
    {
        private readonly FocusModule _f;
        private readonly Ring _ring;
        private readonly TextBlock _label;
        private readonly TextBlock _time;
        private readonly StackPanel _controls;
        private readonly Border _pill;

        public FocusPill(FocusModule f)
        {
            _f = f;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            ShowActivated = false;
            FontFamily = Theme.Font;

            _ring = new Ring(26, 3, Theme.Amber);
            _label = UI.Text("", 14, Theme.Text, FontWeights.SemiBold);
            _label.VerticalAlignment = VerticalAlignment.Center;
            _time = UI.Text("", 14, Theme.Amber, FontWeights.SemiBold, true);
            _time.VerticalAlignment = VerticalAlignment.Center;
            _controls = UI.H(6,
                SmallButton("Pause", () => _f.TogglePause()),
                SmallButton("+5", () => _f.AddFiveMinutes()),
                SmallButton("Skip", () => _f.Skip()),
                SmallButton("End", () => _f.End()));
            _controls.Visibility = Visibility.Collapsed;
            _controls.Margin = new Thickness(8, 0, 0, 0);
            var row = UI.H(12, _ring, _label, _time, _controls);
            _pill = new Border
            {
                Background = Theme.Glass,
                BorderBrush = Theme.GlassBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(22),
                Padding = new Thickness(9, 7, 16, 7),
                Child = row,
                Margin = new Thickness(16, 12, 16, 16),
                Effect = Theme.Shadow(24, 0.35, 6),
                Cursor = Cursors.Hand
            };
            _pill.MouseLeftButtonUp += (s, e) =>
            {
                if (e.OriginalSource is DependencyObject d && IsInside(d, _controls)) return;
                _controls.Visibility = _controls.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            };
            Content = _pill;
            SourceInitialized += (s, e) => WindowHelper.MakeToolWindow(this, true);
            SizeChanged += (s, e) => Place();
            Loaded += (s, e) => { Render(); Place(); };
            _f.Changed += Render;
            Closed += (s, e) => _f.Changed -= Render;
        }

        private static bool IsInside(DependencyObject d, DependencyObject container)
        {
            while (d != null)
            {
                if (d == container) return true;
                d = VisualTreeHelper.GetParent(d);
            }
            return false;
        }

        private FlatButton SmallButton(string text, Action a)
        {
            var b = new FlatButton { Content = text, MinHeight = 30, Height = 30, Padding = new Thickness(12, 0, 12, 0), FontSize = 12, CornerRadius = new CornerRadius(15) };
            b.Click += (s, e) => a();
            return b;
        }

        private void Render()
        {
            _ring.SetValue(_f.Fraction);
            _label.Text = _f.PhaseLabel;
            _time.Text = _f.RemainingText;
            if (_controls.Children[0] is FlatButton pause) pause.Content = _f.Paused ? "Resume" : "Pause";
        }

        private void Place()
        {
            var work = WindowHelper.WorkAreaDips(this, true);
            Left = work.Left + (work.Width - ActualWidth) / 2;
            Top = work.Top;
        }
    }

    /// <summary>Makes calming background sounds on the fly, so no audio files are needed.</summary>
    public class AmbientPlayer
    {
        public static readonly string[] Sounds = { "Off", "Rain", "Ocean", "Brown noise", "White noise" };
        private MediaPlayer _player;
        private string _current;
        private double _volume = 0.4;

        public double Volume
        {
            get => _volume;
            set { _volume = value; if (_player != null) _player.Volume = value; }
        }

        public void Play(string sound, double volume)
        {
            _volume = volume;
            if (string.IsNullOrEmpty(sound) || sound == "Off") { Stop(); return; }
            if (_player != null && _current == sound) { _player.Volume = volume; return; }
            Stop();
            try
            {
                string path = EnsureFile(sound);
                _player = new MediaPlayer { Volume = volume };
                _player.MediaEnded += (s, e) => { _player.Position = TimeSpan.Zero; _player.Play(); };
                _player.Open(new Uri(path));
                _player.Play();
                _current = sound;
            }
            catch (Exception ex) { Log.Write("Ambient sound failed: " + ex.Message); }
        }

        public void Stop()
        {
            if (_player != null)
            {
                _player.Stop();
                _player.Close();
                _player = null;
            }
            _current = null;
        }

        private static string EnsureFile(string sound)
        {
            string dir = Path.Combine(Settings.Folder, "sounds");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, sound.Replace(" ", "-").ToLowerInvariant() + ".wav");
            if (!File.Exists(path)) File.WriteAllBytes(path, Generate(sound));
            return path;
        }

        private static byte[] Generate(string sound)
        {
            const int rate = 44100;
            const int seconds = 24;
            int n = rate * seconds;
            int fade = rate * 2;
            var rnd = new Random(42);
            var buf = new double[n + fade];
            double brown = 0, b0 = 0, b1 = 0, b2 = 0;
            double drop = 0;
            for (int i = 0; i < buf.Length; i++)
            {
                double white = rnd.NextDouble() * 2 - 1;
                double v;
                switch (sound)
                {
                    case "White noise":
                        v = white * 0.35;
                        break;
                    case "Brown noise":
                        brown = (brown + 0.02 * white) / 1.02;
                        v = brown * 3.2;
                        break;
                    case "Ocean":
                        brown = (brown + 0.02 * white) / 1.02;
                        double t = (double)i / rate;
                        double swell = 0.35 + 0.65 * Math.Pow(Math.Sin(Math.PI * t / 8.0), 2);
                        v = brown * 3.5 * swell;
                        break;
                    default: // Rain: soft pink-ish noise with little droplets
                        b0 = 0.99765 * b0 + white * 0.0990460;
                        b1 = 0.96300 * b1 + white * 0.2965164;
                        b2 = 0.57000 * b2 + white * 1.0526913;
                        double pink = (b0 + b1 + b2 + white * 0.1848) * 0.11;
                        if (rnd.NextDouble() < 0.0009) drop = 0.5 + rnd.NextDouble() * 0.5;
                        drop *= 0.992;
                        v = pink * 0.9 + white * drop * 0.25;
                        break;
                }
                buf[i] = v;
            }
            // Crossfade the tail into the start so the loop is seamless.
            for (int i = 0; i < fade; i++)
            {
                double a = (double)i / fade;
                buf[i] = buf[i] * a + buf[n + i] * (1 - a);
            }
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            int dataBytes = n * 2;
            w.Write(new[] { 'R', 'I', 'F', 'F' });
            w.Write(36 + dataBytes);
            w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
            w.Write(16);
            w.Write((short)1);
            w.Write((short)1);
            w.Write(rate);
            w.Write(rate * 2);
            w.Write((short)2);
            w.Write((short)16);
            w.Write(new[] { 'd', 'a', 't', 'a' });
            w.Write(dataBytes);
            for (int i = 0; i < n; i++)
            {
                double v = Math.Max(-1, Math.Min(1, buf[i]));
                w.Write((short)(v * 30000));
            }
            w.Flush();
            return ms.ToArray();
        }
    }
}

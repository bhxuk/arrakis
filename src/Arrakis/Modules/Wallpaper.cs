using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Arrakis.Core;

namespace Arrakis.Modules
{
    /// <summary>Windows' own interface for setting a different wallpaper on each screen.</summary>
    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetMonitorDevicePathAt(uint monitorIndex);
        uint GetMonitorDevicePathCount();
        Native.RECT GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
        void SetBackgroundColor(uint color);
        uint GetBackgroundColor();
        void SetPosition(int position);
        int GetPosition();
        void SetSlideshow(IntPtr items);
        IntPtr GetSlideshow();
        void SetSlideshowOptions(int options, uint slideshowTick);
        void GetSlideshowOptions(out int options, out uint slideshowTick);
        void AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string monitorID, int direction);
        int GetStatus();
        [return: MarshalAs(UnmanagedType.Bool)] bool Enable([MarshalAs(UnmanagedType.Bool)] bool enable);
    }

    public class WallpaperModule : ModuleBase
    {
        public override string Id => "wallpaper";
        public override string Name => "Wallpaper";
        public override string Description => "Scenes that follow the sun and the weather, or your own photos, on every screen.";
        public override string IconData => Icons.Wallpaper;
        public override string ShortcutText => "Automatic";
        public override bool IsEnabled { get => Settings.Current.Wallpaper.Enabled; set => Settings.Current.Wallpaper.Enabled = value; }

        public static readonly string[] Scenes = { "Dawn", "Day", "Sunset", "Night" };

        private DispatcherTimer _timer;
        private string _applied = "";
        private DateTime _lastPhoto = DateTime.MinValue;
        private WallpaperSettings S => Settings.Current.Wallpaper;
        public string CurrentScene { get; private set; } = "";

        private static string Dir => Path.Combine(Settings.Folder, "wallpapers");

        protected override void OnStart()
        {
            if (string.IsNullOrEmpty(S.OriginalWallpaper))
            {
                var sb = new StringBuilder(520);
                if (Native.SystemParametersInfo(Native.SPI_GETDESKWALLPAPER, (uint)sb.Capacity, sb, 0))
                {
                    string cur = sb.ToString();
                    if (!cur.StartsWith(Dir, StringComparison.OrdinalIgnoreCase)) S.OriginalWallpaper = cur;
                    Settings.Save();
                }
            }
            _applied = "";
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            _timer.Tick += async (s, e) => await Update();
            _timer.Start();
            Weather.Updated += OnWeather;
            _ = Update();
        }

        protected override void OnStop()
        {
            _timer?.Stop();
            Weather.Updated -= OnWeather;
            if (!string.IsNullOrEmpty(S.OriginalWallpaper) && File.Exists(S.OriginalWallpaper))
                Native.SystemParametersInfo(Native.SPI_SETDESKWALLPAPER, 0, S.OriginalWallpaper, Native.SPIF_UPDATEINIFILE | Native.SPIF_SENDCHANGE);
            _applied = "";
        }

        private void OnWeather() => Application.Current.Dispatcher.BeginInvoke(new Action(async () => { if (IsRunning) await Update(); }));

        public void Reapply()
        {
            if (!IsRunning) return;
            _applied = "";
            _lastPhoto = DateTime.MinValue;
            _ = Update();
        }

        // ---------------- choosing what to show ----------------
        private async Task Update()
        {
            try
            {
                switch (S.Mode)
                {
                    case "Photo":
                    {
                        string key = "bing-" + DateTime.Today.ToString("yyyyMMdd");
                        if (_applied == key) return;
                        string path = await DailyPhoto();
                        if (path != null) { SetForAll(_ => path); _applied = key; CurrentScene = "Bing photo"; }
                        return;
                    }
                    case "Folder":
                    {
                        if ((DateTime.Now - _lastPhoto).TotalMinutes < Math.Max(1, S.PhotoMinutes) && _applied == "folder") return;
                        var photos = Photos(S.PhotoFolder);
                        if (photos.Count == 0) return;
                        var rnd = new Random();
                        SetForAll(_ => photos[rnd.Next(photos.Count)]);
                        _lastPhoto = DateTime.Now;
                        _applied = "folder";
                        CurrentScene = "Your photos";
                        return;
                    }
                    case "ScenePhotos":
                    {
                        string scene = SceneForNow();
                        if (!S.ScenePhotos.TryGetValue(scene, out var photo) || !File.Exists(photo)) goto default;
                        if (_applied == "photo-" + scene) return;
                        SetForAll(_ => photo);
                        _applied = "photo-" + scene;
                        CurrentScene = scene + " (your photo)";
                        return;
                    }
                    default:
                    {
                        string scene = S.Mode == "Auto" || S.Mode == "ScenePhotos" ? SceneForNow() : S.Mode;
                        string weather = S.MatchWeather ? WeatherKind() : "clear";
                        int sunStep = S.MovingSun ? (int)Math.Round(SunFraction(scene) * 24) : -1;
                        string key = scene + "|" + weather + "|" + sunStep;
                        if (_applied == key) return;
                        SetForAll(r => EnsureScene(scene, r.Width, r.Height, weather, sunStep));
                        _applied = key;
                        CurrentScene = scene + (weather != "clear" ? ", " + weather : "");
                        Cleanup();
                        return;
                    }
                }
            }
            catch (Exception ex) { Log.Write("Wallpaper update failed: " + ex.Message); }
        }

        /// <summary>Sets a (possibly different) picture on every screen. Falls back to one picture everywhere.</summary>
        private static void SetForAll(Func<Native.RECT, string> pathFor)
        {
            try
            {
                var type = Type.GetTypeFromCLSID(new Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD"));
                var dw = (IDesktopWallpaper)Activator.CreateInstance(type);
                uint count = dw.GetMonitorDevicePathCount();
                for (uint i = 0; i < count; i++)
                {
                    string id = dw.GetMonitorDevicePathAt(i);
                    Native.RECT r;
                    try { r = dw.GetMonitorRECT(id); } catch { continue; }
                    if (r.Width <= 0 || r.Height <= 0) continue;
                    string path = pathFor(r);
                    if (!string.IsNullOrEmpty(path)) dw.SetWallpaper(id, path);
                }
                dw.SetPosition(4); // fill
                Marshal.ReleaseComObject(dw);
                return;
            }
            catch (Exception ex) { Log.Write("Per-screen wallpaper unavailable, using one picture: " + ex.Message); }
            var primary = Native.GetPrimaryMonitor().rcMonitor;
            string single = pathFor(primary);
            if (!string.IsNullOrEmpty(single))
                Native.SystemParametersInfo(Native.SPI_SETDESKWALLPAPER, 0, single, Native.SPIF_UPDATEINIFILE | Native.SPIF_SENDCHANGE);
        }

        public static List<string> Photos(string folder)
        {
            try
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return new List<string>();
                var exts = new[] { ".jpg", ".jpeg", ".png", ".bmp" };
                return Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories).Where(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant())).Take(5000).ToList();
            }
            catch { return new List<string>(); }
        }

        public static (DateTime sunrise, DateTime sunset) TodaySun()
        {
            var w = Settings.Current.Widgets;
            double lat = double.IsNaN(w.Latitude) ? 52.48 : w.Latitude;    // default: Birmingham, UK
            double lon = double.IsNaN(w.Longitude) ? -1.89 : w.Longitude;
            return Sun.Times(DateTime.Today, lat, lon);
        }

        /// <summary>Dawn runs from 30 minutes before sunrise to 90 after, sunset from 60 before sunset to 30 after.</summary>
        public static (DateTime dawn, DateTime day, DateTime sunset, DateTime night) Boundaries()
        {
            var (rise, set) = TodaySun();
            return (rise.AddMinutes(-30), rise.AddMinutes(90), set.AddMinutes(-60), set.AddMinutes(30));
        }

        public static string SceneForNow()
        {
            var now = DateTime.Now;
            var b = Boundaries();
            if (now >= b.dawn && now < b.day) return "Dawn";
            if (now >= b.day && now < b.sunset) return "Day";
            if (now >= b.sunset && now < b.night) return "Sunset";
            return "Night";
        }

        /// <summary>How far through the day (or night) we are, 0 to 1, for placing the sun or moon.</summary>
        public static double SunFraction(string scene)
        {
            var (rise, set) = TodaySun();
            var now = DateTime.Now;
            if (scene == "Night")
            {
                DateTime start = now < rise ? set.AddDays(-1) : set;
                DateTime end = now < rise ? rise : rise.AddDays(1);
                return Clamp((now - start).TotalMinutes / Math.Max(1, (end - start).TotalMinutes));
            }
            return Clamp((now - rise).TotalMinutes / Math.Max(1, (set - rise).TotalMinutes));
        }

        private static double Clamp(double v) => Math.Max(0, Math.Min(1, v));

        public static string WeatherKind()
        {
            var w = Weather.Latest;
            if (w == null) return "clear";
            int c = w.Code;
            if (c >= 95) return "storm";
            if ((c >= 71 && c <= 77) || c == 85 || c == 86) return "snow";
            if ((c >= 51 && c <= 67) || (c >= 80 && c <= 82)) return "rain";
            if (c == 45 || c == 48) return "fog";
            if (c == 3) return "cloudy";
            if (c == 1 || c == 2) return "partly cloudy";
            return "clear";
        }

        // ---------------- drawing ----------------
        public static string EnsureScene(string scene, int width, int height, string weather = "clear", int sunStep = -1)
        {
            Directory.CreateDirectory(Dir);
            int w = Math.Max(800, width), h = Math.Max(600, height);
            string tag = scene.ToLowerInvariant() + "-" + weather.Replace(" ", "") + (sunStep >= 0 ? "-s" + sunStep : "") + "-" + w + "x" + h;
            string path = Path.Combine(Dir, tag + ".png");
            if (File.Exists(path)) return path;
            var bmp = Render(scene, w, h, weather, sunStep >= 0 ? sunStep / 24.0 : -1);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var fs = File.Create(path)) enc.Save(fs);
            return path;
        }

        private static void Cleanup()
        {
            try
            {
                var files = new DirectoryInfo(Dir).GetFiles("*.png").Where(f => !f.Name.StartsWith("photo-")).OrderByDescending(f => f.LastWriteTime).Skip(40);
                foreach (var f in files) try { f.Delete(); } catch { }
            }
            catch { }
        }

        private class Palette
        {
            public string S1, S2, S3, Sun, H1, H2, H3;
            public double SunY, SunR;
            public bool Stars;
        }

        private static Palette PaletteFor(string scene) => scene switch
        {
            "Dawn" => new Palette { S1 = "#2B3A67", S2 = "#E58F8F", S3 = "#F6C9A0", Sun = "#FFE3B0", SunY = 470, SunR = 70, H1 = "#5A4A6E", H2 = "#3E3654", H3 = "#26223A" },
            "Day" => new Palette { S1 = "#4C8FD6", S2 = "#9CCBEF", S3 = "#DCEFFA", Sun = "#FFF6D8", SunY = 170, SunR = 70, H1 = "#6FA27A", H2 = "#4F8461", H3 = "#33603F" },
            "Sunset" => new Palette { S1 = "#3B2A5A", S2 = "#D9665A", S3 = "#F6B26B", Sun = "#FFD38A", SunY = 420, SunR = 80, H1 = "#6B3B55", H2 = "#4A2A45", H3 = "#2A1A30" },
            _ => new Palette { S1 = "#070B1C", S2 = "#121B3A", S3 = "#26305A", Sun = "#E9EEF8", SunY = 150, SunR = 34, H1 = "#1B2244", H2 = "#141A36", H3 = "#0C1026", Stars = true }
        };

        private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

        private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

        public static BitmapSource Render(string scene, int width, int height) => Render(scene, width, height, "clear", -1);

        /// <summary>Draws a layered-hills landscape. The design is 1280x800 and is scaled to cover the screen.</summary>
        public static BitmapSource Render(string scene, int width, int height, string weather, double sun)
        {
            var p = PaletteFor(scene);
            bool grey = weather == "cloudy" || weather == "rain" || weather == "storm" || weather == "fog" || weather == "snow";
            Color s1 = C(p.S1), s2 = C(p.S2), s3 = C(p.S3);
            if (grey)
            {
                var g = scene == "Night" ? C("#1C2030") : C("#8E98A8");
                double amt = weather == "storm" ? 0.7 : weather == "fog" ? 0.6 : 0.5;
                s1 = Mix(s1, g, amt); s2 = Mix(s2, g, amt); s3 = Mix(s3, g, amt * 0.8);
            }

            // Where the sun (or moon) sits.
            double sunX = 900, sunY = p.SunY;
            if (sun >= 0)
            {
                sunX = 160 + sun * 960;
                double peak = scene == "Night" ? 330 : 360;
                sunY = 470 - Math.Sin(Math.PI * sun) * peak;
            }

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                double s = Math.Max(width / 1280.0, height / 800.0);
                double ox = (width - 1280 * s) / 2, oy = (height - 800 * s) / 2;
                dc.PushTransform(new TranslateTransform(ox, oy));
                dc.PushTransform(new ScaleTransform(s, s));

                var sky = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                sky.GradientStops.Add(new GradientStop(s1, 0));
                sky.GradientStops.Add(new GradientStop(s2, 0.6));
                sky.GradientStops.Add(new GradientStop(s3, 1));
                dc.DrawRectangle(sky, null, new Rect(-200, -200, 1680, 1200));

                var rnd = new Random(7);
                if (p.Stars && !grey)
                {
                    for (int i = 0; i < 90; i++)
                    {
                        double x = rnd.NextDouble() * 1280, y = rnd.NextDouble() * 420;
                        double r = 0.6 + rnd.NextDouble() * 1.2;
                        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(120 + rnd.Next(135)), 255, 255, 255)), null, new Point(x, y), r, r);
                    }
                }

                if (!grey || weather == "snow")
                {
                    var sunC = C(p.Sun);
                    var glow = new RadialGradientBrush(Color.FromArgb(0x55, sunC.R, sunC.G, sunC.B), Color.FromArgb(0, sunC.R, sunC.G, sunC.B));
                    dc.DrawEllipse(glow, null, new Point(sunX, sunY), p.SunR * 3, p.SunR * 3);
                    dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(grey ? (byte)0x80 : (byte)0xFF, sunC.R, sunC.G, sunC.B)), null, new Point(sunX, sunY), p.SunR, p.SunR);
                }

                // Clouds
                int clouds = weather == "partly cloudy" ? 4 : grey ? 9 : 0;
                bool night = scene == "Night";
                for (int i = 0; i < clouds; i++)
                {
                    double cx = rnd.NextDouble() * 1400 - 60, cy = 60 + rnd.NextDouble() * 260, scale = 0.7 + rnd.NextDouble() * 0.9;
                    byte a = (byte)(grey ? 150 : 200);
                    Color cc = night ? Color.FromArgb(a, 0x3A, 0x42, 0x5C) : grey ? Color.FromArgb(a, 0xC4, 0xCA, 0xD4) : Color.FromArgb(a, 0xFF, 0xFF, 0xFF);
                    var brush = new RadialGradientBrush(cc, Color.FromArgb(0, cc.R, cc.G, cc.B));
                    foreach (var (dx, dy, rx, ry) in new[] { (0.0, 0.0, 120.0, 46.0), (-80.0, 14.0, 80.0, 36.0), (85.0, 12.0, 90.0, 38.0), (30.0, -26.0, 80.0, 44.0) })
                        dc.DrawEllipse(brush, null, new Point(cx + dx * scale, cy + dy * scale), rx * scale * 1.3, ry * scale * 1.3);
                }

                dc.DrawGeometry(new SolidColorBrush(C(p.H1)), null, Geometry.Parse("M0 520 C 200 440 380 490 560 455 C 760 410 980 480 1280 430 L1280 800 L0 800 Z"));
                dc.DrawGeometry(new SolidColorBrush(C(p.H2)), null, Geometry.Parse("M0 610 C 240 555 420 595 640 565 C 880 532 1060 585 1280 555 L1280 800 L0 800 Z"));
                dc.DrawGeometry(new SolidColorBrush(C(p.H3)), null, Geometry.Parse("M0 700 C 300 660 520 690 760 670 C 980 652 1120 680 1280 670 L1280 800 L0 800 Z"));

                if (weather == "snow")
                {
                    // Snow on the hilltops and falling flakes.
                    dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(0x70, 255, 255, 255)), null, Geometry.Parse("M0 520 C 200 440 380 490 560 455 C 760 410 980 480 1280 430 L1280 452 C 980 502 760 432 560 477 C 380 512 200 462 0 542 Z"));
                    for (int i = 0; i < 280; i++)
                    {
                        double x = rnd.NextDouble() * 1280, y = rnd.NextDouble() * 800, r = 1 + rnd.NextDouble() * 2.4;
                        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(140 + rnd.Next(110)), 255, 255, 255)), null, new Point(x, y), r, r);
                    }
                }
                if (weather == "rain" || weather == "storm")
                {
                    var pen = new Pen(new SolidColorBrush(Color.FromArgb(weather == "storm" ? (byte)0x70 : (byte)0x55, 0xDD, 0xE6, 0xF2)), 1.4);
                    for (int i = 0; i < 260; i++)
                    {
                        double x = rnd.NextDouble() * 1400 - 60, y = rnd.NextDouble() * 820 - 20, len = 14 + rnd.NextDouble() * 18;
                        dc.DrawLine(pen, new Point(x, y), new Point(x - len * 0.25, y + len));
                    }
                }
                if (weather == "fog")
                {
                    foreach (var y in new[] { 430.0, 520, 610, 700 })
                    {
                        var band = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                        var fc = night ? Color.FromArgb(0x50, 0x9A, 0xA4, 0xB8) : Color.FromArgb(0x70, 0xF4, 0xF5, 0xF7);
                        band.GradientStops.Add(new GradientStop(Color.FromArgb(0, fc.R, fc.G, fc.B), 0));
                        band.GradientStops.Add(new GradientStop(fc, 0.5));
                        band.GradientStops.Add(new GradientStop(Color.FromArgb(0, fc.R, fc.G, fc.B), 1));
                        dc.DrawRectangle(band, null, new Rect(-100, y - 60, 1480, 120));
                    }
                }
                dc.Pop();
                dc.Pop();
            }
            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        /// <summary>Bing's photo of the day.</summary>
        private static async Task<string> DailyPhoto()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                string path = Path.Combine(Dir, "photo-" + DateTime.Today.ToString("yyyyMMdd") + ".jpg");
                if (File.Exists(path)) return path;
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                string json = await http.GetStringAsync("https://www.bing.com/HPImageArchive.aspx?format=js&idx=0&n=1&mkt=en-GB");
                using var doc = JsonDocument.Parse(json);
                string url = doc.RootElement.GetProperty("images")[0].GetProperty("urlbase").GetString();
                var bytes = await http.GetByteArrayAsync("https://www.bing.com" + url + "_UHD.jpg");
                await File.WriteAllBytesAsync(path, bytes);
                foreach (var old in Directory.GetFiles(Dir, "photo-*.jpg"))
                    if (!string.Equals(old, path, StringComparison.OrdinalIgnoreCase)) try { File.Delete(old); } catch { }
                return path;
            }
            catch (Exception ex)
            {
                Log.Write("Daily photo failed: " + ex.Message);
                return null;
            }
        }

        // ---------------- settings page ----------------
        private static readonly (string label, string mode)[] Modes =
        {
            ("Follow the sun", "Auto"), ("Dawn", "Dawn"), ("Day", "Day"), ("Sunset", "Sunset"), ("Night", "Night"),
            ("Your photos (slideshow)", "Folder"), ("Your photos for each time of day", "ScenePhotos"), ("Bing photo of the day", "Photo")
        };

        public override UIElement BuildPage()
        {
            var preview = new Image { Stretch = Stretch.UniformToFill };
            var previewBox = new Border { Height = 340, CornerRadius = new CornerRadius(20), ClipToBounds = true, Child = preview };
            var showing = UI.Text("", 16, Theme.Text, FontWeights.Bold);
            var badge = new Border
            {
                Background = Theme.Glass,
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(18),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Child = UI.V(2, UI.Label("Preview"), showing)
            };
            var previewGrid = new Grid();
            previewGrid.Children.Add(previewBox);
            previewGrid.Children.Add(badge);

            var times = Boundaries();
            string[] starts = { times.dawn.ToString("HH:mm"), times.day.ToString("HH:mm"), times.sunset.ToString("HH:mm"), times.night.ToString("HH:mm") };

            var thumbs = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4 };
            var thumbButtons = new List<FlatButton>();
            void Select(string scene)
            {
                string weather = S.MatchWeather ? WeatherKind() : "clear";
                double sun = S.MovingSun && scene == SceneForNow() ? SunFraction(scene) : -1;
                preview.Source = Render(scene, 1280, 800, weather, sun);
                showing.Text = scene + (weather != "clear" ? " · " + weather : "") + (S.Mode == "Auto" ? " · automatic" : "");
                for (int i = 0; i < Scenes.Length; i++) thumbButtons[i].BorderBrush = Scenes[i] == scene ? Theme.Amber : Brushes.Transparent;
            }
            for (int i = 0; i < Scenes.Length; i++)
            {
                string scene = Scenes[i];
                var img = new Image { Source = Render(scene, 320, 180), Stretch = Stretch.UniformToFill, Height = 100 };
                var imgBox = new Border { CornerRadius = new CornerRadius(11), ClipToBounds = true, Child = img };
                var caption = new DockPanel { Margin = new Thickness(4, 8, 4, 2) };
                var time = UI.Text(starts[i], 13, Theme.Muted);
                DockPanel.SetDock(time, System.Windows.Controls.Dock.Right);
                caption.Children.Add(time);
                caption.Children.Add(UI.Text(scene, 13, Theme.Text, FontWeights.Bold));
                var btn = new FlatButton
                {
                    Content = UI.V(0, imgBox, caption),
                    Background = Theme.Panel,
                    BorderThickness = new Thickness(2),
                    BorderBrush = Brushes.Transparent,
                    Padding = new Thickness(6),
                    CornerRadius = new CornerRadius(16),
                    Margin = new Thickness(0, 0, 12, 0),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch
                };
                FlatButton.SetA11yName(btn, "Use the " + scene + " scene");
                btn.Click += (s, e) =>
                {
                    S.Mode = scene;
                    Settings.Save();
                    if (!IsRunning) App.Instance.SetModuleEnabled(this, true); else Reapply();
                    Select(scene);
                };
                thumbButtons.Add(btn);
                thumbs.Children.Add(btn);
            }

            var mode = UI.Combo(Modes.Select(m => m.label).ToArray(), Modes.FirstOrDefault(m => m.mode == S.Mode).label ?? "Follow the sun", v =>
            {
                S.Mode = Modes.First(m => m.label == v).mode;
                Settings.Save();
                Reapply();
                Select(Scenes.Contains(S.Mode) ? S.Mode : SceneForNow());
            });

            var weather = new ToggleSwitch(S.MatchWeather, "Match the weather");
            weather.Toggled += on => { S.MatchWeather = on; Settings.Save(); Reapply(); Select(SceneForNow()); };
            var moving = new ToggleSwitch(S.MovingSun, "Moving sun");
            moving.Toggled += on => { S.MovingSun = on; Settings.Save(); Reapply(); Select(SceneForNow()); };

            // Your photos
            var folderText = UI.Text(string.IsNullOrEmpty(S.PhotoFolder) ? "No folder chosen." : S.PhotoFolder + " (" + Photos(S.PhotoFolder).Count + " photos)", 12, Theme.Muted);
            var chooseFolder = FlatButton.Secondary("Choose folder...", () =>
            {
                var dlg = new System.Windows.Forms.FolderBrowserDialog { SelectedPath = S.PhotoFolder, Description = "Choose a folder of photos" };
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                S.PhotoFolder = dlg.SelectedPath;
                Settings.Save();
                folderText.Text = S.PhotoFolder + " (" + Photos(S.PhotoFolder).Count + " photos)";
                if (S.Mode == "Folder") Reapply();
            });
            chooseFolder.MinHeight = 36;
            var every = new MiniSlider(5, 240, S.PhotoMinutes, null, 200) { Step = 5 };
            var everyText = UI.Text(S.PhotoMinutes + " min", 13, Theme.Muted);
            every.ValueChanged += v => { S.PhotoMinutes = (int)v; everyText.Text = (int)v + " min"; Settings.Save(); };

            var scenePhotos = UI.V(0);
            foreach (var scene in Scenes)
            {
                string sc = scene;
                S.ScenePhotos.TryGetValue(sc, out var current);
                var label = UI.Text(string.IsNullOrEmpty(current) ? "Uses the drawn scene" : Path.GetFileName(current), 12, Theme.Muted);
                var pick = FlatButton.Secondary("Choose...", () =>
                {
                    var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Photos|*.jpg;*.jpeg;*.png;*.bmp", Title = "Photo for " + sc };
                    if (dlg.ShowDialog() != true) return;
                    S.ScenePhotos[sc] = dlg.FileName;
                    Settings.Save();
                    label.Text = Path.GetFileName(dlg.FileName);
                    if (S.Mode == "ScenePhotos") Reapply();
                });
                pick.MinHeight = 34;
                var clear = FlatButton.IconButton(Icons.Close, "Use the drawn scene", () =>
                {
                    S.ScenePhotos.Remove(sc);
                    Settings.Save();
                    label.Text = "Uses the drawn scene";
                    if (S.Mode == "ScenePhotos") Reapply();
                }, 34);
                scenePhotos.Children.Add(UI.SettingRow(sc, null, UI.H(8, label, pick, clear)));
            }

            var w = Settings.Current.Widgets;
            string where = double.IsNaN(w.Latitude) ? "Using Birmingham, UK. Set your city in Widgets for exact times and weather." : "Sunrise, sunset and weather for " + w.City + ".";
            var restore = FlatButton.Secondary("Put my old wallpaper back", () => App.Instance.SetModuleEnabled(this, false));

            Select(Scenes.Contains(S.Mode) ? S.Mode : SceneForNow());

            return UI.V(16,
                previewGrid,
                UI.Label("Scenes · click one to keep it"),
                thumbs,
                UI.Card(UI.V(0,
                    UI.SettingRow("Mode", where, mode),
                    UI.Divider(),
                    UI.SettingRow("Match the weather", "Clouds, rain, snow, fog and storms appear when they're happening outside.", weather),
                    UI.Divider(),
                    UI.SettingRow("Moving sun", "The sun travels across the sky through the day, and the moon at night. Updated every few minutes.", moving),
                    UI.Divider(),
                    UI.SettingRow("Every screen", "Each screen gets a picture drawn at its own size. With your photos, each screen gets a different one.", UI.Text("Automatic", 13, Theme.Muted)),
                    UI.Divider(),
                    UI.SettingRow("Your original wallpaper", "Arrakis remembers it and puts it back when you turn this module off.", restore)), 8),
                UI.Label("Your photos"),
                UI.Card(UI.V(0,
                    UI.SettingRow("Slideshow folder", "Used by the \"Your photos (slideshow)\" mode. Subfolders are included.", UI.V(4, chooseFolder, folderText)),
                    UI.Divider(),
                    UI.SettingRow("Change every", null, UI.H(10, every, everyText)),
                    UI.Divider(),
                    UI.Text("Photos for each time of day, used by the \"Your photos for each time of day\" mode:", 13, Theme.Muted).WithMargin(new Thickness(0, 10, 0, 4)),
                    scenePhotos), 8));
        }
    }
}

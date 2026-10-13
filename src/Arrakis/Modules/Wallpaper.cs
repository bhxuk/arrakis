using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
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
    public class WallpaperModule : ModuleBase
    {
        public override string Id => "wallpaper";
        public override string Name => "Wallpaper";
        public override string Description => "Changes with the time of day, from sunrise to night.";
        public override string IconData => Icons.Wallpaper;
        public override string ShortcutText => "Automatic";
        public override bool IsEnabled { get => Settings.Current.Wallpaper.Enabled; set => Settings.Current.Wallpaper.Enabled = value; }

        public static readonly string[] Scenes = { "Dawn", "Day", "Sunset", "Night" };

        private DispatcherTimer _timer;
        private string _applied = "";
        private WallpaperSettings S => Settings.Current.Wallpaper;
        public event Action Applied;
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
            _ = Update();
        }

        protected override void OnStop()
        {
            _timer?.Stop();
            if (!string.IsNullOrEmpty(S.OriginalWallpaper) && File.Exists(S.OriginalWallpaper))
                SetWallpaper(S.OriginalWallpaper);
            _applied = "";
        }

        public void Reapply()
        {
            if (!IsRunning) return;
            _applied = "";
            _ = Update();
        }

        private async Task Update()
        {
            try
            {
                if (S.Mode == "Photo")
                {
                    string key = "photo-" + DateTime.Today.ToString("yyyyMMdd");
                    if (_applied == key) return;
                    string path = await DailyPhoto();
                    if (path != null)
                    {
                        SetWallpaper(path);
                        _applied = key;
                        CurrentScene = "Photo";
                        Applied?.Invoke();
                    }
                    return;
                }
                string scene = S.Mode == "Auto" ? SceneForNow() : S.Mode;
                if (_applied == scene) return;
                string file = EnsureScene(scene);
                SetWallpaper(file);
                _applied = scene;
                CurrentScene = scene;
                Applied?.Invoke();
            }
            catch (Exception ex) { Log.Write("Wallpaper update failed: " + ex.Message); }
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

        private static void SetWallpaper(string path)
        {
            Native.SystemParametersInfo(Native.SPI_SETDESKWALLPAPER, 0, path, Native.SPIF_UPDATEINIFILE | Native.SPIF_SENDCHANGE);
        }

        public static string EnsureScene(string scene)
        {
            Directory.CreateDirectory(Dir);
            var mon = Native.GetPrimaryMonitor().rcMonitor;
            int w = Math.Max(800, mon.Width), h = Math.Max(600, mon.Height);
            string path = Path.Combine(Dir, scene.ToLowerInvariant() + "-" + w + "x" + h + ".png");
            if (File.Exists(path)) return path;
            var bmp = Render(scene, w, h);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var fs = File.Create(path)) enc.Save(fs);
            return path;
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

        /// <summary>Draws a layered-hills landscape. The design is 1280x800 and is scaled to cover the screen.</summary>
        public static BitmapSource Render(string scene, int width, int height)
        {
            var p = PaletteFor(scene);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                double sx = width / 1280.0, sy = height / 800.0;
                double s = Math.Max(sx, sy);
                double ox = (width - 1280 * s) / 2, oy = (height - 800 * s) / 2;
                dc.PushTransform(new TranslateTransform(ox, oy));
                dc.PushTransform(new ScaleTransform(s, s));

                var sky = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                sky.GradientStops.Add(new GradientStop(C(p.S1), 0));
                sky.GradientStops.Add(new GradientStop(C(p.S2), 0.6));
                sky.GradientStops.Add(new GradientStop(C(p.S3), 1));
                dc.DrawRectangle(sky, null, new Rect(-200, -200, 1680, 1200));

                if (p.Stars)
                {
                    var rnd = new Random(7);
                    for (int i = 0; i < 90; i++)
                    {
                        double x = rnd.NextDouble() * 1280, y = rnd.NextDouble() * 420;
                        double r = 0.6 + rnd.NextDouble() * 1.2;
                        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(120 + rnd.Next(135)), 255, 255, 255)), null, new Point(x, y), r, r);
                    }
                }

                // Soft glow around the sun.
                var glow = new RadialGradientBrush(Color.FromArgb(0x55, C(p.Sun).R, C(p.Sun).G, C(p.Sun).B), Color.FromArgb(0, C(p.Sun).R, C(p.Sun).G, C(p.Sun).B));
                dc.DrawEllipse(glow, null, new Point(900, p.SunY), p.SunR * 3, p.SunR * 3);
                dc.DrawEllipse(new SolidColorBrush(C(p.Sun)), null, new Point(900, p.SunY), p.SunR, p.SunR);

                dc.DrawGeometry(new SolidColorBrush(C(p.H1)), null, Geometry.Parse("M0 520 C 200 440 380 490 560 455 C 760 410 980 480 1280 430 L1280 800 L0 800 Z"));
                dc.DrawGeometry(new SolidColorBrush(C(p.H2)), null, Geometry.Parse("M0 610 C 240 555 420 595 640 565 C 880 532 1060 585 1280 555 L1280 800 L0 800 Z"));
                dc.DrawGeometry(new SolidColorBrush(C(p.H3)), null, Geometry.Parse("M0 700 C 300 660 520 690 760 670 C 980 652 1120 680 1280 670 L1280 800 L0 800 Z"));
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

        public override UIElement BuildPage()
        {
            var preview = new Image { Stretch = Stretch.UniformToFill };
            var previewBox = new Border { Height = 360, CornerRadius = new CornerRadius(20), ClipToBounds = true, Child = preview };
            var showing = UI.Text("", 16, Theme.Text, FontWeights.Bold);
            var badge = new Border
            {
                Background = Theme.Glass,
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(18),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Child = UI.V(2, UI.Label("Showing"), showing)
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
                preview.Source = Render(scene, 1280, 800);
                showing.Text = scene + (S.Mode == "Auto" ? " · automatic" : " · fixed");
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

            var mode = UI.Combo(new[] { "Follow the sun", "Dawn", "Day", "Sunset", "Night", "Bing photo of the day" },
                S.Mode == "Auto" ? "Follow the sun" : S.Mode == "Photo" ? "Bing photo of the day" : S.Mode, v =>
                {
                    S.Mode = v == "Follow the sun" ? "Auto" : v == "Bing photo of the day" ? "Photo" : v;
                    Settings.Save();
                    Reapply();
                    Select(S.Mode == "Auto" || S.Mode == "Photo" ? SceneForNow() : S.Mode);
                });

            var w = Settings.Current.Widgets;
            string where = double.IsNaN(w.Latitude) ? "Using Birmingham, UK. Set your city in Widgets for exact times." : "Sunrise and sunset for " + w.City + ".";
            var restore = FlatButton.Secondary("Put my old wallpaper back", () => App.Instance.SetModuleEnabled(this, false));
            restore.HorizontalAlignment = HorizontalAlignment.Left;

            Select(S.Mode == "Auto" || S.Mode == "Photo" ? SceneForNow() : S.Mode);

            return UI.V(16,
                previewGrid,
                UI.Label("Scenes · click one to keep it"),
                thumbs,
                UI.Card(UI.V(0,
                    UI.SettingRow("Mode", where, mode),
                    UI.Divider(),
                    UI.SettingRow("Your original wallpaper", "Arrakis remembers it and puts it back when you turn this module off.", restore)), 8));
        }
    }
}

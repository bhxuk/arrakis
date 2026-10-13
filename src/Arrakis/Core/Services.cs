using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;

namespace Arrakis.Core
{
    /// <summary>CPU, memory and disk usage.</summary>
    public static class SystemStats
    {
        private static ulong _lastIdle, _lastTotal;

        public static double CpuPercent()
        {
            if (!Native.GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
            ulong i = idle.Value, total = kernel.Value + user.Value;
            ulong di = i - _lastIdle, dt = total - _lastTotal;
            _lastIdle = i;
            _lastTotal = total;
            if (dt == 0) return 0;
            return Math.Max(0, Math.Min(100, 100.0 * (dt - di) / dt));
        }

        public static double MemoryPercent()
        {
            var m = new Native.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.MEMORYSTATUSEX)) };
            return Native.GlobalMemoryStatusEx(ref m) ? m.dwMemoryLoad : 0;
        }

        public static double DiskPercent()
        {
            try
            {
                var d = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
                return 100.0 * (d.TotalSize - d.TotalFreeSpace) / d.TotalSize;
            }
            catch { return 0; }
        }
    }

    public class PowerState
    {
        public bool HasBattery;
        public bool OnMains;
        public int Percent;
        public int SecondsLeft;   // -1 when unknown
        public bool Charging;
    }

    public static class Power
    {
        public static PowerState Read()
        {
            var s = new PowerState { Percent = 100, SecondsLeft = -1 };
            if (!Native.GetSystemPowerStatus(out var st)) return s;
            s.HasBattery = st.BatteryFlag != 128 && st.BatteryFlag != 255;
            s.OnMains = st.ACLineStatus == 1;
            s.Percent = st.BatteryLifePercent <= 100 ? st.BatteryLifePercent : 100;
            s.SecondsLeft = st.BatteryLifeTime;
            s.Charging = (st.BatteryFlag & 8) != 0;
            return s;
        }

        public static string FormatTime(int seconds)
        {
            if (seconds < 0) return "";
            int h = seconds / 3600, m = (seconds % 3600) / 60;
            return h > 0 ? h + " h " + m + " min" : m + " min";
        }
    }

    /// <summary>What's playing, from Windows' own media controls (Spotify, browsers, Media Player...).</summary>
    public sealed class MediaService
    {
        public static MediaService Instance { get; } = new MediaService();

        private GlobalSystemMediaTransportControlsSessionManager _manager;
        private GlobalSystemMediaTransportControlsSession _session;
        private readonly Dispatcher _ui = Application.Current.Dispatcher;

        public string Title { get; private set; } = "";
        public string Artist { get; private set; } = "";
        public string AppName { get; private set; } = "";
        public bool IsPlaying { get; private set; }
        public bool HasMedia => !string.IsNullOrEmpty(Title);
        public ImageSource Art { get; private set; }
        public double Position { get; private set; }
        public double Duration { get; private set; }

        public event Action Changed;

        public async void Start()
        {
            try
            {
                _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                _manager.CurrentSessionChanged += (s, e) => Attach();
                Attach();
            }
            catch (Exception ex) { Log.Write("Media controls unavailable: " + ex.Message); }
        }

        private void Attach()
        {
            try
            {
                var session = _manager?.GetCurrentSession();
                if (_session != null)
                {
                    _session.MediaPropertiesChanged -= OnProps;
                    _session.PlaybackInfoChanged -= OnPlayback;
                    _session.TimelinePropertiesChanged -= OnTimeline;
                }
                _session = session;
                if (_session != null)
                {
                    _session.MediaPropertiesChanged += OnProps;
                    _session.PlaybackInfoChanged += OnPlayback;
                    _session.TimelinePropertiesChanged += OnTimeline;
                }
                Refresh();
            }
            catch (Exception ex) { Log.Write("Media attach failed: " + ex.Message); }
        }

        private void OnProps(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) => Refresh();
        private void OnPlayback(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) => Refresh();
        private void OnTimeline(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs e) => Refresh();

        private async void Refresh()
        {
            try
            {
                var s = _session;
                if (s == null)
                {
                    Title = Artist = AppName = "";
                    IsPlaying = false;
                    Art = null;
                }
                else
                {
                    var props = await s.TryGetMediaPropertiesAsync();
                    var info = s.GetPlaybackInfo();
                    var tl = s.GetTimelineProperties();
                    Title = props?.Title ?? "";
                    Artist = props?.Artist ?? "";
                    AppName = FriendlyApp(s.SourceAppUserModelId);
                    IsPlaying = info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                    Position = tl?.Position.TotalSeconds ?? 0;
                    Duration = tl?.EndTime.TotalSeconds ?? 0;
                    Art = null;
                    if (props?.Thumbnail != null)
                    {
                        using var stream = await props.Thumbnail.OpenReadAsync();
                        var bytes = new byte[stream.Size];
                        await stream.ReadAsync(bytes.AsBuffer(), (uint)stream.Size, Windows.Storage.Streams.InputStreamOptions.None);
                        Art = await _ui.InvokeAsync(() =>
                        {
                            try
                            {
                                var img = new BitmapImage();
                                img.BeginInit();
                                img.CacheOption = BitmapCacheOption.OnLoad;
                                img.StreamSource = new MemoryStream(bytes);
                                img.EndInit();
                                img.Freeze();
                                return (ImageSource)img;
                            }
                            catch { return null; }
                        });
                    }
                }
            }
            catch (Exception ex) { Log.Write("Media refresh failed: " + ex.Message); }
            _ui.BeginInvoke(new Action(() => Changed?.Invoke()));
        }

        private static string FriendlyApp(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            string s = id.ToLowerInvariant();
            if (s.Contains("spotify")) return "Spotify";
            if (s.Contains("chrome")) return "Chrome";
            if (s.Contains("msedge")) return "Edge";
            if (s.Contains("firefox")) return "Firefox";
            if (s.Contains("zune") || s.Contains("media")) return "Media Player";
            int bang = id.IndexOf('!');
            string name = bang > 0 ? id.Substring(0, bang) : id;
            name = Path.GetFileNameWithoutExtension(name);
            return name;
        }

        public async void PlayPause() { try { if (_session != null) await _session.TryTogglePlayPauseAsync(); } catch { } }
        public async void Next() { try { if (_session != null) await _session.TrySkipNextAsync(); } catch { } }
        public async void Previous() { try { if (_session != null) await _session.TrySkipPreviousAsync(); } catch { } }
    }

    public class WeatherNow
    {
        public double Temp;
        public double High;
        public double Low;
        public int Code;
        public string City = "";
        public (string time, double temp)[] Hours = Array.Empty<(string, double)>();
        public DateTime Fetched;
    }

    /// <summary>Weather from Open-Meteo (free, no account or key needed).</summary>
    public static class Weather
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        public static WeatherNow Latest { get; private set; }
        public static event Action Updated;

        static Weather()
        {
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("Arrakis/1.0 (+https://github.com/bhxuk/arrakis)");
        }

        public static async Task<(double lat, double lon, string name)?> Geocode(string city)
        {
            try
            {
                string url = "https://geocoding-api.open-meteo.com/v1/search?count=1&language=en&format=json&name=" + Uri.EscapeDataString(city);
                using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
                if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0) return null;
                var r = results[0];
                string name = r.GetProperty("name").GetString();
                if (r.TryGetProperty("country_code", out var cc)) name += ", " + cc.GetString();
                return (r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble(), name);
            }
            catch (Exception ex) { Log.Write("Geocode failed: " + ex.Message); return null; }
        }

        public static async Task Refresh()
        {
            var w = Settings.Current.Widgets;
            if (double.IsNaN(w.Latitude) || double.IsNaN(w.Longitude)) return;
            try
            {
                string unit = w.Fahrenheit ? "&temperature_unit=fahrenheit" : "";
                string url = string.Format(CultureInfo.InvariantCulture,
                    "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}&current=temperature_2m,weather_code&hourly=temperature_2m&daily=temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=2{2}",
                    w.Latitude, w.Longitude, unit);
                using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
                var root = doc.RootElement;
                var now = new WeatherNow { City = w.City, Fetched = DateTime.Now };
                var cur = root.GetProperty("current");
                now.Temp = cur.GetProperty("temperature_2m").GetDouble();
                now.Code = cur.GetProperty("weather_code").GetInt32();
                var daily = root.GetProperty("daily");
                now.High = daily.GetProperty("temperature_2m_max")[0].GetDouble();
                now.Low = daily.GetProperty("temperature_2m_min")[0].GetDouble();
                var times = root.GetProperty("hourly").GetProperty("time");
                var temps = root.GetProperty("hourly").GetProperty("temperature_2m");
                string curTime = cur.GetProperty("time").GetString();
                var hours = new System.Collections.Generic.List<(string, double)>();
                for (int i = 0; i < times.GetArrayLength() && hours.Count < 4; i++)
                {
                    string t = times[i].GetString();
                    if (string.CompareOrdinal(t, curTime) > 0)
                        hours.Add((t.Substring(11, 5), temps[i].GetDouble()));
                }
                now.Hours = hours.ToArray();
                Latest = now;
                Updated?.Invoke();
            }
            catch (Exception ex) { Log.Write("Weather failed: " + ex.Message); }
        }

        public static string Describe(int code)
        {
            if (code == 0) return "Clear";
            if (code <= 2) return "Partly cloudy";
            if (code == 3) return "Cloudy";
            if (code == 45 || code == 48) return "Fog";
            if (code >= 51 && code <= 57) return "Drizzle";
            if (code >= 61 && code <= 67) return "Rain";
            if (code >= 71 && code <= 77) return "Snow";
            if (code >= 80 && code <= 82) return "Showers";
            if (code >= 85 && code <= 86) return "Snow showers";
            if (code >= 95) return "Thunderstorm";
            return "";
        }

        public static string IconFor(int code)
        {
            if (code == 0 || code == 1) return Icons.Sun;
            if (code == 45 || code == 48) return Icons.Fog;
            if ((code >= 51 && code <= 67) || (code >= 80 && code <= 82)) return Icons.Rain;
            if ((code >= 71 && code <= 77) || code == 85 || code == 86) return Icons.Snow;
            if (code >= 95) return Icons.Storm;
            return Icons.Cloud;
        }
    }

    /// <summary>Sunrise and sunset times (NOAA's simplified solar formula).</summary>
    public static class Sun
    {
        public static (DateTime sunrise, DateTime sunset) Times(DateTime date, double lat, double lon)
        {
            DateTime rise = Calc(date, lat, lon, true);
            DateTime set = Calc(date, lat, lon, false);
            return (rise, set);
        }

        private static DateTime Calc(DateTime date, double lat, double lon, bool rising)
        {
            const double zenith = 90.833;
            int n = date.DayOfYear;
            double lngHour = lon / 15.0;
            double t = n + ((rising ? 6 : 18) - lngHour) / 24.0;
            double M = 0.9856 * t - 3.289;
            double L = M + 1.916 * Math.Sin(Rad(M)) + 0.020 * Math.Sin(Rad(2 * M)) + 282.634;
            L = Norm(L, 360);
            double RA = Deg(Math.Atan(0.91764 * Math.Tan(Rad(L))));
            RA = Norm(RA, 360);
            double Lq = Math.Floor(L / 90) * 90;
            double RAq = Math.Floor(RA / 90) * 90;
            RA = (RA + (Lq - RAq)) / 15.0;
            double sinDec = 0.39782 * Math.Sin(Rad(L));
            double cosDec = Math.Cos(Math.Asin(sinDec));
            double cosH = (Math.Cos(Rad(zenith)) - sinDec * Math.Sin(Rad(lat))) / (cosDec * Math.Cos(Rad(lat)));
            if (cosH > 1) return date.Date.AddHours(rising ? 12 : 12);   // sun never rises
            if (cosH < -1) return date.Date.AddHours(rising ? 0 : 24);   // sun never sets
            double H = rising ? 360 - Deg(Math.Acos(cosH)) : Deg(Math.Acos(cosH));
            H /= 15.0;
            double T = H + RA - 0.06571 * t - 6.622;
            double UT = Norm(T - lngHour, 24);
            var utc = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Utc).AddHours(UT);
            return utc.ToLocalTime();
        }

        private static double Rad(double d) => d * Math.PI / 180.0;
        private static double Deg(double r) => r * 180.0 / Math.PI;
        private static double Norm(double v, double m) { v %= m; return v < 0 ? v + m : v; }
    }

    /// <summary>Checks GitHub for a newer release.</summary>
    public static class Updates
    {
        public const string Repo = "bhxuk/arrakis";
        public static string LatestVersion { get; private set; }
        public static string LatestUrl { get; private set; } = "https://github.com/" + Repo + "/releases/latest";

        public static Version Current => typeof(Updates).Assembly.GetName().Version ?? new Version(1, 0, 0);

        public static string CurrentText => Current.Major + "." + Current.Minor + "." + Current.Build;

        public static async Task<bool> Check()
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Arrakis/" + CurrentText);
                using var doc = JsonDocument.Parse(await http.GetStringAsync("https://api.github.com/repos/" + Repo + "/releases/latest"));
                string tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
                LatestUrl = doc.RootElement.GetProperty("html_url").GetString() ?? LatestUrl;
                LatestVersion = tag.TrimStart('v', 'V');
                return Version.TryParse(LatestVersion, out var v) && v > new Version(Current.Major, Current.Minor, Math.Max(0, Current.Build));
            }
            catch (Exception ex)
            {
                Log.Write("Update check failed: " + ex.Message);
                return false;
            }
        }
    }
}

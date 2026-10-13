using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Arrakis.Core
{
    public class CalendarEvent
    {
        public string Uid = "";
        public string Title = "";
        public string Location = "";
        public DateTime Start;
        public DateTime End;
        public bool AllDay;
    }

    /// <summary>
    /// Reads a calendar from an .ics link (Google Calendar's "secret address in iCal format",
    /// Outlook's "publish calendar" ICS link, iCloud public calendars and so on).
    /// </summary>
    public static class CalendarFeed
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        public static List<CalendarEvent> Events { get; private set; } = new List<CalendarEvent>();
        public static DateTime LastUpdated { get; private set; }
        public static string LastError { get; private set; }
        public static event Action Updated;

        static CalendarFeed()
        {
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("Arrakis/1.0 (+https://github.com/bhxuk/arrakis)");
        }

        public static bool IsSet => !string.IsNullOrWhiteSpace(Settings.Current.CalendarUrl);

        public static async Task Refresh()
        {
            string url = Settings.Current.CalendarUrl?.Trim();
            if (string.IsNullOrEmpty(url)) { Events = new List<CalendarEvent>(); Updated?.Invoke(); return; }
            if (url.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)) url = "https://" + url.Substring(9);
            try
            {
                string text = await Http.GetStringAsync(url);
                Events = Parse(text, DateTime.Today.AddDays(-1), DateTime.Today.AddDays(31));
                LastUpdated = DateTime.Now;
                LastError = null;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Log.Write("Calendar failed: " + ex.Message);
            }
            Updated?.Invoke();
        }

        public static IEnumerable<CalendarEvent> Upcoming(int max) =>
            Events.Where(e => e.End > DateTime.Now).OrderBy(e => e.Start).Take(max);

        public static IEnumerable<CalendarEvent> On(DateTime day) =>
            Events.Where(e => e.Start.Date <= day.Date && (e.End > day.Date || e.Start.Date == day.Date)).OrderBy(e => e.Start);

        // ---------------- parsing ----------------
        private class Raw
        {
            public Dictionary<string, (string value, Dictionary<string, string> p)> Props = new Dictionary<string, (string, Dictionary<string, string>)>(StringComparer.OrdinalIgnoreCase);
            public List<(string value, Dictionary<string, string> p)> ExDates = new List<(string, Dictionary<string, string>)>();
        }

        public static List<CalendarEvent> Parse(string ics, DateTime from, DateTime to)
        {
            // Unfold continuation lines.
            var lines = new List<string>();
            foreach (var raw in ics.Replace("\r\n", "\n").Split('\n'))
            {
                if ((raw.StartsWith(" ") || raw.StartsWith("\t")) && lines.Count > 0) lines[lines.Count - 1] += raw.Substring(1);
                else lines.Add(raw);
            }

            var events = new List<Raw>();
            Raw cur = null;
            foreach (var line in lines)
            {
                if (line.Equals("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase)) { cur = new Raw(); continue; }
                if (line.Equals("END:VEVENT", StringComparison.OrdinalIgnoreCase)) { if (cur != null) events.Add(cur); cur = null; continue; }
                if (cur == null) continue;
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                string head = line.Substring(0, colon), value = line.Substring(colon + 1);
                var parts = head.Split(';');
                var p = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in parts.Skip(1))
                {
                    int eq = kv.IndexOf('=');
                    if (eq > 0) p[kv.Substring(0, eq)] = kv.Substring(eq + 1).Trim('"');
                }
                if (parts[0].Equals("EXDATE", StringComparison.OrdinalIgnoreCase)) cur.ExDates.Add((value, p));
                else cur.Props[parts[0]] = (value, p);
            }

            // Edited single occurrences of a repeating event replace the original.
            var overrides = events.Where(e => e.Props.ContainsKey("RECURRENCE-ID")).ToList();
            var result = new List<CalendarEvent>();
            foreach (var e in events.Except(overrides))
            {
                try
                {
                    if (e.Props.TryGetValue("STATUS", out var status) && status.value.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!e.Props.TryGetValue("DTSTART", out var ds)) continue;
                    var start = ParseDate(ds.value, ds.p, out bool allDay);
                    DateTime end;
                    if (e.Props.TryGetValue("DTEND", out var de)) end = ParseDate(de.value, de.p, out _);
                    else if (e.Props.TryGetValue("DURATION", out var du)) end = start + ParseDuration(du.value);
                    else end = allDay ? start.AddDays(1) : start.AddHours(1);
                    var length = end - start;
                    string uid = e.Props.TryGetValue("UID", out var u) ? u.value : Guid.NewGuid().ToString();
                    string title = Unescape(e.Props.TryGetValue("SUMMARY", out var sm) ? sm.value : "(No title)");
                    string location = Unescape(e.Props.TryGetValue("LOCATION", out var lo) ? lo.value : "");
                    var excluded = new HashSet<DateTime>();
                    foreach (var (v, p) in e.ExDates)
                        foreach (var piece in v.Split(','))
                            try { excluded.Add(ParseDate(piece, p, out _)); } catch { }
                    var overridden = overrides.Where(o => o.Props.TryGetValue("UID", out var ou) && ou.value == uid)
                        .Select(o => { try { return (DateTime?)ParseDate(o.Props["RECURRENCE-ID"].value, o.Props["RECURRENCE-ID"].p, out _); } catch { return null; } })
                        .Where(d => d.HasValue).Select(d => d.Value).ToHashSet();

                    IEnumerable<DateTime> starts = e.Props.TryGetValue("RRULE", out var rr)
                        ? Expand(start, rr.value, from, to)
                        : new[] { start };
                    foreach (var s in starts)
                    {
                        if (excluded.Contains(s) || overridden.Contains(s)) continue;
                        if (s + length < from || s > to) continue;
                        result.Add(new CalendarEvent { Uid = uid + "@" + s.Ticks, Title = title, Location = location, Start = s, End = s + length, AllDay = allDay });
                    }
                }
                catch { }
            }
            foreach (var o in overrides)
            {
                try
                {
                    if (o.Props.TryGetValue("STATUS", out var status) && status.value.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase)) continue;
                    var start = ParseDate(o.Props["DTSTART"].value, o.Props["DTSTART"].p, out bool allDay);
                    var end = o.Props.TryGetValue("DTEND", out var de) ? ParseDate(de.value, de.p, out _) : start.AddHours(1);
                    if (end < from || start > to) continue;
                    result.Add(new CalendarEvent
                    {
                        Uid = (o.Props.TryGetValue("UID", out var u) ? u.value : "") + "@" + start.Ticks,
                        Title = Unescape(o.Props.TryGetValue("SUMMARY", out var sm) ? sm.value : "(No title)"),
                        Location = Unescape(o.Props.TryGetValue("LOCATION", out var lo) ? lo.value : ""),
                        Start = start,
                        End = end,
                        AllDay = allDay
                    });
                }
                catch { }
            }
            return result.OrderBy(r => r.Start).ToList();
        }

        private static string Unescape(string s) => s.Replace("\\n", " ").Replace("\\N", " ").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\").Trim();

        private static DateTime ParseDate(string value, Dictionary<string, string> p, out bool allDay)
        {
            value = value.Trim();
            allDay = false;
            if (value.Length == 8 || (p.TryGetValue("VALUE", out var v) && v.Equals("DATE", StringComparison.OrdinalIgnoreCase)))
            {
                allDay = true;
                return DateTime.ParseExact(value.Substring(0, 8), "yyyyMMdd", CultureInfo.InvariantCulture);
            }
            bool utc = value.EndsWith("Z");
            var dt = DateTime.ParseExact(value.TrimEnd('Z').Substring(0, 15), "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture);
            if (utc) return DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToLocalTime();
            if (p.TryGetValue("TZID", out var tzid))
            {
                try
                {
                    var tz = TimeZoneInfo.FindSystemTimeZoneById(tzid);
                    return TimeZoneInfo.ConvertTime(dt, tz, TimeZoneInfo.Local);
                }
                catch { }
            }
            return dt;
        }

        private static TimeSpan ParseDuration(string d)
        {
            // e.g. PT1H30M, P1D
            var ts = TimeSpan.Zero;
            int num = 0;
            bool time = false;
            foreach (char c in d.ToUpperInvariant())
            {
                if (char.IsDigit(c)) { num = num * 10 + (c - '0'); continue; }
                switch (c)
                {
                    case 'T': time = true; break;
                    case 'W': ts += TimeSpan.FromDays(7 * num); break;
                    case 'D': ts += TimeSpan.FromDays(num); break;
                    case 'H': ts += TimeSpan.FromHours(num); break;
                    case 'M': ts += time ? TimeSpan.FromMinutes(num) : TimeSpan.FromDays(30 * num); break;
                    case 'S': ts += TimeSpan.FromSeconds(num); break;
                }
                if (c != 'T' && c != 'P') num = 0;
            }
            return ts == TimeSpan.Zero ? TimeSpan.FromHours(1) : ts;
        }

        private static readonly string[] DayCodes = { "SU", "MO", "TU", "WE", "TH", "FR", "SA" };

        /// <summary>Handles the common repeat rules: daily, weekly (with days), monthly and yearly, with INTERVAL, COUNT and UNTIL.</summary>
        private static IEnumerable<DateTime> Expand(DateTime start, string rule, DateTime from, DateTime to)
        {
            var r = rule.Split(';').Select(x => x.Split('=')).Where(x => x.Length == 2).ToDictionary(x => x[0].ToUpperInvariant(), x => x[1], StringComparer.OrdinalIgnoreCase);
            string freq = r.TryGetValue("FREQ", out var f) ? f.ToUpperInvariant() : "DAILY";
            int interval = r.TryGetValue("INTERVAL", out var iv) && int.TryParse(iv, out var ii) && ii > 0 ? ii : 1;
            int count = r.TryGetValue("COUNT", out var cv) && int.TryParse(cv, out var cc) ? cc : int.MaxValue;
            DateTime until = DateTime.MaxValue;
            if (r.TryGetValue("UNTIL", out var uv)) { try { until = ParseDate(uv, new Dictionary<string, string>(), out _); if (uv.Length == 8) until = until.AddDays(1); } catch { } }
            var byDays = r.TryGetValue("BYDAY", out var bd)
                ? bd.Split(',').Select(x => Array.IndexOf(DayCodes, x.Length >= 2 ? x.Substring(x.Length - 2) : x)).Where(x => x >= 0).ToHashSet()
                : null;

            int produced = 0;
            int guard = 0;
            var cursor = start;
            while (guard++ < 5000)
            {
                if (cursor > until || produced >= count || cursor > to) yield break;
                if (freq == "WEEKLY" && byDays != null && byDays.Count > 0)
                {
                    // Walk each day of this week.
                    var weekStart = cursor.AddDays(-(int)cursor.DayOfWeek);
                    for (int d = 0; d < 7; d++)
                    {
                        var day = weekStart.AddDays(d);
                        var occ = day.Date + start.TimeOfDay;
                        if (occ < start || !byDays.Contains((int)day.DayOfWeek)) continue;
                        if (occ > until || produced >= count) yield break;
                        produced++;
                        if (occ >= from.AddDays(-1) && occ <= to) yield return occ;
                    }
                    cursor = weekStart.AddDays(7 * interval) + start.TimeOfDay;
                    continue;
                }
                produced++;
                if (cursor >= from.AddDays(-1)) yield return cursor;
                cursor = freq switch
                {
                    "WEEKLY" => cursor.AddDays(7 * interval),
                    "MONTHLY" => cursor.AddMonths(interval),
                    "YEARLY" => cursor.AddYears(interval),
                    _ => cursor.AddDays(interval)
                };
            }
        }
    }
}

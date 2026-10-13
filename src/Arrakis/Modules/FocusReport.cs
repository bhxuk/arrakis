using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Arrakis.Core;

namespace Arrakis.Modules
{
    /// <summary>A weekly summary of focus time: totals, records, trends and what you worked on.</summary>
    public static class FocusReport
    {
        private static int Minutes(FocusLog log, DateTime day) => log.MinutesByDay.TryGetValue(day.ToString("yyyy-MM-dd"), out int m) ? m : 0;

        private static DateTime WeekStart(DateTime d)
        {
            var first = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
            int diff = ((int)d.DayOfWeek - (int)first + 7) % 7;
            return d.Date.AddDays(-diff);
        }

        private static string Hm(int minutes) => minutes >= 60 ? (minutes / 60) + " h " + (minutes % 60) + " min" : minutes + " min";

        public static UIElement Build()
        {
            var log = Settings.LoadFile<FocusLog>("focus.json");
            var thisWeek = WeekStart(DateTime.Today);
            int week = Enumerable.Range(0, 7).Sum(i => Minutes(log, thisWeek.AddDays(i)));
            int lastWeek = Enumerable.Range(0, 7).Sum(i => Minutes(log, thisWeek.AddDays(i - 7)));

            // Best day ever and longest streak ever.
            var days = log.MinutesByDay.Where(kv => kv.Value > 0).Select(kv => (day: DateTime.ParseExact(kv.Key, "yyyy-MM-dd", CultureInfo.InvariantCulture), mins: kv.Value)).OrderBy(x => x.day).ToList();
            var best = days.OrderByDescending(d => d.mins).FirstOrDefault();
            int longest = 0, run = 0;
            DateTime? prev = null;
            foreach (var d in days)
            {
                run = prev.HasValue && (d.day - prev.Value).TotalDays == 1 ? run + 1 : 1;
                longest = Math.Max(longest, run);
                prev = d.day;
            }
            int activeDays = days.Count(d => d.day >= DateTime.Today.AddDays(-29));
            int month = days.Where(d => d.day >= DateTime.Today.AddDays(-29)).Sum(d => d.mins);

            UIElement Stat(string label, string value, string sub)
            {
                var v = UI.V(4, UI.Label(label), UI.Text(value, 24, Theme.Text, FontWeights.SemiBold, true), UI.Text(sub, 12, Theme.Muted));
                return new Border { Padding = new Thickness(0, 0, 16, 0), Child = v };
            }

            string trend = lastWeek == 0 ? (week > 0 ? "First week tracked" : "Nothing yet")
                : week >= lastWeek ? "Up " + Hm(week - lastWeek) + " on last week" : "Down " + Hm(lastWeek - week) + " on last week";
            var stats = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4 };
            stats.Children.Add(Stat("This week", Hm(week), trend));
            stats.Children.Add(Stat("Last 30 days", Hm(month), activeDays + (activeDays == 1 ? " day" : " days") + " with focus"));
            stats.Children.Add(Stat("Best day", best.mins > 0 ? Hm(best.mins) : "-", best.mins > 0 ? best.day.ToString("ddd d MMM") : "Keep going"));
            stats.Children.Add(Stat("Longest streak", longest + (longest == 1 ? " day" : " days"), "Now " + FocusModule.Streak() + (FocusModule.Streak() == 1 ? " day" : " days")));

            // Eight-week bars.
            var weeks = Enumerable.Range(0, 8).Select(i =>
            {
                var start = thisWeek.AddDays(-7 * (7 - i));
                return (start, mins: Enumerable.Range(0, 7).Sum(d => Minutes(log, start.AddDays(d))));
            }).ToList();
            int max = Math.Max(60, weeks.Max(w => w.mins));
            var chart = new Grid { Height = 130, Margin = new Thickness(0, 12, 0, 0) };
            for (int i = 0; i < weeks.Count; i++) chart.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < weeks.Count; i++)
            {
                var (start, mins) = weeks[i];
                bool current = i == weeks.Count - 1;
                var col = new DockPanel { Margin = new Thickness(6, 0, 6, 0) };
                var name = UI.Text(current ? "This week" : start.ToString("d MMM"), 11, current ? Theme.Amber : Theme.Muted, FontWeights.SemiBold);
                name.HorizontalAlignment = HorizontalAlignment.Center;
                DockPanel.SetDock(name, System.Windows.Controls.Dock.Bottom);
                var val = UI.Text(mins > 0 ? (mins >= 60 ? Math.Round(mins / 60.0, 1) + "h" : mins + "m") : "", 11, Theme.Muted);
                val.HorizontalAlignment = HorizontalAlignment.Center;
                DockPanel.SetDock(val, System.Windows.Controls.Dock.Top);
                var bar = new Border
                {
                    Height = Math.Max(3, 80.0 * mins / max),
                    CornerRadius = new CornerRadius(6),
                    Background = current ? Theme.Amber : Theme.Brush(Color.FromArgb(0x8C, Theme.TealColor.R, Theme.TealColor.G, Theme.TealColor.B)),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    MaxWidth = 48,
                    Margin = new Thickness(0, 4, 0, 6)
                };
                col.Children.Add(name);
                col.Children.Add(val);
                col.Children.Add(bar);
                Grid.SetColumn(col, i);
                chart.Children.Add(col);
            }

            // What you worked on.
            var recent = log.Sessions.Where(x => x.Start >= DateTime.Today.AddDays(-29)).ToList();
            var tasks = recent.Where(x => !string.IsNullOrWhiteSpace(x.Task)).GroupBy(x => x.Task.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => (task: g.First().Task.Trim(), mins: g.Sum(x => x.Minutes))).OrderByDescending(x => x.mins).Take(5).ToList();
            var taskList = UI.V(6);
            foreach (var (task, mins) in tasks)
            {
                var row = new DockPanel();
                var m = UI.Text(Hm(mins), 13, Theme.Muted);
                DockPanel.SetDock(m, System.Windows.Controls.Dock.Right);
                row.Children.Add(m);
                row.Children.Add(UI.Text(task, 14, Theme.Text, FontWeights.SemiBold));
                taskList.Children.Add(row);
            }
            if (tasks.Count == 0) taskList.Children.Add(UI.Text("Add what you're working on before a session to see it here.", 13, Theme.Muted));

            var sessions = UI.V(4);
            foreach (var x in log.Sessions.OrderByDescending(x => x.Start).Take(6))
            {
                var row = new DockPanel();
                var m = UI.Text(Hm(x.Minutes), 13, Theme.Muted);
                DockPanel.SetDock(m, System.Windows.Controls.Dock.Right);
                row.Children.Add(m);
                string what = string.IsNullOrWhiteSpace(x.Task) ? x.Label : x.Task;
                row.Children.Add(UI.Text(x.Start.ToString("ddd d MMM, HH:mm") + "   " + what, 13, Theme.Text));
                sessions.Children.Add(row);
            }
            if (log.Sessions.Count == 0) sessions.Children.Add(UI.Text("Your sessions will appear here.", 13, Theme.Muted));

            var lists = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Margin = new Thickness(0, 18, 0, 0) };
            lists.Children.Add(new Border { Padding = new Thickness(0, 0, 16, 0), Child = UI.V(8, UI.Label("Top tasks, last 30 days"), taskList) });
            lists.Children.Add(UI.V(8, UI.Label("Recent sessions"), sessions));

            return UI.Card(UI.V(0, stats, chart, lists), 20);
        }
    }
}

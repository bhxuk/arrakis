using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Arrakis.Core;

namespace Arrakis.Modules
{
    /// <summary>Switches between Windows' power modes: best efficiency, balanced and best performance.</summary>
    public static class PowerModes
    {
        [DllImport("powrprof.dll", EntryPoint = "PowerSetActiveOverlayScheme")]
        private static extern uint SetOverlay(ref Guid scheme);

        [DllImport("powrprof.dll", EntryPoint = "PowerGetEffectiveOverlayScheme")]
        private static extern uint GetOverlay(out Guid scheme);

        public static readonly (string name, string desc, Guid overlay, string plan, string icon)[] Modes =
        {
            ("Battery saver", "Longest battery life, a little slower", new Guid("961cc777-2547-4f9d-8174-7d86181b8a7a"), "SCHEME_MAX", Icons.Battery),
            ("Balanced", "Windows decides", Guid.Empty, "SCHEME_BALANCED", Icons.Rings),
            ("Best performance", "Fastest, uses more power", new Guid("ded574b5-45a0-4f42-8737-46345c09c238"), "SCHEME_MIN", Icons.Flame)
        };

        public static int Current()
        {
            try
            {
                if (GetOverlay(out Guid g) == 0)
                    for (int i = 0; i < Modes.Length; i++) if (Modes[i].overlay == g) return i;
            }
            catch { }
            return 1;
        }

        public static bool Set(int index)
        {
            var m = Modes[index];
            try
            {
                var g = m.overlay;
                if (SetOverlay(ref g) == 0) return true;
            }
            catch { }
            // Older Windows: switch the classic power plan instead.
            try
            {
                var p = Process.Start(new ProcessStartInfo("powercfg.exe", "/setactive " + m.plan) { CreateNoWindow = true, UseShellExecute = false });
                p?.WaitForExit(3000);
                return p != null && p.ExitCode == 0;
            }
            catch (Exception ex) { Log.Write("Power mode failed: " + ex.Message); return false; }
        }

        public static UIElement Build()
        {
            var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3 };
            var buttons = new List<FlatButton>();
            int current = Current();
            void Mark(int sel)
            {
                for (int i = 0; i < buttons.Count; i++)
                {
                    buttons[i].Background = i == sel ? Theme.AmberTint : Theme.Panel;
                    buttons[i].BorderBrush = i == sel ? Theme.Amber : Theme.Line;
                }
            }
            for (int i = 0; i < Modes.Length; i++)
            {
                int index = i;
                var m = Modes[i];
                var b = new FlatButton
                {
                    Content = UI.V(6, UI.GridIcon(m.icon, 22, Theme.Amber), UI.Text(m.name, 15, Theme.Text, FontWeights.SemiBold), UI.Text(m.desc, 12, Theme.Muted)),
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(18, 14, 18, 14),
                    CornerRadius = new CornerRadius(18),
                    BorderThickness = new Thickness(1.5),
                    Margin = new Thickness(0, 0, 12, 0)
                };
                b.Click += (s, e) => { if (Set(index)) Mark(index); };
                FlatButton.SetA11yName(b, m.name + " power mode");
                buttons.Add(b);
                grid.Children.Add(b);
            }
            Mark(current);
            return grid;
        }
    }
}

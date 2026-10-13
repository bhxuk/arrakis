using System;
using System.Collections.Generic;
using System.Windows.Input;
using System.Windows.Interop;

namespace Arrakis.Core
{
    /// <summary>A hidden window that receives hotkey and clipboard messages.</summary>
    public sealed class MessageWindow : IDisposable
    {
        public HwndSource Source { get; }
        public IntPtr Handle => Source.Handle;
        public event Func<int, IntPtr, IntPtr, bool> Message;

        public MessageWindow(string name)
        {
            var p = new HwndSourceParameters(name)
            {
                Width = 0,
                Height = 0,
                WindowStyle = 0,
                ParentWindow = Native.HWND_MESSAGE
            };
            Source = new HwndSource(p);
            Source.AddHook(Hook);
        }

        private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (Message != null && Message(msg, wParam, lParam)) handled = true;
            return IntPtr.Zero;
        }

        public void Dispose() => Source.Dispose();
    }

    /// <summary>Registers system-wide keyboard shortcuts such as Ctrl+Alt+V.</summary>
    public static class Hotkeys
    {
        private static MessageWindow _window;
        private static readonly Dictionary<int, Action> Actions = new Dictionary<int, Action>();
        private static readonly Dictionary<string, int> Ids = new Dictionary<string, int>();
        private static int _nextId = 0x5A00;

        public static void Init()
        {
            if (_window != null) return;
            _window = new MessageWindow("ArrakisHotkeys");
            _window.Message += (msg, w, l) =>
            {
                if (msg != Native.WM_HOTKEY) return false;
                if (Actions.TryGetValue(w.ToInt32(), out var a))
                {
                    try { a(); } catch (Exception ex) { Log.Write("Hotkey action failed: " + ex); }
                }
                return true;
            };
        }

        /// <summary>Registers a shortcut under a name. Returns false if another app already owns it.</summary>
        public static bool Register(string name, string shortcut, Action action)
        {
            Init();
            Unregister(name);
            if (string.IsNullOrWhiteSpace(shortcut)) return false;
            if (!TryParse(shortcut, out uint mods, out uint vk)) return false;
            int id = _nextId++;
            if (!Native.RegisterHotKey(_window.Handle, id, mods | Native.MOD_NOREPEAT, vk))
            {
                Log.Write("Could not register hotkey " + shortcut + " for " + name);
                return false;
            }
            Actions[id] = action;
            Ids[name] = id;
            return true;
        }

        public static void Unregister(string name)
        {
            if (_window == null) return;
            if (Ids.TryGetValue(name, out int id))
            {
                Native.UnregisterHotKey(_window.Handle, id);
                Actions.Remove(id);
                Ids.Remove(name);
            }
        }

        public static void UnregisterAll()
        {
            foreach (var name in new List<string>(Ids.Keys)) Unregister(name);
        }

        public static bool TryParse(string text, out uint mods, out uint vk)
        {
            mods = 0;
            vk = 0;
            foreach (var raw in text.Split('+'))
            {
                string part = raw.Trim().ToLowerInvariant();
                switch (part)
                {
                    case "ctrl":
                    case "control": mods |= Native.MOD_CONTROL; break;
                    case "alt": mods |= Native.MOD_ALT; break;
                    case "shift": mods |= Native.MOD_SHIFT; break;
                    case "win":
                    case "windows": mods |= Native.MOD_WIN; break;
                    case "printscreen":
                    case "prtsc":
                    case "print screen": vk = 0x2C; break;
                    default:
                        if (Enum.TryParse(raw.Trim(), true, out Key key))
                            vk = (uint)KeyInterop.VirtualKeyFromKey(key);
                        else if (part.Length == 1)
                            vk = (uint)char.ToUpperInvariant(part[0]);
                        break;
                }
            }
            return vk != 0;
        }
    }
}

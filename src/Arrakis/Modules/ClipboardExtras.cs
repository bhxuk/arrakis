using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using Arrakis.Core;

namespace Arrakis.Modules
{
    public class Snippet
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public string Shortcode { get; set; } = "";
        public string Text { get; set; } = "";
        public DateTime Changed { get; set; } = DateTime.Now;
    }

    /// <summary>Ways to change text as you paste it.</summary>
    public static class ClipTransforms
    {
        public static readonly string[] Names =
        {
            "UPPERCASE", "lowercase", "Title Case", "Sentence case", "Trim spaces", "One line",
            "List to commas", "Commas to list", "Remove link tracking", "Sort lines", "Remove duplicate lines"
        };

        public static string Apply(string name, string text)
        {
            text ??= "";
            var ti = CultureInfo.CurrentCulture.TextInfo;
            string[] Lines() => text.Replace("\r\n", "\n").Split('\n');
            switch (name)
            {
                case "UPPERCASE": return text.ToUpper(CultureInfo.CurrentCulture);
                case "lowercase": return text.ToLower(CultureInfo.CurrentCulture);
                case "Title Case": return ti.ToTitleCase(text.ToLower(CultureInfo.CurrentCulture));
                case "Sentence case":
                {
                    var lower = text.ToLower(CultureInfo.CurrentCulture);
                    return Regex.Replace(lower, @"(^\s*|[.!?]\s+)([a-z])", m => m.Groups[1].Value + m.Groups[2].Value.ToUpper(CultureInfo.CurrentCulture));
                }
                case "Trim spaces": return string.Join(Environment.NewLine, Lines().Select(l => Regex.Replace(l.Trim(), @"[ \t]{2,}", " "))).Trim();
                case "One line": return Regex.Replace(text, @"\s*[\r\n]+\s*", " ").Trim();
                case "List to commas": return string.Join(", ", Lines().Select(l => l.Trim().TrimStart('-', '*', '•').Trim()).Where(l => l.Length > 0));
                case "Commas to list": return string.Join(Environment.NewLine, text.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0));
                case "Remove link tracking": return Regex.Replace(text, @"https?://\S+", m => CleanUrl(m.Value));
                case "Sort lines": return string.Join(Environment.NewLine, Lines().Where(l => l.Trim().Length > 0).OrderBy(l => l, StringComparer.CurrentCultureIgnoreCase));
                case "Remove duplicate lines": return string.Join(Environment.NewLine, Lines().Distinct());
            }
            return text;
        }

        private static readonly string[] TrackingPrefixes = { "utm_", "fbclid", "gclid", "mc_", "igshid", "si", "ref_src", "spm", "_hs", "yclid", "msclkid" };

        public static string CleanUrl(string url)
        {
            try
            {
                int q = url.IndexOf('?');
                if (q < 0) return url;
                string fragment = "";
                int hash = url.IndexOf('#', q);
                if (hash >= 0) { fragment = url.Substring(hash); url = url.Substring(0, hash); }
                var keep = url.Substring(q + 1).Split('&').Where(p =>
                {
                    string key = p.Split('=')[0].ToLowerInvariant();
                    return key.Length > 0 && !TrackingPrefixes.Any(t => key == t || key.StartsWith(t) && t.EndsWith("_"));
                }).ToList();
                return url.Substring(0, q) + (keep.Count > 0 ? "?" + string.Join("&", keep) : "") + fragment;
            }
            catch { return url; }
        }
    }

    /// <summary>Spots card numbers and password-like text so they can be hidden and deleted quickly.</summary>
    public static class Sensitive
    {
        private static readonly Regex CardLike = new Regex(@"^[\d \-]{13,23}$", RegexOptions.Compiled);
        private static readonly Regex IbanLike = new Regex(@"^[A-Z]{2}\d{2}[A-Z0-9 ]{11,30}$", RegexOptions.Compiled);

        public static string Detect(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string t = text.Trim();
            if (CardLike.IsMatch(t))
            {
                string digits = new string(t.Where(char.IsDigit).ToArray());
                if (digits.Length >= 13 && digits.Length <= 19 && Luhn(digits)) return "Card number";
            }
            if (IbanLike.IsMatch(t.ToUpperInvariant()) && t.Replace(" ", "").Length >= 15) return "Bank account";
            // Password-like: one "word", 10 to 64 characters, mixes letters, digits and symbols.
            if (t.Length >= 10 && t.Length <= 64 && !t.Any(char.IsWhiteSpace) && !t.Contains("://") && !t.Contains('@'))
            {
                bool upper = t.Any(char.IsUpper), lower = t.Any(char.IsLower), digit = t.Any(char.IsDigit), symbol = t.Any(c => !char.IsLetterOrDigit(c));
                int kinds = (upper ? 1 : 0) + (lower ? 1 : 0) + (digit ? 1 : 0) + (symbol ? 1 : 0);
                if (kinds >= 4 || (kinds == 3 && symbol && t.Length >= 12)) return "Password";
            }
            return null;
        }

        private static bool Luhn(string digits)
        {
            int sum = 0;
            bool alt = false;
            for (int i = digits.Length - 1; i >= 0; i--)
            {
                int n = digits[i] - '0';
                if (alt) { n *= 2; if (n > 9) n -= 9; }
                sum += n;
                alt = !alt;
            }
            return sum % 10 == 0;
        }

        public static string Mask(string kind, string text)
        {
            string t = (text ?? "").Trim();
            if (kind == "Card number")
            {
                string digits = new string(t.Where(char.IsDigit).ToArray());
                return "•••• •••• •••• " + (digits.Length >= 4 ? digits.Substring(digits.Length - 4) : "");
            }
            return new string('•', Math.Min(12, Math.Max(8, t.Length)));
        }
    }

    /// <summary>Expands typed shortcodes like ;sig into snippets, in any app.</summary>
    public sealed class SnippetExpander : IDisposable
    {
        private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr extra; }

        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, HookProc fn, IntPtr hMod, uint threadId);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int code, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] private static extern short GetKeyState(int vk);
        [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint thread);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int ToUnicodeEx(uint vk, uint scan, byte[] state, StringBuilder buf, int size, uint flags, IntPtr layout);

        private const int WH_KEYBOARD_LL = 13, WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;
        private const uint LLKHF_INJECTED = 0x10;

        private readonly HookProc _proc;
        private IntPtr _hook;
        private readonly StringBuilder _typed = new StringBuilder();
        private readonly Func<IEnumerable<Snippet>> _snippets;
        private readonly Action<Snippet, int> _expand;
        private bool _busy;

        public SnippetExpander(Func<IEnumerable<Snippet>> snippets, Action<Snippet, int> expand)
        {
            _snippets = snippets;
            _expand = expand;
            _proc = Hook;
            using var p = Process.GetCurrentProcess();
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(p.MainModule?.ModuleName), 0);
            if (_hook == IntPtr.Zero) Log.Write("Snippet keyboard hook failed: " + Marshal.GetLastWin32Error());
        }

        public bool Busy { set => _busy = value; }

        private IntPtr Hook(int code, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (code >= 0 && !_busy && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
                {
                    var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                    if ((k.flags & LLKHF_INJECTED) == 0) OnKey(k.vkCode, k.scanCode);
                }
            }
            catch { }
            return CallNextHookEx(_hook, code, wParam, lParam);
        }

        private void OnKey(uint vk, uint scan)
        {
            // Navigation, Enter, Tab and Escape end a word; Backspace removes a character.
            if (vk == 0x08) { if (_typed.Length > 0) _typed.Length--; return; }
            if (vk == 0x0D || vk == 0x09 || vk == 0x1B || (vk >= 0x21 && vk <= 0x28)) { _typed.Clear(); return; }
            bool ctrl = (GetAsyncKeyState(0x11) & 0x8000) != 0, alt = (GetAsyncKeyState(0x12) & 0x8000) != 0, win = (GetAsyncKeyState(0x5B) & 0x8000) != 0;
            if (ctrl || alt || win) { _typed.Clear(); return; }
            if (vk == 0x10 || vk == 0xA0 || vk == 0xA1 || vk == 0x14) return;   // Shift and Caps Lock on their own

            var state = new byte[256];
            if ((GetAsyncKeyState(0x10) & 0x8000) != 0) state[0x10] = 0x80;
            if ((GetKeyState(0x14) & 1) != 0) state[0x14] = 0x01;
            var buf = new StringBuilder(4);
            var layout = GetKeyboardLayout(0);
            int n = ToUnicodeEx(vk, scan, state, buf, buf.Capacity, 4, layout);   // 4 = don't change keyboard state
            if (n <= 0) return;
            _typed.Append(buf.ToString(0, n));
            if (_typed.Length > 40) _typed.Remove(0, _typed.Length - 40);

            string typed = _typed.ToString();
            foreach (var s in _snippets())
            {
                if (string.IsNullOrEmpty(s.Shortcode) || s.Shortcode.Length < 2) continue;
                if (typed.EndsWith(s.Shortcode, StringComparison.Ordinal))
                {
                    _typed.Clear();
                    var snip = s;
                    int len = s.Shortcode.Length;
                    // Let the key reach the app first, then replace the shortcode.
                    Application.Current.Dispatcher.BeginInvoke(new Action(() => _expand(snip, len)), System.Windows.Threading.DispatcherPriority.Background);
                    return;
                }
            }
        }

        public void Dispose()
        {
            if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    /// <summary>Keeps pinned items, collections and snippets the same on all your PCs, through a shared folder.</summary>
    public static class ClipboardSync
    {
        private const string FileName = "arrakis-clipboard.sync";

        public class Payload
        {
            public List<ClipItem> Items { get; set; } = new List<ClipItem>();
            public List<Snippet> Snippets { get; set; } = new List<Snippet>();
            public DateTime Written { get; set; }
            public string Machine { get; set; } = "";
        }

        public static string PasswordToKeyStore(string password)
        {
            var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(password ?? ""), null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(bytes);
        }

        private static string Password()
        {
            try
            {
                var stored = Settings.Current.Clipboard.SyncKey;
                if (string.IsNullOrEmpty(stored)) return null;
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored), null, DataProtectionScope.CurrentUser));
            }
            catch { return null; }
        }

        private static byte[] Encrypt(byte[] plain, string password)
        {
            var salt = RandomNumberGenerator.GetBytes(16);
            var nonce = RandomNumberGenerator.GetBytes(12);
            var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, 200_000, HashAlgorithmName.SHA256, 32);
            var cipher = new byte[plain.Length];
            var tag = new byte[16];
            using (var aes = new AesGcm(key, 16)) aes.Encrypt(nonce, plain, cipher, tag);
            using var ms = new MemoryStream();
            ms.Write(Encoding.ASCII.GetBytes("ARK1"));
            ms.Write(salt);
            ms.Write(nonce);
            ms.Write(tag);
            ms.Write(cipher);
            return ms.ToArray();
        }

        private static byte[] Decrypt(byte[] data, string password)
        {
            if (data.Length < 48 || Encoding.ASCII.GetString(data, 0, 4) != "ARK1") throw new InvalidDataException("Not an Arrakis sync file.");
            var salt = data.AsSpan(4, 16).ToArray();
            var nonce = data.AsSpan(20, 12).ToArray();
            var tag = data.AsSpan(32, 16).ToArray();
            var cipher = data.AsSpan(48).ToArray();
            var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, 200_000, HashAlgorithmName.SHA256, 32);
            var plain = new byte[cipher.Length];
            using (var aes = new AesGcm(key, 16)) aes.Decrypt(nonce, cipher, tag, plain);
            return plain;
        }

        /// <summary>Merges the shared file into this PC, then writes the combined result back. Returns a short status.</summary>
        public static async Task<string> Run(ClipboardModule module)
        {
            var s = Settings.Current.Clipboard;
            string folder = s.SyncFolder, password = Password();
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return "Choose a sync folder first.";
            if (string.IsNullOrEmpty(password)) return "Set a sync password first.";
            string path = Path.Combine(folder, FileName);
            try
            {
                Payload remote = null;
                if (File.Exists(path))
                {
                    var bytes = await File.ReadAllBytesAsync(path);
                    try { remote = JsonSerializer.Deserialize<Payload>(Decrypt(bytes, password), Settings.Json); }
                    catch (CryptographicException) { return "The sync file was made with a different password."; }
                }
                int added = 0;
                if (remote != null) added = module.MergeFromSync(remote.Items, remote.Snippets);
                var local = new Payload
                {
                    Items = module.Items.Where(i => i.Kind != "Image" && !i.IsSensitive && (i.Pinned || !string.IsNullOrEmpty(i.Collection))).ToList(),
                    Snippets = s.Snippets.ToList(),
                    Written = DateTime.Now,
                    Machine = Environment.MachineName
                };
                var json = JsonSerializer.SerializeToUtf8Bytes(local, Settings.Json);
                string tmp = path + ".tmp";
                await File.WriteAllBytesAsync(tmp, Encrypt(json, password));
                File.Move(tmp, path, true);
                s.LastSync = DateTime.Now;
                Settings.Save();
                return "Synced at " + DateTime.Now.ToString("HH:mm") + (added > 0 ? ", " + added + " new from your other PCs." : ".");
            }
            catch (Exception ex)
            {
                Log.Write("Sync failed: " + ex.Message);
                return "Sync failed: " + ex.Message;
            }
        }
    }
}

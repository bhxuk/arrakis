using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Arrakis.Core;

namespace Arrakis.Modules
{
    public class ClipItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Kind { get; set; } = "Text";     // Text, Link, Colour, Image
        public string Text { get; set; } = "";
        public string ImageFile { get; set; } = "";
        public int ImageWidth { get; set; }
        public int ImageHeight { get; set; }
        public string Source { get; set; } = "";
        public DateTime Created { get; set; } = DateTime.Now;
        public bool Pinned { get; set; }
        public string Collection { get; set; } = "";
        public string SensitiveKind { get; set; } = "";
        public DateTime? ExpiresAt { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsSensitive => !string.IsNullOrEmpty(SensitiveKind);
    }

    public class ClipStore
    {
        public List<ClipItem> Items { get; set; } = new List<ClipItem>();
    }

    public class ClipboardModule : ModuleBase
    {
        public override string Id => "clipboard";
        public override string Name => "Clipboard";
        public override string Description => "Everything you copy, searchable. Snippets, collections, paste transformations and sync between PCs.";
        public override string IconData => Icons.Clipboard;
        public override string ShortcutText => Settings.Current.Clipboard.Hotkey;
        public override bool IsEnabled { get => Settings.Current.Clipboard.Enabled; set => Settings.Current.Clipboard.Enabled = value; }

        private MessageWindow _listener;
        private SnippetExpander _expander;
        private System.Windows.Threading.DispatcherTimer _housekeeping;
        public string SyncStatus { get; private set; } = "";
        private ClipStore _store = new ClipStore();
        private bool _ignoreNext;
        private ClipboardPanel _panel;
        public List<ClipItem> Stack { get; } = new List<ClipItem>();
        public IReadOnlyList<ClipItem> Items => _store.Items;
        public event Action ItemsChanged;

        private static string ImageDir => Path.Combine(Settings.Folder, "clipboard");
        private ClipboardSettings S => Settings.Current.Clipboard;

        private static readonly uint ExcludeFormat = Native.RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
        private static readonly uint IgnoreFormat = Native.RegisterClipboardFormat("Clipboard Viewer Ignore");
        private static readonly uint CanIncludeFormat = Native.RegisterClipboardFormat("CanIncludeInClipboardHistory");

        protected override void OnStart()
        {
            SeedSnippets();
            if (Settings.Current.Clipboard.SnippetsEnabled) _expander = new SnippetExpander(() => Settings.Current.Clipboard.Snippets, ExpandSnippet);
            _housekeeping = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            int ticks = 0;
            _housekeeping.Tick += async (s2, e2) =>
            {
                PurgeExpired();
                if (++ticks % 18 == 0 && !string.IsNullOrEmpty(Settings.Current.Clipboard.SyncFolder)) SyncStatus = await ClipboardSync.Run(this);
            };
            _housekeeping.Start();
            _store = Settings.LoadFile<ClipStore>("clipboard.json");
            _listener = new MessageWindow("ArrakisClipboard");
            _listener.Message += (msg, w, l) =>
            {
                if (msg != Native.WM_CLIPBOARDUPDATE) return false;
                OnClipboardChanged();
                return true;
            };
            Native.AddClipboardFormatListener(_listener.Handle);
            if (!Hotkeys.Register("clipboard", S.Hotkey, ShowPanel))
                App.Instance.Tray.Notify("Clipboard shortcut unavailable", S.Hotkey + " is used by another app. Choose a different one in Arrakis > Clipboard.");
        }

        protected override void OnStop()
        {
            _expander?.Dispose();
            _expander = null;
            _housekeeping?.Stop();
            if (_listener != null)
            {
                Native.RemoveClipboardFormatListener(_listener.Handle);
                _listener.Dispose();
                _listener = null;
            }
            Hotkeys.Unregister("clipboard");
            _panel?.Close();
        }

        private UIElement SnippetsCard()
        {
            var c = Settings.Current.Clipboard;
            var on = new ToggleSwitch(c.SnippetsEnabled, "Expand shortcodes");
            on.Toggled += v =>
            {
                c.SnippetsEnabled = v;
                Settings.Save();
                _expander?.Dispose();
                _expander = v && IsRunning ? new SnippetExpander(() => Settings.Current.Clipboard.Snippets, ExpandSnippet) : null;
            };
            var list = UI.V(4);
            void Render()
            {
                list.Children.Clear();
                foreach (var sn in c.Snippets.ToList())
                {
                    var snip = sn;
                    var edit = FlatButton.Secondary("Edit", () => { if (EditSnippet(snip)) Render(); });
                    edit.MinHeight = 34;
                    var del = FlatButton.IconButton(Icons.Trash, "Delete snippet", () => { c.Snippets.Remove(snip); Settings.Save(); Render(); }, 34);
                    string preview = (snip.Text ?? "").Replace("\r", "").Replace("\n", " ");
                    if (preview.Length > 60) preview = preview.Substring(0, 60) + "...";
                    list.Children.Add(UI.SettingRow(snip.Name + (string.IsNullOrEmpty(snip.Shortcode) ? "" : "   " + snip.Shortcode), preview, UI.H(8, edit, del)));
                }
                if (c.Snippets.Count == 0) list.Children.Add(UI.Text("No snippets yet.", 13, Theme.Muted));
            }
            Render();
            var add = FlatButton.Secondary("Add a snippet...", () =>
            {
                var sn = new Snippet();
                if (EditSnippet(sn)) { c.Snippets.Add(sn); Settings.Save(); Render(); }
            });
            add.HorizontalAlignment = HorizontalAlignment.Left;
            return UI.Card(UI.V(10,
                UI.SettingRow("Expand shortcodes as you type", "Type a shortcode like ;sig anywhere and it's replaced with the snippet. Snippets are also in the clipboard panel under Snippets.", on),
                UI.Divider(),
                list,
                add,
                UI.Text("In a snippet, {date}, {time} and {clipboard} are filled in for you.", 12, Theme.Muted)), 16);
        }

        private static bool EditSnippet(Snippet sn)
        {
            var dlg = new Window
            {
                Title = string.IsNullOrEmpty(sn.Name) ? "New snippet" : "Edit snippet",
                Width = 480,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = Theme.Bg,
                ResizeMode = ResizeMode.NoResize,
                FontFamily = Theme.Font
            };
            var name = UI.TextInput(sn.Name, 420);
            var code = UI.TextInput(sn.Shortcode, 200);
            var text = UI.TextInput(sn.Text, 420);
            text.AcceptsReturn = true;
            text.TextWrapping = TextWrapping.Wrap;
            text.Height = 140;
            text.VerticalContentAlignment = VerticalAlignment.Top;
            text.Padding = new Thickness(10, 8, 10, 8);
            var error = UI.Text("", 12, Theme.Red);
            bool saved = false;
            var ok = FlatButton.Primary("Save", () =>
            {
                string sc = code.Text.Trim();
                if (string.IsNullOrWhiteSpace(text.Text)) { error.Text = "Type the text to paste."; return; }
                if (sc.Length == 1) { error.Text = "Use at least 2 characters for the shortcode, like ;sig."; return; }
                sn.Name = string.IsNullOrWhiteSpace(name.Text) ? (sc.Length > 0 ? sc : "Snippet") : name.Text.Trim();
                sn.Shortcode = sc;
                sn.Text = text.Text;
                sn.Changed = DateTime.Now;
                Settings.Save();
                saved = true;
                dlg.Close();
            });
            dlg.Content = new Border
            {
                Padding = new Thickness(24),
                Child = UI.V(10,
                    UI.Text("Name", 13, Theme.Muted), name,
                    UI.Text("Shortcode (optional). Starting with ; avoids typing it by accident.", 13, Theme.Muted), code,
                    UI.Text("Text to paste", 13, Theme.Muted), text,
                    error, ok)
            };
            dlg.SourceInitialized += (s, e) => Native.UseDarkTitleBar(new WindowInteropHelper(dlg).Handle);
            dlg.ShowDialog();
            return saved;
        }

        private UIElement SensitiveCard()
        {
            var c = Settings.Current.Clipboard;
            var hide = new ToggleSwitch(c.HideSensitive, "Hide and delete private things");
            hide.Toggled += v => { c.HideSensitive = v; Settings.Save(); };
            var secs = new MiniSlider(10, 300, c.SensitiveSeconds, null, 200) { Step = 10 };
            var secsText = UI.Text(c.SensitiveSeconds + " s", 13, Theme.Muted);
            secs.ValueChanged += v => { c.SensitiveSeconds = (int)v; secsText.Text = (int)v + " s"; Settings.Save(); };
            return UI.Card(UI.V(0,
                UI.SettingRow("Hide card numbers and passwords", "Things that look like a card number, bank account or password are shown as dots, kept out of search and sync, and deleted after a short time. They're cleared from the clipboard too.", hide),
                UI.Divider(),
                UI.SettingRow("Delete after", null, UI.H(10, secs, secsText))), 8);
        }

        private UIElement SyncCard()
        {
            var c = Settings.Current.Clipboard;
            var folder = UI.Text(string.IsNullOrEmpty(c.SyncFolder) ? "No folder chosen." : c.SyncFolder, 12, Theme.Muted);
            var choose = FlatButton.Secondary("Choose folder...", () =>
            {
                var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = "Choose a folder that syncs between your PCs, like one in OneDrive or Dropbox" };
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                c.SyncFolder = dlg.SelectedPath;
                Settings.Save();
                folder.Text = c.SyncFolder;
            });
            choose.MinHeight = 36;
            var pass = new PasswordBox { Width = 200, Height = 36, FontSize = 14, Padding = new Thickness(10, 0, 10, 0), VerticalContentAlignment = VerticalAlignment.Center, Background = Theme.Subtle, Foreground = Theme.Text, BorderBrush = Theme.Line };
            var status = UI.Text(!string.IsNullOrEmpty(SyncStatus) ? SyncStatus : c.LastSync.HasValue ? "Last synced " + ClipboardPanel.Ago(c.LastSync.Value) + "." : "Not set up yet.", 12, Theme.Muted);
            var setPass = FlatButton.Secondary("Save password", () =>
            {
                if (pass.Password.Length < 8) { status.Text = "Use at least 8 characters."; return; }
                c.SyncKey = ClipboardSync.PasswordToKeyStore(pass.Password);
                Settings.Save();
                pass.Clear();
                status.Text = "Password saved. Use the same one on your other PCs.";
            });
            setPass.MinHeight = 36;
            var now = FlatButton.Primary("Sync now");
            now.Click += async (s, e) => { status.Text = "Syncing..."; SyncStatus = await ClipboardSync.Run(this); status.Text = SyncStatus; };
            now.HorizontalAlignment = HorizontalAlignment.Left;
            return UI.Card(UI.V(0,
                UI.Text("Keeps your pinned items, collections and snippets the same on every PC. Arrakis writes one encrypted file into a folder that you already sync, like OneDrive or Dropbox. Nothing goes anywhere else, and private things are never synced.", 13, Theme.Muted).WithMargin(new Thickness(0, 6, 0, 10)),
                UI.SettingRow("Sync folder", null, UI.V(4, choose, folder)),
                UI.Divider(),
                UI.SettingRow("Password", "Encrypts the sync file. Use the same password on each PC.", UI.H(8, pass, setPass)),
                UI.Divider(),
                UI.SettingRow("Sync", "Runs every 3 minutes while Arrakis is open.", UI.V(4, now, status))), 8);
        }

        private async void OnClipboardChanged()
        {
            if (_ignoreNext) { _ignoreNext = false; return; }
            // Respect apps (like password managers) that ask not to be recorded.
            if (Native.IsClipboardFormatAvailable(ExcludeFormat) || Native.IsClipboardFormatAvailable(IgnoreFormat)) return;
            if (Native.IsClipboardFormatAvailable(CanIncludeFormat))
            {
                try
                {
                    if (System.Windows.Clipboard.GetData("CanIncludeInClipboardHistory") is MemoryStream ms && ms.Length >= 4)
                    {
                        var b = ms.ToArray();
                        if (BitConverter.ToInt32(b, 0) == 0) return;
                    }
                }
                catch { }
            }
            string source = ForegroundApp();
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (System.Windows.Clipboard.ContainsText())
                    {
                        string text = System.Windows.Clipboard.GetText();
                        if (!string.IsNullOrWhiteSpace(text)) AddText(text, source);
                        return;
                    }
                    if (System.Windows.Clipboard.ContainsImage() && S.SaveImages)
                    {
                        var img = System.Windows.Clipboard.GetImage();
                        if (img != null) AddImage(img, source);
                        return;
                    }
                    return;
                }
                catch
                {
                    await Task.Delay(60);
                }
            }
        }

        private static string ForegroundApp()
        {
            try
            {
                var h = Native.GetForegroundWindow();
                Native.GetWindowThreadProcessId(h, out uint pid);
                var path = Native.GetProcessPath(pid);
                return string.IsNullOrEmpty(path) ? "" : WindowTracker.FriendlyName(path);
            }
            catch { return ""; }
        }

        private static readonly Regex HexColour = new Regex(@"^#?([0-9a-fA-F]{6}|[0-9a-fA-F]{3})$", RegexOptions.Compiled);
        private static readonly Regex RgbColour = new Regex(@"^rgba?\(\s*\d{1,3}\s*,\s*\d{1,3}\s*,\s*\d{1,3}", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private void AddText(string text, string source)
        {
            string trimmed = text.Trim();
            var existing = _store.Items.FirstOrDefault(i => i.Kind != "Image" && i.Text == text);
            if (existing != null)
            {
                _store.Items.Remove(existing);
                existing.Created = DateTime.Now;
                _store.Items.Insert(0, existing);
            }
            else
            {
                string kind = "Text";
                if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https") && !trimmed.Contains(' ')) kind = "Link";
                else if (trimmed.Length <= 32 && (HexColour.IsMatch(trimmed) && trimmed.StartsWith("#") || RgbColour.IsMatch(trimmed))) kind = "Colour";
                var clip = new ClipItem { Kind = kind, Text = text, Source = source };
                if (Settings.Current.Clipboard.HideSensitive)
                {
                    var sens = Sensitive.Detect(text);
                    if (sens != null)
                    {
                        clip.SensitiveKind = sens;
                        clip.ExpiresAt = DateTime.Now.AddSeconds(Math.Max(10, Settings.Current.Clipboard.SensitiveSeconds));
                    }
                }
                _store.Items.Insert(0, clip);
            }
            Trim();
            Save();
        }

        private void AddImage(BitmapSource img, string source)
        {
            Directory.CreateDirectory(ImageDir);
            var item = new ClipItem { Kind = "Image", Source = source, ImageWidth = img.PixelWidth, ImageHeight = img.PixelHeight };
            item.ImageFile = Path.Combine(ImageDir, item.Id + ".png");
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(img));
            using (var fs = File.Create(item.ImageFile)) enc.Save(fs);
            _store.Items.Insert(0, item);
            Trim();
            Save();
        }

        private void Trim()
        {
            int max = Math.Max(20, S.MaxItems);
            while (_store.Items.Count(i => !i.Pinned) > max)
            {
                var last = _store.Items.Last(i => !i.Pinned);
                Delete(last, false);
            }
        }

        public void Delete(ClipItem item, bool save = true)
        {
            _store.Items.Remove(item);
            Stack.Remove(item);
            if (item.Kind == "Image" && File.Exists(item.ImageFile)) try { File.Delete(item.ImageFile); } catch { }
            if (save) Save();
        }

        public void ClearUnpinned()
        {
            foreach (var i in _store.Items.Where(x => !x.Pinned).ToList()) Delete(i, false);
            Save();
        }

        public void TogglePin(ClipItem item)
        {
            item.Pinned = !item.Pinned;
            Save();
        }

        private void PurgeExpired()
        {
            var expired = _store.Items.Where(i => i.ExpiresAt.HasValue && i.ExpiresAt.Value < DateTime.Now && !i.Pinned).ToList();
            if (expired.Count == 0) return;
            foreach (var i in expired) Delete(i, false);
            // If it's still on the clipboard, clear that too.
            try
            {
                if (System.Windows.Clipboard.ContainsText() && expired.Any(i => i.Text == System.Windows.Clipboard.GetText()))
                {
                    _ignoreNext = true;
                    System.Windows.Clipboard.Clear();
                }
            }
            catch { }
            Save();
        }

        public IEnumerable<string> Collections => _store.Items.Select(i => i.Collection).Where(c => !string.IsNullOrEmpty(c)).Distinct().OrderBy(c => c);

        public void SetCollection(ClipItem item, string collection)
        {
            item.Collection = collection ?? "";
            if (!string.IsNullOrEmpty(item.Collection)) { item.ExpiresAt = null; }
            Save();
        }

        private void SeedSnippets()
        {
            var c = Settings.Current.Clipboard;
            if (c.SnippetsSeeded) return;
            c.Snippets.Add(new Snippet { Name = "Today's date", Shortcode = ";date", Text = "{date}" });
            c.Snippets.Add(new Snippet { Name = "Email sign-off", Shortcode = ";sig", Text = "Kind regards," + Environment.NewLine + Environment.UserName });
            c.SnippetsSeeded = true;
            Settings.Save();
        }

        /// <summary>Fills in {date}, {time} and {clipboard} placeholders.</summary>
        public static string FillSnippet(string text)
        {
            string clip = "";
            try { if (System.Windows.Clipboard.ContainsText()) clip = System.Windows.Clipboard.GetText(); } catch { }
            return (text ?? "")
                .Replace("{date}", DateTime.Now.ToString("d MMMM yyyy"))
                .Replace("{time}", DateTime.Now.ToString("HH:mm"))
                .Replace("{clipboard}", clip);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

        private async void ExpandSnippet(Snippet s, int typedLength)
        {
            if (_expander != null) _expander.Busy = true;
            try
            {
                await Task.Delay(40);
                for (int i = 0; i < typedLength; i++)
                {
                    keybd_event(0x08, 0, 0, UIntPtr.Zero);
                    keybd_event(0x08, 0, 2, UIntPtr.Zero);
                }
                await PasteTextHere(FillSnippet(s.Text));
            }
            finally { if (_expander != null) _expander.Busy = false; }
        }

        /// <summary>Pastes text into whatever has focus, then puts the old clipboard back.</summary>
        public async Task PasteTextHere(string text)
        {
            IDataObject previous = null;
            try { previous = System.Windows.Clipboard.GetDataObject(); } catch { }
            string previousText = null;
            try { if (System.Windows.Clipboard.ContainsText()) previousText = System.Windows.Clipboard.GetText(); } catch { }
            try
            {
                _ignoreNext = true;
                System.Windows.Clipboard.SetDataObject(new DataObject(DataFormats.UnicodeText, text), true);
            }
            catch { _ignoreNext = false; return; }
            await Task.Delay(60);
            Native.SendCtrlV();
            await Task.Delay(350);
            if (previousText != null)
            {
                try { _ignoreNext = true; System.Windows.Clipboard.SetDataObject(new DataObject(DataFormats.UnicodeText, previousText), true); }
                catch { _ignoreNext = false; }
            }
        }

        public async void PasteText(string text, IntPtr target)
        {
            await Task.Delay(60);
            Native.ForceForeground(target);
            await Task.Delay(120);
            try
            {
                _ignoreNext = true;
                System.Windows.Clipboard.SetDataObject(new DataObject(DataFormats.UnicodeText, text), true);
            }
            catch { _ignoreNext = false; return; }
            await Task.Delay(40);
            Native.SendCtrlV();
        }

        /// <summary>Adds items and snippets from another PC. Returns how many were new.</summary>
        public int MergeFromSync(List<ClipItem> items, List<Snippet> snippets)
        {
            int added = 0;
            foreach (var r in items ?? new List<ClipItem>())
            {
                var mine = _store.Items.FirstOrDefault(i => i.Id == r.Id) ?? _store.Items.FirstOrDefault(i => i.Kind != "Image" && i.Text == r.Text);
                if (mine == null) { _store.Items.Add(r); added++; }
                else
                {
                    mine.Pinned = mine.Pinned || r.Pinned;
                    if (string.IsNullOrEmpty(mine.Collection)) mine.Collection = r.Collection;
                }
            }
            var c = Settings.Current.Clipboard;
            foreach (var sn in snippets ?? new List<Snippet>())
            {
                var mine = c.Snippets.FirstOrDefault(x => x.Id == sn.Id);
                if (mine == null) { c.Snippets.Add(sn); added++; }
                else if (sn.Changed > mine.Changed) { mine.Name = sn.Name; mine.Shortcode = sn.Shortcode; mine.Text = sn.Text; mine.Changed = sn.Changed; }
            }
            if (added > 0) { Save(); Settings.Save(); }
            return added;
        }

        private void Save()
        {
            Settings.SaveFile("clipboard.json", _store);
            ItemsChanged?.Invoke();
        }

        public void ShowPanel()
        {
            if (!IsRunning) return;
            if (_panel != null) { _panel.Close(); return; }
            var previous = Native.GetForegroundWindow();
            _panel = new ClipboardPanel(this, previous);
            _panel.Closed += (s, e) => _panel = null;
            _panel.Show();
            _panel.Activate();
        }

        /// <summary>Puts an item on the clipboard and pastes it into the window the user was in.</summary>
        public async void Paste(ClipItem item, IntPtr target, bool plain)
        {
            if (!SetClipboard(item, plain)) return;
            await Task.Delay(60);
            Native.ForceForeground(target);
            await Task.Delay(120);
            Native.SendCtrlV();
        }

        public async void PasteStack(IntPtr target)
        {
            var texts = Stack.Where(i => i.Kind != "Image").Select(i => i.Text).ToList();
            if (texts.Count == 0) return;
            _ignoreNext = true;
            try { System.Windows.Clipboard.SetText(string.Join(Environment.NewLine, texts)); } catch { _ignoreNext = false; return; }
            Stack.Clear();
            await Task.Delay(60);
            Native.ForceForeground(target);
            await Task.Delay(120);
            Native.SendCtrlV();
        }

        public bool SetClipboard(ClipItem item, bool plain)
        {
            try
            {
                _ignoreNext = true;
                if (item.Kind == "Image")
                {
                    if (!File.Exists(item.ImageFile)) { _ignoreNext = false; return false; }
                    var img = new BitmapImage();
                    img.BeginInit();
                    img.CacheOption = BitmapCacheOption.OnLoad;
                    img.UriSource = new Uri(item.ImageFile);
                    img.EndInit();
                    System.Windows.Clipboard.SetImage(img);
                }
                else
                {
                    string text = plain ? item.Text.Trim() : item.Text;
                    System.Windows.Clipboard.SetDataObject(new DataObject(DataFormats.UnicodeText, text), true);
                }
                // Move it to the top.
                _store.Items.Remove(item);
                _store.Items.Insert(0, item);
                Save();
                return true;
            }
            catch (Exception ex)
            {
                _ignoreNext = false;
                Log.Write("Set clipboard failed: " + ex.Message);
                return false;
            }
        }

        public override UIElement BuildPage()
        {
            var hotkey = UI.TextInput(S.Hotkey, 160);
            var hkStatus = UI.Text("", 12, Theme.Muted);
            var apply = FlatButton.Secondary("Apply", () =>
            {
                string v = hotkey.Text.Trim();
                if (!Hotkeys.TryParse(v, out _, out _)) { hkStatus.Text = "That doesn't look like a shortcut. Try Ctrl+Alt+V."; return; }
                S.Hotkey = v;
                Settings.Save();
                hkStatus.Text = Hotkeys.Register("clipboard", v, ShowPanel) ? "Saved." : "Another app is already using that shortcut.";
            });
            apply.MinHeight = 36;

            var images = new ToggleSwitch(S.SaveImages, "Keep images");
            images.Toggled += on => { S.SaveImages = on; Settings.Save(); };
            var max = new MiniSlider(50, 1000, S.MaxItems, null, 200) { Step = 50 };
            var maxText = UI.Text(S.MaxItems + " items", 13, Theme.Muted);
            max.ValueChanged += v => { S.MaxItems = (int)v; maxText.Text = (int)v + " items"; Settings.Save(); };
            var clear = FlatButton.Secondary("Clear history", () => ClearUnpinned());
            var open = FlatButton.Primary("Open clipboard", () => ShowPanel());
            open.HorizontalAlignment = HorizontalAlignment.Left;

            var count = UI.Text(_store.Items.Count + " items saved, " + _store.Items.Count(i => i.Pinned) + " pinned.", 14, Theme.Muted);
            Action refresh = () => count.Text = _store.Items.Count + " items saved, " + _store.Items.Count(i => i.Pinned) + " pinned.";
            ItemsChanged += refresh;
            count.Unloaded += (s, e) => ItemsChanged -= refresh;

            return UI.V(16,
                UI.Card(UI.V(12, UI.H(16, open, count),
                    UI.Text("Windows' own Win+V clipboard still works. Arrakis keeps a longer, searchable history alongside it.", 13, Theme.Muted)), 20),
                UI.Card(UI.V(0,
                    UI.SettingRow("Shortcut", null, UI.V(4, UI.H(8, hotkey, apply), hkStatus)),
                    UI.Divider(),
                    UI.SettingRow("Keep images", "Screenshots and copied pictures are saved too.", images),
                    UI.Divider(),
                    UI.SettingRow("History size", "Pinned items are never removed.", UI.H(10, max, maxText)),
                    UI.Divider(),
                    UI.SettingRow("Privacy", "Password managers that mark copies as private are never recorded. Everything stays on this PC.", clear)), 8),
                UI.Label("Snippets"),
                SnippetsCard(),
                UI.Label("Private things"),
                SensitiveCard(),
                UI.Label("Sync between your PCs"),
                SyncCard(),
                UI.Label("Keys in the clipboard panel"),
                UI.Card(UI.V(10,
                    UI.H(10, UI.Kbd("Enter"), UI.Text("Paste", 14)),
                    UI.H(10, UI.Kbd("Shift + Enter"), UI.Text("Paste as plain text", 14)),
                    UI.H(10, UI.Kbd("Ctrl + P"), UI.Text("Pin or unpin", 14)),
                    UI.H(10, UI.Kbd("Ctrl + S"), UI.Text("Add to stack, then Ctrl + Shift + V pastes the whole stack", 14)),
                    UI.H(10, UI.Kbd("Delete"), UI.Text("Remove from history", 14)),
                    UI.H(10, UI.Kbd("Right-click"), UI.Text("Paste as UPPERCASE, one line, without link tracking and more, or add to a collection", 14)),
                    UI.H(10, UI.Kbd("Esc"), UI.Text("Close", 14)))));
        }
    }

    public class ClipboardPanel : Window
    {
        private readonly ClipboardModule _m;
        private readonly IntPtr _target;
        private readonly TextBox _search;
        private readonly StackPanel _list;
        private readonly ScrollViewer _scroll;
        private readonly Border _stackBar;
        private readonly TextBlock _stackText;
        private readonly List<Chip> _filters = new List<Chip>();
        private string _filter = "All";
        private int _selected;
        private List<ClipItem> _visible = new List<ClipItem>();
        private bool _closing;

        public ClipboardPanel(ClipboardModule m, IntPtr target)
        {
            _m = m;
            _target = target;
            Title = "Arrakis Clipboard";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            Width = 640;
            SizeToContent = SizeToContent.Height;
            FontFamily = Theme.Font;
            WindowStartupLocation = WindowStartupLocation.Manual;

            var header = new DockPanel();
            var kbd = UI.Kbd(Settings.Current.Clipboard.Hotkey);
            DockPanel.SetDock(kbd, System.Windows.Controls.Dock.Right);
            header.Children.Add(kbd);
            header.Children.Add(UI.H(10, UI.Text("Clipboard", 18, Theme.Text, FontWeights.Bold), UI.Text(m.Items.Count + " items", 13, Theme.Muted)));

            _search = new TextBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Theme.Text,
                CaretBrush = Theme.Text,
                FontSize = 15,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            System.Windows.Automation.AutomationProperties.SetName(_search, "Search clipboard");
            var placeholder = UI.Text("Search everything you've copied", 15, Theme.Muted);
            placeholder.IsHitTestVisible = false;
            placeholder.VerticalAlignment = VerticalAlignment.Center;
            var searchGrid = new Grid();
            searchGrid.Children.Add(placeholder);
            searchGrid.Children.Add(_search);
            _search.TextChanged += (s, e) => { placeholder.Visibility = _search.Text.Length > 0 ? Visibility.Collapsed : Visibility.Visible; _selected = 0; Render(); };
            var searchBox = new Border
            {
                Height = 46,
                CornerRadius = new CornerRadius(14),
                Background = Theme.Subtle,
                BorderBrush = Theme.Brush(Color.FromArgb(0x80, 0xFF, 0xB5, 0x47)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 0, 14, 0),
                Child = UI.H(10, UI.GridIcon(Icons.Search, 18, Theme.Muted), searchGrid)
            };
            searchGrid.Width = 540;

            var filterRow = new WrapPanel();
            var filterNames = new List<string> { "All", "Pinned", "Snippets", "Text", "Images", "Links", "Colours" };
            filterNames.AddRange(m.Collections.Select(c => "@" + c));
            foreach (var f in filterNames)
            {
                var chip = new Chip(f.StartsWith("@") ? f.Substring(1) : f, f == "All") { Margin = new Thickness(0, 0, 8, 8), Focusable = false, Tag = f };
                chip.Click += (s, e) =>
                {
                    _filter = f;
                    foreach (var c in _filters) c.Selected = (string)c.Tag == f;
                    _selected = 0;
                    Render();
                    _search.Focus();
                };
                _filters.Add(chip);
                filterRow.Children.Add(chip);
            }

            _list = new StackPanel();
            _scroll = new ScrollViewer { Content = _list, MaxHeight = 430, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };

            _stackText = UI.Text("", 14, Theme.Text, FontWeights.Bold);
            var pasteAll = FlatButton.Primary("Paste all", () => { _closing = true; Hide(); _m.PasteStack(_target); Close(); });
            pasteAll.Background = Theme.Teal;
            pasteAll.Foreground = Theme.Brush("#082621");
            pasteAll.Focusable = false;
            var stackDock = new DockPanel();
            DockPanel.SetDock(pasteAll, System.Windows.Controls.Dock.Right);
            stackDock.Children.Add(pasteAll);
            stackDock.Children.Add(UI.V(2, _stackText, UI.Text("Paste them all in order, one per line", 12, Theme.Muted)));
            _stackBar = new Border
            {
                Background = Theme.TealTint,
                BorderBrush = Theme.Brush(Color.FromArgb(0x4D, 0x5E, 0xD3, 0xC3)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(14, 10, 10, 10),
                Child = stackDock
            };

            var hints = new WrapPanel();
            foreach (var (k, t) in new[] { ("Enter", "paste"), ("Shift + Enter", "plain text"), ("Ctrl + P", "pin"), ("Ctrl + S", "stack"), ("Del", "remove") })
                hints.Children.Add(UI.H(6, UI.Kbd(k), UI.Text(t, 12, Theme.Muted)).WithMargin(new Thickness(0, 0, 14, 0)));

            var body = UI.V(14, header, searchBox, filterRow, _scroll, _stackBar, hints);
            Content = new Border
            {
                Background = Theme.Popup,
                BorderBrush = Theme.GlassBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(26),
                Padding = new Thickness(20),
                Margin = new Thickness(20),
                Effect = Theme.Shadow(50, 0.5, 14),
                Child = body
            };

            SourceInitialized += (s, e) => WindowHelper.MakeToolWindow(this, false);
            Loaded += (s, e) =>
            {
                var work = SystemParameters.WorkArea;
                Left = work.Left + (work.Width - ActualWidth) / 2;
                Top = work.Top + 60;
                _search.Focus();
            };
            Deactivated += (s, e) => { if (!_closing) SafeClose(); };
            PreviewKeyDown += OnKey;
            Render();
        }

        private void SafeClose()
        {
            if (_closing) return;
            _closing = true;
            Close();
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            switch (e.Key)
            {
                case Key.Escape: SafeClose(); e.Handled = true; break;
                case Key.Down: Move(1); e.Handled = true; break;
                case Key.Up: Move(-1); e.Handled = true; break;
                case Key.Enter:
                    if (_visible.Count > 0) PasteSelected(shift);
                    e.Handled = true;
                    break;
                case Key.P when ctrl:
                    if (_visible.Count > 0) { _m.TogglePin(_visible[_selected]); Render(); }
                    e.Handled = true;
                    break;
                case Key.S when ctrl:
                    if (_visible.Count > 0)
                    {
                        var item = _visible[_selected];
                        if (_m.Stack.Contains(item)) _m.Stack.Remove(item); else _m.Stack.Add(item);
                        Render();
                    }
                    e.Handled = true;
                    break;
                case Key.V when ctrl && shift:
                    if (_m.Stack.Count > 0) { Hide(); _m.PasteStack(_target); SafeClose(); }
                    e.Handled = true;
                    break;
                case Key.Delete:
                    if (_visible.Count > 0 && _search.Text.Length == 0)
                    {
                        _m.Delete(_visible[_selected]);
                        _selected = Math.Max(0, _selected - 1);
                        Render();
                        e.Handled = true;
                    }
                    break;
            }
        }

        private void Move(int delta)
        {
            if (_visible.Count == 0) return;
            _selected = Math.Max(0, Math.Min(_visible.Count - 1, _selected + delta));
            Render();
            if (_selected < _list.Children.Count && _list.Children[_selected] is FrameworkElement fe) fe.BringIntoView();
        }

        private void PasteSelected(bool plain)
        {
            var item = _visible[_selected];
            _closing = true;
            Hide();
            if (item.Kind == "Snippet") _m.PasteText(ClipboardModule.FillSnippet(item.Text), _target);
            else _m.Paste(item, _target, plain);
            Close();
        }

        private void PasteTransformed(ClipItem item, string transform)
        {
            _closing = true;
            Hide();
            string text = item.Kind == "Snippet" ? ClipboardModule.FillSnippet(item.Text) : item.Text;
            _m.PasteText(ClipTransforms.Apply(transform, text), _target);
            Close();
        }

        private void Render()
        {
            string q = _search.Text.Trim();
            IEnumerable<ClipItem> items = _m.Items;
            switch (_filter)
            {
                case "Pinned": items = items.Where(i => i.Pinned); break;
                case "Text": items = items.Where(i => i.Kind == "Text"); break;
                case "Images": items = items.Where(i => i.Kind == "Image"); break;
                case "Links": items = items.Where(i => i.Kind == "Link"); break;
                case "Colours": items = items.Where(i => i.Kind == "Colour"); break;
                case "Snippets":
                    items = Settings.Current.Clipboard.Snippets.Select(sn => new ClipItem { Id = sn.Id, Kind = "Snippet", Text = sn.Text, Source = sn.Shortcode, Collection = sn.Name, Created = sn.Changed });
                    break;
                default:
                    if (_filter.StartsWith("@")) { string col = _filter.Substring(1); items = items.Where(i => i.Collection == col); }
                    break;
            }
            if (q.Length > 0) items = items.Where(i => !i.IsSensitive && ((i.Text ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || (i.Source ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || (i.Collection ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0));
            // Pinned items first, then newest.
            _visible = items.OrderByDescending(i => i.Pinned).ThenByDescending(i => i.Created).Take(150).ToList();
            if (_selected >= _visible.Count) _selected = Math.Max(0, _visible.Count - 1);

            _list.Children.Clear();
            for (int i = 0; i < _visible.Count; i++) _list.Children.Add(Row(_visible[i], i == _selected, i));
            if (_visible.Count == 0)
                _list.Children.Add(UI.Text(q.Length > 0 ? "Nothing matches \"" + q + "\"." : _filter == "Snippets" ? "No snippets yet. Add some in Arrakis > Clipboard." : "Copy something and it will appear here.", 14, Theme.Muted).WithMargin(new Thickness(8, 16, 8, 16)));

            _stackBar.Visibility = _m.Stack.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            _stackText.Text = "Stack: " + _m.Stack.Count + (_m.Stack.Count == 1 ? " item ready" : " items ready");
        }

        private UIElement Row(ClipItem item, bool selected, int index)
        {
            FrameworkElement thumb;
            if (item.Kind == "Image" && File.Exists(item.ImageFile))
            {
                var bi = new BitmapImage();
                try
                {
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.DecodePixelHeight = 88;
                    bi.UriSource = new Uri(item.ImageFile);
                    bi.EndInit();
                }
                catch { bi = null; }
                thumb = new Border { Width = 72, Height = 44, CornerRadius = new CornerRadius(10), Background = bi != null ? new ImageBrush(bi) { Stretch = Stretch.UniformToFill } : (Brush)Theme.Off };
            }
            else if (item.Kind == "Colour")
            {
                Brush b = Theme.Off;
                try { b = Theme.Brush(item.Text.Trim().StartsWith("#") ? item.Text.Trim() : "#" + item.Text.Trim()); } catch { }
                thumb = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(12), Background = b };
            }
            else if (item.IsSensitive)
            {
                thumb = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(12), Background = Theme.Brush(Color.FromArgb(0x30, 0xE2, 0x58, 0x5F)), Child = UI.GridIcon("M6 11h12v9H6zM8 11V8a4 4 0 0 1 8 0v3", 20, Theme.Red) };
            }
            else if (item.Kind == "Snippet")
            {
                thumb = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(12), Background = Theme.TealTint, Child = UI.GridIcon("M8 6l-5 6 5 6M16 6l5 6-5 6", 20, Theme.Teal) };
            }
            else
            {
                string icon = item.Pinned ? Icons.Pin : item.Kind == "Link" ? Icons.Link : Icons.Lines;
                Brush tint = item.Pinned ? Theme.AmberTint : item.Kind == "Link" ? Theme.TealTint : Theme.Subtle;
                Brush fg = item.Pinned ? Theme.Amber : item.Kind == "Link" ? Theme.Teal : Theme.Text;
                thumb = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(12), Background = tint, Child = UI.GridIcon(icon, 20, fg) };
            }

            string title = item.Kind == "Image" ? "Image " + item.ImageWidth + " × " + item.ImageHeight
                : item.IsSensitive ? Sensitive.Mask(item.SensitiveKind, item.Text)
                : item.Kind == "Snippet" ? (string.IsNullOrEmpty(item.Collection) ? FirstLine(item.Text) : item.Collection)
                : FirstLine(item.Text);
            var titleText = UI.Text(title, 14, Theme.Text, FontWeights.SemiBold);
            titleText.TextWrapping = TextWrapping.NoWrap;
            titleText.TextTrimming = TextTrimming.CharacterEllipsis;
            string meta = (item.Pinned ? "Pinned · " : "") + (string.IsNullOrEmpty(item.Collection) || item.Kind == "Snippet" ? "" : item.Collection + " · ") + KindLabel(item) + " · " + Ago(item.Created) + (string.IsNullOrEmpty(item.Source) ? "" : " · " + item.Source);
            if (item.Kind == "Snippet") meta = "Snippet" + (string.IsNullOrEmpty(item.Source) ? "" : " · type " + item.Source) + " · " + FirstLine(item.Text);
            if (item.IsSensitive) meta = item.SensitiveKind + " · hidden" + (item.ExpiresAt.HasValue ? " · deleted in " + Math.Max(0, (int)(item.ExpiresAt.Value - DateTime.Now).TotalSeconds) + "s" : "") + (string.IsNullOrEmpty(item.Source) ? "" : " · " + item.Source);
            if (_m.Stack.Contains(item)) meta = "In stack (" + (_m.Stack.IndexOf(item) + 1) + ") · " + meta;
            var info = UI.V(2, titleText, UI.Text(meta, 12, Theme.Muted));
            info.VerticalAlignment = VerticalAlignment.Center;

            var dock = new DockPanel();
            if (selected)
            {
                var enter = UI.Kbd("Enter");
                DockPanel.SetDock(enter, System.Windows.Controls.Dock.Right);
                dock.Children.Add(enter);
            }
            DockPanel.SetDock(thumb, System.Windows.Controls.Dock.Left);
            thumb.Margin = new Thickness(0, 0, 14, 0);
            dock.Children.Add(thumb);
            dock.Children.Add(info);

            var row = new Border
            {
                Padding = new Thickness(12),
                CornerRadius = new CornerRadius(16),
                Background = selected ? Theme.Brush(Color.FromArgb(0x24, 0xFF, 0xB5, 0x47)) : Brushes.Transparent,
                BorderBrush = selected ? Theme.Brush(Color.FromArgb(0x59, 0xFF, 0xB5, 0x47)) : Brushes.Transparent,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 4),
                Child = dock,
                Cursor = Cursors.Hand
            };
            row.MouseEnter += (s, e) => { if (_selected != index) { row.Background = Theme.Subtle; } };
            row.MouseLeave += (s, e) => { if (_selected != index) { row.Background = Brushes.Transparent; } };
            row.MouseLeftButtonUp += (s, e) => { _selected = index; PasteSelected((Keyboard.Modifiers & ModifierKeys.Shift) != 0); };
            row.MouseRightButtonUp += (s, e) =>
            {
                _selected = index;
                var menu = new ContextMenu();
                var paste = new MenuItem { Header = "Paste" };
                paste.Click += (a, b) => PasteSelected(false);
                var plain = new MenuItem { Header = "Paste as plain text" };
                plain.Click += (a, b) => PasteSelected(true);
                var copy = new MenuItem { Header = "Copy only" };
                copy.Click += (a, b) => { _m.SetClipboard(item, false); SafeClose(); };
                var pin = new MenuItem { Header = item.Pinned ? "Unpin" : "Pin" };
                pin.Click += (a, b) => { _m.TogglePin(item); Render(); };
                var stack = new MenuItem { Header = _m.Stack.Contains(item) ? "Remove from stack" : "Add to stack" };
                stack.Click += (a, b) => { if (_m.Stack.Contains(item)) _m.Stack.Remove(item); else _m.Stack.Add(item); Render(); };
                var del = new MenuItem { Header = "Remove from history" };
                del.Click += (a, b) => { _m.Delete(item); Render(); };
                menu.Items.Add(paste);
                if (item.Kind != "Image") menu.Items.Add(plain);
                if (item.Kind != "Image")
                {
                    var pasteAs = new MenuItem { Header = "Paste as" };
                    foreach (var t in ClipTransforms.Names)
                    {
                        string tn = t;
                        var mi = new MenuItem { Header = tn };
                        mi.Click += (a, b) => PasteTransformed(item, tn);
                        pasteAs.Items.Add(mi);
                    }
                    menu.Items.Add(pasteAs);
                }
                if (item.Kind == "Snippet") { menu.IsOpen = true; e.Handled = true; return; }
                menu.Items.Add(copy);
                menu.Items.Add(new Separator());
                menu.Items.Add(pin);
                if (item.Kind != "Image") menu.Items.Add(stack);
                var coll = new MenuItem { Header = "Add to collection" };
                foreach (var c in _m.Collections)
                {
                    string cn = c;
                    var mi = new MenuItem { Header = cn, IsChecked = item.Collection == cn };
                    mi.Click += (a, b) => { _m.SetCollection(item, item.Collection == cn ? "" : cn); Render(); };
                    coll.Items.Add(mi);
                }
                var newColl = new MenuItem { Header = "New collection..." };
                newColl.Click += (a, b) =>
                {
                    _closing = true;
                    string name = Ask("New collection", "Name, for example Addresses or Replies");
                    _closing = false;
                    if (!string.IsNullOrWhiteSpace(name)) { _m.SetCollection(item, name.Trim()); Render(); }
                    Activate();
                };
                if (coll.Items.Count > 0) coll.Items.Add(new Separator());
                coll.Items.Add(newColl);
                menu.Items.Add(coll);
                if (item.IsSensitive)
                {
                    var keep = new MenuItem { Header = "Keep it (don't delete)" };
                    keep.Click += (a, b) => { item.ExpiresAt = null; _m.SetCollection(item, item.Collection); Render(); };
                    menu.Items.Add(keep);
                }
                if (item.Kind == "Link")
                {
                    var open = new MenuItem { Header = "Open link" };
                    open.Click += (a, b) => { Shell.Open(item.Text.Trim()); SafeClose(); };
                    menu.Items.Add(open);
                }
                menu.Items.Add(del);
                menu.IsOpen = true;
                e.Handled = true;
            };
            return row;
        }

        public static string Ask(string title, string prompt, string value = "")
        {
            var dlg = new Window
            {
                Title = title,
                Width = 380,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = Theme.Bg,
                ResizeMode = ResizeMode.NoResize,
                FontFamily = Theme.Font,
                Topmost = true
            };
            var box = UI.TextInput(value, 320);
            string result = null;
            var ok = FlatButton.Primary("OK", () => { result = box.Text; dlg.Close(); });
            box.KeyDown += (s, e) => { if (e.Key == Key.Enter) { result = box.Text; dlg.Close(); } };
            dlg.Content = new Border { Padding = new Thickness(22), Child = UI.V(12, UI.Text(prompt, 14, Theme.Text), box, ok) };
            dlg.SourceInitialized += (s, e) => Native.UseDarkTitleBar(new WindowInteropHelper(dlg).Handle);
            dlg.Loaded += (s, e) => box.Focus();
            dlg.ShowDialog();
            return result;
        }

        private static string KindLabel(ClipItem i) => i.Kind switch { "Image" => "Image", "Link" => "Link", "Colour" => "Colour", _ => "Text" };

        private static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string t = text.Trim().Replace("\r", "");
            int nl = t.IndexOf('\n');
            string line = nl >= 0 ? t.Substring(0, nl) + "  ..." : t;
            return line.Length > 140 ? line.Substring(0, 140) + "..." : line;
        }

        public static string Ago(DateTime t)
        {
            var d = DateTime.Now - t;
            if (d.TotalMinutes < 1) return "just now";
            if (d.TotalMinutes < 60) return (int)d.TotalMinutes + " min ago";
            if (d.TotalHours < 24) return (int)d.TotalHours + " h ago";
            if (d.TotalDays < 7) return (int)d.TotalDays + (d.TotalDays < 2 ? " day ago" : " days ago");
            return t.ToString("d MMM");
        }
    }

    public static class ElementExtensions
    {
        public static T WithMargin<T>(this T e, Thickness m) where T : FrameworkElement
        {
            e.Margin = m;
            return e;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Arrakis.Core;

namespace Arrakis.Modules
{
    public sealed class WhImage
    {
        public string Id, Page, Full, Thumb, Resolution, Category, FileType;
        public int Width, Height, Views, Favorites;
        public long FileSize;
        public List<string> Colours = new List<string>();
    }

    /// <summary>wallhaven.cc's public API. Safe-for-work results only.</summary>
    public static class WallhavenApi
    {
        private static readonly HttpClient Http = CreateHttp();
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(6);
        private static readonly Dictionary<string, BitmapSource> Thumbs = new Dictionary<string, BitmapSource>();

        public static string Folder => Path.Combine(Settings.Folder, "wallhaven");

        private static HttpClient CreateHttp()
        {
            var h = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            h.DefaultRequestHeaders.UserAgent.ParseAdd("Arrakis/" + Updates.CurrentText + " (+https://github.com/bhxuk/arrakis)");
            return h;
        }

        // ---------- API key (stored encrypted for this Windows user) ----------
        public static string Key
        {
            get
            {
                try
                {
                    var s = Settings.Current.Wallpaper.WallhavenKey;
                    if (string.IsNullOrEmpty(s)) return null;
                    return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(s), null, DataProtectionScope.CurrentUser));
                }
                catch { return null; }
            }
            set
            {
                Settings.Current.Wallpaper.WallhavenKey = string.IsNullOrWhiteSpace(value) ? "" :
                    Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value.Trim()), null, DataProtectionScope.CurrentUser));
                Settings.Save();
            }
        }

        public static (int w, int h) ScreenSize()
        {
            var s = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            return (s.Width, s.Height);
        }

        /// <summary>sorting: toplist, hot, date_added, random, relevance.</summary>
        public static async Task<(List<WhImage> items, int lastPage, string seed, string error)> Search(string sorting, string topRange, string query, int page, string seed)
        {
            var w = Settings.Current.Wallpaper;
            var q = new List<string>
            {
                "categories=" + (string.IsNullOrEmpty(w.WallhavenCategories) || w.WallhavenCategories == "000" ? "100" : w.WallhavenCategories),
                "purity=100",
                "sorting=" + sorting,
                "order=desc",
                "page=" + page
            };
            if (sorting == "toplist") q.Add("topRange=" + (topRange ?? "1M"));
            if (sorting == "random" && !string.IsNullOrEmpty(seed)) q.Add("seed=" + seed);
            if (!string.IsNullOrWhiteSpace(query)) q.Add("q=" + Uri.EscapeDataString(query.Trim()));
            if (w.WallhavenFit)
            {
                var (sw, sh) = ScreenSize();
                q.Add("atleast=" + sw + "x" + sh);
                q.Add("ratios=" + (sw >= sh ? "landscape" : "portrait"));
            }
            var req = new HttpRequestMessage(HttpMethod.Get, "https://wallhaven.cc/api/v1/search?" + string.Join("&", q));
            var key = Key;
            if (!string.IsNullOrEmpty(key)) req.Headers.Add("X-API-Key", key);
            try
            {
                using var resp = await Http.SendAsync(req);
                if ((int)resp.StatusCode == 429) return (null, 0, null, "Wallhaven is busy. Give it a minute and try again.");
                if ((int)resp.StatusCode == 401) return (null, 0, null, "Wallhaven didn't accept the API key. Check it in the settings below, or clear it.");
                if (!resp.IsSuccessStatusCode) return (null, 0, null, "Wallhaven answered with an error (" + (int)resp.StatusCode + ").");
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                var list = new List<WhImage>();
                foreach (var e in doc.RootElement.GetProperty("data").EnumerateArray())
                {
                    var img = new WhImage
                    {
                        Id = Str(e, "id"),
                        Page = Str(e, "url"),
                        Full = Str(e, "path"),
                        Resolution = Str(e, "resolution"),
                        Category = Str(e, "category"),
                        FileType = Str(e, "file_type"),
                        Width = Int(e, "dimension_x"),
                        Height = Int(e, "dimension_y"),
                        Views = Int(e, "views"),
                        Favorites = Int(e, "favorites"),
                        FileSize = e.TryGetProperty("file_size", out var fs) && fs.ValueKind == JsonValueKind.Number ? fs.GetInt64() : 0
                    };
                    if (e.TryGetProperty("thumbs", out var th)) img.Thumb = Str(th, "large") ?? Str(th, "original");
                    if (e.TryGetProperty("colors", out var cs) && cs.ValueKind == JsonValueKind.Array)
                        foreach (var c in cs.EnumerateArray()) img.Colours.Add(c.GetString());
                    if (!string.IsNullOrEmpty(img.Full)) list.Add(img);
                }
                int last = 1;
                string newSeed = seed;
                if (doc.RootElement.TryGetProperty("meta", out var meta))
                {
                    last = Int(meta, "last_page");
                    if (meta.TryGetProperty("seed", out var sd) && sd.ValueKind == JsonValueKind.String) newSeed = sd.GetString();
                }
                return (list, Math.Max(1, last), newSeed, null);
            }
            catch (Exception ex)
            {
                Log.Write("Wallhaven search: " + ex.Message);
                return (null, 0, null, "Couldn't reach Wallhaven. Check your internet connection.");
            }
        }

        private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        private static int Int(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i) ? i : 0;

        /// <summary>Downloads a picture (once) and decodes it at the given width for showing on screen.</summary>
        public static async Task<BitmapSource> LoadImage(string url, int decodeWidth)
        {
            if (string.IsNullOrEmpty(url)) return null;
            string cacheKey = url + "|" + decodeWidth;
            lock (Thumbs) if (Thumbs.TryGetValue(cacheKey, out var hit)) return hit;
            await Gate.WaitAsync();
            try
            {
                var bytes = await Http.GetByteArrayAsync(url);
                var bmp = await Task.Run(() =>
                {
                    var b = new BitmapImage();
                    b.BeginInit();
                    b.CacheOption = BitmapCacheOption.OnLoad;
                    b.StreamSource = new MemoryStream(bytes);
                    if (decodeWidth > 0) b.DecodePixelWidth = decodeWidth;
                    b.EndInit();
                    b.Freeze();
                    return (BitmapSource)b;
                });
                lock (Thumbs) { if (Thumbs.Count > 400) Thumbs.Clear(); Thumbs[cacheKey] = bmp; }
                return bmp;
            }
            catch { return null; }
            finally { Gate.Release(); }
        }

        /// <summary>Saves the full-size picture into the Arrakis wallhaven folder and returns its path.</summary>
        public static async Task<string> Download(WhImage img, IProgress<double> progress = null)
        {
            Directory.CreateDirectory(Folder);
            string ext = Path.GetExtension(new Uri(img.Full).AbsolutePath);
            if (string.IsNullOrEmpty(ext)) ext = ".jpg";
            string path = Path.Combine(Folder, "wallhaven-" + img.Id + ext);
            if (File.Exists(path) && new FileInfo(path).Length > 0) return path;
            using (var resp = await Http.GetAsync(img.Full, HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                long total = resp.Content.Headers.ContentLength ?? img.FileSize;
                string tmp = path + ".part";
                using (var src = await resp.Content.ReadAsStreamAsync())
                using (var dst = File.Create(tmp))
                {
                    var buf = new byte[81920];
                    long done = 0;
                    int n;
                    while ((n = await src.ReadAsync(buf, 0, buf.Length)) > 0)
                    {
                        await dst.WriteAsync(buf, 0, n);
                        done += n;
                        if (total > 0) progress?.Report((double)done / total);
                    }
                }
                File.Move(tmp, path, true);
            }
            Tidy(path);
            return path;
        }

        /// <summary>Keeps the 30 most recent downloads (and anything currently in use).</summary>
        private static void Tidy(string keep)
        {
            try
            {
                var w = Settings.Current.Wallpaper;
                var inUse = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { keep, w.WallhavenPath, w.WallhavenDailyPath };
                foreach (var v in w.WallhavenScreens.Values) inUse.Add(v);
                foreach (var f in new DirectoryInfo(Folder).GetFiles("wallhaven-*").OrderByDescending(f => f.LastWriteTime).Skip(30))
                    if (!inUse.Contains(f.FullName)) try { f.Delete(); } catch { }
            }
            catch { }
        }

        /// <summary>For "a popular one each day": picks something from this week's toplist that wasn't used last time.</summary>
        public static async Task<string> DailyPick()
        {
            var w = Settings.Current.Wallpaper;
            string today = DateTime.Today.ToString("yyyy-MM-dd");
            if (w.WallhavenDailyDate == today && File.Exists(w.WallhavenDailyPath)) return w.WallhavenDailyPath;
            var rnd = new Random();
            var (items, _, _, error) = await Search("toplist", "1w", null, rnd.Next(1, 4), null);
            if (items == null || items.Count == 0) return File.Exists(w.WallhavenDailyPath) ? w.WallhavenDailyPath : null;
            var choices = items.Where(i => string.IsNullOrEmpty(w.WallhavenDailyPath) || !w.WallhavenDailyPath.Contains(i.Id)).ToList();
            var pool = choices.Count > 0 ? choices : items;
            var pick = pool[rnd.Next(pool.Count)];
            string path = await Download(pick);
            w.WallhavenDailyDate = today;
            w.WallhavenDailyPath = path;
            Settings.Save();
            return path;
        }

        public static string FormatSize(long bytes) => bytes >= 1_000_000 ? (bytes / 1_000_000.0).ToString("0.0") + " MB" : (bytes / 1000) + " KB";
    }

    /// <summary>The scrolling Wallhaven gallery on the Wallpaper page.</summary>
    public static class WallhavenBrowser
    {
        public static UIElement Build(WallpaperModule module)
        {
            var w = Settings.Current.Wallpaper;
            string sorting = "toplist", range = "1M", query = "", seed = null;
            int page = 0, lastPage = 1;
            bool loading = false;
            int generation = 0;

            var status = UI.Text("", 13, Theme.Muted);
            var grid = new WrapPanel { Orientation = Orientation.Horizontal };
            var scroll = new ScrollViewer { Content = grid, Height = 560, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, PanningMode = PanningMode.VerticalOnly };
            var detailHost = new Border { Visibility = Visibility.Collapsed, Background = Theme.Panel, CornerRadius = new CornerRadius(18) };
            var stage = new Grid();
            stage.Children.Add(scroll);
            stage.Children.Add(detailHost);

            // ----- controls -----
            var rangeRow = new WrapPanel();
            var rangeChips = new List<Chip>();
            foreach (var (id, name) in new[] { ("1d", "Today"), ("1w", "This week"), ("1M", "This month"), ("1y", "This year") })
            {
                string v = id;
                var c = new Chip(name, id == range) { Margin = new Thickness(0, 0, 8, 8), Tag = id };
                c.Click += (s, e) => { range = v; foreach (var x in rangeChips) x.Selected = (string)x.Tag == v; Reload(); };
                rangeChips.Add(c);
                rangeRow.Children.Add(c);
            }
            var sortRow = new WrapPanel();
            var sortChips = new List<Chip>();
            foreach (var (id, name) in new[] { ("toplist", "Top"), ("hot", "Hot"), ("date_added", "Latest"), ("random", "Random") })
            {
                string v = id;
                var c = new Chip(name, id == sorting) { Margin = new Thickness(0, 0, 8, 8), Tag = id };
                c.Click += (s, e) =>
                {
                    sorting = v;
                    query = "";
                    foreach (var x in sortChips) x.Selected = (string)x.Tag == v;
                    rangeRow.Visibility = v == "toplist" ? Visibility.Visible : Visibility.Collapsed;
                    Reload();
                };
                sortChips.Add(c);
                sortRow.Children.Add(c);
            }

            var search = UI.TextInput("", 240);
            System.Windows.Automation.AutomationProperties.SetName(search, "Search Wallhaven");
            void DoSearch()
            {
                query = search.Text.Trim();
                if (query.Length == 0) return;
                sorting = "relevance";
                foreach (var x in sortChips) x.Selected = false;
                rangeRow.Visibility = Visibility.Collapsed;
                Reload();
            }
            search.KeyDown += (s, e) => { if (e.Key == Key.Enter) DoSearch(); };
            var searchBtn = FlatButton.Secondary("Search", DoSearch);
            searchBtn.MinHeight = 34;

            var catRow = new WrapPanel();
            string[] catNames = { "General", "Anime", "People" };
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                var c = new Chip(catNames[i], (w.WallhavenCategories ?? "110").Length == 3 && w.WallhavenCategories[i] == '1') { Margin = new Thickness(0, 0, 8, 8) };
                c.Click += (s, e) =>
                {
                    var chars = (w.WallhavenCategories ?? "110").PadRight(3, '0').ToCharArray();
                    chars[idx] = chars[idx] == '1' ? '0' : '1';
                    if (!chars.Contains('1')) chars[0] = '1';
                    w.WallhavenCategories = new string(chars);
                    Settings.Save();
                    c.Selected = chars[idx] == '1';
                    Reload();
                };
                catRow.Children.Add(c);
            }
            var fit = new Chip("Fits my screen", w.WallhavenFit) { Margin = new Thickness(8, 0, 8, 8) };
            fit.Click += (s, e) => { w.WallhavenFit = !w.WallhavenFit; fit.Selected = w.WallhavenFit; Settings.Save(); Reload(); };
            catRow.Children.Add(fit);

            // ----- loading pages -----
            async void LoadMore()
            {
                if (loading || page >= lastPage) return;
                loading = true;
                int gen = generation;
                status.Text = page == 0 ? "Loading wallpapers..." : "Loading more...";
                var (items, last, newSeed, error) = await WallhavenApi.Search(sorting, range, query, page + 1, seed);
                if (gen != generation) { loading = false; return; }
                if (items == null)
                {
                    status.Text = error;
                    loading = false;
                    return;
                }
                page++;
                lastPage = last;
                seed = newSeed;
                foreach (var img in items) grid.Children.Add(Card(img));
                status.Text = grid.Children.Count == 0 ? "Nothing found. Try another search, or turn off \"Fits my screen\"." : grid.Children.Count + " wallpapers" + (page < lastPage ? " · scroll for more" : "");
                loading = false;
                // Fill the view if the first page didn't.
                if (scroll.ExtentHeight <= scroll.ViewportHeight + 10 && page < lastPage) LoadMore();
            }

            void Reload()
            {
                generation++;
                page = 0;
                lastPage = 1;
                seed = null;
                loading = false;
                grid.Children.Clear();
                scroll.ScrollToTop();
                HideDetail();
                LoadMore();
            }

            scroll.ScrollChanged += (s, e) =>
            {
                if (scroll.VerticalOffset + scroll.ViewportHeight >= scroll.ExtentHeight - 400) LoadMore();
            };

            // ----- cards -----
            FrameworkElement Card(WhImage img)
            {
                var pic = new Border { Width = 252, Height = 158, CornerRadius = new CornerRadius(12), Background = Theme.Subtle };
                var res = new Border
                {
                    Background = Theme.Brush(Color.FromArgb(0xB0, 0, 0, 0)),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(8, 3, 8, 3),
                    Margin = new Thickness(8),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Child = UI.Text(img.Resolution + "  ·  " + Compact(img.Favorites) + " favourites", 11, Brushes.White, FontWeights.SemiBold),
                    Opacity = 0
                };
                var g = new Grid();
                g.Children.Add(pic);
                g.Children.Add(res);
                var btn = new FlatButton
                {
                    Content = g,
                    Padding = new Thickness(0),
                    Margin = new Thickness(0, 0, 12, 12),
                    CornerRadius = new CornerRadius(12),
                    Background = Brushes.Transparent
                };
                FlatButton.SetA11yName(btn, "Wallpaper " + img.Resolution);
                btn.MouseEnter += (s, e) => res.Opacity = 1;
                btn.MouseLeave += (s, e) => res.Opacity = 0;
                btn.Click += (s, e) => ShowDetail(img);
                btn.Loaded += async (s, e) =>
                {
                    if (pic.Background is ImageBrush) return;
                    var bmp = await WallhavenApi.LoadImage(img.Thumb, 380);
                    if (bmp != null) pic.Background = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
                };
                return btn;
            }

            // ----- detail -----
            void HideDetail()
            {
                detailHost.Visibility = Visibility.Collapsed;
                detailHost.Child = null;
                scroll.Visibility = Visibility.Visible;
            }

            void ShowDetail(WhImage img)
            {
                var big = new Border { CornerRadius = new CornerRadius(14), Background = Theme.Subtle, Height = 360 };
                _ = Fill();
                async Task Fill()
                {
                    var t = await WallhavenApi.LoadImage(img.Thumb, 0);
                    if (t != null && big.Background is SolidColorBrush) big.Background = new ImageBrush(t) { Stretch = Stretch.Uniform };
                    var full = await WallhavenApi.LoadImage(img.Full, 1600);
                    if (full != null) big.Background = new ImageBrush(full) { Stretch = Stretch.Uniform };
                }

                var info = UI.Text(img.Resolution + "  ·  " + WallhavenApi.FormatSize(img.FileSize) + "  ·  " + Compact(img.Views) + " views  ·  " + Compact(img.Favorites) + " favourites  ·  " + (string.IsNullOrEmpty(img.Category) ? "" : char.ToUpper(img.Category[0]) + img.Category.Substring(1)), 13, Theme.Muted);
                var swatches = UI.H(6);
                foreach (var c in img.Colours.Take(6))
                {
                    try { swatches.Children.Add(new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = Theme.Brush(c), ToolTip = c }); } catch { }
                }
                var msg = UI.Text("", 13, Theme.Muted);

                async Task Set(string screenKey)
                {
                    try
                    {
                        msg.Text = "Downloading...";
                        var path = await WallhavenApi.Download(img, new Progress<double>(p => msg.Text = "Downloading... " + (int)(p * 100) + "%"));
                        module.UseWallhaven(path, screenKey);
                        msg.Text = screenKey == null ? "Set on every screen." : "Set on that screen.";
                    }
                    catch (Exception ex) { msg.Text = "Couldn't download it: " + ex.Message; }
                }

                var setAll = FlatButton.Primary("Set as wallpaper", () => _ = Set(null));
                var buttons = UI.H(8, setAll);
                var screens = System.Windows.Forms.Screen.AllScreens;
                if (screens.Length > 1)
                {
                    var names = screens.Select((sc, i) => "Screen " + (i + 1) + " (" + sc.Bounds.Width + " x " + sc.Bounds.Height + (sc.Primary ? ", main" : "") + ")").ToArray();
                    var pick = new ComboBox { ItemsSource = names, SelectedIndex = 0, MinWidth = 200, Height = 34 };
                    System.Windows.Automation.AutomationProperties.SetName(pick, "Screen");
                    var setOne = FlatButton.Secondary("Set on this screen", () =>
                    {
                        var b = screens[Math.Max(0, pick.SelectedIndex)].Bounds;
                        _ = Set(WallpaperModule.ScreenKey(b.Left, b.Top, b.Right, b.Bottom));
                    });
                    buttons.Children.Add(pick);
                    buttons.Children.Add(setOne);
                }
                var save = FlatButton.Secondary("Save a copy", async () =>
                {
                    try
                    {
                        msg.Text = "Downloading...";
                        var path = await WallhavenApi.Download(img);
                        string pics = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Wallhaven");
                        Directory.CreateDirectory(pics);
                        string dest = Path.Combine(pics, Path.GetFileName(path));
                        File.Copy(path, dest, true);
                        msg.Text = "Saved to Pictures\\Wallhaven.";
                    }
                    catch (Exception ex) { msg.Text = "Couldn't save it: " + ex.Message; }
                });
                var open = FlatButton.Secondary("Open on Wallhaven", () => Shell.Open(img.Page));
                var back = FlatButton.IconButton(Icons.Close, "Back to the gallery", HideDetail, 36);
                var head = new DockPanel();
                DockPanel.SetDock(back, System.Windows.Controls.Dock.Right);
                head.Children.Add(back);
                head.Children.Add(info);
                detailHost.Child = new Border
                {
                    Padding = new Thickness(18),
                    Child = UI.V(12, head, big, swatches, UI.H(8, buttons, save, open), msg)
                };
                detailHost.Visibility = Visibility.Visible;
                scroll.Visibility = Visibility.Hidden;
            }

            // ----- key -----
            var keyBox = new PasswordBox { Width = 240, Height = 34, Password = WallhavenApi.Key ?? "", VerticalContentAlignment = VerticalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(keyBox, "Wallhaven API key");
            var keyStatus = UI.Text(string.IsNullOrEmpty(WallhavenApi.Key) ? "Not needed for browsing. Add it to use your Wallhaven account's settings." : "Saved, encrypted for your Windows account.", 12, Theme.Muted);
            var keySave = FlatButton.Secondary("Save key", () =>
            {
                WallhavenApi.Key = keyBox.Password;
                keyStatus.Text = string.IsNullOrEmpty(keyBox.Password) ? "Key removed." : "Saved, encrypted for your Windows account.";
                Reload();
            });
            keySave.MinHeight = 34;

            var header = new DockPanel();
            var searchRow = UI.H(8, search, searchBtn);
            DockPanel.SetDock(searchRow, System.Windows.Controls.Dock.Right);
            header.Children.Add(searchRow);
            header.Children.Add(sortRow);

            var root = UI.V(10,
                header,
                rangeRow,
                catRow,
                stage,
                status,
                UI.Divider(),
                UI.SettingRow("Wallhaven API key", "Optional. Find yours at wallhaven.cc > Settings > Account. Only safe-for-work wallpapers are shown either way.", UI.V(4, UI.H(8, keyBox, keySave), keyStatus)));
            bool started = false;
            root.Loaded += (s, e) => { if (!started) { started = true; LoadMore(); } };
            return root;
        }

        private static string Compact(int n) => n >= 1_000_000 ? (n / 1_000_000.0).ToString("0.#") + "M" : n >= 1000 ? (n / 1000.0).ToString("0.#") + "k" : n.ToString();
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Arrakis.Core;

namespace Arrakis.Modules
{
    public class Note
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Text { get; set; } = "";
        public string Colour { get; set; } = "Yellow";
        public DateTime Created { get; set; } = DateTime.Now;
        public DateTime Updated { get; set; } = DateTime.Now;
        public string FileName { get; set; } = "";
        /// <summary>True when Arrakis names the file after the first line. False for files you added yourself.</summary>
        public bool AutoName { get; set; } = true;

        public string Title
        {
            get
            {
                var t = (Text ?? "").Trim();
                if (t.Length == 0) return "New note";
                int nl = t.IndexOfAny(new[] { '\r', '\n' });
                if (nl > 0) t = t.Substring(0, nl);
                return t.Length > 60 ? t.Substring(0, 60) + "..." : t;
            }
        }

        public string Preview
        {
            get
            {
                var t = (Text ?? "").Trim().Replace("\r", "");
                int nl = t.IndexOf('\n');
                t = nl > 0 ? t.Substring(nl + 1).Replace('\n', ' ').Trim() : "";
                return t.Length > 90 ? t.Substring(0, 90) + "..." : t;
            }
        }
    }

    public class NoteBook
    {
        public List<Note> Notes { get; set; } = new List<Note>();
    }

    /// <summary>Where notes live (notes.json in the settings folder, so they're included in backups).</summary>
    public static class NoteStore
    {
        private static NoteBook _book;
        public static event Action Changed;

        public static List<Note> All
        {
            get
            {
                _book ??= Settings.LoadFile<NoteBook>("notes.json");
                return _book.Notes;
            }
        }

        public static readonly (string name, string hex)[] Colours =
        {
            ("Yellow", "#FFE7A8"), ("Peach", "#FFD3B5"), ("Pink", "#F9C6D6"), ("Lilac", "#D9CCF7"), ("Blue", "#C3DDFB"), ("Mint", "#C4EBD6")
        };

        public static string Hex(string colour) => Colours.FirstOrDefault(c => c.name == colour).hex ?? "#FFE7A8";

        public static Note Find(string id) => All.FirstOrDefault(n => n.Id == id);

        public static Note Create()
        {
            var n = new Note();
            All.Insert(0, n);
            Save();
            return n;
        }

        public static void Delete(Note n)
        {
            DeleteFile(n);
            All.Remove(n);
            Save();
            // Remove its sticky from the desktop too.
            try
            {
                var w = Settings.Current.Widgets.Items.Where(i => i.Kind == "QuickNote" && i.Source == n.Id).ToList();
                foreach (var item in w) App.Instance.Widgets.Remove(item);
            }
            catch { }
        }

        public static void Save()
        {
            if (Settings.Current.Notes.SaveFiles) WriteFiles();
            Settings.SaveFile("notes.json", _book ?? new NoteBook());
            Changed?.Invoke();
        }

        // ---------------- notes as text files ----------------
        public static string FilesFolder
        {
            get
            {
                var f = Settings.Current.Notes.Folder;
                return string.IsNullOrWhiteSpace(f) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Arrakis Notes") : f;
            }
        }

        private static string SafeName(Note n)
        {
            var t = (n.Text ?? "").Trim();
            int nl = t.IndexOfAny(new[] { '\r', '\n' });
            if (nl > 0) t = t.Substring(0, nl);
            if (t.Length > 60) t = t.Substring(0, 60);
            foreach (var c in Path.GetInvalidFileNameChars()) t = t.Replace(c, ' ');
            t = string.Join(" ", t.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim().TrimEnd('.');
            return t.Length == 0 ? "Note" : t;
        }

        /// <summary>Writes each note to its own .txt file (named after its first line), renaming and tidying as notes change.</summary>
        private static void WriteFiles()
        {
            try
            {
                string dir = FilesFolder;
                Directory.CreateDirectory(dir);
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var n in All.OrderBy(x => x.Created))
                {
                    string old = string.IsNullOrEmpty(n.FileName) ? null : Path.Combine(dir, n.FileName);
                    if (string.IsNullOrWhiteSpace(n.Text))
                    {
                        if (old != null && File.Exists(old)) Recycle(old);
                        n.FileName = "";
                        continue;
                    }
                    string name;
                    if (!n.AutoName && !string.IsNullOrEmpty(n.FileName)) name = n.FileName;
                    else
                    {
                        string baseName = SafeName(n);
                        name = baseName + ".txt";
                        for (int i = 2; used.Contains(name) || All.Any(o => o != n && string.Equals(o.FileName, name, StringComparison.OrdinalIgnoreCase))
                            || (File.Exists(Path.Combine(dir, name)) && !string.Equals(name, n.FileName, StringComparison.OrdinalIgnoreCase)); i++)
                            name = baseName + " (" + i + ").txt";
                    }
                    used.Add(name);
                    string path = Path.Combine(dir, name);
                    if (old != null && !string.Equals(old, path, StringComparison.OrdinalIgnoreCase) && File.Exists(old) && !File.Exists(path))
                        File.Move(old, path);
                    if (!File.Exists(path) || File.ReadAllText(path) != n.Text) File.WriteAllText(path, n.Text);
                    n.FileName = name;
                }
            }
            catch (Exception ex) { Log.Write("Saving note files: " + ex.Message); }
        }

        /// <summary>Picks up .txt and .md files added to the folder, and edits made to note files outside Arrakis.</summary>
        public static void ImportFromFolder()
        {
            if (!Settings.Current.Notes.SaveFiles) return;
            try
            {
                string dir = FilesFolder;
                if (!Directory.Exists(dir)) return;
                bool changed = false;
                foreach (var file in Directory.EnumerateFiles(dir).Where(f => f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".md", StringComparison.OrdinalIgnoreCase)))
                {
                    string name = Path.GetFileName(file);
                    var info = new FileInfo(file);
                    if (info.Length > 2_000_000) continue;
                    var note = All.FirstOrDefault(n => string.Equals(n.FileName, name, StringComparison.OrdinalIgnoreCase));
                    if (note == null)
                    {
                        All.Add(new Note { Text = File.ReadAllText(file), FileName = name, AutoName = false, Created = info.CreationTime, Updated = info.LastWriteTime });
                        changed = true;
                    }
                    else if (info.LastWriteTime > note.Updated.AddSeconds(2))
                    {
                        string text = File.ReadAllText(file);
                        if (text != note.Text) { note.Text = text; note.Updated = info.LastWriteTime; changed = true; }
                    }
                }
                if (changed)
                {
                    Settings.SaveFile("notes.json", _book ?? new NoteBook());
                    Changed?.Invoke();
                }
            }
            catch (Exception ex) { Log.Write("Reading note files: " + ex.Message); }
        }

        public static void DeleteFile(Note n)
        {
            if (string.IsNullOrEmpty(n.FileName)) return;
            string path = Path.Combine(FilesFolder, n.FileName);
            if (File.Exists(path)) Recycle(path);
        }

        /// <summary>Sends a file to the Recycle Bin rather than deleting it outright.</summary>
        private static void Recycle(string path)
        {
            try
            {
                var op = new ShFileOp { wFunc = 3 /* delete */, pFrom = path + "\0\0", fFlags = 0x0040 | 0x0010 | 0x0004 | 0x0400 /* allow undo, no confirm, silent, no error UI */ };
                if (SHFileOperation(ref op) != 0) File.Delete(path);
            }
            catch { try { File.Delete(path); } catch { } }
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private struct ShFileOp
        {
            public IntPtr hwnd;
            public uint wFunc;
            public string pFrom;
            public string pTo;
            public ushort fFlags;
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string lpszProgressTitle;
        }

        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int SHFileOperation(ref ShFileOp op);

        public static IEnumerable<Note> Search(string q)
        {
            if (string.IsNullOrWhiteSpace(q)) return All.OrderByDescending(n => n.Updated);
            var words = q.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return All.Where(n => words.All(w => (n.Text ?? "").IndexOf(w, StringComparison.CurrentCultureIgnoreCase) >= 0)).OrderByDescending(n => n.Updated);
        }
    }

    public class NotesModule : ModuleBase
    {
        public override string Id => "notes";
        public override string Name => "Quick notes";
        public override string Description => "A note pad one shortcut away. Search every note, and pin any of them to your desktop.";
        public override string IconData => Icons.NotePad;
        public override string ShortcutText => S.Hotkey;
        public override bool IsEnabled { get => S.Enabled; set => S.Enabled = value; }

        private NotesSettings S => Settings.Current.Notes;
        private NotesWindow _window;

        protected override void OnStart()
        {
            NoteStore.ImportFromFolder();
            if (!Hotkeys.Register("notes", S.Hotkey, () => Open(null, true)))
                App.Instance.Tray.Notify("Quick notes shortcut unavailable", S.Hotkey + " is used by another app. Choose another in Arrakis > Quick notes.");
        }

        protected override void OnStop()
        {
            Hotkeys.Unregister("notes");
            _window?.Close();
        }

        /// <summary>Opens the notes window. With newNote, starts a blank note ready to type (reusing an empty one if there is one).</summary>
        public void Open(string noteId = null, bool newNote = false)
        {
            if (_window == null)
            {
                NoteStore.ImportFromFolder();
                _window = new NotesWindow();
                _window.Closed += (s, e) => _window = null;
            }
            Note target = noteId != null ? NoteStore.Find(noteId) : null;
            if (newNote) target = NoteStore.All.FirstOrDefault(n => string.IsNullOrWhiteSpace(n.Text)) ?? NoteStore.Create();
            _window.Show();
            if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
            _window.Activate();
            _window.Select(target, newNote);
        }

        public void PinToDesktop(Note n)
        {
            var w = Settings.Current.Widgets;
            if (w.Items.Any(i => i.Kind == "QuickNote" && i.Source == n.Id)) return;
            App.Instance.Widgets.AddNote(n.Id);
        }

        public override UIElement BuildPage()
        {
            var hotkey = UI.TextInput(S.Hotkey, 160);
            var status = UI.Text("", 12, Theme.Muted);
            var apply = FlatButton.Secondary("Use this shortcut", () =>
            {
                string old = S.Hotkey;
                S.Hotkey = hotkey.Text.Trim();
                if (IsRunning)
                {
                    Hotkeys.Unregister("notes");
                    if (Hotkeys.Register("notes", S.Hotkey, () => Open(null, true))) { status.Text = "Saved."; Settings.Save(); }
                    else { status.Text = S.Hotkey + " is taken. Try another."; S.Hotkey = old; Hotkeys.Register("notes", old, () => Open(null, true)); }
                }
                else { Settings.Save(); status.Text = "Saved."; }
            });

            var folderText = UI.Text(NoteStore.FilesFolder, 12, Theme.Muted);
            folderText.TextWrapping = TextWrapping.Wrap;
            var files = new ToggleSwitch(S.SaveFiles, "Save notes as text files");
            files.Toggled += on => { S.SaveFiles = on; Settings.Save(); if (on) { NoteStore.ImportFromFolder(); NoteStore.Save(); } };
            var change = FlatButton.Secondary("Change folder...", () =>
            {
                var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = "Choose where your notes are saved", SelectedPath = NoteStore.FilesFolder, UseDescriptionForTitle = true };
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                S.Folder = dlg.SelectedPath;
                Settings.Save();
                foreach (var n in NoteStore.All) if (n.AutoName) n.FileName = "";
                NoteStore.ImportFromFolder();
                NoteStore.Save();
                folderText.Text = NoteStore.FilesFolder;
            });
            change.MinHeight = 34;
            var openFolder = FlatButton.IconButton(Icons.Folder, "Open the notes folder", () =>
            {
                try { Directory.CreateDirectory(NoteStore.FilesFolder); Shell.Open(NoteStore.FilesFolder); } catch { }
            }, 34);
            var reset = FlatButton.Secondary("Use the default", () =>
            {
                S.Folder = "";
                Settings.Save();
                foreach (var n in NoteStore.All) if (n.AutoName) n.FileName = "";
                NoteStore.ImportFromFolder();
                NoteStore.Save();
                folderText.Text = NoteStore.FilesFolder;
            });
            reset.MinHeight = 34;

            var recent = UI.V(8);
            void Fill()
            {
                recent.Children.Clear();
                foreach (var n in NoteStore.Search(null).Where(n => !string.IsNullOrWhiteSpace(n.Text)).Take(6))
                {
                    var note = n;
                    var sw = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = Theme.Brush(NoteStore.Hex(note.Colour)), Margin = new Thickness(0, 4, 12, 0), VerticalAlignment = VerticalAlignment.Top };
                    var row = new FlatButton
                    {
                        Content = UI.H(0, sw, UI.V(2, UI.Text(note.Title, 14, Theme.Text, FontWeights.SemiBold), UI.Text(note.Updated.ToString("d MMM, HH:mm"), 12, Theme.Muted))),
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        Padding = new Thickness(12, 8, 12, 8),
                        Background = Brushes.Transparent
                    };
                    row.Click += (s, e) => Open(note.Id);
                    recent.Children.Add(row);
                }
                if (recent.Children.Count == 0) recent.Children.Add(UI.Text("No notes yet. Press " + S.Hotkey + " and start typing.", 13, Theme.Muted));
            }
            Fill();

            return UI.V(16,
                UI.Card(UI.V(10,
                    UI.Text("Write it down before it's gone", 18, Theme.Text, FontWeights.SemiBold, true),
                    UI.Text("Press " + S.Hotkey + " in any app and a fresh note is ready to type into. Notes save as you type, can be searched here or from the command bar, and can be pinned to your desktop as a sticky note.", 14, Theme.Muted),
                    UI.H(10, FlatButton.Primary("New note", () => Open(null, true)), FlatButton.Secondary("All notes", () => Open()))), 22),
                UI.Label("Recent notes"),
                UI.Card(recent, 10),
                UI.Label("Settings"),
                UI.Card(UI.V(4,
                    UI.SettingRow("Shortcut", "Opens a new note from anywhere.", UI.V(4, UI.H(8, hotkey, apply), status)),
                    UI.SettingRow("Save notes as text files", "Each note is also saved as a .txt file, named after its first line, and kept up to date as you type. Text files you add to the folder show up as notes too.", files),
                    UI.SettingRow("Notes folder", null, UI.V(6, UI.H(8, change, openFolder, reset), folderText)),
                    UI.SettingRow("Backups", "Notes are included when you export a backup in Arrakis > Settings.", UI.Text(NoteStore.All.Count + (NoteStore.All.Count == 1 ? " note" : " notes"), 13, Theme.Muted))), 8));
        }
    }

    /// <summary>The notes window: a searchable list on the left, the note on the right.</summary>
    public class NotesWindow : Window
    {
        private readonly TextBox _search;
        private readonly StackPanel _list = new StackPanel();
        private readonly TextBox _editor;
        private readonly TextBlock _meta;
        private readonly StackPanel _colours = new StackPanel { Orientation = Orientation.Horizontal };
        private readonly FlatButton _pin;
        private readonly DispatcherTimer _save = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        private Note _current;
        private bool _loading;

        public NotesWindow()
        {
            Title = "Quick notes";
            Width = 900;
            Height = 600;
            MinWidth = 620;
            MinHeight = 380;
            Background = Theme.Bg;
            FontFamily = Theme.Font;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            try { Icon = IconCache.AppIcon(); } catch { }
            SourceInitialized += (s, e) => Native.UseDarkTitleBar(new WindowInteropHelper(this).Handle);

            // Left: search and list
            _search = new TextBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Theme.Text, CaretBrush = Theme.Text, FontSize = 14, VerticalContentAlignment = VerticalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(_search, "Search notes");
            var ph = UI.Text("Search notes", 14, Theme.Muted);
            ph.IsHitTestVisible = false;
            ph.VerticalAlignment = VerticalAlignment.Center;
            var sg = new Grid();
            sg.Children.Add(ph);
            sg.Children.Add(_search);
            _search.TextChanged += (s, e) => { ph.Visibility = _search.Text.Length > 0 ? Visibility.Collapsed : Visibility.Visible; FillList(); };
            var searchBox = new Border { Height = 40, CornerRadius = new CornerRadius(12), Background = Theme.Subtle, Padding = new Thickness(12, 0, 12, 0), Child = sg };
            var newBtn = FlatButton.Primary("New note", () => Select(NoteStore.Create(), true));

            var left = new DockPanel { Width = 290, Margin = new Thickness(16, 16, 0, 16) };
            var top = UI.V(10, newBtn, searchBox);
            top.Margin = new Thickness(0, 0, 0, 10);
            DockPanel.SetDock(top, System.Windows.Controls.Dock.Top);
            left.Children.Add(top);
            left.Children.Add(new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

            // Right: editor
            _editor = new TextBox
            {
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = Brushes.Black,
                CaretBrush = Brushes.Black,
                FontSize = 16,
                Padding = new Thickness(6)
            };
            System.Windows.Automation.AutomationProperties.SetName(_editor, "Note");
            _editor.TextChanged += (s, e) => { if (_loading || _current == null) return; _save.Stop(); _save.Start(); };
            _save.Tick += (s, e) => { _save.Stop(); SaveCurrent(); };

            _meta = UI.Text("", 12, Theme.Brush("#6B5B3A"));
            _pin = FlatButton.Secondary("Pin to desktop", () =>
            {
                if (_current == null) return;
                SaveCurrent();
                App.Instance.Notes.PinToDesktop(_current);
                _pin.Content = "On your desktop";
            });
            var copy = FlatButton.Secondary("Copy", () => { if (_current != null) try { System.Windows.Clipboard.SetText(_current.Text ?? ""); } catch { } });
            var del = FlatButton.Secondary("Delete", () =>
            {
                if (_current == null) return;
                if (!string.IsNullOrWhiteSpace(_current.Text) && MessageBox.Show(this, "Delete this note?", "Quick notes", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
                NoteStore.Delete(_current);
                _current = null;
                Select(NoteStore.Search(null).FirstOrDefault(), false);
            });

            var tools = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            var folderBtn = FlatButton.IconButton(Icons.Folder, "Open the notes folder", () =>
            {
                try { Directory.CreateDirectory(NoteStore.FilesFolder); Shell.Open(NoteStore.FilesFolder); } catch { }
            }, 38);
            var right = UI.H(8, folderBtn, copy, _pin, del);
            DockPanel.SetDock(right, System.Windows.Controls.Dock.Right);
            tools.Children.Add(right);
            tools.Children.Add(_colours);

            var paper = new Border { CornerRadius = new CornerRadius(18), Padding = new Thickness(22, 18, 22, 14), Effect = Theme.Shadow(24, 0.25, 6) };
            var paperBody = new DockPanel();
            DockPanel.SetDock(_meta, System.Windows.Controls.Dock.Bottom);
            paperBody.Children.Add(_meta);
            paperBody.Children.Add(_editor);
            paper.Child = paperBody;

            var rightPane = new DockPanel { Margin = new Thickness(16) };
            DockPanel.SetDock(tools, System.Windows.Controls.Dock.Bottom);
            rightPane.Children.Add(tools);
            rightPane.Children.Add(paper);
            _paper = paper;

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.Children.Add(left);
            Grid.SetColumn(rightPane, 1);
            grid.Children.Add(rightPane);
            Content = grid;

            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) { Close(); e.Handled = true; }
                else if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control) { Select(NoteStore.Create(), true); e.Handled = true; }
                else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { _search.Focus(); _search.SelectAll(); e.Handled = true; }
            };
            Closing += (s, e) =>
            {
                SaveCurrent();
                // Tidy away empty notes.
                foreach (var n in NoteStore.All.Where(n => string.IsNullOrWhiteSpace(n.Text)).ToList())
                    if (!Settings.Current.Widgets.Items.Any(i => i.Kind == "QuickNote" && i.Source == n.Id)) NoteStore.All.Remove(n);
                NoteStore.Save();
            };

            FillList();
        }

        private readonly Border _paper;

        public void Select(Note n, bool focusEditor)
        {
            SaveCurrent();
            _current = n;
            _loading = true;
            _editor.Text = n?.Text ?? "";
            _editor.IsEnabled = n != null;
            _loading = false;
            Paint();
            FillList();
            if (n != null && focusEditor)
            {
                Dispatcher.BeginInvoke(new Action(() => { _editor.Focus(); _editor.CaretIndex = _editor.Text.Length; }), DispatcherPriority.Input);
            }
        }

        private void Paint()
        {
            string hex = NoteStore.Hex(_current?.Colour);
            _paper.Background = _current == null ? Theme.Subtle : Theme.Brush(hex);
            _meta.Text = _current == null ? "" : "Edited " + _current.Updated.ToString("d MMM yyyy, HH:mm") + "  ·  Ctrl+N new  ·  Ctrl+F search  ·  Esc close";
            _pin.Content = _current != null && Settings.Current.Widgets.Items.Any(i => i.Kind == "QuickNote" && i.Source == _current.Id) ? "On your desktop" : "Pin to desktop";
            _colours.Children.Clear();
            foreach (var (name, h) in NoteStore.Colours)
            {
                string c = name;
                bool sel = _current?.Colour == c;
                var dot = new FlatButton
                {
                    Width = 26, Height = 26, MinHeight = 26,
                    Padding = new Thickness(0),
                    CornerRadius = new CornerRadius(13),
                    Background = Theme.Brush(h),
                    BorderBrush = sel ? Theme.Text : Brushes.Transparent,
                    BorderThickness = new Thickness(2),
                    Margin = new Thickness(0, 0, 8, 0)
                };
                FlatButton.SetA11yName(dot, c + " note");
                dot.Click += (s, e) => { if (_current == null) return; _current.Colour = c; _current.Updated = DateTime.Now; NoteStore.Save(); Paint(); FillList(); };
                _colours.Children.Add(dot);
            }
        }

        private void SaveCurrent()
        {
            if (_current == null) return;
            if (_current.Text == _editor.Text) return;
            _current.Text = _editor.Text;
            _current.Updated = DateTime.Now;
            NoteStore.Save();
            FillList();
            _meta.Text = "Saved " + _current.Updated.ToString("HH:mm") + "  ·  Ctrl+N new  ·  Ctrl+F search  ·  Esc close";
        }

        private void FillList()
        {
            _list.Children.Clear();
            var notes = NoteStore.Search(_search?.Text).ToList();
            foreach (var n in notes)
            {
                var note = n;
                bool sel = note == _current;
                var sw = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = Theme.Brush(NoteStore.Hex(note.Colour)), Margin = new Thickness(0, 5, 10, 0), VerticalAlignment = VerticalAlignment.Top };
                var text = UI.V(2, UI.Text(note.Title, 14, Theme.Text, FontWeights.SemiBold), UI.Text(string.IsNullOrEmpty(note.Preview) ? note.Updated.ToString("d MMM, HH:mm") : note.Preview, 12, Theme.Muted));
                foreach (var tb in text.Children.OfType<TextBlock>()) { tb.TextTrimming = TextTrimming.CharacterEllipsis; tb.MaxWidth = 230; }
                var b = new FlatButton
                {
                    Content = UI.H(0, sw, text),
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(12, 10, 12, 10),
                    Margin = new Thickness(0, 0, 0, 4),
                    CornerRadius = new CornerRadius(12),
                    Background = sel ? Theme.AmberTint : Brushes.Transparent
                };
                b.Click += (s, e) => Select(note, true);
                _list.Children.Add(b);
            }
            if (notes.Count == 0) _list.Children.Add(UI.Text(string.IsNullOrWhiteSpace(_search?.Text) ? "No notes yet." : "Nothing matches.", 13, Theme.Muted).WithMargin(new Thickness(12)));
        }
    }
}

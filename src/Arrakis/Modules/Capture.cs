using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Arrakis.Core;
using Path = System.Windows.Shapes.Path;

namespace Arrakis.Modules
{
    public class CaptureModule : ModuleBase
    {
        public override string Id => "capture";
        public override string Name => "Capture";
        public override string Description => "Screenshots with arrows, blur, numbered steps and pretty frames.";
        public override string IconData => Icons.Capture;
        public override string ShortcutText => Settings.Current.Capture.Hotkey;
        public override bool IsEnabled { get => Settings.Current.Capture.Enabled; set => Settings.Current.Capture.Enabled = value; }

        private CaptureSettings S => Settings.Current.Capture;
        private bool _busy;

        protected override void OnStart()
        {
            RegisterKeys();
        }

        private void RegisterKeys()
        {
            if (!Hotkeys.Register("capture", S.Hotkey, StartCapture))
                App.Instance.Tray.Notify("Screenshot shortcut unavailable", S.Hotkey + " is used by another app. Choose a different one in Arrakis > Capture.");
            if (S.UsePrintScreen) Hotkeys.Register("capture-prtsc", "PrintScreen", StartCapture);
            else Hotkeys.Unregister("capture-prtsc");
        }

        protected override void OnStop()
        {
            Hotkeys.Unregister("capture");
            Hotkeys.Unregister("capture-prtsc");
        }

        public void StartCapture()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                Native.GetCursorPos(out var pt);
                var mon = Native.GetMonitorAt(pt).rcMonitor;
                var shot = Grab(mon);
                var overlay = new CaptureOverlay(shot, mon);
                overlay.Closed += (s, e) => _busy = false;
                overlay.Captured += bmp =>
                {
                    var editor = new CaptureEditor(bmp);
                    editor.Show();
                    editor.Activate();
                };
                overlay.Show();
                overlay.Activate();
            }
            catch (Exception ex)
            {
                _busy = false;
                Log.Write("Capture failed: " + ex);
            }
        }

        private static BitmapSource Grab(Native.RECT r)
        {
            using var bmp = new System.Drawing.Bitmap(r.Width, r.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
                g.CopyFromScreen(r.Left, r.Top, 0, 0, new System.Drawing.Size(r.Width, r.Height), System.Drawing.CopyPixelOperation.SourceCopy);
            var hbm = bmp.GetHbitmap();
            try
            {
                var src = Imaging.CreateBitmapSourceFromHBitmap(hbm, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            finally { DeleteObject(hbm); }
        }

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        public override UIElement BuildPage()
        {
            var hotkey = UI.TextInput(S.Hotkey, 160);
            var hkStatus = UI.Text("", 12, Theme.Muted);
            var apply = FlatButton.Secondary("Apply", () =>
            {
                string v = hotkey.Text.Trim();
                if (!Hotkeys.TryParse(v, out _, out _)) { hkStatus.Text = "That doesn't look like a shortcut. Try Ctrl+Alt+S."; return; }
                S.Hotkey = v;
                Settings.Save();
                hkStatus.Text = Hotkeys.Register("capture", v, StartCapture) ? "Saved." : "Another app is already using that shortcut.";
            });
            apply.MinHeight = 36;

            var prt = new ToggleSwitch(S.UsePrintScreen, "Use Print Screen");
            prt.Toggled += on => { S.UsePrintScreen = on; Settings.Save(); if (IsRunning) RegisterKeys(); };
            var blur = new ToggleSwitch(S.AutoBlurEmails, "Auto-blur emails");
            blur.Toggled += on => { S.AutoBlurEmails = on; Settings.Save(); };
            var folder = UI.Text(SaveFolder(), 12, Theme.Muted);
            var choose = FlatButton.Secondary("Change...", () =>
            {
                var dlg = new System.Windows.Forms.FolderBrowserDialog { SelectedPath = SaveFolder(), Description = "Where should screenshots be saved?" };
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    S.SaveFolder = dlg.SelectedPath;
                    Settings.Save();
                    folder.Text = S.SaveFolder;
                }
            });
            choose.MinHeight = 36;
            var take = FlatButton.Primary("Take a screenshot", () =>
            {
                if (!IsRunning) App.Instance.SetModuleEnabled(this, true);
                App.Instance.Hub?.Hide();
                var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
                t.Tick += (s, e) => { t.Stop(); StartCapture(); };
                t.Start();
            });
            take.HorizontalAlignment = HorizontalAlignment.Left;

            return UI.V(16,
                UI.Card(UI.V(12, take, UI.Text("Drag over what you want. Then add arrows, boxes, text, highlights, blur and numbered steps, pick a frame, and copy, save or pin it.", 13, Theme.Muted))),
                UI.Card(UI.V(0,
                    UI.SettingRow("Shortcut", null, UI.V(4, UI.H(8, hotkey, apply), hkStatus)),
                    UI.Divider(),
                    UI.SettingRow("Use the Print Screen key", "Only works if Windows isn't using Print Screen for Snipping Tool (Settings > Accessibility > Keyboard).", prt),
                    UI.Divider(),
                    UI.SettingRow("Auto-blur email addresses", "Reads the screenshot on your PC (nothing is uploaded) and blurs any emails it finds.", blur),
                    UI.Divider(),
                    UI.SettingRow("Save to", null, UI.V(4, choose, folder))), 8));
        }

        public static string SaveFolder()
        {
            var s = Settings.Current.Capture.SaveFolder;
            if (!string.IsNullOrEmpty(s)) return s;
            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Arrakis");
        }
    }

    /// <summary>Full-screen, frozen copy of the screen. Drag to pick an area.</summary>
    public class CaptureOverlay : Window
    {
        private readonly BitmapSource _shot;
        private readonly Native.RECT _mon;
        private readonly Grid _root;
        private readonly Path _dim;
        private readonly Rectangle _sel;
        private readonly Border _size;
        private Point _start;
        private bool _dragging;
        public event Action<BitmapSource> Captured;

        public CaptureOverlay(BitmapSource shot, Native.RECT mon)
        {
            _shot = shot;
            _mon = mon;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            ShowInTaskbar = false;
            Cursor = Cursors.Cross;
            Background = Brushes.Black;
            FontFamily = Theme.Font;

            _root = new Grid();
            _root.Children.Add(new Image { Source = shot, Stretch = Stretch.Fill });
            _dim = new Path { Fill = Theme.Brush(Color.FromArgb(0x80, 0, 0, 0)) };
            _root.Children.Add(_dim);
            _sel = new Rectangle { Stroke = Theme.Amber, StrokeThickness = 2, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed };
            _root.Children.Add(_sel);
            _size = new Border
            {
                Background = Theme.Glass,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 4, 8, 4),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Visibility = Visibility.Collapsed,
                Child = UI.Text("", 12, Theme.Text, FontWeights.SemiBold)
            };
            _root.Children.Add(_size);
            var hint = new Border
            {
                Background = Theme.Glass,
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(16, 10, 16, 10),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 24, 0, 0),
                IsHitTestVisible = false,
                Child = UI.Text("Drag to capture  ·  Enter for the whole screen  ·  Esc to cancel", 14, Theme.Text, FontWeights.SemiBold)
            };
            _root.Children.Add(hint);
            Content = _root;

            SourceInitialized += (s, e) =>
            {
                var h = new WindowInteropHelper(this).Handle;
                Native.SetWindowPos(h, Native.HWND_TOPMOST, _mon.Left, _mon.Top, _mon.Width, _mon.Height, Native.SWP_SHOWWINDOW);
            };
            Loaded += (s, e) => { UpdateDim(Rect.Empty); Activate(); Focus(); };
            MouseLeftButtonDown += (s, e) =>
            {
                _start = e.GetPosition(_root);
                _dragging = true;
                CaptureMouse();
                hint.Visibility = Visibility.Collapsed;
            };
            MouseMove += (s, e) =>
            {
                if (!_dragging) return;
                var r = new Rect(_start, e.GetPosition(_root));
                _sel.Margin = new Thickness(r.X, r.Y, 0, 0);
                _sel.Width = r.Width;
                _sel.Height = r.Height;
                _sel.Visibility = Visibility.Visible;
                double sx = _shot.PixelWidth / Math.Max(1, _root.ActualWidth);
                ((TextBlock)_size.Child).Text = (int)(r.Width * sx) + " × " + (int)(r.Height * sx);
                _size.Margin = new Thickness(r.X, Math.Max(0, r.Y - 30), 0, 0);
                _size.Visibility = Visibility.Visible;
                UpdateDim(r);
            };
            MouseLeftButtonUp += (s, e) =>
            {
                if (!_dragging) return;
                _dragging = false;
                ReleaseMouseCapture();
                var r = new Rect(_start, e.GetPosition(_root));
                if (r.Width < 4 || r.Height < 4) { Finish(new Rect(0, 0, _root.ActualWidth, _root.ActualHeight)); return; }
                Finish(r);
            };
            MouseRightButtonUp += (s, e) => Close();
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) Close();
                else if (e.Key == Key.Enter) Finish(new Rect(0, 0, _root.ActualWidth, _root.ActualHeight));
            };
        }

        private void UpdateDim(Rect hole)
        {
            var full = new RectangleGeometry(new Rect(0, 0, Math.Max(1, _root.ActualWidth), Math.Max(1, _root.ActualHeight)));
            _dim.Data = hole.IsEmpty ? (Geometry)full : new CombinedGeometry(GeometryCombineMode.Exclude, full, new RectangleGeometry(hole));
        }

        private void Finish(Rect r)
        {
            double sx = _shot.PixelWidth / Math.Max(1, _root.ActualWidth);
            double sy = _shot.PixelHeight / Math.Max(1, _root.ActualHeight);
            int x = (int)Math.Round(r.X * sx), y = (int)Math.Round(r.Y * sy);
            int w = (int)Math.Round(r.Width * sx), h = (int)Math.Round(r.Height * sy);
            x = Math.Max(0, Math.Min(_shot.PixelWidth - 1, x));
            y = Math.Max(0, Math.Min(_shot.PixelHeight - 1, y));
            w = Math.Max(1, Math.Min(_shot.PixelWidth - x, w));
            h = Math.Max(1, Math.Min(_shot.PixelHeight - y, h));
            var crop = new CroppedBitmap(_shot, new Int32Rect(x, y, w, h));
            var copy = new WriteableBitmap(crop);
            copy.Freeze();
            Close();
            Captured?.Invoke(copy);
        }
    }

    /// <summary>The screenshot editor.</summary>
    public class CaptureEditor : Window
    {
        private readonly BitmapSource _source;
        private readonly int _w, _h;
        private readonly double _sw;
        private readonly Border _frame;
        private readonly Border _shadowCard;
        private readonly Grid _content;
        private readonly Canvas _canvas;
        private readonly List<FrameworkElement> _history = new List<FrameworkElement>();
        private string _tool = "Arrow";
        private Color _color = Theme.RedColor;
        private readonly Dictionary<string, FlatButton> _toolButtons = new Dictionary<string, FlatButton>();
        private readonly TextBlock _status;
        private Point _down;
        private FrameworkElement _preview;
        private FrameworkElement _moving;
        private Point _moveStart;
        private Vector _moveOrigin;
        private int _step = 1;

        private CaptureSettings S => Settings.Current.Capture;

        public static readonly (string name, Brush brush)[] Frames =
        {
            ("None", null),
            ("Sunset", Gradient("#FFB547", "#E2585F")),
            ("Ocean", Gradient("#5ED3C3", "#3D7BF5")),
            ("Meadow", Gradient("#8FD16A", "#1FA392")),
            ("Light", Theme.Brush("#F3F1EC")),
            ("Dark", Theme.Brush("#2A2F3A"))
        };

        private static Brush Gradient(string a, string b)
        {
            var g = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(a), (Color)ColorConverter.ConvertFromString(b), 45);
            g.Freeze();
            return g;
        }

        public CaptureEditor(BitmapSource source)
        {
            _source = source;
            _w = source.PixelWidth;
            _h = source.PixelHeight;
            _sw = Math.Max(3, Math.Min(_w, _h) / 160.0);
            Title = "Arrakis Capture";
            Background = Theme.Bg;
            FontFamily = Theme.Font;
            Width = Math.Min(1400, SystemParameters.WorkArea.Width - 80);
            Height = Math.Min(900, SystemParameters.WorkArea.Height - 80);
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            try { Icon = IconCache.AppIcon(); } catch { }

            // ----- the composition that gets exported -----
            var img = new Image { Source = source, Width = _w, Height = _h, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            _canvas = new Canvas { Width = _w, Height = _h, Background = Brushes.Transparent, ClipToBounds = true };
            _content = new Grid { Width = _w, Height = _h };
            _content.Children.Add(img);
            _content.Children.Add(_canvas);
            _shadowCard = new Border { Background = Brushes.White };
            var holder = new Grid();
            holder.Children.Add(_shadowCard);
            holder.Children.Add(_content);
            _frame = new Border { Child = holder, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

            var viewbox = new Viewbox { Child = _frame, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Margin = new Thickness(32) };
            var stage = new Border
            {
                Background = Theme.Brush("#0B0D11"),
                Child = viewbox
            };

            // ----- toolbar -----
            var tools = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var (name, icon) in new[]
            {
                ("Select", Icons.Cursor), ("Arrow", Icons.Arrow), ("Box", Icons.Box), ("Text", Icons.TextTool),
                ("Highlight", Icons.Highlight), ("Blur", Icons.Blur), ("Steps", Icons.Steps)
            })
            {
                var b = new FlatButton
                {
                    Width = 68,
                    Height = 54,
                    Padding = new Thickness(0),
                    Background = Brushes.Transparent,
                    Content = UI.V(3, UI.GridIcon(icon, 18, Theme.Text).Centered(), UI.Text(name, 11, Theme.Text, FontWeights.SemiBold).Centered()),
                    Margin = new Thickness(0, 0, 4, 0)
                };
                string n = name;
                b.Click += (s, e) => SetTool(n);
                FlatButton.SetA11yName(b, name + " tool");
                _toolButtons[name] = b;
                tools.Children.Add(b);
            }
            var colours = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var swatches = new List<FlatButton>();
            foreach (var c in new[] { Theme.RedColor, Theme.AmberColor, Theme.TealColor, Colors.White, Color.FromRgb(0x11, 0x13, 0x18) })
            {
                var sw = new FlatButton { Width = 28, Height = 28, MinHeight = 28, Padding = new Thickness(0), CornerRadius = new CornerRadius(14), Background = Theme.Brush(c), BorderThickness = new Thickness(2), Margin = new Thickness(0, 0, 8, 0) };
                var col = c;
                sw.Click += (s, e) =>
                {
                    _color = col;
                    foreach (var x in swatches) x.BorderBrush = Brushes.Transparent;
                    sw.BorderBrush = Brushes.White;
                };
                FlatButton.SetA11yName(sw, "Annotation colour");
                swatches.Add(sw);
                colours.Children.Add(sw);
            }
            swatches[0].BorderBrush = Brushes.White;
            var undo = FlatButton.IconButton(Icons.Undo, "Undo (Ctrl+Z)", Undo);
            var toolbar = new DockPanel { Height = 70, Background = Theme.Side, LastChildFill = false };
            DockPanel.SetDock(undo, System.Windows.Controls.Dock.Right);
            undo.Margin = new Thickness(0, 0, 16, 0);
            var leftTools = UI.H(0, tools, colours);
            leftTools.Margin = new Thickness(16, 0, 0, 0);
            leftTools.VerticalAlignment = VerticalAlignment.Center;
            toolbar.Children.Add(undo);
            toolbar.Children.Add(leftTools);

            // ----- side panel -----
            var frameRow = new WrapPanel();
            var frameButtons = new List<FlatButton>();
            foreach (var (name, brush) in Frames)
            {
                var fb = new FlatButton
                {
                    Width = 36,
                    Height = 32,
                    MinHeight = 32,
                    Padding = new Thickness(0),
                    CornerRadius = new CornerRadius(9),
                    Background = brush ?? Brushes.Transparent,
                    BorderThickness = new Thickness(2),
                    BorderBrush = name == S.Frame ? Brushes.White : (name == "None" ? Theme.Muted : Brushes.Transparent),
                    Margin = new Thickness(0, 0, 8, 8),
                    ToolTip = name
                };
                FlatButton.SetA11yName(fb, name + " frame");
                string n = name;
                fb.Click += (s, e) =>
                {
                    S.Frame = n;
                    Settings.Save();
                    for (int i = 0; i < Frames.Length; i++) frameButtons[i].BorderBrush = Frames[i].name == n ? Brushes.White : (Frames[i].name == "None" ? Theme.Muted : Brushes.Transparent);
                    ApplyFrame();
                };
                frameButtons.Add(fb);
                frameRow.Children.Add(fb);
            }
            var padding = new MiniSlider(0, 160, S.Padding, null, 230) { Step = 4 };
            var padText = UI.Text(S.Padding + " px", 13, Theme.Muted);
            padding.ValueChanged += v => { S.Padding = (int)v; padText.Text = (int)v + " px"; Settings.Save(); ApplyFrame(); };
            var corners = new MiniSlider(0, 48, S.Corners, null, 230) { Step = 2 };
            var cornerText = UI.Text(S.Corners + " px", 13, Theme.Muted);
            corners.ValueChanged += v => { S.Corners = (int)v; cornerText.Text = (int)v + " px"; Settings.Save(); ApplyFrame(); };
            var shadow = new ToggleSwitch(S.Shadow, "Shadow");
            shadow.Toggled += on => { S.Shadow = on; Settings.Save(); ApplyFrame(); };
            var blurEmails = FlatButton.Secondary("Blur emails now", async () => await BlurEmails(true));
            blurEmails.HorizontalAlignment = HorizontalAlignment.Left;

            DockPanel Labelled(string label, UIElement value)
            {
                var d = new DockPanel();
                DockPanel.SetDock(value, System.Windows.Controls.Dock.Right);
                d.Children.Add(value);
                d.Children.Add(UI.Text(label, 14, Theme.Text, FontWeights.SemiBold));
                return d;
            }

            var side = new Border
            {
                Width = 290,
                Background = Theme.Side,
                Padding = new Thickness(22),
                Child = UI.V(22,
                    UI.V(12, UI.Label("Frame background"), frameRow),
                    UI.V(10, Labelled("Padding", padText), padding),
                    UI.V(10, Labelled("Rounded corners", cornerText), corners),
                    Labelled("Shadow", shadow),
                    UI.V(8, UI.Text("Private details", 14, Theme.Text, FontWeights.SemiBold), UI.Text("Finds email addresses in the picture and blurs them. Runs on your PC.", 12, Theme.Muted), blurEmails))
            };

            // ----- bottom bar -----
            _status = UI.Text(_w + " × " + _h + "  ·  captured " + DateTime.Now.ToString("HH:mm"), 13, Theme.Muted);
            _status.VerticalAlignment = VerticalAlignment.Center;
            var copyBtn = FlatButton.Primary("Copy");
            copyBtn.Click += (s, e) => { Copy(); copyBtn.Content = "Copied"; };
            var saveBtn = FlatButton.Secondary("Save", Save);
            var pinBtn = FlatButton.Secondary("Pin to screen", Pin);
            var actions = UI.H(10, pinBtn, saveBtn, copyBtn);
            var bottom = new DockPanel { Height = 70, Background = Theme.Side, LastChildFill = false };
            DockPanel.SetDock(actions, System.Windows.Controls.Dock.Right);
            actions.Margin = new Thickness(0, 0, 20, 0);
            actions.VerticalAlignment = VerticalAlignment.Center;
            _status.Margin = new Thickness(20, 0, 0, 0);
            bottom.Children.Add(actions);
            bottom.Children.Add(_status);

            var layout = new DockPanel();
            DockPanel.SetDock(toolbar, System.Windows.Controls.Dock.Top);
            DockPanel.SetDock(bottom, System.Windows.Controls.Dock.Bottom);
            DockPanel.SetDock(side, System.Windows.Controls.Dock.Right);
            layout.Children.Add(toolbar);
            layout.Children.Add(bottom);
            layout.Children.Add(side);
            layout.Children.Add(stage);
            Content = layout;

            _canvas.MouseLeftButtonDown += OnDown;
            _canvas.MouseMove += OnMove;
            _canvas.MouseLeftButtonUp += OnUp;
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { Undo(); e.Handled = true; }
                if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) != 0 && !(Keyboard.FocusedElement is TextBox)) { Copy(); copyBtn.Content = "Copied"; e.Handled = true; }
                if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { Save(); e.Handled = true; }
                if (e.Key == Key.Escape && !(Keyboard.FocusedElement is TextBox)) Close();
            };
            SourceInitialized += (s, e) => Native.UseDarkTitleBar(new WindowInteropHelper(this).Handle);
            Loaded += async (s, e) =>
            {
                SetTool("Arrow");
                ApplyFrame();
                if (S.AutoBlurEmails) await BlurEmails(false);
            };
        }

        private void SetTool(string name)
        {
            CommitText();
            _tool = name;
            foreach (var kv in _toolButtons) kv.Value.Background = kv.Key == name ? Theme.AmberTint : Brushes.Transparent;
            _canvas.Cursor = name == "Select" ? Cursors.SizeAll : name == "Text" ? Cursors.IBeam : Cursors.Cross;
        }

        private void ApplyFrame()
        {
            var brush = Frames.FirstOrDefault(f => f.name == S.Frame).brush;
            bool none = brush == null;
            double scale = Math.Max(1, _w / 1200.0);   // keep the frame in proportion on big screenshots
            _frame.Background = brush ?? Brushes.Transparent;
            _frame.Padding = none ? new Thickness(0) : new Thickness(S.Padding * scale);
            var radius = new CornerRadius(none ? 0 : S.Corners * scale);
            _shadowCard.CornerRadius = radius;
            _shadowCard.Effect = !none && S.Shadow ? new DropShadowEffect { BlurRadius = 40 * scale, ShadowDepth = 12 * scale, Direction = 270, Opacity = 0.4, Color = Colors.Black } : null;
            _shadowCard.Visibility = none ? Visibility.Collapsed : Visibility.Visible;
            double r = none ? 0 : S.Corners * scale;
            _content.Clip = new RectangleGeometry(new Rect(0, 0, _w, _h), r, r);
        }

        // ----- drawing -----
        private Brush CurrentBrush => Theme.Brush(_color);

        private void OnDown(object sender, MouseButtonEventArgs e)
        {
            _down = e.GetPosition(_canvas);
            if (_tool != "Text") CommitText();
            switch (_tool)
            {
                case "Select":
                    _moving = HitAnnotation(e.OriginalSource as DependencyObject);
                    if (_moving != null)
                    {
                        _moveStart = _down;
                        var t = Translate(_moving);
                        _moveOrigin = new Vector(t.X, t.Y);
                        _canvas.CaptureMouse();
                    }
                    return;
                case "Text":
                    AddText(_down);
                    e.Handled = true;
                    return;
                case "Steps":
                    AddStep(_down);
                    return;
            }
            _canvas.CaptureMouse();
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(_canvas);
            if (_tool == "Select")
            {
                if (_moving == null) return;
                var t = Translate(_moving);
                t.X = _moveOrigin.X + (p.X - _moveStart.X);
                t.Y = _moveOrigin.Y + (p.Y - _moveStart.Y);
                return;
            }
            if (_tool == "Text" || _tool == "Steps") return;
            if (_preview != null) _canvas.Children.Remove(_preview);
            _preview = Build(_tool, _down, p, true);
            if (_preview != null) _canvas.Children.Add(_preview);
        }

        private void OnUp(object sender, MouseButtonEventArgs e)
        {
            _canvas.ReleaseMouseCapture();
            if (_tool == "Select") { _moving = null; return; }
            if (_preview != null) { _canvas.Children.Remove(_preview); _preview = null; }
            if (_tool == "Text" || _tool == "Steps") return;
            var p = e.GetPosition(_canvas);
            if ((p - _down).Length < 6) return;
            var el = Build(_tool, _down, p, false);
            if (el != null) Add(el);
        }

        private void Add(FrameworkElement el)
        {
            el.RenderTransform = new TranslateTransform();
            _canvas.Children.Add(el);
            _history.Add(el);
        }

        private static TranslateTransform Translate(FrameworkElement el)
        {
            if (!(el.RenderTransform is TranslateTransform t) || t.IsFrozen)
            {
                t = new TranslateTransform();
                el.RenderTransform = t;
            }
            return t;
        }

        private FrameworkElement HitAnnotation(DependencyObject d)
        {
            while (d != null && d != _canvas)
            {
                if (d is FrameworkElement fe && _history.Contains(fe)) return fe;
                d = VisualTreeHelper.GetParent(d);
            }
            return null;
        }

        private FrameworkElement Build(string tool, Point a, Point b, bool preview)
        {
            var r = new Rect(a, b);
            switch (tool)
            {
                case "Arrow":
                    return ArrowShape(a, b);
                case "Box":
                    var box = new Rectangle { Width = r.Width, Height = r.Height, Stroke = CurrentBrush, StrokeThickness = _sw, RadiusX = _sw, RadiusY = _sw };
                    Canvas.SetLeft(box, r.X);
                    Canvas.SetTop(box, r.Y);
                    return box;
                case "Highlight":
                    var hl = new Rectangle { Width = r.Width, Height = r.Height, Fill = Theme.Brush(Color.FromArgb(0x5C, _color.R, _color.G, _color.B)), RadiusX = _sw, RadiusY = _sw };
                    Canvas.SetLeft(hl, r.X);
                    Canvas.SetTop(hl, r.Y);
                    return hl;
                case "Blur":
                    if (preview)
                    {
                        var dash = new Rectangle { Width = r.Width, Height = r.Height, Stroke = Brushes.White, StrokeThickness = Math.Max(1.5, _sw / 2), StrokeDashArray = new DoubleCollection { 4, 3 } };
                        Canvas.SetLeft(dash, r.X);
                        Canvas.SetTop(dash, r.Y);
                        return dash;
                    }
                    return Pixelate(r);
            }
            return null;
        }

        private FrameworkElement ArrowShape(Point a, Point b)
        {
            var v = b - a;
            if (v.Length < 1) v = new Vector(1, 0);
            v.Normalize();
            var n = new Vector(-v.Y, v.X);
            double headLen = _sw * 4.5, headW = _sw * 3.2;
            var tip = b;
            var baseP = b - v * headLen;
            var head = new PathFigure { StartPoint = tip, IsClosed = true, IsFilled = true };
            head.Segments.Add(new LineSegment(baseP + n * headW, true));
            head.Segments.Add(new LineSegment(baseP - n * headW, true));
            var headGeo = new PathGeometry();
            headGeo.Figures.Add(head);
            var group = new GeometryGroup();
            group.Children.Add(new LineGeometry(a, baseP + v * (headLen * 0.2)));
            group.Children.Add(headGeo);
            return new Path
            {
                Data = group,
                Stroke = CurrentBrush,
                Fill = CurrentBrush,
                StrokeThickness = _sw,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round
            };
        }

        private FrameworkElement Pixelate(Rect r)
        {
            int x = (int)Math.Max(0, r.X), y = (int)Math.Max(0, r.Y);
            int w = (int)Math.Min(_w - x, r.Width), h = (int)Math.Min(_h - y, r.Height);
            if (w < 2 || h < 2) return null;
            var crop = new CroppedBitmap(_source, new Int32Rect(x, y, w, h));
            double block = Math.Max(8, Math.Min(w, h) / 5.0);
            var small = new TransformedBitmap(crop, new ScaleTransform(Math.Max(1.0 / w, 1.0 / block), Math.Max(1.0 / h, 1.0 / block)));
            var img = new Image { Source = small, Width = w, Height = h, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
            Canvas.SetLeft(img, x);
            Canvas.SetTop(img, y);
            return img;
        }

        private TextBox _editing;

        private void AddText(Point p)
        {
            if (_editing != null) { CommitText(); return; }
            var tb = new TextBox
            {
                FontSize = _sw * 7,
                FontWeight = FontWeights.Bold,
                FontFamily = Theme.Display,
                Foreground = CurrentBrush,
                CaretBrush = CurrentBrush,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                MinWidth = 40,
                AcceptsReturn = false
            };
            Canvas.SetLeft(tb, p.X);
            Canvas.SetTop(tb, p.Y - _sw * 5);
            Add(tb);
            _editing = tb;
            tb.KeyDown += (s, e) => { if (e.Key == Key.Enter || e.Key == Key.Escape) { CommitText(); e.Handled = true; } };
            Dispatcher.BeginInvoke(new Action(() => { tb.Focus(); Keyboard.Focus(tb); }), System.Windows.Threading.DispatcherPriority.Input);
        }

        private void CommitText()
        {
            if (_editing == null) return;
            var tb = _editing;
            _editing = null;
            if (string.IsNullOrWhiteSpace(tb.Text))
            {
                _canvas.Children.Remove(tb);
                _history.Remove(tb);
                return;
            }
            tb.IsReadOnly = true;
            tb.Focusable = false;
            tb.IsHitTestVisible = true;
            Keyboard.ClearFocus();
        }

        private void AddStep(Point p)
        {
            double d = _sw * 10;
            var g = new Grid { Width = d, Height = d };
            g.Children.Add(new Ellipse { Fill = CurrentBrush, Stroke = Brushes.White, StrokeThickness = Math.Max(2, _sw * 0.7) });
            var t = new TextBlock
            {
                Text = _step.ToString(),
                Foreground = _color == Colors.White ? Theme.Bg : Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = d * 0.5,
                FontFamily = Theme.Display,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            g.Children.Add(t);
            Canvas.SetLeft(g, p.X - d / 2);
            Canvas.SetTop(g, p.Y - d / 2);
            g.Tag = "step";
            Add(g);
            _step++;
        }

        private void Undo()
        {
            CommitText();
            if (_history.Count == 0) return;
            var last = _history[_history.Count - 1];
            _history.RemoveAt(_history.Count - 1);
            _canvas.Children.Remove(last);
            if (last is Grid g && (g.Tag as string) == "step") _step = Math.Max(1, _step - 1);
        }

        // ----- email blur (Windows' built-in OCR, entirely offline) -----
        private static readonly Regex Email = new Regex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.Compiled);

        private async Task BlurEmails(bool manual)
        {
            try
            {
                var engine = Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages();
                if (engine == null) { if (manual) _status.Text = "Text recognition isn't available on this PC."; return; }
                if (_w > Windows.Media.Ocr.OcrEngine.MaxImageDimension || _h > Windows.Media.Ocr.OcrEngine.MaxImageDimension)
                {
                    if (manual) _status.Text = "Screenshot is too big to scan for emails.";
                    return;
                }
                var conv = new FormatConvertedBitmap(_source, PixelFormats.Bgra32, null, 0);
                int stride = _w * 4;
                var pixels = new byte[stride * _h];
                conv.CopyPixels(pixels, stride, 0);
                var sb = Windows.Graphics.Imaging.SoftwareBitmap.CreateCopyFromBuffer(
                    pixels.AsBuffer(), Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, _w, _h, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);
                var result = await engine.RecognizeAsync(sb);
                int found = 0;
                foreach (var line in result.Lines)
                {
                    foreach (var word in line.Words)
                    {
                        if (!Email.IsMatch(word.Text)) continue;
                        var br = word.BoundingRect;
                        var rect = new Rect(br.X - 4, br.Y - 4, br.Width + 8, br.Height + 8);
                        var el = Pixelate(rect);
                        if (el != null) { Add(el); found++; }
                    }
                }
                if (manual || found > 0) _status.Text = found == 0 ? "No email addresses found." : "Blurred " + found + (found == 1 ? " email address." : " email addresses.") + " Ctrl+Z to undo.";
            }
            catch (Exception ex)
            {
                Log.Write("OCR failed: " + ex.Message);
                if (manual) _status.Text = "Couldn't scan the picture.";
            }
        }

        // ----- export -----
        private BitmapSource Render()
        {
            CommitText();
            _frame.UpdateLayout();
            double w = _frame.ActualWidth, h = _frame.ActualHeight;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
                dc.DrawRectangle(new VisualBrush(_frame) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, w, h));
            var rtb = new RenderTargetBitmap((int)Math.Ceiling(w), (int)Math.Ceiling(h), 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        private void Copy()
        {
            try { System.Windows.Clipboard.SetImage(Render()); _status.Text = "Copied to the clipboard."; }
            catch (Exception ex) { _status.Text = "Couldn't copy: " + ex.Message; }
        }

        private void Save()
        {
            try
            {
                string dir = CaptureModule.SaveFolder();
                Directory.CreateDirectory(dir);
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    InitialDirectory = dir,
                    FileName = "Screenshot " + DateTime.Now.ToString("yyyy-MM-dd HHmmss") + ".png",
                    Filter = "PNG image (*.png)|*.png|JPEG image (*.jpg)|*.jpg",
                    DefaultExt = ".png"
                };
                if (dlg.ShowDialog(this) != true) return;
                BitmapEncoder enc = dlg.FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ? new JpegBitmapEncoder { QualityLevel = 92 } : new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(Render()));
                using (var fs = File.Create(dlg.FileName)) enc.Save(fs);
                _status.Text = "Saved to " + dlg.FileName;
            }
            catch (Exception ex) { _status.Text = "Couldn't save: " + ex.Message; }
        }

        private void Pin()
        {
            var pin = new PinWindow(Render());
            pin.Show();
        }
    }

    /// <summary>A screenshot that floats above everything. Drag to move, scroll to resize, double-click or Esc to close.</summary>
    public class PinWindow : Window
    {
        private readonly Image _img;
        private double _scale = 1;

        public PinWindow(BitmapSource bmp)
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            double dpi = 1;
            try { dpi = VisualTreeHelper.GetDpi(Application.Current.MainWindow ?? this).DpiScaleX; } catch { }
            double maxW = SystemParameters.WorkArea.Width * 0.6;
            double w = bmp.PixelWidth / dpi;
            _scale = w > maxW ? maxW / w : 1;
            _img = new Image { Source = bmp, Width = w * _scale, Stretch = Stretch.Uniform };
            Content = new Border
            {
                Margin = new Thickness(12),
                BorderBrush = Theme.Amber,
                BorderThickness = new Thickness(1.5),
                Effect = Theme.Shadow(24, 0.45, 6),
                Child = _img,
                ToolTip = "Drag to move · scroll to resize · double-click to close"
            };
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2) { Close(); return; }
                try { DragMove(); } catch { }
            };
            MouseWheel += (s, e) =>
            {
                double f = e.Delta > 0 ? 1.1 : 0.9;
                _img.Width = Math.Max(80, Math.Min(4000, _img.Width * f));
            };
            KeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
            MouseRightButtonUp += (s, e) =>
            {
                var menu = new ContextMenu();
                var copy = new MenuItem { Header = "Copy" };
                copy.Click += (a, b) => System.Windows.Clipboard.SetImage(bmp);
                var close = new MenuItem { Header = "Close" };
                close.Click += (a, b) => Close();
                menu.Items.Add(copy);
                menu.Items.Add(close);
                menu.IsOpen = true;
            };
        }
    }

    public static class CenterExtensions
    {
        public static T Centered<T>(this T e) where T : FrameworkElement
        {
            e.HorizontalAlignment = HorizontalAlignment.Center;
            return e;
        }
    }
}

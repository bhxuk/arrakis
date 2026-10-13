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
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Arrakis.Core;
using Path = System.Windows.Shapes.Path;

namespace Arrakis.Modules
{
    /// <summary>Something drawn on top of the screenshot.</summary>
    public class Annot
    {
        public string Kind;
        public FrameworkElement El;
        public Color Colour;
        public Action<Color> Recolour;
        public TextBox Text;
    }

    public class CaptureEditor : Window
    {
        // ----- image and layers -----
        private BitmapSource _base;
        private readonly string _title;
        private int _w, _h;
        private readonly double _u;                 // one "unit" in image pixels, so tools scale with the screenshot
        private readonly Image _img;
        private readonly Canvas _canvas;            // annotations
        private readonly Canvas _adorn;             // selection outline and previews
        private readonly Grid _content;
        private readonly Border _shadowCard;
        private readonly Border _frame;
        private readonly Border _zoomHost;
        private readonly ScaleTransform _zoom = new ScaleTransform(1, 1);
        private readonly ScrollViewer _scroll;
        private readonly Rectangle _selBox;

        // ----- state -----
        private string _tool = "Arrow";
        private Color _colour;
        private readonly Dictionary<string, FlatButton> _toolButtons = new Dictionary<string, FlatButton>();
        private readonly List<FlatButton> _swatches = new List<FlatButton>();
        private readonly Stack<(Action undo, Action redo)> _undo = new Stack<(Action, Action)>();
        private readonly Stack<(Action undo, Action redo)> _redo = new Stack<(Action, Action)>();
        private Annot _selected;
        private Annot _editing;
        private Point _down;
        private bool _drawing;
        private FrameworkElement _preview;
        private Polyline _pen;
        private Point _moveStart;
        private Vector _moveOrigin;
        private bool _moving;
        private readonly TextBlock _status;
        private readonly TextBlock _zoomText;
        private readonly TextBlock _sizeText;
        private FlatButton _undoBtn, _redoBtn;
        private string _lastSaved;

        private CaptureSettings S => Settings.Current.Capture;

        private static readonly (string id, string name, string icon, Key key)[] Tools =
        {
            ("Select", "Select", Icons.Cursor, Key.V),
            ("Rectangle", "Box", Icons.Box, Key.R),
            ("Ellipse", "Ellipse", "M4 12a8 6 0 1 0 16 0a8 6 0 1 0-16 0", Key.E),
            ("Line", "Line", "M5 19L19 5", Key.L),
            ("Arrow", "Arrow", Icons.Arrow, Key.A),
            ("Pen", "Pen", "M4 19c2-1 3-5 5-7s3 1 5-1s2-6 6-7", Key.F),
            ("Text", "Text", Icons.TextTool, Key.T),
            ("Callout", "Callout", "M4 5h16v10H10l-4 4v-4H4z", Key.S),
            ("Steps", "Steps", Icons.Steps, Key.N),
            ("Highlight", "Highlight", Icons.Highlight, Key.H),
            ("Pixelate", "Pixelate", Icons.Blur, Key.P),
            ("Blur", "Blur", "M12 3c3 5 6 8 6 11a6 6 0 0 1-12 0c0-3 3-6 6-11z", Key.B),
            ("Crop", "Crop", "M6 2v16h16M2 6h16v16", Key.C),
            ("Picker", "Colour", "M14 4l6 6-9 9H5v-6zM12 6l6 6", Key.I)
        };

        private static readonly Color[] Palette =
        {
            (Color)ColorConverter.ConvertFromString("#E2585F"),
            (Color)ColorConverter.ConvertFromString("#FFB547"),
            (Color)ColorConverter.ConvertFromString("#F5D547"),
            (Color)ColorConverter.ConvertFromString("#8FD16A"),
            (Color)ColorConverter.ConvertFromString("#5ED3C3"),
            (Color)ColorConverter.ConvertFromString("#3D7BF5"),
            (Color)ColorConverter.ConvertFromString("#A06CF0"),
            Colors.White,
            (Color)ColorConverter.ConvertFromString("#111318")
        };

        public static readonly (string name, Brush brush)[] Frames =
        {
            ("None", null),
            ("Sunset", Gradient("#FFB547", "#E2585F")),
            ("Ocean", Gradient("#5ED3C3", "#3D7BF5")),
            ("Meadow", Gradient("#8FD16A", "#1FA392")),
            ("Dusk", Gradient("#A06CF0", "#E2585F")),
            ("Light", Theme.Brush("#F3F1EC")),
            ("Dark", Theme.Brush("#2A2F3A"))
        };

        private static Brush Gradient(string a, string b)
        {
            var g = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(a), (Color)ColorConverter.ConvertFromString(b), 45);
            g.Freeze();
            return g;
        }

        public CaptureEditor(BitmapSource source, string title = "")
        {
            _base = CaptureImaging.ToBgra(source);
            _title = title ?? "";
            _w = _base.PixelWidth;
            _h = _base.PixelHeight;
            _u = Math.Max(1, Math.Min(_w, _h) / 600.0);
            try { _colour = (Color)ColorConverter.ConvertFromString(S.Colour); } catch { _colour = Palette[0]; }

            Title = "Arrakis Capture" + (string.IsNullOrWhiteSpace(_title) ? "" : " · " + _title);
            Background = Theme.Bg;
            Foreground = Theme.Text;
            FontFamily = Theme.Font;
            Width = Math.Min(1500, SystemParameters.WorkArea.Width - 60);
            Height = Math.Min(940, SystemParameters.WorkArea.Height - 60);
            MinWidth = 1080;
            MinHeight = 640;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            try { Icon = IconCache.AppIcon(); } catch { }

            // ----- composition -----
            _img = new Image { Source = _base, Width = _w, Height = _h, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(_img, BitmapScalingMode.HighQuality);
            _canvas = new Canvas { Width = _w, Height = _h, Background = Brushes.Transparent, ClipToBounds = true };
            _adorn = new Canvas { Width = _w, Height = _h, IsHitTestVisible = false };
            _selBox = new Rectangle { Stroke = Theme.Amber, StrokeThickness = Math.Max(1.5, _u * 1.2), StrokeDashArray = new DoubleCollection { 4, 3 }, Visibility = Visibility.Collapsed };
            _adorn.Children.Add(_selBox);
            _content = new Grid { Width = _w, Height = _h };
            _content.Children.Add(_img);
            _content.Children.Add(_canvas);
            _content.Children.Add(_adorn);
            _shadowCard = new Border { Background = Brushes.White };
            var holder = new Grid();
            holder.Children.Add(_shadowCard);
            holder.Children.Add(_content);
            _frame = new Border { Child = holder, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            _zoomHost = new Border { Child = _frame, LayoutTransform = _zoom, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(32) };
            _scroll = new ScrollViewer
            {
                Content = _zoomHost,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = Theme.Stage,
                Focusable = false
            };
            _scroll.PreviewMouseWheel += (s, e) =>
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
                SetZoom(_zoom.ScaleX * (e.Delta > 0 ? 1.15 : 1 / 1.15));
                e.Handled = true;
            };

            // ----- toolbar -----
            var tools = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            foreach (var t in Tools)
            {
                var b = new FlatButton
                {
                    Width = 60,
                    Height = 54,
                    MinHeight = 54,
                    Padding = new Thickness(0),
                    Background = Brushes.Transparent,
                    Content = UI.V(3, UI.GridIcon(t.icon, 18, Theme.Text).Centered(), UI.Text(t.name, 11, Theme.Text, FontWeights.SemiBold).Centered()),
                    Margin = new Thickness(0, 0, 2, 0),
                    ToolTip = t.name + " (" + t.key + ")"
                };
                string id = t.id;
                b.Click += (s, e) => SetTool(id);
                FlatButton.SetA11yName(b, t.name + " tool");
                _toolButtons[id] = b;
                tools.Children.Add(b);
            }
            _undoBtn = FlatButton.IconButton(Icons.Undo, "Undo (Ctrl+Z)", Undo);
            _redoBtn = FlatButton.IconButton("M15 14l5-5-5-5M20 9H9a5 5 0 0 0 0 10h3", "Redo (Ctrl+Y)", Redo);
            var deleteBtn = FlatButton.IconButton(Icons.Trash, "Delete selected (Del)", DeleteSelected);
            var history = UI.H(6, deleteBtn, _undoBtn, _redoBtn);
            history.VerticalAlignment = VerticalAlignment.Center;
            var toolbar = new DockPanel { Background = Theme.Side, LastChildFill = true, MinHeight = 70 };
            DockPanel.SetDock(history, System.Windows.Controls.Dock.Right);
            history.Margin = new Thickness(0, 0, 14, 0);
            tools.Margin = new Thickness(12, 6, 0, 6);
            toolbar.Children.Add(history);
            toolbar.Children.Add(tools);

            // ----- side panel -----
            var side = new ScrollViewer
            {
                Width = 300,
                Background = Theme.Side,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new Border { Padding = new Thickness(20), Child = BuildSidePanel() }
            };

            // ----- bottom bar -----
            _status = UI.Text("", 13, Theme.Muted);
            _status.VerticalAlignment = VerticalAlignment.Center;
            _status.TextTrimming = TextTrimming.CharacterEllipsis;
            _status.TextWrapping = TextWrapping.NoWrap;
            _status.Cursor = Cursors.Hand;
            _status.MouseLeftButtonUp += (s, e) =>
            {
                if (_lastSaved != null && File.Exists(_lastSaved)) Process.Start("explorer.exe", "/select,\"" + _lastSaved + "\"");
            };
            _sizeText = UI.Text("", 13, Theme.Muted);
            _sizeText.VerticalAlignment = VerticalAlignment.Center;
            _zoomText = UI.Text("100%", 13, Theme.Text, FontWeights.SemiBold);
            _zoomText.VerticalAlignment = VerticalAlignment.Center;
            _zoomText.Width = 46;
            _zoomText.TextAlignment = TextAlignment.Center;
            var zoomOut = FlatButton.IconButton("M5 12h14", "Zoom out (Ctrl -)", () => SetZoom(_zoom.ScaleX / 1.25), 36);
            var zoomIn = FlatButton.IconButton(Icons.Plus, "Zoom in (Ctrl +)", () => SetZoom(_zoom.ScaleX * 1.25), 36);
            var fit = new FlatButton { Content = "Fit", MinHeight = 36, Height = 36, Padding = new Thickness(12, 0, 12, 0), FontSize = 13 };
            fit.Click += (s, e) => FitZoom();
            var actual = new FlatButton { Content = "1:1", MinHeight = 36, Height = 36, Padding = new Thickness(12, 0, 12, 0), FontSize = 13 };
            actual.Click += (s, e) => SetZoom(1);
            var zoomRow = UI.H(4, zoomOut, _zoomText, zoomIn, fit, actual);
            zoomRow.VerticalAlignment = VerticalAlignment.Center;

            FlatButton Small(string text, string tip, Action a)
            {
                var b = FlatButton.Secondary(text, a);
                b.ToolTip = tip;
                b.Padding = new Thickness(14, 0, 14, 0);
                return b;
            }
            var copyBtn = FlatButton.Primary("Copy");
            copyBtn.ToolTip = "Copy to clipboard (Ctrl+C)";
            copyBtn.Click += (s, e) => Copy();
            var actions = UI.H(8,
                Small("Paint", "Open in Paint", OpenInPaint),
                Small("Open with...", "Open in another app", OpenWith),
                Small("Print", "Print (Ctrl+P)", Print),
                Small("Pin", "Float above everything", Pin),
                Small("Save as...", "Save as (Ctrl+Shift+S)", SaveAs),
                Small("Save", "Save to your screenshot folder (Ctrl+S)", QuickSave),
                copyBtn);
            actions.VerticalAlignment = VerticalAlignment.Center;
            var bottom = new DockPanel { Height = 70, Background = Theme.Side, LastChildFill = true };
            DockPanel.SetDock(actions, System.Windows.Controls.Dock.Right);
            DockPanel.SetDock(zoomRow, System.Windows.Controls.Dock.Left);
            DockPanel.SetDock(_sizeText, System.Windows.Controls.Dock.Left);
            actions.Margin = new Thickness(0, 0, 16, 0);
            zoomRow.Margin = new Thickness(14, 0, 14, 0);
            _sizeText.Margin = new Thickness(0, 0, 14, 0);
            _status.Margin = new Thickness(0, 0, 14, 0);
            bottom.Children.Add(actions);
            bottom.Children.Add(zoomRow);
            bottom.Children.Add(_sizeText);
            bottom.Children.Add(_status);

            var layout = new DockPanel();
            DockPanel.SetDock(toolbar, System.Windows.Controls.Dock.Top);
            DockPanel.SetDock(bottom, System.Windows.Controls.Dock.Bottom);
            DockPanel.SetDock(side, System.Windows.Controls.Dock.Right);
            layout.Children.Add(toolbar);
            layout.Children.Add(bottom);
            layout.Children.Add(side);
            layout.Children.Add(_scroll);
            Content = layout;

            _canvas.PreviewMouseLeftButtonDown += OnDown;
            _canvas.MouseMove += OnMove;
            _canvas.MouseLeftButtonUp += OnUp;
            PreviewKeyDown += OnKey;
            SourceInitialized += (s, e) => Native.UseDarkTitleBar(new WindowInteropHelper(this).Handle);
            Loaded += async (s, e) =>
            {
                SetTool("Arrow");
                ApplyFrame();
                UpdateSize();
                UpdateHistoryButtons();
                await Dispatcher.InvokeAsync(FitZoom, System.Windows.Threading.DispatcherPriority.Loaded);
                if (S.AutoBlurEmails) await BlurEmails(false);
            };
        }

        // =====================================================================
        // Side panel
        // =====================================================================
        private UIElement BuildSidePanel()
        {
            // Colours
            var swatchRow = new WrapPanel();
            foreach (var c in Palette)
            {
                var sw = new FlatButton
                {
                    Width = 26,
                    Height = 26,
                    MinHeight = 26,
                    Padding = new Thickness(0),
                    CornerRadius = new CornerRadius(13),
                    Background = Theme.Brush(c),
                    BorderThickness = new Thickness(2),
                    BorderBrush = Brushes.Transparent,
                    Margin = new Thickness(0, 0, 7, 7)
                };
                FlatButton.SetA11yName(sw, "Colour");
                var col = c;
                sw.Click += (s, e) => PickColour(col);
                _swatches.Add(sw);
                swatchRow.Children.Add(sw);
            }
            HighlightSwatch();

            var fillRow = UI.H(6);
            var fillChips = new List<Chip>();
            foreach (var f in new[] { "None", "Soft", "Solid" })
            {
                var chip = new Chip(f, S.Fill == f);
                chip.Click += (s, e) =>
                {
                    S.Fill = f;
                    Settings.Save();
                    foreach (var c in fillChips) c.Selected = (string)c.Content == f;
                };
                fillChips.Add(chip);
                fillRow.Children.Add(chip);
            }

            var thick = new MiniSlider(1, 10, S.Thickness, null, 250);
            thick.ValueChanged += v => { S.Thickness = (int)v; Settings.Save(); };
            var font = new MiniSlider(2, 12, S.FontSize, null, 250);
            font.ValueChanged += v => { S.FontSize = (int)v; Settings.Save(); };
            var shadow = new ToggleSwitch(S.AnnotationShadow, "Shadow on shapes");
            shadow.Toggled += on => { S.AnnotationShadow = on; Settings.Save(); };

            // Image effects
            FlatButton EffectButton(string text, Func<BitmapSource, BitmapSource> op)
            {
                var b = new FlatButton { Content = text, MinHeight = 38, Height = 38, FontSize = 13, Margin = new Thickness(0, 0, 6, 6), HorizontalAlignment = HorizontalAlignment.Stretch };
                b.Click += (s, e) => ApplyImageOp(text, op);
                return b;
            }
            var effects = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
            effects.Children.Add(EffectButton("Rotate left", b => CaptureImaging.Rotate(b, -90)));
            effects.Children.Add(EffectButton("Rotate right", b => CaptureImaging.Rotate(b, 90)));
            effects.Children.Add(EffectButton("Flip", b => CaptureImaging.Flip(b, true)));
            var resizeBtn = new FlatButton { Content = "Resize...", MinHeight = 38, Height = 38, FontSize = 13, Margin = new Thickness(0, 0, 6, 6) };
            resizeBtn.Click += (s, e) => ResizeDialog();
            effects.Children.Add(resizeBtn);
            effects.Children.Add(EffectButton("Greyscale", CaptureImaging.Grayscale));
            effects.Children.Add(EffectButton("Invert", CaptureImaging.Invert));
            effects.Children.Add(EffectButton("Border", b => CaptureImaging.AddBorder(b, _colour, (int)Math.Round(4 * _u))));
            effects.Children.Add(EffectButton("Shadow", b => CaptureImaging.DropShadow(b, _u)));
            effects.Children.Add(EffectButton("Torn edges", b => CaptureImaging.TornEdges(b, _u)));

            // Frame
            var frameRow = new WrapPanel();
            var frameButtons = new List<FlatButton>();
            foreach (var (name, brush) in Frames)
            {
                var fb = new FlatButton
                {
                    Width = 30,
                    Height = 30,
                    MinHeight = 30,
                    Padding = new Thickness(0),
                    CornerRadius = new CornerRadius(8),
                    Background = brush ?? Brushes.Transparent,
                    BorderThickness = new Thickness(2),
                    Margin = new Thickness(0, 0, 7, 7),
                    ToolTip = name
                };
                FlatButton.SetA11yName(fb, name + " frame");
                frameButtons.Add(fb);
                frameRow.Children.Add(fb);
            }
            void MarkFrames()
            {
                for (int i = 0; i < Frames.Length; i++)
                    frameButtons[i].BorderBrush = Frames[i].name == S.Frame ? Brushes.White : (Frames[i].name == "None" ? Theme.Muted : Brushes.Transparent);
            }
            for (int i = 0; i < Frames.Length; i++)
            {
                string n = Frames[i].name;
                frameButtons[i].Click += (s, e) => { S.Frame = n; Settings.Save(); MarkFrames(); ApplyFrame(); };
            }
            MarkFrames();
            var padding = new MiniSlider(0, 160, S.Padding, null, 250) { Step = 4 };
            padding.ValueChanged += v => { S.Padding = (int)v; Settings.Save(); ApplyFrame(); };
            var corners = new MiniSlider(0, 48, S.Corners, null, 250) { Step = 2 };
            corners.ValueChanged += v => { S.Corners = (int)v; Settings.Save(); ApplyFrame(); };
            var frameShadow = new ToggleSwitch(S.Shadow, "Frame shadow");
            frameShadow.Toggled += on => { S.Shadow = on; Settings.Save(); ApplyFrame(); };

            // Privacy and text
            var blurEmails = FlatButton.Secondary("Blur emails", async () => await BlurEmails(true));
            var copyText = FlatButton.Secondary("Copy text", async () => await CopyText());
            copyText.ToolTip = "Reads the words in the picture and copies them";

            DockPanel Row(string label, UIElement value)
            {
                var d = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                DockPanel.SetDock(value, System.Windows.Controls.Dock.Right);
                d.Children.Add(value);
                var t = UI.Text(label, 14, Theme.Text, FontWeights.SemiBold);
                t.VerticalAlignment = VerticalAlignment.Center;
                d.Children.Add(t);
                return d;
            }

            return UI.V(24,
                UI.V(10, UI.Label("Colour"), swatchRow, UI.Text("Pick from the picture with the Colour tool (I).", 12, Theme.Muted)),
                UI.V(10, UI.Label("Fill for boxes and ellipses"), fillRow),
                UI.V(10, UI.Label("Line thickness"), thick),
                UI.V(10, UI.Label("Text size"), font),
                Row("Shadow on shapes", shadow),
                UI.Divider(),
                UI.V(10, UI.Label("Image"), effects),
                UI.Divider(),
                UI.V(10, UI.Label("Frame"), frameRow),
                UI.V(8, UI.Text("Padding", 14, Theme.Text, FontWeights.SemiBold), padding),
                UI.V(8, UI.Text("Rounded corners", 14, Theme.Text, FontWeights.SemiBold), corners),
                Row("Frame shadow", frameShadow),
                UI.Divider(),
                UI.V(10, UI.Label("Text in the picture"),
                    UI.Text("Runs on your PC using Windows' own text recognition.", 12, Theme.Muted),
                    UI.H(8, blurEmails, copyText)));
        }

        private void HighlightSwatch()
        {
            for (int i = 0; i < Palette.Length; i++)
                _swatches[i].BorderBrush = Palette[i] == _colour ? Theme.Amber : (Palette[i] == Colors.White ? Theme.Muted : Brushes.Transparent);
        }

        private void PickColour(Color c)
        {
            _colour = c;
            S.Colour = c.ToString();
            Settings.Save();
            HighlightSwatch();
            if (_selected != null)
            {
                var a = _selected;
                var old = a.Colour;
                Do(() => { a.Recolour(c); a.Colour = c; }, () => { a.Recolour(old); a.Colour = old; });
            }
        }

        // =====================================================================
        // Tools and drawing
        // =====================================================================
        private double Stroke => Math.Max(1.5, S.Thickness * _u * 1.1);
        private double FontPx => Math.Max(12, S.FontSize * _u * 5);

        private Brush FillBrush(Color c)
        {
            if (S.Fill == "Solid") return Theme.Brush(c);
            if (S.Fill == "Soft") return Theme.Brush(Color.FromArgb(0x55, c.R, c.G, c.B));
            return Brushes.Transparent;
        }

        private void SetTool(string id)
        {
            CommitText();
            _tool = id;
            foreach (var kv in _toolButtons) kv.Value.Background = kv.Key == id ? Theme.AmberTint : Brushes.Transparent;
            _canvas.Cursor = id == "Select" ? Cursors.Arrow : id == "Text" || id == "Callout" ? Cursors.IBeam : id == "Picker" ? Cursors.Hand : Cursors.Cross;
            if (id != "Select") Select(null);
            _status.Text = Hint(id);
        }

        private static string Hint(string id) => id switch
        {
            "Select" => "Click something you've added to select it. Drag to move, Delete to remove, pick a colour to recolour. Double-click text to edit.",
            "Rectangle" => "Drag to draw a box. Hold Shift for a square.",
            "Ellipse" => "Drag to draw an ellipse. Hold Shift for a circle.",
            "Line" => "Drag to draw a line. Hold Shift to snap to 45°.",
            "Arrow" => "Drag from where it starts to where it points. Hold Shift to snap to 45°.",
            "Pen" => "Draw freehand.",
            "Text" => "Click where you want text and type. Enter to finish, Shift+Enter for a new line.",
            "Callout" => "Click to add a speech bubble and type.",
            "Steps" => "Click to drop numbered circles: 1, 2, 3...",
            "Highlight" => "Drag over something to highlight it.",
            "Pixelate" => "Drag over anything private to pixelate it.",
            "Blur" => "Drag over anything private to blur it.",
            "Crop" => "Drag the area you want to keep.",
            "Picker" => "Click anywhere in the picture to use that colour.",
            _ => ""
        };

        private void OnDown(object sender, MouseButtonEventArgs e)
        {
            var p = Clamp(e.GetPosition(_canvas));
            _down = p;
            // Clicks inside the text being typed belong to the text box.
            if (_editing != null && e.OriginalSource is DependencyObject d0 && IsInside(d0, _editing.El)) return;
            if (_editing != null) CommitText();

            switch (_tool)
            {
                case "Select":
                {
                    var hit = HitAnnot(e.OriginalSource as DependencyObject);
                    Select(hit);
                    if (hit != null)
                    {
                        if (e.ClickCount == 2 && hit.Text != null) { BeginEdit(hit); e.Handled = true; return; }
                        e.Handled = true;
                        _moving = true;
                        _moveStart = p;
                        var t = Translate(hit.El);
                        _moveOrigin = new Vector(t.X, t.Y);
                        _canvas.CaptureMouse();
                    }
                    return;
                }
                case "Text":
                case "Callout":
                    if (HitAnnot(e.OriginalSource as DependencyObject) is Annot existing && existing.Text != null) { BeginEdit(existing); e.Handled = true; return; }
                    AddText(p, _tool == "Callout");
                    e.Handled = true;
                    return;
                case "Steps":
                    AddStep(p);
                    return;
                case "Picker":
                    PickFromImage(p);
                    return;
                case "Pen":
                    _pen = new Polyline { Stroke = Theme.Brush(_colour), StrokeThickness = Stroke, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                    _pen.Points.Add(p);
                    _canvas.Children.Add(_pen);
                    break;
            }
            _drawing = true;
            _canvas.CaptureMouse();
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            var p = Clamp(e.GetPosition(_canvas));
            if (_moving && _selected != null)
            {
                var t = Translate(_selected.El);
                t.X = _moveOrigin.X + (p.X - _moveStart.X);
                t.Y = _moveOrigin.Y + (p.Y - _moveStart.Y);
                UpdateSelectionBox();
                return;
            }
            if (!_drawing) return;
            if (_tool == "Pen" && _pen != null)
            {
                var last = _pen.Points[_pen.Points.Count - 1];
                if ((p - last).Length >= Math.Max(1, _u)) _pen.Points.Add(p);
                return;
            }
            if (_preview != null) _canvas.Children.Remove(_preview);
            _preview = Build(_tool, _down, Constrain(_down, p), true);
            if (_preview != null) _canvas.Children.Add(_preview);
        }

        private void OnUp(object sender, MouseButtonEventArgs e)
        {
            _canvas.ReleaseMouseCapture();
            if (_moving)
            {
                _moving = false;
                if (_selected != null)
                {
                    var a = _selected;
                    var t = Translate(a.El);
                    var from = _moveOrigin;
                    var to = new Vector(t.X, t.Y);
                    if ((to - from).Length > 0.5)
                        Record(() => { var tt = Translate(a.El); tt.X = from.X; tt.Y = from.Y; UpdateSelectionBox(); },
                               () => { var tt = Translate(a.El); tt.X = to.X; tt.Y = to.Y; UpdateSelectionBox(); });
                }
                return;
            }
            if (!_drawing) return;
            _drawing = false;
            if (_preview != null) { _canvas.Children.Remove(_preview); _preview = null; }
            var p = Constrain(_down, Clamp(e.GetPosition(_canvas)));

            if (_tool == "Pen" && _pen != null)
            {
                var pen = _pen;
                _pen = null;
                _canvas.Children.Remove(pen);
                if (pen.Points.Count < 2) return;
                var col = _colour;
                var a = new Annot { Kind = "Pen", El = pen, Colour = col, Recolour = c => pen.Stroke = Theme.Brush(c) };
                AddShadow(pen);
                AddAnnot(a);
                return;
            }
            if ((p - _down).Length < 4) return;
            if (_tool == "Crop") { Crop(new Rect(_down, p)); return; }
            var el = Build(_tool, _down, p, false);
            if (el == null) return;
            var colour = _colour;
            var annot = new Annot { Kind = _tool, El = el, Colour = colour, Recolour = RecolourFor(_tool, el) };
            if (_tool != "Highlight" && _tool != "Pixelate" && _tool != "Blur") AddShadow(el);
            AddAnnot(annot);
        }

        private Point Clamp(Point p) => new Point(Math.Max(0, Math.Min(_w, p.X)), Math.Max(0, Math.Min(_h, p.Y)));

        private Point Constrain(Point a, Point b)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) == 0) return b;
            var v = b - a;
            if (_tool == "Rectangle" || _tool == "Ellipse" || _tool == "Highlight" || _tool == "Crop")
            {
                double s = Math.Max(Math.Abs(v.X), Math.Abs(v.Y));
                return new Point(a.X + Math.Sign(v.X == 0 ? 1 : v.X) * s, a.Y + Math.Sign(v.Y == 0 ? 1 : v.Y) * s);
            }
            if (_tool == "Line" || _tool == "Arrow")
            {
                double ang = Math.Atan2(v.Y, v.X);
                double snap = Math.Round(ang / (Math.PI / 4)) * (Math.PI / 4);
                double len = v.Length;
                return new Point(a.X + Math.Cos(snap) * len, a.Y + Math.Sin(snap) * len);
            }
            return b;
        }

        private FrameworkElement Build(string tool, Point a, Point b, bool preview)
        {
            var r = new Rect(a, b);
            var c = _colour;
            switch (tool)
            {
                case "Rectangle":
                {
                    var box = new Rectangle { Width = r.Width, Height = r.Height, Stroke = Theme.Brush(c), StrokeThickness = Stroke, Fill = FillBrush(c), RadiusX = Stroke, RadiusY = Stroke };
                    Canvas.SetLeft(box, r.X);
                    Canvas.SetTop(box, r.Y);
                    return box;
                }
                case "Ellipse":
                {
                    var el = new Ellipse { Width = r.Width, Height = r.Height, Stroke = Theme.Brush(c), StrokeThickness = Stroke, Fill = FillBrush(c) };
                    Canvas.SetLeft(el, r.X);
                    Canvas.SetTop(el, r.Y);
                    return el;
                }
                case "Line":
                    return new Line { X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, Stroke = Theme.Brush(c), StrokeThickness = Stroke, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                case "Arrow":
                    return ArrowShape(a, b, c);
                case "Highlight":
                {
                    var hl = new Rectangle { Width = r.Width, Height = r.Height, Fill = Theme.Brush(Color.FromArgb(0x60, c.R, c.G, c.B)), RadiusX = _u * 2, RadiusY = _u * 2 };
                    Canvas.SetLeft(hl, r.X);
                    Canvas.SetTop(hl, r.Y);
                    return hl;
                }
                case "Pixelate":
                case "Blur":
                case "Crop":
                    if (preview)
                    {
                        var dash = new Rectangle { Width = r.Width, Height = r.Height, Stroke = tool == "Crop" ? Theme.Amber : Brushes.White, StrokeThickness = Math.Max(1.5, _u), StrokeDashArray = new DoubleCollection { 4, 3 }, Fill = tool == "Crop" ? Theme.Brush(Color.FromArgb(0x22, 0xFF, 0xB5, 0x47)) : null };
                        Canvas.SetLeft(dash, r.X);
                        Canvas.SetTop(dash, r.Y);
                        return dash;
                    }
                    if (tool == "Crop") return null;
                    return tool == "Pixelate" ? Pixelate(r) : Blur(r);
            }
            return null;
        }

        private Action<Color> RecolourFor(string tool, FrameworkElement el)
        {
            switch (el)
            {
                case Rectangle rect when tool == "Highlight":
                    return c => rect.Fill = Theme.Brush(Color.FromArgb(0x60, c.R, c.G, c.B));
                case Shape shape:
                {
                    bool filled = shape.Fill != null && shape.Fill != Brushes.Transparent && !(shape is Line);
                    bool soft = filled && shape.Fill is SolidColorBrush sb && sb.Color.A < 0xFF;
                    bool arrow = tool == "Arrow";
                    return c =>
                    {
                        shape.Stroke = Theme.Brush(c);
                        if (arrow) shape.Fill = Theme.Brush(c);
                        else if (filled) shape.Fill = soft ? Theme.Brush(Color.FromArgb(0x55, c.R, c.G, c.B)) : Theme.Brush(c);
                    };
                }
            }
            return c => { };
        }

        private Path ArrowShape(Point a, Point b, Color c)
        {
            var v = b - a;
            if (v.Length < 1) v = new Vector(1, 0);
            v.Normalize();
            var n = new Vector(-v.Y, v.X);
            double sw = Stroke;
            double headLen = sw * 4.5, headW = sw * 3.2;
            var baseP = b - v * headLen;
            var head = new PathFigure { StartPoint = b, IsClosed = true, IsFilled = true };
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
                Stroke = Theme.Brush(c),
                Fill = Theme.Brush(c),
                StrokeThickness = sw,
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
            var crop = new CroppedBitmap(_base, new Int32Rect(x, y, w, h));
            double block = Math.Max(8, Math.Min(w, h) / 5.0);
            var small = new TransformedBitmap(crop, new ScaleTransform(Math.Max(1.0 / w, 1.0 / block), Math.Max(1.0 / h, 1.0 / block)));
            var img = new Image { Source = small, Width = w, Height = h, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
            Canvas.SetLeft(img, x);
            Canvas.SetTop(img, y);
            return img;
        }

        private FrameworkElement Blur(Rect r)
        {
            int x = (int)Math.Max(0, r.X), y = (int)Math.Max(0, r.Y);
            int w = (int)Math.Min(_w - x, r.Width), h = (int)Math.Min(_h - y, r.Height);
            if (w < 2 || h < 2) return null;
            double radius = Math.Max(10, Math.Min(w, h) / 4.0);
            int pad = (int)Math.Ceiling(radius);
            int px = Math.Max(0, x - pad), py = Math.Max(0, y - pad);
            int pw = Math.Min(_w - px, w + pad * 2), ph = Math.Min(_h - py, h + pad * 2);
            var crop = new CroppedBitmap(_base, new Int32Rect(px, py, pw, ph));
            var img = new Image { Source = crop, Width = pw, Height = ph, Effect = new BlurEffect { Radius = radius, KernelType = KernelType.Gaussian } };
            var holder = new Canvas { Width = w, Height = h, ClipToBounds = true };
            Canvas.SetLeft(img, px - x);
            Canvas.SetTop(img, py - y);
            holder.Children.Add(img);
            Canvas.SetLeft(holder, x);
            Canvas.SetTop(holder, y);
            return holder;
        }

        private void AddShadow(UIElement el)
        {
            if (!S.AnnotationShadow) return;
            el.Effect = new DropShadowEffect { BlurRadius = 6 * _u, ShadowDepth = 2 * _u, Direction = 300, Opacity = 0.45, Color = Colors.Black };
        }

        private void AddText(Point p, bool callout)
        {
            var c = _colour;
            bool darkText = callout && (c.R * 0.299 + c.G * 0.587 + c.B * 0.114) > 160;
            var tb = new TextBox
            {
                FontSize = FontPx,
                FontWeight = FontWeights.Bold,
                FontFamily = Theme.Display,
                Foreground = callout ? (darkText ? Theme.Bg : Brushes.White) : Theme.Brush(c),
                CaretBrush = callout ? (darkText ? Theme.Bg : Brushes.White) : Theme.Brush(c),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                MinWidth = 40,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                Padding = new Thickness(0)
            };
            FrameworkElement el;
            Action<Color> recolour;
            if (callout)
            {
                var bubble = new Border { Background = Theme.Brush(c), CornerRadius = new CornerRadius(FontPx * 0.5), Padding = new Thickness(FontPx * 0.6, FontPx * 0.35, FontPx * 0.6, FontPx * 0.35), Child = tb };
                double t = FontPx * 0.7;
                var tail = new Path
                {
                    Data = Geometry.Parse(string.Format(System.Globalization.CultureInfo.InvariantCulture, "M0 0 L{0} 0 L0 {1} Z", t * 1.4, t)),
                    Fill = Theme.Brush(c),
                    Margin = new Thickness(FontPx * 0.8, -1, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                var stack = new StackPanel();
                stack.Children.Add(bubble);
                stack.Children.Add(tail);
                el = stack;
                recolour = col =>
                {
                    bubble.Background = Theme.Brush(col);
                    tail.Fill = Theme.Brush(col);
                    bool dark = (col.R * 0.299 + col.G * 0.587 + col.B * 0.114) > 160;
                    tb.Foreground = dark ? Theme.Bg : Brushes.White;
                };
                Canvas.SetLeft(el, p.X);
                Canvas.SetTop(el, Math.Max(0, p.Y - FontPx * 1.2));
            }
            else
            {
                el = tb;
                recolour = col => { tb.Foreground = Theme.Brush(col); tb.CaretBrush = Theme.Brush(col); };
                Canvas.SetLeft(el, p.X);
                Canvas.SetTop(el, Math.Max(0, p.Y - FontPx * 0.7));
            }
            AddShadow(el);
            el.RenderTransform = new TranslateTransform();
            _canvas.Children.Add(el);
            var annot = new Annot { Kind = callout ? "Callout" : "Text", El = el, Colour = c, Recolour = recolour, Text = tb };
            el.Tag = annot;
            tb.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape || (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0))
                {
                    CommitText();
                    e.Handled = true;
                }
            };
            _editing = annot;
            annot.El.Uid = "new";
            Dispatcher.BeginInvoke(new Action(() => { tb.Focus(); Keyboard.Focus(tb); }), System.Windows.Threading.DispatcherPriority.Input);
        }

        private void BeginEdit(Annot a)
        {
            if (a.Text == null) return;
            CommitText();
            _editing = a;
            a.Text.IsReadOnly = false;
            a.Text.Focusable = true;
            a.Text.Cursor = Cursors.IBeam;
            a.El.Uid = "edit:" + a.Text.Text;
            Dispatcher.BeginInvoke(new Action(() => { a.Text.Focus(); a.Text.CaretIndex = a.Text.Text.Length; }), System.Windows.Threading.DispatcherPriority.Input);
        }

        private void CommitText()
        {
            if (_editing == null) return;
            var a = _editing;
            _editing = null;
            var tb = a.Text;
            bool isNew = a.El.Uid == "new";
            string before = a.El.Uid.StartsWith("edit:") ? a.El.Uid.Substring(5) : "";
            a.El.Uid = "";
            tb.IsReadOnly = true;
            tb.Focusable = false;
            tb.Cursor = Cursors.Arrow;
            Keyboard.ClearFocus();
            Focus();
            if (string.IsNullOrWhiteSpace(tb.Text))
            {
                if (isNew) { _canvas.Children.Remove(a.El); return; }
                // Emptied an existing note: treat it as a delete.
                tb.Text = before;
                Select(a);
                DeleteSelected();
                return;
            }
            if (isNew)
            {
                // Already on the canvas: record the add without adding it again.
                Record(() => { if (_selected == a) Select(null); _canvas.Children.Remove(a.El); }, () => { if (!_canvas.Children.Contains(a.El)) _canvas.Children.Add(a.El); });
            }
            else if (before != tb.Text)
            {
                string after = tb.Text;
                Record(() => tb.Text = before, () => tb.Text = after);
            }
        }

        private void AddStep(Point p)
        {
            int number = _canvas.Children.OfType<FrameworkElement>().Count(e => (e.Tag as Annot)?.Kind == "Steps") + 1;
            double d = Math.Max(26, _u * 13 * (0.6 + S.FontSize / 10.0));
            var c = _colour;
            var g = new Grid { Width = d, Height = d };
            var circle = new Ellipse { Fill = Theme.Brush(c), Stroke = Brushes.White, StrokeThickness = Math.Max(2, _u * 0.8) };
            bool dark = (c.R * 0.299 + c.G * 0.587 + c.B * 0.114) > 160;
            var t = new TextBlock
            {
                Text = number.ToString(),
                Foreground = dark ? Theme.Bg : Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = d * 0.5,
                FontFamily = Theme.Display,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            g.Children.Add(circle);
            g.Children.Add(t);
            Canvas.SetLeft(g, p.X - d / 2);
            Canvas.SetTop(g, p.Y - d / 2);
            AddShadow(g);
            AddAnnot(new Annot
            {
                Kind = "Steps",
                El = g,
                Colour = c,
                Recolour = col =>
                {
                    circle.Fill = Theme.Brush(col);
                    t.Foreground = (col.R * 0.299 + col.G * 0.587 + col.B * 0.114) > 160 ? Theme.Bg : Brushes.White;
                }
            });
        }

        private void PickFromImage(Point p)
        {
            int x = (int)Math.Min(_w - 1, Math.Max(0, p.X)), y = (int)Math.Min(_h - 1, Math.Max(0, p.Y));
            var px = new byte[4];
            new CroppedBitmap(new FormatConvertedBitmap(_base, PixelFormats.Bgra32, null, 0), new Int32Rect(x, y, 1, 1)).CopyPixels(px, 4, 0);
            var c = Color.FromRgb(px[2], px[1], px[0]);
            _colour = c;
            S.Colour = c.ToString();
            Settings.Save();
            HighlightSwatch();
            string hex = "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
            try { Clipboard.SetText(hex); } catch { }
            _status.Text = "Colour set to " + hex + " (also copied as text).";
        }

        // =====================================================================
        // Annotations, selection and history
        // =====================================================================
        private void AddAnnot(Annot a)
        {
            a.El.Tag = a;
            a.El.RenderTransform = new TranslateTransform();
            Do(() => { if (!_canvas.Children.Contains(a.El)) _canvas.Children.Add(a.El); },
               () => { if (_selected == a) Select(null); _canvas.Children.Remove(a.El); });
        }

        private Annot HitAnnot(DependencyObject d)
        {
            while (d != null && d != _canvas)
            {
                if (d is FrameworkElement fe && fe.Tag is Annot a && _canvas.Children.Contains(fe)) return a;
                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return null;
        }

        private static bool IsInside(DependencyObject d, DependencyObject target)
        {
            while (d != null)
            {
                if (d == target) return true;
                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return false;
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

        private void Select(Annot a)
        {
            _selected = a;
            UpdateSelectionBox();
        }

        private void UpdateSelectionBox()
        {
            if (_selected == null || !_canvas.Children.Contains(_selected.El)) { _selBox.Visibility = Visibility.Collapsed; return; }
            try
            {
                _canvas.UpdateLayout();
                var bounds = VisualTreeHelper.GetDescendantBounds(_selected.El);
                var r = _selected.El.TransformToAncestor(_canvas).TransformBounds(bounds);
                double pad = 4 * _u;
                r.Inflate(pad, pad);
                Canvas.SetLeft(_selBox, r.X);
                Canvas.SetTop(_selBox, r.Y);
                _selBox.Width = Math.Max(1, r.Width);
                _selBox.Height = Math.Max(1, r.Height);
                _selBox.Visibility = Visibility.Visible;
            }
            catch { _selBox.Visibility = Visibility.Collapsed; }
        }

        private void DeleteSelected()
        {
            if (_selected == null) return;
            var a = _selected;
            Do(() => { if (_selected == a) Select(null); _canvas.Children.Remove(a.El); },
               () => { if (!_canvas.Children.Contains(a.El)) _canvas.Children.Add(a.El); });
        }

        private void Do(Action redo, Action undo)
        {
            redo();
            Record(undo, redo);
        }

        private void Record(Action undo, Action redo)
        {
            _undo.Push((undo, redo));
            _redo.Clear();
            UpdateHistoryButtons();
        }

        private void Undo()
        {
            CommitText();
            if (_undo.Count == 0) return;
            var step = _undo.Pop();
            step.undo();
            _redo.Push(step);
            UpdateSelectionBox();
            UpdateHistoryButtons();
        }

        private void Redo()
        {
            CommitText();
            if (_redo.Count == 0) return;
            var step = _redo.Pop();
            step.redo();
            _undo.Push(step);
            UpdateSelectionBox();
            UpdateHistoryButtons();
        }

        private void UpdateHistoryButtons()
        {
            if (_undoBtn != null) _undoBtn.IsEnabled = _undo.Count > 0;
            if (_redoBtn != null) _redoBtn.IsEnabled = _redo.Count > 0;
        }

        // =====================================================================
        // Image operations (crop, rotate, effects)
        // =====================================================================
        private void SetBase(BitmapSource b)
        {
            _base = b;
            _w = b.PixelWidth;
            _h = b.PixelHeight;
            _img.Source = b;
            _img.Width = _canvas.Width = _adorn.Width = _content.Width = _w;
            _img.Height = _canvas.Height = _adorn.Height = _content.Height = _h;
            ApplyFrame();
            UpdateSize();
        }

        /// <summary>The screenshot with everything drawn on it, without the frame.</summary>
        private BitmapSource Flatten()
        {
            CommitText();
            var sel = _selBox.Visibility;
            _selBox.Visibility = Visibility.Collapsed;
            var clip = _content.Clip;
            _content.Clip = null;
            _content.UpdateLayout();
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
                dc.DrawRectangle(new VisualBrush(_content) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, _w, _h));
            var flat = CaptureImaging.ToBgra(CaptureImaging.Render(dv, _w, _h));
            _content.Clip = clip;
            _selBox.Visibility = sel;
            return flat;
        }

        private void ReplaceBase(BitmapSource next)
        {
            var oldBase = _base;
            var oldAnnots = _canvas.Children.OfType<FrameworkElement>().ToList();
            Select(null);
            Do(() =>
            {
                _canvas.Children.Clear();
                SetBase(next);
            }, () =>
            {
                _canvas.Children.Clear();
                foreach (var el in oldAnnots) _canvas.Children.Add(el);
                SetBase(oldBase);
            });
        }

        private void ApplyImageOp(string name, Func<BitmapSource, BitmapSource> op)
        {
            try
            {
                Cursor = Cursors.Wait;
                ReplaceBase(CaptureImaging.ToBgra(op(Flatten())));
                _status.Text = name + " done. Ctrl+Z to undo.";
            }
            catch (Exception ex)
            {
                Log.Write("Image op failed: " + ex);
                _status.Text = "Couldn't do that: " + ex.Message;
            }
            finally { Cursor = null; }
        }

        private void Crop(Rect r)
        {
            int x = (int)Math.Max(0, Math.Round(r.X)), y = (int)Math.Max(0, Math.Round(r.Y));
            int w = (int)Math.Min(_w - x, Math.Round(r.Width)), h = (int)Math.Min(_h - y, Math.Round(r.Height));
            if (w < 2 || h < 2) return;
            var flat = Flatten();
            var cropped = new WriteableBitmap(new CroppedBitmap(flat, new Int32Rect(x, y, w, h)));
            cropped.Freeze();
            ReplaceBase(cropped);
            SetTool("Select");
            _status.Text = "Cropped to " + w + " × " + h + ". Ctrl+Z to undo.";
        }

        private void ResizeDialog()
        {
            var dlg = new Window
            {
                Title = "Resize",
                Width = 340,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = Theme.Bg,
                ResizeMode = ResizeMode.NoResize,
                FontFamily = Theme.Font
            };
            var options = new[] { "25%", "50%", "75%", "125%", "150%", "200%" };
            var combo = UI.Combo(options, "50%", v => { });
            var preview = UI.Text("", 13, Theme.Muted);
            void Update()
            {
                double f = double.Parse(((string)combo.SelectedItem).TrimEnd('%')) / 100.0;
                preview.Text = _w + " × " + _h + "  →  " + (int)Math.Round(_w * f) + " × " + (int)Math.Round(_h * f);
            }
            combo.SelectionChanged += (s, e) => Update();
            Update();
            var ok = FlatButton.Primary("Resize", () =>
            {
                double f = double.Parse(((string)combo.SelectedItem).TrimEnd('%')) / 100.0;
                dlg.Close();
                ApplyImageOp("Resize", b => CaptureImaging.Resize(b, f));
            });
            dlg.Content = new Border { Padding = new Thickness(24), Child = UI.V(14, UI.Text("New size", 15, Theme.Text, FontWeights.SemiBold), combo, preview, ok) };
            dlg.SourceInitialized += (s, e) => Native.UseDarkTitleBar(new WindowInteropHelper(dlg).Handle);
            dlg.ShowDialog();
        }

        // =====================================================================
        // Frame and zoom
        // =====================================================================
        private void ApplyFrame()
        {
            var brush = Frames.FirstOrDefault(f => f.name == S.Frame).brush;
            bool none = brush == null;
            double scale = Math.Max(1, _w / 1200.0);
            _frame.Background = brush ?? Brushes.Transparent;
            _frame.Padding = none ? new Thickness(0) : new Thickness(S.Padding * scale);
            var radius = none ? 0 : S.Corners * scale;
            _shadowCard.CornerRadius = new CornerRadius(radius);
            _shadowCard.Effect = !none && S.Shadow ? new DropShadowEffect { BlurRadius = 40 * scale, ShadowDepth = 12 * scale, Direction = 270, Opacity = 0.4, Color = Colors.Black } : null;
            _shadowCard.Visibility = none ? Visibility.Collapsed : Visibility.Visible;
            _content.Clip = radius > 0 ? new RectangleGeometry(new Rect(0, 0, _w, _h), radius, radius) : null;
            UpdateSize();
        }

        private void SetZoom(double z)
        {
            z = Math.Max(0.05, Math.Min(8, z));
            _zoom.ScaleX = _zoom.ScaleY = z;
            _zoomText.Text = Math.Round(z * 100) + "%";
        }

        private void FitZoom()
        {
            _frame.UpdateLayout();
            double fw = _frame.ActualWidth, fh = _frame.ActualHeight;
            double vw = Math.Max(100, _scroll.ViewportWidth - 64), vh = Math.Max(100, _scroll.ViewportHeight - 64);
            if (fw <= 0 || fh <= 0) return;
            SetZoom(Math.Min(1, Math.Min(vw / fw, vh / fh)));
        }

        private void UpdateSize()
        {
            if (_sizeText != null) _sizeText.Text = _w + " × " + _h;
        }

        // =====================================================================
        // Text recognition
        // =====================================================================
        private static readonly Regex Email = new Regex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.Compiled);

        private async Task BlurEmails(bool manual)
        {
            try
            {
                if (manual) _status.Text = "Looking for email addresses...";
                var (result, scale) = await CaptureImaging.Recognise(_base);
                if (result == null) { if (manual) _status.Text = "Text recognition isn't available on this PC."; return; }
                int found = 0;
                foreach (var line in result.Lines)
                {
                    foreach (var word in line.Words)
                    {
                        if (!Email.IsMatch(word.Text)) continue;
                        var br = word.BoundingRect;
                        var rect = new Rect(br.X / scale - 4, br.Y / scale - 4, br.Width / scale + 8, br.Height / scale + 8);
                        var el = Pixelate(rect);
                        if (el == null) continue;
                        AddAnnot(new Annot { Kind = "Pixelate", El = el, Colour = _colour, Recolour = c => { } });
                        found++;
                    }
                }
                if (manual || found > 0)
                    _status.Text = found == 0 ? "No email addresses found." : "Blurred " + found + (found == 1 ? " email address." : " email addresses.") + " Ctrl+Z to undo.";
            }
            catch (Exception ex)
            {
                Log.Write("OCR failed: " + ex.Message);
                if (manual) _status.Text = "Couldn't scan the picture.";
            }
        }

        private async Task CopyText()
        {
            try
            {
                _status.Text = "Reading the text...";
                string text = await CaptureImaging.ReadText(Flatten());
                if (text == null) { _status.Text = "Text recognition isn't available on this PC."; return; }
                if (text.Length == 0) { _status.Text = "No text found in the picture."; return; }
                Clipboard.SetText(text);
                int lines = text.Split('\n').Length;
                _status.Text = "Copied " + lines + (lines == 1 ? " line" : " lines") + " of text.";
            }
            catch (Exception ex)
            {
                Log.Write("Copy text failed: " + ex.Message);
                _status.Text = "Couldn't read the text.";
            }
        }

        // =====================================================================
        // Output
        // =====================================================================
        /// <summary>The final picture, including the frame.</summary>
        private BitmapSource Render()
        {
            CommitText();
            var sel = _selBox.Visibility;
            _selBox.Visibility = Visibility.Collapsed;
            _frame.UpdateLayout();
            double w = _frame.ActualWidth, h = _frame.ActualHeight;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
                dc.DrawRectangle(new VisualBrush(_frame) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, w, h));
            var bmp = CaptureImaging.Render(dv, (int)Math.Ceiling(w), (int)Math.Ceiling(h));
            _selBox.Visibility = sel;
            return bmp;
        }

        private void Copy()
        {
            try { Clipboard.SetImage(Render()); _status.Text = "Copied to the clipboard."; }
            catch (Exception ex) { _status.Text = "Couldn't copy: " + ex.Message; }
        }

        private void QuickSave()
        {
            try
            {
                _lastSaved = CaptureImaging.QuickSave(Render(), _title);
                _status.Text = "Saved " + System.IO.Path.GetFileName(_lastSaved) + ". Click here to show it.";
            }
            catch (Exception ex) { _status.Text = "Couldn't save: " + ex.Message; }
        }

        private void SaveAs()
        {
            try
            {
                string dir = CaptureImaging.SaveFolder();
                Directory.CreateDirectory(dir);
                var formats = new[] { "png", "jpg", "bmp", "gif", "tiff" };
                int idx = Math.Max(0, Array.IndexOf(formats, S.Format)) + 1;
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    InitialDirectory = dir,
                    FileName = CaptureImaging.FileNameFromPattern(S.FilePattern, _title),
                    Filter = "PNG image (*.png)|*.png|JPG image (*.jpg)|*.jpg|BMP image (*.bmp)|*.bmp|GIF image (*.gif)|*.gif|TIFF image (*.tiff)|*.tiff",
                    FilterIndex = idx,
                    AddExtension = true
                };
                if (dlg.ShowDialog(this) != true) return;
                CaptureImaging.SaveTo(Render(), dlg.FileName);
                _lastSaved = dlg.FileName;
                _status.Text = "Saved " + System.IO.Path.GetFileName(_lastSaved) + ". Click here to show it.";
            }
            catch (Exception ex) { _status.Text = "Couldn't save: " + ex.Message; }
        }

        private string TempFile()
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Arrakis");
            Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, CaptureImaging.FileNameFromPattern(S.FilePattern, _title) + ".png");
            CaptureImaging.SaveTo(Render(), path);
            return path;
        }

        private void OpenInPaint()
        {
            try { Process.Start(new ProcessStartInfo("mspaint.exe", "\"" + TempFile() + "\"") { UseShellExecute = true }); }
            catch (Exception ex) { _status.Text = "Couldn't open Paint: " + ex.Message; }
        }

        private void OpenWith()
        {
            try { Process.Start(new ProcessStartInfo("rundll32.exe", "shell32.dll,OpenAs_RunDLL " + TempFile()) { UseShellExecute = false }); }
            catch (Exception ex) { _status.Text = "Couldn't open: " + ex.Message; }
        }

        private void Print()
        {
            try
            {
                var pd = new PrintDialog();
                if (pd.ShowDialog() != true) return;
                var bmp = Render();
                double pw = pd.PrintableAreaWidth, ph = pd.PrintableAreaHeight;
                double margin = 48;
                var img = new Image { Source = bmp, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Width = pw - margin * 2, Height = ph - margin * 2 };
                var page = new Grid { Width = pw, Height = ph, Background = Brushes.White };
                img.HorizontalAlignment = HorizontalAlignment.Center;
                img.VerticalAlignment = VerticalAlignment.Top;
                img.Margin = new Thickness(margin);
                page.Children.Add(img);
                page.Measure(new Size(pw, ph));
                page.Arrange(new Rect(0, 0, pw, ph));
                pd.PrintVisual(page, "Arrakis screenshot");
                _status.Text = "Sent to the printer.";
            }
            catch (Exception ex) { _status.Text = "Couldn't print: " + ex.Message; }
        }

        private void Pin()
        {
            var pin = new PinWindow(Render());
            pin.Show();
        }

        // =====================================================================
        // Keyboard
        // =====================================================================
        private void OnKey(object sender, KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            bool typing = _editing != null || Keyboard.FocusedElement is TextBox tb && !tb.IsReadOnly || Keyboard.FocusedElement is ComboBox;

            if (ctrl)
            {
                switch (e.Key)
                {
                    case Key.Z: if (!typing) { Undo(); e.Handled = true; } return;
                    case Key.Y: if (!typing) { Redo(); e.Handled = true; } return;
                    case Key.C: if (!typing) { Copy(); e.Handled = true; } return;
                    case Key.S: if (shift) SaveAs(); else QuickSave(); e.Handled = true; return;
                    case Key.P: Print(); e.Handled = true; return;
                    case Key.D0: case Key.NumPad0: FitZoom(); e.Handled = true; return;
                    case Key.D1: case Key.NumPad1: SetZoom(1); e.Handled = true; return;
                    case Key.OemPlus: case Key.Add: SetZoom(_zoom.ScaleX * 1.25); e.Handled = true; return;
                    case Key.OemMinus: case Key.Subtract: SetZoom(_zoom.ScaleX / 1.25); e.Handled = true; return;
                }
                return;
            }
            if (typing) return;
            if (e.Key == Key.Escape)
            {
                if (_selected != null) Select(null);
                else Close();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Delete || e.Key == Key.Back) { DeleteSelected(); e.Handled = true; return; }
            if (_selected != null && (e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Up || e.Key == Key.Down))
            {
                var a = _selected;
                double step = shift ? 10 * _u : _u;
                double dx = e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0;
                double dy = e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0;
                Do(() => { var t = Translate(a.El); t.X += dx; t.Y += dy; UpdateSelectionBox(); },
                   () => { var t = Translate(a.El); t.X -= dx; t.Y -= dy; UpdateSelectionBox(); });
                e.Handled = true;
                return;
            }
            foreach (var t in Tools)
            {
                if (t.key == e.Key) { SetTool(t.id); e.Handled = true; return; }
            }
        }
    }

    /// <summary>A screenshot that floats above everything. Drag to move, scroll to resize, double-click or Esc to close.</summary>
    public class PinWindow : Window
    {
        private readonly Image _img;

        public PinWindow(BitmapSource bmp)
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            double maxW = SystemParameters.WorkArea.Width * 0.6;
            double w = bmp.PixelWidth;
            double scale = w > maxW ? maxW / w : 1;
            _img = new Image { Source = bmp, Width = w * scale, Stretch = Stretch.Uniform };
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
                copy.Click += (a, b) => Clipboard.SetImage(bmp);
                var edit = new MenuItem { Header = "Edit" };
                edit.Click += (a, b) => { new CaptureEditor(bmp, "Pinned").Show(); Close(); };
                var close = new MenuItem { Header = "Close" };
                close.Click += (a, b) => Close();
                menu.Items.Add(copy);
                menu.Items.Add(edit);
                menu.Items.Add(close);
                menu.IsOpen = true;
            };
        }
    }
}

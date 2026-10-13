using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Arrakis.Core
{
    /// <summary>The Arrakis colour palette. Light or dark and the accent colour are read once at start-up.</summary>
    public static class Theme
    {
        public static readonly bool IsLight = ResolveLight();
        public static readonly Color AccentColor = ResolveAccent();

        public static readonly string[] AccentNames = { "Amber", "Teal", "Blue", "Pink", "Green", "Purple" };

        public static Color AccentFor(string name) => name switch
        {
            "Teal" => Color.FromRgb(0x3F, 0xC4, 0xB2),
            "Blue" => Color.FromRgb(0x5B, 0x8C, 0xFF),
            "Pink" => Color.FromRgb(0xF0, 0x6A, 0x9A),
            "Green" => Color.FromRgb(0x7C, 0xC8, 0x5A),
            "Purple" => Color.FromRgb(0xA0, 0x7C, 0xF0),
            _ => Color.FromRgb(0xFF, 0xB5, 0x47)
        };

        private static bool ResolveLight()
        {
            string t = "Dark";
            try { t = Settings.Current?.Theme ?? "Dark"; } catch { }
            if (t == "Light") return true;
            if (t == "System")
            {
                try
                {
                    using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                    return k?.GetValue("AppsUseLightTheme") is int v && v == 1;
                }
                catch { }
            }
            return false;
        }

        private static Color ResolveAccent()
        {
            try { return AccentFor(Settings.Current?.Accent ?? "Amber"); } catch { return AccentFor("Amber"); }
        }

        public static readonly Color BgColor = IsLight ? Color.FromRgb(0xF6, 0xF5, 0xF2) : Color.FromRgb(0x0F, 0x11, 0x15);
        public static readonly Color PanelColor = IsLight ? Color.FromRgb(0xFF, 0xFF, 0xFF) : Color.FromRgb(0x18, 0x1B, 0x22);
        public static readonly Color SideColor = IsLight ? Color.FromRgb(0xEE, 0xEC, 0xE8) : Color.FromRgb(0x14, 0x17, 0x1D);
        public static readonly Color TextColor = IsLight ? Color.FromRgb(0x17, 0x19, 0x1F) : Color.FromRgb(0xF3, 0xF1, 0xEC);
        public static readonly Color MutedColor = IsLight ? Color.FromRgb(0x5B, 0x60, 0x6B) : Color.FromRgb(0xA7, 0xAB, 0xB5);
        public static readonly Color AmberColor = AccentColor;
        public static readonly Color TealColor = IsLight ? Color.FromRgb(0x1F, 0xA3, 0x92) : Color.FromRgb(0x5E, 0xD3, 0xC3);
        public static readonly Color GreenColor = IsLight ? Color.FromRgb(0x4E, 0xA0, 0x3A) : Color.FromRgb(0x8F, 0xD1, 0x6A);
        public static readonly Color RedColor = Color.FromRgb(0xE2, 0x58, 0x5F);

        public static readonly SolidColorBrush Bg = Freeze(new SolidColorBrush(BgColor));
        public static readonly SolidColorBrush Panel = Freeze(new SolidColorBrush(PanelColor));
        public static readonly SolidColorBrush Side = Freeze(new SolidColorBrush(SideColor));
        public static readonly SolidColorBrush Text = Freeze(new SolidColorBrush(TextColor));
        public static readonly SolidColorBrush Muted = Freeze(new SolidColorBrush(MutedColor));
        public static readonly SolidColorBrush Amber = Freeze(new SolidColorBrush(AmberColor));
        public static readonly SolidColorBrush Teal = Freeze(new SolidColorBrush(TealColor));
        public static readonly SolidColorBrush Green = Freeze(new SolidColorBrush(GreenColor));
        public static readonly SolidColorBrush Red = Freeze(new SolidColorBrush(RedColor));
        public static readonly SolidColorBrush Dark = Freeze(new SolidColorBrush(Color.FromRgb(0x1A, 0x12, 0x06)));
        public static readonly SolidColorBrush Line = Freeze(new SolidColorBrush(IsLight ? Color.FromArgb(0x1A, 0, 0, 0) : Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)));
        public static readonly SolidColorBrush Subtle = Freeze(new SolidColorBrush(IsLight ? Color.FromArgb(0x0D, 0, 0, 0) : Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF)));
        public static readonly SolidColorBrush Off = Freeze(new SolidColorBrush(IsLight ? Color.FromRgb(0xC4, 0xC8, 0xD0) : Color.FromRgb(0x3A, 0x3F, 0x4B)));
        public static readonly SolidColorBrush Glass = Freeze(new SolidColorBrush(IsLight ? Color.FromArgb(0xEB, 0xF8, 0xF7, 0xF4) : Color.FromArgb(0xD8, 0x12, 0x14, 0x1A)));
        public static readonly SolidColorBrush Popup = Freeze(new SolidColorBrush(IsLight ? Color.FromArgb(0xF7, 0xFB, 0xFA, 0xF8) : Color.FromArgb(0xF2, 0x14, 0x16, 0x1C)));
        public static readonly SolidColorBrush GlassBorder = Freeze(new SolidColorBrush(IsLight ? Color.FromArgb(0x24, 0, 0, 0) : Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF)));
        public static readonly SolidColorBrush AmberTint = Freeze(new SolidColorBrush(Color.FromArgb(IsLight ? (byte)0x30 : (byte)0x29, AccentColor.R, AccentColor.G, AccentColor.B)));
        public static readonly SolidColorBrush TealTint = Freeze(new SolidColorBrush(Color.FromArgb(0x29, 0x5E, 0xD3, 0xC3)));
        public static readonly SolidColorBrush Stage = Freeze(new SolidColorBrush(IsLight ? Color.FromRgb(0xE4, 0xE2, 0xDE) : Color.FromRgb(0x0B, 0x0D, 0x11)));
        public static readonly SolidColorBrush Warm = Freeze(new SolidColorBrush(IsLight ? Color.FromRgb(0xFF, 0xF4, 0xE3) : Color.FromRgb(0x1E, 0x1A, 0x12)));

        public static readonly FontFamily Font = new FontFamily("Segoe UI Variable Text, Segoe UI");
        public static readonly FontFamily Display = new FontFamily("Segoe UI Variable Display, Segoe UI");

        private static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

        public static SolidColorBrush Brush(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        public static SolidColorBrush Brush(string hex)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            return Brush(c);
        }

        public static DropShadowEffect Shadow(double blur = 30, double opacity = 0.35, double depth = 10)
        {
            return new DropShadowEffect { BlurRadius = blur, Opacity = opacity, ShadowDepth = depth, Direction = 270, Color = Colors.Black };
        }
    }

    /// <summary>Small factory helpers so the code-built UI stays readable.</summary>
    public static class UI
    {
        public static TextBlock Text(string text, double size = 14, Brush fg = null, FontWeight? weight = null, bool display = false)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = size,
                Foreground = fg ?? Theme.Text,
                FontWeight = weight ?? FontWeights.Normal,
                FontFamily = display ? Theme.Display : Theme.Font,
                TextWrapping = TextWrapping.Wrap
            };
        }

        public static TextBlock Label(string text)
        {
            var t = Text(text.ToUpperInvariant(), 11, Theme.Muted, FontWeights.SemiBold);
            t.TextWrapping = TextWrapping.NoWrap;
            return t;
        }

        public static Border Card(UIElement child, double padding = 20, Brush bg = null, double radius = 20)
        {
            return new Border
            {
                Background = bg ?? Theme.Panel,
                BorderBrush = Theme.Line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(radius),
                Padding = new Thickness(padding),
                Child = child
            };
        }

        public static StackPanel Stack(Orientation o, double gap, params UIElement[] children)
        {
            var sp = new GapStack { Orientation = o, Gap = gap };
            foreach (var c in children) if (c != null) sp.Children.Add(c);
            return sp;
        }

        public static StackPanel V(double gap, params UIElement[] children) => Stack(Orientation.Vertical, gap, children);

        public static StackPanel H(double gap, params UIElement[] children)
        {
            var s = Stack(Orientation.Horizontal, gap, children);
            return s;
        }

        public static Path Icon(string data, double size = 18, Brush stroke = null, double thickness = 1.8)
        {
            var p = new Path
            {
                Data = Geometry.Parse(data),
                Stroke = stroke ?? Theme.Text,
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform
            };
            return p;
        }

        /// <summary>A 24x24-grid icon drawn at the given size, keeping the stroke weight right.</summary>
        public static FrameworkElement GridIcon(string data, double size = 18, Brush stroke = null, double thickness = 1.8)
        {
            var path = new Path
            {
                Data = Geometry.Parse(data),
                Stroke = stroke ?? Theme.Text,
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round
            };
            var canvas = new Canvas { Width = 24, Height = 24 };
            canvas.Children.Add(path);
            return new Viewbox { Width = size, Height = size, Child = canvas, Stretch = Stretch.Uniform };
        }

        public static Border Divider() => new Border { Height = 1, Background = Theme.Line, Margin = new Thickness(0, 4, 0, 4) };

        public static Border Kbd(string text)
        {
            return new Border
            {
                Background = Theme.Subtle,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 3, 8, 3),
                Child = Text(text, 12, Theme.Text, FontWeights.SemiBold),
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        /// <summary>A setting row: title, optional description and a control on the right.</summary>
        public static Grid SettingRow(string title, string description, UIElement control)
        {
            var g = new Grid { MinHeight = 56, Margin = new Thickness(0, 2, 0, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = V(2, Text(title, 14, Theme.Text, FontWeights.SemiBold),
                string.IsNullOrEmpty(description) ? null : Text(description, 12, Theme.Muted));
            left.VerticalAlignment = VerticalAlignment.Center;
            left.Margin = new Thickness(0, 6, 16, 6);
            g.Children.Add(left);
            if (control is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, 1);
            g.Children.Add(control);
            return g;
        }

        public static ComboBox Combo(string[] options, string selected, Action<string> changed)
        {
            var cb = new ComboBox { MinWidth = 160, Height = 36, FontSize = 14, VerticalContentAlignment = VerticalAlignment.Center };
            foreach (var o in options) cb.Items.Add(o);
            cb.SelectedItem = Array.IndexOf(options, selected) >= 0 ? selected : options[0];
            cb.SelectionChanged += (s, e) => changed(cb.SelectedItem as string);
            return cb;
        }

        public static TextBox TextInput(string value, double width = 220)
        {
            return new TextBox
            {
                Text = value ?? "",
                Width = width,
                Height = 36,
                FontSize = 14,
                Padding = new Thickness(10, 0, 10, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = Theme.Subtle,
                Foreground = Theme.Text,
                CaretBrush = Theme.Text,
                BorderBrush = Theme.Line,
                BorderThickness = new Thickness(1)
            };
        }
    }

    /// <summary>StackPanel with a gap between children.</summary>
    public class GapStack : StackPanel
    {
        public double Gap { get; set; }

        protected override void OnVisualChildrenChanged(DependencyObject visualAdded, DependencyObject visualRemoved)
        {
            base.OnVisualChildrenChanged(visualAdded, visualRemoved);
            ApplyGap();
        }

        private void ApplyGap()
        {
            for (int i = 0; i < Children.Count; i++)
            {
                if (!(Children[i] is FrameworkElement fe)) continue;
                var m = fe.Margin;
                double g = i == 0 ? 0 : Gap;
                fe.Margin = Orientation == Orientation.Vertical
                    ? new Thickness(m.Left, g, m.Right, m.Bottom)
                    : new Thickness(g, m.Top, m.Right, m.Bottom);
            }
        }
    }

    /// <summary>A flat button with rounded corners and a soft hover.</summary>
    public class FlatButton : Button
    {
        public static readonly DependencyProperty CornerRadiusProperty =
            DependencyProperty.Register(nameof(CornerRadius), typeof(CornerRadius), typeof(FlatButton), new PropertyMetadata(new CornerRadius(12)));

        public CornerRadius CornerRadius
        {
            get => (CornerRadius)GetValue(CornerRadiusProperty);
            set => SetValue(CornerRadiusProperty, value);
        }

        private static ControlTemplate _template;

        private static ControlTemplate BuildTemplate()
        {
            const string xaml =
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
                "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>" +
                "<Border x:Name='b' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' " +
                "BorderThickness='{TemplateBinding BorderThickness}' " +
                "CornerRadius='{Binding CornerRadius, RelativeSource={RelativeSource TemplatedParent}}' SnapsToDevicePixels='True'>" +
                "<ContentPresenter Margin='{TemplateBinding Padding}' HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' " +
                "VerticalAlignment='{TemplateBinding VerticalContentAlignment}' RecognizesAccessKey='False'/>" +
                "</Border>" +
                "<ControlTemplate.Triggers>" +
                "<Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Opacity' Value='0.86'/></Trigger>" +
                "<Trigger Property='IsPressed' Value='True'><Setter TargetName='b' Property='Opacity' Value='0.7'/></Trigger>" +
                "<Trigger Property='IsEnabled' Value='False'><Setter TargetName='b' Property='Opacity' Value='0.4'/></Trigger>" +
                "<Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='b' Property='BorderBrush' Value='#FFB547'/></Trigger>" +
                "</ControlTemplate.Triggers>" +
                "</ControlTemplate>";
            return (ControlTemplate)XamlReader.Parse(xaml);
        }

        public FlatButton()
        {
            if (_template == null) _template = BuildTemplate();
            Template = _template;
            Background = Theme.Subtle;
            Foreground = Theme.Text;
            BorderBrush = Brushes.Transparent;
            BorderThickness = new Thickness(1);
            Padding = new Thickness(16, 0, 16, 0);
            MinHeight = 40;
            FontSize = 14;
            FontWeight = FontWeights.SemiBold;
            FontFamily = Theme.Font;
            Cursor = Cursors.Hand;
            HorizontalContentAlignment = HorizontalAlignment.Center;
            VerticalContentAlignment = VerticalAlignment.Center;
            FocusVisualStyle = null;
        }

        public static FlatButton Primary(string text, Action onClick = null)
        {
            var b = new FlatButton { Content = text, Background = Theme.Amber, Foreground = Theme.Dark, MinHeight = 44, Padding = new Thickness(20, 0, 20, 0) };
            if (onClick != null) b.Click += (s, e) => onClick();
            return b;
        }

        public static FlatButton Secondary(string text, Action onClick = null)
        {
            var b = new FlatButton
            {
                Content = text,
                Background = Brushes.Transparent,
                BorderBrush = Theme.Brush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
                MinHeight = 44
            };
            if (onClick != null) b.Click += (s, e) => onClick();
            return b;
        }

        public static FlatButton IconButton(string iconData, string tooltip, Action onClick = null, double size = 44)
        {
            var b = new FlatButton
            {
                Content = UI.GridIcon(iconData, 18, Theme.Text),
                Width = size,
                Height = size,
                MinHeight = size,
                Padding = new Thickness(0),
                ToolTip = tooltip
            };
            SetA11yName(b, tooltip);
            if (onClick != null) b.Click += (s, e) => onClick();
            return b;
        }

        public static void SetA11yName(UIElement e, string name)
        {
            System.Windows.Automation.AutomationProperties.SetName(e, name);
        }
    }

    /// <summary>An on/off switch.</summary>
    public class ToggleSwitch : FlatButton
    {
        private readonly Border _knob;
        private bool _isOn;

        public event Action<bool> Toggled;

        public bool IsOn
        {
            get => _isOn;
            set { _isOn = value; Render(); }
        }

        public ToggleSwitch(bool on = false, string name = "Toggle")
        {
            Width = 46;
            Height = 26;
            MinHeight = 26;
            Padding = new Thickness(3);
            CornerRadius = new CornerRadius(13);
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
            _knob = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = Brushes.White };
            Content = _knob;
            SetA11yName(this, name);
            _isOn = on;
            Render();
            Click += (s, e) => { IsOn = !IsOn; Toggled?.Invoke(IsOn); };
        }

        private void Render()
        {
            Background = _isOn ? Theme.Amber : Theme.Off;
            _knob.HorizontalAlignment = _isOn ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        }
    }

    /// <summary>Circular progress ring.</summary>
    public class Ring : Grid
    {
        private readonly Path _arc;
        private readonly double _size;
        private readonly double _thickness;
        public TextBlock Center { get; }

        public Ring(double size, double thickness, Brush color, string centerText = "", double fontSize = 13)
        {
            _size = size;
            _thickness = thickness;
            Width = size;
            Height = size;
            var track = new Ellipse { Stroke = Theme.Brush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)), StrokeThickness = thickness };
            _arc = new Path { Stroke = color, StrokeThickness = thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            Center = UI.Text(centerText, fontSize, Theme.Text, FontWeights.SemiBold, true);
            Center.HorizontalAlignment = HorizontalAlignment.Center;
            Center.VerticalAlignment = VerticalAlignment.Center;
            Center.TextAlignment = TextAlignment.Center;
            Children.Add(track);
            Children.Add(_arc);
            Children.Add(Center);
        }

        public Brush ArcBrush { set => _arc.Stroke = value; }

        public void SetValue(double fraction)
        {
            fraction = Math.Max(0, Math.Min(0.9999, fraction));
            double r = (_size - _thickness) / 2;
            double cx = _size / 2, cy = _size / 2;
            if (fraction <= 0.0001) { _arc.Data = null; return; }
            double angle = fraction * 2 * Math.PI;
            var start = new Point(cx, cy - r);
            var end = new Point(cx + r * Math.Sin(angle), cy - r * Math.Cos(angle));
            var fig = new PathFigure { StartPoint = start, IsClosed = false };
            fig.Segments.Add(new ArcSegment(end, new Size(r, r), 0, fraction > 0.5, SweepDirection.Clockwise, true));
            var geo = new PathGeometry();
            geo.Figures.Add(fig);
            _arc.Data = geo;
        }
    }

    /// <summary>A simple dark slider.</summary>
    public class MiniSlider : Grid
    {
        private readonly Border _fill;
        private readonly Border _thumb;
        private readonly Border _track;
        private double _value;
        private bool _dragging;

        public double Minimum { get; set; }
        public double Maximum { get; set; } = 100;
        public double Step { get; set; } = 1;
        public event Action<double> ValueChanged;

        public double Value
        {
            get => _value;
            set { _value = Math.Max(Minimum, Math.Min(Maximum, value)); Layout(); }
        }

        public MiniSlider(double min, double max, double value, Brush color = null, double width = 220)
        {
            Minimum = min;
            Maximum = max;
            Width = width;
            Height = 28;
            Background = Brushes.Transparent;
            Cursor = Cursors.Hand;
            Focusable = true;
            _track = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = Theme.Brush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)), VerticalAlignment = VerticalAlignment.Center };
            _fill = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = color ?? Theme.Amber, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
            _thumb = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            Children.Add(_track);
            Children.Add(_fill);
            Children.Add(_thumb);
            _value = value;
            SizeChanged += (s, e) => Layout();
            MouseLeftButtonDown += (s, e) => { _dragging = true; CaptureMouse(); SetFromMouse(e.GetPosition(this).X); };
            MouseMove += (s, e) => { if (_dragging) SetFromMouse(e.GetPosition(this).X); };
            MouseLeftButtonUp += (s, e) => { _dragging = false; ReleaseMouseCapture(); };
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Left || e.Key == Key.Down) { Value -= Step; ValueChanged?.Invoke(_value); e.Handled = true; }
                if (e.Key == Key.Right || e.Key == Key.Up) { Value += Step; ValueChanged?.Invoke(_value); e.Handled = true; }
            };
        }

        private void SetFromMouse(double x)
        {
            double w = Math.Max(1, ActualWidth - 18);
            double f = Math.Max(0, Math.Min(1, (x - 9) / w));
            double v = Minimum + f * (Maximum - Minimum);
            v = Math.Round(v / Step) * Step;
            if (Math.Abs(v - _value) > 0.00001)
            {
                _value = v;
                Layout();
                ValueChanged?.Invoke(_value);
            }
        }

        private void Layout()
        {
            double w = Math.Max(1, ActualWidth - 18);
            double f = Maximum > Minimum ? (_value - Minimum) / (Maximum - Minimum) : 0;
            _fill.Width = 9 + f * w;
            _thumb.Margin = new Thickness(f * w, 0, 0, 0);
        }
    }

    /// <summary>A rounded "chip" toggle used for filters.</summary>
    public class Chip : FlatButton
    {
        private bool _selected;

        public bool Selected
        {
            get => _selected;
            set
            {
                _selected = value;
                Background = value ? Theme.Text : Brushes.Transparent;
                Foreground = value ? Theme.Bg : Theme.Text;
                BorderBrush = value ? Theme.Text : Theme.Brush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));
            }
        }

        public Chip(string text, bool selected = false)
        {
            Content = text;
            MinHeight = 34;
            Height = 34;
            CornerRadius = new CornerRadius(17);
            Padding = new Thickness(14, 0, 14, 0);
            FontSize = 13;
            Selected = selected;
        }
    }

    public static class Icons
    {
        public const string Home = "M3 11l9-7 9 7M5 10v10h14V10";
        public const string Dock = "M4 15h16a1 1 0 0 1 1 1v3a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1v-3a1 1 0 0 1 1-1zM7 11h2M11 11h2M15 11h2";
        public const string Widgets = "M4 4h7v7H4zM13 4h7v4h-7zM13 10h7v10h-7zM4 13h7v7H4z";
        public const string Wallpaper = "M3 5h18v14H3zM3 16l5-5 4 4 3-3 6 6";
        public const string Clipboard = "M9 4h6v3H9zM7 5H5v16h14V5h-2M8 12h8M8 16h5";
        public const string Capture = "M4 8V5a1 1 0 0 1 1-1h3M16 4h3a1 1 0 0 1 1 1v3M20 16v3a1 1 0 0 1-1 1h-3M8 20H5a1 1 0 0 1-1-1v-3M9 12h6M12 9v6";
        public const string Focus = "M12 3a9 9 0 1 0 0 18a9 9 0 1 0 0-18M12 7v5l3 2";
        public const string Battery = "M3 8h15a1 1 0 0 1 1 1v6a1 1 0 0 1-1 1H3zM21 11v2M6 11h6";
        public const string Settings = "M12 9a3 3 0 1 0 0 6a3 3 0 1 0 0-6M12 2v3M12 19v3M2 12h3M19 12h3M4.9 4.9l2.1 2.1M17 17l2.1 2.1M4.9 19.1L7 17M17 7l2.1-2.1";
        public const string Search = "M11 4a7 7 0 1 0 0 14a7 7 0 1 0 0-14M20 20l-4-4";
        public const string Pin = "M9 4h6M10 4v6l-3 4h10l-3-4V4M12 14v6";
        public const string Link = "M10 14a4 4 0 0 0 5.7 0l3-3a4 4 0 0 0-5.7-5.7l-1 1M14 10a4 4 0 0 0-5.7 0l-3 3a4 4 0 0 0 5.7 5.7l1-1";
        public const string Lines = "M5 6h14M5 11h14M5 16h9";
        public const string Trash = "M4 7h16M9 7V4h6v3M6 7l1 13h10l1-13";
        public const string Close = "M6 6l12 12M18 6L6 18";
        public const string Play = "M7 4l13 8-13 8z";
        public const string Pause = "M8 5v14M16 5v14";
        public const string Prev = "M19 5L9 12l10 7zM5 5v14";
        public const string Next = "M5 5l10 7-10 7zM19 5v14";
        public const string Check = "M5 12l5 5 9-10";
        public const string Plus = "M12 5v14M5 12h14";
        public const string Undo = "M9 14L4 9l5-5M4 9h11a5 5 0 0 1 0 10h-3";
        public const string Cursor = "M5 3l14 8-6 2-2 6z";
        public const string Arrow = "M5 19L19 5M9 5h10v10";
        public const string Box = "M4 6h16v12H4z";
        public const string TextTool = "M5 6V4h14v2M12 4v16M9 20h6";
        public const string Highlight = "M4 20h16M14 4l6 6-8 8H6v-6z";
        public const string Blur = "M4 4h4v4H4zM12 4h4v4h-4zM8 8h4v4H8zM16 8h4v4h-4zM4 12h4v4H4zM12 12h4v4h-4zM8 16h4v4H8zM16 16h4v4h-4z";
        public const string Steps = "M12 3a9 9 0 1 0 0 18a9 9 0 1 0 0-18M11 8h2v8";
        public const string Cloud = "M7 18h10a4 4 0 0 0 0-8a6 6 0 0 0-11.5 1.5A3.3 3.3 0 0 0 7 18z";
        public const string Sun = "M12 8a4 4 0 1 0 0 8a4 4 0 1 0 0-8M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4";
        public const string Rain = "M7 15h10a4 4 0 0 0 0-8a6 6 0 0 0-11.5 1.5A3.3 3.3 0 0 0 7 15zM8 18l-1 3M12 18l-1 3M16 18l-1 3";
        public const string Snow = "M7 15h10a4 4 0 0 0 0-8a6 6 0 0 0-11.5 1.5A3.3 3.3 0 0 0 7 15zM8 19h.01M12 19h.01M16 19h.01M10 21h.01M14 21h.01";
        public const string Storm = "M7 15h10a4 4 0 0 0 0-8a6 6 0 0 0-11.5 1.5A3.3 3.3 0 0 0 7 15zM12 15l-2 4h4l-2 3";
        public const string Fog = "M4 10h16M4 14h16M6 18h12M7 6h10";
        public const string Calendar = "M4 6h16v14H4zM4 10h16M9 3v4M15 3v4";
        public const string Clock = "M12 3a9 9 0 1 0 0 18a9 9 0 1 0 0-18M12 7v5l3 2";
        public const string Rings = "M12 4a8 8 0 1 0 8 8M12 8a4 4 0 1 0 4 4";
        public const string Music = "M9 18V5l11-2v13M9 18a3 3 0 1 1-6 0a3 3 0 1 1 6 0M20 16a3 3 0 1 1-6 0a3 3 0 1 1 6 0";
        public const string Note = "M5 4h14v16H5zM8 9h8M8 13h8M8 17h5";
        public const string Timer = "M12 5a8 8 0 1 0 0 16a8 8 0 1 0 0-16M12 9v4M9 2h6";
        public const string Volume = "M4 9v6h4l5 4V5L8 9zM16 9a4 4 0 0 1 0 6";
        public const string Folder = "M3 7a1 1 0 0 1 1-1h5l2 2h9a1 1 0 0 1 1 1v9a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1z";
        public const string Save = "M5 4h11l3 3v13H5zM8 4v5h7V4M8 14h8v6H8z";
        public const string Copy = "M8 8h12v12H8zM4 16V4h12";
        public const string Flame = "M12 3c1 4 5 5 5 10a5 5 0 0 1-10 0c0-2 1-3 2-4c0 2 1 3 2 3c0-3-1-6 1-9z";
        public const string Plug = "M9 3v5M15 3v5M6 8h12v3a6 6 0 0 1-12 0zM12 17v4";
        public const string Refresh = "M20 11a8 8 0 1 0-2.3 5.7M20 4v7h-7";
    }
}

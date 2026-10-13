using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Arrakis.Core;

namespace Arrakis.Modules
{
    /// <summary>Saving, image effects and text recognition for screenshots.</summary>
    public static class CaptureImaging
    {
        private static CaptureSettings S => Settings.Current.Capture;

        public static string SaveFolder()
        {
            var s = S.SaveFolder;
            if (!string.IsNullOrEmpty(s)) return s;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Arrakis");
        }

        public static BitmapSource ToBgra(BitmapSource src)
        {
            if (src == null) return null;
            BitmapSource b = src.Format == PixelFormats.Bgra32 || src.Format == PixelFormats.Pbgra32 ? src : new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            // Normalise to 96 DPI so pixels and layout units line up in the editor.
            if (Math.Abs(b.DpiX - 96) > 0.5 || Math.Abs(b.DpiY - 96) > 0.5)
            {
                int stride = b.PixelWidth * 4;
                var px = new byte[stride * b.PixelHeight];
                new FormatConvertedBitmap(b, PixelFormats.Bgra32, null, 0).CopyPixels(px, stride, 0);
                b = BitmapSource.Create(b.PixelWidth, b.PixelHeight, 96, 96, PixelFormats.Bgra32, null, px, stride);
            }
            if (b.CanFreeze && !b.IsFrozen) b.Freeze();
            return b;
        }

        private static readonly Regex Token = new Regex(@"\{([^}]+)\}", RegexOptions.Compiled);

        public static string FileNameFromPattern(string pattern, string title)
        {
            if (string.IsNullOrWhiteSpace(pattern)) pattern = "Screenshot {yyyy-MM-dd HH.mm.ss}";
            string name = pattern.Replace("{title}", string.IsNullOrWhiteSpace(title) ? "Screenshot" : title, StringComparison.OrdinalIgnoreCase);
            name = Token.Replace(name, m =>
            {
                try { return DateTime.Now.ToString(m.Groups[1].Value); }
                catch { return m.Value; }
            });
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '-');
            name = name.Trim();
            if (name.Length > 120) name = name.Substring(0, 120);
            return name.Length == 0 ? "Screenshot" : name;
        }

        public static BitmapEncoder EncoderFor(string ext)
        {
            switch (ext.TrimStart('.').ToLowerInvariant())
            {
                case "jpg":
                case "jpeg": return new JpegBitmapEncoder { QualityLevel = Math.Max(10, Math.Min(100, S.JpegQuality)) };
                case "bmp": return new BmpBitmapEncoder();
                case "gif": return new GifBitmapEncoder();
                case "tif":
                case "tiff": return new TiffBitmapEncoder();
                default: return new PngBitmapEncoder();
            }
        }

        public static void SaveTo(BitmapSource img, string path)
        {
            string ext = Path.GetExtension(path);
            BitmapSource toSave = img;
            // JPG and BMP have no transparency: put it on white.
            if (ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) || ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase))
                toSave = OnBackground(img, Colors.White);
            var enc = EncoderFor(ext);
            enc.Frames.Add(BitmapFrame.Create(toSave));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using var fs = File.Create(path);
            enc.Save(fs);
        }

        /// <summary>Saves to the screenshot folder with the file name pattern. Never overwrites.</summary>
        public static string QuickSave(BitmapSource img, string title)
        {
            string dir = SaveFolder();
            Directory.CreateDirectory(dir);
            string ext = string.IsNullOrEmpty(S.Format) ? "png" : S.Format.ToLowerInvariant();
            string baseName = FileNameFromPattern(S.FilePattern, title);
            string path = Path.Combine(dir, baseName + "." + ext);
            int n = 2;
            while (File.Exists(path)) path = Path.Combine(dir, baseName + " (" + n++ + ")." + ext);
            SaveTo(img, path);
            return path;
        }

        public static BitmapSource OnBackground(BitmapSource img, Color bg)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(bg), null, new Rect(0, 0, img.PixelWidth, img.PixelHeight));
                dc.DrawImage(img, new Rect(0, 0, img.PixelWidth, img.PixelHeight));
            }
            return Render(dv, img.PixelWidth, img.PixelHeight);
        }

        public static BitmapSource Render(Visual v, int w, int h)
        {
            var rtb = new RenderTargetBitmap(Math.Max(1, w), Math.Max(1, h), 96, 96, PixelFormats.Pbgra32);
            rtb.Render(v);
            rtb.Freeze();
            return rtb;
        }

        /// <summary>Lays out an element off-screen at a fixed size and renders it.</summary>
        public static BitmapSource RenderElement(FrameworkElement el, int w, int h)
        {
            el.Measure(new Size(w, h));
            el.Arrange(new Rect(0, 0, w, h));
            el.UpdateLayout();
            return Render(el, w, h);
        }

        // ---------------- effects ----------------
        public static BitmapSource Rotate(BitmapSource img, double angle)
        {
            var t = new TransformedBitmap(img, new RotateTransform(angle));
            return ToBgra(t);
        }

        public static BitmapSource Flip(BitmapSource img, bool horizontal)
        {
            var t = new TransformedBitmap(img, new ScaleTransform(horizontal ? -1 : 1, horizontal ? 1 : -1));
            return ToBgra(t);
        }

        public static BitmapSource Resize(BitmapSource img, double factor)
        {
            int w = Math.Max(1, (int)Math.Round(img.PixelWidth * factor)), h = Math.Max(1, (int)Math.Round(img.PixelHeight * factor));
            var image = new Image { Source = img, Stretch = Stretch.Fill, Width = w, Height = h };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            return RenderElement(image, w, h);
        }

        private static byte[] Pixels(BitmapSource img, out int stride)
        {
            var b = new FormatConvertedBitmap(img, PixelFormats.Bgra32, null, 0);
            stride = b.PixelWidth * 4;
            var px = new byte[stride * b.PixelHeight];
            b.CopyPixels(px, stride, 0);
            return px;
        }

        private static BitmapSource FromPixels(byte[] px, int w, int h, int stride)
        {
            var b = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, stride);
            b.Freeze();
            return b;
        }

        public static BitmapSource Grayscale(BitmapSource img)
        {
            var px = Pixels(img, out int stride);
            for (int i = 0; i < px.Length; i += 4)
            {
                byte y = (byte)Math.Min(255, 0.114 * px[i] + 0.587 * px[i + 1] + 0.299 * px[i + 2]);
                px[i] = px[i + 1] = px[i + 2] = y;
            }
            return FromPixels(px, img.PixelWidth, img.PixelHeight, stride);
        }

        public static BitmapSource Invert(BitmapSource img)
        {
            var px = Pixels(img, out int stride);
            for (int i = 0; i < px.Length; i += 4)
            {
                px[i] = (byte)(255 - px[i]);
                px[i + 1] = (byte)(255 - px[i + 1]);
                px[i + 2] = (byte)(255 - px[i + 2]);
            }
            return FromPixels(px, img.PixelWidth, img.PixelHeight, stride);
        }

        public static BitmapSource AddBorder(BitmapSource img, Color colour, int thickness)
        {
            int w = img.PixelWidth + thickness * 2, h = img.PixelHeight + thickness * 2;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(colour), null, new Rect(0, 0, w, h));
                dc.DrawImage(img, new Rect(thickness, thickness, img.PixelWidth, img.PixelHeight));
            }
            return Render(dv, w, h);
        }

        public static BitmapSource DropShadow(BitmapSource img, double unit)
        {
            int pad = (int)Math.Ceiling(28 * unit);
            int w = img.PixelWidth + pad * 2, h = img.PixelHeight + pad * 2;
            var image = new Image
            {
                Source = img,
                Width = img.PixelWidth,
                Height = img.PixelHeight,
                Effect = new DropShadowEffect { BlurRadius = 24 * unit, ShadowDepth = 6 * unit, Direction = 300, Opacity = 0.5, Color = Colors.Black }
            };
            var grid = new Grid { Width = w, Height = h, Background = Brushes.Transparent };
            image.HorizontalAlignment = HorizontalAlignment.Center;
            image.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(image);
            return RenderElement(grid, w, h);
        }

        /// <summary>A ripped-paper look: jagged edges all round, with a soft shadow.</summary>
        public static BitmapSource TornEdges(BitmapSource img, double unit)
        {
            int iw = img.PixelWidth, ih = img.PixelHeight;
            double tooth = Math.Max(8, 12 * unit), depth = Math.Max(4, 7 * unit);
            var rnd = new Random(img.PixelWidth * 31 + img.PixelHeight);
            var fig = new PathFigure { IsClosed = true, IsFilled = true };
            var pts = new List<Point>();
            for (double x = 0; x <= iw; x += tooth) pts.Add(new Point(x, rnd.NextDouble() * depth));
            for (double y = 0; y <= ih; y += tooth) pts.Add(new Point(iw - rnd.NextDouble() * depth, y));
            for (double x = iw; x >= 0; x -= tooth) pts.Add(new Point(x, ih - rnd.NextDouble() * depth));
            for (double y = ih; y >= 0; y -= tooth) pts.Add(new Point(rnd.NextDouble() * depth, y));
            fig.StartPoint = pts[0];
            foreach (var p in pts.Skip(1)) fig.Segments.Add(new LineSegment(p, true));
            var geo = new PathGeometry();
            geo.Figures.Add(fig);
            geo.Freeze();

            var image = new Image { Source = img, Width = iw, Height = ih, Clip = geo };
            var holder = new Grid { Width = iw, Height = ih, Effect = new DropShadowEffect { BlurRadius = 18 * unit, ShadowDepth = 4 * unit, Direction = 300, Opacity = 0.45, Color = Colors.Black } };
            holder.Children.Add(image);
            int pad = (int)Math.Ceiling(22 * unit);
            int w = iw + pad * 2, h = ih + pad * 2;
            var grid = new Grid { Width = w, Height = h, Background = Brushes.Transparent };
            holder.HorizontalAlignment = HorizontalAlignment.Center;
            holder.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(holder);
            return RenderElement(grid, w, h);
        }

        // ---------------- text recognition (Windows' own, offline) ----------------
        public static async Task<(Windows.Media.Ocr.OcrResult result, double scale)> Recognise(BitmapSource img)
        {
            var engine = Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine == null) return (null, 1);
            BitmapSource src = img;
            double max = Windows.Media.Ocr.OcrEngine.MaxImageDimension;
            double scale = 1;
            if (src.PixelWidth > max || src.PixelHeight > max)
            {
                scale = Math.Min(max / src.PixelWidth, max / src.PixelHeight);
                src = Resize(src, scale);
            }
            var px = Pixels(src, out int stride);
            var sb = Windows.Graphics.Imaging.SoftwareBitmap.CreateCopyFromBuffer(
                px.AsBuffer(), Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, src.PixelWidth, src.PixelHeight, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);
            return (await engine.RecognizeAsync(sb), scale);
        }

        public static async Task<string> ReadText(BitmapSource img)
        {
            var (r, _) = await Recognise(img);
            if (r == null) return null;
            var sb = new StringBuilder();
            foreach (var line in r.Lines) sb.AppendLine(line.Text);
            return sb.ToString().TrimEnd();
        }
    }
}

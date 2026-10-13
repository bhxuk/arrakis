using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;

namespace Arrakis.Core
{
    /// <summary>App-wide styles that every window picks up automatically.</summary>
    public static class AppStyles
    {
        /// <summary>Thin, rounded, see-through scroll bars that grow a little when you hover them.</summary>
        public static void Install()
        {
            try
            {
                string thumb = Theme.IsLight ? "#38000000" : "#33FFFFFF";
                string hover = Theme.IsLight ? "#66000000" : "#66FFFFFF";
                string xaml = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style x:Key='ArrakisThumb' TargetType='Thumb'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='IsTabStop' Value='False'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Thumb'>
          <Border CornerRadius='4' Background='{TemplateBinding Background}'/>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key='ArrakisPage' TargetType='RepeatButton'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='IsTabStop' Value='False'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <ControlTemplate x:Key='ArrakisVertical' TargetType='ScrollBar'>
    <Grid Background='Transparent'>
      <Track x:Name='PART_Track' IsDirectionReversed='True'>
        <Track.DecreaseRepeatButton><RepeatButton Style='{StaticResource ArrakisPage}' Command='ScrollBar.PageUpCommand'/></Track.DecreaseRepeatButton>
        <Track.IncreaseRepeatButton><RepeatButton Style='{StaticResource ArrakisPage}' Command='ScrollBar.PageDownCommand'/></Track.IncreaseRepeatButton>
        <Track.Thumb><Thumb x:Name='thumb' Style='{StaticResource ArrakisThumb}' Background='" + thumb + @"' Width='5' Margin='0,6' HorizontalAlignment='Center'/></Track.Thumb>
      </Track>
    </Grid>
    <ControlTemplate.Triggers>
      <Trigger Property='IsMouseOver' Value='True'>
        <Setter TargetName='thumb' Property='Width' Value='8'/>
        <Setter TargetName='thumb' Property='Background' Value='" + hover + @"'/>
      </Trigger>
    </ControlTemplate.Triggers>
  </ControlTemplate>
  <ControlTemplate x:Key='ArrakisHorizontal' TargetType='ScrollBar'>
    <Grid Background='Transparent'>
      <Track x:Name='PART_Track' IsDirectionReversed='False'>
        <Track.DecreaseRepeatButton><RepeatButton Style='{StaticResource ArrakisPage}' Command='ScrollBar.PageLeftCommand'/></Track.DecreaseRepeatButton>
        <Track.IncreaseRepeatButton><RepeatButton Style='{StaticResource ArrakisPage}' Command='ScrollBar.PageRightCommand'/></Track.IncreaseRepeatButton>
        <Track.Thumb><Thumb x:Name='thumb' Style='{StaticResource ArrakisThumb}' Background='" + thumb + @"' Height='5' Margin='6,0' VerticalAlignment='Center'/></Track.Thumb>
      </Track>
    </Grid>
    <ControlTemplate.Triggers>
      <Trigger Property='IsMouseOver' Value='True'>
        <Setter TargetName='thumb' Property='Height' Value='8'/>
        <Setter TargetName='thumb' Property='Background' Value='" + hover + @"'/>
      </Trigger>
    </ControlTemplate.Triggers>
  </ControlTemplate>
  <Style TargetType='ScrollBar'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Width' Value='14'/>
    <Setter Property='MinWidth' Value='14'/>
    <Setter Property='Template' Value='{StaticResource ArrakisVertical}'/>
    <Style.Triggers>
      <Trigger Property='Orientation' Value='Horizontal'>
        <Setter Property='Width' Value='Auto'/>
        <Setter Property='MinWidth' Value='0'/>
        <Setter Property='Height' Value='14'/>
        <Setter Property='MinHeight' Value='14'/>
        <Setter Property='Template' Value='{StaticResource ArrakisHorizontal}'/>
      </Trigger>
    </Style.Triggers>
  </Style>
</ResourceDictionary>";
                var dict = (ResourceDictionary)XamlReader.Parse(xaml);
                Application.Current.Resources.MergedDictionaries.Add(dict);
            }
            catch (Exception ex) { Log.Write("Styles: " + ex.Message); }
        }
    }

    /// <summary>Mouse-wheel scrolling that glides to the new position instead of jumping.</summary>
    public static class SmoothScroll
    {
        public static void Enable(ScrollViewer sv)
        {
            double target = 0;
            bool running = false;
            void Frame(object s, EventArgs e)
            {
                double cur = sv.VerticalOffset;
                double next = cur + (target - cur) * 0.22;
                if (Math.Abs(target - next) < 0.5) next = target;
                sv.ScrollToVerticalOffset(next);
                if (next == target) { CompositionTarget.Rendering -= Frame; running = false; }
            }
            sv.PreviewMouseWheel += (s, e) =>
            {
                // Let inner scrollable areas (lists, galleries) scroll themselves first.
                if (e.OriginalSource is DependencyObject d && InnerScrollerCanScroll(d, sv, e.Delta)) return;
                if (!running) target = sv.VerticalOffset;
                target = Math.Max(0, Math.Min(sv.ScrollableHeight, target - e.Delta * 0.9));
                e.Handled = true;
                if (!running) { running = true; CompositionTarget.Rendering += Frame; }
            };
            sv.ScrollChanged += (s, e) => { if (!running) target = sv.VerticalOffset; };
        }

        private static bool InnerScrollerCanScroll(DependencyObject d, ScrollViewer outer, int delta)
        {
            while (d != null && d != outer)
            {
                if (d is ScrollViewer inner && inner.ScrollableHeight > 0)
                {
                    if (delta > 0 && inner.VerticalOffset > 0) return true;
                    if (delta < 0 && inner.VerticalOffset < inner.ScrollableHeight) return true;
                }
                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return false;
        }
    }

    /// <summary>Windows 11's frosted-glass window background (acrylic), with a solid fallback elsewhere.</summary>
    public static class Glass
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Margins { public int Left, Right, Top, Bottom; }

        [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
        private const int DWMSBT_TRANSIENTWINDOW = 3;   // acrylic

        /// <summary>Windows 11 22H2 or later, and the person hasn't turned glass off.</summary>
        public static bool Available => Environment.OSVersion.Version.Build >= 22621 && Settings.Current.GlassWindow;

        /// <summary>Call from SourceInitialized. Returns false if the backdrop couldn't be applied.</summary>
        public static bool Apply(Window w)
        {
            try
            {
                var hwnd = new WindowInteropHelper(w).Handle;
                var src = HwndSource.FromHwnd(hwnd);
                if (src?.CompositionTarget == null) return false;
                src.CompositionTarget.BackgroundColor = Colors.Transparent;
                var m = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                DwmExtendFrameIntoClientArea(hwnd, ref m);
                int type = DWMSBT_TRANSIENTWINDOW;
                if (DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref type, sizeof(int)) != 0) return false;
                Native.RoundCorners(hwnd);
                return true;
            }
            catch (Exception ex) { Log.Write("Glass: " + ex.Message); return false; }
        }
    }
}

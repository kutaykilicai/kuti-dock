using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace KutiDock
{
    /// <summary>Küçük arayüz yardımcıları ve özgün çizim kontrolleri.</summary>
    internal static class Ui
    {
        public static TextBlock Text(string s, double size, Brush fg, FontWeight? w = null, double maxWidth = double.PositiveInfinity) => new()
        {
            Text = s, FontSize = size, Foreground = fg, FontFamily = Theme.Font, FontWeight = w ?? FontWeights.Normal,
            MaxWidth = maxWidth, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
        };

        public static Path Glyph(string data, Brush fill, double size)
        {
            return new Path
            {
                Data = Geometry.Parse(data), Fill = fill, Width = size, Height = size, Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
            };
        }

        // Özgün, basit vektör simgeleri
        public const string GStart = "M0,0H9V9H0Z M11,0H20V9H11Z M0,11H9V20H0Z M11,11H20V20H11Z";
        public const string GSpeaker = "M1,7H5L11,2V18L5,13H1Z M13.5,5.5A6,6 0 0 1 13.5,14.5L12.6,13.3A4.5,4.5 0 0 0 12.6,6.7Z M16,2.8A10,10 0 0 1 16,17.2L15.1,16A8.5,8.5 0 0 0 15.1,4Z";
        public const string GBolt = "M11,0L2,12H9L7,20L17,7H10Z";
        public const string GSpark = "M10,0C11,6 14,9 20,10C14,11 11,14 10,20C9,14 6,11 0,10C6,9 9,6 10,0Z";
        public const string GDown = "M8,0H12V12H17L10,20L3,12H8Z";
        public const string GUp = "M8,20H12V8H17L10,0L3,8H8Z";

        /// <summary>Basılınca hafifçe küçülen düğme davranışı.</summary>
        public static void PressEffect(FrameworkElement e)
        {
            var st = new ScaleTransform(1, 1);
            e.RenderTransform = st; e.RenderTransformOrigin = new Point(0.5, 0.5);
            void To(double v) { var a = new DoubleAnimation(v, TimeSpan.FromMilliseconds(110)) { EasingFunction = new QuadraticEase() }; st.BeginAnimation(ScaleTransform.ScaleXProperty, a); st.BeginAnimation(ScaleTransform.ScaleYProperty, a); }
            e.PreviewMouseLeftButtonDown += (_, _) => To(0.92);
            e.PreviewMouseLeftButtonUp += (_, _) => To(1);
            e.MouseLeave += (_, _) => To(1);
        }

    }

    /// <summary>Açılma/kapanma animasyonları (Gravitone'un yumuşak geçişlerinden esinlenildi; ayarlardan kapatılabilir).</summary>
    internal static class Anim
    {
        public static bool Enabled = true;
        static readonly IEasingFunction Out = new CubicEase { EasingMode = EasingMode.EaseOut };

        static Point Origin(KutiDock.Core.DockEdge e) => e switch
        {
            KutiDock.Core.DockEdge.Ust => new Point(0.5, 0), KutiDock.Core.DockEdge.Sol => new Point(0, 0.5),
            KutiDock.Core.DockEdge.Sag => new Point(1, 0.5), _ => new Point(0.5, 1),
        };

        /// <summary>Panel dock'tan doğru büyüyerek ve belirerek açılır (macOS menü/panel hissi).</summary>
        public static void PopIn(UIElement el, KutiDock.Core.DockEdge edge)
        {
            if (!Enabled || el == null) return;
            if (el is Window w) { el = w.Content as UIElement; if (el == null) return; }
            var st = new ScaleTransform(0.92, 0.92);
            el.RenderTransform = st; el.RenderTransformOrigin = Origin(edge);
            var d = TimeSpan.FromMilliseconds(190);
            var a = new DoubleAnimation(0.92, 1, d) { EasingFunction = Out };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, a); st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
        }

        /// <summary>Tek zıplama (Başlat düğmesi): parabol, ekranın içine doğru.</summary>
        public static void Hop(FrameworkElement el, KutiDock.Core.DockEdge edge, double height)
        {
            if (!Enabled) return;
            var tt = el.RenderTransform as TranslateTransform ?? new TranslateTransform();
            el.RenderTransform = tt;
            double sign = edge is KutiDock.Core.DockEdge.Alt or KutiDock.Core.DockEdge.Sag ? -1 : 1;
            var prop = edge is KutiDock.Core.DockEdge.Sol or KutiDock.Core.DockEdge.Sag ? TranslateTransform.XProperty : TranslateTransform.YProperty;
            tt.BeginAnimation(prop, new DoubleAnimation(0, sign * height, TimeSpan.FromMilliseconds(150)) { AutoReverse = true, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
        }

        /// <summary>Dock belirir/çekilir: kaydırma + saydamlık (yaklaşık Gravitone gizle/göster hızı).</summary>
        public static void Slide(Window w, FrameworkElement body, KutiDock.Core.DockEdge edge, bool show, Action done = null, double distance = 14)
        {
            if (!Enabled) { w.Opacity = 1; done?.Invoke(); return; }
            var tt = body.RenderTransform as TranslateTransform ?? new TranslateTransform();
            body.RenderTransform = tt;
            double off = distance * (edge is KutiDock.Core.DockEdge.Alt or KutiDock.Core.DockEdge.Sag ? 1 : -1);
            var prop = edge is KutiDock.Core.DockEdge.Sol or KutiDock.Core.DockEdge.Sag ? TranslateTransform.XProperty : TranslateTransform.YProperty;
            var d = TimeSpan.FromMilliseconds(show ? 220 : 150);
            tt.BeginAnimation(prop, new DoubleAnimation(show ? off : 0, show ? 0 : off, d) { EasingFunction = Out });
            var fade = new DoubleAnimation(show ? 0 : 1, show ? 1 : 0, d);
            fade.Completed += (_, _) => { w.BeginAnimation(UIElement.OpacityProperty, null); w.Opacity = 1; done?.Invoke(); };
            w.BeginAnimation(UIElement.OpacityProperty, fade);
        }
    }

    /// <summary>Halka gösterge (CPU/RAM/GPU).</summary>
    internal sealed class Ring : FrameworkElement
    {
        public double Value; // 0..1
        public string Label = "", Caption = "";
        public Brush Stroke = Theme.Accent, Track = Theme.B("#33FFFFFF"), Fg = Brushes.White, Dim = Brushes.Gray;

        protected override void OnRender(DrawingContext dc)
        {
            double s = Math.Min(ActualWidth, ActualHeight), th = Math.Max(2.5, s * 0.09), r = s / 2 - th / 2;
            var c = new Point(ActualWidth / 2, ActualHeight / 2);
            dc.DrawEllipse(null, new Pen(Track, th), c, r, r);
            double v = Math.Clamp(Value, 0, 1);
            if (v > 0.001)
            {
                double ang = v * 359.9 * Math.PI / 180;
                var start = new Point(c.X, c.Y - r);
                var end = new Point(c.X + r * Math.Sin(ang), c.Y - r * Math.Cos(ang));
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(start, false, false);
                    ctx.ArcTo(end, new Size(r, r), 0, v > 0.5, SweepDirection.Clockwise, true, false);
                }
                dc.DrawGeometry(null, new Pen(Stroke, th) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, g);
            }
            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var ft = new FormattedText(Label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(Theme.Font, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), s * 0.32, Fg, dpi);
            var cap = new FormattedText(Caption, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(Theme.Font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), s * 0.19, Dim, dpi);
            dc.DrawText(ft, new Point(c.X - ft.Width / 2, c.Y - ft.Height / 2 - cap.Height * 0.35));
            dc.DrawText(cap, new Point(c.X - cap.Width / 2, c.Y + ft.Height * 0.28));
        }
    }

    /// <summary>Yuvarlak uçlu yatay ilerleme çubuğu.</summary>
    internal sealed class Bar : FrameworkElement
    {
        public double Value; public Brush Fill = Theme.Green, Track = Theme.B("#33FFFFFF");
        protected override void OnRender(DrawingContext dc)
        {
            double h = ActualHeight, w = ActualWidth;
            dc.DrawRoundedRectangle(Track, null, new Rect(0, 0, w, h), h / 2, h / 2);
            double fw = Math.Max(h, w * Math.Clamp(Value, 0, 1));
            if (Value > 0.001) dc.DrawRoundedRectangle(Fill, null, new Rect(0, 0, fw, h), h / 2, h / 2);
        }
    }

    /// <summary>Küçük çizgi grafiği.</summary>
    internal sealed class Spark : FrameworkElement
    {
        public IList<double> Values = Array.Empty<double>();
        public Brush Stroke = Theme.Accent;
        protected override void OnRender(DrawingContext dc)
        {
            int n = Values.Count; if (n == 0) return;
            double max = 0; foreach (var v in Values) max = Math.Max(max, v);
            if (max <= 0) max = 1;
            double w = ActualWidth, h = ActualHeight;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                for (int i = 0; i < n; i++)
                {
                    var p = new Point(n == 1 ? 0 : i * w / (n - 1), h - 1 - (h - 2) * Values[i] / max);
                    if (i == 0) ctx.BeginFigure(p, false, false); else ctx.LineTo(p, true, true);
                }
            }
            dc.DrawGeometry(null, new Pen(Stroke, 1.4) { LineJoin = PenLineJoin.Round }, g);
        }
    }
}

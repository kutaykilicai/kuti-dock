using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Media.Effects;
using KutiDock.Core;
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

        /// <summary>1.6.11: küçük Apple tarzı anahtar (açıkken yeşil iz, düğme sağa kayar). Tıklayınca durum değişir.</summary>
        public static Border MiniSwitch(bool on, Action<bool> changed)
        {
            var knobMove = new TranslateTransform(on ? 12 : 0, 0);
            var knob = new Ellipse { Width = 14, Height = 14, Fill = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2, 0, 0, 0), RenderTransform = knobMove,
                Effect = new DropShadowEffect { BlurRadius = 3, ShadowDepth = 1, Opacity = 0.3 } };
            var trackOn = (Color)ColorConverter.ConvertFromString("#34C759"); var trackOff = (Color)ColorConverter.ConvertFromString("#48787880");
            var fill = new SolidColorBrush(on ? trackOn : trackOff);
            var sw = new Border { Width = 30, Height = 18, CornerRadius = new CornerRadius(9), Background = fill, Child = knob, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
            sw.MouseLeftButtonUp += (_, e) =>
            {
                on = !on;
                var d = TimeSpan.FromMilliseconds(Anim.Enabled ? 180 : 0);
                knobMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(on ? 12 : 0, d) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                fill.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(on ? trackOn : trackOff, d));
                e.Handled = true;
                changed(on);
            };
            return sw;
        }
    }

    /// <summary>Açılma/kapanma animasyonları (Gravitone'un yumuşak geçişlerinden esinlenildi; ayarlardan kapatılabilir).</summary>
    /// <summary>
    /// 1.6.5: tek değerli yay animasyonu. Yalnızca hareket sürerken ekran yenilemesine (CompositionTarget.Rendering) bağlanır;
    /// hedef yarıda değişirse konum ve hız korunarak yeni hedefe döner.
    /// </summary>
    internal sealed class SpringRun
    {
        static readonly Stopwatch Clock = Stopwatch.StartNew();
        double _x, _v, _target, _last;
        SpringSpec _spec;
        Action _done;
        /// <summary>Değeri uygulayan (kenar/mesafe değişince yenilenebilir).</summary>
        public Action<double> Apply;
        bool _on;

        public SpringRun(double x, Action<double> apply) { _x = _target = x; Apply = apply; }
        public double Value => _x;
        public double Target => _target;

        public void To(double target, SpringSpec spec, Action done = null)
        {
            _target = target; _spec = spec; _done = done; // önceki hedefin işi iptal (kesildi)
            if (_on) return;
            _on = true; _last = Clock.Elapsed.TotalSeconds;
            CompositionTarget.Rendering += Tick;
        }

        /// <summary>Hıza dürtü ver (zıplama), hedef aynı kalır.</summary>
        public void Kick(double velocity, SpringSpec spec) { _v += velocity; To(_target, spec, _done); }

        public void Jump(double x)
        {
            Stop(); _x = _target = x; _v = 0; _done = null; Apply?.Invoke(x);
        }

        void Stop() { if (_on) { _on = false; CompositionTarget.Rendering -= Tick; } }

        void Tick(object sender, EventArgs e)
        {
            double now = Clock.Elapsed.TotalSeconds, dt = Math.Clamp(now - _last, 0, 1.0 / 20); _last = now;
            (_x, _v) = Spring.Step(_x, _v, _target, dt, _spec);
            if (Spring.Settled(_x, _v, _target)) { _x = _target; _v = 0; Stop(); }
            Apply?.Invoke(_x);
            if (!_on) { var d = _done; _done = null; d?.Invoke(); }
        }
    }

    internal static class Anim
    {
        public static bool Enabled = true;
        static readonly ConditionalWeakTable<UIElement, SpringRun> Runs = new();

        static Point Origin(KutiDock.Core.DockEdge e) => e switch
        {
            KutiDock.Core.DockEdge.Ust => new Point(0.5, 0), KutiDock.Core.DockEdge.Sol => new Point(0, 0.5),
            KutiDock.Core.DockEdge.Sag => new Point(1, 0.5), _ => new Point(0.5, 1),
        };

        static UIElement Body(UIElement el) => el is Window w ? w.Content as UIElement : el;

        /// <summary>
        /// Panel ya da menü dock'tan doğru büyür: küçük, bulanık ve saydam başlar; hafif taşmalı yayla netleşir.
        /// Kapanırken açılırsa olduğu yerden geri döner.
        /// </summary>
        public static void PopIn(UIElement el, KutiDock.Core.DockEdge edge, bool menu = false)
        {
            el = Body(el);
            if (!Enabled || el == null) return;
            double from = menu ? AppleMotion.MenuFromScale : AppleMotion.PanelFromScale, blurMax = menu ? AppleMotion.PanelBlur / 2 : AppleMotion.PanelBlur;
            var st = el.RenderTransform as ScaleTransform ?? new ScaleTransform();
            el.RenderTransform = st; el.RenderTransformOrigin = Origin(edge);
            el.BeginAnimation(UIElement.OpacityProperty, null);
            var blur = new BlurEffect { Radius = blurMax, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance };
            if (!Runs.TryGetValue(el, out var run)) { run = new SpringRun(0, null); Runs.Add(el, run); }
            var e0 = el;
            run.Apply = p =>
            {
                st.ScaleX = st.ScaleY = Math.Max(0.01, from + (1 - from) * p);
                e0.Opacity = AppleMotion.Opacity(p);
                double r = AppleMotion.Blur(p, blurMax);
                if (r > 0.3) { if (e0.Effect is BlurEffect b) b.Radius = r; } else if (e0.Effect is BlurEffect) e0.Effect = null; // bitince efekt kalkar (GPU yükü yok)
            };
            if (run.Target == 0 && run.Value == 0) run.Jump(0);
            if (run.Value < 0.7) el.Effect = blur;
            run.To(1, AppleMotion.PanelOpen);
        }

        /// <summary>Kapanış: taşmasız, kısa; bitince <paramref name="close"/>. Animasyon kapalıysa hemen kapatır.</summary>
        public static void PopOut(UIElement el, Action close)
        {
            el = Body(el);
            if (!Enabled || el == null || !Runs.TryGetValue(el, out var run)) { close(); return; }
            run.To(0, AppleMotion.PanelClose, () => { close(); run.Jump(0); });
        }

        /// <summary>Tek zıplama (Başlat düğmesi): yay dürtüsüyle ekranın içine sıçrar, sallanarak yerine oturur.</summary>
        public static void Hop(FrameworkElement el, KutiDock.Core.DockEdge edge, double height)
        {
            if (!Enabled) return;
            var tt = el.RenderTransform as TranslateTransform ?? new TranslateTransform();
            el.RenderTransform = tt;
            bool side = edge is KutiDock.Core.DockEdge.Sol or KutiDock.Core.DockEdge.Sag;
            double sign = edge is KutiDock.Core.DockEdge.Alt or KutiDock.Core.DockEdge.Sag ? -1 : 1;
            if (!Runs.TryGetValue(el, out var run))
            {
                run = new SpringRun(0, x => { if (side) tt.X = x; else tt.Y = x; });
                Runs.Add(el, run);
            }
            // ω·h·1,9 hızla tepe yaklaşık h olur (0,5 sönümde)
            run.Kick(sign * height * 1.9 * 2 * Math.PI / AppleMotion.Press.Response, AppleMotion.Press);
        }

        /// <summary>
        /// Dock belirir/çekilir: tek bir yay ilerlemesi kaymayı ve saydamlığı sürer. Çekilirken imleç geri gelirse
        /// olduğu yerden geri döner (kesilebilir).
        /// </summary>
        public static void Slide(Window w, FrameworkElement body, KutiDock.Core.DockEdge edge, bool show, Action done = null, double distance = 14)
        {
            w.BeginAnimation(UIElement.OpacityProperty, null);
            var tt = body.RenderTransform as TranslateTransform ?? new TranslateTransform();
            body.RenderTransform = tt;
            if (!Enabled) { tt.X = tt.Y = 0; w.Opacity = 1; done?.Invoke(); return; }
            bool side = edge is KutiDock.Core.DockEdge.Sol or KutiDock.Core.DockEdge.Sag;
            double off = distance * (edge is KutiDock.Core.DockEdge.Alt or KutiDock.Core.DockEdge.Sag ? 1 : -1);
            if (!Runs.TryGetValue(w, out var run))
            {
                run = new SpringRun(show ? 0 : 1, null);
                Runs.Add(w, run);
            }
            run.Apply = p => // mesafe/kenar her çağrıda değişebilir
            {
                double o = off * (1 - p);
                if (side) { tt.X = o; tt.Y = 0; } else { tt.Y = o; tt.X = 0; }
                w.Opacity = Math.Clamp(p, 0, 1);
            };
            if (show && !w.IsVisible) run.Jump(0);
            run.To(show ? 1 : 0, show ? AppleMotion.DockShow : AppleMotion.DockHide, () => { done?.Invoke(); if (!show) w.Opacity = 1; });
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

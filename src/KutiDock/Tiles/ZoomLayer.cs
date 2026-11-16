using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace KutiDock.Tiles
{
    /// <summary>
    /// Klasik dock'un büyüyen simgeleri için şeffaf, tıklamayı geçiren katman. Büyüyen/zıplayan simgeler dock'un üstünde
    /// buraya çizilir: dock'un yuvarlak köşeli bölgesi ve bulanıklığı onları kesmez, şeridin yerleşimi de değişmez.
    /// </summary>
    internal sealed class ZoomLayer : Window
    {
        public readonly record struct Item(FrameworkElement Host, ImageSource Source, double Scale, double LiftX, double LiftY, double OriginX, double OriginY);

        readonly Canvas _canvas = new() { IsHitTestVisible = false };
        readonly Dictionary<FrameworkElement, Image> _images = new();
        IntPtr _hwnd;
        (int X, int Y, int W, int H) _rect;

        public ZoomLayer()
        {
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; ShowActivated = false; IsHitTestVisible = false;
            Left = -32000; Top = -32000; Width = 1; Height = 1; Content = _canvas; Title = "Kuti Dock büyütme";
            SourceInitialized += (_, _) =>
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                long ex = Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE).ToInt64();
                Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE, new IntPtr(ex | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | 0x20 /*WS_EX_TRANSPARENT*/));
                // Büyümüş simgenin opak pikselleri de tıklamayı/fareyi almasın: altındaki dock alsın
                HwndSource.FromHwnd(_hwnd)?.AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) =>
                {
                    if (msg != 0x84 /*WM_NCHITTEST*/) return IntPtr.Zero;
                    handled = true;
                    return new IntPtr(-1); // HTTRANSPARENT
                });
            };
        }

        /// <summary>Simgeleri büyümüş hâlleriyle çizer. <paramref name="pad"/>: şeridin çevresinde ayrılan boşluk (şerit DIP'i).</summary>
        public bool Draw(FrameworkElement strip, IReadOnlyList<Item> items, double pad)
        {
            try
            {
                if (!strip.IsVisible || PresentationSource.FromVisual(strip) == null) return false;
                var a = strip.PointToScreen(new Point(-pad, -pad));
                var b = strip.PointToScreen(new Point(strip.ActualWidth + pad, strip.ActualHeight + pad));
                var r = ((int)Math.Floor(Math.Min(a.X, b.X)), (int)Math.Floor(Math.Min(a.Y, b.Y)),
                         (int)Math.Ceiling(Math.Abs(b.X - a.X)), (int)Math.Ceiling(Math.Abs(b.Y - a.Y)));
                if (!IsVisible) Show();
                if (_hwnd == IntPtr.Zero) return false;
                if (r != _rect)
                {
                    // dock'un üstünde (her ikisi de en üstte; bu sonra konur)
                    _rect = r;
                    Native.SetWindowPos(_hwnd, new IntPtr(-1) /*HWND_TOPMOST*/, r.Item1, r.Item2, r.Item3, r.Item4, Native.SWP_NOACTIVATE);
                }

                var keep = new HashSet<FrameworkElement>();
                foreach (var it in items)
                {
                    var h = it.Host;
                    if (!h.IsVisible || h.ActualWidth <= 0) continue;
                    var p0 = _canvas.PointFromScreen(h.PointToScreen(new Point(0, 0)));
                    var p1 = _canvas.PointFromScreen(h.PointToScreen(new Point(h.ActualWidth, h.ActualHeight)));
                    double rw = p1.X - p0.X, rh = p1.Y - p0.Y, f = rw / h.ActualWidth;
                    double sw = rw * it.Scale, sh = rh * it.Scale;
                    if (!_images.TryGetValue(h, out var img))
                    {
                        img = new Image { IsHitTestVisible = false };
                        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                        _images[h] = img; _canvas.Children.Add(img);
                    }
                    img.Source = it.Source; img.Width = sw; img.Height = sh;
                    Canvas.SetLeft(img, p0.X + it.OriginX * (rw - sw) + it.LiftX * f);
                    Canvas.SetTop(img, p0.Y + it.OriginY * (rh - sh) + it.LiftY * f);
                    keep.Add(h);
                }
                foreach (var gone in new List<FrameworkElement>(_images.Keys))
                    if (!keep.Contains(gone)) { _canvas.Children.Remove(_images[gone]); _images.Remove(gone); }
                return true;
            }
            catch (Exception) { return false; }
        }

        public void Clear()
        {
            if (IsVisible) Hide();
            _canvas.Children.Clear(); _images.Clear(); _rect = default;
        }
    }
}

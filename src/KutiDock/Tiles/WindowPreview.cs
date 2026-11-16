using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using KutiDock.Core;

namespace KutiDock.Tiles
{
    /// <summary>
    /// Windows 11 görev çubuğundaki gibi pencere önizleme paneli: yuvarlak köşeli koyu panelde her pencere için
    /// simge + başlık + canlı küçük önizleme (DWM thumbnail). Tıkla = o pencere öne gelir; orta tık ya da × = kapat.
    /// Panel odak çalmaz; fare hem düğmeden hem panelden çıkınca kapanır.
    /// </summary>
    internal sealed class WindowPreview : Window
    {
        // ---- DWM thumbnail (belgelenmiş dwmapi) ----
        [DllImport("dwmapi.dll")] static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);
        [DllImport("dwmapi.dll")] static extern int DwmUnregisterThumbnail(IntPtr thumb);
        [DllImport("dwmapi.dll")] static extern int DwmUpdateThumbnailProperties(IntPtr thumb, ref THUMBPROPS p);
        [DllImport("dwmapi.dll")] static extern int DwmQueryThumbnailSourceSize(IntPtr thumb, out PSIZE size);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        [StructLayout(LayoutKind.Sequential)] struct PSIZE { public int x, y; }
        [StructLayout(LayoutKind.Sequential)]
        struct THUMBPROPS
        {
            public int dwFlags; public Native.RECT rcDestination, rcSource; public byte opacity;
            [MarshalAs(UnmanagedType.Bool)] public bool fVisible; [MarshalAs(UnmanagedType.Bool)] public bool fSourceClientAreaOnly;
        }
        const int DWM_TNP_RECTDESTINATION = 0x1, DWM_TNP_OPACITY = 0x4, DWM_TNP_VISIBLE = 0x8, DWM_TNP_SOURCECLIENTAREAONLY = 0x10;

        static WindowPreview _current;
        public static bool IsOpenFor(object key) => _current != null && Equals(_current._key, key);
        public static void CloseCurrent() { _current?.Close(); _current = null; }

        /// <summary>Paneli düğmenin üstünde (dock üstteyse altında) açar.</summary>
        public static void Open(object key, IReadOnlyList<IntPtr> windows, ImageSource icon, FrameworkElement anchor, DockEdge edge)
        {
            if (IsOpenFor(key)) return;
            CloseCurrent();
            var alive = windows.Where(Native.IsWindowVisible).ToList();
            if (alive.Count == 0) return;
            _current = new WindowPreview(key, alive, icon, anchor, edge);
            _current.Show();
        }

        const double CardW = 236, ThumbH = 132;
        readonly object _key;
        readonly FrameworkElement _anchor;
        readonly DockEdge _edge;
        readonly ImageSource _icon;
        readonly StackPanel _row = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(6) };
        readonly List<(IntPtr src, Border slot, IntPtr thumb)> _thumbs = new();
        readonly DispatcherTimer _watch = new() { Interval = TimeSpan.FromMilliseconds(150) };
        List<IntPtr> _windows;
        IntPtr _hwnd;
        int _outside;

        WindowPreview(object key, List<IntPtr> windows, ImageSource icon, FrameworkElement anchor, DockEdge edge)
        {
            _key = key; _windows = windows; _icon = icon; _anchor = anchor; _edge = edge;
            // DWM önizlemesi katmanlı (AllowsTransparency) pencerelerde çizilmez; köşeleri DWM yuvarlatır.
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true;
            ShowActivated = false; SizeToContent = SizeToContent.WidthAndHeight;
            Background = Theme.B("#FF202020"); FontFamily = Theme.Font;
            Content = _row;
            Left = -32000; Top = -32000; // yerleşene kadar ekran dışında
            SourceInitialized += (_, _) =>
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                long ex = Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE).ToInt64();
                Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE, new IntPtr(ex | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE));
                int round = 2; DwmSetWindowAttribute(_hwnd, 33, ref round, 4);   // DWMWA_WINDOW_CORNER_PREFERENCE = yuvarlak
                int dark = 1; DwmSetWindowAttribute(_hwnd, 20, ref dark, 4);     // koyu kenarlık
            };
            Build();
            ContentRendered += (_, _) => { Place(); UpdateThumbs(); };
            SizeChanged += (_, _) => { Place(); UpdateThumbs(); };
            _watch.Tick += (_, _) => Watch();
            _watch.Start();
        }

        void Build()
        {
            ClearThumbs();
            _row.Children.Clear();
            foreach (var h in _windows) _row.Children.Add(Card(h));
            if (_hwnd != IntPtr.Zero) Dispatcher.BeginInvoke(new Action(() => { Place(); UpdateThumbs(); }), DispatcherPriority.Loaded);
        }

        FrameworkElement Card(IntPtr h)
        {
            var title = Native.WindowTitle(h);
            var head = new DockPanel { Margin = new Thickness(8, 6, 4, 6), LastChildFill = true };
            var close = new Border
            {
                Width = 26, Height = 22, CornerRadius = new CornerRadius(4), Background = Brushes.Transparent, Visibility = Visibility.Hidden,
                Child = new TextBlock { Text = "✕", FontSize = 11, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                Cursor = Cursors.Hand, ToolTip = "Pencereyi kapat",
            };
            close.MouseEnter += (_, _) => close.Background = Theme.B("#FFC42B1C");
            close.MouseLeave += (_, _) => close.Background = Brushes.Transparent;
            close.MouseLeftButtonUp += (_, e) => { CloseWindow(h); e.Handled = true; };
            DockPanel.SetDock(close, Dock.Right); head.Children.Add(close);
            if (_icon != null)
            {
                var ic = new Image { Source = _icon, Width = 16, Height = 16, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
                RenderOptions.SetBitmapScalingMode(ic, BitmapScalingMode.HighQuality);
                DockPanel.SetDock(ic, Dock.Left); head.Children.Add(ic);
            }
            head.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });

            // Önizleme yeri: DWM buraya canlı küçük görüntüyü çizer. Küçültülmüş pencerede görüntü olmaz, büyük simge gösterilir.
            var slot = new Border { Height = ThumbH, Margin = new Thickness(8, 0, 8, 8) };
            if (Native.IsIconic(h) && _icon != null) slot.Child = new Image { Source = _icon, Width = 40, Height = 40, Opacity = 0.9 };

            var card = new Border
            {
                Width = CardW, CornerRadius = new CornerRadius(6), Margin = new Thickness(2), Background = Brushes.Transparent,
                Child = new DockPanel { Children = { OnTop(head), slot } }, Cursor = Cursors.Hand, ToolTip = title,
            };
            card.MouseEnter += (_, _) => { card.Background = Theme.B("#FF2D2D2D"); close.Visibility = Visibility.Visible; };
            card.MouseLeave += (_, _) => { card.Background = Brushes.Transparent; close.Visibility = Visibility.Hidden; };
            card.MouseLeftButtonUp += (_, e) => { Native.Activate(h); CloseCurrent(); e.Handled = true; };
            card.MouseUp += (_, e) => { if (e.ChangedButton == MouseButton.Middle) { CloseWindow(h); e.Handled = true; } };
            if (!Native.IsIconic(h)) _thumbs.Add((h, slot, IntPtr.Zero));
            return card;
        }

        static UIElement OnTop(UIElement e) { DockPanel.SetDock(e, System.Windows.Controls.Dock.Top); return e; }

        void CloseWindow(IntPtr h)
        {
            Native.PostMessage(h, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            _windows = _windows.Where(w => w != h).ToList();
            if (_windows.Count == 0) { CloseCurrent(); return; }
            Build();
        }

        void ClearThumbs()
        {
            foreach (var t in _thumbs) if (t.thumb != IntPtr.Zero) DwmUnregisterThumbnail(t.thumb);
            _thumbs.Clear();
        }

        void UpdateThumbs()
        {
            if (_hwnd == IntPtr.Zero) return;
            var dpi = VisualTreeHelper.GetDpi(this);
            for (int i = 0; i < _thumbs.Count; i++)
            {
                var (src, slot, thumb) = _thumbs[i];
                if (thumb == IntPtr.Zero)
                {
                    if (DwmRegisterThumbnail(_hwnd, src, out thumb) != 0) continue;
                    _thumbs[i] = (src, slot, thumb);
                }
                if (!slot.IsVisible || slot.ActualWidth <= 0) continue;
                var p = slot.TransformToAncestor(this).Transform(new Point(0, 0));
                double bw = slot.ActualWidth * dpi.DpiScaleX, bh = slot.ActualHeight * dpi.DpiScaleY;
                double x = p.X * dpi.DpiScaleX, y = p.Y * dpi.DpiScaleY;
                // En-boy oranını koru, kutuya ortala
                if (DwmQueryThumbnailSourceSize(thumb, out var sz) == 0)
                    (x, y, bw, bh) = KutiDock.Core.Thumb.Fit(x, y, bw, bh, sz.x, sz.y);
                var props = new THUMBPROPS
                {
                    dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_VISIBLE | DWM_TNP_OPACITY | DWM_TNP_SOURCECLIENTAREAONLY,
                    rcDestination = new Native.RECT { Left = (int)x, Top = (int)y, Right = (int)(x + bw), Bottom = (int)(y + bh) },
                    opacity = 255, fVisible = true, fSourceClientAreaOnly = false,
                };
                DwmUpdateThumbnailProperties(thumb, ref props);
            }
        }

        void Place()
        {
            if (!_anchor.IsVisible) return;
            var a = _anchor.PointToScreen(new Point(0, 0));
            var b = _anchor.PointToScreen(new Point(_anchor.ActualWidth, _anchor.ActualHeight)); // kompakt görünümde ölçekli boyut
            var dpi = VisualTreeHelper.GetDpi(this);
            double ax = a.X / dpi.DpiScaleX, ay = a.Y / dpi.DpiScaleY, aw = (b.X - a.X) / dpi.DpiScaleX, ah = (b.Y - a.Y) / dpi.DpiScaleY;
            var wa = SystemParameters.VirtualScreenWidth > 0
                ? new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight)
                : SystemParameters.WorkArea;
            (Left, Top) = DockLayout.Popup(_edge, ax, ay, aw, ah, ActualWidth, ActualHeight, 10, wa.X, wa.Y, wa.Width, wa.Height);
        }

        void Watch()
        {
            if (!Native.GetCursorPos(out var c)) return;
            bool over = Inside(this, c) || (_anchor.IsVisible && Inside(_anchor, c));
            _outside = over ? 0 : _outside + 1;
            if (_outside >= 3) CloseCurrent();   // ~0,45 sn dışarıda kalınca kapan
        }

        static bool Inside(FrameworkElement e, Native.POINT c)
        {
            try
            {
                var tl = e.PointToScreen(new Point(0, 0));
                var br = e.PointToScreen(new Point(e.ActualWidth, e.ActualHeight));
                // Düğme ile panel arasındaki 10 px'lik boşlukta kapanmasın
                return c.X >= tl.X - 12 && c.X <= br.X + 12 && c.Y >= tl.Y - 14 && c.Y <= br.Y + 14;
            }
            catch (InvalidOperationException) { return false; }
        }

        protected override void OnClosed(EventArgs e)
        {
            _watch.Stop();
            ClearThumbs();
            if (_current == this) _current = null;
            base.OnClosed(e);
        }
    }
}

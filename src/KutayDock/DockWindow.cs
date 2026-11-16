using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using KutayDock.Core;
using KutayDock.Tiles;

namespace KutayDock
{
    /// <summary>Ekranın altında (ya da üstünde) yüzen, yuvarlak köşeli dock penceresi.</summary>
    internal sealed class DockWindow : Window
    {
        readonly Services _s;
        readonly Action _openSettings, _exit;
        readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
        readonly AlertEngine _alerts = new();
        readonly Action<string, string> _notify;
        Border _body;
        StackPanel _row;
        List<Tile> _tiles = new();
        Theme _theme;
        IntPtr _hwnd;
        AppBar _appBar;
        bool _hiddenForFullscreen, _userHidden, _hiding;
        // Tam ekran 250 ms'de bir yoklanır: video/oyun ≈0,5 sn'de gizler (2 gözlem), dock 2 sn sonra geri gelir (8 gözlem).
        readonly DispatcherTimer _fsTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
        readonly Debounce _fullscreen = new(2, 8);

        public DockWindow(Services s, Action openSettings, Action exit, Action<string, string> notify)
        {
            _s = s; _openSettings = openSettings; _exit = exit; _notify = notify;
            Title = "Kutay Dock";
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; ShowActivated = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            UseLayoutRounding = true;
            SourceInitialized += (_, _) =>
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                long ex = Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE).ToInt64();
                // Alt+Tab'da görünme, tıklanınca diğer pencerelerin odağını çalma
                Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE, new IntPtr(ex | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE));
                _appBar = new AppBar(_hwnd);
            };
            SizeChanged += (_, _) => Place();
            Build();
            _timer.Tick += (_, _) => Tick();
            _timer.Start();
            _fsTimer.Tick += (_, _) => CheckFullscreen();
            _fsTimer.Start();
            ContextMenu = MakeMenu();
        }

        ContextMenu MakeMenu()
        {
            var m = new ContextMenu();
            void Add(string h, Action a) { var i = new MenuItem { Header = h }; i.Click += (_, _) => a(); m.Items.Add(i); }
            Add("Ayarlar…", _openSettings);
            Add("Widget'lar…", _openSettings);
            m.Items.Add(new Separator());
            Add("Görev Yöneticisi", () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("taskmgr.exe") { UseShellExecute = true }));
            Add("Masaüstünü göster", () => Native.PressKeys(Native.VK_LWIN, (byte)'D'));
            m.Items.Add(new Separator());
            Add("Kutay Dock'tan çık", _exit);
            return m;
        }

        /// <summary>Ayar değişince tüm görünümü yeniden kurar.</summary>
        public void Build()
        {
            var set = _s.Settings;
            _theme = Theme.For(set.Style);
            double k = set.Scale;
            Anim.Enabled = set.Animations;
            bool vertical = DockLayout.Vertical(set.Edge);
            _row = vertical
                ? new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4 * k, 0, 4 * k) }
                : new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(1 * k, 0, 1 * k, 0) };
            _tiles = new List<Tile>();
            foreach (var id in set.Widgets)
            {
                Tile t = id switch
                {
                    "start" => new StartTile(_s, _theme),
                    "search" => new SearchTile(_s, _theme),
                    "apps" => new AppsTile(_s, _theme, () => _hwnd),
                    "ai" => new LimitsTile(_s, _theme, _openSettings),
                    "volume" => new VolumeTile(_s, _theme),
                    "system" => new SystemTile(_s, _theme),
                    "network" => new NetworkTile(_s, _theme),
                    "warp" => new WarpTile(_s, _theme),
                    "storage" => new StorageTile(_s, _theme),
                    "screenshot" => new ScreenshotTile(_s, _theme),
                    "battery" => new BatteryTile(_s, _theme),
                    "tray" => new TrayTile(_s, _theme),
                    "clock" => new ClockTile(_s, _theme),
                    _ => null,
                };
                if (t == null) continue;
                _tiles.Add(t);
                // Dikeyde sütuna sığmayan geniş widget'lar (sistem, ağ, saat…) kompakt: orantılı küçültülür
                _row.Children.Add(vertical ? new Viewbox { Child = t, StretchDirection = StretchDirection.DownOnly, MaxWidth = 52 * k, Margin = new Thickness(0, 1 * k, 0, 1 * k) } : t);
            }
            if (vertical)
            {
                // Ekran yüksekliğine sığmazsa tüm sütun küçülür
                var fit = new Viewbox { Child = _row, StretchDirection = StretchDirection.DownOnly, MaxHeight = Math.Max(200, SystemParameters.WorkArea.Height - 16) };
                _body = Body(fit, k, vertical);
            }
            else if (set.FullWidth)
            {
                // Tam genişlik: saat ve sonrası sağa yaslanır
                var dock = new DockPanel { LastChildFill = false };
                _row.HorizontalAlignment = HorizontalAlignment.Left;
                dock.Children.Add(_row);
                _body = Body(dock, k, false);
            }
            else _body = Body(_row, k, false);
            Content = _body;
            ApplyBlur();
            Refresh();
            Dispatcher.BeginInvoke(new Action(Place), DispatcherPriority.Loaded);
        }

        Border Body(UIElement child, double k, bool vertical)
        {
            var b = new Border
            {
                CornerRadius = new CornerRadius(16 * k), Background = _theme.DockBg,
                BorderBrush = _theme.DockBorder, BorderThickness = new Thickness(1), Child = child, ClipToBounds = true,
            };
            if (vertical) b.Width = 58 * k; else b.Height = 54 * k;
            if (_s.Settings.FullWidth && !vertical)
            {
                var wa = SystemParameters.WorkArea;
                b.Width = (_s.Settings.ReserveSpace ? SystemParameters.PrimaryScreenWidth : wa.Width) - 16;
            }
            return b;
        }

        void ApplyBlur()
        {
            if (_hwnd == IntPtr.Zero) { SourceInitialized += (_, _) => ApplyBlur(); return; }
            Native.SetBlur(_hwnd, _theme.Blur);
        }

        /// <summary>Dock'u ekranın alt (ya da üst) ortasına yerleştirir, gerekirse ekranda yer ayırır.</summary>
        void Place()
        {
            if (_hwnd == IntPtr.Zero || _body == null) return;
            var set = _s.Settings; double k = set.Scale, margin = 8;
            double w = ActualWidth, h = ActualHeight;
            var dpi = VisualTreeHelper.GetDpi(this);
            bool top = set.Edge == DockEdge.Ust;
            // Yer ayırma yalnızca yatay (Üst/Alt) dock için; Sol/Sağ her zaman yüzer.
            bool reserve = set.ReserveSpace && !DockLayout.Vertical(set.Edge);
            if (reserve) _appBar.Reserve(top, (int)Math.Ceiling((h + margin * 2) * dpi.DpiScaleY));
            else _appBar.Remove();
            Rect area = reserve ? _appBar.ReservedRect(dpi.DpiScaleX, dpi.DpiScaleY) ?? SystemParameters.WorkArea : SystemParameters.WorkArea;
            if (reserve) { Left = area.Left + (area.Width - w) / 2; Top = area.Top + (area.Height - h) / 2; }
            else (Left, Top) = DockLayout.Position(set.Edge, area.Left, area.Top, area.Width, area.Height, w, h, margin);
            // Bulanıklık dikdörtgen olmasın: pencere bölgesini yuvarlak köşeli yap
            int pw = (int)Math.Round(w * dpi.DpiScaleX), ph = (int)Math.Round(h * dpi.DpiScaleY), r = (int)Math.Round(32 * k * dpi.DpiScaleX);
            Native.SetWindowRgn(_hwnd, Native.CreateRoundRectRgn(0, 0, pw + 1, ph + 1, r, r), true);
        }

        void CheckFullscreen()
        {
            bool fs = _s.Settings.HideOnFullscreen && Native.ForegroundIsFullscreen(_hwnd);
            if (_fullscreen.Observe(fs))
            {
                _hiddenForFullscreen = _fullscreen.State;
                UpdateVisibility();
            }
        }

        void Tick()
        {
            // Tam ekran oyun/video varken dock çekilir ve hiçbir şey ölçülmez (oyunlarda ek yük yok).
            if (!IsVisible) return;
            _s.Tick();
            Refresh();
            RunAlerts();
        }

        public void ToggleUserHidden() { _userHidden = !_userHidden; UpdateVisibility(); }

        void UpdateVisibility()
        {
            bool show = !_userHidden && !_hiddenForFullscreen;
            if (show && (!IsVisible || _hiding))
            {
                _hiding = false;
                if (!IsVisible) Show();
                // Gizliyken monitör/çözünürlük/AppBar değişmiş olabilir: yerleşimi düzen bittikten sonra da tazele.
                Place();
                Dispatcher.BeginInvoke(new Action(Place), DispatcherPriority.Loaded);
                Anim.Slide(this, _body, _s.Settings.Edge, true);
            }
            else if (!show && IsVisible && !_hiding)
            {
                _hiding = true;
                Anim.Slide(this, _body, _s.Settings.Edge, false, () => { if (_hiding) { _hiding = false; Hide(); } });
            }
        }

        void Refresh()
        {
            foreach (var t in _tiles)
            {
                var vis = t.Wanted ? Visibility.Visible : Visibility.Collapsed;
                if (t.Visibility != vis) t.Visibility = vis;
                if (vis == Visibility.Visible) { try { t.Refresh(); } catch (Exception) { } }
            }
        }

        void RunAlerts()
        {
            var input = new AlertInput(DateTimeOffset.Now, _s.GrokBotLimit().Limit, _s.Settings.Cursor, _s.Battery, _s.Charging);
            foreach (var a in _alerts.Evaluate(_s.Settings, input))
                if (!string.IsNullOrEmpty(a.Title)) _notify(a.Title, a.Body);
        }

        protected override void OnClosed(EventArgs e)
        {
            _timer.Stop(); _fsTimer.Stop();
            _appBar?.Remove();
            base.OnClosed(e);
        }
    }
}

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
        bool _hiddenForFullscreen, _userHidden;

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
            _row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(1 * k, 0, 1 * k, 0) };
            _tiles = new List<Tile>();
            foreach (var id in set.Widgets)
            {
                Tile t = id switch
                {
                    "start" => new StartTile(_s, _theme),
                    "apps" => new AppsTile(_s, _theme, () => _hwnd),
                    "ai" => set.AiSource == AiSource.Kapali ? null : new AiTile(_s, _theme),
                    "media" => new MediaTile(_s, _theme),
                    "volume" => new VolumeTile(_s, _theme),
                    "system" => new SystemTile(_s, _theme),
                    "network" => new NetworkTile(_s, _theme),
                    "screentime" => new ScreenTimeTile(_s, _theme),
                    "calendar" => new CalendarTile(_s, _theme, _openSettings),
                    "battery" => new BatteryTile(_s, _theme),
                    "tray" => new TrayTile(_s, _theme),
                    "clock" => new ClockTile(_s, _theme),
                    _ => null,
                };
                if (t == null) continue;
                _tiles.Add(t); _row.Children.Add(t);
            }
            if (set.FullWidth)
            {
                // Tam genişlik: saat ve sonrası sağa yaslanır
                var dock = new DockPanel { LastChildFill = false };
                _row.HorizontalAlignment = HorizontalAlignment.Left;
                dock.Children.Add(_row);
                _body = Body(dock, k);
            }
            else _body = Body(_row, k);
            Content = _body;
            ApplyBlur();
            Refresh();
            Dispatcher.BeginInvoke(new Action(Place), DispatcherPriority.Loaded);
        }

        Border Body(UIElement child, double k)
        {
            var b = new Border
            {
                Height = 54 * k, CornerRadius = new CornerRadius(16 * k), Background = _theme.DockBg,
                BorderBrush = _theme.DockBorder, BorderThickness = new Thickness(1), Child = child, ClipToBounds = true,
            };
            if (_s.Settings.FullWidth)
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
            if (set.ReserveSpace) _appBar.Reserve(top, (int)Math.Ceiling((h + margin * 2) * dpi.DpiScaleY));
            else _appBar.Remove();
            var screen = new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
            Rect area = set.ReserveSpace ? _appBar.ReservedRect(dpi.DpiScaleX, dpi.DpiScaleY) ?? SystemParameters.WorkArea : SystemParameters.WorkArea;
            Left = area.Left + (area.Width - w) / 2;
            Top = set.ReserveSpace ? area.Top + (area.Height - h) / 2 : (top ? area.Top + margin : area.Bottom - h - margin);
            // Bulanıklık dikdörtgen olmasın: pencere bölgesini yuvarlak köşeli yap
            int pw = (int)Math.Round(w * dpi.DpiScaleX), ph = (int)Math.Round(h * dpi.DpiScaleY), r = (int)Math.Round(32 * k * dpi.DpiScaleX);
            Native.SetWindowRgn(_hwnd, Native.CreateRoundRectRgn(0, 0, pw + 1, ph + 1, r, r), true);
        }

        void Tick()
        {
            // Tam ekran oyun/video varken dock çekilir ve hiçbir şey ölçülmez (oyunlarda ek yük yok).
            bool fs = _s.Settings.HideOnFullscreen && Native.ForegroundIsFullscreen(_hwnd);
            if (fs != _hiddenForFullscreen)
            {
                _hiddenForFullscreen = fs;
                UpdateVisibility();
            }
            if (!IsVisible) return;
            _s.Tick();
            Refresh();
            RunAlerts();
        }

        public void ToggleUserHidden() { _userHidden = !_userHidden; UpdateVisibility(); }

        void UpdateVisibility()
        {
            bool show = !_userHidden && !_hiddenForFullscreen;
            if (show && !IsVisible) { Show(); Place(); }
            else if (!show && IsVisible) Hide();
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
            var input = new AlertInput(DateTimeOffset.Now, Ics.Next(_s.Events, DateTimeOffset.Now), _s.MediaPlaying,
                _s.Settings.AiSource == AiSource.Kapali ? null : _s.Ai, _s.Battery, _s.Charging);
            foreach (var a in _alerts.Evaluate(_s.Settings, input))
            {
                if (a.Action == AlertAction.PauseMedia) _s.MediaCommand("pause");
                if (!string.IsNullOrEmpty(a.Title)) _notify(a.Title, a.Body);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _timer.Stop();
            _appBar?.Remove();
            base.OnClosed(e);
        }
    }
}

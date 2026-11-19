using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using KutiDock.Core;
using KutiDock.Tiles;

namespace KutiDock
{
    /// <summary>Şeridin yeri: ayardaki kenar; ya da Mac dock açıkken altta sol/sağ köşe (Mac dock'un iki yanında).</summary>
    internal enum StripAnchor { Kenar, SolAlt, SagAlt }

    /// <summary>Ekranın altında (ya da üstünde) yüzen, yuvarlak köşeli dock penceresi.</summary>
    internal sealed class DockWindow : Window
    {
        readonly Services _s;
        readonly Action _openSettings, _exit;
        readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
        readonly AlertEngine _alerts = new();
        readonly Action<string, string> _notify;
        Border _body;
        /// <summary>1.6.10: şeridin gövdesi (kayma animasyonu bunun RenderTransform'unda; Genie hedefi bu ötelemeyi çıkarır).</summary>
        public FrameworkElement SlideBody => _body;
        StackPanel _row;
        List<Tile> _tiles = new();
        Theme _theme;
        IntPtr _hwnd;
        AppBar _appBar;
        bool _hiddenForFullscreen, _userHidden, _hiding, _autoHidden;
        // 1.6.3: kenarda saklanma (fare kenara gelince belirir)
        readonly DispatcherTimer _hoverTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
        readonly AutoHide _auto = new();
        bool AutoHideOn => _s.Settings.AutoHideDock && _anchor == StripAnchor.Kenar;
        // Tam ekran 250 ms'de bir yoklanır: video/oyun ≈0,5 sn'de gizler (2 gözlem), dock 2 sn sonra geri gelir (8 gözlem).
        readonly DispatcherTimer _fsTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
        Debounce _fullscreen = new(2, 8);
        // 1.6.8: dock'un bulunduğu monitör (saklıyken fareyi takip eder); Zero: ana ekran
        IntPtr _mon;
        // 1.6.9: ekrana sığdırma (ölçek → kompakt grup → taşma menüsü)
        List<(string Id, Tile T)> _ids = new();
        FitPlan _plan = FitPlan.Natural;
        Border _overflow; bool _fitting;
        bool FollowOn => AutoHideOn;
        IntPtr DockMonitor => FollowOn && !_s.Settings.PrimaryOnly && _mon != IntPtr.Zero ? _mon : Native.PrimaryMonitor();

        readonly List<string> _only;     // null: ayardaki tüm widget'lar
        readonly StripAnchor _anchor;
        static DockWindow _alertOwner;   // birden çok şerit varken uyarılar tek kez
        // Mac dock birleşik şeridi: şerit gerekirse daralır (asla Mac dock'un üstüne binmez), yeri Mac dock'tan gelir (piksel)
        Viewbox _fit;
        double _edgeX, _midY, _bottomY, _placeScale; bool _placed;
        double _lastNatural;
        /// <summary>Doğal genişlik belirgin değişince (Mac dock yerleşimi tazelensin).</summary>
        public Action NaturalChanged;

        public DockWindow(Services s, Action openSettings, Action exit, Action<string, string> notify, List<string> only, StripAnchor anchor)
            : this(s, openSettings, exit, notify, only, anchor, true) { }

        public DockWindow(Services s, Action openSettings, Action exit, Action<string, string> notify)
            : this(s, openSettings, exit, notify, null, StripAnchor.Kenar, true) { }

        DockWindow(Services s, Action openSettings, Action exit, Action<string, string> notify, List<string> only, StripAnchor anchor, bool _)
        {
            _only = only; _anchor = anchor;
            if (_alertOwner == null) _alertOwner = this;
            _s = s; _openSettings = openSettings; _exit = exit; _notify = notify;
            Title = "Kuti Dock";
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
            // Per-Monitor V2: başka DPI'lı ekrana geçince WPF pencereyi yeniden boyutlar; yer, yeni ölçekle tazelenir
            DpiChanged += (_, _) => Dispatcher.BeginInvoke(new Action(Place), DispatcherPriority.Loaded);
            Build();
            _timer.Tick += (_, _) => Tick();
            _timer.Start();
            _fsTimer.Tick += (_, _) => CheckFullscreen();
            _fsTimer.Start();
            _hoverTimer.Tick += (_, _) => CheckHover();
            _hoverTimer.Start();
            ContextMenu = MakeMenu();
        }

        ContextMenu MakeMenu()
        {
            var m = new ContextMenu(); Tiles.OutsideCloser.Attach(m, _s.Settings.Edge);
            void Add(string h, Action a) { var i = new MenuItem { Header = h }; i.Click += (_, _) => a(); m.Items.Add(i); }
            Add("Ayarlar…", _openSettings);
            Add("Widget'lar…", _openSettings);
            m.Items.Add(new Separator());
            Add("Görev Yöneticisi", () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("taskmgr.exe") { UseShellExecute = true }));
            Add("Masaüstünü göster", () => Native.PressKeys(Native.VK_LWIN, (byte)'D'));
            m.Items.Add(new Separator());
            Add("Kuti Dock'tan çık", _exit);
            return m;
        }

        /// <summary>Ayar değişince tüm görünümü yeniden kurar.</summary>
        public void Build()
        {
            var set = _s.Settings;
            _theme = Theme.For(set.Style);
            double k = set.Scale;
            Anim.Enabled = set.Animations;
            bool vertical = _anchor == StripAnchor.Kenar && DockLayout.Vertical(set.Edge);
            _row = vertical
                ? new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4 * k, 0, 4 * k) }
                : new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(1 * k, 0, 1 * k, 0) };
            _tiles = new List<Tile>(); _ids = new List<(string, Tile)>(); _plan = FitPlan.Natural; _overflow = null;
            foreach (var id in _only ?? set.Widgets)
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
                _tiles.Add(t); _ids.Add((id, t));
                // Dikeyde sütuna sığmayan geniş widget'lar (sistem, ağ, saat…) kompakt: orantılı küçültülür
                _row.Children.Add(vertical ? new Viewbox { Child = t, StretchDirection = StretchDirection.DownOnly, MaxWidth = 52 * k, Margin = new Thickness(0, 1 * k, 0, 1 * k) } : t);
            }
            if (vertical)
            {
                // Ekran yüksekliğine sığmazsa tüm sütun küçülür
                var fit = new Viewbox { Child = _row, StretchDirection = StretchDirection.DownOnly, MaxHeight = Math.Max(200, SystemParameters.WorkArea.Height - 16) };
                _body = Body(fit, k, vertical);
            }
            else if (set.FullWidth && _anchor == StripAnchor.Kenar)
            {
                // Tam genişlik: saat ve sonrası sağa yaslanır
                var dock = new DockPanel { LastChildFill = false };
                _row.HorizontalAlignment = HorizontalAlignment.Left;
                dock.Children.Add(_row);
                _body = Body(dock, k, false);
            }
            else _body = Body(_row, k, false);
            if (_anchor != StripAnchor.Kenar)
            {
                _fit = new Viewbox { Child = _body, StretchDirection = StretchDirection.DownOnly };
                Content = _fit;
            }
            else Content = _body;
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

        /// <summary>Daraltılmamış genişlik, verilen monitör ölçeğinde piksel (kenar boşluğu dahil).</summary>
        public double NaturalPx(double scale)
        {
            if (_body == null) return 0;
            _body.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            _lastNatural = _body.DesiredSize.Width;
            return _lastNatural <= 0 ? 0 : (_lastNatural + 8) * scale;
        }

        /// <summary>Mac dock'un verdiği yere yerleşir: monitör kenarı, izin verilen genişlik, plaka ortası ve şerit altı (piksel).</summary>
        public void PlaceAt(int screenLeft, int screenRight, double allowedPx, double midY, double bottomY, double scale)
        {
            if (_fit == null) return;
            _placeScale = scale;
            _edgeX = _anchor == StripAnchor.SolAlt ? screenLeft + 8 * scale : screenRight - 8 * scale;
            _midY = midY; _bottomY = bottomY; _placed = true;
            double dip = (allowedPx - 8 * scale) / scale;
            _fit.Visibility = dip < 24 ? Visibility.Collapsed : Visibility.Visible;
            _fit.MaxWidth = Math.Max(24, dip);
            Dispatcher.BeginInvoke(new Action(Place), DispatcherPriority.Loaded);
        }

        /// <summary>Dock'u ekranın alt (ya da üst) ortasına yerleştirir, gerekirse ekranda yer ayırır.</summary>
        void Place()
        {
            if (_hwnd == IntPtr.Zero || _body == null) return;
            if (_anchor != StripAnchor.Kenar && _placed)
            {
                var d = VisualTreeHelper.GetDpi(this);
                double wpx = ActualWidth * d.DpiScaleX, hpx = ActualHeight * d.DpiScaleY;
                double x = _anchor == StripAnchor.SolAlt ? _edgeX : _edgeX - wpx;
                double y = Math.Min(_midY - hpx / 2, _bottomY - hpx - 2 * _placeScale);
                Native.SetWindowPos(_hwnd, IntPtr.Zero, (int)Math.Round(x), (int)Math.Round(y), 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                double f = _lastNatural > 0 ? Math.Min(1, ActualWidth / _lastNatural) : 1; // daralınca köşe yarıçapı da küçülür
                int rr = (int)Math.Round(32 * _s.Settings.Scale * f * d.DpiScaleX);
                Native.SetWindowRgn(_hwnd, Native.CreateRoundRectRgn(0, 0, (int)Math.Round(wpx) + 1, (int)Math.Round(hpx) + 1, rr, rr), true);
                return;
            }
            var set = _s.Settings; double k = set.Scale, margin = 8;
            double w = ActualWidth, h = ActualHeight;
            var dpi = VisualTreeHelper.GetDpi(this);
            if (FollowOn && _anchor == StripAnchor.Kenar)
            {
                // 1.6.8: saklı dock, bulunduğu monitörün çalışma alanında (piksel, o monitörün DPI'siyle)
                _appBar.Remove();
                var mon = DockMonitor;
                if (Native.MonitorBox(mon, out _, out var work))
                {
                    uint md = Native.MonitorDpi(mon);
                    // Düzen değiştiyse SizeChanged yeniden yerleştirir; 1.6.20: pencere yine de hemen hedef monitöre alınır
                    // (eskiden burada dönülüyordu, pencere bir süre oyunun olduğu eski monitörde kalıyordu).
                    Fit((work.Right - work.Left - 2.0 * DockMonitors.ToPixels(margin, md)) * 96.0 / md);
                    int wpx = DockMonitors.ToPixels(w, md), hpx = DockMonitors.ToPixels(h, md), mpx = DockMonitors.ToPixels(margin, md);
                    var (px, py) = DockLayout.Position(set.Edge, work.Left, work.Top, work.Right - work.Left, work.Bottom - work.Top, wpx, hpx, mpx);
                    Native.SetWindowPos(_hwnd, IntPtr.Zero, (int)Math.Round(px), (int)Math.Round(py), 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                    // Bölge pencerenin şu anki gerçek boyutuyla (DPI değişimi henüz işlenmediyse eski ölçek); DpiChanged sonrası tazelenir
                    int rpx = (int)Math.Round(32 * k * dpi.DpiScaleX);
                    int aw = Native.GetWindowRect(_hwnd, out var wr) ? wr.Right - wr.Left : (int)Math.Round(w * dpi.DpiScaleX);
                    int ah = wr.Bottom - wr.Top > 0 ? wr.Bottom - wr.Top : (int)Math.Round(h * dpi.DpiScaleY);
                    Native.SetWindowRgn(_hwnd, Native.CreateRoundRectRgn(0, 0, aw + 1, ah + 1, rpx, rpx), true);
                    return;
                }
            }
            bool top = set.Edge == DockEdge.Ust;
            // Yer ayırma yalnızca yatay (Üst/Alt) dock için; Sol/Sağ her zaman yüzer.
            bool reserve = set.ReserveSpace && !DockLayout.Vertical(set.Edge) && _anchor == StripAnchor.Kenar && !AutoHideOn;
            if (reserve) _appBar.Reserve(top, (int)Math.Ceiling((h + margin * 2) * dpi.DpiScaleY));
            else _appBar.Remove();
            Rect area = reserve ? _appBar.ReservedRect(dpi.DpiScaleX, dpi.DpiScaleY) ?? SystemParameters.WorkArea : SystemParameters.WorkArea;
            if (reserve) { Left = area.Left + (area.Width - w) / 2; Top = area.Top + (area.Height - h) / 2; }
            else if (_anchor != StripAnchor.Kenar)
            {
                // Mac dock birincil ekranın alt şeridini ayırır; widget'lar o şeridin köşelerinde durur
                Left = _anchor == StripAnchor.SolAlt ? margin : SystemParameters.PrimaryScreenWidth - w - margin;
                Top = SystemParameters.PrimaryScreenHeight - h - 6;
            }
            else
            {
                if (Fit(area.Width - 2 * margin)) return;
                (Left, Top) = DockLayout.Position(set.Edge, area.Left, area.Top, area.Width, area.Height, w, h, margin);
            }
            // Bulanıklık dikdörtgen olmasın: pencere bölgesini yuvarlak köşeli yap
            int pw = (int)Math.Round(w * dpi.DpiScaleX), ph = (int)Math.Round(h * dpi.DpiScaleY), r = (int)Math.Round(32 * k * dpi.DpiScaleX);
            Native.SetWindowRgn(_hwnd, Native.CreateRoundRectRgn(0, 0, pw + 1, ph + 1, r, r), true);
        }

        void CheckFullscreen()
        {
            if (_hwnd == IntPtr.Zero) return;
            // 1.6.20: dock'un mantıksal monitörü için karar (pencerenin o anki yeri taşımadan sonra geride kalabiliyor)
            var mon = (IntPtr)DockMonitors.FullscreenProbe(FollowOn, (long)DockMonitor, (long)Native.MonitorFromWindow(_hwnd, 2));
            bool fs = _s.Settings.HideOnFullscreen && Native.ForegroundIsFullscreenOn(mon);
            if (_fullscreen.Observe(fs))
            {
                _hiddenForFullscreen = _fullscreen.State;
                Log($"tam ekran {(_hiddenForFullscreen ? "gizledi" : "bitti")}: {Native.Describe(mon)}; ön plan {Native.ForegroundSummary()}");
                // Tam ekrandan çıkınca saklı dock kendiliğinden belirmez; fare kenara gelince belirir
                if (_hiddenForFullscreen && AutoHideOn) { _auto.Reset(); _autoHidden = true; }
                UpdateVisibility();
            }
        }

        void Tick()
        {
            // Tam ekran oyun/video varken dock çekilir ve hiçbir şey ölçülmez (oyunlarda ek yük yok).
            if (!IsVisible)
            {
                // Yalnızca kenarda saklıyken ölçüm ve uyarılar sürer (tam ekranda/oyunda değil)
                if (_autoHidden && !_hiddenForFullscreen && !_userHidden) { _s.Tick(); RunAlerts(); }
                return;
            }
            _s.Tick();
            Refresh();
            if (_fit != null && NaturalChanged != null)
            {
                double before = _lastNatural;
                NaturalPx(1);
                if (Math.Abs(_lastNatural - before) > 12) NaturalChanged();
            }
            RunAlerts();
        }

        /// <summary>
        /// 1.6.10: ilk açılış Genie'si sırasında saklı dock kısa süre yay animasyonuyla belirir, sonra her zamanki gibi saklanır
        /// (Mac'teki gibi). Tam ekranda, oyun öndeyken ya da kullanıcı gizlediyse gösterilmez. Gösterildiyse true.
        /// </summary>
        public bool PeekForLaunch()
        {
            try
            {
                if (_hwnd == IntPtr.Zero) return false;
                var mon = DockMonitor;
                bool fs = Native.ForegroundIsFullscreenOn(mon);
                bool game = false;
                var fg = Native.GetForegroundWindow();
                if (fg != IntPtr.Zero)
                {
                    Native.GetWindowThreadProcessId(fg, out uint pid);
                    var path = Native.ProcessPath(pid) ?? "";
                    game = path.Length > 0 && OptimizeRules.IsGame(System.IO.Path.GetFileNameWithoutExtension(path), path);
                }
                if (!LaunchTarget.ShouldPeek(_s.Settings.Animations, AutoHideOn, _autoHidden, _userHidden, _hiddenForFullscreen, fs, game))
                {
                    if (AutoHideOn && !_autoHidden && _auto.Shown) _auto.Peek(DateTime.UtcNow.AddMilliseconds(LaunchTarget.PeekMs)); // zaten açık: Genie bitene kadar açık kalsın
                    return false;
                }
                _auto.Peek(DateTime.UtcNow.AddMilliseconds(LaunchTarget.PeekMs));
                _autoHidden = false;
                UpdateVisibility();
                UpdateLayout();
                return true;
            }
            catch (Exception) { return false; }
        }

        public void ToggleUserHidden() { _userHidden = !_userHidden; UpdateVisibility(); }

        /// <summary>Kenarda saklanma: imleç kenarda → belir; dock'tan ve panellerinden uzaklaşınca 0,6 sn sonra saklan.</summary>
        void CheckHover()
        {
            if (!AutoHideOn)
            {
                if (_autoHidden) { _autoHidden = false; UpdateVisibility(); }
                return;
            }
            if (_hwnd == IntPtr.Zero || _userHidden || !Native.GetCursorPos(out var p)) return;
            var edge = _s.Settings.Edge;
            static bool OnAny(int x, int y) => Native.MonitorAt(x, y, Native.MONITOR_DEFAULTTONULL) != IntPtr.Zero;
            // 1.6.8: fare başka monitörün dış kenarına gelince dock oraya taşınır (o monitörde tam ekran yoksa)
            var cursorMon = Native.MonitorAt(p.X, p.Y, Native.MONITOR_DEFAULTTONEAREST);
            var current = DockMonitor;
            if (cursorMon != current && Native.MonitorBox(cursorMon, out var cm, out _))
            {
                bool outer = DockMonitors.AtOuterEdge(edge, p.X, p.Y, cm, OnAny);
                bool fsThere = outer && _s.Settings.HideOnFullscreen && Native.ForegroundIsFullscreenOn(cursorMon);
                var chosen = DockMonitors.Choose((long)current, (long)cursorMon, (long)Native.PrimaryMonitor(), _s.Settings.FollowMouse,
                    _s.Settings.PrimaryOnly, outer, fsThere);
                if (chosen == (long)cursorMon) MoveTo(cursorMon);
            }
            LogEdgeArrival(p, edge, cursorMon);
            if (_hiddenForFullscreen) return;
            if (!Native.MonitorBox(DockMonitor, out var mon, out _)) return;
            bool over = IsVisible && Native.GetWindowRect(_hwnd, out var r) && new Box(r.Left, r.Top, r.Right, r.Bottom).Contains(p.X, p.Y, 12);
            if (_auto.Update(DockMonitors.AtOuterEdge(edge, p.X, p.Y, mon, OnAny), over, OwnPopupOpen(), DateTime.UtcNow, TimeSpan.FromMilliseconds(600)))
            {
                _autoHidden = !_auto.Shown;
                UpdateVisibility();
            }
        }

        bool _atEdgeLogged;
        /// <summary>
        /// 1.6.20 tanı: fare bir monitörün dış dock kenarına her gelişinde bir satır (kenarda dururken tekrar yazılmaz).
        /// Oyun açıkken dock'un neden açılmadığı hata.log'dan okunabilsin.
        /// </summary>
        void LogEdgeArrival(Native.POINT p, DockEdge edge, IntPtr cursorMon)
        {
            static bool OnAny(int x, int y) => Native.MonitorAt(x, y, Native.MONITOR_DEFAULTTONULL) != IntPtr.Zero;
            bool at = Native.MonitorBox(cursorMon, out var cm, out _) && DockMonitors.AtOuterEdge(edge, p.X, p.Y, cm, OnAny);
            if (at == _atEdgeLogged) return;
            _atEdgeLogged = at;
            if (!at) return;
            string win = Native.GetWindowRect(_hwnd, out var wr) ? $"[{wr.Left},{wr.Top},{wr.Right},{wr.Bottom}] {Native.Describe(Native.MonitorFromWindow(_hwnd, 2))}" : "?";
            Log($"kenar: fare {p.X},{p.Y} {Native.Describe(cursorMon)}; dock {Native.Describe(DockMonitor)}; pencere {win}; " +
                $"tamEkranGizli={_hiddenForFullscreen} kenardaSakli={_autoHidden} elleGizli={_userHidden} gorunur={IsVisible} panelAcik={OwnPopupOpen()}; " +
                $"ön plan {Native.ForegroundSummary()}");
        }

        /// <summary>
        /// 1.6.9: şerit <paramref name="available"/> DIP genişliğe sığsın (DockFit: ölçek ≥ %70 → kompakt grup → taşma menüsü).
        /// Düzen değiştiyse true (pencere yeniden boyutlanır, Place SizeChanged ile yeniden çağrılır).
        /// </summary>
        bool Fit(double available)
        {
            if (_fitting || _anchor != StripAnchor.Kenar || DockLayout.Vertical(_s.Settings.Edge) || _s.Settings.FullWidth || _row == null) return false;
            _fitting = true;
            try
            {
                double k = _s.Settings.Scale;
                var items = new List<FitItem>();
                foreach (var (id, t) in _ids)
                {
                    if (!t.Wanted) continue;
                    t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    double f = t.LayoutTransform is ScaleTransform st && st.ScaleX > 0 ? st.ScaleX : 1;
                    items.Add(new FitItem(id, t.DesiredSize.Width / f));
                }
                var plan = DockFit.Plan(available, items, 2 * k + 2, 44 * k);
                if (plan.SameAs(_plan) && plan.Scale >= _plan.Scale) return false; // küçülme hemen, büyüme %1 eşiğiyle (titreme olmasın)
                ApplyFit(plan);
                Dispatcher.BeginInvoke(new Action(Place), DispatcherPriority.Loaded); // boyut aynı kalsa da yeniden yerleştir
                return true;
            }
            catch (Exception) { return false; }
            finally { _fitting = false; }
        }

        void ApplyFit(FitPlan plan)
        {
            _plan = plan;
            double k = _s.Settings.Scale;
            foreach (var (_, t) in _ids) { if (t.Parent is Panel p) p.Children.Remove(t); t.LayoutTransform = null; }
            _row.Children.Clear();
            var spill = new List<Tile>();
            System.Windows.Controls.Primitives.UniformGrid group = null;
            int clockAt = -1;
            foreach (var (id, t) in _ids)
            {
                bool compactMember = DockFit.CompactIds.Contains(id);
                if (plan.Hidden.Contains(id) || (compactMember && plan.Hidden.Contains("compact"))) { spill.Add(t); continue; }
                if (plan.Compact && compactMember)
                {
                    if (group == null) { group = new System.Windows.Controls.Primitives.UniformGrid { Rows = 2, VerticalAlignment = VerticalAlignment.Center }; _row.Children.Add(group); }
                    t.LayoutTransform = new ScaleTransform(DockFit.CompactScale, DockFit.CompactScale);
                    group.Children.Add(t);
                    continue;
                }
                if (id == "clock") clockAt = _row.Children.Count;
                _row.Children.Add(t);
            }
            if (spill.Count > 0)
            {
                // "»": fazla öğeler açılır panelde (gerçek widget'lar, tıklanabilir); saat ve ses her zaman şeritte
                var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6 * k) };
                foreach (var t in spill) panel.Children.Add(t);
                var pop = new System.Windows.Controls.Primitives.Popup
                {
                    StaysOpen = false, AllowsTransparency = true, Placement = System.Windows.Controls.Primitives.PlacementMode.Top, VerticalOffset = -8,
                    Child = new Border { Child = panel, Background = _theme.DockBg, BorderBrush = _theme.DockBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14 * k) },
                };
                _overflow = new Border
                {
                    Width = 40 * k, Height = 40 * k, Margin = new Thickness(2 * k, 0, 2 * k, 0), CornerRadius = new CornerRadius(10 * k), Background = Brushes.Transparent,
                    VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, ToolTip = "Diğer widget'lar",
                    Child = new TextBlock { Text = "»", FontSize = 20 * k, Foreground = _theme.Fg, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                };
                pop.PlacementTarget = _overflow;
                _overflow.MouseLeftButtonUp += (_, e) => { pop.IsOpen = !pop.IsOpen; e.Handled = true; };
                if (clockAt >= 0) _row.Children.Insert(clockAt, _overflow); else _row.Children.Add(_overflow);
            }
            else _overflow = null;
            _row.LayoutTransform = plan.Scale < 0.999 ? new ScaleTransform(plan.Scale, plan.Scale) : null;
            Log("yerleşim: " + (plan.SameAs(FitPlan.Natural) ? "doğal" : $"ölçek {plan.Scale:0.00}{(plan.Compact ? ", kompakt grup" : "")}{(plan.Hidden.Count > 0 ? ", taşma: " + string.Join(",", plan.Hidden) : "")}"));
        }

        static void Log(string line)
        {
            try { KutiDock.Core.RotatingLog.Append(System.IO.Path.Combine(Services.DataDir, "hata.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} Dock {line}\n"); }
            catch (Exception) { }
        }

        /// <summary>1.6.8: dock'u başka monitöre taşır: açıksa hemen saklanır, yeni monitörde kenardan kayarak belirir.</summary>
        void MoveTo(IntPtr monitor)
        {
            if (OwnPopupOpen()) return; // açık panel/menü varken taşınmaz
            _mon = monitor;
            _fullscreen = new Debounce(2, 8); _hiddenForFullscreen = false; // tam ekran durumu yeni monitör için yeniden ölçülür
            if (IsVisible) { _hiding = false; Hide(); }
            _auto.Reset(); _autoHidden = true;
            Place();
        }

        /// <summary>Dock'un açtığı menü, açılır panel, önizleme ya da arama paneli açıkken dock saklanmaz.</summary>
        static bool OwnPopupOpen()
        {
            foreach (PresentationSource ps in PresentationSource.CurrentSources)
            {
                if (ps is not HwndSource hs || hs.IsDisposed || !Native.IsWindowVisible(hs.Handle)) continue;
                var rv = hs.RootVisual;
                if (rv is Tiles.WindowPreview or Tiles.SearchPanel || rv?.GetType().Name == "PopupRoot") return true;
            }
            return false;
        }

        void UpdateVisibility()
        {
            bool show = !_userHidden && !_hiddenForFullscreen && !_autoHidden;
            // Kenarda saklanırken şerit tamamen dışarı kayar; diğer durumlarda kısa kayma
            double dist = AutoHideOn ? (DockLayout.Vertical(_s.Settings.Edge) ? ActualWidth : ActualHeight) + 8 : 14;
            if (show && (!IsVisible || _hiding))
            {
                _hiding = false;
                if (!IsVisible) { Refresh(); Show(); }
                // Gizliyken monitör/çözünürlük/AppBar değişmiş olabilir: yerleşimi düzen bittikten sonra da tazele.
                Place();
                Dispatcher.BeginInvoke(new Action(Place), DispatcherPriority.Loaded);
                Anim.Slide(this, _body, _s.Settings.Edge, true, null, dist);
            }
            else if (!show && IsVisible && !_hiding)
            {
                _hiding = true;
                Anim.Slide(this, _body, _s.Settings.Edge, false, () => { if (_hiding) { _hiding = false; Hide(); } }, dist);
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
            if (_alertOwner != this) return;
            var input = new AlertInput(DateTimeOffset.Now, _s.GrokBotLimit().Limit, _s.Settings.Cursor, _s.Battery, _s.Charging);
            foreach (var a in _alerts.Evaluate(_s.Settings, input))
                if (!string.IsNullOrEmpty(a.Title)) _notify(a.Title, a.Body);
        }

        protected override void OnClosed(EventArgs e)
        {
            _timer.Stop(); _fsTimer.Stop(); _hoverTimer.Stop();
            if (_alertOwner == this) _alertOwner = null;
            _appBar?.Remove();
            base.OnClosed(e);
        }
    }
}

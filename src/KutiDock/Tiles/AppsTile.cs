using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Ellipse = System.Windows.Shapes.Ellipse;
using KutiDock.Core;

namespace KutiDock.Tiles
{
    /// <summary>
    /// Windows görev çubuğuna sabitlenmiş uygulamalar + açık pencereler. Aynı exe tek simgede toplanır.
    /// Tıkla: kapalıysa aç, arkadaysa öne getir, zaten öndeyse küçült. Sağ tık: pencere listesi.
    /// </summary>
    internal sealed class AppsTile : Tile
    {
        public override string Id => "apps";
        readonly StackPanel _panel = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        readonly Func<IntPtr> _self;
        string _signature = "";

        // macOS büyütmesi ve açılış sıçraması (Gravitone'dan uyarlandı, MIT; bkz. Core/Motion.cs)
        sealed class Slot { public FrameworkElement Outer; public ScaleTransform Scale; public TranslateTransform Lift; public Image Img; public string Key; public double Cur = 1, Vel;
            public TranslateTransform Shift; public ScaleTransform Poof; } // 1.6.24: sürükleme aralığı ve kaldırma "puf"u
        ZoomLayer _zoom;   // büyüyen simgeler dock'un üstünde buraya çizilir (kesilmez, şerit zıplamaz)
        readonly List<Slot> _slots = new();
        readonly Dictionary<string, Bouncer> _bounce = new();
        readonly Stopwatch _clock = Stopwatch.StartNew();
        double _last; bool _looping;

        sealed class AppItem
        {
            public string Key, Name, Launch, IconPath; public bool Pinned;
            public string TvTool; public bool? Tv;   // 1.6.12: TV Aç-Kapat kısayolu (TvEkranGecis.exe) ve TV'nin durumu
            public List<IntPtr> Windows = new();
        }

        public AppsTile(Services s, Theme t, Func<IntPtr> self) : base(s, t, card: false)
        {
            _self = self; Height = 48 * K;
            // 1.6.24: sürükle-bırak ile sabitleme; bırakma yeri göstergesi simgelerin üstünde ayrı katmanda
            Child = new Grid { Children = { _panel, _overlay } };
            _panel.Background = Brushes.Transparent; // simgeler arasındaki boşluğa da bırakılabilsin
            AllowDrop = true;
            DragEnter += OnDragOver; DragOver += OnDragOver; DragLeave += OnDragLeave; Drop += OnDrop;
            if (DockLayout.Vertical(s.Settings.Edge))
            {
                _panel.Orientation = Orientation.Vertical; _panel.HorizontalAlignment = HorizontalAlignment.Center;
                Height = double.NaN; Width = 50 * K;
            }
            // 1.6.4: pencerenin kendi küçültme düğmesinde de Genie (simgeye doğru)
            MinimizeGenie.Target = TargetFor;
            MinimizeGenie.Enabled = () => S.Settings.Animations;
            MinimizeGenie.Start();
            // 1.6.7: ilk açılış Genie'si sistem genelinde (dock dışından açılanlar da): pencere hangi simgeye bağlanır, açık mı, simge nerede
            LaunchGenie.ResolveKey = ResolveKey;
            LaunchGenie.HasOpenWindows = (key, except) => _slots.Any(sl => sl.Outer.Tag is AppItem it && AppGrouping.SameKey(it.Key, key)
                && it.Windows.Any(h => h != except && Native.IsWindowVisible(h)));
            LaunchGenie.IconFor = key => () => IconForKey(key);
            LaunchGenie.Enabled = () => S.Settings.Animations;
            LaunchGenie.Peek = () => Window.GetWindow(this) is DockWindow dw && dw.PeekForLaunch(); // 1.6.10
            LaunchGenie.Start();
            MouseMove += (_, _) => Loop(true);
            MouseLeave += (_, _) => Loop(true);
        }

        void Loop(bool on)
        {
            if (on == _looping) return;
            _looping = on;
            if (on) { _last = _clock.Elapsed.TotalSeconds; CompositionTarget.Rendering += Frame; }
            else CompositionTarget.Rendering -= Frame;
        }

        /// <summary>Her karede: imlece uzaklığa göre kosinüs büyütmesi (yumuşak yaklaşma) ve sıçrama yüksekliği.</summary>
        void Frame(object sender, EventArgs e)
        {
            double now = _clock.Elapsed.TotalSeconds, dt = Math.Clamp(now - _last, 0, 0.1); _last = now;
            bool vertical = DockLayout.Vertical(S.Settings.Edge);
            var edge = S.Settings.Edge;
            double icon = Magnify.ClassicIcon * K, max = Magnify.ClassicMax * K;
            // 1.6.27: katman (ayrı pencere) yalnızca dock tam görünürken; saklanırken/gizliyken büyütme anında sıfırlanır
            bool layerOk = Window.GetWindow(this) is DockWindow dw ? dw.SteadyShown : IsVisible;
            bool over = ZoomLayerPolicy.Magnify(IsMouseOver, S.Settings.Magnification, Dragging, layerOk); // 1.6.24: sürüklerken büyütme yok
            var m = Mouse.GetPosition(_panel);
            bool busy = over;
            var (ox, oy) = DockLayout.ZoomOrigin(edge);
            var draw = new List<ZoomLayer.Item>();
            foreach (var sl in _slots)
            {
                if (!sl.Outer.IsLoaded) continue;
                double target = 1;
                if (!layerOk) { sl.Cur = 1; sl.Vel = 0; }
                if (over)
                {
                    var c = sl.Outer.TranslatePoint(new Point(sl.Outer.ActualWidth / 2, sl.Outer.ActualHeight / 2), _panel); // yerleşim konumu: büyütmeden etkilenmez
                    target = Magnify.Scale(vertical ? m.Y - c.Y : m.X - c.X, icon, max, 1);
                }
                // 1.6.5: yay (Apple): imleç hızla gezinse de hız korunur, büyütme kopmadan yeni hedefe akar
                (sl.Cur, sl.Vel) = Spring.Step(sl.Cur, sl.Vel, target, dt, target > sl.Cur ? AppleMotion.MagnifyGrow : AppleMotion.MagnifyShrink);
                if (sl.Cur < 1) { sl.Cur = 1; sl.Vel = Math.Max(0, sl.Vel); }
                if (Spring.Settled(sl.Cur, sl.Vel, target)) { sl.Cur = target; sl.Vel = 0; }
                if (sl.Cur != 1) busy = true;
                double lift = 0;
                if (_bounce.TryGetValue(sl.Key, out var b) && b.IsActive) { lift = b.Offset(now) * 18 * K; busy = true; }
                double lx = edge == DockEdge.Sol ? lift : edge == DockEdge.Sag ? -lift : 0;
                double ly = edge == DockEdge.Alt ? -lift : edge == DockEdge.Ust ? lift : 0;
                draw.Add(new ZoomLayer.Item(sl.Img, sl.Img.Source, sl.Cur, lx, ly, ox, oy));
            }
            // Büyüyen ya da zıplayan simge varken hepsi katmanda çizilir; katman kullanılamazsa eski yol (dock içinde dönüşüm).
            bool lifted = draw.Exists(d => d.Scale > 1.0005 || d.LiftX != 0 || d.LiftY != 0);
            bool drawn = lifted && layerOk && (_zoom ??= new ZoomLayer()).Draw(this, draw, 46 * K);
            if (ZoomLayerPolicy.KeepLayer(lifted, layerOk, drawn))
            {
                foreach (var sl in _slots) { sl.Img.Opacity = 0; sl.Scale.ScaleX = sl.Scale.ScaleY = 1; sl.Lift.X = sl.Lift.Y = 0; }
            }
            else
            {
                _zoom?.Clear(); // 1.6.27: çizilmeyen karede eski büyümüş resim ekranda kalmaz (dock gizlenince takılı kalıyordu)
                for (int i = 0; i < _slots.Count && i < draw.Count; i++)
                {
                    var sl = _slots[i]; sl.Img.Opacity = 1;
                    sl.Scale.ScaleX = sl.Scale.ScaleY = lifted ? sl.Cur : 1;
                    sl.Lift.X = lifted ? draw[i].LiftX : 0; sl.Lift.Y = lifted ? draw[i].LiftY : 0;
                }
            }
            if (!busy) { _zoom?.Clear(); foreach (var sl in _slots) sl.Img.Opacity = 1; Loop(false); }
        }

        /// <summary>
        /// 1.6.27: dock saklanmaya başlarken/gizlenirken çağrılır: büyütme ve katman hemen sıfırlanır, simgeler dock'un içinde
        /// normal boyutta kalır (dock ile birlikte kayar/solar). Süren açılış sıçraması dock içinde devam eder.
        /// </summary>
        public void ResetZoom()
        {
            _zoom?.Clear();
            foreach (var sl in _slots)
            {
                sl.Cur = 1; sl.Vel = 0; sl.Img.Opacity = 1;
                sl.Scale.ScaleX = sl.Scale.ScaleY = 1; sl.Lift.X = sl.Lift.Y = 0;
            }
            if (!_bounce.Values.Any(b => b.IsActive)) Loop(false);
        }

        static string PinnedDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");

        List<AppItem> _pinnedCache; DateTime _pinnedAt;

        List<AppItem> Pinned()
        {
            if (_pinnedCache != null && (DateTime.Now - _pinnedAt).TotalSeconds < 30) return _pinnedCache;
            var list = new List<AppItem>();
            try
            {
                foreach (var lnk in Pins.Ordered()) // 1.6.24: .url da; sıra yine oluşturulma zamanı
                    {
                        var (key, target) = LinkInfo(lnk);
                        list.Add(new AppItem
                        {
                            Key = key, Name = Path.GetFileNameWithoutExtension(lnk),
                            Launch = lnk, IconPath = lnk, Pinned = true,
                            TvTool = TvStatus.IsTvTool(target) ? target : null,
                        });
                    }
            }
            catch (Exception) { }
            _pinnedAt = DateTime.Now;
            return _pinnedCache = list;
        }

        /// <summary>Kısayolun dock anahtarı ve hedef exe'si (Gezgin kısayolunda hedef yok sayılır).</summary>
        static (string Key, string Target) LinkInfo(string lnk)
        {
            var target = ShellIcons.LinkTarget(lnk);
            if (target != null && Path.GetFileName(target).Equals("explorer.exe", StringComparison.OrdinalIgnoreCase)) target = null;
            var (args, linkAumid) = ShellIcons.LinkIdentity(lnk);
            var appsAumid = target == null ? ShellIcons.LinkAppsFolderAumid(lnk) : null; // 1.6.24: Mağaza uygulaması kısayolu
            return (AppGrouping.PinnedKey(lnk, target, args, linkAumid, appsAumid), target);
        }

        public override void Refresh()
        {
            if (Dragging) return; // 1.6.24: sürükleme bitene kadar simgeler yeniden kurulmaz
            var items = Pinned().Select(p => new AppItem { Key = p.Key, Name = p.Name, Launch = p.Launch, IconPath = p.IconPath, Pinned = true, TvTool = p.TvTool }).ToList();
            // 1.6.12: TV Aç-Kapat simgesi TV'nin açık/kapalı durumunu gösterir (CCD; ekran değişince hemen tazelenir)
            foreach (var p in items) if (p.TvTool != null) p.Tv = TvDisplay.State(p.TvTool);
            var byKey = items.ToDictionary(i => i.Key, i => i);
            foreach (var h in Native.TaskbarWindows(_self()))
            {
                Native.GetWindowThreadProcessId(h, out uint pid);
                var path = Native.ProcessPath(pid);
                if (path == null) continue;
                // Paketli uygulamalar (Ayarlar, Not Defteri vb.): simge ve başlatma AppUserModelID ile.
                // Eskiden ApplicationFrameHost pencereleri simgesiz kalıp baş harf ("A") gösteriliyordu.
                bool frame = Path.GetFileName(path).Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase);
                bool packaged = frame || path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase);
                // 1.6.6: her pencerenin AppUserModelID'sine bakılır: Chrome'dan kurulan uygulamalar (WhatsApp vb.) kendi simgesine bağlanır
                string aumid = Native.AppUserModelId(h);
                string key = AppGrouping.WindowKey(path, aumid, packaged), name, launch, icon;
                key = ResolveKey(key, Native.WindowTitle(h), Native.ClassName(h).StartsWith("Chrome_WidgetWin", StringComparison.Ordinal)); // 1.6.7
                if (key.StartsWith("crx:")) { launch = icon = @"shell:AppsFolder\" + aumid; name = Native.WindowTitle(h); }
                else if (key.StartsWith("aumid:")) { launch = icon = @"shell:AppsFolder\" + aumid; name = frame ? Native.WindowTitle(h) : Services.AppName(path); }
                else if (frame) continue; // kimliği çözülemeyen boş UWP çerçevesi (askıdaki uygulama) gösterilmez
                else { launch = icon = path; name = Services.AppName(path); }
                if (!byKey.TryGetValue(key, out var it))
                {
                    it = new AppItem { Key = key, Name = name, Launch = launch, IconPath = icon };
                    byKey[key] = it; items.Add(it);
                }
                it.Windows.Add(h);
            }
            // Açılan uygulama penceresini gösterince sıçrama biter
            foreach (var it in items)
                if (it.Windows.Count > 0 && _bounce.TryGetValue(it.Key, out var b) && b.IsActive) b.Stop(_clock.Elapsed.TotalSeconds);
            // Görünüm yalnızca liste değişince yeniden kurulur.
            var fg = Native.GetForegroundWindow();
            var sig = string.Join("|", items.Select(i => i.Key + ":" + i.Windows.Count + (i.Windows.Contains(fg) ? "*" : "")
                + (i.TvTool != null ? "#tv" + (i.Tv?.ToString() ?? "?") : "")));
            if (sig == _signature) { UpdateHandles(items); return; }
            _signature = sig;
            _panel.Children.Clear(); _slots.Clear(); _zoom?.Clear();
            foreach (var it in items) _panel.Children.Add(MakeButton(it, it.Windows.Contains(fg)));
            if (_bounce.Values.Any(b => b.IsActive)) Loop(true);
        }

        void UpdateHandles(List<AppItem> items)
        {
            int i = 0;
            foreach (FrameworkElement el in _panel.Children) { if (i < items.Count) el.Tag = items[i]; i++; }
        }

        FrameworkElement MakeButton(AppItem item, bool active)
        {
            double size = 42 * K;
            var source = ShellIcons.Get(item.IconPath) ?? ShellIcons.Generic;
            if (item.TvTool != null && item.Tv == false) source = Dimmed(source); // kapalı TV: soluk, gri simge
            var img = new Image { Width = 28 * K, Height = 28 * K, Source = source, SnapsToDevicePixels = true };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            UIElement icon = img;
            var scale = new ScaleTransform(1, 1); var lift = new TranslateTransform();
            FrameworkElement outer = null;
            var edge = S.Settings.Edge;
            var (ox, oy) = DockLayout.ZoomOrigin(edge); // büyüme ekranın içine doğru
            var iconHost = new Border { Child = icon, RenderTransform = new TransformGroup { Children = { scale, lift } }, RenderTransformOrigin = new Point(ox, oy) };
            var dot = new Border
            {
                Width = (active ? 10 : 4) * K, Height = 4 * K, CornerRadius = new CornerRadius(2 * K), Background = Theme.Accent,
                VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 1 * K),
                Visibility = item.Windows.Count > 0 ? Visibility.Visible : Visibility.Hidden,
            };
            if (DockLayout.Vertical(edge))
            {
                // Dikey dock: çalışma noktası dock'un dış kenarında, dikey çizgi
                dot.Width = 4 * K; dot.Height = (active ? 10 : 4) * K; dot.VerticalAlignment = VerticalAlignment.Center;
                dot.HorizontalAlignment = edge == DockEdge.Sol ? HorizontalAlignment.Left : HorizontalAlignment.Right;
                dot.Margin = edge == DockEdge.Sol ? new Thickness(1 * K, 0, 0, 0) : new Thickness(0, 0, 1 * K, 0);
            }
            bool card = item.Windows.Count == 0; // sitedeki gibi: çalışmayan sabitlenmiş uygulama kendi kartında
            if (card) size = 39 * K;
            var grid = new Grid { Width = size, Height = (card ? 39 : 46) * K };
            grid.Children.Add(iconHost); grid.Children.Add(dot);
            if (item.TvTool != null) AddTvBadge(grid, item.Tv);
            var btn = new Border
            {
                Width = size, Height = (card ? 39 : 46) * K, CornerRadius = new CornerRadius((card ? 13 : 10) * K), Background = Brushes.Transparent, Child = grid,
                Tag = item, Cursor = Cursors.Hand,
                ToolTip = item.TvTool != null ? TvStatus.Tooltip(item.Tv) : WindowPicker.Tooltip(item.Windows.Count, item.Windows.Count > 0 ? Native.WindowTitle(item.Windows[0]) : null, item.Name), // 1.6.25
            };
            outer = btn;
            if (card)
            {
                outer = new Border
                {
                    Width = 50 * K, Height = 48 * K, CornerRadius = new CornerRadius(16 * K), Background = T.TileBg, BorderBrush = T.TileBorder,
                    BorderThickness = new Thickness(1), Child = btn, Tag = item,
                    Margin = DockLayout.Vertical(edge) ? new Thickness(0, 2 * K, 0, 2 * K) : new Thickness(2 * K, 0, 2 * K, 0),
                };
            }
            var shift = new TranslateTransform(); var poof = new ScaleTransform(1, 1);
            outer.RenderTransform = new TransformGroup { Children = { poof, shift } }; outer.RenderTransformOrigin = new Point(0.5, 0.5);
            _slots.Add(new Slot { Outer = outer, Scale = scale, Lift = lift, Img = img, Key = item.Key, Shift = shift, Poof = poof });
            // Windows 11 gibi: üzerinde biraz bekleyince açık pencerelerin canlı önizlemesi
            var hover = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            hover.Tick += (_, _) => { hover.Stop(); if (btn.IsMouseOver) Preview((AppItem)outer.Tag, btn); };
            btn.MouseEnter += (_, _) => { btn.Background = T.TileHover; hover.Start(); };
            btn.MouseLeave += (_, _) => { btn.Background = Brushes.Transparent; hover.Stop(); };
            // Basıldığı andaki ön pencere (Gravitone gibi): bırakana kadar önizleme/menü odağı değiştirse bile doğru karar
            btn.PreviewMouseLeftButtonDown += (_, e) => { _fgAtPress = Native.GetForegroundWindow(); _press = outer; _pressAt = e.GetPosition(_panel); };
            btn.MouseMove += (_, e) => DragMove(outer, btn, e);
            btn.LostMouseCapture += (_, _) => { if (_drag != null && !_dragEnding) CancelDrag(); };
            btn.MouseLeftButtonUp += (_, e) => { e.Handled = true; if (EndDrag(outer, btn, e)) return; Click((AppItem)outer.Tag, btn); };
            btn.MouseRightButtonUp += (_, e) => { WindowPreview.CloseCurrent(); Menu((AppItem)outer.Tag, btn); e.Handled = true; };
            return outer;
        }

        IntPtr _fgAtPress;

        // Apple sistem renkleri: yeşil (açık), gri (kapalı)
        static readonly Brush TvOnBrush = Frozen(Color.FromRgb(0x30, 0xD1, 0x58));
        static readonly Brush TvOffBrush = Frozen(Color.FromRgb(0x8E, 0x8E, 0x93));
        static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        /// <summary>1.6.12: TV açıkken simgenin sağ üstünde yeşil nokta; kapalıyken altta küçük gri "kapalı" rozeti.</summary>
        void AddTvBadge(Grid grid, bool? on)
        {
            if (on == true)
            {
                grid.Children.Add(new Ellipse
                {
                    Width = 9 * K, Height = 9 * K, Fill = TvOnBrush, Stroke = Frozen(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)), StrokeThickness = 1.3 * K,
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3 * K, 3 * K, 0),
                    IsHitTestVisible = false,
                });
            }
            else if (TvStatus.Badge(on) is { } text)
            {
                grid.Children.Add(new Border
                {
                    Background = TvOffBrush, CornerRadius = new CornerRadius(4 * K), Padding = new Thickness(3.5 * K, 0, 3.5 * K, 0.5 * K),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 0.5 * K),
                    IsHitTestVisible = false,
                    Child = new TextBlock { Text = text, FontSize = 7.5 * K, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White },
                });
            }
        }

        /// <summary>Simgenin gri ve soluk kopyası (saydamlık korunur); büyütme katmanı da aynı görüntüyü çizer.</summary>
        static ImageSource Dimmed(ImageSource src)
        {
            if (src is not BitmapSource bs) return src;
            try
            {
                var rect = new Rect(0, 0, bs.Width, bs.Height);
                var gray = new FormatConvertedBitmap(bs, PixelFormats.Gray8, null, 0);
                var group = new DrawingGroup { Opacity = 0.42, OpacityMask = new ImageBrush(bs) };
                group.Children.Add(new ImageDrawing(gray, rect));
                var di = new DrawingImage(group);
                di.Freeze();
                return di;
            }
            catch (Exception) { return src; }
        }

        /// <summary>1.6.12: TV Aç-Kapat: pencere açılmaz, sıçrama/Genie yok; düzen değişikliği bitince durum yeniden okunur.</summary>
        void ToggleTv(AppItem it)
        {
            try { Process.Start(new ProcessStartInfo(it.Launch) { UseShellExecute = true }); } catch (Exception) { return; }
            foreach (var ms in TvStatus.RecheckAfterClickMs)
            {
                var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
                var tool = it.TvTool;
                t.Tick += (_, _) => { t.Stop(); TvDisplay.Read(tool); _signature = ""; try { Refresh(); } catch (Exception) { } };
                t.Start();
            }
        }

        void Click(AppItem it, FrameworkElement target)
        {
            if (it.TvTool != null && it.Launch != null) { ToggleTv(it); return; }
            var alive = it.Windows.Where(Native.IsWindowVisible).ToList();
            if (alive.Count == 0) { Launch(it, target); return; }
            var fg = _fgAtPress != IntPtr.Zero ? _fgAtPress : Native.GetForegroundWindow();
            _fgAtPress = IntPtr.Zero;
            // 1.6'daki Genie (Mac dock gibi): öndeki pencere simgeye akarak küçülür; dock'tan küçültülmüş pencere simgeden
            // büyüyerek gelir. Genie olmazsa normal küçültme/öne getirme.
            int front = alive.IndexOf(fg);
            // 1.6.25: birden çok pencere (iki Gezgin klasörü gibi): her zaman seçici açılır, öndeki küçültülmez
            if (WindowPicker.Decide(alive.Count, front >= 0) == AppClick.Picker) { Preview(it, target); return; }
            if (front >= 0)
            {
                WindowPreview.CloseCurrent();
                if (!Genie(fg, target, false)) Native.ShowWindow(fg, Native.SW_MINIMIZE);
                _signature = "";
                return;
            }
            var back = alive.FirstOrDefault(h => Native.IsIconic(h) && KutiDock.Mac.Core.ClassicGenie.HasCapture(h));
            if (back != IntPtr.Zero && Genie(back, target, true)) { WindowPreview.CloseCurrent(); _signature = ""; return; }
            WindowPreview.CloseCurrent();
            if (!(Native.IsIconic(alive[0]) && Genie(alive[0], target, true))) Native.Activate(alive[0]);
            _signature = ""; // aktif göstergesini tazele
        }

        /// <summary>Pencerenin dock'taki simgesi (ekran pikseli). Simgesi henüz yoksa (dock saklıyken açılan uygulama) şeridin ortası.</summary>
        (KutiDock.Mac.Interop.RECT, KutiDock.Mac.Core.DockEdge, IntPtr)? TargetFor(IntPtr hwnd)
        {
            try
            {
                var edge = S.Settings.Edge switch
                {
                    DockEdge.Alt => KutiDock.Mac.Core.DockEdge.Bottom, DockEdge.Sol => KutiDock.Mac.Core.DockEdge.Left,
                    DockEdge.Sag => KutiDock.Mac.Core.DockEdge.Right, _ => (KutiDock.Mac.Core.DockEdge?)null,
                };
                if (edge == null || PresentationSource.FromVisual(this) == null) return null;
                FrameworkElement el = hwnd == IntPtr.Zero ? null : _slots.FirstOrDefault(sl => sl.Outer.Tag is AppItem it && it.Windows.Contains(hwnd))?.Outer;
                var r = el != null ? ScreenRect(el) : null;
                // 1.6.10: simgesi henüz olmayan uygulama: simgenin dock'ta belireceği yer (son simgenin ardı)
                r ??= NewSlotRect();
                return r == null ? null : (r.Value, edge.Value, _self());
            }
            catch (Exception) { return null; }
        }

        /// <summary>1.6.7: pencere anahtarını sabitlenmiş simgeye çözer (kısaltılmış Chrome kimliği, PWA başlığı).</summary>
        string ResolveKey(string key, string title, bool chromium) =>
            AppGrouping.Resolve(key, title, chromium, Pinned().Select(p => (p.Key, p.Name)));

        /// <summary>1.6.7: anahtarın dock simgesi; yoksa (dock'ta olmayan uygulama) şerit yenilenir ve yeni simge kullanılır.</summary>
        (KutiDock.Mac.Interop.RECT, KutiDock.Mac.Core.DockEdge, IntPtr)? IconForKey(string key)
        {
            FrameworkElement Find() => _slots.FirstOrDefault(sl => sl.Outer.Tag is AppItem it && AppGrouping.SameKey(it.Key, key))?.Outer;
            var el = Find();
            if (el == null) { try { Refresh(); UpdateLayout(); } catch (Exception) { } el = Find(); }
            return el != null ? IconTarget(el) : TargetFor(IntPtr.Zero);
        }

        /// <summary>Simgenin ekran dikdörtgeni, kenar ve dock penceresi (Genie hedefi); dock üstteyse ya da simge görünmüyorsa null.</summary>
        (KutiDock.Mac.Interop.RECT, KutiDock.Mac.Core.DockEdge, IntPtr)? IconTarget(FrameworkElement target)
        {
            try
            {
                var edge = S.Settings.Edge switch
                {
                    DockEdge.Alt => KutiDock.Mac.Core.DockEdge.Bottom, DockEdge.Sol => KutiDock.Mac.Core.DockEdge.Left,
                    DockEdge.Sag => KutiDock.Mac.Core.DockEdge.Right, _ => (KutiDock.Mac.Core.DockEdge?)null,
                };
                // 1.6.10: dock otomatik gizliyken de (görünmez pencere) simgenin açıldığındaki yeri hesaplanır
                if (edge == null) return null;
                var r = ScreenRect(target);
                return r == null ? null : (r.Value, edge.Value, _self());
            }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// 1.6.10: öğenin dock açıkken duracağı ekran dikdörtgeni (piksel). Pencere gizli olsa da çalışır:
        /// dock penceresinin ekrandaki konumu + öğenin pencere içindeki yeri × DPI; kayma animasyonunun ötelemesi çıkarılır.
        /// </summary>
        KutiDock.Mac.Interop.RECT? ScreenRect(FrameworkElement el)
        {
            if (el == null || el.ActualWidth <= 0 || PresentationSource.FromVisual(el) == null) return null;
            var w = Window.GetWindow(el);
            if (w == null) return null;
            var hwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero || !Native.GetWindowRect(hwnd, out var wr)) return null;
            var tl = el.TransformToVisual(w).Transform(new Point(0, 0));
            var br = el.TransformToVisual(w).Transform(new Point(el.ActualWidth, el.ActualHeight));
            double sx = 0, sy = 0;
            if (w is DockWindow dw && dw.SlideBody?.RenderTransform is TranslateTransform tt && dw.SlideBody.IsDescendantOf(w) && el.IsDescendantOf(dw.SlideBody))
            {
                var g = dw.SlideBody.TransformToVisual(w);
                var o = g.Transform(new Point(0, 0)); var m = g.Transform(new Point(tt.X, tt.Y));
                sx = m.X - o.X; sy = m.Y - o.Y; // A·t: kaymanın pencere koordinatındaki karşılığı
            }
            double scale = VisualTreeHelper.GetDpi(w).DpiScaleX;
            var b = LaunchTarget.ToScreen(wr.Left, wr.Top, Math.Min(tl.X, br.X), Math.Min(tl.Y, br.Y), Math.Abs(br.X - tl.X), Math.Abs(br.Y - tl.Y), scale, sx, sy);
            return new KutiDock.Mac.Interop.RECT(b.Left, b.Top, b.Right, b.Bottom);
        }

        /// <summary>1.6.10: yeni uygulamanın simgesinin belireceği yer: son simgenin ardı; simge yoksa şeridin ortası.</summary>
        KutiDock.Mac.Interop.RECT? NewSlotRect()
        {
            var last = _slots.LastOrDefault()?.Outer;
            var lr = last != null ? ScreenRect(last) : null;
            var self = ScreenRect(this);
            if (lr == null && self == null) return null;
            (double X, double Y, double W, double H)? prev = lr is { } q ? (q.Left, q.Top, q.Right - q.Left, q.Bottom - q.Top) : null;
            var s = self ?? lr.Value;
            double size = 42 * K * (Window.GetWindow(this) is Window w ? VisualTreeHelper.GetDpi(w).DpiScaleX : 1);
            var n = LaunchTarget.NextSlot(prev, DockLayout.Vertical(S.Settings.Edge), (s.Left + s.Right) / 2.0, (s.Top + s.Bottom) / 2.0, size);
            return new KutiDock.Mac.Interop.RECT((int)Math.Round(n.X), (int)Math.Round(n.Y), (int)Math.Round(n.X + n.W), (int)Math.Round(n.Y + n.H));
        }

        bool Genie(IntPtr hwnd, FrameworkElement target, bool restore)
        {
            if (!S.Settings.Animations) return false;
            try
            {
                var edge = S.Settings.Edge switch
                {
                    DockEdge.Alt => KutiDock.Mac.Core.DockEdge.Bottom, DockEdge.Sol => KutiDock.Mac.Core.DockEdge.Left,
                    DockEdge.Sag => KutiDock.Mac.Core.DockEdge.Right, _ => (KutiDock.Mac.Core.DockEdge?)null,
                };
                if (edge == null || !target.IsVisible) return false;
                var a = target.PointToScreen(new Point(0, 0));
                var b = target.PointToScreen(new Point(target.ActualWidth, target.ActualHeight));
                var r = new KutiDock.Mac.Interop.RECT((int)Math.Min(a.X, b.X), (int)Math.Min(a.Y, b.Y), (int)Math.Max(a.X, b.X), (int)Math.Max(a.Y, b.Y));
                return restore ? KutiDock.Mac.Core.ClassicGenie.Restore(hwnd, r, edge.Value, _self())
                               : KutiDock.Mac.Core.ClassicGenie.Minimize(hwnd, r, edge.Value, _self());
            }
            catch (Exception) { return false; }
        }

        void Preview(AppItem it, FrameworkElement anchor)
        {
            if (it.Windows.Count == 0) return;
            WindowPreview.Open(it.Key, it.Windows, img(it) ?? ShellIcons.Generic, anchor, S.Settings.Edge);
        }

        void Launch(AppItem it, FrameworkElement anchor = null)
        {
            if (it.Launch == null) return;
            if (it.Windows.Count == 0 && S.Settings.Animations)
            {
                if (!_bounce.TryGetValue(it.Key, out var b)) _bounce[it.Key] = b = new Bouncer();
                b.Start(BounceKind.Launch, _clock.Elapsed.TotalSeconds);
                Loop(true);
                // 1.6.6: ilk açılışta da pencere simgeden Genie ile büyür (PWA, uygulama, dosya, klasör)
                if (anchor != null) LaunchGenie.Expect(it.Key, () => IconTarget(anchor));
            }
            try { Process.Start(new ProcessStartInfo(it.Launch) { UseShellExecute = true }); } catch (Exception) { }
        }

        /// <summary>Sağ tık: pencere listesi, yeni pencere, kapat.</summary>
        void Menu(AppItem it, FrameworkElement target)
        {
            var m = new ContextMenu(); OutsideCloser.Attach(m, S.Settings.Edge);
            var fg = Native.GetForegroundWindow();
            var wins = it.Windows.Where(Native.IsWindowVisible).ToList();
            foreach (var h in wins)
            {
                var title = Native.WindowTitle(h);
                var mi = new MenuItem { Header = title.Length > 60 ? title[..60] + "…" : title, FontWeight = h == fg ? FontWeights.SemiBold : FontWeights.Normal };
                if (img(it) is { } src) mi.Icon = new Image { Source = src, Width = 16, Height = 16 };
                mi.Click += (_, _) => { Native.Activate(h); _signature = ""; };
                m.Items.Add(mi);
            }
            if (it.Windows.Count > 0) m.Items.Add(new Separator());
            if (it.Launch != null)
            {
                var n = new MenuItem { Header = it.Windows.Count > 0 ? "Yeni pencere" : "Aç" };
                n.Click += (_, _) => Launch(it); m.Items.Add(n);
            }
            if (it.Windows.Count > 0)
            {
                var c = new MenuItem { Header = it.Windows.Count > 1 ? "Tüm pencereleri kapat" : "Pencereyi kapat" };
                c.Click += (_, _) => { foreach (var h in it.Windows) Native.PostMessage(h, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero); };
                m.Items.Add(c);
            }
            // 1.6.24: kullanıcının kendi sabitlemesi
            if (it.Pinned || it.Launch != null)
            {
                if (m.Items.Count > 0 && m.Items[^1] is not Separator) m.Items.Add(new Separator());
                var pin = new MenuItem { Header = it.Pinned ? "Dock'tan kaldır" : "Dock'a sabitle" };
                pin.Click += (_, _) => { if (it.Pinned) Unpin(it); else PinRunning(it); };
                m.Items.Add(pin);
            }
            // 1.6.13: donmuş uygulama için "Görevi sonlandır" (onaysız, en altta, kırmızı). Korumalı süreçlerde gösterilmez.
            if (it.Windows.Count > 0 && TaskKiller.Kind(it.Key, it.Windows) != KillKind.None)
            {
                m.Items.Add(new Separator());
                var k = new MenuItem { Header = TaskKill.MenuText, Foreground = KillBrush };
                var key = it.Key; var name = it.Name; var allWins = it.Windows.ToList();
                k.Click += (_, _) => { WindowPreview.CloseCurrent(); TaskKiller.End(key, name, allWins); _signature = ""; };
                m.Items.Add(k);
            }
            m.PlacementTarget = target;
            m.Placement = Pop.Placement(S.Settings.Edge);
            m.IsOpen = true;
        }

        // ───────────── 1.6.24: kullanıcının sabitlemesi (sağ tık, sürükle-bırak, sıralama, dışarı sürükleyip kaldırma) ─────────────
        // Sabitlenmiş uygulamalar yine görev çubuğunun "User Pinned\TaskBar" kısayollarıdır (bkz. Core/PinFolder.cs).

        static string RemovedDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KutiDock", "kaldirilan-sabitler");
        static PinFolder Pins => new(PinnedDir, RemovedDir);

        /// <summary>Sürükleme sürerken dock kenara saklanmaz ve simgeler yeniden kurulmaz.</summary>
        public static bool Dragging { get; private set; }

        readonly Canvas _overlay = new() { IsHitTestVisible = false };
        System.Windows.Shapes.Rectangle _marker;
        FrameworkElement _press, _drag; Point _pressAt; int _dragFrom, _dragTo; bool _dragOff, _dragEnding;

        void PinsChanged()
        {
            _pinnedCache = null; _signature = "";
            try { Refresh(); } catch (Exception) { }
        }

        void Unpin(AppItem it)
        {
            if (!it.Pinned || it.Launch == null) return;
            try { Pins.Remove(it.Launch); } catch (Exception) { }
            PinsChanged(); // çalışıyorsa sabitlenmemiş uygulama olarak kalır
        }

        static IEnumerable<string> ShortcutRoots() => new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        };

        /// <summary>Çalışan uygulamayı sabitler: Başlat menüsü ya da masaüstünde kısayolu varsa onun kopyası, yoksa exe'ye (ya da AUMID'e) yeni kısayol.</summary>
        void PinRunning(AppItem it, int index = int.MaxValue)
        {
            var key = it.Key; var name = it.Name; var target = it.Launch;
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var lnk = PinFolder.FindShortcut(key, PinFolder.Shortcuts(ShortcutRoots()), l => LinkInfo(l).Key);
                    if (lnk != null) Pins.AddCopy(lnk, index);
                    else Pins.AddNew(name, path => ShellIcons.CreateLink(path, target), index);
                }
                catch (Exception) { }
                Dispatcher.BeginInvoke(new Action(PinsChanged));
            });
        }

        /// <summary>Dock'a bırakılan dosyayı sabitler; zaten sabitliyse yalnızca yerini değiştirir. Sabitlenmiş kaçıncı sıraya gittiğini döndürür.</summary>
        int PinPath(string path, int index)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            bool folder = Directory.Exists(path);
            string key = folder ? null : ext == ".exe" ? path.ToLowerInvariant() : ext == ".lnk" ? LinkInfo(path).Key : path.ToLowerInvariant();
            var pinned = Pinned();
            int at = key == null ? -1 : pinned.FindIndex(p => AppGrouping.SameKey(p.Key, key));
            if (at >= 0)
            {
                int to = PinFolder.MoveTarget(at, index);
                Pins.Move(pinned[at].Launch, to);
                _pinnedCache = null;
                return to + 1;
            }
            if (ext == ".lnk" || ext == ".url") Pins.AddCopy(path, index);
            else
            {
                string name = folder ? (new DirectoryInfo(path).Name is { Length: > 0 } n ? n : path) : Services.AppName(path);
                if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileNameWithoutExtension(path);
                Pins.AddNew(name, p => ShellIcons.CreateLink(p, path), index);
            }
            _pinnedCache = null;
            return index + 1;
        }

        static List<string> DroppedPaths(IDataObject data)
        {
            try
            {
                if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files)
                    return files.Where(PinFolder.CanPin).ToList();
            }
            catch (Exception) { }
            return new List<string>();
        }

        bool Vertical => DockLayout.Vertical(S.Settings.Edge);
        double Along(Point p) => Vertical ? p.Y : p.X;

        /// <summary>Sabitlenmiş simgelerin yerleşimdeki yerleri (dönüşümlerden etkilenmez).</summary>
        List<(double Start, double Size)> PinnedSlots() => _slots.Where(sl => sl.Outer.Tag is AppItem { Pinned: true })
            .Select(sl => System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(sl.Outer)).Select(r => Vertical ? (r.Top, r.Height) : (r.Left, r.Width)).ToList();

        void OnDragOver(object sender, DragEventArgs e)
        {
            e.Handled = true;
            if (_drag != null || DroppedPaths(e.Data).Count == 0) { e.Effects = DragDropEffects.None; return; }
            e.Effects = DragDropEffects.Link;
            Dragging = true;
            ShowGap(PinFolder.InsertIndex(PinnedSlots(), Along(e.GetPosition(_panel))));
        }

        void OnDragLeave(object sender, DragEventArgs e)
        {
            e.Handled = true;
            var p = e.GetPosition(this); // çocuklar arasında gezinirken de gelir: hâlâ şeridin içindeyse aralık kalır
            if (p.X >= 0 && p.Y >= 0 && p.X < ActualWidth && p.Y < ActualHeight) return;
            ClearGap(); Dragging = false;
        }

        void OnDrop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            var paths = DroppedPaths(e.Data);
            int index = PinFolder.InsertIndex(PinnedSlots(), Along(e.GetPosition(_panel)));
            ClearGap(); Dragging = false;
            if (paths.Count == 0) return;
            e.Effects = DragDropEffects.Link;
            foreach (var path in paths)
                try { index = PinPath(path, index); } catch (Exception) { }
            PinsChanged();
        }

        static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        static void Animate(DependencyObject o, DependencyProperty p, double to, double ms = 220) =>
            ((IAnimatable)o).BeginAnimation(p, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = Ease });
        DependencyProperty ShiftProp => Vertical ? TranslateTransform.YProperty : TranslateTransform.XProperty;

        /// <summary>Dışarıdan sürüklenen dosya için aralık: önceki simgeler yarım aralık geri, sonrakiler ileri kayar; ortada ince bir çizgi.</summary>
        void ShowGap(int index)
        {
            double gap = 24 * K;
            for (int i = 0; i < _slots.Count; i++) Animate(_slots[i].Shift, ShiftProp, i < index ? -gap / 2 : gap / 2);
            var slots = PinnedSlots();
            double at = slots.Count == 0 ? 0 : index == 0 ? slots[0].Start : index >= slots.Count ? slots[^1].Start + slots[^1].Size
                : (slots[index - 1].Start + slots[index - 1].Size + slots[index].Start) / 2;
            if (_marker == null)
            {
                _marker = new System.Windows.Shapes.Rectangle { Fill = Theme.Accent, RadiusX = 1.5 * K, RadiusY = 1.5 * K, Opacity = 0 };
                _overlay.Children.Add(_marker);
            }
            double len = 30 * K, thick = 3 * K;
            _marker.Width = Vertical ? len : thick; _marker.Height = Vertical ? thick : len;
            if (Vertical) { Canvas.SetLeft(_marker, (_panel.ActualWidth - len) / 2); Animate(_marker, Canvas.TopProperty, at - thick / 2, 120); }
            else { Canvas.SetTop(_marker, (_panel.ActualHeight - len) / 2); Animate(_marker, Canvas.LeftProperty, at - thick / 2, 120); }
            Animate(_marker, OpacityProperty, 1, 160);
        }

        void ClearGap()
        {
            foreach (var sl in _slots) Animate(sl.Shift, ShiftProp, 0);
            if (_marker != null) Animate(_marker, OpacityProperty, 0, 160);
        }

        /// <summary>Sabitlenmiş simgeyi sürükleme: dock boyunca sıralar; dock'tan uzağa çekilince soluklaşır, bırakılınca kaldırılır.</summary>
        void DragMove(FrameworkElement outer, FrameworkElement btn, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) { _press = null; return; }
            var pos = e.GetPosition(_panel);
            if (_drag == null)
            {
                if (_press != outer || outer.Tag is not AppItem { Pinned: true }) return;
                if (Math.Abs(pos.X - _pressAt.X) < 6 * K && Math.Abs(pos.Y - _pressAt.Y) < 6 * K) return;
                _dragFrom = _dragTo = _slots.FindIndex(sl => sl.Outer == outer);
                if (_dragFrom < 0) return;
                _drag = outer; _dragOff = false; Dragging = true;
                WindowPreview.CloseCurrent(); _zoom?.Clear();
                Panel.SetZIndex(outer, 1);
                btn.CaptureMouse();
            }
            var sl = _slots[_dragFrom];
            sl.Shift.BeginAnimation(TranslateTransform.XProperty, null); sl.Shift.BeginAnimation(TranslateTransform.YProperty, null);
            sl.Shift.X = pos.X - _pressAt.X; sl.Shift.Y = pos.Y - _pressAt.Y; // simge imleçle gider
            double away = S.Settings.Edge switch
            {
                DockEdge.Alt => -pos.Y, DockEdge.Ust => pos.Y - _panel.ActualHeight, DockEdge.Sol => pos.X - _panel.ActualWidth, _ => -pos.X,
            };
            bool off = away > 40 * K;
            if (off != _dragOff) { _dragOff = off; Animate(outer, OpacityProperty, off ? 0.35 : 1, 150); }
            var slots = PinnedSlots();
            _dragTo = off ? _dragFrom : PinFolder.MoveTarget(_dragFrom, PinFolder.InsertIndex(slots, Along(pos)));
            double size = slots[_dragFrom].Size;
            for (int i = 0; i < slots.Count; i++)
            {
                if (i == _dragFrom) continue;
                double to = i > _dragFrom && i <= _dragTo ? -size : i < _dragFrom && i >= _dragTo ? size : 0;
                Animate(_slots[i].Shift, ShiftProp, to);
            }
        }

        /// <summary>Sürükleme bittiyse sıralamayı ya da kaldırmayı uygular ve true döner (tıklama sayılmaz).</summary>
        bool EndDrag(FrameworkElement outer, FrameworkElement btn, MouseButtonEventArgs e)
        {
            _press = null;
            if (_drag == null) return false;
            var it = _drag.Tag as AppItem; var el = _drag; int from = _dragFrom, to = _dragTo; bool off = _dragOff;
            _dragEnding = true; btn.ReleaseMouseCapture(); _dragEnding = false;
            _drag = null;
            if (off && it != null)
            {
                // "Puf": simge büyüyüp kaybolur, sonra sabitleme kaldırılır
                var sl = _slots[from];
                Animate(sl.Poof, ScaleTransform.ScaleXProperty, 1.4, 200); Animate(sl.Poof, ScaleTransform.ScaleYProperty, 1.4, 200);
                Animate(el, OpacityProperty, 0, 200);
                var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
                t.Tick += (_, _) => { t.Stop(); Dragging = false; Unpin(it); };
                t.Start();
                return true;
            }
            Dragging = false;
            if (it != null && to != from)
            {
                try { Pins.Move(it.Launch, to); } catch (Exception) { }
                PinsChanged();
                return true;
            }
            CancelDrag();
            return true;
        }

        void CancelDrag()
        {
            var el = _drag; _drag = null; _press = null; Dragging = false;
            foreach (var sl in _slots) { Animate(sl.Shift, TranslateTransform.XProperty, 0); Animate(sl.Shift, TranslateTransform.YProperty, 0); }
            if (el != null) { Animate(el, OpacityProperty, 1, 150); Panel.SetZIndex(el, 0); }
        }

        static readonly Brush KillBrush = Frozen(Color.FromRgb(0xFF, 0x45, 0x3A)); // Apple systemRed (yıkıcı işlem)

        static ImageSource img(AppItem it) => it.IconPath == null ? null : ShellIcons.Get(it.IconPath);
    }
}

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
        sealed class Slot { public FrameworkElement Outer; public ScaleTransform Scale; public TranslateTransform Lift; public Image Img; public string Key; public double Cur = 1; }
        ZoomLayer _zoom;   // büyüyen simgeler dock'un üstünde buraya çizilir (kesilmez, şerit zıplamaz)
        readonly List<Slot> _slots = new();
        readonly Dictionary<string, Bouncer> _bounce = new();
        readonly Stopwatch _clock = Stopwatch.StartNew();
        double _last; bool _looping;

        sealed class AppItem
        {
            public string Key, Name, Launch, IconPath; public bool Pinned;
            public List<IntPtr> Windows = new();
        }

        public AppsTile(Services s, Theme t, Func<IntPtr> self) : base(s, t, card: false)
        {
            _self = self; Child = _panel; Height = 48 * K;
            if (DockLayout.Vertical(s.Settings.Edge))
            {
                _panel.Orientation = Orientation.Vertical; _panel.HorizontalAlignment = HorizontalAlignment.Center;
                Height = double.NaN; Width = 50 * K;
            }
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
            bool over = IsMouseOver && S.Settings.Magnification;
            var m = Mouse.GetPosition(_panel);
            bool busy = over;
            var (ox, oy) = DockLayout.ZoomOrigin(edge);
            var draw = new List<ZoomLayer.Item>();
            foreach (var sl in _slots)
            {
                if (!sl.Outer.IsLoaded) continue;
                double target = 1;
                if (over)
                {
                    var c = sl.Outer.TranslatePoint(new Point(sl.Outer.ActualWidth / 2, sl.Outer.ActualHeight / 2), _panel); // yerleşim konumu: büyütmeden etkilenmez
                    target = Magnify.Scale(vertical ? m.Y - c.Y : m.X - c.X, icon, max, 1);
                }
                sl.Cur = Magnify.Approach(sl.Cur, target, dt, target > sl.Cur ? Magnify.TauGrow : Magnify.TauShrink);
                if (sl.Cur != 1) busy = true;
                double lift = 0;
                if (_bounce.TryGetValue(sl.Key, out var b) && b.IsActive) { lift = b.Offset(now) * 18 * K; busy = true; }
                double lx = edge == DockEdge.Sol ? lift : edge == DockEdge.Sag ? -lift : 0;
                double ly = edge == DockEdge.Alt ? -lift : edge == DockEdge.Ust ? lift : 0;
                draw.Add(new ZoomLayer.Item(sl.Img, sl.Img.Source, sl.Cur, lx, ly, ox, oy));
            }
            // Büyüyen ya da zıplayan simge varken hepsi katmanda çizilir; katman kullanılamazsa eski yol (dock içinde dönüşüm).
            bool lifted = draw.Exists(d => d.Scale > 1.0005 || d.LiftX != 0 || d.LiftY != 0);
            if (lifted && (_zoom ??= new ZoomLayer()).Draw(this, draw, 44 * K))
            {
                foreach (var sl in _slots) { sl.Img.Opacity = 0; sl.Scale.ScaleX = sl.Scale.ScaleY = 1; sl.Lift.X = sl.Lift.Y = 0; }
            }
            else
            {
                if (!lifted) _zoom?.Clear();
                for (int i = 0; i < _slots.Count && i < draw.Count; i++)
                {
                    var sl = _slots[i]; sl.Img.Opacity = 1;
                    sl.Scale.ScaleX = sl.Scale.ScaleY = lifted ? sl.Cur : 1;
                    sl.Lift.X = lifted ? draw[i].LiftX : 0; sl.Lift.Y = lifted ? draw[i].LiftY : 0;
                }
            }
            if (!busy) { _zoom?.Clear(); foreach (var sl in _slots) sl.Img.Opacity = 1; Loop(false); }
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
                if (Directory.Exists(PinnedDir))
                    foreach (var lnk in Directory.GetFiles(PinnedDir, "*.lnk").OrderBy(f => File.GetCreationTimeUtc(f)))
                    {
                        var target = ShellIcons.LinkTarget(lnk);
                        if (target != null && Path.GetFileName(target).Equals("explorer.exe", StringComparison.OrdinalIgnoreCase)) target = null;
                        list.Add(new AppItem
                        {
                            Key = (target ?? lnk).ToLowerInvariant(), Name = Path.GetFileNameWithoutExtension(lnk),
                            Launch = lnk, IconPath = lnk, Pinned = true,
                        });
                    }
            }
            catch (Exception) { }
            _pinnedAt = DateTime.Now;
            return _pinnedCache = list;
        }

        public override void Refresh()
        {
            var items = Pinned().Select(p => new AppItem { Key = p.Key, Name = p.Name, Launch = p.Launch, IconPath = p.IconPath, Pinned = true }).ToList();
            var byKey = items.ToDictionary(i => i.Key, i => i);
            foreach (var h in Native.TaskbarWindows(_self()))
            {
                Native.GetWindowThreadProcessId(h, out uint pid);
                var path = Native.ProcessPath(pid);
                if (path == null) continue;
                // Paketli uygulamalar (Ayarlar, Not Defteri vb.): simge ve başlatma AppUserModelID ile.
                // Eskiden ApplicationFrameHost pencereleri simgesiz kalıp baş harf ("A") gösteriliyordu.
                bool frame = Path.GetFileName(path).Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase);
                string aumid = frame || path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase) ? Native.AppUserModelId(h) : null;
                string key, name, launch, icon;
                if (aumid != null) { key = "aumid:" + aumid.ToLowerInvariant(); launch = icon = @"shell:AppsFolder\" + aumid; name = frame ? Native.WindowTitle(h) : Services.AppName(path); }
                else if (frame) continue; // kimliği çözülemeyen boş UWP çerçevesi (askıdaki uygulama) gösterilmez
                else { key = path.ToLowerInvariant(); launch = icon = path; name = Services.AppName(path); }
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
            var sig = string.Join("|", items.Select(i => i.Key + ":" + i.Windows.Count + (i.Windows.Contains(fg) ? "*" : "")));
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
            var img = new Image { Width = 28 * K, Height = 28 * K, Source = ShellIcons.Get(item.IconPath) ?? ShellIcons.Generic, SnapsToDevicePixels = true };
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
            var btn = new Border
            {
                Width = size, Height = (card ? 39 : 46) * K, CornerRadius = new CornerRadius((card ? 13 : 10) * K), Background = Brushes.Transparent, Child = grid,
                Tag = item, ToolTip = item.Windows.Count > 0 ? Native.WindowTitle(item.Windows[0]) : item.Name, Cursor = Cursors.Hand,
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
            _slots.Add(new Slot { Outer = outer, Scale = scale, Lift = lift, Img = img, Key = item.Key });
            // Windows 11 gibi: üzerinde biraz bekleyince açık pencerelerin canlı önizlemesi
            var hover = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            hover.Tick += (_, _) => { hover.Stop(); if (btn.IsMouseOver) Preview((AppItem)outer.Tag, btn); };
            btn.MouseEnter += (_, _) => { btn.Background = T.TileHover; hover.Start(); };
            btn.MouseLeave += (_, _) => { btn.Background = Brushes.Transparent; hover.Stop(); };
            btn.MouseLeftButtonUp += (_, e) => { Click((AppItem)outer.Tag, btn); e.Handled = true; };
            btn.MouseRightButtonUp += (_, e) => { WindowPreview.CloseCurrent(); Menu((AppItem)outer.Tag, btn); e.Handled = true; };
            return outer;
        }

        void Click(AppItem it, FrameworkElement target)
        {
            var alive = it.Windows.Where(Native.IsWindowVisible).ToList();
            if (alive.Count == 0) { Launch(it); return; }
            if (alive.Count > 1) { Preview(it, target); return; } // birden çok pencere: önizleme panelinden seç
            WindowPreview.CloseCurrent();
            var fg = Native.GetForegroundWindow();
            // 1.6'daki Genie: öndeyse simgeye akarak küçülür, küçültülmüşse simgeden büyüyerek gelir; olmazsa normal yol
            if (alive[0] == fg) { if (!Genie(alive[0], target, false)) Native.ShowWindow(fg, Native.SW_MINIMIZE); }
            else if (!(Native.IsIconic(alive[0]) && Genie(alive[0], target, true))) Native.Activate(alive[0]);
            _signature = ""; // aktif göstergesini tazele
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

        void Launch(AppItem it)
        {
            if (it.Launch == null) return;
            if (it.Windows.Count == 0 && S.Settings.Animations)
            {
                if (!_bounce.TryGetValue(it.Key, out var b)) _bounce[it.Key] = b = new Bouncer();
                b.Start(BounceKind.Launch, _clock.Elapsed.TotalSeconds);
                Loop(true);
            }
            try { Process.Start(new ProcessStartInfo(it.Launch) { UseShellExecute = true }); } catch (Exception) { }
        }

        /// <summary>Sağ tık: pencere listesi, yeni pencere, kapat.</summary>
        void Menu(AppItem it, FrameworkElement target)
        {
            var m = new ContextMenu();
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
            m.PlacementTarget = target;
            m.Placement = Pop.Placement(S.Settings.Edge);
            m.IsOpen = true;
        }

        static ImageSource img(AppItem it) => it.IconPath == null ? null : ShellIcons.Get(it.IconPath);
    }
}

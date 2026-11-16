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
using KutayDock.Core;

namespace KutayDock.Tiles
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
            // Görünüm yalnızca liste değişince yeniden kurulur.
            var fg = Native.GetForegroundWindow();
            var sig = string.Join("|", items.Select(i => i.Key + ":" + i.Windows.Count + (i.Windows.Contains(fg) ? "*" : "")));
            if (sig == _signature) { UpdateHandles(items); return; }
            _signature = sig;
            _panel.Children.Clear();
            foreach (var it in items) _panel.Children.Add(MakeButton(it, it.Windows.Contains(fg)));
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
            var scale = new ScaleTransform(1, 1);
            FrameworkElement outer = null;
            var edge = S.Settings.Edge;
            var (ox, oy) = DockLayout.ZoomOrigin(edge); // büyüme ekranın içine doğru
            var iconHost = new Border { Child = icon, RenderTransform = scale, RenderTransformOrigin = new Point(ox, oy) };
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
            // Üzerine gelince büyüme (macOS dock hissi, kart sınırları içinde)
            void Zoom(double v)
            {
                var a = new DoubleAnimation(v, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, a); scale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            }
            // Windows 11 gibi: üzerinde biraz bekleyince açık pencerelerin canlı önizlemesi
            var hover = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            hover.Tick += (_, _) => { hover.Stop(); if (btn.IsMouseOver) Preview((AppItem)outer.Tag, btn); };
            btn.MouseEnter += (_, _) => { btn.Background = T.TileHover; Zoom(1.22); hover.Start(); };
            btn.MouseLeave += (_, _) => { btn.Background = Brushes.Transparent; Zoom(1.0); hover.Stop(); };
            btn.PreviewMouseLeftButtonDown += (_, _) => Zoom(1.05);
            btn.MouseLeftButtonUp += (_, e) => { Zoom(1.22); Click((AppItem)outer.Tag, btn); e.Handled = true; };
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
            if (alive[0] == fg) Native.ShowWindow(fg, Native.SW_MINIMIZE);
            else Native.Activate(alive[0]);
            _signature = ""; // aktif göstergesini tazele
        }

        void Preview(AppItem it, FrameworkElement anchor)
        {
            if (it.Windows.Count == 0) return;
            WindowPreview.Open(it.Key, it.Windows, img(it) ?? ShellIcons.Generic, anchor, S.Settings.Edge);
        }

        static void Launch(AppItem it)
        {
            if (it.Launch == null) return;
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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using KutayDock.Core;

namespace KutayDock.Tiles
{
    internal static class Pop
    {
        /// <summary>Kartın üstünde açılan küçük koyu panel; dışarı tıklanınca kapanır.</summary>
        public static Popup Make(FrameworkElement target, UIElement content, DockEdge edge)
        {
            var p = new Popup
            {
                PlacementTarget = target, Placement = Placement(edge), StaysOpen = false,
                AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade,
                VerticalOffset = edge switch { DockEdge.Ust => 8, DockEdge.Alt => -8, _ => 0 },
                HorizontalOffset = edge switch { DockEdge.Sol => 8, DockEdge.Sag => -8, _ => 0 },
                Child = new Border
                {
                    Background = Theme.B("#F21C1C1F"), BorderBrush = Theme.B("#33FFFFFF"), BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 12, 14, 12), Child = content, MinWidth = 260,
                },
            };
            p.Opened += (_, _) => Anim.PopIn(p.Child, edge);
            return p;
        }

        /// <summary>Dock'tan ekranın içine doğru açılma yönü (panel, sağ tık menüsü).</summary>
        public static PlacementMode Placement(DockEdge edge) => edge switch
        {
            DockEdge.Ust => PlacementMode.Bottom,
            DockEdge.Sol => PlacementMode.Right,
            DockEdge.Sag => PlacementMode.Left,
            _ => PlacementMode.Top,
        };

        public static Border Link(string text, Action a)
        {
            var t = Ui.Text(text, 12, Theme.Accent, FontWeights.SemiBold);
            var b = new Border { Child = t, Cursor = Cursors.Hand, Background = Brushes.Transparent, Margin = new Thickness(0, 8, 14, 0) };
            b.MouseLeftButtonUp += (_, e) => { a(); e.Handled = true; };
            return b;
        }
    }

    /// <summary>
    /// Grok Bot limiti kartı; tıklanınca Cursor limitleri paneli açılır.
    /// İkisi için de belgelenmiş bir kişisel kullanım API'si yok: yüzde ve sıfırlanma zamanı elle girilir.
    /// Cursor planı (ör. Pro+) Cursor CLI'nin belgelenmiş "agent about" komutundan okunur.
    /// </summary>
    internal sealed class LimitsTile : Tile
    {
        public override string Id => "ai";
        readonly Bar _bar; readonly TextBlock _reset, _pct, _fresh;
        readonly Action _openSettings;
        Popup _popup;

        public LimitsTile(Services s, Theme t, Action openSettings) : base(s, t)
        {
            _openSettings = openSettings;
            var badge = new Border
            {
                Width = 36 * K, Height = 36 * K, CornerRadius = new CornerRadius(10 * K), Margin = new Thickness(0, 0, 10 * K, 0),
                Background = Theme.B("#FF0B0B0D"), BorderBrush = Theme.B("#33FFFFFF"), BorderThickness = new Thickness(1),
                Child = Ui.Glyph(Ui.GSpark, Brushes.White, 18 * K),
            };
            Grid g = new() { VerticalAlignment = VerticalAlignment.Center };
            foreach (var w in new[] { 58.0, 80, 62, 36 }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w * K) });
            g.RowDefinitions.Add(new RowDefinition()); g.RowDefinitions.Add(new RowDefinition());
            void Put(UIElement e, int r, int c, int span = 1) { Grid.SetRow(e, r); Grid.SetColumn(e, c); Grid.SetColumnSpan(e, span); g.Children.Add(e); }
            _bar = new Bar { Height = 5 * K, Margin = new Thickness(0, 0, 6 * K, 0), VerticalAlignment = VerticalAlignment.Center, Track = t.Track };
            _reset = Txt("", 9, true); _reset.HorizontalAlignment = HorizontalAlignment.Right;
            _pct = Txt("", 11, w: FontWeights.SemiBold); _pct.HorizontalAlignment = HorizontalAlignment.Right;
            _fresh = Txt("", 9, true);
            Put(Txt("Grok Bot", 10, w: FontWeights.SemiBold), 0, 0); Put(_bar, 0, 1); Put(_reset, 0, 2); Put(_pct, 0, 3);
            Put(Txt("Haftalık", 10, true), 1, 0); Put(_fresh, 1, 1, 3);
            Child = Row(badge, g);
            ToolTip = "Tıkla: Cursor limitleri";
            OnClick(ShowCursor);
        }

        public override void Refresh()
        {
            var now = DateTimeOffset.Now;
            var (l, live) = S.GrokBotLimit();
            var f = Limits.Fraction(l, now);
            _bar.Value = f ?? 0; _bar.Fill = Color(f ?? 0); _bar.InvalidateVisual();
            _pct.Text = f is { } v ? $"%{v * 100:0}" : "—";
            _reset.Text = Limits.ResetLabel(l, now); // yerel saat (Kasa: UTC+3)
            var c = S.Cursor;
            _fresh.Text = c.SignedIn && c.NeedsRelogin ? "yeniden giriş gerekli"
                : live ? Limits.LiveFreshness(l.UpdatedAt ?? now, now)
                : c.SignedIn && c.Sand is { Enabled: false } ? "Grok Bot kotası yok"
                : Limits.Freshness(l, now);
            _fresh.Foreground = c.SignedIn && c.NeedsRelogin ? Theme.Orange : T.FgDim;
        }

        void ShowCursor() => ShowCursor(true);

        void ShowCursor(bool fetch)
        {
            var acc = S.Cursor;
            if (fetch && acc.SignedIn && !acc.NeedsRelogin)
                _ = acc.RefreshSummary().ContinueWith(_ => Dispatcher.BeginInvoke(new Action(() => { if (_popup?.IsOpen == true) ShowCursor(false); })));
            var now = DateTimeOffset.Now;
            var c = S.Settings.Cursor;
            var f = Limits.Fraction(c, now);
            var p = new StackPanel();
            var head = new DockPanel();
            var tier = Ui.Text(S.CursorTier ?? "plan bilinmiyor", 11, Brushes.White, FontWeights.SemiBold);
            var chip = new Border { Background = Theme.Accent, CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 2, 8, 2), Child = tier };
            DockPanel.SetDock(chip, Dock.Right); head.Children.Add(chip);
            head.Children.Add(Ui.Text("Cursor", 15, Brushes.White, FontWeights.SemiBold));
            p.Children.Add(head);
            if (acc.SignedIn && acc.Summary is { } sum)
            {
                AddSummary(p, sum, acc.SummaryAt ?? now, now);
            }
            else if (f is { } v)
            {
                var row = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
                var pct = Ui.Text($"%{v * 100:0}", 13, Brushes.White, FontWeights.SemiBold);
                DockPanel.SetDock(pct, Dock.Right); row.Children.Add(pct);
                row.Children.Add(new Bar { Height = 6, Value = v, Fill = Color(v), Track = Theme.B("#33FFFFFF"), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
                p.Children.Add(row);
                p.Children.Add(Ui.Text($"Sıfırlanma: {Limits.ResetLabel(c, now)} · {Limits.Freshness(c, now)}", 11, Theme.B("#99F5F5F7")));
            }
            else
            {
                var msg = acc.SignedIn && acc.NeedsRelogin ? "Cursor oturumu geçersiz: Ayarlar > Limitler'den yeniden giriş yap. Şimdilik elle girilen değerler gösteriliyor."
                    : acc.SignedIn ? "Cursor kullanım özeti alınıyor…"
                    : "Kaynak yok: Ayarlar > Limitler'den \"Cursor ile giriş yap\" ya da değeri kullanım sayfasından bakıp elle gir.";
                var t = Ui.Text(msg, 11, acc.SignedIn && acc.NeedsRelogin ? Theme.Orange : Theme.B("#99F5F5F7"));
                t.TextWrapping = TextWrapping.Wrap; t.MaxWidth = 280; t.Margin = new Thickness(0, 8, 0, 0);
                p.Children.Add(t);
            }
            var (g, live) = S.GrokBotLimit();
            p.Children.Add(Ui.Text($"Grok Bot haftalık: {(Limits.Fraction(g, now) is { } gv ? $"%{gv * 100:0}" : "—")} · {Limits.ResetLabel(g, now)} · {(live ? "Cursor" : "elle")}", 11, Theme.B("#99F5F5F7")));
            var links = new WrapPanel();
            links.Children.Add(Pop.Link("Kullanım sayfasını aç", () => { _popup.IsOpen = false; Run("https://cursor.com/dashboard?tab=usage"); }));
            links.Children.Add(Pop.Link("Değerleri gir…", () => { _popup.IsOpen = false; _openSettings(); }));
            p.Children.Add(links);
            if (_popup != null) _popup.IsOpen = false;
            _popup = Pop.Make(this, p, S.Settings.Edge);
            _popup.IsOpen = true;
        }

        static void AddSummary(StackPanel p, UsageSummary u, DateTimeOffset at, DateTimeOffset now)
        {
            var dim = Theme.B("#99F5F5F7");
            void BarRow(string name, double? pct)
            {
                if (pct is not { } v) return;
                var row = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
                var t = Ui.Text($"%{v:0}", 12, Brushes.White, FontWeights.SemiBold);
                DockPanel.SetDock(t, Dock.Right); row.Children.Add(t);
                var n = Ui.Text(name, 11, dim); n.Width = 60; DockPanel.SetDock(n, Dock.Left); row.Children.Add(n);
                row.Children.Add(new Bar { Height = 6, Value = Format.Clamp01(v / 100), Fill = Color(v / 100), Track = Theme.B("#33FFFFFF"), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
                p.Children.Add(row);
            }
            BarRow("Toplam", u.TotalPercent);
            BarRow("Auto", u.AutoPercent);
            BarRow("API", u.ApiPercent);
            var tr = Format.Tr;
            if (u.UsedDollars is { } used && u.LimitDollars is { } lim)
                p.Children.Add(Ui.Text($"Dahil kullanım: ${used.ToString("0.00", tr)} / ${lim.ToString("0.00", tr)}", 11, dim));
            if (u.OnDemandEnabled)
                p.Children.Add(Ui.Text($"İsteğe bağlı: ${(u.OnDemandUsedDollars ?? 0).ToString("0.00", tr)}" + (u.OnDemandLimitDollars is { } ol ? $" / ${ol.ToString("0.00", tr)}" : ""), 11, dim));
            var end = u.CycleEnd is { } e ? $"Dönem sonu: {e.ToLocalTime().ToString("d MMM HH:mm", tr)} · " : "";
            p.Children.Add(Ui.Text($"{end}{Limits.LiveFreshness(at, now)}", 11, dim));
        }

        static Brush Color(double f) => f >= 0.9 ? Theme.Red : f >= 0.75 ? Theme.Orange : Theme.Green;
    }

    /// <summary>Depolama: sistem diskinin boş alanı ve doluluk çubuğu; tıklanınca tüm diskler.</summary>
    internal sealed class StorageTile : Tile
    {
        public override string Id => "storage";
        readonly TextBlock _name, _free; readonly Bar _bar; Popup _popup;

        public StorageTile(Services s, Theme t) : base(s, t)
        {
            var icon = Ui.Glyph("M2,4H18A2,2 0 0 1 20,6V14A2,2 0 0 1 18,16H2A2,2 0 0 1 0,14V6A2,2 0 0 1 2,4Z M15,9A1.5,1.5 0 1 0 15.01,9Z", t.FgDim, 18 * K);
            icon.Margin = new Thickness(0, 0, 8 * K, 0);
            _name = Txt("", 10, w: FontWeights.SemiBold); _free = Txt("", 10, true);
            var top = new DockPanel { Width = 116 * K };
            DockPanel.SetDock(_free, Dock.Right); top.Children.Add(_free); top.Children.Add(_name);
            _bar = new Bar { Width = 116 * K, Height = 5 * K, Margin = new Thickness(0, 4 * K, 0, 0), Track = t.Track };
            Child = Row(icon, Col(top, _bar));
            OnClick(ShowAll);
        }

        static DriveInfo SystemDrive(System.Collections.Generic.List<DriveInfo> d)
        {
            var sys = Path.GetPathRoot(Environment.SystemDirectory);
            return d.FirstOrDefault(x => string.Equals(x.Name, sys, StringComparison.OrdinalIgnoreCase)) ?? d.FirstOrDefault();
        }

        public override void Refresh()
        {
            var d = SystemDrive(S.Drives);
            if (d == null) { _name.Text = "Disk"; _free.Text = "—"; return; }
            try
            {
                double used = 1 - (double)d.AvailableFreeSpace / d.TotalSize;
                _name.Text = d.Name.TrimEnd('\\');
                _free.Text = Format.Bytes(d.AvailableFreeSpace) + " boş";
                _bar.Value = used; _bar.Fill = Fill(used); _bar.InvalidateVisual();
                ToolTip = $"{Label(d)}: {Format.Bytes(d.TotalSize - d.AvailableFreeSpace)} / {Format.Bytes(d.TotalSize)} dolu (%{used * 100:0}). Tıkla: tüm diskler";
            }
            catch (Exception) { }
        }

        static Brush Fill(double used) => used >= 0.9 ? Theme.Red : used >= 0.75 ? Theme.Orange : Theme.Accent;
        static string Label(DriveInfo d) { try { return string.IsNullOrWhiteSpace(d.VolumeLabel) ? d.Name.TrimEnd('\\') : $"{d.VolumeLabel} ({d.Name.TrimEnd('\\')})"; } catch (Exception) { return d.Name; } }

        void ShowAll()
        {
            var p = new StackPanel();
            p.Children.Add(Ui.Text("Diskler", 15, Brushes.White, FontWeights.SemiBold));
            foreach (var d in S.Drives)
            {
                try
                {
                    double used = 1 - (double)d.AvailableFreeSpace / d.TotalSize;
                    var row = new StackPanel { Margin = new Thickness(0, 10, 0, 0), Cursor = Cursors.Hand, Background = Brushes.Transparent };
                    var top = new DockPanel();
                    var right = Ui.Text($"{Format.Bytes(d.AvailableFreeSpace)} boş / {Format.Bytes(d.TotalSize)}", 11, Theme.B("#99F5F5F7"));
                    DockPanel.SetDock(right, Dock.Right); top.Children.Add(right);
                    top.Children.Add(Ui.Text(Label(d), 12, Brushes.White, FontWeights.SemiBold));
                    row.Children.Add(top);
                    row.Children.Add(new Bar { Height = 6, Value = used, Fill = Fill(used), Track = Theme.B("#33FFFFFF"), Margin = new Thickness(0, 4, 0, 0) });
                    var path = d.Name;
                    row.ToolTip = "Tıkla: klasör boyutları · Sağ tık: Explorer'da aç";
                    row.MouseLeftButtonUp += (_, e) => { _ = ShowFolder(path); e.Handled = true; };
                    row.MouseRightButtonUp += (_, e) => { _popup.IsOpen = false; Run("explorer.exe", $"\"{path}\""); e.Handled = true; };
                    p.Children.Add(row);
                }
                catch (Exception) { }
            }
            p.Children.Add(Pop.Link("Depolama ayarları", () => { _popup.IsOpen = false; Run("ms-settings:storagesense"); }));
            if (_popup != null) _popup.IsOpen = false;
            _popup = Pop.Make(this, p, S.Settings.Edge);
            _popup.MinWidth = 300;
            _popup.IsOpen = true;
        }

        EverythingClient _ev;
        int _seq;

        void SetContent(UIElement c) { if (_popup?.Child is Border b) b.Child = c; }

        /// <summary>
        /// TreeSize benzeri sade görünüm: klasörün alt klasörleri boyuta göre, çubuklu. Veri Everything'den (IPC, arka planda);
        /// tam tarama yapılmaz. Everything klasör boyutlarını dizinliyorsa doğrudan, yoksa eşikten büyük dosyalar toplanır (yaklaşık).
        /// </summary>
        async System.Threading.Tasks.Task ShowFolder(string path)
        {
            int seq = ++_seq;
            var head = new DockPanel();
            var back = new Border { Child = Ui.Text("‹ Geri", 12, Theme.Accent, FontWeights.SemiBold), Cursor = Cursors.Hand, Background = Brushes.Transparent, Margin = new Thickness(0, 0, 10, 0) };
            var parent = FolderSizes.Parent(path);
            back.MouseLeftButtonUp += (_, e) => { if (parent == null) ShowAllContent(); else _ = ShowFolder(parent); e.Handled = true; };
            var open = new Border { Child = Ui.Text("Explorer'da aç", 11, Theme.Accent), Cursor = Cursors.Hand, Background = Brushes.Transparent, Margin = new Thickness(10, 0, 0, 0) };
            open.MouseLeftButtonUp += (_, e) => { _popup.IsOpen = false; Run("explorer.exe", $"\"{path}\""); e.Handled = true; };
            DockPanel.SetDock(back, Dock.Left); DockPanel.SetDock(open, Dock.Right);
            head.Children.Add(back); head.Children.Add(open);
            head.Children.Add(Ui.Text(path, 13, Brushes.White, FontWeights.SemiBold, 250));
            var status = Ui.Text("Hesaplanıyor…", 11, Theme.B("#99F5F5F7"));
            status.Margin = new Thickness(0, 6, 0, 0);
            var list = new StackPanel();
            var scroll = new ScrollViewer { Content = list, MaxHeight = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 6, 0, 0) };
            var p = new StackPanel { Width = 340, Children = { head, status, scroll } };
            SetContent(p);

            if (!EverythingClient.Running) { status.Text = "Everything çalışmıyor (Kutay Dock onu başlatmaz)."; return; }
            _ev ??= new EverythingClient();
            var root = path.TrimEnd('\\') + "\\";
            List<FolderEntry> entries;
            bool approx = false;
            // 1) Everything klasör boyutlarını dizinliyorsa doğrudan alt öğeler
            var direct = await _ev.Sized($"parent:\"{(root.Length <= 3 ? root : root.TrimEnd('\\'))}\"", 500);
            if (seq != _seq) return;
            if (direct == null) { status.Text = "Everything yanıt vermedi."; return; }
            if (direct.Any(d => d.IsFolder && d.Size > 0))
                entries = direct.Where(d => d.Size > 0).OrderByDescending(d => d.Size).Take(30)
                    .Select(d => new FolderEntry(d.Name, root + d.Name, d.Size, d.IsFolder, 0)).ToList();
            else
            {
                // 2) Dizinlenmemişse: eşikten büyük dosyaları alt klasörlere topla
                long min = root.Length <= 3 ? 8L << 20 : 256L << 10;
                var files = await _ev.Sized(FolderSizes.Query(root, min), 60000);
                if (seq != _seq) return;
                if (files == null) { status.Text = "Everything yanıt vermedi."; return; }
                entries = FolderSizes.Aggregate(root, files.Select(f => (f.Folder, f.Name, f.Size)));
                // Boyutu eşik altında kalan alt klasörler de görünsün (gezinmek için)
                foreach (var d in direct.Where(d => d.IsFolder && !entries.Any(e => e.Name.Equals(d.Name, StringComparison.OrdinalIgnoreCase))).Take(40))
                    entries.Add(new FolderEntry(d.Name, root + d.Name, 0, true, 0));
                approx = true;
                status.Text = $"Yaklaşık: {Format.Bytes(min)} üstü dosyalar sayıldı · Everything";
            }
            if (!approx) status.Text = "Everything klasör boyutları";
            if (entries.Count == 0) { status.Text = "Bu klasörde gösterilecek öğe yok."; return; }
            long max = Math.Max(1, entries.Max(e => e.Bytes));
            foreach (var e in entries)
            {
                var row = new StackPanel { Margin = new Thickness(0, 4, 6, 0), Background = Brushes.Transparent, Cursor = e.IsFolder ? Cursors.Hand : Cursors.Arrow };
                var top = new DockPanel();
                var size = Ui.Text(e.Bytes > 0 ? Format.Bytes(e.Bytes) : "—", 11, Theme.B("#99F5F5F7"));
                DockPanel.SetDock(size, Dock.Right); top.Children.Add(size);
                top.Children.Add(Ui.Text((e.IsFolder ? "📁 " : "") + e.Name, 12, Brushes.White, null, 250));
                row.Children.Add(top);
                row.Children.Add(new Bar { Height = 4, Value = (double)e.Bytes / max, Fill = Theme.Accent, Track = Theme.B("#22FFFFFF"), Margin = new Thickness(0, 3, 0, 0) });
                var full = e.FullPath; bool folder = e.IsFolder;
                row.ToolTip = full + (folder ? "\nTıkla: içine gir · Sağ tık: Explorer'da aç" : "\nSağ tık: klasörde göster");
                if (folder) row.MouseLeftButtonUp += (_, ev) => { _ = ShowFolder(full); ev.Handled = true; };
                row.MouseRightButtonUp += (_, ev) =>
                {
                    _popup.IsOpen = false;
                    Run("explorer.exe", folder ? $"\"{full}\"" : $"/select,\"{full}\"");
                    ev.Handled = true;
                };
                list.Children.Add(row);
            }
        }

        void ShowAllContent() { _seq++; ShowAll(); }
    }

    /// <summary>
    /// Cloudflare One Client (WARP): durum + tek tıkla bağlan/kes. Yalnızca warp-cli (pencere açmadan, CreateNoWindow);
    /// uygulama penceresi açılmaz, kullanım koşulları kendiliğinden kabul edilmez.
    /// </summary>
    internal sealed class WarpTile : Tile
    {
        public override string Id => "warp";
        static readonly string Cli = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Cloudflare", "Cloudflare WARP", "warp-cli.exe");
        readonly TextBlock _state; readonly System.Windows.Shapes.Ellipse _dot;
        WarpState _now = WarpState.Unknown; DateTime _next; bool _busy;

        public WarpTile(Services s, Theme t) : base(s, t)
        {
            _dot = new System.Windows.Shapes.Ellipse { Width = 8 * K, Height = 8 * K, Fill = t.FgDim, Margin = new Thickness(0, 0, 6 * K, 0), VerticalAlignment = VerticalAlignment.Center };
            _state = Txt("", 10, true);
            Child = Row(_dot, Col(Txt("WARP", 11, w: FontWeights.SemiBold), _state));
            OnClick(Toggle);
            Ui.PressEffect(this);
            Refresh();
        }

        public override void Refresh()
        {
            if (_busy || DateTime.Now < _next) return;
            _next = DateTime.Now.AddSeconds(10);
            _ = Poll();
        }

        async System.Threading.Tasks.Task Poll()
        {
            _busy = true;
            var st = File.Exists(Cli) ? Warp.Parse(await RunCli("status")) : WarpState.NotInstalled;
            _busy = false;
            Show(st);
        }

        void Show(WarpState st)
        {
            _now = st;
            _state.Text = Warp.Label(st);
            _dot.Fill = st switch { WarpState.Connected => Theme.Green, WarpState.Connecting => Theme.Orange, _ => T.FgDim };
            ToolTip = st == WarpState.NotInstalled ? "Cloudflare WARP kurulu değil" : $"Cloudflare WARP: {Warp.Label(st)}. Tıkla: " + (st == WarpState.Connected ? "bağlantıyı kes" : "bağlan");
        }

        async void Toggle()
        {
            if (_busy || _now == WarpState.NotInstalled) return;
            _busy = true;
            bool connect = _now != WarpState.Connected;
            Show(WarpState.Connecting);
            if (!connect) _state.Text = "Kesiliyor…";
            await RunCli(connect ? "connect" : "disconnect");
            await System.Threading.Tasks.Task.Delay(1500);
            _busy = false; _next = DateTime.MinValue;
            await Poll();
        }

        static System.Threading.Tasks.Task<string> RunCli(string args) => System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo(Cli, args)
                {
                    UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
                });
                p.StandardInput.Close(); // soru sorarsa (ör. kullanım koşulları) beklemeden çıksın; kabul edilmez
                var o = p.StandardOutput.ReadToEndAsync();
                if (!p.WaitForExit(8000)) { try { p.Kill(); } catch (Exception) { } return null; }
                return o.Result;
            }
            catch (Exception) { return null; }
        });
    }

    /// <summary>Ekran görüntüsü: ShareX'in komut satırı eylemleri. Sol tık bölge, sağ tık seçenekler.</summary>
    internal sealed class ScreenshotTile : Tile
    {
        public override string Id => "screenshot";

        public ScreenshotTile(Services s, Theme t) : base(s, t)
        {
            Width = 50 * K; Padding = new Thickness(0);
            Child = Ui.Glyph("M7,2H13L14.5,4H18A2,2 0 0 1 20,6V16A2,2 0 0 1 18,18H2A2,2 0 0 1 0,16V6A2,2 0 0 1 2,4H5.5Z M10,6.5A4.5,4.5 0 1 0 10.01,6.5Z M10,8.5A2.5,2.5 0 1 1 9.99,8.5Z", t.Fg, 18 * K);
            ToolTip = "Ekran görüntüsü (ShareX): tıkla = bölge, sağ tık = diğer seçenekler";
            OnClick(() => Capture(ShareXCapture.Bolge));
            Ui.PressEffect(this);
            var m = new ContextMenu();
            void Add(string h, Action a) { var i = new MenuItem { Header = h }; i.Click += (_, _) => a(); m.Items.Add(i); }
            Add("Bölge seç", () => Capture(ShareXCapture.Bolge));
            Add("Etkin pencere", () => Capture(ShareXCapture.Pencere));
            Add("Etkin monitör", () => Capture(ShareXCapture.Monitor));
            Add("Tüm ekran", () => Capture(ShareXCapture.TamEkran));
            Add("Son bölge", () => Capture(ShareXCapture.SonBolge));
            m.Items.Add(new Separator());
            Add("ShareX'i aç", () => { var p = Path(); if (p != null) Run(p); });
            ContextMenu = m;
        }

        string Path()
        {
            string pf(Environment.SpecialFolder f) => Environment.GetFolderPath(f);
            var candidates = new[]
            {
                System.IO.Path.Combine(pf(Environment.SpecialFolder.ProgramFiles), "ShareX", "ShareX.exe"),
                System.IO.Path.Combine(pf(Environment.SpecialFolder.ProgramFilesX86), "ShareX", "ShareX.exe"),
                System.IO.Path.Combine(pf(Environment.SpecialFolder.LocalApplicationData), "Programs", "ShareX", "ShareX.exe"),
                RegistryInstallPath(),
            };
            return ShareX.Find(S.Settings.ShareXPath, candidates, File.Exists);
        }

        static string RegistryInstallPath()
        {
            try
            {
                foreach (var root in new[] { Microsoft.Win32.Registry.LocalMachine, Microsoft.Win32.Registry.CurrentUser })
                    using (var k = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ShareX_is1"))
                        if (k?.GetValue("InstallLocation") is string loc) return System.IO.Path.Combine(loc, "ShareX.exe");
            }
            catch (Exception) { }
            return null;
        }

        void Capture(ShareXCapture c)
        {
            var p = Path();
            if (p == null) { MessageBox.Show("ShareX bulunamadı. Ayarlar > Ekran görüntüsü bölümüne ShareX.exe yolunu yaz.", "Kutay Dock"); return; }
            try { Process.Start(new ProcessStartInfo(p, ShareX.Args(c)) { UseShellExecute = false }); } catch (Exception) { }
        }

        public override void Refresh() { }
    }
}

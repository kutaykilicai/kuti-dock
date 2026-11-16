using System;
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
        public static Popup Make(FrameworkElement target, UIElement content, bool top)
        {
            var p = new Popup
            {
                PlacementTarget = target, Placement = top ? PlacementMode.Bottom : PlacementMode.Top, StaysOpen = false,
                AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade, VerticalOffset = top ? 8 : -8,
                Child = new Border
                {
                    Background = Theme.B("#F21C1C1F"), BorderBrush = Theme.B("#33FFFFFF"), BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 12, 14, 12), Child = content, MinWidth = 260,
                },
            };
            return p;
        }

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
            var l = S.Settings.GrokBot;
            var f = Limits.Fraction(l, now);
            _bar.Value = f ?? 0; _bar.Fill = Color(f ?? 0); _bar.InvalidateVisual();
            _pct.Text = f is { } v ? $"%{v * 100:0}" : "—";
            _reset.Text = Limits.ResetLabel(l, now);
            _fresh.Text = Limits.Freshness(l, now);
        }

        void ShowCursor()
        {
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
            if (f is { } v)
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
                var t = Ui.Text("Kaynak yok: Cursor kişisel planlar için belgelenmiş bir kullanım API'si sunmuyor. Değeri kullanım sayfasından bakıp elle gir.", 11, Theme.B("#99F5F5F7"));
                t.TextWrapping = TextWrapping.Wrap; t.MaxWidth = 280; t.Margin = new Thickness(0, 8, 0, 0);
                p.Children.Add(t);
            }
            var g = S.Settings.GrokBot;
            p.Children.Add(Ui.Text($"Grok Bot haftalık: {(Limits.Fraction(g, now) is { } gv ? $"%{gv * 100:0}" : "—")} · {Limits.ResetLabel(g, now)}", 11, Theme.B("#99F5F5F7")));
            var links = new WrapPanel();
            links.Children.Add(Pop.Link("Kullanım sayfasını aç", () => { _popup.IsOpen = false; Run("https://cursor.com/dashboard?tab=usage"); }));
            links.Children.Add(Pop.Link("Değerleri gir…", () => { _popup.IsOpen = false; _openSettings(); }));
            p.Children.Add(links);
            if (_popup != null) _popup.IsOpen = false;
            _popup = Pop.Make(this, p, S.Settings.Edge == DockEdge.Ust);
            _popup.IsOpen = true;
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
                    row.MouseLeftButtonUp += (_, e) => { _popup.IsOpen = false; Run("explorer.exe", $"\"{path}\""); e.Handled = true; };
                    p.Children.Add(row);
                }
                catch (Exception) { }
            }
            p.Children.Add(Pop.Link("Depolama ayarları", () => { _popup.IsOpen = false; Run("ms-settings:storagesense"); }));
            if (_popup != null) _popup.IsOpen = false;
            _popup = Pop.Make(this, p, S.Settings.Edge == DockEdge.Ust);
            _popup.MinWidth = 300;
            _popup.IsOpen = true;
        }
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

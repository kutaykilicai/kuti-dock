using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using KutiDock.Core;

namespace KutiDock.Tiles
{
    internal sealed class StartTile : Tile
    {
        public override string Id => "start";
        public StartTile(Services s, Theme t) : base(s, t, card: false)
        {
            Width = 42 * K; Height = 42 * K; CornerRadius = new CornerRadius(10 * K); Background = Brushes.Transparent;
            Child = Ui.Glyph(Ui.GStart, Theme.Accent, 22 * K);
            ToolTip = "Başlat";
            MouseEnter += (_, _) => Background = t.TileHover; MouseLeave += (_, _) => Background = Brushes.Transparent;
            var glyph = (FrameworkElement)Child;
            OnClick(() => { Anim.Hop(glyph, s.Settings.Edge, 6 * K); TaskbarRemover.OpenStart(); }); // görev çubuğu açılmadan yalnızca Başlat menüsü
            Ui.PressEffect(this);
        }
        public override void Refresh() { }
    }

    /// <summary>"^" kartı: Windows'un gizli tepsi simgelerini açar (Win+B, sonra Enter).</summary>
    internal sealed class TrayTile : Tile
    {
        public override string Id => "tray";
        public TrayTile(Services s, Theme t) : base(s, t)
        {
            Width = 50 * K; Padding = new Thickness(0);
            Child = Ui.Glyph("M2,9L8,3L14,9L12.6,10.4L8,5.8L3.4,10.4Z", t.FgDim, 12 * K);
            ToolTip = "Gizli simgeler";
            OnClick(() => { Native.PressKeys(Native.VK_LWIN, (byte)'B'); Native.PressKeys(0x0D); });
            Ui.PressEffect(this);
        }
        public override void Refresh() { }
    }

    /// <summary>Saat + tarih + hava durumu (sağ uçtaki kart).</summary>
    internal sealed class ClockTile : Tile
    {
        public override string Id => "clock";
        readonly TextBlock _day, _date, _time, _ampm, _weather, _full;
        public ClockTile(Services s, Theme t) : base(s, t)
        {
            _day = Txt("", 10, w: FontWeights.SemiBold); _day.Foreground = Theme.Red;
            _date = Txt("", 15, w: FontWeights.SemiBold);
            _time = Txt("", 16, w: FontWeights.SemiBold); _ampm = Txt("", 9, true, FontWeights.SemiBold);
            _ampm.Margin = new Thickness(2 * K, 3 * K, 0, 0);
            _weather = Txt("", 10, true); _weather.HorizontalAlignment = HorizontalAlignment.Right;
            var left = Col(_day, _date); left.Margin = new Thickness(0, 0, 16 * K, 0);
            var tr = Row(_time, _ampm); tr.HorizontalAlignment = HorizontalAlignment.Right;
            var right = Col(tr, _weather);
            _full = Txt("", 9, true); _full.HorizontalAlignment = HorizontalAlignment.Center; // tam tarih: 16.11.2026
            Child = Col(Row(left, right), _full);
            OnClick(() => Native.PressKeys(Native.VK_LWIN, (byte)'N')); // Windows 11: bildirimler + takvim
            Refresh();
        }
        public override void Refresh()
        {
            var now = DateTime.Now;
            _day.Text = now.ToString("dddd", Format.Tr).ToUpper(Format.Tr);
            _date.Text = now.ToString("d MMM", Format.Tr);
            _full.Text = now.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture);
            if (S.Settings.Use24Hour) { _time.Text = now.ToString("HH:mm"); _ampm.Text = ""; }
            else { _time.Text = now.ToString("h:mm"); _ampm.Text = now.ToString("tt", System.Globalization.CultureInfo.InvariantCulture); }
            if (S.Weather is { } w)
            {
                var (g, txt) = Weather.Describe(w.Code, w.IsDay);
                _weather.Text = $"{g} {Math.Round(w.TempC)}°";
                ToolTip = $"{S.Settings.City}: {txt}, {Math.Round(w.TempC)}° (en yüksek {Math.Round(w.MaxC)}°, en düşük {Math.Round(w.MinC)}°)";
            }
        }
    }

    /// <summary>Mavi pil kartı. Masaüstü PC'de (pil yoksa) görünmez.</summary>
    internal sealed class BatteryTile : Tile
    {
        public override string Id => "battery";
        public override bool Wanted => S.HasBattery;
        readonly TextBlock _pct; readonly FrameworkElement _bolt; readonly Border _fill;
        static readonly Brush Blue = MakeBlue();
        static Brush MakeBlue() { var b = new LinearGradientBrush(Color.FromRgb(0x4D, 0xA8, 0xFF), Color.FromRgb(0x0A, 0x6C, 0xE0), 90); b.Freeze(); return b; }
        public BatteryTile(Services s, Theme t) : base(s, t, card: false)
        {
            Width = 48 * K; CornerRadius = new CornerRadius(14 * K); ClipToBounds = true;
            Background = Theme.B("#1F2C3A");
            _fill = new Border { VerticalAlignment = VerticalAlignment.Bottom, Background = Blue };
            _pct = Ui.Text("", 14 * K, Brushes.White, FontWeights.SemiBold);
            _bolt = Ui.Glyph(Ui.GBolt, Brushes.White, 10 * K);
            var g = new Grid();
            g.Children.Add(_fill);
            g.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = { _bolt, _pct } });
            Child = g;
            OnClick(() => Run("ms-settings:batterysaver"));
            Refresh();
        }
        public override void Refresh()
        {
            int p = S.Battery ?? 0;
            _pct.Text = " " + p;
            _bolt.Visibility = S.Charging ? Visibility.Visible : Visibility.Collapsed;
            _fill.Height = Math.Max(0, (Height) * p / 100.0);
            _fill.Background = p <= S.Settings.BatteryAlertPercent && !S.Charging ? Theme.Red : Blue;
            ToolTip = $"Pil %{p}" + (S.Charging ? " · şarj oluyor" : "");
        }
    }

    /// <summary>CPU / RAM / GPU halkaları. Tıklayınca Windows optimizasyonu paneli.</summary>
    internal sealed class SystemTile : Tile
    {
        public override string Id => "system";
        readonly Ring _cpu, _ram, _gpu; Popup _popup;
        public SystemTile(Services s, Theme t) : base(s, t)
        {
            Ring R(string cap) => new() { Width = 36 * K, Height = 36 * K, Caption = cap, Fg = t.Fg, Dim = t.FgDim, Track = t.Track, Margin = new Thickness(2 * K, 0, 2 * K, 0) };
            _cpu = R("CPU"); _ram = R("RAM"); _gpu = R("GPU");
            Child = Row(_cpu, _ram, _gpu);
            OnClick(ShowOptimize);
        }

        /// <summary>
        /// Windows optimizasyonu: RAM boşalt (çalışma kümeleri + bekleme listesi), geçici dosyalar (önce MB önizleme, sonra onay),
        /// DNS önbelleği. Oyun açıkken düğmeler pasif. Tarayıcı verisine, oyunlara, Steam'e, anti-cheat'e, GPU sürücüsüne dokunulmaz.
        /// </summary>
        void ShowOptimize()
        {
            var game = Optimizer.RunningGame();
            var dim = Theme.B("#99F5F5F7");
            var p = new StackPanel { Width = 300 };
            p.Children.Add(Ui.Text("Windows optimizasyonu", 15, Brushes.White, FontWeights.SemiBold));
            var info = Ui.Text($"RAM {S.RamUsed / 1073741824.0:0.0}/{S.RamTotal / 1073741824.0:0.0} GB · CPU %{S.Cpu:0}" + (S.Gpu >= 0 ? $" · GPU %{S.Gpu:0}" : ""), 11, dim);
            info.Margin = new Thickness(0, 4, 0, 0); p.Children.Add(info);
            var status = new TextBlock { Foreground = dim, FontSize = 11, FontFamily = Theme.Font, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
            if (game != null) status.Text = $"Oyun açık ({game}): temizlik düğmeleri pasif.";

            Button Btn(string text) => new()
            {
                Content = text, Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(10, 6, 10, 6), IsEnabled = game == null,
                Foreground = Brushes.White, Background = Theme.B("#2C2C30"), BorderBrush = Theme.B("#33FFFFFF"), HorizontalContentAlignment = HorizontalAlignment.Left,
                FontFamily = Theme.Font, FontSize = 12, Cursor = Cursors.Hand,
            };
            var ram = Btn("RAM'i boşalt (çalışma kümeleri + bekleme listesi)");
            var scan = Btn("Geçici dosyaları tara");
            var clean = Btn("Temizle"); clean.Visibility = Visibility.Collapsed; clean.Background = Theme.B("#0A84FF");
            var dns = Btn("DNS önbelleğini temizle");
            Optimizer.TempScan found = null;

            ram.Click += async (_, _) =>
            {
                ram.IsEnabled = false; status.Text = "RAM boşaltılıyor…";
                long before = UsedRam();
                int n = await System.Threading.Tasks.Task.Run(Optimizer.TrimWorkingSets);
                bool standby = await Optimizer.PurgeStandbyAsync();
                await System.Threading.Tasks.Task.Delay(1200);
                long freed = Math.Max(0, before - UsedRam());
                status.Text = $"{n} sürecin çalışma kümesi küçültüldü" + (standby ? ", bekleme listesi boşaltıldı" : ", bekleme listesi için yönetici izni alınamadı") + $". Kullanılan RAM ~{Format.Bytes(freed)} azaldı.";
                ram.IsEnabled = Optimizer.RunningGame() == null;
            };
            scan.Click += async (_, _) =>
            {
                scan.IsEnabled = false; clean.Visibility = Visibility.Collapsed; status.Text = "Taranıyor…";
                found = await Optimizer.ScanAsync();
                status.Text = $"Silinebilir (24 saatten eski): kullanıcı Temp {Format.Bytes(found.UserBytes)} ({found.UserFiles} dosya), Windows\\Temp {Format.Bytes(found.WinBytes)} ({found.WinFiles} dosya). Toplam {Format.Bytes(found.Total)}. Tarayıcı verisine dokunulmaz.";
                clean.Content = $"Onayla: {Format.Bytes(found.Total)} sil";
                clean.Visibility = found.Total > 0 ? Visibility.Visible : Visibility.Collapsed;
                scan.IsEnabled = true;
            };
            clean.Click += async (_, _) =>
            {
                if (Optimizer.RunningGame() is { } g) { status.Text = $"Oyun açık ({g}): temizlik yapılmadı."; return; }
                clean.IsEnabled = false; scan.IsEnabled = false; status.Text = "Siliniyor…";
                long freed = await Optimizer.CleanAsync(found != null && found.WinFiles > 0);
                status.Text = $"Kullanıcı Temp: {Format.Bytes(freed)} silindi" + (found is { WinFiles: > 0 } ? ", Windows\\Temp yönetici olarak temizlendi" : "") + ". Kullanımdaki dosyalar atlandı; DNS önbelleği temizlendi.";
                clean.Visibility = Visibility.Collapsed; clean.IsEnabled = true; scan.IsEnabled = true;
            };
            dns.Click += (_, _) => status.Text = Optimizer.FlushDns() ? "DNS önbelleği temizlendi." : "DNS önbelleği temizlenemedi.";

            foreach (var b in new UIElement[] { ram, scan, clean, dns, status }) p.Children.Add(b);
            p.Children.Add(Pop.Link("Görev Yöneticisi", () => { _popup.IsOpen = false; Run("taskmgr.exe"); }));
            if (_popup != null) _popup.IsOpen = false;
            _popup = Pop.Make(this, p, S.Settings.Edge);
            _popup.IsOpen = true;
        }

        static long UsedRam()
        {
            var m = new Native.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.MEMORYSTATUSEX>() };
            return Native.GlobalMemoryStatusEx(ref m) ? (long)(m.ullTotalPhys - m.ullAvailPhys) : 0;
        }

        public override void Refresh()
        {
            Set(_cpu, S.Cpu); Set(_ram, S.Ram);
            _gpu.Visibility = S.Gpu < 0 ? Visibility.Collapsed : Visibility.Visible;
            if (S.Gpu >= 0) Set(_gpu, S.Gpu);
            ToolTip = $"CPU %{S.Cpu:0} · RAM {S.RamUsed / 1073741824.0:0.0}/{S.RamTotal / 1073741824.0:0.0} GB" + (S.Gpu >= 0 ? $" · GPU %{S.Gpu:0}" : "");
        }
        static void Set(Ring r, double v)
        {
            r.Value = v / 100; r.Label = Math.Round(v).ToString();
            r.Stroke = v >= 90 ? Theme.Red : v >= 70 ? Theme.Orange : Theme.Accent;
            r.InvalidateVisual();
        }
    }

    internal sealed class NetworkTile : Tile
    {
        public override string Id => "network";
        readonly TextBlock _d, _u; readonly Spark _spark;
        public NetworkTile(Services s, Theme t) : base(s, t)
        {
            _d = Txt("", 11, w: FontWeights.SemiBold); _u = Txt("", 10, true);
            var dRow = Row(Ui.Glyph(Ui.GDown, Theme.Accent, 9 * K), _d); _d.Margin = new Thickness(4 * K, 0, 0, 0);
            var uRow = Row(Ui.Glyph(Ui.GUp, t.FgDim, 9 * K), _u); _u.Margin = new Thickness(4 * K, 0, 0, 0);
            var texts = Col(dRow, uRow); texts.Width = 74 * K;
            _spark = new Spark { Width = 56 * K, Height = 24 * K, Margin = new Thickness(6 * K, 0, 0, 0) };
            Child = Row(texts, _spark);
            OnClick(() => Run("ms-settings:network-status"));
        }
        public override void Refresh()
        {
            _d.Text = Format.Rate(S.Down); _u.Text = Format.Rate(S.Up);
            _spark.Values = S.DownHistory.ToArray(); _spark.InvalidateVisual();
        }
    }

    /// <summary>"Hoparlör %8" ve kaydırıcı (sitedeki ses kartı düzeni).</summary>
    internal sealed class VolumeTile : Tile
    {
        public override string Id => "volume";
        readonly TextBlock _pct; readonly Slider _slider; readonly System.Windows.Shapes.Path _icon; bool _updating;
        public VolumeTile(Services s, Theme t) : base(s, t)
        {
            Width = 170 * K;
            _icon = Ui.Glyph(Ui.GSpeaker, t.FgDim, 12 * K);
            var icon = new Border { Child = _icon, Cursor = Cursors.Hand, Background = Brushes.Transparent, Padding = new Thickness(0, 0, 6 * K, 0) };
            icon.MouseLeftButtonUp += (_, e) => { S.Audio.ToggleMute(); Refresh(); e.Handled = true; };
            var label = Txt("Ses", 11, w: FontWeights.SemiBold);
            _pct = Txt("", 10, true); _pct.HorizontalAlignment = HorizontalAlignment.Right;
            var top = new DockPanel();
            DockPanel.SetDock(_pct, Dock.Right); top.Children.Add(_pct);
            var devices = new Border { Child = Ui.Glyph("M2,9L8,3L14,9L12.6,10.4L8,5.8L3.4,10.4Z", t.FgDim, 8 * K), Cursor = Cursors.Hand, Background = Brushes.Transparent, Padding = new Thickness(6 * K, 0, 6 * K, 0), ToolTip = "Ses çıkış aygıtı" };
            devices.MouseLeftButtonUp += (_, e) => { ShowDevices(); e.Handled = true; };
            DockPanel.SetDock(devices, Dock.Right); top.Children.Add(devices);
            top.Children.Add(Row(icon, label));
            _slider = new Slider { Minimum = 0, Maximum = 100, Margin = new Thickness(0, 2 * K, 0, 0), Style = PillSlider(t), Height = 14 * K };
            _slider.ValueChanged += (_, e) => { if (!_updating) { S.Audio.SetLevel((float)(e.NewValue / 100)); _pct.Text = $"%{e.NewValue:0}"; } };
            Child = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { top, _slider } };
            MouseRightButtonUp += (_, e) => { ShowDevices(); e.Handled = true; };
            MouseWheel += (_, e) => { _slider.Value = Math.Clamp(_slider.Value + (e.Delta > 0 ? 2 : -2), 0, 100); e.Handled = true; };
            Refresh();
        }
        Popup _popup;

        /// <summary>Ses çıkış ve giriş (mikrofon) aygıtları; tıklanan aygıt varsayılan olur (tüm roller). Seçilemezse Ses ayarları açılır.</summary>
        void ShowDevices()
        {
            var p = new StackPanel();
            void Section(string title, System.Collections.Generic.List<CoreAudio.Device> list, bool input)
            {
                var head = new DockPanel { Margin = new Thickness(0, p.Children.Count == 0 ? 0 : 14, 0, 0) };
                if (input && S.Audio.Read(true) is { } mic)
                {
                    var mute = Pop.Link(mic.muted ? "Mikrofonu aç" : "Mikrofonu sessize al", () => { S.Audio.SetMute(!mic.muted, true); _popup.IsOpen = false; });
                    mute.Margin = new Thickness(8, 2, 0, 0);
                    DockPanel.SetDock(mute, Dock.Right); head.Children.Add(mute);
                }
                head.Children.Add(Ui.Text(title, 15, Brushes.White, FontWeights.SemiBold));
                p.Children.Add(head);
                if (list.Count == 0) p.Children.Add(Ui.Text("Aygıt bulunamadı", 12, Theme.B("#99F5F5F7")));
                foreach (var d in list)
                {
                    var row = new DockPanel { Margin = new Thickness(0, 8, 0, 0), Background = Brushes.Transparent, Cursor = Cursors.Hand };
                    var mark = Ui.Text(d.IsDefault ? "✓" : "", 12, Theme.Accent, FontWeights.SemiBold); mark.Width = 18;
                    DockPanel.SetDock(mark, Dock.Left); row.Children.Add(mark);
                    row.Children.Add(Ui.Text(d.Name, 12, Brushes.White, d.IsDefault ? FontWeights.SemiBold : FontWeights.Normal, 280));
                    var id = d.Id;
                    row.MouseLeftButtonUp += (_, e) =>
                    {
                        _popup.IsOpen = false;
                        if (!S.Audio.SetDefault(id)) Run("ms-settings:sound"); // arayüz kullanılamazsa Windows ses ayarları
                        Refresh(); e.Handled = true;
                    };
                    p.Children.Add(row);
                }
            }
            Section("Ses çıkışı", S.Audio.Outputs(), false);
            Section("Ses girişi", S.Audio.Inputs(), true);
            p.Children.Add(Pop.Link("Ses ayarları", () => { _popup.IsOpen = false; Run("ms-settings:sound"); }));
            if (_popup != null) _popup.IsOpen = false;
            _popup = Pop.Make(this, p, S.Settings.Edge);
            _popup.IsOpen = true;
        }

        public override void Refresh()
        {
            if (_slider.IsMouseCaptureWithin) return;
            var v = S.Audio.Read();
            if (v == null) { _pct.Text = "—"; return; }
            _updating = true; _slider.Value = Math.Round(v.Value.level * 100); _updating = false;
            _pct.Text = v.Value.muted ? "Sessiz" : $"%{_slider.Value:0}";
            _icon.Fill = v.Value.muted ? Theme.Red : T.FgDim;
        }

        /// <summary>Yuvarlak, kalın, mavi dolgulu kaydırıcı (özgün şablon).</summary>
        static Style PillSlider(Theme t)
        {
            var xaml = $@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Slider'>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Slider'>
    <Grid>
      <Border CornerRadius='7' Background='{((SolidColorBrush)t.Track).Color}'/>
      <Track x:Name='PART_Track'>
        <Track.DecreaseRepeatButton><RepeatButton Command='Slider.DecreaseLarge' Focusable='False'>
          <RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border CornerRadius='7,0,0,7' Background='#0A84FF'/></ControlTemplate></RepeatButton.Template>
        </RepeatButton></Track.DecreaseRepeatButton>
        <Track.IncreaseRepeatButton><RepeatButton Command='Slider.IncreaseLarge' Focusable='False'>
          <RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template>
        </RepeatButton></Track.IncreaseRepeatButton>
        <Track.Thumb><Thumb Width='14'><Thumb.Template><ControlTemplate TargetType='Thumb'>
          <Ellipse Fill='White' Width='14' Height='14'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
      </Track>
    </Grid>
  </ControlTemplate></Setter.Value></Setter>
  <Setter Property='IsMoveToPointEnabled' Value='True'/>
</Style>";
            return (Style)System.Windows.Markup.XamlReader.Parse(xaml);
        }
    }
}

using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using KutayDock.Core;

namespace KutayDock.Tiles
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
            OnClick(() => Native.PressKeys(Native.VK_LWIN));
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
        readonly TextBlock _day, _date, _time, _ampm, _weather;
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
            Child = Row(left, right);
            OnClick(() => Native.PressKeys(Native.VK_LWIN, (byte)'N')); // Windows 11: bildirimler + takvim
            Refresh();
        }
        public override void Refresh()
        {
            var now = DateTime.Now;
            _day.Text = now.ToString("dddd", Format.Tr).ToUpper(Format.Tr);
            _date.Text = now.ToString("d MMM", Format.Tr);
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

    /// <summary>CPU / RAM / GPU halkaları. Tıklayınca Görev Yöneticisi.</summary>
    internal sealed class SystemTile : Tile
    {
        public override string Id => "system";
        readonly Ring _cpu, _ram, _gpu;
        public SystemTile(Services s, Theme t) : base(s, t)
        {
            Ring R(string cap) => new() { Width = 36 * K, Height = 36 * K, Caption = cap, Fg = t.Fg, Dim = t.FgDim, Track = t.Track, Margin = new Thickness(2 * K, 0, 2 * K, 0) };
            _cpu = R("CPU"); _ram = R("RAM"); _gpu = R("GPU");
            Child = Row(_cpu, _ram, _gpu);
            OnClick(() => Run("taskmgr.exe"));
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
            top.Children.Add(Row(icon, label));
            _slider = new Slider { Minimum = 0, Maximum = 100, Margin = new Thickness(0, 2 * K, 0, 0), Style = PillSlider(t), Height = 14 * K };
            _slider.ValueChanged += (_, e) => { if (!_updating) { S.Audio.SetLevel((float)(e.NewValue / 100)); _pct.Text = $"%{e.NewValue:0}"; } };
            Child = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { top, _slider } };
            MouseWheel += (_, e) => { _slider.Value = Math.Clamp(_slider.Value + (e.Delta > 0 ? 2 : -2), 0, 100); e.Handled = true; };
            Refresh();
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

    internal sealed class MediaTile : Tile
    {
        public override string Id => "media";
        readonly TextBlock _title, _artist; readonly Border _play;
        public MediaTile(Services s, Theme t) : base(s, t)
        {
            _title = Txt("Çalan bir şey yok", 11, w: FontWeights.SemiBold, maxW: 130);
            _artist = Txt("", 10, true, maxW: 130);
            var texts = Col(_title, _artist); texts.Width = 130 * K;
            _play = Ui.IconButton(Ui.GPlay, t.Fg, 28 * K, () => S.MediaCommand("toggle"), 11 * K);
            var prev = Ui.IconButton(Ui.GPrev, t.Fg, 24 * K, () => S.MediaCommand("prev"), 9 * K);
            var next = Ui.IconButton(Ui.GNext, t.Fg, 24 * K, () => S.MediaCommand("next"), 9 * K);
            var cover = new Border
            {
                Width = 34 * K, Height = 34 * K, CornerRadius = new CornerRadius(17 * K), Margin = new Thickness(0, 0, 8 * K, 0),
                Background = new LinearGradientBrush(Color.FromRgb(0x2B, 0x2B, 0x30), Color.FromRgb(0x10, 0x10, 0x12), 45),
                Child = Ui.Glyph("M7,2V13.5A3.5,3.5 0 1 0 9,16.6V6H15V2Z", t.FgDim, 14 * K),
            };
            Child = Row(cover, texts, prev, _play, next);
        }
        public override void Refresh()
        {
            _title.Text = S.MediaAvailable && !string.IsNullOrEmpty(S.MediaTitle) ? S.MediaTitle : "Çalan bir şey yok";
            _artist.Text = S.MediaAvailable ? S.MediaArtist ?? "" : "";
            ((System.Windows.Shapes.Path)_play.Child).Data = Geometry.Parse(S.MediaPlaying ? Ui.GPause : Ui.GPlay);
        }
    }

    internal sealed class ScreenTimeTile : Tile
    {
        public override string Id => "screentime";
        readonly TextBlock _total; readonly Spark _bars;
        public ScreenTimeTile(Services s, Theme t) : base(s, t)
        {
            var watch = new Border { Width = 26 * K, Height = 34 * K, Child = Ui.Glyph(Ui.GWatch, Theme.Accent, 22 * K), Margin = new Thickness(0, 0, 8 * K, 0) };
            _total = Txt("", 17, w: FontWeights.SemiBold);
            var texts = Col(Txt("Ekran süresi", 10, true), _total);
            _bars = new Spark { Columns = true, Width = 60 * K, Height = 20 * K, Margin = new Thickness(10 * K, 0, 0, 0), VerticalAlignment = VerticalAlignment.Bottom };
            Child = Row(watch, texts, _bars);
        }
        public override void Refresh()
        {
            var d = S.ScreenTime.Day(DateTime.Now);
            _total.Text = Format.Duration(TimeSpan.FromSeconds(d.TotalSeconds));
            int h = DateTime.Now.Hour;
            _bars.Values = Enumerable.Range(Math.Max(0, h - 11), Math.Min(12, h + 1)).Select(i => d.Hours[i]).ToArray();
            _bars.InvalidateVisual();
            ToolTip = "Bugün en çok:\n" + string.Join("\n", S.ScreenTime.Top(DateTime.Now, 5).Select(x => $"{x.App}: {Format.Duration(TimeSpan.FromSeconds(x.Seconds))}"));
        }
    }

    /// <summary>Sıradaki etkinlik: gün kutusu + başlık + "14:30 · 1s 27dk sonra".</summary>
    internal sealed class CalendarTile : Tile
    {
        public override string Id => "calendar";
        readonly TextBlock _dow, _dayNum, _title, _when;
        readonly Action _openSettings;
        public CalendarTile(Services s, Theme t, Action openSettings) : base(s, t)
        {
            _openSettings = openSettings;
            _dow = Txt("", 9, w: FontWeights.Bold); _dow.Foreground = Theme.Red; _dow.HorizontalAlignment = HorizontalAlignment.Center;
            _dayNum = Txt("", 18, w: FontWeights.Normal); _dayNum.HorizontalAlignment = HorizontalAlignment.Center;
            var box = Col(_dow, _dayNum); box.Width = 30 * K; box.Margin = new Thickness(0, 0, 10 * K, 0);
            var bar = new Border { Width = 3 * K, Height = 14 * K, CornerRadius = new CornerRadius(1.5 * K), Background = Theme.Orange, Margin = new Thickness(0, 0, 6 * K, 0) };
            _title = Txt("", 11, w: FontWeights.SemiBold, maxW: 170);
            _when = Txt("", 10, true, maxW: 180);
            Child = Row(box, Col(Row(bar, _title), _when));
            OnClick(() => { if (S.Settings.IcsUrls.Count == 0) _openSettings(); else Run("outlookcal:"); });
        }
        public override void Refresh()
        {
            var now = DateTimeOffset.Now;
            _dow.Text = now.ToString("ddd", Format.Tr).ToUpper(Format.Tr);
            _dayNum.Text = now.Day.ToString();
            if (S.Settings.IcsUrls.Count == 0) { _title.Text = "Takvim ekle"; _when.Text = "Ayarlar › Takvim (iCal linki)"; return; }
            var ev = Ics.Next(S.Events, now);
            if (ev == null) { _title.Text = S.CalendarError ?? "Yaklaşan etkinlik yok"; _when.Text = "Önümüzdeki 14 gün boş"; return; }
            _title.Text = ev.Title;
            var local = ev.Start.ToLocalTime();
            string day = local.Date == DateTime.Today ? "" : local.Date == DateTime.Today.AddDays(1) ? "Yarın " : local.ToString("d MMM ", Format.Tr);
            _when.Text = ev.Start <= now
                ? $"Şimdi · {Format.Duration(ev.End - now)} kaldı"
                : $"{day}{local:HH:mm} · {Format.Duration(ev.Start - now)} sonra";
        }
    }

    /// <summary>Oturum / Haftalık yapay zekâ kullanım çubukları.</summary>
    internal sealed class AiTile : Tile
    {
        public override string Id => "ai";
        readonly Bar _s, _w; readonly TextBlock _sReset, _wReset, _sPct, _wPct;
        public AiTile(Services s, Theme t) : base(s, t)
        {
            var badge = new Border
            {
                Width = 36 * K, Height = 36 * K, CornerRadius = new CornerRadius(10 * K), Margin = new Thickness(0, 0, 10 * K, 0),
                Background = s.Settings.AiSource == AiSource.Codex ? Brushes.White : Theme.B("#D97757"),
                Child = Ui.Glyph(Ui.GSpark, s.Settings.AiSource == AiSource.Codex ? Brushes.Black : Brushes.White, 18 * K),
            };
            Grid g = new() { VerticalAlignment = VerticalAlignment.Center };
            foreach (var w in new[] { 56.0, 80, 52, 36 }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w * K) });
            g.RowDefinitions.Add(new RowDefinition()); g.RowDefinitions.Add(new RowDefinition());
            void Put(UIElement e, int r, int c) { Grid.SetRow(e, r); Grid.SetColumn(e, c); g.Children.Add(e); }
            Bar B() => new() { Height = 5 * K, Margin = new Thickness(0, 0, 6 * K, 0), VerticalAlignment = VerticalAlignment.Center, Track = t.Track };
            _s = B(); _w = B();
            _sReset = Txt("", 9, true); _wReset = Txt("", 9, true);
            _sPct = Txt("", 11, w: FontWeights.SemiBold); _wPct = Txt("", 11, w: FontWeights.SemiBold);
            _sPct.HorizontalAlignment = _wPct.HorizontalAlignment = HorizontalAlignment.Right;
            _sReset.HorizontalAlignment = _wReset.HorizontalAlignment = HorizontalAlignment.Right;
            Put(Txt("Oturum", 10, w: FontWeights.SemiBold), 0, 0); Put(_s, 0, 1); Put(_sReset, 0, 2); Put(_sPct, 0, 3);
            Put(Txt("Haftalık", 10, w: FontWeights.SemiBold), 1, 0); Put(_w, 1, 1); Put(_wReset, 1, 2); Put(_wPct, 1, 3);
            Child = Row(badge, g);
        }
        public override void Refresh()
        {
            var a = S.Ai;
            if (a == null)
            {
                _sPct.Text = _wPct.Text = "—"; _sReset.Text = _wReset.Text = ""; _s.Value = _w.Value = 0;
                ToolTip = S.AiError ?? "Okunuyor…"; _s.InvalidateVisual(); _w.InvalidateVisual(); return;
            }
            var now = DateTimeOffset.Now;
            _s.Value = a.SessionFraction; _w.Value = a.WeeklyFraction;
            _s.Fill = Color(a.SessionFraction); _w.Fill = Color(a.WeeklyFraction);
            _sPct.Text = $"%{a.SessionFraction * 100:0}"; _wPct.Text = $"%{a.WeeklyFraction * 100:0}";
            _sReset.Text = a.SessionResetsAt is { } sr ? Format.Duration(sr - now) : "boşta";
            _wReset.Text = a.WeeklyResetsAt is { } wr ? wr.ToLocalTime().ToString("ddd d", Format.Tr) : "";
            ToolTip = $"{a.Provider}\nOturum: {a.SessionDetail}\nHaftalık: {a.WeeklyDetail}" + (a.TopModel != null ? $"\nEn çok: {a.TopModel}" : "") +
                      (a.Provider == "Claude Code" ? "\nYüzdeler Ayarlar'daki token limitine göre tahmindir." : "");
            _s.InvalidateVisual(); _w.InvalidateVisual();
        }
        static Brush Color(double f) => f >= 0.9 ? Theme.Red : f >= 0.75 ? Theme.Orange : Theme.Green;
    }
}

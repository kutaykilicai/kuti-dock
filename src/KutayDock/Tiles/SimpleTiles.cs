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
}

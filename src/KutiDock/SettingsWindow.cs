using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using KutiDock.Core;

namespace KutiDock
{
    /// <summary>Ayarlar penceresi (koyu tema, koyu başlık çubuğu, yuvarlak köşe). Kaydet'e basınca dock yeniden kurulur.</summary>
    internal sealed class SettingsWindow : Window
    {
        readonly Settings _orig;
        readonly Settings _s;
        readonly Action<Settings> _apply;
        readonly CursorAccount _cursor;

        static readonly (string id, string name)[] AllWidgets =
        {
            ("start", "Başlat düğmesi"), ("search", "Ara (Windows araması; ayarda Everything)"), ("apps", "Uygulamalar (sabitlenenler + açık pencereler)"), ("ai", "Limitler (Grok Bot; tıklayınca Cursor)"),
            ("volume", "Ses"), ("system", "Sistem (CPU / RAM / GPU)"), ("network", "Ağ hızı"), ("warp", "Cloudflare WARP bağlan / kes"),
            ("storage", "Depolama (disk boş alanı)"), ("screenshot", "Ekran görüntüsü (ShareX)"), ("battery", "Pil (yalnızca dizüstü)"), ("tray", "Gizli tepsi simgeleri (^)"), ("clock", "Saat, tarih ve hava durumu"),
        };

        readonly Action _openMac;

        public SettingsWindow(Settings current, Action<Settings> apply, CursorAccount cursor, Action openMac = null)
        {
            _openMac = openMac;
            _orig = current; _apply = apply; _cursor = cursor;
            _s = Settings.Deserialize<Settings>(Settings.Serialize(current)); // kopya
            Title = "Kuti Dock · Ayarlar"; Width = 620; Height = 760; MinWidth = 520;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x18));
            FontFamily = Theme.Font;
            Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/KutiDock;component/SettingsStyles.xaml") });
            SourceInitialized += (_, _) => DarkFrame();
            Content = BuildUi();
        }

        void DarkFrame()
        {
            try
            {
                var h = new WindowInteropHelper(this).Handle;
                int dark = 1; DwmSetWindowAttribute(h, 20, ref dark, 4);        // koyu başlık çubuğu
                int corner = 2; DwmSetWindowAttribute(h, 33, ref corner, 4);    // yuvarlak köşe
            }
            catch (Exception) { }
        }
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int s);

        UIElement BuildUi()
        {
            var root = new DockPanel();
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(20, 12, 20, 16) };
            var cancel = new Button { Content = "Vazgeç", Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
            cancel.Click += (_, _) => Close();
            var save = new Button { Content = "Kaydet", IsDefault = true };
            save.Click += (_, _) => { _apply(_s); Close(); };
            buttons.Children.Add(cancel); buttons.Children.Add(save);
            DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);

            var p = new StackPanel { Margin = new Thickness(20, 16, 20, 0) };
            p.Children.Add(new TextBlock { Text = "Kuti Dock", FontSize = 24, FontWeight = FontWeights.SemiBold });
            p.Children.Add(new TextBlock { Text = "Değişiklikler Kaydet'e basınca uygulanır.", Foreground = Brush("#9A9AA0"), Margin = new Thickness(0, 2, 0, 8) });

            // Görünüm
            var look = Card(p, "Görünüm");
            look.Children.Add(Label("Stil"));
            look.Children.Add(Pills(new[] { ("Cam", DockStyle.Cam), ("Siyah", DockStyle.Siyah), ("Gece", DockStyle.Gece), ("Beyaz", DockStyle.Beyaz) }, _s.Style, v => _s.Style = v));
            look.Children.Add(Label("Konum"));
            look.Children.Add(Pills(new[] { ("Altta", DockEdge.Alt), ("Üstte", DockEdge.Ust), ("Solda", DockEdge.Sol), ("Sağda", DockEdge.Sag) }, _s.Edge, v => _s.Edge = v));
            look.Children.Add(Label("Boyut"));
            look.Children.Add(Pills(new[] { ("Küçük", 0.85), ("Orta", 1.0), ("Büyük", 1.15) }, _s.Scale, v => _s.Scale = v));
            look.Children.Add(Toggle("Tam genişlik", _s.FullWidth, v => _s.FullWidth = v));
            look.Children.Add(Toggle("24 saat biçimi", _s.Use24Hour, v => _s.Use24Hour = v));

            // Mac dock (Gravitone motoru)
            var mac = Card(p, "Mac dock");
            mac.Children.Add(Toggle("Ayrı Mac dock katmanı (akrilik plaka, yığınlar, Geri Dönüşüm Kutusu)", _s.MacDock, v => _s.MacDock = v));
            mac.Children.Add(new TextBlock { Text = "Kapalıyken (varsayılan) klasik dock: simgeler Mac gibi dalga hâlinde büyür, tıklayınca sıçrama ve Genie; menü çubuğu üstte. Açıkken uygulamalar Mac dock'ta durur, Başlat ve Ara solda, widget'lar sağda (tek şerit, asla üst üste binmez); Windows görev çubuğunu Mac dock yönetir. Menü çubuğu, Genie, büyütme, ekran ve görev çubuğu ayarları Mac dock ayarlarında (dock'a sağ tık).", Foreground = Brush("#9A9AA0"), TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 2, 0, 6) });
            if (_openMac != null)
            {
                var macBtn = new Button { Content = "Mac dock ayarları…", HorizontalAlignment = HorizontalAlignment.Left };
                macBtn.Click += (_, _) => _openMac();
                mac.Children.Add(macBtn);
            }

            // Davranış
            var beh = Card(p, "Davranış");
            beh.Children.Add(Toggle("Ekranda dock için yer ayır (pencereler altına girmez)", _s.ReserveSpace, v => _s.ReserveSpace = v));
            beh.Children.Add(Toggle("Dock'u ekran kenarında sakla (fare kenara gelince belirir)", _s.AutoHideDock, v => _s.AutoHideDock = v));
            beh.Children.Add(Toggle("Dock fareyi takip etsin (hangi ekranın kenarına gidersen orada belirir)", _s.FollowMouse, v => _s.FollowMouse = v));
            beh.Children.Add(Toggle("Yalnızca ana ekran", _s.PrimaryOnly, v => _s.PrimaryOnly = v));
            beh.Children.Add(Toggle("Tam ekran oyun/videoda dock'u gizle", _s.HideOnFullscreen, v => _s.HideOnFullscreen = v));
            beh.Children.Add(Toggle("Windows görev çubuğunu otomatik gizle (çıkınca geri gelir)", _s.AutoHideWindowsTaskbar, v => _s.AutoHideWindowsTaskbar = v));
            beh.Children.Add(Toggle("Windows görev çubuğunu tamamen kaldır (dock kapanınca/çökünce geri gelir)", _s.RemoveWindowsTaskbar, v => _s.RemoveWindowsTaskbar = v));
            beh.Children.Add(Toggle("Aramada Everything kullan (kapalıyken Windows araması gibi)", _s.SearchWithEverything, v => _s.SearchWithEverything = v));
            beh.Children.Add(Toggle("macOS büyütmesi (simgeler imlecin çevresinde büyür)", _s.Magnification, v => _s.Magnification = v));
            beh.Children.Add(Toggle("Animasyonlar (açılış sıçraması, açılma/kapanma)", _s.Animations, v => _s.Animations = v));

            // Widget'lar
            var wid = Card(p, "Widget'lar");
            wid.Children.Add(new TextBlock { Text = "Sırayı oklarla değiştir. Açık olanlar dock'ta soldan sağa bu sırayla durur.", Foreground = Brush("#9A9AA0"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
            var list = new StackPanel(); wid.Children.Add(list);
            var order = _s.Widgets.Concat(AllWidgets.Select(w => w.id).Where(id => !_s.Widgets.Contains(id))).ToList();
            var enabled = new HashSet<string>(_s.Widgets);
            void Render()
            {
                list.Children.Clear();
                for (int i = 0; i < order.Count; i++)
                {
                    int idx = i; var id = order[i];
                    var name = AllWidgets.FirstOrDefault(w => w.id == id).name ?? id;
                    var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                    var up = Small("▲", () => { if (idx > 0) { (order[idx - 1], order[idx]) = (order[idx], order[idx - 1]); Sync(); Render(); } });
                    var down = Small("▼", () => { if (idx < order.Count - 1) { (order[idx + 1], order[idx]) = (order[idx], order[idx + 1]); Sync(); Render(); } });
                    DockPanel.SetDock(up, Dock.Left); DockPanel.SetDock(down, Dock.Left);
                    row.Children.Add(up); row.Children.Add(down);
                    row.Children.Add(Toggle(name, enabled.Contains(id), v => { if (v) enabled.Add(id); else enabled.Remove(id); Sync(); }));
                    list.Children.Add(row);
                }
            }
            void Sync() => _s.Widgets = order.Where(enabled.Contains).ToList();
            Render();

            // Hava
            var wx = Card(p, "Hava durumu (Open-Meteo, anahtarsız)");
            wx.Children.Add(TextField("Şehir adı", _s.City, v => _s.City = v));
            wx.Children.Add(Number("Enlem", _s.Latitude, v => _s.Latitude = v));
            wx.Children.Add(Number("Boylam", _s.Longitude, v => _s.Longitude = v));

            // Limitler
            var lim = Card(p, "Limitler");
            lim.Children.Add(new TextBlock { Text = "İstersen Cursor hesabınla giriş yap: Grok Bot yüzdesi ve sıfırlanma zamanı 5 dakikada bir Cursor'dan okunur. Yoksa ya da okunamazsa aşağıdaki elle girilen değerler kullanılır (boş = kaynak yok). Cursor planı (ör. Pro+) Cursor CLI'nin \"agent about\" komutundan okunur. Tarih biçimi: 17.11.2026 13:07", Foreground = Brush("#9A9AA0"), TextWrapping = TextWrapping.Wrap });
            CursorLogin(lim);
            LimitFields(lim, "Grok Bot (elle; giriş yoksa ya da okunamazsa kullanılır)", _s.GrokBot);
            LimitFields(lim, "Cursor", _s.Cursor);

            // Ekran görüntüsü
            var shot = Card(p, "Ekran görüntüsü (ShareX)");
            shot.Children.Add(new TextBlock { Text = "Boş bırakırsan ShareX.exe otomatik bulunur (Program Files, kullanıcı klasörü, kayıt defteri).", Foreground = Brush("#9A9AA0"), TextWrapping = TextWrapping.Wrap });
            shot.Children.Add(TextField("ShareX.exe yolu", _s.ShareXPath, v => _s.ShareXPath = v));

            // Uyarılar
            var al = Card(p, "Akıllı uyarılar");
            al.Children.Add(Toggle("Grok Bot / Cursor limiti eşiği geçince uyar", _s.AlertAiLimit, v => _s.AlertAiLimit = v));
            al.Children.Add(Number("Uyarı eşiği (%)", _s.AiAlertPercent, v => _s.AiAlertPercent = Math.Clamp((int)v, 1, 100)));
            al.Children.Add(Toggle("Pil azalınca uyar", _s.AlertBattery, v => _s.AlertBattery = v));
            al.Children.Add(Number("Pil eşiği (%)", _s.BatteryAlertPercent, v => _s.BatteryAlertPercent = Math.Clamp((int)v, 1, 100)));
            al.Children.Add(Toggle("Sessiz saatler (bildirimler bekletilir)", _s.QuietHoursEnabled, v => _s.QuietHoursEnabled = v));
            al.Children.Add(Number("Sessiz saat başlangıcı (0-23)", _s.QuietStartHour, v => _s.QuietStartHour = Math.Clamp((int)v, 0, 23)));
            al.Children.Add(Number("Sessiz saat bitişi (0-23)", _s.QuietEndHour, v => _s.QuietEndHour = Math.Clamp((int)v, 0, 23)));

            p.Children.Add(new TextBlock { Text = "Kuti Dock 1.6.10 · MIT · Mac dock motoru: Gravitone (MIT, Stefano Lupano) · hesap yok, telemetri yok. Veriler: %APPDATA%\\KutiDock", Foreground = Brush("#6A6A70"), FontSize = 11, Margin = new Thickness(0, 8, 0, 16) });
            root.Children.Add(new ScrollViewer { Content = p, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            return root;
        }

        static SolidColorBrush Brush(string hex) => Theme.B(hex);

        void CursorLogin(Panel parent)
        {
            var box = new StackPanel { Margin = new Thickness(0, 8, 0, 4) };
            var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
            var login = new Button { Content = "Cursor ile giriş yap", Margin = new Thickness(0, 0, 8, 0) };
            var logout = new Button { Content = "Çıkış yap" };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(login); buttons.Children.Add(logout);
            box.Children.Add(status); box.Children.Add(buttons);
            box.Children.Add(new TextBlock
            {
                Text = "Risk: Cursor'un belgelenmemiş dahili API'si kullanılır; Cursor değiştirirse çalışmayabilir. Oturum belirteci tüm Cursor hesabına erişir; yalnızca bu PC'de, Windows'un kullanıcıya özel şifrelemesiyle (DPAPI) saklanır. \"Çıkış yap\" belirteci siler.",
                Foreground = Brush("#9A9AA0"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0),
            });
            string message = null;
            void Render()
            {
                status.Text = message ?? (_cursor.LoggingIn ? "Tarayıcıda Cursor girişini onayla… (15 dk)"
                    : !_cursor.SignedIn ? "Cursor girişi yok (elle girilen değerler kullanılıyor)."
                    : _cursor.NeedsRelogin ? "Oturum geçersiz: yeniden giriş yap."
                    : _cursor.SandAt is { } t ? $"Girişli. Grok Bot son okuma: {t.ToLocalTime():HH:mm}"
                    : "Girişli. Grok Bot kullanımı okunuyor…");
                status.Foreground = _cursor.SignedIn && _cursor.NeedsRelogin ? Theme.Orange : Brushes.White;
                login.Content = _cursor.LoggingIn ? "Girişi iptal et" : _cursor.SignedIn ? "Yeniden giriş yap" : "Cursor ile giriş yap";
                logout.IsEnabled = _cursor.SignedIn;
            }
            void OnChanged() => Dispatcher.BeginInvoke(new Action(Render));
            _cursor.Changed += OnChanged;
            Closed += (_, _) => _cursor.Changed -= OnChanged;
            login.Click += async (_, _) =>
            {
                if (_cursor.LoggingIn) { _cursor.CancelLogin(); return; }
                message = null; Render();
                message = await _cursor.LoginAsync(); // tarayıcı yalnızca bu düğmeyle açılır
                Render(); message = null;
            };
            logout.Click += (_, _) => { _cursor.Logout(); message = "Çıkış yapıldı, belirteç silindi."; Render(); message = null; };
            Render();
            parent.Children.Add(box);
        }

        static void LimitFields(Panel parent, string name, ManualLimit l)
        {
            parent.Children.Add(Label(name));
            var tr = Format.Tr;
            var pct = Free("Kullanılan (%)", l.Percent?.ToString("0.#", tr) ?? "", t =>
            {
                if (t.Length == 0) { l.Percent = null; return true; }
                if (!double.TryParse(t.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || v < 0 || v > 100) return false;
                l.Percent = v; return true;
            }, () => l.UpdatedAt = DateTimeOffset.Now);
            var rst = Free("Sıfırlanma (gg.aa.yyyy ss:dd)", l.ResetsAt?.ToLocalTime().ToString("dd.MM.yyyy HH:mm", tr) ?? "", t =>
            {
                if (t.Length == 0) { l.ResetsAt = null; return true; }
                if (!DateTime.TryParseExact(t, new[] { "dd.MM.yyyy HH:mm", "d.M.yyyy H:mm", "dd.MM.yyyy" }, tr, DateTimeStyles.AssumeLocal, out var d)) return false;
                l.ResetsAt = new DateTimeOffset(d); return true;
            }, () => l.UpdatedAt = DateTimeOffset.Now);
            parent.Children.Add(pct); parent.Children.Add(rst);
        }

        static DockPanel Free(string label, string value, Func<string, bool> tryset, Action changed)
        {
            var d = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            var tb = new TextBox { Text = value, Width = 180 };
            tb.TextChanged += (_, _) => { if (tryset(tb.Text.Trim())) { tb.BorderBrush = Brush("#3A3A40"); changed(); } else tb.BorderBrush = Theme.Red; };
            DockPanel.SetDock(tb, Dock.Right); d.Children.Add(tb);
            d.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            return d;
        }

        static StackPanel Card(Panel parent, string title)
        {
            var inner = new StackPanel();
            var b = new Border { Background = Brush("#C81F1F22"), CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 6, 0, 6), BorderBrush = Brush("#2E2E33"), BorderThickness = new Thickness(1) };
            inner.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
            b.Child = inner; parent.Children.Add(b);
            return inner;
        }

        static TextBlock Label(string s) => new() { Text = s, Foreground = Brush("#9A9AA0"), Margin = new Thickness(0, 8, 0, 4) };

        static CheckBox Toggle(string text, bool value, Action<bool> set)
        {
            var c = new CheckBox { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, IsChecked = value };
            c.Checked += (_, _) => set(true); c.Unchecked += (_, _) => set(false);
            return c;
        }

        static WrapPanel Pills<T>((string label, T value)[] options, T current, Action<T> set)
        {
            var w = new WrapPanel();
            var group = Guid.NewGuid().ToString();
            foreach (var (label, value) in options)
            {
                var r = new RadioButton { Content = new TextBlock { Text = label }, GroupName = group, IsChecked = EqualityComparer<T>.Default.Equals(value, current), Margin = new Thickness(0, 0, 6, 4) };
                var v = value; r.Checked += (_, _) => set(v);
                w.Children.Add(r);
            }
            return w;
        }

        static Button Small(string s, Action a)
        {
            var b = new Button { Content = new TextBlock { Text = s, FontSize = 10 }, Width = 28, Height = 26, Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(0) };
            b.Click += (_, _) => a();
            return b;
        }

        static DockPanel Number(string label, double value, Action<double> set)
        {
            var d = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            var tb = new TextBox { Text = value.ToString(CultureInfo.InvariantCulture), Width = 140 };
            tb.TextChanged += (_, _) =>
            {
                var t = tb.Text.Replace(',', '.');
                if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) { set(v); tb.BorderBrush = Brush("#3A3A40"); }
                else tb.BorderBrush = Theme.Red;
            };
            DockPanel.SetDock(tb, Dock.Right); d.Children.Add(tb);
            d.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            return d;
        }

        static DockPanel TextField(string label, string value, Action<string> set)
        {
            var d = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            var tb = new TextBox { Text = value, Width = 220 };
            tb.TextChanged += (_, _) => set(tb.Text.Trim());
            DockPanel.SetDock(tb, Dock.Right); d.Children.Add(tb);
            d.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            return d;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using KutiDock.Core;

namespace KutiDock.Tiles
{
    /// <summary>
    /// Everything ile arka planda dosya arama (belgelenmiş IPC, WM_COPYDATA). Penceresi açılmaz, ayarlarına dokunulmaz;
    /// 1.6.11: aramadaki Everything anahtarı açıkken çalışmıyorsa EverythingLauncher onu tepside başlatır.
    /// </summary>
    internal sealed class EverythingClient : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] struct COPYDATASTRUCT { public IntPtr dwData; public int cbData; public IntPtr lpData; }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string name);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, ref COPYDATASTRUCT l);
        const int WM_COPYDATA = 0x4A;

        readonly HwndSource _sink;   // görünmez, yalnızca ileti alan pencere (HWND_MESSAGE)
        uint _id = 1000;
        readonly Dictionary<uint, TaskCompletionSource<byte[]>> _waiting = new();

        public EverythingClient()
        {
            _sink = new HwndSource(new HwndSourceParameters("KutiDockEverything") { ParentWindow = new IntPtr(-3), WindowStyle = 0 });
            _sink.AddHook(Hook);
        }

        public static bool Running => FindWindow(EverythingIpc.WindowClass, null) != IntPtr.Zero;

        /// <summary>Basit arama (ad + klasör). Everything yoksa null; yalnızca son sorgunun yanıtı döner.</summary>
        public async Task<List<SearchHit>> Search(string text, int max = 20)
        {
            var b = await Send(EverythingIpc.CopyDataQueryW, id => EverythingIpc.BuildQuery((uint)_sink.Handle.ToInt64(), id, text, max), latestOnly: true);
            return b == null ? null : EverythingIpc.ParseList(b);
        }

        /// <summary>Boyutlu sorgu (QUERY2): ad, klasör, boyut; boyuta göre azalan.</summary>
        public async Task<List<(string Name, string Folder, long Size, bool IsFolder)>> Sized(string text, int max)
        {
            var b = await Send(EverythingIpc.CopyDataQuery2W, id => EverythingIpc.BuildQuery2((uint)_sink.Handle.ToInt64(), id, text, max,
                EverythingIpc.RequestName | EverythingIpc.RequestPath | EverythingIpc.RequestSize, EverythingIpc.SortSizeDescending), latestOnly: false);
            return b == null ? null : EverythingIpc.ParseList2(b);
        }

        Task<byte[]> Send(int type, Func<uint, byte[]> build, bool latestOnly)
        {
            var ev = FindWindow(EverythingIpc.WindowClass, null);
            if (ev == IntPtr.Zero) return Task.FromResult<byte[]>(null);
            uint id = ++_id;
            if (latestOnly) { foreach (var w in _waiting.Values) w.TrySetResult(null); _waiting.Clear(); }
            var tcs = new TaskCompletionSource<byte[]>();
            _waiting[id] = tcs;
            var data = build(id);
            var p = Marshal.AllocHGlobal(data.Length);
            try
            {
                Marshal.Copy(data, 0, p, data.Length);
                var cds = new COPYDATASTRUCT { dwData = new IntPtr(type), cbData = data.Length, lpData = p };
                if (SendMessage(ev, WM_COPYDATA, _sink.Handle, ref cds) == IntPtr.Zero) { _waiting.Remove(id); return Task.FromResult<byte[]>(null); }
            }
            finally { Marshal.FreeHGlobal(p); }
            // Yanıt gelmezse 10 sn sonra vazgeç
            _ = Task.Delay(10_000).ContinueWith(_ => tcs.TrySetResult(null));
            return tcs.Task;
        }

        IntPtr Hook(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg != WM_COPYDATA) return IntPtr.Zero;
            var cds = Marshal.PtrToStructure<COPYDATASTRUCT>(l);
            uint id = (uint)cds.dwData.ToInt64();
            handled = true;
            if (_waiting.Remove(id, out var tcs))
            {
                var b = new byte[cds.cbData];
                Marshal.Copy(cds.lpData, b, 0, cds.cbData);
                tcs.TrySetResult(b);
            }
            return new IntPtr(1);
        }

        public void Dispose()
        {
            foreach (var w in _waiting.Values) w.TrySetResult(null);
            _sink.Dispose();
        }
    }

    /// <summary>Dock'taki büyüteç; tıklayınca arama paneli.</summary>
    internal sealed class SearchTile : Tile
    {
        public override string Id => "search";
        public SearchTile(Services s, Theme t) : base(s, t, card: false)
        {
            Width = 42 * K; Height = 42 * K; CornerRadius = new CornerRadius(10 * K); Background = Brushes.Transparent;
            Child = Ui.Glyph("M9.5,3A6.5,6.5 0 0,1 16,9.5C16,11.11 15.41,12.59 14.44,13.73L14.71,14H15.5L20.5,19L19,20.5L14,15.5V14.71L13.73,14.44C12.59,15.41 11.11,16 9.5,16A6.5,6.5 0 0,1 3,9.5A6.5,6.5 0 0,1 9.5,3M9.5,5C7,5 5,7 5,9.5C5,12 7,14 9.5,14C12,14 14,12 14,9.5C14,7 12,5 9.5,5Z", t.Fg, 20 * K);
            ToolTip = "Ara";
            MouseEnter += (_, _) => Background = t.TileHover; MouseLeave += (_, _) => Background = Brushes.Transparent;
            OnClick(() => SearchPanel.Toggle(this, S.Settings.Edge, S.Settings.SearchWithEverything, v =>
            {
                // 1.6.11: aramadaki anahtar Ayarlar'daki "Aramada Everything kullan" ile aynı ayar; kalıcı
                S.Settings.SearchWithEverything = v;
                try { S.Settings.Save(System.IO.Path.Combine(Services.DataDir, "ayarlar.json")); } catch (Exception) { }
            }));
            Ui.PressEffect(this);
        }
        public override void Refresh() { }
    }

    /// <summary>
    /// Windows arama kutusu gibi küçük koyu panel. Varsayılan: Windows araması gibi uygulamalar, ayarlar ve dosyalar
    /// (Windows Search dizini; boşken son dosyalar). Ayarlarda "Everything ile ara" açıksa Everything kullanılır.
    /// Hiçbir dış pencere açılmaz; yalnızca seçilen sonuç açılır.
    /// </summary>
    internal sealed class SearchPanel : Window
    {
        static SearchPanel _current;
        public static void Toggle(FrameworkElement anchor, DockEdge edge, bool everything, Action<bool> setEverything)
        {
            if (_current != null) { _current.Close(); return; }
            _current = new SearchPanel(anchor, edge, everything, setEverything);
            _current.Show();
            _current.Activate();
            Native.Activate(new WindowInteropHelper(_current).Handle); // dock odak almadığı için klavyeyi panele ver
        }

        readonly FrameworkElement _anchor;
        readonly DockEdge _edge;
        readonly Action<bool> _setEverything;
        bool _everything;
        readonly TextBox _box;
        readonly ListBox _list;
        readonly TextBlock _status;
        EverythingClient _ev;
        readonly DispatcherTimer _typing = new() { Interval = TimeSpan.FromMilliseconds(120) };
        int _seq;
        bool _closing;

        SearchPanel(FrameworkElement anchor, DockEdge edge, bool everything, Action<bool> setEverything)
        {
            _anchor = anchor; _edge = edge; _everything = everything; _setEverything = setEverything;
            if (everything) _ev = new EverythingClient();
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; Width = 440; SizeToContent = SizeToContent.Height;
            FontFamily = Theme.Font; Left = -32000; Top = -32000;

            _box = new TextBox
            {
                FontSize = 14, Foreground = Brushes.White, Background = Theme.B("#22FFFFFF"), BorderThickness = new Thickness(0),
                CaretBrush = Brushes.White, Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 0, 6), VerticalContentAlignment = VerticalAlignment.Center,
            };
            _status = new TextBlock { Foreground = Theme.B("#9A9AA0"), FontSize = 11, Margin = new Thickness(4, 0, 4, 4) };
            _list = new ListBox
            {
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Brushes.White, MaxHeight = 400,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
            _list.MouseLeftButtonUp += (_, e) => { if (_list.SelectedItem is ListBoxItem { Tag: QuickHit h }) Open(h, Keyboard.Modifiers.HasFlag(ModifierKeys.Control)); };

            // 1.6.11: kutunun altında küçük Everything anahtarı (Ayarlar'a gitmeden)
            var evRow = new DockPanel { Margin = new Thickness(4, 0, 4, 4) };
            var evSwitch = Ui.MiniSwitch(everything, on => SetEverything(on));
            evSwitch.ToolTip = "Açıkken dosyalar Everything ile aranır (çalışmıyorsa arka planda başlatılır); kapalıyken Windows araması";
            DockPanel.SetDock(evSwitch, Dock.Right); evRow.Children.Add(evSwitch);
            evRow.Children.Add(new TextBlock { Text = "Everything", Foreground = Theme.B("#C8C8CE"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 8, 0) });

            var stack = new StackPanel();
            stack.Children.Add(_box); stack.Children.Add(evRow); stack.Children.Add(_status); stack.Children.Add(_list);
            Content = new Border
            {
                Background = Theme.B("#F21C1C1F"), BorderBrush = Theme.B("#33FFFFFF"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12), Padding = new Thickness(8), Child = stack,
            };

            _box.TextChanged += (_, _) => { _retry = 0; _typing.Stop(); _typing.Start(); };
            _typing.Tick += (_, _) => { _typing.Stop(); Search(); };
            PreviewKeyDown += Keys;
            Deactivated += (_, _) => { if (!_closing) Close(); };
            ContentRendered += (_, _) => { Place(); _box.Focus(); Anim.PopIn(this, _edge); };
            SizeChanged += (_, _) => Place();
            Search();
            if (everything && !EverythingClient.Running) StartEverything();
        }

        /// <summary>1.6.11: anahtar. Açılınca Everything (gerekirse) başlatılır; kapanınca Windows aramasına dönülür.</summary>
        void SetEverything(bool on)
        {
            _everything = on;
            _setEverything?.Invoke(on);
            _box.Focus();
            if (on) { _ev ??= new EverythingClient(); if (!EverythingClient.Running) { StartEverything(); return; } }
            else EverythingLauncher.StopIfStartedByDock();
            Search();
        }

        async void StartEverything()
        {
            _status.Text = "Everything arka planda başlatılıyor…";
            bool ok = await EverythingLauncher.EnsureRunning();
            if (_closing || !_everything) return;
            if (!ok) { _status.Text = "Everything bulunamadı ya da başlatılamadı."; return; }
            Search();
        }

        int _retry;

        async void Search()
        {
            var q = _box.Text.Trim();
            int seq = ++_seq;
            if (_everything && q.Length == 0) { _list.Items.Clear(); _status.Text = "Everything ile ara · Enter aç · Ctrl+Enter klasörde göster · Esc kapat"; return; }
            if (q.Length == 0) { Show(WindowsSearch.Recent(8), "Son dosyalar"); return; }
            // 1.6.10: her iki kipte de önce Uygulamalar, sonra Ayarlar/Denetim Masası, en son Dosyalar (yalnızca kullanıcı klasörleri)
            var apps = WindowsSearch.Apps(q, 6).ToList();
            var settings = WindowsSearch.Settings(q, 4).ToList();
            Show(WinSearch.Merge(apps, settings, Array.Empty<QuickHit>()), null);
            string noApp = apps.Count == 0 ? $"\"{q}\" adında yüklü uygulama yok · Enter aç · Esc kapat" : null;
            if (noApp != null) _status.Text = noApp;
            List<QuickHit> files;
            if (_everything)
            {
                var roots = WindowsSearch.UserRoots();
                if (!EverythingClient.Running) { StartEverything(); return; } // 1.6.14: kapanmışsa yeniden arka planda başlat
                var hits = await _ev.Search(WinSearch.EverythingQuery(q, roots), 40);
                if (seq != _seq) return;
                if (hits == null) { if (apps.Count + settings.Count == 0) { _list.Items.Clear(); } _status.Text = "Everything yanıt vermedi."; return; }
                // 1.6.14: veritabanı henüz yükleniyorsa kısa bekleyip aramayı kendiliğinden yenile
                bool loaded = EverythingLauncher.DbLoaded();
                if (EverythingPolicy.ShouldRetry(loaded, hits.Count, _retry))
                {
                    if (noApp == null) _status.Text = "Everything veritabanı yükleniyor…";
                    int delay = EverythingPolicy.RetryDelaysMs[_retry++];
                    await Task.Delay(delay);
                    if (seq == _seq && !_closing) Search();
                    return;
                }
                _retry = 0;
                files = hits.Select(h => new QuickHit(HitKind.File, h.Name, h.Folder, h.FullPath))
                    .Where(h => WinSearch.AllowedFile(h.Target, roots)).Take(8).ToList();
            }
            else files = await WindowsSearch.FilesAsync(q, 8);
            if (seq != _seq || files.Count == 0) return;
            Show(WinSearch.Merge(apps, settings, files), null);
            if (noApp != null) _status.Text = noApp;
        }

        void Show(List<QuickHit> hits, string header)
        {
            var keep = _list.SelectedIndex;
            _list.Items.Clear();
            _status.Text = header ?? (hits.Count == 0 ? "Sonuç yok" : "Enter aç · Ctrl+Enter klasörde göster · Esc kapat");
            HitKind? last = null;
            foreach (var h in hits)
            {
                if (header == null && h.Kind != last)
                {
                    _list.Items.Add(new ListBoxItem { Content = new TextBlock { Text = h.Kind switch { HitKind.App => "Uygulamalar", HitKind.Setting => "Ayarlar", _ => "Dosyalar" }, Foreground = Theme.B("#9A9AA0"), FontSize = 11, Margin = new Thickness(2, 6, 0, 0) }, IsEnabled = false, Focusable = false });
                    last = h.Kind;
                }
                var src = h.Kind == HitKind.Setting ? null : ShellIcons.Get(h.Target, 32);
                var icon = new Image { Width = 20, Height = 20, Margin = new Thickness(0, 0, 10, 0), Source = src ?? ShellIcons.Generic, VerticalAlignment = VerticalAlignment.Center };
                var text = new StackPanel();
                text.Children.Add(new TextBlock { Text = h.Name, Foreground = Brushes.White, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis });
                if (h.Detail.Length > 0) text.Children.Add(new TextBlock { Text = h.Detail, Foreground = Theme.B("#9A9AA0"), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
                var row = new DockPanel { Margin = new Thickness(2, 3, 2, 3) };
                DockPanel.SetDock(icon, Dock.Left); row.Children.Add(icon); row.Children.Add(text);
                _list.Items.Add(new ListBoxItem { Content = row, Tag = h, ToolTip = h.Target, Cursor = Cursors.Hand });
            }
            Select(keep >= 0 && keep < _list.Items.Count ? keep : 0, 1);
        }

        void Select(int i, int dir)
        {
            while (i >= 0 && i < _list.Items.Count && _list.Items[i] is ListBoxItem { IsEnabled: false }) i += dir;
            if (i >= 0 && i < _list.Items.Count) { _list.SelectedIndex = i; _list.ScrollIntoView(_list.SelectedItem); }
        }

        void Keys(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape: Close(); e.Handled = true; break;
                case Key.Down: Select(_list.SelectedIndex + 1, 1); e.Handled = true; break;
                case Key.Up: Select(_list.SelectedIndex - 1, -1); e.Handled = true; break;
                case Key.Enter:
                    if (_list.SelectedItem is ListBoxItem { Tag: QuickHit h }) Open(h, Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
                    e.Handled = true; break;
            }
        }

        void Open(QuickHit h, bool inFolder)
        {
            try
            {
                var psi = inFolder && h.Kind == HitKind.File
                    ? new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{h.Target}\"")
                    : new System.Diagnostics.ProcessStartInfo(h.Target);
                psi.UseShellExecute = true;
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception) { }
            Close();
        }

        void Place()
        {
            if (!_anchor.IsVisible) return;
            var a = _anchor.PointToScreen(new Point(0, 0));
            var b = _anchor.PointToScreen(new Point(_anchor.ActualWidth, _anchor.ActualHeight));
            var dpi = VisualTreeHelper.GetDpi(this);
            var wa = Native.WorkAreaDipAt((a.X + b.X) / 2, (a.Y + b.Y) / 2, dpi.DpiScaleX, dpi.DpiScaleY); // 1.6.8: dock'un ekranı
            (Left, Top) = DockLayout.Popup(_edge, a.X / dpi.DpiScaleX, a.Y / dpi.DpiScaleY, (b.X - a.X) / dpi.DpiScaleX, (b.Y - a.Y) / dpi.DpiScaleY,
                ActualWidth, ActualHeight, 10, wa.X, wa.Y, wa.Width, wa.Height);
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e) { _closing = true; base.OnClosing(e); }

        protected override void OnClosed(EventArgs e)
        {
            _typing.Stop(); _ev?.Dispose();
            if (_current == this) _current = null;
            base.OnClosed(e);
        }
    }

    /// <summary>
    /// Everything'i arka planda (-startup: pencere açmadan) başlatır ve veritabanı yüklenene kadar bekler. 1.6.14: dock'un
    /// başlattığı süreç everything-dock.txt'ye yazılır; dock yeniden başlasa da anahtar kapatılınca yalnızca o süreç
    /// "-exit" ile kapanır. Kutay'ın kendi açtığı ya da penceresi açık Everything'e dokunulmaz.
    /// </summary>
    internal static class EverythingLauncher
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string name);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr h, int msg, IntPtr w, IntPtr l, int flags, int timeout, out IntPtr result);
        static readonly SemaphoreSlim _gate = new(1, 1);

        static string Exe => EverythingIpc.FindExe(System.IO.File.Exists,
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        static string OwnerPath => System.IO.Path.Combine(Services.DataDir, EverythingPolicy.OwnerFile);

        /// <summary>Everything'in veritabanı yüklü mü (EVERYTHING_IPC_IS_DB_LOADED). Everything yoksa ya da yanıt vermezse false.</summary>
        public static bool DbLoaded()
        {
            var h = FindWindow(EverythingIpc.WindowClass, null);
            if (h == IntPtr.Zero) return false;
            return SendMessageTimeout(h, EverythingPolicy.WmUser, new IntPtr(EverythingPolicy.IpcIsDbLoaded), IntPtr.Zero, 0x2 /* SMTO_ABORTIFHUNG */, 1000, out var r) != IntPtr.Zero
                && r != IntPtr.Zero;
        }

        /// <summary>Everything'in arama penceresi açık mı (Kutay kullanıyor).</summary>
        public static bool MainWindowVisible()
        {
            var h = FindWindow("EVERYTHING", null);
            return h != IntPtr.Zero && IsWindowVisible(h);
        }

        /// <summary>Çalışmıyorsa penceresiz başlatır; IPC penceresi ve (en çok 20 sn) veritabanı için bekler.</summary>
        public static async Task<bool> EnsureRunning(bool waitForDb = true)
        {
            await _gate.WaitAsync();
            try
            {
                if (!EverythingClient.Running)
                {
                    var exe = Exe;
                    if (exe == null) { Trace("bulunamadı (Program Files\\Everything\\Everything.exe yok)"); return false; }
                    try
                    {
                        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, EverythingIpc.StartArgs) { UseShellExecute = false, CreateNoWindow = true });
                        if (p != null)
                        {
                            System.IO.Directory.CreateDirectory(Services.DataDir);
                            System.IO.File.WriteAllText(OwnerPath, new OwnedEverything(p.Id, p.StartTime.ToUniversalTime()).Serialize());
                            Trace($"arka planda başlatıldı (pid {p.Id})");
                        }
                    }
                    catch (Exception ex) { Trace("başlatılamadı: " + ex.Message); return false; }
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    while (!EverythingClient.Running && sw.Elapsed < EverythingPolicy.WindowWait) await Task.Delay(100);
                    if (!EverythingClient.Running) { Trace("başlatıldı ama IPC penceresi gelmedi"); return false; }
                }
                if (waitForDb)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    while (!DbLoaded() && sw.Elapsed < EverythingPolicy.DbWait) await Task.Delay(200);
                    if (!DbLoaded()) Trace("veritabanı 20 sn'de yüklenmedi; arama yine de denenecek");
                }
                return true;
            }
            finally { _gate.Release(); }
        }

        /// <summary>Anahtar kapatılınca: yalnızca dock'un başlattığı ve penceresi kapalı Everything "-exit" ile kapanır.</summary>
        public static void StopIfStartedByDock()
        {
            OwnedEverything owned = null;
            try { if (System.IO.File.Exists(OwnerPath)) owned = OwnedEverything.Parse(System.IO.File.ReadAllText(OwnerPath)); } catch (Exception) { }
            if (owned == null) { Trace("kapatılmadı: dock başlatmamış (Kutay'ın kendi açtığı ya da çalışmıyor)"); return; }
            int? pid = null; DateTime? start = null;
            try
            {
                using var p = System.Diagnostics.Process.GetProcessById(owned.Pid);
                if (p.ProcessName.Equals("Everything", StringComparison.OrdinalIgnoreCase)) { pid = p.Id; start = p.StartTime.ToUniversalTime(); }
            }
            catch (Exception) { }
            bool window = MainWindowVisible();
            if (!EverythingPolicy.ShouldExit(owned, pid, start, window))
            {
                Trace(window && pid != null ? "kapatılmadı: Everything penceresi açık (kullanılıyor)" : "kapatılmadı: dock'un başlattığı süreç artık çalışmıyor");
                if (!(window && pid != null)) TryDelete();
                return;
            }
            var exe = Exe;
            if (exe == null) return;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, EverythingIpc.ExitArgs) { UseShellExecute = false, CreateNoWindow = true })?.Dispose();
                Trace($"anahtar kapatıldı: dock'un başlattığı Everything kapatıldı (pid {owned.Pid})");
            }
            catch (Exception ex) { Trace("kapatılamadı: " + ex.Message); }
            TryDelete();
        }

        static void TryDelete() { try { System.IO.File.Delete(OwnerPath); } catch (Exception) { } }

        static void Trace(string line)
        {
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(Services.DataDir, "hata.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} Everything: {line}\n"); }
            catch (Exception) { }
        }
    }

    /// <summary>Windows araması kaynakları: Uygulamalar klasörü (shell:AppsFolder), Ayarlar sayfaları, Windows Search dizini, son dosyalar.</summary>
    internal static class WindowsSearch
    {
        static List<(string Name, string Target)> _apps; static DateTime _appsAt;

        /// <summary>Başlat menüsündeki tüm uygulamalar (masaüstü + Store), 2 dakika önbellekli.</summary>
        static List<(string Name, string Target)> AllApps()
        {
            if (_apps != null && DateTime.Now - _appsAt < TimeSpan.FromMinutes(2)) return _apps;
            var list = new List<(string, string)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // 1.6.10: Uygulamalar klasörü (masaüstü + UWP/Mağaza/Office) geç bağlamayla değil açık IDispatch çağrısıyla okunur
            try
            {
                object shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                object folder = Call(shell, "NameSpace", "shell:::{4234d49b-0245-4df3-b780-3893943456e1}");
                object items = folder == null ? null : Call(folder, "Items");
                int count = items == null ? 0 : Convert.ToInt32(Get(items, "Count"));
                for (int i = 0; i < count; i++)
                {
                    var item = Call(items, "Item", i);
                    if (item == null) continue;
                    var name = Get(item, "Name") as string; var path = Get(item, "Path") as string;
                    if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(path) && seen.Add(name)) list.Add((name, @"shell:AppsFolder\" + path));
                }
            }
            catch (Exception) { }
            // Başlat menüsü kısayolları (kullanıcı ve ortak): Uygulamalar klasörü okunamazsa da uygulamalar bulunur
            foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), Environment.GetFolderPath(Environment.SpecialFolder.StartMenu) })
            {
                try
                {
                    if (string.IsNullOrEmpty(root) || !System.IO.Directory.Exists(root)) continue;
                    foreach (var lnk in System.IO.Directory.EnumerateFiles(root, "*.lnk", System.IO.SearchOption.AllDirectories))
                    {
                        var name = System.IO.Path.GetFileNameWithoutExtension(lnk);
                        if (name.Contains("uninstall", StringComparison.OrdinalIgnoreCase) || name.Contains("kaldır", StringComparison.OrdinalIgnoreCase)) continue;
                        if (seen.Add(name)) list.Add((name, lnk));
                    }
                }
                catch (Exception) { }
            }
            _appsAt = DateTime.Now;
            return _apps = list;
        }

        public static IEnumerable<QuickHit> Apps(string q, int take) =>
            WinSearch.Rank(AllApps(), a => a.Name, q, take).Select(a => new QuickHit(HitKind.App, a.Name, "Uygulama", a.Target));

        static object Call(object o, string name, params object[] args) => o.GetType().InvokeMember(name, System.Reflection.BindingFlags.InvokeMethod, null, o, args);
        static object Get(object o, string name) => o.GetType().InvokeMember(name, System.Reflection.BindingFlags.GetProperty, null, o, null);

        public static IEnumerable<QuickHit> Settings(string q, int take) =>
            WinSearch.Rank(WinSearch.SettingsPages.Select(p => (p.Name, Detail: "Ayarlar", Target: p.Uri))
                    .Concat(WinSearch.ControlPanelItems.Select(c => (c.Name, Detail: "Denetim Masası", c.Target))), p => p.Name, q, take)
                .Select(p => new QuickHit(HitKind.Setting, p.Name, p.Detail, p.Target));

        [DllImport("shell32.dll")] static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

        /// <summary>1.6.10: dosya aramasının sınırı: Masaüstü, Belgeler, İndirilenler, Resimler.</summary>
        public static List<string> UserRoots()
        {
            string downloads = null;
            try
            {
                if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out var p) == 0)
                { downloads = Marshal.PtrToStringUni(p); Marshal.FreeCoTaskMem(p); }
            }
            catch (Exception) { }
            downloads ??= System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            return new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), downloads, Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) }
                .Where(r => !string.IsNullOrEmpty(r)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Windows Search dizininden dosya adları (OLE DB, Search.CollatorDSO). Dizin hizmeti yoksa boş.</summary>
        public static Task<List<QuickHit>> FilesAsync(string q, int take) => Task.Run(() =>
        {
            var list = new List<QuickHit>();
            try
            {
                using var c = new System.Data.OleDb.OleDbConnection("Provider=Search.CollatorDSO;Extended Properties='Application=Windows';");
                c.Open();
                var roots = UserRoots();
                using var cmd = new System.Data.OleDb.OleDbCommand(WinSearch.IndexSql(q, take * 4, roots), c);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var path = r.IsDBNull(0) ? null : r.GetValue(0) as string;
                    if (string.IsNullOrEmpty(path) || !WinSearch.AllowedFile(path, roots)) continue;
                    if (list.Count >= take) break;
                    list.Add(new QuickHit(HitKind.File, System.IO.Path.GetFileName(path), System.IO.Path.GetDirectoryName(path) ?? "", path));
                }
            }
            catch (Exception) { }
            return list;
        });

        /// <summary>Son kullanılan dosyalar (Windows'un "Son öğeler" kısayolları).</summary>
        public static List<QuickHit> Recent(int take)
        {
            var list = new List<QuickHit>();
            try
            {
                var dir = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
                foreach (var lnk in new System.IO.DirectoryInfo(dir).GetFiles("*.lnk").OrderByDescending(f => f.LastWriteTime))
                {
                    var t = ShellIcons.LinkTarget(lnk.FullName);
                    if (t == null || !System.IO.File.Exists(t)) continue;
                    list.Add(new QuickHit(HitKind.File, System.IO.Path.GetFileName(t), System.IO.Path.GetDirectoryName(t) ?? "", t));
                    if (list.Count >= take) break;
                }
            }
            catch (Exception) { }
            return list;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Threading;
using KutiDock.Core;
using KutiDock.Mac.Core;

namespace KutiDock
{
    /// <summary>
    /// 1.6.6: dock'tan çalışmıyorken başlatılan uygulamanın (Chrome PWA, normal uygulama, dosya, klasör) ilk penceresi
    /// Genie ile simgeden büyüyerek açılır. EVENT_OBJECT_SHOW ile beklenen pencere (AUMID/exe anahtarı) yakalanır, Windows'un
    /// açılış animasyonu kapatılır ve pencere ilk dolu karesi çizilene kadar DWM'den gizlenir (cloak; siyah/boş kare görünmez);
    /// sonra o kareyle Genie oynar ve son karede pencere görünür olur. 5 sn'de pencere yoksa, kare 2 sn'de dolmazsa,
    /// tam ekranda, oyunda ya da UAC'de normal açılışa düşer.
    /// </summary>
    internal static class LaunchGenie
    {
        delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);
        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
        const uint EVENT_OBJECT_SHOW = 0x8002, WINEVENT_OUTOFCONTEXT = 0, WINEVENT_SKIPOWNPROCESS = 2;
        const long WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80, WS_CAPTION = 0xC00000, WS_THICKFRAME = 0x40000;
        const int DWMWA_TRANSITIONS_FORCEDISABLED = 3;
        const int DWMWA_CLOAK = 13; // 1.6.19: DWM bileşiminde pencereyi gizler (yalnızca kendi sürecinin penceresinde çalışır)
        const uint LWA_ALPHA = 2;

        sealed class Pending
        {
            public string Key;
            public Func<(KutiDock.Mac.Interop.RECT Icon, KutiDock.Mac.Core.DockEdge Edge, IntPtr Dock)?> Target;
            public DateTime Until;
        }

        /// <summary>AppsTile verir: pencere anahtarını dock simgesinin anahtarına çevirir (anahtar, başlık, Chromium mu).</summary>
        public static Func<string, string, bool, string> ResolveKey = (k, _, _) => k;
        /// <summary>AppsTile verir: uygulamanın (bu pencere dışında) zaten görünür penceresi var mı.</summary>
        public static Func<string, IntPtr, bool> HasOpenWindows = (_, _) => true;
        /// <summary>AppsTile verir: anahtarın simge hedefi (çağrıldığı anda hesaplanır; simge yoksa şerit yenilenir).</summary>
        public static Func<string, Func<(KutiDock.Mac.Interop.RECT, KutiDock.Mac.Core.DockEdge, IntPtr)?>> IconFor = _ => () => null;
        public static Func<bool> Enabled = () => false;
        /// <summary>1.6.10: saklı dock'u Genie süresince kısa gösterir (tam ekran/oyunda göstermez); gösterildiyse true.</summary>
        public static Func<bool> Peek = () => false;
        static DateTime _armed = DateTime.MaxValue;

        static readonly WinEventProc Proc = OnShow; // yerel kod çağırır: canlı tutulmalı
        static IntPtr _hook;
        static readonly List<Pending> Waiting = new();
        static readonly Dictionary<IntPtr, LaunchGeniePolicy.HideHow> Hidden = new(); // 1.6.21: nasıl gizlendiği (geri açarken)
        static readonly HashSet<IntPtr> Done = new(); // bir pencere için en fazla bir kez
        static readonly List<LaunchGeniePolicy.LaunchPlay> Played = new(); // 1.6.19: süreç/uygulama başına bir kez
        static DispatcherTimer _expire;

        /// <summary>Dock simgesinden başlatma: anahtarı (AppGrouping) beklenen ilk pencere Genie ile açılır.</summary>
        /// <summary>
        /// 1.6.7: kanca sürekli açık. Önceden yalnızca dock'tan başlatma 5 sn izleniyordu; dock dışından (Başlat, kısayol)
        /// açılan ya da 5 sn'den geç pencere açan uygulamalar (Electron: Grok Bot) Genie'siz açılıyordu.
        /// Dock açıldıktan sonraki ilk 6 sn'de (zaten açık pencereler) bir şey yapılmaz.
        /// </summary>
        public static void Start()
        {
            if (_hook != IntPtr.Zero) return;
            _armed = DateTime.UtcNow.AddSeconds(6);
            _hook = SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, IntPtr.Zero, Proc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
            _expire = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _expire.Tick += (_, _) => Prune();
            AppDomain.CurrentDomain.ProcessExit += (_, _) => RevealAll();
            Trace("başladı (sistem geneli)");
        }

        /// <summary>hata.log'a ilk açılış kararı (yalnızca uygulama adı ve karar; başlık, yol ya da kimlik yazılmaz).</summary>
        static void Trace(string line)
        {
            try { KutiDock.Core.RotatingLog.Append(Path.Combine(Services.DataDir, "hata.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} İlk açılış Genie: {line}\n"); }
            catch (Exception) { }
        }

        public static void Expect(string key, Func<(KutiDock.Mac.Interop.RECT, KutiDock.Mac.Core.DockEdge, IntPtr)?> target)
        {
            try
            {
                var t = target();
                if (t == null) { Trace("atlandı: dock simgesi yok ya da dock üstte"); return; }
                if (!LaunchGeniePolicy.Watch(true, Native.ForegroundIsFullscreen(t.Value.Item3))) { Trace("atlandı: tam ekran"); return; }
                Waiting.RemoveAll(p => p.Key == key);
                Waiting.Add(new Pending { Key = key ?? "", Target = target, Until = DateTime.UtcNow.AddMilliseconds(LaunchGeniePolicy.WindowTimeoutMs) });
                if (_hook == IntPtr.Zero) Start();
                _expire.Start();
            }
            catch (Exception) { }
        }

        static void Prune()
        {
            foreach (var p in Waiting.FindAll(p => DateTime.UtcNow > p.Until))
                Trace($"zaman aşımı: dock'tan başlatılan uygulamanın penceresi {LaunchGeniePolicy.WindowTimeoutMs / 1000} sn'de gelmedi; normal açılış");
            Waiting.RemoveAll(p => DateTime.UtcNow > p.Until); // dosya/klasör beklemesi: normal açılış
            if (Waiting.Count == 0) _expire?.Stop();
        }

        static void OnShow(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time)
        {
            if (obj != 0 || child != 0 || hwnd == IntPtr.Zero) return;
            try
            {
                if (GetAncestor(hwnd, 2 /*GA_ROOT*/) != hwnd) return;
                long style = Native.GetWindowLongPtr(hwnd, Native.GWL_STYLE).ToInt64();
                long ex = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
                bool toolOrOwned = (ex & WS_EX_TOOLWINDOW) != 0 || Native.GetWindow(hwnd, Native.GW_OWNER) != IntPtr.Zero;
                bool frame = (style & WS_CAPTION) == WS_CAPTION || (style & WS_THICKFRAME) != 0;
                Native.GetWindowRect(hwnd, out var r);
                // Araç ipuçları, açılır menüler, sahipli iletişim kutuları, çerçevesiz/küçük pencereler: sessizce geç
                if (!LaunchGeniePolicy.MainWindowShape(toolOrOwned, frame, r.Right - r.Left, r.Bottom - r.Top, Native.ClassName(hwnd))) return;
                if ((style & 0x20000000) != 0) return; // küçültülmüş gösterildi (WS_MINIMIZE)
                if (Hidden.ContainsKey(hwnd) || Done.Contains(hwnd)) return;
                bool fromDock = Waiting.Count > 0;
                if (!fromDock && (DateTime.UtcNow < _armed || !Enabled())) return;

                Native.GetWindowThreadProcessId(hwnd, out uint pid);
                var path = Native.ProcessPath(pid) ?? "";
                var exe = Path.GetFileNameWithoutExtension(path);
                bool game = path.Length > 0 && OptimizeRules.IsGame(exe, path);
                var cls = Native.ClassName(hwnd);
                if (!LaunchGeniePolicy.Candidate(pid == (uint)Environment.ProcessId, toolOrOwned, frame, r.Right - r.Left, r.Bottom - r.Top, cls, exe, game))
                {
                    if (game || LaunchGeniePolicy.IsUac(cls, exe)) Trace($"atlandı ({exe}): {(game ? "oyun" : "UAC")}");
                    return;
                }
                if ((ex & WS_EX_LAYERED) != 0) { Trace($"atlandı ({exe}): pencere kendi saydamlığını yönetiyor"); return; }
                bool frameHost = exe.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase);
                bool packaged = frameHost || path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase);
                var key = AppGrouping.WindowKey(path, Native.AppUserModelId(hwnd), packaged);
                key = ResolveKey(key, Native.WindowTitle(hwnd), cls.StartsWith("Chrome_WidgetWin", StringComparison.Ordinal));

                var p = Waiting.Find(w => LaunchGeniePolicy.Matches(w.Key, key));
                bool viaDock = p != null;
                if (p != null) { Waiting.Remove(p); Prune(); }
                else
                {
                    if (DateTime.UtcNow < _armed || !Enabled()) return;
                    if (HasOpenWindows(key, hwnd)) return; // uygulamanın ikinci penceresi: ilk açılış değil
                    p = new Pending { Key = key, Target = IconFor(key), Until = DateTime.UtcNow };
                }
                if (Done.Count > 500) Done.Clear();
                Done.Add(hwnd);
                var dock = p.Target()?.Item3 ?? IntPtr.Zero;
                if (Native.ForegroundIsFullscreen(dock)) { Trace($"atlandı ({exe}): tam ekran"); return; }
                var now = DateTime.UtcNow;
                Played.RemoveAll(x => now - x.AtUtc > TimeSpan.FromMilliseconds(LaunchGeniePolicy.PerProcessMs));
                if (LaunchGeniePolicy.AlreadyPlayed(Played, key, (int)pid, now))
                { Trace($"atlandı ({exe}): aynı süreçte veya birkaç saniye içinde zaten oynatıldı"); return; }
                Played.Add(new LaunchGeniePolicy.LaunchPlay(key, (int)pid, now));
                Trace($"bulundu ({exe}): {(viaDock ? "dock'tan başlatıldı" : "sistem geneli ilk pencere")}");
                if (Peek()) Trace($"dock kısa süre gösterildi ({exe})");
                Play(hwnd, p, exe);
            }
            catch (Exception) { }
        }

        static void Play(IntPtr hwnd, Pending p, string exe)
        {
            // Windows'un açılış animasyonu kapatılır, pencere Genie bitene kadar gizlenir.
            // 1.6.21: DWMWA_CLOAK başka sürecin penceresinde E_ACCESSDENIED döner (Kasa'da ölçüldü); 1.6.19'dan beri
            // sonuç denetlenmediği için asıl pencere hiç gizlenmiyor, Genie kopyası üstünde oynuyordu (çift görüntü).
            // Artık cloak olmazsa saydamlık (WS_EX_LAYERED + alpha 0) denenir ve tuttuğu doğrulanır; WPF gibi stili geri
            // alan pencerelerde ikisi de tutmaz: Genie hiç oynamaz, pencere normal açılır (tek görüntü).
            int on = 1; DwmSetWindowAttribute(hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, ref on, 4);
            var how = HideWindow(hwnd);
            if (how == LaunchGeniePolicy.HideHow.None)
            {
                int off = 0; DwmSetWindowAttribute(hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, ref off, 4);
                Trace($"atlandı ({exe}): pencere başka süreçten gizlenemiyor; çift görüntü olmasın diye normal açılış");
                return;
            }
            Hidden[hwnd] = how;
            var ui = Dispatcher.CurrentDispatcher;
            var until = DateTime.UtcNow.AddMilliseconds(LaunchGeniePolicy.PaintTimeoutMs);
            // Güvenlik: ne olursa olsun pencere en geç 3 sn'de görünür olur
            var guard = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
            guard.Tick += (_, _) => { guard.Stop(); Reveal(hwnd); };
            guard.Start();

            uint[] buffer = null; // 1.6.22: 70 ms'de bir denemede her seferinde yeni tam boy dizi ayrılmasın
            void Poll()
            {
                Task.Run(() =>
                {
                    var c = IsWindow(hwnd) ? GenieCapture.From(hwnd, ref buffer) : null;
                    return c != null && FrameCheck.HasDetail(c.Pixels, c.Width, c.Height) ? c : null;
                }).ContinueWith(t => ui.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (!IsWindow(hwnd)) { Hidden.Remove(hwnd); guard.Stop(); return; }
                        var c = t.Status == TaskStatus.RanToCompletion ? t.Result : null;
                        if (c == null)
                        {
                            if (DateTime.UtcNow < until) { Delay(Poll); return; }
                            Trace($"atlandı ({exe}): {LaunchGeniePolicy.PaintTimeoutMs / 1000.0:0.#} sn'de dolu kare çizilmedi; normal gösterildi");
                            guard.Stop(); Reveal(hwnd); return;
                        }
                        Peek(); // 1.6.10: dock Genie bitene kadar açık kalsın
                        var target = p.Target();
                        bool played = target != null && !Native.IsIconic(hwnd)
                            && ClassicGenie.PlayLaunch(hwnd, c, target.Value.Icon, target.Value.Edge, target.Value.Dock, () => { guard.Stop(); Reveal(hwnd); });
                        if (!played) { guard.Stop(); Reveal(hwnd); Trace($"atlandı ({exe}): {(target == null ? "simge hedefi yok" : "Genie oynatılamadı (başka animasyon ya da ekran kartı)")}"); }
                        else Trace($"oynatıldı ({exe})");
                    }
                    catch (Exception) { guard.Stop(); Reveal(hwnd); }
                })));
            }

            void Delay(Action a)
            {
                var d = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(LaunchGeniePolicy.PollMs) };
                d.Tick += (_, _) => { d.Stop(); a(); };
                d.Start();
            }

            Delay(Poll);
        }

        /// <summary>1.6.21: önce cloak, olmazsa alpha 0; hangisi gerçekten tuttuysa o (hiçbiri değilse None, pencereye dokunulmamış olur).</summary>
        static LaunchGeniePolicy.HideHow HideWindow(IntPtr hwnd)
        {
            int cloak = 1;
            int hr = DwmSetWindowAttribute(hwnd, DWMWA_CLOAK, ref cloak, 4);
            long ex = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
            bool layered = false, alpha = false;
            if (hr != 0)
            {
                Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(ex | WS_EX_LAYERED));
                layered = (Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64() & WS_EX_LAYERED) != 0;
                alpha = layered && SetLayeredWindowAttributes(hwnd, 0, 0, LWA_ALPHA);
                if (layered && !alpha) Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));
            }
            return LaunchGeniePolicy.Hide(hr, layered, alpha);
        }

        /// <summary>Pencere eski hâline döner: gizleme (cloak ya da eklenen saydamlık) kaldırılır, Windows animasyonu geri açılır.</summary>
        static void Reveal(IntPtr hwnd)
        {
            if (!Hidden.Remove(hwnd, out var how) || !IsWindow(hwnd)) return;
            try
            {
                if (how == LaunchGeniePolicy.HideHow.Cloak) { int cloak = 0; DwmSetWindowAttribute(hwnd, DWMWA_CLOAK, ref cloak, 4); }
                else
                {
                    SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA);
                    long ex = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
                    Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(ex & ~WS_EX_LAYERED));
                }
                int off = 0; DwmSetWindowAttribute(hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, ref off, 4);
                // Pencere önde kalırsa, kendi küçültme düğmesinin Genie'si (1.6.4) için animasyon yine bu pencerede kapalı tutulur
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                t.Tick += (_, _) => { t.Stop(); if (Native.GetForegroundWindow() == hwnd && MinimizeGenie.Enabled()) ClassicGenie.Hold(hwnd); };
                t.Start();
            }
            catch (Exception) { }
        }

        public static void RevealAll()
        {
            foreach (var h in new List<IntPtr>(Hidden.Keys)) Reveal(h);
        }
    }
}

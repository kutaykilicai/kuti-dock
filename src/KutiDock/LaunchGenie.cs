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
    /// açılış animasyonu kapatılır ve pencere ilk dolu karesi çizilene kadar saydam tutulur (siyah/boş kare görünmez);
    /// sonra o kareyle Genie oynar ve son karede pencere görünür olur. 5 sn'de pencere yoksa, kare 1,5 sn'de dolmazsa,
    /// tam ekranda, oyunda ya da UAC'de normal açılışa düşer.
    /// </summary>
    internal static class LaunchGenie
    {
        delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);
        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
        [DllImport("user32.dll")] static extern bool GetLayeredWindowAttributes(IntPtr h, out uint key, out byte alpha, out uint flags);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
        const uint EVENT_OBJECT_SHOW = 0x8002, WINEVENT_OUTOFCONTEXT = 0, WINEVENT_SKIPOWNPROCESS = 2;
        const long WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80, WS_CAPTION = 0xC00000, WS_THICKFRAME = 0x40000;
        const uint LWA_ALPHA = 2;
        const int DWMWA_TRANSITIONS_FORCEDISABLED = 3;

        sealed class Pending
        {
            public string Key;
            public Func<(KutiDock.Mac.Interop.RECT Icon, KutiDock.Mac.Core.DockEdge Edge, IntPtr Dock)?> Target;
            public DateTime Until;
        }

        static readonly WinEventProc Proc = OnShow; // yerel kod çağırır: canlı tutulmalı
        static IntPtr _hook;
        static readonly List<Pending> Waiting = new();
        static readonly HashSet<IntPtr> Hidden = new();
        static DispatcherTimer _expire;

        /// <summary>Dock simgesinden başlatma: anahtarı (AppGrouping) beklenen ilk pencere Genie ile açılır.</summary>
        public static void Expect(string key, Func<(KutiDock.Mac.Interop.RECT, KutiDock.Mac.Core.DockEdge, IntPtr)?> target)
        {
            try
            {
                var t = target();
                if (t == null || !LaunchGeniePolicy.Watch(true, Native.ForegroundIsFullscreen(t.Value.Item3))) return;
                Waiting.RemoveAll(p => p.Key == key);
                Waiting.Add(new Pending { Key = key ?? "", Target = target, Until = DateTime.UtcNow.AddMilliseconds(LaunchGeniePolicy.WindowTimeoutMs) });
                if (_hook == IntPtr.Zero)
                    _hook = SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, IntPtr.Zero, Proc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
                if (_expire == null)
                {
                    _expire = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                    _expire.Tick += (_, _) => Prune();
                    AppDomain.CurrentDomain.ProcessExit += (_, _) => RevealAll();
                }
                _expire.Start();
            }
            catch (Exception) { }
        }

        static void Prune()
        {
            Waiting.RemoveAll(p => DateTime.UtcNow > p.Until); // 5 sn: normal açılış
            if (Waiting.Count > 0) return;
            _expire?.Stop();
            if (_hook != IntPtr.Zero) { UnhookWinEvent(_hook); _hook = IntPtr.Zero; }
        }

        static void OnShow(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time)
        {
            if (obj != 0 || child != 0 || hwnd == IntPtr.Zero || Waiting.Count == 0) return;
            try
            {
                if (GetAncestor(hwnd, 2 /*GA_ROOT*/) != hwnd) return;
                long style = Native.GetWindowLongPtr(hwnd, Native.GWL_STYLE).ToInt64();
                long ex = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
                if ((ex & WS_EX_LAYERED) != 0) return; // kendi saydamlığını yöneten pencere: dokunulmaz
                Native.GetWindowThreadProcessId(hwnd, out uint pid);
                var path = Native.ProcessPath(pid) ?? "";
                var exe = Path.GetFileNameWithoutExtension(path);
                Native.GetWindowRect(hwnd, out var r);
                bool toolOrOwned = (ex & WS_EX_TOOLWINDOW) != 0 || Native.GetWindow(hwnd, Native.GW_OWNER) != IntPtr.Zero;
                bool frame = (style & WS_CAPTION) == WS_CAPTION || (style & WS_THICKFRAME) != 0;
                bool game = path.Length > 0 && OptimizeRules.IsGame(exe, path);
                if (!LaunchGeniePolicy.Candidate(pid == (uint)Environment.ProcessId, toolOrOwned, frame, r.Right - r.Left, r.Bottom - r.Top,
                        Native.ClassName(hwnd), exe, game)) return;
                bool frameHost = exe.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase);
                bool packaged = frameHost || path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase);
                var key = AppGrouping.WindowKey(path, Native.AppUserModelId(hwnd), packaged);
                var p = Waiting.Find(w => LaunchGeniePolicy.Matches(w.Key, key));
                if (p == null) return;
                Waiting.Remove(p);
                Prune();
                Play(hwnd, p);
            }
            catch (Exception) { }
        }

        static void Play(IntPtr hwnd, Pending p)
        {
            // Windows'un açılış animasyonu kapatılır, pencere ilk dolu karesine kadar saydam tutulur
            int on = 1; DwmSetWindowAttribute(hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, ref on, 4);
            long ex = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
            Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(ex | WS_EX_LAYERED));
            SetLayeredWindowAttributes(hwnd, 0, 0, LWA_ALPHA);
            Hidden.Add(hwnd);
            var ui = Dispatcher.CurrentDispatcher;
            var until = DateTime.UtcNow.AddMilliseconds(LaunchGeniePolicy.PaintTimeoutMs);
            // Güvenlik: ne olursa olsun pencere en geç 3 sn'de görünür olur
            var guard = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            guard.Tick += (_, _) => { guard.Stop(); Reveal(hwnd); };
            guard.Start();

            void Poll()
            {
                Task.Run(() =>
                {
                    var c = IsWindow(hwnd) ? GenieCapture.From(hwnd) : null;
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
                            Log.Info("Launch genie: no painted frame in time, showing normally");
                            guard.Stop(); Reveal(hwnd); return;
                        }
                        var target = p.Target();
                        bool played = target != null && !Native.IsIconic(hwnd)
                            && ClassicGenie.PlayLaunch(hwnd, c, target.Value.Icon, target.Value.Edge, target.Value.Dock, () => { guard.Stop(); Reveal(hwnd); });
                        if (!played) { guard.Stop(); Reveal(hwnd); }
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

        /// <summary>Pencere eski hâline döner: tam opak, eklenen WS_EX_LAYERED kaldırılır, Windows animasyonu geri açılır.</summary>
        static void Reveal(IntPtr hwnd)
        {
            if (!Hidden.Remove(hwnd) || !IsWindow(hwnd)) return;
            try
            {
                SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA);
                long ex = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
                Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(ex & ~WS_EX_LAYERED));
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
            foreach (var h in new List<IntPtr>(Hidden)) Reveal(h);
        }
    }
}

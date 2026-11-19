using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Threading;
using KutiDock.Core;
using KutiDock.Mac.Core;

namespace KutiDock
{
    /// <summary>
    /// 1.6.4: pencere kendi küçültme düğmesiyle (ya da Win+Aşağı) küçülünce de Genie oynar. Öndeki uygulama penceresinin
    /// görüntüsü ön plan değişince ve 2,5 sn'de bir arka planda alınır; Windows'un küçültme animasyonu yalnızca o pencere için
    /// kapatılır (önden çekilince geri açılır). Tam ekranda ve oyun açıkken hiçbir şey yapılmaz.
    /// </summary>
    internal static class MinimizeGenie
    {
        public delegate (KutiDock.Mac.Interop.RECT Icon, KutiDock.Mac.Core.DockEdge Edge, IntPtr Dock)? TargetFn(IntPtr hwnd);
        /// <summary>Klasik dock'un uygulamalar şeridi verir: pencerenin simgesi (ekran pikseli), kenar, dock penceresi.</summary>
        public static TargetFn Target;
        public static Func<bool> Enabled = () => false;

        delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);
        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
        const uint EVENT_SYSTEM_FOREGROUND = 0x0003, EVENT_SYSTEM_MINIMIZESTART = 0x0016, EVENT_SYSTEM_MINIMIZEEND = 0x0017;
        const uint WINEVENT_OUTOFCONTEXT = 0, WINEVENT_SKIPOWNPROCESS = 2;

        static readonly WinEventProc Proc = OnEvent; // yerel kod çağırır: canlı tutulmalı
        static IntPtr _fgHook, _minHook;
        static readonly DispatcherTimer Timer = new() { Interval = MinimizeGeniePolicy.Refresh };
        static IntPtr _front;
        static bool _capturing;

        public static void Start()
        {
            if (_fgHook != IntPtr.Zero) return;
            _fgHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, Proc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
            _minHook = SetWinEventHook(EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZEEND, IntPtr.Zero, Proc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
            Timer.Tick += (_, _) => Snapshot();
            Front(Native.GetForegroundWindow());
        }

        static void OnEvent(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time)
        {
            if (obj != 0 || child != 0) return;
            try
            {
                switch (ev)
                {
                    case EVENT_SYSTEM_FOREGROUND: Front(hwnd); break;
                    case EVENT_SYSTEM_MINIMIZESTART:
                        if (Enabled() && Target?.Invoke(hwnd) is { } t) ClassicGenie.PlayMinimizing(hwnd, t.Icon, t.Edge, t.Dock);
                        break;
                    case EVENT_SYSTEM_MINIMIZEEND:
                        if (hwnd != _front) ClassicGenie.Release(hwnd); // başka yoldan (Alt+Tab) geri geldi
                        break;
                }
            }
            catch (Exception) { }
        }

        static void Front(IntPtr hwnd)
        {
            if (hwnd == _front) return;
            var prev = _front; _front = IntPtr.Zero; Timer.Stop();
            if (prev != IntPtr.Zero) ClassicGenie.Release(prev);
            if (!Enabled() || Target == null) return;
            if (!MinimizeGeniePolicy.Track(true, OwnWindow(hwnd), Native.IsAppWindow(hwnd), Fullscreen(), GameWindow(hwnd))) return;
            if (Target(hwnd) == null) return;
            _front = hwnd;
            ClassicGenie.Hold(hwnd);
            Timer.Interval = MinimizeGeniePolicy.FirstCapture;
            Timer.Start();
        }

        /// <summary>Öndeki pencerenin görüntüsü arka planda (aynı anda tek iş).</summary>
        static void Snapshot()
        {
            Timer.Interval = MinimizeGeniePolicy.Refresh;
            var h = _front;
            if (h == IntPtr.Zero || _capturing || Native.GetForegroundWindow() != h || Native.IsIconic(h)) return;
            if (Fullscreen()) { ClassicGenie.Release(h); _front = IntPtr.Zero; Timer.Stop(); return; } // tam ekrana geçti
            _capturing = true;
            var ui = Dispatcher.CurrentDispatcher;
            var buffer = ClassicGenie.TakeSpare(); // 1.6.22: yeni dizi yerine eskiyen görüntünün dizisi
            Task.Run(() => ClassicGenie.Capture(h, ref buffer)).ContinueWith(t =>
            {
                ui.BeginInvoke(new Action(() =>
                {
                    _capturing = false;
                    if (t.Status == TaskStatus.RanToCompletion && t.Result is { } c && h == _front) ClassicGenie.SetFresh(h, c);
                    else ClassicGenie.ReturnSpare(buffer);
                }));
            });
        }

        /// <summary>Pencere bir oyunun mu (Steam/Epic/Riot/Marvel Rivals…): yalnızca bu pencerenin süreci denetlenir, ucuz.</summary>
        static bool GameWindow(IntPtr h)
        {
            Native.GetWindowThreadProcessId(h, out uint pid);
            var path = Native.ProcessPath(pid);
            return path != null && OptimizeRules.IsGame(System.IO.Path.GetFileNameWithoutExtension(path), path);
        }

        static bool OwnWindow(IntPtr h)
        {
            Native.GetWindowThreadProcessId(h, out uint pid);
            return pid == (uint)Environment.ProcessId;
        }

        // Ön pencere herhangi bir monitörü tam kaplıyor mu (tam ekran video/oyun): KutiDock'un kendi kuralı, dock monitöründen bağımsız
        static bool Fullscreen()
        {
            var fg = Native.GetForegroundWindow();
            return fg != IntPtr.Zero && Native.ForegroundIsFullscreen(Target?.Invoke(fg)?.Dock ?? IntPtr.Zero);
        }
    }
}

using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace KutiDock
{
    /// <summary>
    /// Kabuk "AppBar" kaydı: ekranın kenarında dock için yer ayırır; büyütülen pencereler dock'un altına girmez.
    /// Ayrıca Windows görev çubuğunu (isteğe bağlı) otomatik gizler ve çıkışta eski hâline döndürür.
    /// </summary>
    internal sealed class AppBar
    {
        readonly IntPtr _hwnd; bool _registered; Native.RECT _rc;
        public AppBar(IntPtr hwnd) { _hwnd = hwnd; }

        Native.APPBARDATA Data() => new() { cbSize = Marshal.SizeOf<Native.APPBARDATA>(), hWnd = _hwnd };

        public void Reserve(bool top, int heightPx)
        {
            var d = Data();
            if (!_registered) { d.uCallbackMessage = 0x0400 + 77; Native.SHAppBarMessage(Native.ABM_NEW, ref d); _registered = true; }
            int sw = (int)Math.Round(SystemParameters.PrimaryScreenWidth * Dpi().x), sh = (int)Math.Round(SystemParameters.PrimaryScreenHeight * Dpi().y);
            d.uEdge = top ? Native.ABE_TOP : Native.ABE_BOTTOM;
            d.rc = new Native.RECT { Left = 0, Right = sw, Top = top ? 0 : sh - heightPx, Bottom = top ? heightPx : sh };
            Native.SHAppBarMessage(Native.ABM_QUERYPOS, ref d);
            if (top) d.rc.Bottom = d.rc.Top + heightPx; else d.rc.Top = d.rc.Bottom - heightPx;
            Native.SHAppBarMessage(Native.ABM_SETPOS, ref d);
            _rc = d.rc;
            DockReserve = B(_rc);
        }

        (double x, double y) Dpi()
        {
            var src = System.Windows.Interop.HwndSource.FromHwnd(_hwnd);
            var m = src?.CompositionTarget?.TransformToDevice;
            return m.HasValue ? (m.Value.M11, m.Value.M22) : (1, 1);
        }

        /// <summary>Ayrılan alan (WPF birimleri).</summary>
        public Rect? ReservedRect(double sx, double sy) =>
            _registered ? new Rect(_rc.Left / sx, _rc.Top / sy, (_rc.Right - _rc.Left) / sx, (_rc.Bottom - _rc.Top) / sy) : null;

        public void Remove()
        {
            if (!_registered) return;
            var d = Data(); Native.SHAppBarMessage(Native.ABM_REMOVE, ref d); _registered = false;
            DockReserve = null;
        }

        // ---- Windows görev çubuğu ----
        // Önce Windows'un kendi ayarı denenir (ABM_SETSTATE / ABS_AUTOHIDE). Windows 11 25H2'de bu çağrı
        // "başarılı" dönüp hiçbir şey değiştirmeyebiliyor (test PC'sinde durum 0 kaldı). O zaman yedek yol:
        // tüm görev çubukları (Shell_TrayWnd + her monitördeki Shell_SecondaryTrayWnd) gizlenir ve fare
        // çubuğun bulunduğu ekran kenarına (üst/alt/sol/sağ) gelince geri gösterilir. Explorer yeniden
        // başlatılmaz, kayıt defteri değişmez; çıkışta her şey açılıştaki hâline döner.
        static int? _originalState;
        static bool _fallback;
        static System.Windows.Threading.DispatcherTimer _poll;
        static readonly System.Collections.Generic.HashSet<IntPtr> _hiddenBars = new();
        static bool _revealed;
        /// <summary>Dock'un AppBar ile ayırdığı alan (piksel); ayırma kapalıysa null.</summary>
        static KutiDock.Core.Box? DockReserve;
        static DateTime _lastReclaim;

        static Native.APPBARDATA TrayData() => new() { cbSize = Marshal.SizeOf<Native.APPBARDATA>(), hWnd = Native.FindWindow("Shell_TrayWnd", null) };
        static int GetState() { var d = TrayData(); return (int)Native.SHAppBarMessage(Native.ABM_GETSTATE, ref d).ToUInt32(); }
        static void SetState(int st) { var d = TrayData(); d.lParam = new IntPtr(st); Native.SHAppBarMessage(Native.ABM_SETSTATE, ref d); }

        public static void SetTaskbarAutoHide(bool on)
        {
            int state = GetState();
            _originalState ??= state;
            if (!on) { RestoreTaskbar(); return; }
            if ((state & Native.ABS_AUTOHIDE) != 0) return; // zaten otomatik gizli
            SetState(state | Native.ABS_AUTOHIDE);
            if ((GetState() & Native.ABS_AUTOHIDE) != 0) return; // Windows ayarı işe yaradı
            StartFallback();
        }

        /// <summary>Kuti Dock kapanırken görev çubuğunu açılıştaki hâline döndürür.</summary>
        public static void RestoreTaskbar()
        {
            StopFallback();
            if (_originalState is { } o && GetState() != o) SetState(o);
        }

        /// <summary>Önceki bir çalışma çökerek görev çubuklarını gizli bıraktıysa geri getirir.</summary>
        public static void ShowAllTaskbars()
        {
            foreach (var h in Taskbars()) if (!Native.IsWindowVisible(h)) Native.ShowWindow(h, Native.SW_SHOWNA);
        }

        static System.Collections.Generic.List<IntPtr> Taskbars()
        {
            var list = new System.Collections.Generic.List<IntPtr>();
            Native.EnumWindows((h, _) =>
            {
                var c = Native.ClassName(h);
                if (c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd") list.Add(h);
                return true;
            }, IntPtr.Zero);
            return list;
        }

        static void StartFallback()
        {
            _fallback = true;
            HideBars();
            ReclaimWorkArea();
            _poll ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _poll.Tick -= Poll; _poll.Tick += Poll;
            _poll.Start();
        }

        static void StopFallback()
        {
            if (!_fallback) return;
            _fallback = false; _poll?.Stop();
            foreach (var h in _hiddenBars) Native.ShowWindow(h, Native.SW_SHOWNA);
            _hiddenBars.Clear(); _revealed = false;
            // Görev çubuklarının alanını geri ver (Explorer kendisi yeniden hesaplamayabilir)
            foreach (var h in Taskbars())
                if (Native.GetWindowRect(h, out var r) && Monitor(h, out var mon, out var work))
                    SetWork(work, KutiDock.Core.WorkArea.Subtract(KutiDock.Core.WorkArea.WhileTaskbarHidden(mon, DockReserve), B(r), mon));
        }

        static void HideBars()
        {
            foreach (var h in Taskbars())
                if (Native.IsWindowVisible(h)) { Native.ShowWindow(h, Native.SW_HIDE); _hiddenBars.Add(h); }
            _revealed = false;
        }

        /// <summary>
        /// Gizlenen görev çubuğu, gizli pencere olsa da ekranda alanını (48 px) ayırmaya devam eder; büyütülen pencereler
        /// boş bir şerit bırakır. Gizliyken her monitörün çalışma alanı monitörün tamamına (eksi dock'un ayırdığı alan) çekilir.
        /// </summary>
        static void ReclaimWorkArea()
        {
            foreach (var h in Taskbars())
                if (Monitor(h, out var mon, out var work))
                    SetWork(work, KutiDock.Core.WorkArea.WhileTaskbarHidden(mon, DockReserve));
        }

        static bool Monitor(IntPtr h, out KutiDock.Core.Box mon, out KutiDock.Core.Box work)
        {
            var mi = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
            bool ok = Native.GetMonitorInfo(Native.MonitorFromWindow(h, 2), ref mi);
            mon = B(mi.rcMonitor); work = B(mi.rcWork);
            return ok;
        }

        static void SetWork(KutiDock.Core.Box current, KutiDock.Core.Box want)
        {
            if (current == want) return;
            var rc = new Native.RECT { Left = want.Left, Top = want.Top, Right = want.Right, Bottom = want.Bottom };
            Native.SystemParametersInfo(Native.SPI_SETWORKAREA, 0, ref rc, Native.SPIF_SENDCHANGE);
        }

        static KutiDock.Core.Box B(Native.RECT r) => new(r.Left, r.Top, r.Right, r.Bottom);

        static void Poll(object sender, EventArgs e)
        {
            // Dock'un Başlat düğmesi menüyü açarken görev çubuğu gösterilmez (yalnızca Başlat menüsü)
            if (TaskbarRemover.Suppressing || TaskbarRemover.Active) return;
            if (!Native.GetCursorPos(out var p)) return;
            var bars = Taskbars();
            bool hover = false;
            foreach (var h in bars)
            {
                if (!Native.GetWindowRect(h, out var r)) continue;
                var mi = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
                if (!Native.GetMonitorInfo(Native.MonitorFromWindow(h, 2), ref mi)) continue;
                if (KutiDock.Core.TaskbarGeometry.TriggerStrip(B(r), B(mi.rcMonitor)).Contains(p.X, p.Y) || (_revealed && B(r).Contains(p.X, p.Y, 8))) { hover = true; break; }
            }
            // Başlat menüsü, bildirim alanı taşması vb. açıkken çubuk açık kalır
            var fg = Native.ClassName(Native.GetForegroundWindow());
            bool shellUi = fg is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "NotifyIconOverflowWindow" or "TopLevelWindowForOverflowXamlIsland" or "Windows.UI.Core.CoreWindow" or "Xaml_WindowedPopupClass";
            bool keep = hover || shellUi;
            if (keep && !_revealed)
            {
                foreach (var h in bars) { Native.ShowWindow(h, Native.SW_SHOWNA); _hiddenBars.Add(h); }
                _revealed = true;
            }
            else if (!keep)
            {
                // Explorer bir çubuğu kendiliğinden geri gösterirse (ör. yeni monitör) tekrar gizle
                foreach (var h in bars) if (Native.IsWindowVisible(h)) { Native.ShowWindow(h, Native.SW_HIDE); _hiddenBars.Add(h); }
                _revealed = false;
                // Explorer alanı geri alırsa yeniden ayarla; ayar yayını sık olmasın diye en çok saniyede bir
                if (DateTime.Now - _lastReclaim > TimeSpan.FromSeconds(1)) { _lastReclaim = DateTime.Now; ReclaimWorkArea(); }
            }
        }
    }
}

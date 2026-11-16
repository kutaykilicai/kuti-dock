// Görev çubuğunu kaldırma yöntemi Gravitone'dan uyarlanmıştır (Core/TaskbarReplacer.cs, Core/TaskbarState.cs, Core/Guard.cs):
// https://github.com/lupanostefano/Gravitone — Copyright (c) 2026 Stefano Lupano — MIT Lisansı (THIRD-PARTY-NOTICES.md).
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Windows.Threading;

namespace KutayDock
{
    /// <summary>
    /// Windows görev çubuğunu dock çalışırken tamamen kaldırır: Explorer otomatik gizlemeye alınır (ekran alanını yalnızca
    /// böyle bırakır), tüm görev çubuğu pencereleri gizlenir ve Explorer bir çubuğu gösterdiği an (Başlat, ekran değişimi)
    /// olay kancasıyla geri gizlenir. Kullanıcının asıl durumu bir işaret dosyasında tutulur; dock kapanınca ya da çökünce
    /// ayrı bir "bekçi" süreci görev çubuğunu geri getirir. Explorer yeniden başlatılmaz.
    /// </summary>
    internal static class TaskbarRemover
    {
        public const string GuardArg = "--gorev-cubugu-bekci";
        const string Primary = "Shell_TrayWnd", Secondary = "Shell_SecondaryTrayWnd";
        const uint EVENT_OBJECT_SHOW = 0x8002, WINEVENT_OUTOFCONTEXT = 0;
        const int OBJID_WINDOW = 0;

        delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);
        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);

        sealed record Marker(int OriginalState, int OwnerPid);
        static string MarkerPath => Path.Combine(Services.DataDir, "gorev-cubugu.json");

        static readonly WinEventProc Proc = OnShow; // yerel kod çağırır: canlı tutulmalı
        static IntPtr _hook; static uint _hookedPid;
        static DispatcherTimer _enforce;
        static Process _guard;
        static bool _active;
        static int _suppress; // Başlat açılırken geçici gizleme (kaldırma kapalıyken)

        public static bool Active => _active;

        public static void SetEnabled(bool on)
        {
            if (on == _active) return;
            _active = on;
            if (on)
            {
                Hide();
                Hook();
                _enforce ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _enforce.Tick -= Enforce; _enforce.Tick += Enforce;
                _enforce.Start();
                StartGuard();
            }
            else
            {
                _enforce?.Stop();
                if (_suppress == 0) Unhook();
                Restore(Environment.ProcessId);
            }
        }

        /// <summary>Önceki bir çalışma (çökme) görev çubuğunu gizli bıraktıysa geri getirir.</summary>
        public static void RestoreOrphaned()
        {
            var m = ReadMarker();
            if (m != null && !IsRunning(m.OwnerPid)) Restore(m.OwnerPid);
        }

        /// <summary>
        /// Dock'taki Başlat: yalnızca Başlat menüsü açılsın, görev çubuğu görünmesin. Başlat açık kaldığı sürece Explorer'ın
        /// gösterdiği görev çubuğu anında gizlenir; menü kapanınca önceki görünürlük geri verilir.
        /// </summary>
        public static void OpenStart()
        {
            if (_active) { Native.PressKeys(Native.VK_LWIN); return; }
            var wasVisible = new System.Collections.Generic.List<IntPtr>();
            foreach (var h in Bars()) if (Native.IsWindowVisible(h)) { wasVisible.Add(h); Native.ShowWindow(h, Native.SW_HIDE); }
            _suppress++;
            Hook();
            Native.PressKeys(Native.VK_LWIN);
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            var started = DateTime.Now;
            t.Tick += (_, _) =>
            {
                var el = DateTime.Now - started;
                if (el < TimeSpan.FromMinutes(5) && (el < TimeSpan.FromSeconds(1) || StartIsOpen())) return; // Başlat hâlâ açık
                t.Stop();
                _suppress--;
                if (_suppress == 0 && !_active) Unhook();
                if (!_active) foreach (var h in wasVisible) Native.ShowWindow(h, Native.SW_SHOWNA);
            };
            t.Start();
        }

        public static bool Suppressing => _suppress > 0;

        static bool StartIsOpen()
        {
            Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out uint pid);
            try
            {
                using var p = Process.GetProcessById((int)pid);
                return p.ProcessName is "StartMenuExperienceHost" or "SearchHost" or "SearchApp";
            }
            catch (Exception) { return false; }
        }

        static void OnShow(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time)
        {
            if (obj != OBJID_WINDOW || child != 0 || (!_active && _suppress == 0)) return;
            var c = Native.ClassName(hwnd);
            if (c is Primary or Secondary) Native.ShowWindow(hwnd, Native.SW_HIDE);
        }

        static void Hook()
        {
            var tray = Native.FindWindow(Primary, null);
            if (tray == IntPtr.Zero || _hook != IntPtr.Zero) return;
            Native.GetWindowThreadProcessId(tray, out uint pid);
            _hook = SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, IntPtr.Zero, Proc, pid, 0, WINEVENT_OUTOFCONTEXT);
            _hookedPid = _hook != IntPtr.Zero ? pid : 0;
        }

        static void Unhook()
        {
            if (_hook != IntPtr.Zero) UnhookWinEvent(_hook);
            _hook = IntPtr.Zero; _hookedPid = 0;
        }

        static void Enforce(object sender, EventArgs e)
        {
            if (!_active) return;
            var tray = Native.FindWindow(Primary, null);
            if (tray == IntPtr.Zero) return;
            if ((GetState(tray) & Native.ABS_AUTOHIDE) == 0) Hide(); else HideBars();
            Native.GetWindowThreadProcessId(tray, out uint pid);
            if (pid != _hookedPid) { Unhook(); Hook(); } // Explorer yeniden başladı
            if (_guard == null || _guard.HasExited) StartGuard();
        }

        static void Hide()
        {
            var tray = Native.FindWindow(Primary, null);
            if (tray == IntPtr.Zero) return;
            using (Lock())
            {
                var m = ReadMarker();
                if (m == null || m.OwnerPid != Environment.ProcessId)
                    WriteMarker(new Marker(m?.OriginalState ?? GetState(tray), Environment.ProcessId)); // eski işaret asıl durumu tutar
                if ((GetState(tray) & Native.ABS_AUTOHIDE) == 0) SetState(tray, Native.ABS_AUTOHIDE | Native.ABS_ALWAYSONTOP);
            }
            HideBars();
        }

        static void HideBars() { foreach (var h in Bars()) if (Native.IsWindowVisible(h)) Native.ShowWindow(h, Native.SW_HIDE); }

        static System.Collections.Generic.List<IntPtr> Bars()
        {
            var list = new System.Collections.Generic.List<IntPtr>();
            var h = Native.FindWindow(Primary, null);
            if (h != IntPtr.Zero) list.Add(h);
            h = IntPtr.Zero;
            while ((h = Native.FindWindowEx(IntPtr.Zero, h, Secondary, null)) != IntPtr.Zero) list.Add(h);
            return list;
        }

        /// <summary>Kullanıcının görev çubuğu durumunu geri koyar ve tüm çubukları gösterir (yalnızca işaret o sürece aitse).</summary>
        public static bool Restore(int ownerPid)
        {
            using (Lock())
            {
                var m = ReadMarker();
                if (m == null || m.OwnerPid != ownerPid) return false;
                var tray = Native.FindWindow(Primary, null);
                if (tray == IntPtr.Zero) return false; // Explorer yok: işaret kalsın, sonraki açılış düzeltir
                SetState(tray, m.OriginalState);
                PersistAutoHide((m.OriginalState & Native.ABS_AUTOHIDE) != 0);
                foreach (var h in Bars()) Native.ShowWindow(h, Native.SW_SHOWNA);
                try { File.Delete(MarkerPath); } catch (Exception) { }
                return true;
            }
        }

        /// <summary>
        /// Explorer otomatik gizleme durumunu bellekte tutar; kayıt defterine de yazılır ki geri verilen durum bir Explorer
        /// yeniden başlangıcında kaybolmasın (StuckRects3\Settings, 8. bayt, bit 0).
        /// </summary>
        static void PersistAutoHide(bool autoHide)
        {
            try
            {
                using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StuckRects3", true);
                if (k?.GetValue("Settings") is not byte[] { Length: > 8 } s) return;
                byte b = (byte)(autoHide ? s[8] | 1 : s[8] & ~1);
                if (b == s[8]) return;
                s[8] = b; k.SetValue("Settings", s, Microsoft.Win32.RegistryValueKind.Binary);
            }
            catch (Exception) { }
        }

        // ---- Bekçi: dock ölünce görev çubuğunu geri getiren ayrı süreç ----
        static void StartGuard()
        {
            try
            {
                using var self = Process.GetCurrentProcess();
                var psi = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
                psi.ArgumentList.Add(GuardArg); psi.ArgumentList.Add(self.Id.ToString()); psi.ArgumentList.Add(self.StartTime.Ticks.ToString());
                _guard = Process.Start(psi);
            }
            catch (Exception) { _guard = null; }
        }

        /// <summary>Bekçi süreci: dock bitene kadar bekler, görev çubuğu gizli kaldıysa geri getirir. Pencere açmaz.</summary>
        public static int RunGuard(string[] args)
        {
            if (args.Length < 3 || !int.TryParse(args[1], out int pid) || !long.TryParse(args[2], out long ticks)) return 1;
            try { using var dock = Process.GetProcessById(pid); if (dock.StartTime.Ticks == ticks) dock.WaitForExit(); }
            catch (Exception) { }
            for (int i = 0; i < 60 && !Restore(pid); i++) // Explorer o an yoksa bir dakika dene
            {
                if (ReadMarker()?.OwnerPid != pid) break;
                Thread.Sleep(1000);
            }
            return 0;
        }

        static bool IsRunning(int pid)
        {
            try { using var p = Process.GetProcessById(pid); return p.ProcessName.Equals("KutayDock", StringComparison.OrdinalIgnoreCase); }
            catch (Exception) { return false; }
        }

        static int GetState(IntPtr tray) { var d = new Native.APPBARDATA { cbSize = Marshal.SizeOf<Native.APPBARDATA>(), hWnd = tray }; return (int)Native.SHAppBarMessage(Native.ABM_GETSTATE, ref d).ToUInt32(); }
        static void SetState(IntPtr tray, int st) { var d = new Native.APPBARDATA { cbSize = Marshal.SizeOf<Native.APPBARDATA>(), hWnd = tray, lParam = new IntPtr(st) }; Native.SHAppBarMessage(Native.ABM_SETSTATE, ref d); }

        static Marker ReadMarker()
        {
            try { return File.Exists(MarkerPath) ? JsonSerializer.Deserialize<Marker>(File.ReadAllText(MarkerPath)) : null; }
            catch (Exception) { return null; }
        }

        static void WriteMarker(Marker m)
        {
            try
            {
                Directory.CreateDirectory(Services.DataDir);
                File.WriteAllText(MarkerPath + ".tmp", JsonSerializer.Serialize(m));
                File.Move(MarkerPath + ".tmp", MarkerPath, true);
            }
            catch (Exception) { }
        }

        static IDisposable Lock() => new MutexLock(new Mutex(false, @"Local\KutayDock.GorevCubugu"));

        sealed class MutexLock : IDisposable
        {
            readonly Mutex _m; readonly bool _owned;
            public MutexLock(Mutex m)
            {
                _m = m;
                try { _owned = m.WaitOne(TimeSpan.FromSeconds(5)); } catch (AbandonedMutexException) { _owned = true; }
            }
            public void Dispose() { if (_owned) _m.ReleaseMutex(); _m.Dispose(); }
        }
    }
}

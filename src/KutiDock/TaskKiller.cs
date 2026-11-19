using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using KutiDock.Core;

namespace KutiDock
{
    /// <summary>
    /// 1.6.13: dock sağ tık menüsündeki "Görevi sonlandır". Donmuş uygulamada da çalışır: süreçler alt süreç ağacıyla
    /// zorla kapatılır (Process.Kill(true)); yetki yetmezse taskkill /F /T yönetici olarak, pencere açmadan.
    /// Chrome/Edge PWA'larında yalnızca pencere kapatılır; tarayıcının tamamı kapanmaz.
    /// </summary>
    internal static class TaskKiller
    {
        /// <summary>Pencerelerin sahibi süreçler. Paketli uygulamada ApplicationFrameHost yerine içindeki asıl süreç.</summary>
        public static List<ProcInfo> Owners(IEnumerable<IntPtr> windows)
        {
            var list = new List<ProcInfo>();
            foreach (var h in windows)
            {
                Native.GetWindowThreadProcessId(h, out uint pid);
                var path = Native.ProcessPath(pid);
                if (path != null && Path.GetFileName(path).Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
                {
                    uint inner = FrameChildPid(h, pid);
                    if (inner == 0) continue; // asıl süreç bulunamadı: paylaşılan çerçeve sürecine dokunulmaz
                    pid = inner; path = Native.ProcessPath(pid);
                }
                if (pid == 0 || list.Any(p => p.Pid == pid)) continue;
                list.Add(new ProcInfo((int)pid, path != null ? Path.GetFileNameWithoutExtension(path) : "", path));
            }
            return list;
        }

        public static KillKind Kind(string key, IReadOnlyCollection<IntPtr> windows) =>
            TaskKill.KindFor(key, windows.Count, Owners(windows));

        /// <summary>Simgenin uygulamasını sonlandırır (arka planda; arayüzü bekletmez).</summary>
        public static void End(string key, string name, IReadOnlyCollection<IntPtr> windows)
        {
            var wins = windows.ToList();
            var kind = Kind(key, wins);
            Task.Run(async () =>
            {
                try
                {
                    if (kind == KillKind.ClosePwaWindows) await EndPwa(name, wins);
                    else if (kind == KillKind.KillProcesses) KillAll(key, name, wins);
                }
                catch (Exception ex) { Trace($"Görevi sonlandır ({name}): hata {ex.GetType().Name}: {ex.Message}"); }
            });
        }

        static void KillAll(string key, string name, List<IntPtr> wins)
        {
            var owners = Owners(wins);
            var all = new List<ProcInfo>();
            foreach (var p in Process.GetProcesses())
            {
                try { all.Add(new ProcInfo(p.Id, p.ProcessName, Native.ProcessPath((uint)p.Id))); }
                catch (Exception) { }
                finally { p.Dispose(); }
            }
            var targets = TaskKill.Targets(key, owners, all, Environment.ProcessId);
            var denied = new List<int>();
            int killed = 0;
            foreach (var pid in targets)
            {
                try { using var p = Process.GetProcessById(pid); p.Kill(entireProcessTree: true); killed++; }
                catch (Win32Exception) { denied.Add(pid); }      // erişim reddedildi (yönetici olarak çalışan süreç)
                catch (ArgumentException) { }                    // zaten kapanmış
                catch (InvalidOperationException) { }            // zaten kapanmış
                catch (AggregateException) { denied.Add(pid); }  // ağaçtaki bir alt süreç kapatılamadı
            }
            Trace($"Görevi sonlandır ({name}): {targets.Count} süreç, {killed} kapatıldı" + (denied.Count > 0 ? $", {denied.Count} yönetici ile deneniyor" : ""));
            if (denied.Count == 0) return;
            try
            {
                var psi = new ProcessStartInfo("taskkill.exe", TaskKill.TaskkillArgs(denied))
                { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(10000);
                Trace($"Görevi sonlandır ({name}): taskkill (yönetici) çıkış kodu {proc?.ExitCode}");
            }
            catch (Win32Exception) { Trace($"Görevi sonlandır ({name}): yönetici izni verilmedi"); }
        }

        static async Task EndPwa(string name, List<IntPtr> wins)
        {
            foreach (var h in wins) Native.PostMessage(h, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            await Task.Delay(TaskKill.PwaCloseWait);
            var left = wins.Where(IsWindow).ToList();
            if (left.Count == 0) { Trace($"Görevi sonlandır ({name}, PWA): pencere kapandı"); return; }
            foreach (var h in left)
            {
                Native.GetWindowThreadProcessId(h, out uint pid);
                int others = VisibleWindowsOf(pid).Count(w => !wins.Contains(w));
                if (TaskKill.CanKillPwaProcess(others))
                {
                    try { using var p = Process.GetProcessById((int)pid); p.Kill(entireProcessTree: false); }
                    catch (Exception) { }
                    Trace($"Görevi sonlandır ({name}, PWA): pencere yanıt vermedi, yalnızca kendi süreci sonlandırıldı");
                }
                else
                    Trace($"Görevi sonlandır ({name}, PWA): pencere yanıt vermedi; süreci tarayıcının {others} başka penceresini de taşıdığı için sonlandırılmadı");
            }
        }

        static List<IntPtr> VisibleWindowsOf(uint pid)
        {
            var list = new List<IntPtr>();
            Native.EnumWindows((h, _) =>
            {
                if (Native.IsWindowVisible(h)) { Native.GetWindowThreadProcessId(h, out uint p); if (p == pid) list.Add(h); }
                return true;
            }, IntPtr.Zero);
            return list;
        }

        static uint FrameChildPid(IntPtr frame, uint framePid)
        {
            uint found = 0;
            EnumChildWindows(frame, (c, _) =>
            {
                Native.GetWindowThreadProcessId(c, out uint p);
                if (p != 0 && p != framePid) { found = p; return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        static void Trace(string line)
        {
            try { KutiDock.Core.RotatingLog.Append(Path.Combine(Services.DataDir, "hata.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}\n"); }
            catch (Exception) { }
        }

        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, Native.EnumWindowsProc cb, IntPtr l);
    }
}

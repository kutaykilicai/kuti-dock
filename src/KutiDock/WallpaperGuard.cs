using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using KutiDock.Core;

namespace KutiDock
{
    /// <summary>
    /// 1.6.17: Lively duvar kağıtları kaybolursa (dock yeniden başlaması, ekran/çözünürlük değişikliği, oyun kapanışı)
    /// LivelyGeriYukle.ps1 -Mod Kontrol'ü pencere açmadan çalıştırır. Kontroller hafiftir (süreç listesi + küçük JSON);
    /// tam ekran oyun/uygulama ön plandayken hiçbir şey yapılmaz. Betik yolu: dock klasörünün kardeşi LivelyGeriYukle\.
    /// </summary>
    internal sealed class WallpaperGuard
    {
        static readonly string Layout = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lively Wallpaper", "WallpaperLayout.json");
        static string Script => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "LivelyGeriYukle", "LivelyGeriYukle.ps1"));

        readonly DispatcherTimer _periodic, _once;
        DateTime? _lastRun;

        public WallpaperGuard(Dispatcher d)
        {
            _periodic = new DispatcherTimer(WallpaperGuardPolicy.Periodic, DispatcherPriority.Background, (_, _) => Check("periyodik"), d);
            _once = new DispatcherTimer(DispatcherPriority.Background, d);
            _once.Tick += (_, _) => { _once.Stop(); Check(_reason); };
        }

        string _reason = "açılış";

        public void Start()
        {
            Schedule(WallpaperGuardPolicy.AfterDockStart, "dock açılışı");
            _periodic.Start();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) =>
                _once.Dispatcher.BeginInvoke(new Action(() => Schedule(WallpaperGuardPolicy.AfterDisplayChange, "ekran değişikliği")));
        }

        void Schedule(TimeSpan after, string reason)
        {
            _reason = reason; _once.Stop(); _once.Interval = after; _once.Start();
        }

        void Check(string reason)
        {
            try
            {
                bool lively = Process.GetProcessesByName("Lively").Length > 0;
                if (!lively) return;
                int players = 0;
                foreach (var p in Process.GetProcessesByName("mpv"))
                {
                    try { if (p.MainModule?.FileName?.Contains("Lively Wallpaper", StringComparison.OrdinalIgnoreCase) == true) players++; }
                    catch (Exception) { players++; } // yol okunamadıysa Lively'nin sayılır: gereksiz yeniden başlatmaktansa
                    finally { p.Dispose(); }
                }
                int? count = null;
                try { count = File.Exists(Layout) ? WallpaperGuardPolicy.LayoutCount(File.ReadAllText(Layout)) : null; } catch (Exception) { }
                var fg = Native.GetForegroundWindow();
                bool fullscreen = fg != IntPtr.Zero && Native.ForegroundIsFullscreenOn(Native.MonitorFromWindow(fg, 2));
                var script = Script;
                if (!WallpaperGuardPolicy.ShouldRestore(lively, count, players, fullscreen, File.Exists(script), _lastRun, DateTime.UtcNow))
                {
                    if (WallpaperGuardPolicy.Missing(count, players)) Log($"eksik ama çalıştırılmadı ({reason}; tam ekran={fullscreen}, betik={File.Exists(script)})");
                    return;
                }
                _lastRun = DateTime.UtcNow;
                Log($"duvar kağıdı eksik ({reason}; düzen={count?.ToString() ?? "?"}, oynatıcı={players}); geri yükleme betiği çalıştırıldı");
                Process.Start(new ProcessStartInfo("powershell.exe",
                    $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\" -Mod Kontrol")
                {
                    UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(script),
                })?.Dispose();
            }
            catch (Exception ex) { Log("kontrol hatası (" + ex.GetType().Name + ")"); }
        }

        static void Log(string msg)
        {
            try { File.AppendAllText(Path.Combine(Services.DataDir, "hata.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} Duvar kağıdı bekçisi: {msg}\n"); }
            catch (Exception) { }
        }
    }
}

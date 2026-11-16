using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using KutayDock.Core;

namespace KutayDock
{
    /// <summary>
    /// Windows optimizasyonu: RAM boşaltma (süreçlerin çalışma kümesi + yönetici ile bekleme listesi) ve geçici dosya /
    /// DNS önbelleği temizliği. Tarayıcı önbelleği, oturumlar ve kullanıcı verisine dokunulmaz. Oyun açıkken çalışmaz;
    /// oyun, Steam, hile önleyici ve GPU sürücüsü süreçlerine asla dokunulmaz.
    /// </summary>
    internal static class Optimizer
    {
        public const string AdminArg = "--yonetici-optimizasyon";
        [DllImport("psapi.dll")] static extern bool EmptyWorkingSet(IntPtr h);
        [DllImport("dnsapi.dll")] static extern bool DnsFlushResolverCache();
        public static bool FlushDns() { try { return DnsFlushResolverCache(); } catch (Exception) { return false; } }
        const uint PROCESS_SET_QUOTA = 0x0100;

        static string WinTemp => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
        static string UserTemp => Path.GetTempPath();
        /// <summary>Son 24 saatte değişen dosyalar silinmez (çalışan programların dosyaları).</summary>
        static readonly TimeSpan MinAge = TimeSpan.FromHours(24);

        /// <summary>Oyun açık mı (Marvel Rivals, Steam/Epic/Riot oyunları ya da D3D tam ekran).</summary>
        public static string RunningGame()
        {
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    var path = OptimizeRules.IsGame(p.ProcessName, null) ? null : Native.ProcessPath((uint)p.Id);
                    if (OptimizeRules.IsGame(p.ProcessName, path)) return p.ProcessName;
                }
                catch (Exception) { }
                finally { p.Dispose(); }
            }
            return null;
        }

        /// <summary>Kendi oturumundaki korunmayan süreçlerin çalışma kümesini boşaltır. Boşaltılan süreç sayısını döndürür.</summary>
        public static int TrimWorkingSets()
        {
            int n = 0, me = Environment.ProcessId;
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == me || p.Id <= 4) continue;
                    var path = Native.ProcessPath((uint)p.Id);
                    if (OptimizeRules.IsProtected(p.ProcessName, path)) continue;
                    var h = Native.OpenProcess(PROCESS_SET_QUOTA | Native.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)p.Id);
                    if (h == IntPtr.Zero) continue;
                    try { if (EmptyWorkingSet(h)) n++; } finally { Native.CloseHandle(h); }
                }
                catch (Exception) { }
                finally { p.Dispose(); }
            }
            return n;
        }

        /// <summary>Bekleme listesi (standby) temizliği yönetici ister: kendi exe'miz gizli "runas" ile çalışır, pencere açılmaz.</summary>
        public static Task<bool> PurgeStandbyAsync() => RunElevated("standby");

        public sealed record TempScan(long UserBytes, int UserFiles, long WinBytes, int WinFiles)
        {
            public long Total => UserBytes + WinBytes;
        }

        /// <summary>Silinecekleri ölçer (silmez). Windows\Temp yönetici ister; ölçülemezse 0.</summary>
        public static async Task<TempScan> ScanAsync()
        {
            var (ub, uf) = await Task.Run(() => Measure(UserTemp));
            long wb = 0; int wf = 0;
            var tmp = Path.Combine(Path.GetTempPath(), "kutaydock-wintemp.txt");
            try
            {
                File.Delete(tmp);
                if (await RunElevated("scan", tmp) && File.Exists(tmp))
                {
                    var parts = File.ReadAllText(tmp).Split(';');
                    long.TryParse(parts[0], out wb); if (parts.Length > 1) int.TryParse(parts[1], out wf);
                }
            }
            catch (Exception) { }
            finally { try { File.Delete(tmp); } catch (Exception) { } }
            return new TempScan(ub, uf, wb, wf);
        }

        /// <summary>Onaydan sonra: geçici dosyalar (24 saatten eski), Windows\Temp (yönetici) ve DNS önbelleği.</summary>
        public static async Task<long> CleanAsync(bool windowsTemp)
        {
            long freed = await Task.Run(() => Delete(UserTemp));
            if (windowsTemp) await RunElevated("clean");
            try { DnsFlushResolverCache(); } catch (Exception) { }
            return freed;
        }

        static (long bytes, int files) Measure(string dir)
        {
            long b = 0; int n = 0;
            foreach (var f in Files(dir)) { b += f.Length; n++; }
            return (b, n);
        }

        static long Delete(string dir)
        {
            long freed = 0;
            foreach (var f in Files(dir))
            {
                try { long len = f.Length; f.Delete(); freed += len; } catch (Exception) { } // kullanımdaki dosya: atla
            }
            foreach (var d in SafeDirs(dir).OrderByDescending(x => x.Length))
                try { if (!Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d); } catch (Exception) { }
            return freed;
        }

        static IEnumerable<FileInfo> Files(string dir)
        {
            var cutoff = DateTime.Now - MinAge;
            var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System };
            IEnumerable<FileInfo> all;
            try { all = new DirectoryInfo(dir).EnumerateFiles("*", opts); } catch (Exception) { yield break; }
            foreach (var f in all)
            {
                bool old;
                try { old = f.LastWriteTime < cutoff && f.LastAccessTime < cutoff; } catch (Exception) { continue; }
                if (old) yield return f;
            }
        }

        static IEnumerable<string> SafeDirs(string dir)
        {
            try { return Directory.EnumerateDirectories(dir, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }).ToList(); }
            catch (Exception) { return Array.Empty<string>(); }
        }

        static Task<bool> RunElevated(params string[] args) => Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
                psi.Arguments = AdminArg + " " + string.Join(" ", args.Select(a => "\"" + a + "\""));
                using var p = Process.Start(psi);
                if (p == null || !p.WaitForExit(120_000)) return false;
                return p.ExitCode == 0;
            }
            catch (Exception) { return false; } // UAC reddedildi ya da yetki yok
        });

        // ---- Yönetici kipi (yalnızca dock'un kendisi başlatır; pencere açmaz) ----
        [DllImport("ntdll.dll")] static extern int NtSetSystemInformation(int cls, ref int info, int len);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr p, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool LookupPrivilegeValue(string sys, string name, out long luid);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool AdjustTokenPrivileges(IntPtr t, bool disableAll, ref TOKEN_PRIVILEGES np, int len, IntPtr prev, IntPtr retLen);
        [StructLayout(LayoutKind.Sequential, Pack = 4)] struct TOKEN_PRIVILEGES { public int Count; public long Luid; public int Attributes; }

        public static int RunAdmin(string[] args)
        {
            try
            {
                switch (args.Length > 1 ? args[1] : "")
                {
                    case "standby":
                        Enable("SeProfileSingleProcessPrivilege");
                        int cmd = 4; // MemoryPurgeStandbyList
                        return NtSetSystemInformation(80 /*SystemMemoryListInformation*/, ref cmd, 4) == 0 ? 0 : 2;
                    case "scan":
                        var (b, n) = Measure(WinTemp);
                        File.WriteAllText(args[2], $"{b};{n}");
                        return 0;
                    case "clean":
                        Delete(WinTemp);
                        return 0;
                }
            }
            catch (Exception) { }
            return 1;
        }

        static void Enable(string privilege)
        {
            if (!OpenProcessToken(Process.GetCurrentProcess().Handle, 0x20 | 0x8, out var t)) return; // ADJUST_PRIVILEGES | QUERY
            try
            {
                if (!LookupPrivilegeValue(null, privilege, out long luid)) return;
                var tp = new TOKEN_PRIVILEGES { Count = 1, Luid = luid, Attributes = 2 /*SE_PRIVILEGE_ENABLED*/ };
                AdjustTokenPrivileges(t, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
            }
            finally { Native.CloseHandle(t); }
        }
    }
}

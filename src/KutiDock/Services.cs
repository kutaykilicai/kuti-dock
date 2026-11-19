using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using KutiDock.Core;

namespace KutiDock
{
    /// <summary>Widget'ların ortak veri kaynakları. Ağır işler arka planda ve seyrek yapılır.</summary>
    internal sealed class Services : IDisposable
    {
        public static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KutiDock");
        public static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

        public Settings Settings;
        public readonly CoreAudio Audio = new();

        // Sistem
        public double Cpu, Ram, Gpu = -1; public ulong RamUsed, RamTotal;
        public double Down, Up; public readonly List<double> DownHistory = new();
        public int? Battery; public bool Charging, HasBattery;
        Native.FILETIME _i0, _k0, _u0; long _rx0 = -1, _tx0; DateTime _net0;

        // Veri
        public WeatherNow Weather; DateTime _weatherAt = DateTime.MinValue;
        public List<DriveInfo> Drives = new(); DateTime _drivesAt = DateTime.MinValue;
        /// <summary>Cursor planı, Cursor CLI'nin belgelenmiş "agent about --format json" komutundan.</summary>
        public string CursorTier; DateTime _tierAt = DateTime.MinValue;
        /// <summary>Kuti Dock'un kendi Cursor girişi (isteğe bağlı; belgelenmemiş API).</summary>
        public readonly CursorAccount Cursor = new();
        /// <summary>1.6.15: AllModels / OpenRouter / Kimi bakiyeleri (resmi API'ler, DPAPI'li anahtarlar).</summary>
        public readonly AiBalanceService Balances = new();
        /// <summary>1.6.16: Gemini kullanım kartları, Kutay'ın Chrome eklentisinden 127.0.0.1 üzerinden.</summary>
        public readonly GeminiBridge Gemini = new();

        /// <summary>
        /// Grok Bot limiti: girişliyse ve son 30 dk'da okunduysa Cursor'dan gelen değer (live = true),
        /// değilse elle girilen değer.
        /// </summary>
        public (ManualLimit Limit, bool Live) GrokBotLimit()
        {
            var c = Cursor;
            if (c.SignedIn && !c.NeedsRelogin && c.Sand is { Enabled: true, Percent: not null } s && c.SandAt is { } at && DateTimeOffset.Now - at < TimeSpan.FromMinutes(30))
                return (new ManualLimit { Percent = s.Percent, ResetsAt = s.ResetsAt, UpdatedAt = at }, true);
            return (Settings.GrokBot, false);
        }

        PerformanceCounterHelper _gpu;
        bool _busyWeather, _busyTier; volatile bool _busyGpu;

        public Services(Settings s)
        {
            Settings = s;
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("KutiDock/1.0");
            Native.GetSystemTimes(out _i0, out _k0, out _u0);
            _gpu = new PerformanceCounterHelper();
            Gemini.Start();
        }

        DateTime _lastTick;

        /// <summary>Her saniye çağrılır (dock gizliyken çağrılmaz). Birden çok şerit çağırsa da saniyede bir ölçer.</summary>
        public void Tick()
        {
            if ((DateTime.UtcNow - _lastTick).TotalMilliseconds < 700) return;
            _lastTick = DateTime.UtcNow;
            // CPU
            Native.GetSystemTimes(out var i1, out var k1, out var u1);
            Cpu = Format.CpuPercent(_i0.Value, _k0.Value, _u0.Value, i1.Value, k1.Value, u1.Value);
            _i0 = i1; _k0 = k1; _u0 = u1;
            // RAM
            var m = new Native.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.MEMORYSTATUSEX>() };
            if (Native.GlobalMemoryStatusEx(ref m)) { Ram = m.dwMemoryLoad; RamTotal = m.ullTotalPhys; RamUsed = m.ullTotalPhys - m.ullAvailPhys; }
            // GPU (2 sn'de bir)
            if (DateTime.Now.Second % 2 == 0 && !_busyGpu) { _busyGpu = true; Task.Run(() => { Gpu = _gpu.Read(); _busyGpu = false; }); }
            // Ağ
            long rx = 0, tx = 0;
            try
            {
                foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (n.OperationalStatus != OperationalStatus.Up || n.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var st = n.GetIPStatistics(); rx += st.BytesReceived; tx += st.BytesSent;
                }
            }
            catch (Exception) { }
            var now = DateTime.UtcNow;
            if (_rx0 >= 0)
            {
                double sec = Math.Max(0.2, (now - _net0).TotalSeconds);
                Down = Math.Max(0, (rx - _rx0) / sec); Up = Math.Max(0, (tx - _tx0) / sec);
                DownHistory.Add(Down); if (DownHistory.Count > 40) DownHistory.RemoveAt(0);
            }
            _rx0 = rx; _tx0 = tx; _net0 = now;
            // Pil
            if (Native.GetSystemPowerStatus(out var ps))
            {
                HasBattery = (ps.BatteryFlag & 128) == 0 && ps.BatteryLifePercent <= 100;
                Battery = HasBattery ? ps.BatteryLifePercent : null;
                Charging = ps.ACLineStatus == 1;
            }
            RefreshSlowData();
        }

        static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase);
        public static string AppName(string path)
        {
            if (Names.TryGetValue(path, out var n)) return n;
            try { n = FileVersionInfo.GetVersionInfo(path).FileDescription; } catch (Exception) { }
            if (string.IsNullOrWhiteSpace(n)) n = Path.GetFileNameWithoutExtension(path);
            return Names[path] = n.Trim();
        }

        void RefreshSlowData()
        {
            var now = DateTime.Now;
            if (!_busyWeather && (now - _weatherAt).TotalMinutes >= 30) { _busyWeather = true; _ = LoadWeather(); }
            if ((now - _drivesAt).TotalSeconds >= 30)
            {
                _drivesAt = now;
                try { Drives = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable && d.TotalSize > 0).ToList(); }
                catch (Exception) { }
            }
            if (!_busyTier && (now - _tierAt).TotalHours >= 6) { _busyTier = true; _ = LoadCursorTier(); }
            _ = Cursor.RefreshSand(); // kendi içinde 5 dk sınırı var
            _ = Balances.Refresh(force: false); // kendi içinde 10 dk sınırı var
        }

        public void InvalidateData() { _weatherAt = DateTime.MinValue; _drivesAt = DateTime.MinValue; }

        async Task LoadWeather()
        {
            try { Weather = Core.Weather.Parse(await Http.GetStringAsync(Core.Weather.Url(Settings.Latitude, Settings.Longitude))); }
            catch (Exception) { }
            finally { _weatherAt = DateTime.Now; _busyWeather = false; }
        }

        /// <summary>
        /// Cursor CLI kuruluysa "agent about --format json" pencere açmadan çalıştırılır; yalnızca subscriptionTier okunur.
        /// Cursor'ın belirteçlerine, çerezlerine ya da yerel veritabanına dokunulmaz.
        /// </summary>
        async Task LoadCursorTier()
        {
            try
            {
                var cmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "cursor-agent", "agent.cmd");
                if (!File.Exists(cmd)) return;
                var psi = new ProcessStartInfo(cmd, "about --format json")
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                };
                using var p = Process.Start(psi);
                var outTask = p.StandardOutput.ReadToEndAsync();
                var done = await Task.Run(() => p.WaitForExit(30000));
                if (!done) { try { p.Kill(true); } catch (Exception) { } return; }
                CursorTier = Limits.ParseCursorTier(await outTask) ?? CursorTier;
            }
            catch (Exception) { }
            finally { _tierAt = DateTime.Now; _busyTier = false; }
        }

        public void Dispose() { _gpu?.Dispose(); Gemini.Dispose(); }
    }

    /// <summary>GPU yükü: Windows "GPU Engine" sayaçlarındaki 3D motorlarının toplamı. Yoksa -1.</summary>
    internal sealed class PerformanceCounterHelper : IDisposable
    {
        // 1.6.21: kategori tek okumada (her 3D motor örneği için ayrı sayaç yerine; bkz. GpuUsage)
        readonly PerformanceCounterCategory _category = new("GPU Engine");
        Dictionary<string, (long Raw, long Time)> _previous;
        bool _unavailable;

        public double Read()
        {
            if (_unavailable) return -1;
            try
            {
                var current = new Dictionary<string, (long Raw, long Time)>();
                var data = _category.ReadCategory()["utilization percentage"];
                if (data == null) { _unavailable = true; return -1; }
                foreach (InstanceData d in data.Values)
                    if (GpuUsage.Is3D(d.InstanceName)) current[d.InstanceName] = (d.Sample.RawValue, d.Sample.TimeStamp100nSec);
                var previous = _previous;
                _previous = current;
                return previous == null ? 0 : GpuUsage.Percent(previous, current);
            }
            catch (Exception) { _unavailable = true; return -1; }
        }

        public void Dispose() { }
    }
}

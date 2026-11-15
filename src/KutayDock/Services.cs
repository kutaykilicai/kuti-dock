using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using KutayDock.Core;
using Windows.Media.Control;

namespace KutayDock
{
    /// <summary>Widget'ların ortak veri kaynakları. Ağır işler arka planda ve seyrek yapılır.</summary>
    internal sealed class Services : IDisposable
    {
        public static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KutayDock");
        public static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

        public Settings Settings;
        public readonly CoreAudio Audio = new();

        // Sistem
        public double Cpu, Ram, Gpu = -1; public ulong RamUsed, RamTotal;
        public double Down, Up; public readonly List<double> DownHistory = new();
        public int? Battery; public bool Charging, HasBattery;
        Native.FILETIME _i0, _k0, _u0; long _rx0 = -1, _tx0; DateTime _net0;

        // Medya
        GlobalSystemMediaTransportControlsSessionManager _media;
        public string MediaTitle, MediaArtist; public bool MediaPlaying, MediaAvailable;

        // Veri
        public List<CalEvent> Events = new(); public DateTime EventsAt = DateTime.MinValue; public string CalendarError;
        public WeatherNow Weather; DateTime _weatherAt = DateTime.MinValue;
        public AiUsageSnapshot Ai; DateTime _aiAt = DateTime.MinValue; public string AiError;
        public ScreenTimeStore ScreenTime = new(); DateTime _stLast = DateTime.Now, _stSaved = DateTime.Now;

        PerformanceCounterHelper _gpu;
        bool _busyCal, _busyWeather, _busyAi; volatile bool _busyGpu;
        readonly Dictionary<string, (long len, DateTime mtime, List<ClaudeUsage.Entry> entries)> _claudeCache = new();

        public Services(Settings s)
        {
            Settings = s;
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("KutayDock/1.0");
            Native.GetSystemTimes(out _i0, out _k0, out _u0);
            try
            {
                var p = Path.Combine(DataDir, "ekran-suresi.json");
                if (File.Exists(p)) ScreenTime = Settings.Deserialize<ScreenTimeStore>(File.ReadAllText(p)) ?? new();
            }
            catch (Exception) { }
            _ = InitMedia();
            _gpu = new PerformanceCounterHelper();
        }

        async Task InitMedia()
        {
            try { _media = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync(); }
            catch (Exception) { _media = null; }
        }

        /// <summary>Her saniye çağrılır (dock gizliyken çağrılmaz).</summary>
        public void Tick()
        {
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
            // Medya
            try
            {
                var sess = _media?.GetCurrentSession();
                MediaAvailable = sess != null;
                if (sess != null)
                {
                    MediaPlaying = sess.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                    _ = RefreshMediaProps(sess);
                }
                else { MediaPlaying = false; MediaTitle = null; MediaArtist = null; }
            }
            catch (Exception) { MediaAvailable = false; }

            TrackScreenTime();
            RefreshSlowData();
        }

        async Task RefreshMediaProps(GlobalSystemMediaTransportControlsSession s)
        {
            try { var p = await s.TryGetMediaPropertiesAsync(); MediaTitle = p?.Title; MediaArtist = p?.Artist; } catch (Exception) { }
        }

        public async void MediaCommand(string cmd)
        {
            try
            {
                var s = _media?.GetCurrentSession(); if (s == null) return;
                switch (cmd)
                {
                    case "toggle": await s.TryTogglePlayPauseAsync(); break;
                    case "pause": await s.TryPauseAsync(); break;
                    case "next": await s.TrySkipNextAsync(); break;
                    case "prev": await s.TrySkipPreviousAsync(); break;
                }
            }
            catch (Exception) { }
        }

        void TrackScreenTime()
        {
            var now = DateTime.Now;
            double sec = (now - _stLast).TotalSeconds; _stLast = now;
            if (sec <= 0 || sec > 10) return; // uyku/hazırda bekleme sonrası atla
            if (Native.IdleTime() > TimeSpan.FromMinutes(3)) return; // boşta sayılmaz
            var fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero) return;
            Native.GetWindowThreadProcessId(fg, out uint pid);
            var path = Native.ProcessPath(pid);
            if (path == null) return;
            ScreenTime.Add(AppName(path), now, sec);
            if ((now - _stSaved).TotalSeconds >= 60) { SaveScreenTime(); _stSaved = now; }
        }

        static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase);
        public static string AppName(string path)
        {
            if (Names.TryGetValue(path, out var n)) return n;
            try { n = FileVersionInfo.GetVersionInfo(path).FileDescription; } catch (Exception) { }
            if (string.IsNullOrWhiteSpace(n)) n = Path.GetFileNameWithoutExtension(path);
            return Names[path] = n.Trim();
        }

        public void SaveScreenTime()
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                ScreenTime.Prune(DateTime.Today);
                File.WriteAllText(Path.Combine(DataDir, "ekran-suresi.json"), Settings.Serialize(ScreenTime));
            }
            catch (Exception) { }
        }

        void RefreshSlowData()
        {
            var now = DateTime.Now;
            if (!_busyCal && Settings.IcsUrls.Count > 0 && (now - EventsAt).TotalMinutes >= 15) { _busyCal = true; _ = LoadCalendar(); }
            if (!_busyWeather && (now - _weatherAt).TotalMinutes >= 30) { _busyWeather = true; _ = LoadWeather(); }
            if (!_busyAi && Settings.AiSource != AiSource.Kapali && (now - _aiAt).TotalSeconds >= 60) { _busyAi = true; _ = LoadAi(); }
        }

        public void InvalidateData() { EventsAt = DateTime.MinValue; _weatherAt = DateTime.MinValue; _aiAt = DateTime.MinValue; if (Settings.IcsUrls.Count == 0) Events = new(); }

        async Task LoadCalendar()
        {
            try
            {
                var from = DateTimeOffset.Now.AddHours(-12); var to = DateTimeOffset.Now.AddDays(14);
                var all = new List<CalEvent>(); string err = null;
                foreach (var url in Settings.IcsUrls.Where(u => !string.IsNullOrWhiteSpace(u)))
                {
                    try
                    {
                        var u = url.Trim(); if (u.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)) u = "https://" + u[9..];
                        var text = await Http.GetStringAsync(u);
                        all.AddRange(await Task.Run(() => Ics.Parse(text, from, to)));
                    }
                    catch (Exception) { err = "Bir takvim okunamadı"; }
                }
                Events = all.OrderBy(e => e.Start).ToList(); CalendarError = err;
            }
            finally { EventsAt = DateTime.Now; _busyCal = false; }
        }

        async Task LoadWeather()
        {
            try { Weather = Core.Weather.Parse(await Http.GetStringAsync(Core.Weather.Url(Settings.Latitude, Settings.Longitude))); }
            catch (Exception) { }
            finally { _weatherAt = DateTime.Now; _busyWeather = false; }
        }

        async Task LoadAi()
        {
            try
            {
                var s = Settings;
                Ai = await Task.Run(() =>
                {
                    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    var since = DateTime.UtcNow.AddDays(-8);
                    if (s.AiSource == AiSource.Codex)
                    {
                        var dir = Path.Combine(home, ".codex", "sessions");
                        if (!Directory.Exists(dir)) { AiError = "~/.codex bulunamadı"; return null; }
                        var files = Directory.EnumerateFiles(dir, "*.jsonl", SearchOption.AllDirectories)
                            .Select(f => new FileInfo(f)).Where(f => f.LastWriteTimeUtc > since).OrderByDescending(f => f.LastWriteTimeUtc).Take(3);
                        foreach (var f in files) { var snap = CodexUsage.FromLines(ReadLines(f.FullName)); if (snap != null) { AiError = null; return snap; } }
                        AiError = "Codex kaydında limit yok"; return null;
                    }
                    var roots = new[] { Path.Combine(home, ".claude", "projects"), Path.Combine(home, ".config", "claude", "projects") }.Where(Directory.Exists).ToList();
                    if (roots.Count == 0) { AiError = "~/.claude bulunamadı"; return null; }
                    // Değişmeyen dosyalar yeniden okunmaz (önbellek: boyut + değiştirilme zamanı).
                    var entries = new List<ClaudeUsage.Entry>();
                    var seenFiles = new HashSet<string>();
                    foreach (var f in roots.SelectMany(r => Directory.EnumerateFiles(r, "*.jsonl", SearchOption.AllDirectories)).Select(f => new FileInfo(f)).Where(f => f.LastWriteTimeUtc > since))
                    {
                        seenFiles.Add(f.FullName);
                        if (!_claudeCache.TryGetValue(f.FullName, out var c) || c.len != f.Length || c.mtime != f.LastWriteTimeUtc)
                            _claudeCache[f.FullName] = c = (f.Length, f.LastWriteTimeUtc, ClaudeUsage.ParseLines(ReadLines(f.FullName)).ToList());
                        entries.AddRange(c.entries);
                    }
                    foreach (var k in _claudeCache.Keys.Where(k => !seenFiles.Contains(k)).ToList()) _claudeCache.Remove(k);
                    AiError = null;
                    return ClaudeUsage.Compute(entries, DateTimeOffset.Now, s.ClaudeSessionTokenLimit, s.ClaudeWeeklyTokenLimit, s.WeeklyResetDay, s.WeeklyResetHour);
                });
            }
            catch (Exception) { AiError = "Okunamadı"; }
            finally { _aiAt = DateTime.Now; _busyAi = false; }
        }

        static IEnumerable<string> ReadLines(string path)
        {
            // Dosya başka bir süreç tarafından yazılırken de okunabilsin.
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs);
            string line;
            while ((line = sr.ReadLine()) != null) yield return line;
        }

        public void Dispose() { SaveScreenTime(); _gpu?.Dispose(); }
    }

    /// <summary>GPU yükü: Windows "GPU Engine" sayaçlarındaki 3D motorlarının toplamı. Yoksa -1.</summary>
    internal sealed class PerformanceCounterHelper : IDisposable
    {
        readonly Dictionary<string, PerformanceCounter> _counters = new();
        DateTime _listed = DateTime.MinValue; bool _unavailable;

        public double Read()
        {
            if (_unavailable) return -1;
            try
            {
                if ((DateTime.Now - _listed).TotalSeconds > 15)
                {
                    _listed = DateTime.Now;
                    var cat = new PerformanceCounterCategory("GPU Engine");
                    var names = cat.GetInstanceNames().Where(n => n.EndsWith("engtype_3D", StringComparison.OrdinalIgnoreCase)).ToHashSet();
                    foreach (var k in _counters.Keys.Where(k => !names.Contains(k)).ToList()) { _counters[k].Dispose(); _counters.Remove(k); }
                    foreach (var n in names.Where(n => !_counters.ContainsKey(n)))
                    {
                        var c = new PerformanceCounter("GPU Engine", "Utilization Percentage", n, true);
                        c.NextValue(); _counters[n] = c;
                    }
                }
                double sum = 0;
                foreach (var c in _counters.Values) { try { sum += c.NextValue(); } catch (Exception) { } }
                return Math.Min(100, sum);
            }
            catch (Exception) { _unavailable = true; return -1; }
        }

        public void Dispose() { foreach (var c in _counters.Values) c.Dispose(); _counters.Clear(); }
    }
}

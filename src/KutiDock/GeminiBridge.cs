using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KutiDock.Core;

namespace KutiDock
{
    /// <summary>
    /// 1.6.16: Kutay'ın Chrome'undaki Kuti Dock Gemini eklentisinden kullanım kartlarını alan yerel uç nokta.
    /// Yalnızca 127.0.0.1:47615 dinlenir (dış ağa kapalı), her istek dock'un ürettiği gizli değeri taşımalı.
    /// Çerez, oturum belirteci ya da Chrome profili okunmaz; dock Google'a hiç bağlanmaz.
    /// Uçlar: POST /gemini {text, source, backgroundOk}  ·  GET /gemini/istek → {"yenile":bool}
    /// </summary>
    internal sealed class GeminiBridge : IDisposable
    {
        static readonly string Store = Path.Combine(Services.DataDir, GeminiUsage.StoreFile);
        static readonly string SecretPath = Path.Combine(Services.DataDir, GeminiUsage.SecretFile);
        readonly string _secret;
        TcpListener _listener;
        readonly CancellationTokenSource _cts = new();
        volatile bool _refresh;

        public GeminiUsageData Data { get; private set; }
        /// <summary>Eklentinin son arka plan denemesi başarılı mıydı (null = hiç denemedi).</summary>
        public bool? BackgroundOk { get; private set; }
        public DateTimeOffset? RefreshRequestedAt { get; private set; }
        public event Action Changed;

        public GeminiBridge()
        {
            try { Data = File.Exists(Store) ? GeminiUsageData.FromJson(File.ReadAllText(Store)) : null; } catch (Exception) { }
            BackgroundOk = Data?.BackgroundOk;
            _secret = LoadSecret();
            WriteExtensionSecret();
        }

        public static string ExtensionFolder =>
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", GeminiUsage.ExtensionFolderName));

        public void Start()
        {
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, GeminiUsage.Port); // 0.0.0.0 değil
                _listener.Start();
                _ = Loop(_cts.Token);
                Log($"yerel uç nokta 127.0.0.1:{GeminiUsage.Port} dinleniyor");
            }
            catch (Exception ex) { Log("uç nokta başlatılamadı (" + ex.GetType().Name + ")"); }
        }

        /// <summary>Panelde Gemini satırına tıklanınca: eklenti arka planda okuyabiliyorsa yenileme ister (true), okuyamıyorsa false.</summary>
        public bool RequestRefresh()
        {
            if (BackgroundOk != true) return false;
            _refresh = true; RefreshRequestedAt = DateTimeOffset.Now; Changed?.Invoke();
            return true;
        }

        async Task Loop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient c;
                try { c = await _listener.AcceptTcpClientAsync(ct); }
                catch (Exception) { if (ct.IsCancellationRequested) return; await Task.Delay(500); continue; }
                _ = Task.Run(() => Handle(c));
            }
        }

        async Task Handle(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 5000; client.SendTimeout = 5000;
                    if (client.Client.RemoteEndPoint is IPEndPoint ep && !IPAddress.IsLoopback(ep.Address)) return;
                    var s = client.GetStream();
                    var (method, path, headers, body) = await Read(s);
                    if (method == null) { await Reply(s, 400, "{}"); return; }
                    headers.TryGetValue("origin", out var origin);
                    if (!string.IsNullOrEmpty(origin) && !origin.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase)) { await Reply(s, 403, "{}"); return; }
                    if (method == "OPTIONS") { await Reply(s, 204, "", origin); return; } // olası CORS / Private Network ön isteği
                    headers.TryGetValue(GeminiUsage.SecretHeader.ToLowerInvariant(), out var given);
                    if (!GeminiUsage.SecretMatches(_secret, given)) { await Reply(s, 403, "{}"); Log("gizli değer eşleşmeyen istek reddedildi"); return; }
                    if (method == "GET" && path == "/gemini/istek")
                    {
                        var r = _refresh; _refresh = false;
                        await Reply(s, 200, r ? "{\"yenile\":true}" : "{\"yenile\":false}", origin);
                        return;
                    }
                    if (method == "POST" && path == "/gemini")
                    {
                        await Reply(s, 200, Accept(body) ? "{\"ok\":true}" : "{\"ok\":false}", origin);
                        return;
                    }
                    await Reply(s, 404, "{}");
                }
                catch (Exception) { }
            }
        }

        bool Accept(string body)
        {
            string text = null, source = "sayfa"; bool? bg = null;
            try
            {
                var r = JsonDocument.Parse(body).RootElement;
                if (r.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String) text = t.GetString();
                if (r.TryGetProperty("source", out var so) && so.ValueKind == JsonValueKind.String) source = so.GetString() == "arka plan" ? "arka plan" : "sayfa";
                if (r.TryGetProperty("backgroundOk", out var b) && b.ValueKind is JsonValueKind.True or JsonValueKind.False) bg = b.GetBoolean();
            }
            catch (Exception) { return false; }
            if (bg != null) BackgroundOk = bg;
            var now = DateTimeOffset.Now;
            var parsed = GeminiUsage.Parse(text, now.LocalDateTime);
            if (parsed is not { } p)
            {
                if (text != null) Log($"metin okunamadı ({source})");
                Changed?.Invoke();
                return bg != null && text == null; // yalnızca durum bildirimi
            }
            Data = new GeminiUsageData(p.Current, p.Weekly, now, source, BackgroundOk == true);
            try { Directory.CreateDirectory(Services.DataDir); File.WriteAllText(Store, Data.ToJson()); } catch (Exception) { }
            Log($"güncellendi ({source})");
            Changed?.Invoke();
            return true;
        }

        static async Task<(string, string, System.Collections.Generic.Dictionary<string, string>, string)> Read(NetworkStream s)
        {
            var buf = new byte[GeminiUsage.MaxBody + 4096];
            int n = 0, headerEnd = -1;
            while (n < buf.Length)
            {
                var k = await s.ReadAsync(buf.AsMemory(n, buf.Length - n));
                if (k <= 0) break;
                n += k;
                headerEnd = IndexOf(buf, n);
                if (headerEnd >= 0) break;
            }
            if (headerEnd < 0) return (null, null, null, null);
            var head = Encoding.ASCII.GetString(buf, 0, headerEnd).Split("\r\n");
            var first = head[0].Split(' ');
            if (first.Length < 2) return (null, null, null, null);
            var headers = new System.Collections.Generic.Dictionary<string, string>();
            foreach (var line in head[1..])
            {
                var i = line.IndexOf(':');
                if (i > 0) headers[line[..i].Trim().ToLowerInvariant()] = line[(i + 1)..].Trim();
            }
            int len = headers.TryGetValue("content-length", out var cl) && int.TryParse(cl, out var l) ? l : 0;
            if (len < 0 || len > GeminiUsage.MaxBody) return (null, null, null, null);
            int start = headerEnd + 4;
            while (n - start < len)
            {
                var k = await s.ReadAsync(buf.AsMemory(n, buf.Length - n));
                if (k <= 0) break;
                n += k;
            }
            var body = Encoding.UTF8.GetString(buf, start, Math.Min(len, n - start));
            return (first[0].ToUpperInvariant(), first[1].Split('?')[0], headers, body);
        }

        static int IndexOf(byte[] b, int n)
        {
            for (int i = 3; i < n; i++) if (b[i - 3] == '\r' && b[i - 2] == '\n' && b[i - 1] == '\r' && b[i] == '\n') return i - 3;
            return -1;
        }

        static async Task Reply(NetworkStream s, int code, string json, string extOrigin = null)
        {
            var body = Encoding.UTF8.GetBytes(json);
            var cors = string.IsNullOrEmpty(extOrigin) ? "" :
                $"Access-Control-Allow-Origin: {extOrigin}\r\nAccess-Control-Allow-Methods: GET, POST\r\nAccess-Control-Allow-Headers: Content-Type, {GeminiUsage.SecretHeader}\r\nAccess-Control-Allow-Private-Network: true\r\n";
            var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {code} {(code is 200 or 204 ? "OK" : "Hata")}\r\n{cors}Content-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
            await s.WriteAsync(head); await s.WriteAsync(body);
        }

        static string LoadSecret()
        {
            try
            {
                if (File.Exists(SecretPath) && File.ReadAllText(SecretPath).Trim() is { Length: 64 } v) return v;
                Directory.CreateDirectory(Services.DataDir);
                var s = GeminiUsage.NewSecret();
                File.WriteAllText(SecretPath, s);
                return s;
            }
            catch (Exception) { return null; }
        }

        /// <summary>Eklenti klasörü (dock exe'sinin yanındaki klasörün kardeşi) varsa gizli değeri oraya da yazar; eklenti kendi dosyasından okur.</summary>
        void WriteExtensionSecret()
        {
            try
            {
                var dir = ExtensionFolder;
                if (_secret == null || !File.Exists(Path.Combine(dir, "manifest.json"))) return;
                var f = Path.Combine(dir, GeminiUsage.ExtensionSecretFile);
                var json = JsonSerializer.Serialize(new { port = GeminiUsage.Port, gizli = _secret });
                if (!File.Exists(f) || File.ReadAllText(f) != json) File.WriteAllText(f, json);
            }
            catch (Exception) { }
        }

        static void Log(string msg)
        {
            try { File.AppendAllText(Path.Combine(Services.DataDir, "hata.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} Gemini: {msg}\n"); }
            catch (Exception) { }
        }

        public void Dispose() { _cts.Cancel(); try { _listener?.Stop(); } catch (Exception) { } }
    }
}

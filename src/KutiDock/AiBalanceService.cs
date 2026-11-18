using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KutiDock.Core;

namespace KutiDock
{
    /// <summary>
    /// 1.6.15: AllModels / OpenRouter / Kimi bakiyeleri, yalnızca resmi API'ler ve Kutay'ın API anahtarlarıyla.
    /// Tarayıcı çerezi, oturum belirteci ya da tarayıcı veritabanı okunmaz; sayfa kazınmaz.
    /// Anahtarlar %APPDATA%\KutiDock\api-anahtarlari.dat'ta DPAPI (CurrentUser) ile şifreli durur; log'a ve ekrana yazılmaz.
    /// Aynı klasöre düz metin api-anahtarlari.yeni.json konursa dock açılışta onu şifreli dosyaya katar ve düz metni siler.
    /// </summary>
    internal sealed class AiBalanceService
    {
        static readonly string KeyFile = Path.Combine(Services.DataDir, "api-anahtarlari.dat");
        static readonly string ImportFile = Path.Combine(Services.DataDir, "api-anahtarlari.yeni.json");
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("KutiDock.ApiAnahtarlari.v1");
        static readonly HttpClient Http = new(new HttpClientHandler { UseCookies = false }) { Timeout = TimeSpan.FromSeconds(20) };

        readonly Dictionary<AiProvider, AiBalance> _last = new();
        Dictionary<string, string> _keys = new(StringComparer.OrdinalIgnoreCase);
        DateTimeOffset _at = DateTimeOffset.MinValue;
        int _busy;
        public event Action Changed;

        public AiBalanceService()
        {
            _keys = Load();
            ImportPlain();
        }

        public bool HasKey(AiProvider p) => AiBalances.KeyName(p) is { } n && _keys.ContainsKey(n);

        public AiBalance Get(AiProvider p)
        {
            lock (_last) return _last.TryGetValue(p, out var b) ? b : null;
        }

        /// <summary>Ayarlar'dan: boş değer = dokunma, "-" = sil.</summary>
        public void SetKeys(IDictionary<string, string> incoming)
        {
            var next = new Dictionary<string, string>(_keys, StringComparer.OrdinalIgnoreCase);
            foreach (var kv in incoming)
            {
                var v = kv.Value?.Trim();
                if (string.IsNullOrEmpty(v)) continue;
                if (v == "-") next.Remove(kv.Key); else next[kv.Key] = v;
            }
            _keys = next; Save(next); Log("anahtarlar güncellendi (" + string.Join(", ", next.Keys) + ")");
            _ = Refresh(force: true);
        }

        /// <summary>Services.Tick'ten (10 dk'da bir) ve panel açılınca (force; en çok 30 sn'de bir).</summary>
        public async Task Refresh(bool force)
        {
            var now = DateTimeOffset.Now;
            if (now - _at < (force ? AiBalances.PanelMinGap : AiBalances.RefreshEvery)) return;
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;
            _at = now;
            try
            {
                await Task.WhenAll(Fetch(AiProvider.AllModels), Fetch(AiProvider.OpenRouter), Fetch(AiProvider.Kimi));
                Set(new AiBalance(AiProvider.Gemini, null, null, AiBalances.GeminiNote));
            }
            finally { Interlocked.Exchange(ref _busy, 0); Changed?.Invoke(); }
        }

        async Task Fetch(AiProvider p)
        {
            var name = AiBalances.Name(p);
            if (!_keys.TryGetValue(AiBalances.KeyName(p), out var key)) { Set(new AiBalance(p, null, null, "API anahtarı yok")); return; }
            try
            {
                double? v; string note = null;
                switch (p)
                {
                    case AiProvider.Kimi:
                        v = AiBalances.ParseKimi(await GetOk(AiBalances.KimiBalanceUrl, key, name));
                        break;
                    case AiProvider.AllModels:
                        v = AiBalances.ParseAllModels(await GetOk(AiBalances.AllModelsBalanceUrl, key, name));
                        break;
                    default:
                        var (code, body) = await Get(AiBalances.OpenRouterCreditsUrl, key);
                        if (code == HttpStatusCode.OK) v = AiBalances.ParseOpenRouterCredits(body);
                        else if (code is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
                        {
                            // Normal (inference) anahtarı /credits'i okuyamaz; yalnızca bu anahtarın kalan limiti okunur.
                            var (c2, b2) = await Get(AiBalances.OpenRouterKeyUrl, key);
                            if (c2 != HttpStatusCode.OK) throw new HttpRequestException($"{name}: HTTP {(int)c2}");
                            v = AiBalances.ParseOpenRouterKey(b2);
                            note = v == null ? "toplam bakiye için yönetim (management) anahtarı gerekli" : "anahtar limiti";
                        }
                        else throw new HttpRequestException($"{name}: HTTP {(int)code}");
                        break;
                }
                Set(new AiBalance(p, v, DateTimeOffset.Now, v == null && note == null ? "yanıtta bakiye yok" : note));
                Log($"{name}: güncellendi");
            }
            catch (Exception ex)
            {
                var prev = Get(p);
                Set(new AiBalance(p, prev?.Value, prev?.UpdatedAt, "güncellenemedi"));
                Log($"{name}: güncellenemedi ({ex.GetType().Name}{(ex is HttpRequestException h ? ": " + h.Message : "")})");
            }
        }

        static async Task<(HttpStatusCode, string)> Get(string url, string key)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using var res = await Http.SendAsync(req);
            return (res.StatusCode, await res.Content.ReadAsStringAsync());
        }

        static async Task<string> GetOk(string url, string key, string name)
        {
            var (code, body) = await Get(url, key);
            if (code != HttpStatusCode.OK) throw new HttpRequestException($"{name}: HTTP {(int)code}");
            return body;
        }

        void Set(AiBalance b) { lock (_last) _last[b.Provider] = b; }

        static Dictionary<string, string> Load()
        {
            try
            {
                if (File.Exists(KeyFile))
                    return AiBalances.ParseKeys(Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(KeyFile), Entropy, DataProtectionScope.CurrentUser)));
            }
            catch (Exception ex) { Log("anahtar dosyası okunamadı (" + ex.GetType().Name + ")"); }
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        static void Save(Dictionary<string, string> keys)
        {
            Directory.CreateDirectory(Services.DataDir);
            var tmp = KeyFile + ".tmp";
            File.WriteAllBytes(tmp, ProtectedData.Protect(Encoding.UTF8.GetBytes(AiBalances.SerializeKeys(keys)), Entropy, DataProtectionScope.CurrentUser));
            File.Move(tmp, KeyFile, true);
        }

        void ImportPlain()
        {
            if (!File.Exists(ImportFile)) return;
            try
            {
                var incoming = AiBalances.ParseKeys(File.ReadAllText(ImportFile));
                _keys = AiBalances.Merge(_keys, incoming);
                Save(_keys);
                Log("anahtarlar içe aktarıldı (" + string.Join(", ", incoming.Keys) + ")");
            }
            catch (Exception ex) { Log("içe aktarma başarısız (" + ex.GetType().Name + ")"); }
            finally
            {
                try
                {
                    var len = new FileInfo(ImportFile).Length; // düz metni silmeden önce üzerine yaz
                    File.WriteAllBytes(ImportFile, new byte[len]);
                    File.Delete(ImportFile);
                }
                catch (Exception) { }
            }
        }

        static void Log(string msg)
        {
            try { File.AppendAllText(Path.Combine(Services.DataDir, "hata.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} AI bakiye: {msg}\n"); }
            catch (Exception) { }
        }
    }
}

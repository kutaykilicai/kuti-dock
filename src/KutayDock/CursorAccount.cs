using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KutayDock.Core;

namespace KutayDock
{
    /// <summary>
    /// Kutay Dock'un KENDİ Cursor girişi. Belirteç yalnızca bu akıştan alınır (Cursor / Grok Bot uygulamalarının
    /// belirteçlerine, çerezlerine ya da yerel veritabanına dokunulmaz), DPAPI (CurrentUser) ile şifreli saklanır
    /// ve hiçbir yere (hata.log dahil) yazılmaz. Uç noktalar Cursor'un BELGELENMEMİŞ dahili API'leridir.
    /// </summary>
    internal sealed class CursorAccount
    {
        static readonly string TokenFile = Path.Combine(Services.DataDir, "cursor-oturum.bin");
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("KutayDock.CursorOturum.v1");
        static readonly HttpClient Http = new(new HttpClientHandler { UseCookies = false }) { Timeout = TimeSpan.FromSeconds(20) };

        string _token;
        CancellationTokenSource _login;
        int _busy;
        DateTimeOffset _sandTried = DateTimeOffset.MinValue;

        public bool SignedIn => _token != null;
        public bool LoggingIn => _login != null;
        /// <summary>Son istek 401/403 döndü: Ayarlar'dan yeniden giriş gerekir.</summary>
        public bool NeedsRelogin { get; private set; }
        public SandUsage Sand { get; private set; }
        public DateTimeOffset? SandAt { get; private set; }
        public UsageSummary Summary { get; private set; }
        public DateTimeOffset? SummaryAt { get; private set; }
        public event Action Changed;

        public CursorAccount()
        {
            try
            {
                if (File.Exists(TokenFile))
                    _token = Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(TokenFile), Entropy, DataProtectionScope.CurrentUser));
            }
            catch (Exception) { _token = null; }
        }

        /// <summary>Yalnızca Ayarlar'daki "Cursor ile giriş yap" düğmesinden çağrılır: tarayıcıda Cursor giriş sayfasını açar ve onayı bekler.</summary>
        public async Task<string> LoginAsync()
        {
            if (_login != null) return "Giriş zaten bekliyor; tarayıcıda onayla.";
            var cts = _login = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            Changed?.Invoke();
            try
            {
                var verifier = CursorApi.NewVerifier();
                var uuid = Guid.NewGuid();
                Process.Start(new ProcessStartInfo(CursorApi.LoginUrl(CursorApi.Challenge(verifier), uuid)) { UseShellExecute = true });
                while (!cts.IsCancellationRequested)
                {
                    await Task.Delay(2000, cts.Token);
                    using var res = await Http.GetAsync(CursorApi.PollUrl(uuid, verifier), cts.Token);
                    if (res.StatusCode == HttpStatusCode.NotFound) continue; // henüz onaylanmadı
                    if (!res.IsSuccessStatusCode) continue;
                    var token = CursorApi.ParsePoll(await res.Content.ReadAsStringAsync(cts.Token));
                    if (token == null) continue;
                    Save(token);
                    _ = RefreshSand(force: true);
                    return "Giriş tamam.";
                }
                return "Giriş zaman aşımına uğradı (5 dk). Tekrar dene.";
            }
            catch (OperationCanceledException) { return "Giriş iptal edildi ya da zaman aşımına uğradı."; }
            catch (Exception) { return "Giriş başarısız (ağ hatası). Tekrar dene."; }
            finally { _login = null; Changed?.Invoke(); }
        }

        public void CancelLogin() => _login?.Cancel();

        public void Logout()
        {
            CancelLogin();
            _token = null; Sand = null; SandAt = null; Summary = null; SummaryAt = null; NeedsRelogin = false;
            _sandTried = DateTimeOffset.MinValue;
            try { if (File.Exists(TokenFile)) File.Delete(TokenFile); } catch (Exception) { }
            Changed?.Invoke();
        }

        void Save(string token)
        {
            _token = token; NeedsRelogin = false; _sandTried = DateTimeOffset.MinValue; SummaryAt = null;
            Directory.CreateDirectory(Services.DataDir);
            File.WriteAllBytes(TokenFile, ProtectedData.Protect(Encoding.UTF8.GetBytes(token), Entropy, DataProtectionScope.CurrentUser));
            Changed?.Invoke();
        }

        /// <summary>Grok Bot kullanımı; en sık 5 dakikada bir.</summary>
        public async Task RefreshSand(bool force = false)
        {
            if (!SignedIn || NeedsRelogin) return;
            if (!force && DateTimeOffset.Now - _sandTried < TimeSpan.FromMinutes(5)) return;
            _sandTried = DateTimeOffset.Now; // hata olsa da 5 dk bekle
            var json = await Request(CursorApi.SandUrl, post: true);
            if (json != null && CursorApi.ParseSand(json) is { } s) { Sand = s; SandAt = DateTimeOffset.Now; }
            Changed?.Invoke();
        }

        /// <summary>Cursor kullanım özeti; panel açılınca, en sık dakikada bir.</summary>
        public async Task RefreshSummary()
        {
            if (!SignedIn || NeedsRelogin) return;
            if (SummaryAt is { } t && DateTimeOffset.Now - t < TimeSpan.FromMinutes(1)) return;
            SummaryAt = DateTimeOffset.Now;
            var json = await Request(CursorApi.SummaryUrl, post: false);
            if (json != null && CursorApi.ParseSummary(json) is { } s) Summary = s;
            Changed?.Invoke();
        }

        /// <summary>İstek; her hata sessizce null döner (ayrıntı hiçbir yere yazılmaz).</summary>
        async Task<string> Request(string url, bool post)
        {
            if (Interlocked.Exchange(ref _busy, 1) == 1) return null;
            try
            {
                var cookie = _token == null ? null : CursorApi.SessionCookie(_token);
                if (cookie == null) { NeedsRelogin = true; return null; }
                foreach (var method in post ? new[] { HttpMethod.Post, HttpMethod.Get } : new[] { HttpMethod.Get })
                {
                    using var req = new HttpRequestMessage(method, url);
                    req.Headers.TryAddWithoutValidation("Cookie", cookie);
                    req.Headers.TryAddWithoutValidation("Origin", CursorApi.Origin);
                    req.Headers.TryAddWithoutValidation("Referer", CursorApi.Origin + "/dashboard");
                    if (method == HttpMethod.Post) req.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                    using var res = await Http.SendAsync(req);
                    if (res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) { NeedsRelogin = true; return null; }
                    if (res.StatusCode == HttpStatusCode.MethodNotAllowed) continue; // POST kabul edilmezse GET dene
                    return res.IsSuccessStatusCode ? await res.Content.ReadAsStringAsync() : null;
                }
                return null;
            }
            catch (Exception) { return null; }
            finally { Interlocked.Exchange(ref _busy, 0); }
        }
    }
}

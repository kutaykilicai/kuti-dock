using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace KutiDock.Core
{
    /// <summary>Limit panelindeki ek sağlayıcılar (1.6.15). Yalnızca resmi API'ler ve Kutay'ın API anahtarları kullanılır.</summary>
    public enum AiProvider { AllModels, OpenRouter, Kimi, Gemini }

    /// <summary>Bir sağlayıcının son okunan durumu. Value null = değer yok (anahtar yok, API yok ya da okunamadı).</summary>
    public sealed record AiBalance(AiProvider Provider, double? Value, DateTimeOffset? UpdatedAt, string Note);

    public static class AiBalances
    {
        public static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(10);
        /// <summary>Panel her açılışta yeniler; art arda açılışlarda en çok 30 sn'de bir.</summary>
        public static readonly TimeSpan PanelMinGap = TimeSpan.FromSeconds(30);

        public static readonly AiProvider[] Order = { AiProvider.AllModels, AiProvider.OpenRouter, AiProvider.Kimi, AiProvider.Gemini };

        public static string Name(AiProvider p) => p switch
        {
            AiProvider.AllModels => "AllModels",
            AiProvider.OpenRouter => "OpenRouter",
            AiProvider.Kimi => "Kimi (Moonshot)",
            _ => "Gemini",
        };

        /// <summary>Kullanıcının bakiyeyi kendi gördüğü sayfa (panelde "Sayfayı aç").</summary>
        public static string PageUrl(AiProvider p) => p switch
        {
            AiProvider.AllModels => "https://dash.allmodels.io/overview",
            AiProvider.OpenRouter => "https://openrouter.ai/settings/credits",
            AiProvider.Kimi => "https://platform.kimi.ai/console/account",
            _ => "https://gemini.google.com/usage",
        };

        /// <summary>Anahtar dosyasındaki ad. Gemini uygulama limiti için resmi API yok, anahtarı da yok.</summary>
        public static string KeyName(AiProvider p) => p switch
        {
            AiProvider.AllModels => "allmodels",
            AiProvider.OpenRouter => "openrouter",
            AiProvider.Kimi => "moonshot",
            _ => null,
        };

        public const string KimiBalanceUrl = "https://api.moonshot.ai/v1/users/me/balance";
        public const string AllModelsBalanceUrl = "https://api.allmodels.io/account/balance";
        /// <summary>Hesabın toplam kalan kredisi (sayfadaki "Total available"); OpenRouter'da management anahtarı ister.</summary>
        public const string OpenRouterCreditsUrl = "https://openrouter.ai/api/v1/credits";
        /// <summary>Normal anahtarla: yalnızca bu anahtarın limit_remaining değeri (anahtara limit konmadıysa null).</summary>
        public const string OpenRouterKeyUrl = "https://openrouter.ai/api/v1/key";

        public const string GeminiNote = "Resmi API yok · 5 saatte bir yenilenir, haftalık limit var";

        static JsonElement Root(string json) => JsonDocument.Parse(json).RootElement;

        static double? Num(JsonElement o, string name) =>
            o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

        /// <summary>Moonshot: {"data":{"available_balance":4.94,...}}</summary>
        public static double? ParseKimi(string json)
        {
            var r = Root(json);
            return r.TryGetProperty("data", out var d) ? Num(d, "available_balance") : null;
        }

        /// <summary>AllModels /account/balance: paid_balance_usd; yoksa spendable_paid_credits / 1e6 (1 kredi = $0.000001).</summary>
        public static double? ParseAllModels(string json)
        {
            var r = Root(json);
            if (Num(r, "paid_balance_usd") is { } usd) return usd;
            return Num(r, "spendable_paid_credits") is { } c ? c / 1_000_000d : null;
        }

        /// <summary>OpenRouter /credits: total_credits - total_usage (sayfadaki "Total available").</summary>
        public static double? ParseOpenRouterCredits(string json)
        {
            var r = Root(json);
            if (!r.TryGetProperty("data", out var d)) return null;
            return Num(d, "total_credits") is { } t && Num(d, "total_usage") is { } u ? Math.Round(t - u, 6) : null;
        }

        /// <summary>OpenRouter /key: data.limit_remaining (limitsiz anahtarda null).</summary>
        public static double? ParseOpenRouterKey(string json)
        {
            var r = Root(json);
            return r.TryGetProperty("data", out var d) ? Num(d, "limit_remaining") : null;
        }

        public static string Money(double? v) => v is { } x ? "$" + x.ToString("0.00", CultureInfo.InvariantCulture) : "—";

        /// <summary>Alt satır: "güncellendi 13:42" / not.</summary>
        public static string Status(AiBalance b, DateTimeOffset now)
        {
            if (b == null) return "okunuyor…";
            var parts = new List<string>();
            if (b.UpdatedAt is { } at) parts.Add("güncellendi " + at.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(b.Note)) parts.Add(b.Note);
            return parts.Count == 0 ? "—" : string.Join(" · ", parts);
        }

        // ---- Anahtar dosyası (düz metin hali; uygulama DPAPI ile şifreler) ----

        /// <summary>{"openrouter":"...","allmodels":"...","moonshot":"..."}: bilinmeyen adlar ve boş değerler atılır.</summary>
        public static Dictionary<string, string> ParseKeys(string json)
        {
            var known = Order.Select(KeyName).Where(n => n != null).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var res = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var r = Root(json);
            if (r.ValueKind != JsonValueKind.Object) return res;
            foreach (var p in r.EnumerateObject())
                if (known.Contains(p.Name) && p.Value.ValueKind == JsonValueKind.String && p.Value.GetString()?.Trim() is { Length: > 0 } v)
                    res[p.Name.ToLowerInvariant()] = v;
            return res;
        }

        /// <summary>Yeni gelenler eskilerin üstüne yazar; eskide olup yenide olmayanlar kalır.</summary>
        public static Dictionary<string, string> Merge(IDictionary<string, string> old, IDictionary<string, string> incoming)
        {
            var res = new Dictionary<string, string>(old ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
            foreach (var kv in incoming ?? new Dictionary<string, string>()) res[kv.Key] = kv.Value;
            return res;
        }

        public static string SerializeKeys(IDictionary<string, string> keys) => JsonSerializer.Serialize(keys);
    }
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KutayDock.Core;

/// <summary>Grok Bot haftalık kullanımı (cursor.com/api/dashboard/get-sand-usage-status; "sand" = Grok Bot).</summary>
public sealed record SandUsage(bool Enabled, double? Percent, DateTimeOffset? ResetsAt);

/// <summary>Cursor kullanım özeti (cursor.com/api/usage-summary). Tutarlar dolar.</summary>
public sealed record UsageSummary(string? Membership, double? TotalPercent, double? AutoPercent, double? ApiPercent,
    double? UsedDollars, double? LimitDollars, bool OnDemandEnabled, double? OnDemandUsedDollars, double? OnDemandLimitDollars,
    DateTimeOffset? CycleEnd);

/// <summary>
/// Cursor hesabı: giriş adresleri ve yanıt ayrıştırma. DİKKAT: bu uç noktalar Cursor'un BELGELENMEMİŞ dahili
/// API'leridir (Kutay riski kabul ederek seçti). Alan adları, Nowdex 1.4.1 ikilisindeki dizelerden çıkarıldı;
/// yanıtın tam yapısı bilinmediği için alanlar JSON içinde adıyla (derinlik fark etmeksizin) aranır.
/// </summary>
public static class CursorApi
{
    public const string LoginBase = "https://cursor.com/loginDeepControl";
    public const string PollBase = "https://api2.cursor.sh/auth/poll";
    public const string SandUrl = "https://cursor.com/api/dashboard/get-sand-usage-status";
    public const string SummaryUrl = "https://cursor.com/api/usage-summary";
    public const string Origin = "https://cursor.com";

    // ---- Giriş (PKCE benzeri: doğrulayıcı yerelde kalır, tarayıcıya yalnızca özeti gider) ----
    public static string NewVerifier() => B64Url(RandomNumberGenerator.GetBytes(32));
    public static string Challenge(string verifier) => B64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    public static string LoginUrl(string challenge, Guid uuid) =>
        $"{LoginBase}?challenge={Uri.EscapeDataString(challenge)}&uuid={uuid}&mode=login";
    public static string PollUrl(Guid uuid, string verifier) =>
        $"{PollBase}?uuid={uuid}&verifier={Uri.EscapeDataString(verifier)}";

    static string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Yoklama yanıtından erişim belirteci (henüz onaylanmadıysa null).</summary>
    public static string? ParsePoll(string json)
    {
        try
        {
            using var d = JsonDocument.Parse(json);
            return Find(d.RootElement, "accessToken") is { ValueKind: JsonValueKind.String } t && t.GetString() is { Length: > 0 } s ? s : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// cursor.com çerezi: "WorkosCursorSessionToken=&lt;kullanıcı&gt;%3A%3A&lt;belirteç&gt;". Kullanıcı kimliği JWT'nin
    /// "sub" alanının '|' sonrası kısmıdır. Belirteç çözülemezse null.
    /// </summary>
    public static string? SessionCookie(string accessToken)
    {
        var parts = accessToken.Split('.');
        if (parts.Length < 2) return null;
        try
        {
            var p = parts[1].Replace('-', '+').Replace('_', '/');
            p = p.PadRight(p.Length + (4 - p.Length % 4) % 4, '=');
            using var d = JsonDocument.Parse(Convert.FromBase64String(p));
            if (!d.RootElement.TryGetProperty("sub", out var sub) || sub.GetString() is not { Length: > 0 } s) return null;
            var user = s.Contains('|') ? s[(s.LastIndexOf('|') + 1)..] : s;
            return $"WorkosCursorSessionToken={user}%3A%3A{accessToken}";
        }
        catch (Exception e) when (e is FormatException or JsonException or ArgumentException) { return null; }
    }

    // ---- Yanıtlar ----
    public static SandUsage? ParseSand(string json)
    {
        try
        {
            using var d = JsonDocument.Parse(json);
            var r = d.RootElement;
            bool enabled = Bool(r, "isEnabled") ?? true;
            if (Bool(r, "includedLimitZero") == true || Bool(r, "hasNonZeroIncludedLimit") == false) enabled = false;
            double? pct = Num(r, "usagePercent") ?? Num(r, "totalPercentUsed");
            return new SandUsage(enabled, pct is { } v ? Math.Clamp(v, 0, 100) : null, Time(r, "nextResetTimestampUtc"));
        }
        catch (JsonException) { return null; }
    }

    public static UsageSummary? ParseSummary(string json)
    {
        try
        {
            using var d = JsonDocument.Parse(json);
            var r = d.RootElement;
            var plan = Find(r, "plan") is { ValueKind: JsonValueKind.Object } p ? p : r;
            var od = Find(r, "onDemand") is { ValueKind: JsonValueKind.Object } o ? o : default;
            double? used = Num(plan, "used"), limit = Num(plan, "limit");
            double? total = Num(plan, "totalPercentUsed") ?? (used is { } u && limit is > 0 ? u / limit.Value * 100 : null);
            return new UsageSummary(
                Find(r, "membershipType")?.GetString(), total, Num(plan, "autoPercentUsed"), Num(plan, "apiPercentUsed"),
                used / 100, limit / 100, // sent → dolar
                od.ValueKind == JsonValueKind.Object && (Bool(od, "enabled") ?? false),
                od.ValueKind == JsonValueKind.Object ? Num(od, "used") / 100 : null,
                od.ValueKind == JsonValueKind.Object ? Num(od, "limit") / 100 : null,
                Time(r, "billingCycleEnd"));
        }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// Yanıtın YAPISI: alan adları ve tipleri (ör. "{a:num,b:{c:str}}"), hiçbir değer içermez. Teşhis günlüğü için.
    /// </summary>
    public static string Shape(string json)
    {
        try { using var d = JsonDocument.Parse(json); return Shape(d.RootElement, 0); }
        catch (JsonException) { return "json değil"; }
    }

    static string Shape(JsonElement e, int depth) => e.ValueKind switch
    {
        JsonValueKind.Object when depth >= 5 => "{…}",
        JsonValueKind.Object => "{" + string.Join(",", e.EnumerateObject().Take(40).Select(p => p.Name + ":" + Shape(p.Value, depth + 1))) + "}",
        JsonValueKind.Array => e.GetArrayLength() == 0 ? "[]" : "[" + Shape(e[0], depth + 1) + "]",
        JsonValueKind.String => "str",
        JsonValueKind.Number => "num",
        JsonValueKind.True or JsonValueKind.False => "bool",
        _ => "null",
    };

    /// <summary>Adıyla ilk eşleşen alanı (genişlik öncelikli, her derinlikte) bulur.</summary>
    static JsonElement? Find(JsonElement root, string name)
    {
        var q = new Queue<JsonElement>(); q.Enqueue(root);
        while (q.Count > 0)
        {
            var e = q.Dequeue();
            if (e.ValueKind == JsonValueKind.Object)
                foreach (var p in e.EnumerateObject())
                {
                    if (p.NameEquals(name)) return p.Value;
                    q.Enqueue(p.Value);
                }
            else if (e.ValueKind == JsonValueKind.Array)
                foreach (var x in e.EnumerateArray()) q.Enqueue(x);
        }
        return null;
    }

    static double? Num(JsonElement r, string name) => Find(r, name) switch
    {
        { ValueKind: JsonValueKind.Number } n => n.GetDouble(),
        { ValueKind: JsonValueKind.String } s when double.TryParse(s.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) => v,
        _ => null,
    };

    static bool? Bool(JsonElement r, string name) => Find(r, name) switch
    {
        { ValueKind: JsonValueKind.True } => true,
        { ValueKind: JsonValueKind.False } => false,
        _ => null,
    };

    /// <summary>ISO-8601 metin ya da Unix zamanı (saniye veya milisaniye; sayı ya da metin).</summary>
    static DateTimeOffset? Time(JsonElement r, string name)
    {
        var e = Find(r, name);
        if (e is not { } v) return null;
        string? s = v.ValueKind == JsonValueKind.String ? v.GetString() : v.ValueKind == JsonValueKind.Number ? v.GetRawText() : null;
        if (string.IsNullOrEmpty(s)) return null;
        if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            return n > 100_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(n) : DateTimeOffset.FromUnixTimeSeconds(n);
        return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t : null;
    }
}

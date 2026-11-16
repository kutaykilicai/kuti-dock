using System.Text.Json;

namespace KutayDock.Core;

/// <summary>Limit kartı için metin üretimi ve Cursor CLI çıktısı okuma.</summary>
public static class Limits
{
    /// <summary>Kartta görünen yüzde (0..1). Değer yoksa ya da sıfırlanma zamanı geçtiyse null.</summary>
    public static double? Fraction(ManualLimit? l, DateTimeOffset now)
    {
        if (l?.Percent is not { } p) return null;
        if (l.ResetsAt is { } r && r <= now) return null;
        return Format.Clamp01(p / 100.0);
    }

    /// <summary>"Sal 13:07" gibi sıfırlanma etiketi.</summary>
    public static string ResetLabel(ManualLimit? l, DateTimeOffset now)
    {
        if (l?.ResetsAt is not { } r) return "";
        if (r <= now) return "sıfırlandı";
        var local = r.ToLocalTime();
        return r - now < TimeSpan.FromDays(1) ? Format.Duration(r - now) : local.ToString("ddd HH:mm", Format.Tr);
    }

    /// <summary>"elle · 2s önce" gibi kaynak/güncellik etiketi.</summary>
    public static string Freshness(ManualLimit? l, DateTimeOffset now)
    {
        if (l?.Percent == null) return "kaynak yok · elle gir";
        if (l.UpdatedAt is not { } u) return "elle girildi";
        return $"elle · {Format.Duration(now - u)} önce";
    }

    /// <summary>Cursor hesabından okunan değer için: "Cursor · 2dk önce".</summary>
    public static string LiveFreshness(DateTimeOffset fetched, DateTimeOffset now) => $"Cursor · {Format.Duration(now - fetched)} önce";

    /// <summary>
    /// "agent about --format json" çıktısından (Cursor CLI'nin belgelenmiş komutu) yalnızca plan adını okur.
    /// E-posta gibi diğer alanlar okunmaz ve saklanmaz.
    /// </summary>
    public static string? ParseCursorTier(string json)
    {
        try
        {
            int start = json.IndexOf('{');
            if (start < 0) return null;
            using var doc = JsonDocument.Parse(json[start..]);
            return doc.RootElement.TryGetProperty("subscriptionTier", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        }
        catch (JsonException) { return null; }
    }
}

public enum ShareXCapture { Bolge, Pencere, TamEkran, SonBolge, Monitor }

/// <summary>ShareX komut satırı (getsharex.com/docs/command-line-arguments: her hotkey eylemi bir argümandır).</summary>
public static class ShareX
{
    public static string Args(ShareXCapture c) => c switch
    {
        ShareXCapture.Pencere => "-ActiveWindow",
        ShareXCapture.TamEkran => "-PrintScreen",
        ShareXCapture.SonBolge => "-LastRegion",
        ShareXCapture.Monitor => "-ActiveMonitor",
        _ => "-RectangleRegion",
    };

    /// <summary>Ayardaki yol, sonra bilinen kurulum yerleri. Bulunamazsa null.</summary>
    public static string? Find(string? configured, IEnumerable<string> candidates, Func<string, bool> exists)
    {
        if (!string.IsNullOrWhiteSpace(configured) && exists(configured.Trim('"', ' '))) return configured.Trim('"', ' ');
        return candidates.FirstOrDefault(c => !string.IsNullOrEmpty(c) && exists(c));
    }
}

using System.Text.Json;

namespace KutiDock.Core;

/// <summary>Bir ekran yolu (CCD): ad, aygıt yolu, HDMI mi, Windows düzeninde etkin mi.</summary>
public readonly record struct TvPath(string FriendlyName, string DevicePath, bool Hdmi, bool Active);

/// <summary>TvEkranGecis'in ayarlar.json'undan TV'yi tanıma ipuçları (ad ve EDID anahtarı, ör. "LG TV" / "GSM0001").</summary>
public sealed record TvHint(string? FriendlyName, string? DevicePathContains)
{
    public static readonly TvHint None = new(null, null);

    /// <summary>ayarlar.json metni (camelCase). Okunamazsa ipucu yok.</summary>
    public static TvHint Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return None;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string? Str(string name) =>
                root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            return new TvHint(Str("friendlyName"), Str("devicePathContains"));
        }
        catch (JsonException) { return None; }
    }
}

/// <summary>
/// 1.6.12: dock'taki TV Aç-Kapat simgesi için TV'nin Windows ekran düzeninde açık mı kapalı mı olduğu.
/// TvEkranGecis ile aynı tanıma sırası: EDID anahtarı (aygıt yolu), sonra ad, en son adında "TV" geçen tek ekran.
/// </summary>
public static class TvStatus
{
    public const string ToolExe = "TvEkranGecis.exe";

    /// <summary>Kısayolun hedefi TV Ekran Geçiş aracı mı.</summary>
    public static bool IsTvTool(string? target) =>
        !string.IsNullOrWhiteSpace(target) && FileName(target).Equals(ToolExe, StringComparison.OrdinalIgnoreCase);

    // Windows yolu her platformda (testler Linux'ta da koşar): hem \ hem / ayırıcı
    static string FileName(string path)
    {
        var t = path.Trim().Trim('"');
        int i = t.LastIndexOfAny(new[] { '\\', '/' });
        return i >= 0 ? t[(i + 1)..] : t;
    }

    /// <summary>
    /// true: TV etkin, false: TV bağlı ama düzenden çıkarılmış, null: TV tanınamadı.
    /// <paramref name="paths"/> hem etkin hem etkin olmayan yolları içerebilir; aynı ekranın etkin yolu varsa açık sayılır.
    /// </summary>
    public static bool? IsOn(IEnumerable<TvPath> paths, TvHint hint)
    {
        var list = paths.Where(p => !string.IsNullOrWhiteSpace(p.FriendlyName) || !string.IsNullOrWhiteSpace(p.DevicePath)).ToList();
        List<TvPath> tv = new();
        if (!string.IsNullOrWhiteSpace(hint.DevicePathContains))
            tv = list.Where(p => p.DevicePath.Contains(hint.DevicePathContains, StringComparison.OrdinalIgnoreCase)).ToList();
        if (tv.Count == 0 && !string.IsNullOrWhiteSpace(hint.FriendlyName))
            tv = list.Where(p => p.FriendlyName.Equals(hint.FriendlyName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (tv.Count == 0)
        {
            var named = list.Where(p => p.FriendlyName.Contains("TV", StringComparison.OrdinalIgnoreCase)).ToList();
            var ids = named.Select(Identity).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (ids.Count > 1)
            {
                named = named.Where(p => p.Hdmi).ToList();
                ids = named.Select(Identity).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
            if (ids.Count == 1) tv = named;
        }
        if (tv.Count == 0) return null;
        return tv.Any(p => p.Active);
    }

    static string Identity(TvPath p) => string.IsNullOrWhiteSpace(p.DevicePath) ? p.FriendlyName : p.DevicePath;

    /// <summary>Simgenin ipucu metni.</summary>
    public static string Tooltip(bool? on) => on switch
    {
        true => "TV: Açık",
        false => "TV: Kapalı",
        _ => "TV: Durum okunamadı",
    };

    /// <summary>Simge altındaki küçük rozet (yalnızca kapalıyken).</summary>
    public static string? Badge(bool? on) => on == false ? "kapalı" : null;

    /// <summary>TV simgesine tıklandıktan sonra durumun yeniden okunacağı gecikmeler (ms): düzen değişikliği birkaç saniye sürebilir.</summary>
    public static readonly int[] RecheckAfterClickMs = { 700, 1800, 4000 };
}

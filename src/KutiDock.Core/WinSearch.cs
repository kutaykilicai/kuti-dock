namespace KutiDock.Core;

public enum HitKind { App, Setting, File }

/// <summary>Windows araması gibi sade sonuç: ad, alt satır, açılacak hedef.</summary>
public sealed record QuickHit(HitKind Kind, string Name, string Detail, string Target);

public static class WinSearch
{
    /// <summary>Sık kullanılan Ayarlar sayfaları (ms-settings: adresleri belgelenmiştir).</summary>
    public static readonly (string Name, string Uri)[] SettingsPages =
    {
        ("Ekran", "ms-settings:display"), ("Ses", "ms-settings:sound"), ("Bluetooth ve cihazlar", "ms-settings:bluetooth"),
        ("Wi-Fi", "ms-settings:network-wifi"), ("Ağ ve İnternet", "ms-settings:network"), ("VPN", "ms-settings:network-vpn"),
        ("Bildirimler", "ms-settings:notifications"), ("Güç ve pil", "ms-settings:powersleep"), ("Depolama", "ms-settings:storagesense"),
        ("Uygulamalar", "ms-settings:appsfeatures"), ("Varsayılan uygulamalar", "ms-settings:defaultapps"), ("Başlangıç uygulamaları", "ms-settings:startupapps"),
        ("Windows Update", "ms-settings:windowsupdate"), ("Kişiselleştirme", "ms-settings:personalization"), ("Arka plan", "ms-settings:personalization-background"),
        ("Renkler", "ms-settings:colors"), ("Görev çubuğu", "ms-settings:taskbar"), ("Fare", "ms-settings:mousetouchpad"),
        ("Klavye", "ms-settings:keyboard"), ("Yazıcılar", "ms-settings:printers"), ("Hesaplar", "ms-settings:yourinfo"),
        ("Tarih ve saat", "ms-settings:dateandtime"), ("Dil ve bölge", "ms-settings:regionlanguage"), ("Oyun modu", "ms-settings:gaming-gamemode"),
        ("Gizlilik ve güvenlik", "ms-settings:privacy"), ("Windows Güvenliği", "windowsdefender:"), ("Sistem hakkında", "ms-settings:about"),
        ("Odak", "ms-settings:quiethours"), ("Pano", "ms-settings:clipboard"), ("Gece ışığı", "ms-settings:nightlight"),
    };

    /// <summary>
    /// Eşleşme puanı (0 = eşleşmez): tam ad 100, ad başı 80, kelime başı 60, içerir 40. Türkçe büyük/küçük harf duyarsız.
    /// </summary>
    public static int Score(string name, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return 0;
        var n = name.ToLower(Format.Tr); var q = query.Trim().ToLower(Format.Tr);
        if (n == q) return 100;
        if (n.StartsWith(q)) return 80;
        if (n.Split(' ', '-', '_', '.').Any(w => w.StartsWith(q))) return 60;
        return n.Contains(q) ? 40 : 0;
    }

    /// <summary>Puanı en yüksek ilk <paramref name="take"/> öğe (eşitlikte kısa ad önce).</summary>
    public static List<T> Rank<T>(IEnumerable<T> items, Func<T, string> name, string query, int take) =>
        items.Select(i => (i, s: Score(name(i), query))).Where(x => x.s > 0)
            .OrderByDescending(x => x.s).ThenBy(x => name(x.i).Length).Take(take).Select(x => x.i).ToList();

    /// <summary>Windows Search dizini (SystemIndex) için güvenli dosya adı sorgusu: tek tırnak kaçırılır, % atılır.</summary>
    public static string IndexSql(string query, int top) =>
        $"SELECT TOP {top} System.ItemPathDisplay, System.ItemNameDisplay FROM SystemIndex " +
        $"WHERE SCOPE='file:' AND System.ItemNameDisplay LIKE '%{query.Replace("'", "''").Replace("%", "")}%' " +
        "ORDER BY System.DateModified DESC";
}

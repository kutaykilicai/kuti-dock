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

    /// <summary>1.6.10: klasik Denetim Masası öğeleri (Ayarlar'dan sonra, dosyalardan önce listelenir).</summary>
    public static readonly (string Name, string Target)[] ControlPanelItems =
    {
        ("Denetim Masası", "control.exe"), ("Programlar ve Özellikler", "appwiz.cpl"), ("Ağ Bağlantıları", "ncpa.cpl"),
        ("Aygıt Yöneticisi", "devmgmt.msc"), ("Ses (klasik)", "mmsys.cpl"), ("Güç Seçenekleri", "powercfg.cpl"),
        ("Sistem Özellikleri", "sysdm.cpl"), ("Disk Yönetimi", "diskmgmt.msc"), ("Görev Yöneticisi", "taskmgr.exe"),
        ("Hizmetler", "services.msc"), ("Kullanıcı Hesapları", "control.exe /name Microsoft.UserAccounts"),
    };

    static readonly char[] WordSeparators = { ' ', '-', '_', '.', '(', ')', '&' };

    /// <summary>
    /// Eşleşme puanı (0 = eşleşmez): tam ad 100, bir kelimesi tam eşit 90 ("word" → "Microsoft Word"), ad başı 80,
    /// kelime başı 60, baş harfler 50 ("vsc" → "Visual Studio Code"), içerir 40. Türkçe büyük/küçük harf duyarsız.
    /// </summary>
    public static int Score(string name, string query)
    {
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrEmpty(name)) return 0;
        var n = name.ToLower(Format.Tr); var q = query.Trim().ToLower(Format.Tr);
        if (n == q) return 100;
        var words = n.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);
        if (words.Contains(q)) return 90;
        if (n.StartsWith(q)) return 80;
        if (words.Any(w => w.StartsWith(q))) return 60;
        if (q.Length >= 2 && !q.Contains(' ') && string.Concat(words.Select(w => w[0])).StartsWith(q)) return 50;
        return n.Contains(q) ? 40 : 0;
    }

    /// <summary>1.6.10: sıralama hep Uygulamalar → Ayarlar/Denetim Masası → Dosyalar (Everything açık da olsa); Enter en üsttekini açar.</summary>
    public static List<QuickHit> Merge(IEnumerable<QuickHit> apps, IEnumerable<QuickHit> settings, IEnumerable<QuickHit> files) =>
        apps.Concat(settings).Concat(files).OrderBy(h => (int)h.Kind).ToList();

    /// <summary>Aramada hiç gösterilmeyen klasörler (program verisi, araç ve derleme klasörleri, sistem).</summary>
    public static readonly string[] ExcludedSegments =
    {
        "appdata", ".grok", "tools", "dist", "node_modules", ".git", "$recycle.bin", "windows", "program files", "program files (x86)",
        "programdata", "system volume information", "bin", "obj", ".vs", ".nuget", ".cache",
    };

    /// <summary>
    /// 1.6.10: dosya sonucu gösterilir mi: yalnızca kullanıcı klasörlerinin (Masaüstü, Belgeler, İndirilenler, Resimler) içinde
    /// ve hariç tutulan bir klasörün altında değilse.
    /// </summary>
    public static bool AllowedFile(string path, IEnumerable<string> roots)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var full = path.Replace('/', '\\').TrimEnd('\\');
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            var r = root.Replace('/', '\\').TrimEnd('\\');
            if (!full.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase)) continue;
            var rest = full.Substring(r.Length + 1).Split('\\');
            // son parça dosya adı; yalnızca klasörler hariç tutulur
            return !rest.Take(rest.Length - 1).Any(seg => ExcludedSegments.Contains(seg.ToLowerInvariant()));
        }
        return false;
    }

    /// <summary>1.6.10: Everything sorgusu kullanıcı klasörleriyle sınırlı: &lt;"C:\\…\\Desktop\\"|…&gt; sorgu (yol ayırıcılı terim tam yolda aranır).</summary>
    public static string EverythingQuery(string query, IEnumerable<string> roots)
    {
        var rs = (roots ?? Enumerable.Empty<string>()).Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => "\"" + r.Replace("\"", "").TrimEnd('\\') + "\\\"").ToList();
        var q = query.Trim();
        return rs.Count == 0 ? q : "<" + string.Join("|", rs) + "> " + q;
    }

    /// <summary>Puanı en yüksek ilk <paramref name="take"/> öğe (eşitlikte kısa ad önce).</summary>
    public static List<T> Rank<T>(IEnumerable<T> items, Func<T, string> name, string query, int take) =>
        items.Select(i => (i, s: Score(name(i), query))).Where(x => x.s > 0)
            .OrderByDescending(x => x.s).ThenBy(x => name(x.i).Length).Take(take).Select(x => x.i).ToList();

    /// <summary>
    /// Windows Search dizini (SystemIndex) için güvenli dosya adı sorgusu: tek tırnak kaçırılır, % atılır.
    /// 1.6.10: <paramref name="roots"/> verilirse yalnızca o klasörler (kullanıcı klasörleri) aranır; tüm disk değil.
    /// </summary>
    public static string IndexSql(string query, int top, IEnumerable<string> roots = null)
    {
        var scopes = (roots ?? Enumerable.Empty<string>()).Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => $"SCOPE='file:{r.Replace('\\', '/').TrimEnd('/').Replace("'", "''")}'").ToList();
        var scope = scopes.Count == 0 ? "SCOPE='file:'" : "(" + string.Join(" OR ", scopes) + ")";
        return $"SELECT TOP {top} System.ItemPathDisplay, System.ItemNameDisplay FROM SystemIndex " +
            $"WHERE {scope} AND System.ItemNameDisplay LIKE '%{query.Replace("'", "''").Replace("%", "")}%' " +
            "ORDER BY System.DateModified DESC";
    }
}

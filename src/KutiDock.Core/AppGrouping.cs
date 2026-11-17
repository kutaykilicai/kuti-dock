namespace KutiDock.Core;

/// <summary>
/// 1.6.6: pencereler dock simgelerine AppUserModelID ile bağlanır. Önceden yalnızca exe yoluna bakıldığı için Chrome'dan
/// kurulan uygulamalar (WhatsApp Web PWA vb., süreç yine chrome.exe) Chrome simgesinin penceresi sayılıyordu; Chrome'a
/// tıklamak WhatsApp'ı küçültüyor ya da öne getiriyordu. Chrome/Edge PWA kimliği AUMID'de "_crx_&lt;id&gt;", kısayolda
/// "--app-id=&lt;id&gt;" olarak geçer.
/// </summary>
public static class AppGrouping
{
    /// <summary>AUMID ya da kısayol argümanlarından Chromium uygulama kimliği; yoksa null.</summary>
    public static string? CrxId(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        foreach (var marker in new[] { "_crx_", "--app-id=" })
        {
            int i = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (i < 0) continue;
            int s = i + marker.Length, e = s;
            while (e < text.Length && char.IsLetterOrDigit(text[e])) e++;
            if (e - s >= 16) return text[s..e].ToLowerInvariant();
        }
        return null;
    }

    /// <summary>Penceresinin dock anahtarı: PWA → "crx:id", paketli uygulama → "aumid:…", diğerleri exe yolu.</summary>
    public static string WindowKey(string exePath, string? windowAumid, bool packaged)
    {
        if (CrxId(windowAumid) is { } crx) return "crx:" + crx;
        if (packaged && !string.IsNullOrEmpty(windowAumid)) return "aumid:" + windowAumid.ToLowerInvariant();
        return exePath.ToLowerInvariant();
    }

    /// <summary>Sabitlenmiş kısayolun anahtarı: PWA kısayolu → "crx:id", diğerleri hedef exe (yoksa kısayolun kendisi).</summary>
    public static string PinnedKey(string lnkPath, string? target, string? arguments, string? linkAumid) =>
        (CrxId(arguments) ?? CrxId(linkAumid)) is { } crx ? "crx:" + crx : (target ?? lnkPath).ToLowerInvariant();

    // ── 1.6.7: Chrome pencere AUMID'sinde uygulama kimliğini kısaltır ──
    // Kasa'da: kısayol --app-id=hnpfjngllnobngcgfapefoaidbinmjnm, pencere AUMID'i Chrome._crx_hnpfjngllnfapefoaidbinmjnm
    // (ortadan 6 karakter atılmış). Anahtarlar tutmadığı için WhatsApp penceresi sabitlenmiş simgeye bağlanmıyor, simgeye
    // basmak yeni başlatma sayılıyordu. Kısaltılmış kimlik tam kimliğin başı + sonu olarak eşleşir.

    /// <summary>İki Chromium uygulama kimliği aynı uygulama mı (biri ortadan kısaltılmış olabilir).</summary>
    public static bool SameCrx(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        a = a.ToLowerInvariant(); b = b.ToLowerInvariant();
        if (a == b) return true;
        var (shortId, longId) = a.Length < b.Length ? (a, b) : (b, a);
        if (shortId.Length < 16 || shortId.Length == longId.Length) return false;
        for (int k = 4; k <= shortId.Length - 4; k++)
            if (longId.StartsWith(shortId[..k], StringComparison.Ordinal) && longId.EndsWith(shortId[k..], StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>Dock anahtarları aynı uygulamayı mı gösterir (büyük/küçük harf, kısaltılmış Chrome kimliği).</summary>
    public static bool SameKey(string? a, string? b)
    {
        if (a == null || b == null) return false;
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
        return a.StartsWith("crx:", StringComparison.OrdinalIgnoreCase) && b.StartsWith("crx:", StringComparison.OrdinalIgnoreCase)
            && SameCrx(a[4..], b[4..]);
    }

    /// <summary>
    /// Pencerenin bağlanacağı sabitlenmiş simge: önce Chrome uygulama kimliği (kısaltılmış dahil), sonra Chromium penceresinin
    /// başlığı bir PWA kısayolunun adıyla birebir aynıysa o kısayol, sonra exe/AUMID anahtarı. Eşleşme yoksa pencerenin kendi anahtarı.
    /// </summary>
    public static string Resolve(string windowKey, string? title, bool chromium, IEnumerable<(string Key, string Name)> pinned)
    {
        var list = pinned as IList<(string Key, string Name)> ?? pinned.ToList();
        if (windowKey.StartsWith("crx:", StringComparison.Ordinal))
            foreach (var p in list) if (SameKey(p.Key, windowKey)) return p.Key;
        // Tarayıcı penceresinin başlığı "… - Google Chrome" olur; kısayol adıyla birebir aynı başlık yalnızca uygulama penceresidir
        if (chromium && !string.IsNullOrWhiteSpace(title))
            foreach (var p in list)
                if (p.Key.StartsWith("crx:", StringComparison.Ordinal) && string.Equals(p.Name, title.Trim(), StringComparison.OrdinalIgnoreCase))
                    return p.Key;
        foreach (var p in list) if (SameKey(p.Key, windowKey)) return p.Key;
        return windowKey;
    }
}

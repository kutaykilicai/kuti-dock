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
}

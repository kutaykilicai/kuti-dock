namespace KutiDock.Core;

/// <summary>Kutay Dock → Kuti Dock: eski %APPDATA%\KutayDock dosyalarını yeni klasöre kopyalar (eskiler silinmez).</summary>
public static class DataMigration
{
    /// <summary>Taşınan dosyalar: ayarlar, Cursor oturumu (DPAPI ile şifreli, olduğu gibi kopyalanır), görev çubuğu işareti.</summary>
    public static readonly string[] Files = { "ayarlar.json", "cursor-oturum.bin", "gorev-cubugu.json" };

    /// <summary>Yeni klasörde olmayan dosyaları kopyalar; kopyalanan dosya adlarını döner (içerik asla okunmaz/yazdırılmaz).</summary>
    public static List<string> Run(string oldDir, string newDir)
    {
        var copied = new List<string>();
        if (!Directory.Exists(oldDir)) return copied;
        Directory.CreateDirectory(newDir);
        foreach (var f in Files)
        {
            var src = Path.Combine(oldDir, f);
            var dst = Path.Combine(newDir, f);
            if (!File.Exists(src) || File.Exists(dst)) continue;
            try { File.Copy(src, dst); copied.Add(f); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        return copied;
    }
}

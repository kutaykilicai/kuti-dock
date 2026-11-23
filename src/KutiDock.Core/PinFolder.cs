namespace KutiDock.Core;

/// <summary>
/// 1.6.24: kullanıcının kendi sabitlemesi. Klasik dock'un sabitlenmiş uygulamaları zaten Windows görev çubuğunun
/// "User Pinned\TaskBar" klasöründeki kısayollardır ve oluşturulma zamanına göre sıralanır. Sabitleme, kaldırma ve
/// sıralama aynı biçimde bu klasörde yapılır: kısayol eklenir, kaldırılan kısayol silinmez, yedek klasörüne taşınır,
/// sıra oluşturulma zamanları yeniden yazılarak değişir.
/// </summary>
public sealed class PinFolder
{
    public static readonly string[] Patterns = ["*.lnk", "*.url"];

    readonly string _dir, _removedDir;
    readonly Func<string, DateTime> _getTime;
    readonly Action<string, DateTime> _setTime;

    public PinFolder(string dir, string removedDir, Func<string, DateTime>? getTime = null, Action<string, DateTime>? setTime = null)
    {
        _dir = dir; _removedDir = removedDir;
        _getTime = getTime ?? File.GetCreationTimeUtc;
        _setTime = setTime ?? File.SetCreationTimeUtc;
    }

    public string Dir => _dir;

    /// <summary>Sabitlenmiş kısayollar dock'taki sırasıyla (oluşturulma zamanı, eşitse ad).</summary>
    public List<string> Ordered()
    {
        if (!Directory.Exists(_dir)) return [];
        return Patterns.SelectMany(p => Directory.GetFiles(_dir, p))
            .OrderBy(f => _getTime(f)).ThenBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Dock'a bırakılabilen ya da sabitlenebilen yol: .exe, .lnk, .url ya da klasör.</summary>
    public static bool CanPin(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        if (Directory.Exists(path)) return true;
        if (!File.Exists(path)) return false;
        var ext = Path.GetExtension(path);
        return ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) || ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".url", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Bu klasörde kullanılmayan dosya adı: "Ad.lnk", doluysa "Ad (2).lnk" …</summary>
    public string FreePath(string name, string ext)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        name = name.Trim();
        if (name.Length == 0) name = "Uygulama";
        var path = Path.Combine(_dir, name + ext);
        for (int i = 2; File.Exists(path); i++) path = Path.Combine(_dir, $"{name} ({i}){ext}");
        return path;
    }

    /// <summary>Var olan bir .lnk ya da .url dosyasının kopyasını <paramref name="index"/>. sıraya sabitler.</summary>
    public string AddCopy(string source, int index)
    {
        Directory.CreateDirectory(_dir);
        var path = FreePath(Path.GetFileNameWithoutExtension(source), Path.GetExtension(source).ToLowerInvariant());
        File.Copy(source, path);
        Place(path, index);
        return path;
    }

    /// <summary>Yeni kısayolu <paramref name="write"/> ile yazar (yol verilir) ve <paramref name="index"/>. sıraya sabitler.</summary>
    public string? AddNew(string name, Action<string> write, int index)
    {
        Directory.CreateDirectory(_dir);
        var path = FreePath(name, ".lnk");
        write(path);
        if (!File.Exists(path)) return null;
        Place(path, index);
        return path;
    }

    /// <summary>Sabitlemeyi kaldırır: kısayol silinmez, yedek klasörüne taşınır (geri almak için).</summary>
    public string? Remove(string path)
    {
        if (!File.Exists(path)) return null;
        Directory.CreateDirectory(_removedDir);
        var name = Path.GetFileNameWithoutExtension(path);
        var dest = Path.Combine(_removedDir, $"{name} {DateTime.Now:yyyyMMdd-HHmmss}{Path.GetExtension(path)}");
        for (int i = 2; File.Exists(dest); i++) dest = Path.Combine(_removedDir, $"{name} {DateTime.Now:yyyyMMdd-HHmmss} ({i}){Path.GetExtension(path)}");
        File.Move(path, dest);
        return dest;
    }

    /// <summary>Sabitlenmiş kısayolu <paramref name="index"/>. sıraya taşır (dock içinde sürükleyerek sıralama).</summary>
    public void Move(string path, int index) => Place(path, index);

    void Place(string path, int index)
    {
        var order = Ordered();
        order.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        order.Insert(Math.Clamp(index, 0, order.Count), path);
        var times = Times(order.Select(_getTime).ToList());
        for (int i = 0; i < order.Count; i++)
            if (_getTime(order[i]) != times[i]) _setTime(order[i], times[i]);
    }

    /// <summary>
    /// Yeni sıra için oluşturulma zamanları: sıra zaten artan ise dokunulmaz; değilse en eski zamandan başlayarak
    /// birer saniye arayla. Böylece dock sırası Windows'un kendi sabitleme biçimiyle aynı kalır.
    /// </summary>
    public static List<DateTime> Times(IReadOnlyList<DateTime> current)
    {
        bool increasing = true;
        for (int i = 1; i < current.Count; i++) if (current[i] <= current[i - 1]) { increasing = false; break; }
        if (increasing) return current.ToList();
        var start = current.Count == 0 ? DateTime.UtcNow : current.Min();
        return Enumerable.Range(0, current.Count).Select(i => start.AddSeconds(i)).ToList();
    }

    /// <summary>
    /// Bırakma ya da sürükleme sırası: imlecin dock ekseni boyunca konumu, sabitlenmiş simgelerin (başlangıç, boyut)
    /// listesine göre kaçıncı aralığa düştüğü. Simgenin ortasını geçince bir sonraki aralık.
    /// </summary>
    public static int InsertIndex(IReadOnlyList<(double Start, double Size)> slots, double pos)
    {
        for (int i = 0; i < slots.Count; i++) if (pos < slots[i].Start + slots[i].Size / 2) return i;
        return slots.Count;
    }

    /// <summary>Simge <paramref name="from"/>. yerden alınıp <paramref name="gap"/>. aralığa bırakılınca yeni sırası.</summary>
    public static int MoveTarget(int from, int gap) => gap > from ? gap - 1 : gap;

    /// <summary>
    /// Çalışan uygulamanın anahtarına uyan ilk kısayol (Başlat menüsü, masaüstü). Simge, argümanlar ve AUMID doğru gelsin diye
    /// yeni kısayol yazmak yerine bu kopyalanır. <paramref name="keyOf"/> kısayolun dock anahtarı (çözülemezse null).
    /// </summary>
    public static string? FindShortcut(string key, IEnumerable<string> shortcuts, Func<string, string?> keyOf)
    {
        foreach (var lnk in shortcuts)
        {
            string? k;
            try { k = keyOf(lnk); } catch (Exception) { continue; }
            if (AppGrouping.SameKey(k, key)) return lnk;
        }
        return null;
    }

    /// <summary>Klasördeki (alt klasörler dahil) .lnk dosyaları; erişilemeyen klasörler atlanır.</summary>
    public static IEnumerable<string> Shortcuts(IEnumerable<string> roots)
    {
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var root in roots)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(root, "*.lnk", opts).ToList(); } catch (Exception) { continue; }
            foreach (var f in files) yield return f;
        }
    }
}

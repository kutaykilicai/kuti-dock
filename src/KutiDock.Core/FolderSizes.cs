namespace KutiDock.Core;

/// <summary>Bir klasörün doğrudan alt öğesi ve toplam boyutu (TreeSize benzeri görünüm için).</summary>
public sealed record FolderEntry(string Name, string FullPath, long Bytes, bool IsFolder, int Files);

public static class FolderSizes
{
    /// <summary>
    /// Everything'in döndürdüğü dosyaları (klasör yolu + boyut) <paramref name="root"/>'un doğrudan alt klasörlerine
    /// göre toplar; kökteki dosyalar kendi adıyla ayrı satır olur. Büyükten küçüğe, en çok <paramref name="top"/> satır.
    /// </summary>
    public static List<FolderEntry> Aggregate(string root, IEnumerable<(string Folder, string Name, long Size)> files, int top = 30)
    {
        var r = root.TrimEnd('\\') + "\\";
        var map = new Dictionary<string, (long bytes, int files, bool folder)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (folder, name, size) in files)
        {
            if (size <= 0) continue;
            var f = folder.TrimEnd('\\') + "\\";
            string key; bool isFolder;
            if (f.Equals(r, StringComparison.OrdinalIgnoreCase)) { key = name; isFolder = false; }
            else if (f.StartsWith(r, StringComparison.OrdinalIgnoreCase)) { var rest = f[r.Length..]; key = rest[..rest.IndexOf('\\')]; isFolder = true; }
            else continue;
            map.TryGetValue(key, out var v);
            map[key] = (v.bytes + size, v.files + 1, isFolder);
        }
        return map.Select(kv => new FolderEntry(kv.Key, r + kv.Key, kv.Value.bytes, kv.Value.folder, kv.Value.files))
            .OrderByDescending(e => e.Bytes).Take(top).ToList();
    }

    /// <summary>Everything sorgusu: kökün altındaki dosyalar, verilen eşikten büyük olanlar (tam tarama yok).</summary>
    public static string Query(string root, long minBytes) =>
        $"file: \"{root.TrimEnd('\\')}\\\"" + (minBytes > 0 ? $" size:>{minBytes}" : "");

    /// <summary>Bir üst klasör; sürücü kökündeyse null.</summary>
    public static string? Parent(string path)
    {
        var p = path.TrimEnd('\\');
        int i = p.LastIndexOf('\\');
        return i < 0 ? null : (i == 2 && p[1] == ':' ? p[..3] : p[..i]);
    }
}
